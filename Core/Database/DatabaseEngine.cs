using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;
using StreamMesh.Core.Utils;
using StreamMesh.Core.Media;

namespace StreamMesh.Core.Database
{
    public class DatabaseEngine
    {
        private readonly string _dbPath;
        private string ConnectionString => $"Data Source={_dbPath};Default Timeout=10;Pooling=True;";

        private static readonly System.Threading.SemaphoreSlim AsyncDbLock = new System.Threading.SemaphoreSlim(1, 1);
        private static readonly object _cacheLock = new object();
        public static bool SuppressEvents { get; set; } = false;

        private static bool _cleanupTriggered = false;

        public DatabaseEngine()
        {
            // V1.8.5: Database is now kept in the application folder for better reliability and speed.
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database_v2.db");
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            var directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                using (var pragmaCmd = connection.CreateCommand())
                {
                    pragmaCmd.CommandText = @"
                        PRAGMA journal_mode = WAL;
                        PRAGMA synchronous = NORMAL;
                        PRAGMA busy_timeout = 5000;
                        PRAGMA cache_size = -32000;
                        PRAGMA mmap_size = 268435456;
                        PRAGMA temp_store = MEMORY;
                    ";
                    pragmaCmd.ExecuteNonQuery();
                }

                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AppSettings (
                        Key TEXT PRIMARY KEY,
                        Value TEXT
                    );
                    CREATE TABLE IF NOT EXISTS Channels (
                        Id TEXT PRIMARY KEY,
                        Name TEXT,
                        EpgId TEXT DEFAULT '',
                        EpgUrl TEXT DEFAULT '',
                        Url TEXT,
                        GroupTitle TEXT,
                        LogoUrl TEXT,
                        SourceType TEXT,
                        AddedDate INTEGER,
                        Category TEXT,
                        Language TEXT,
                        PlaylistUrl TEXT,
                        IsFavorite INTEGER DEFAULT 0,
                        IsVerified INTEGER DEFAULT 0,
                        PersonalWatchCount INTEGER DEFAULT 0,
                        IsLocked INTEGER DEFAULT 0,
                        Notes TEXT DEFAULT '',
                        IsPremium INTEGER DEFAULT 0,
                        IsWatched INTEGER DEFAULT 0,
                        ImdbId TEXT DEFAULT '',
                        Overview TEXT DEFAULT '',
                        BackdropUrl TEXT DEFAULT '',
                        [Cast] TEXT DEFAULT '',
                        ViewersCount INTEGER DEFAULT 0,
                        UrlSpeeds TEXT DEFAULT '',
                        PreferredNameIndex INTEGER DEFAULT 0,
                        PreferredLogoIndex INTEGER DEFAULT 0,
                        PreferredEpgIndex INTEGER DEFAULT 0
                    );
                    CREATE TABLE IF NOT EXISTS EpgPrograms (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ChannelName TEXT,
                        Title TEXT,
                        Description TEXT,
                        StartTime TEXT,
                        EndTime TEXT,
                        SourceUrl TEXT
                    );
                    CREATE TABLE IF NOT EXISTS EpgChannels (
                        EpgId TEXT,
                        Name TEXT,
                        LogoUrl TEXT,
                        SourceUrl TEXT,
                        PRIMARY KEY (EpgId, SourceUrl)
                    );
                    CREATE TABLE IF NOT EXISTS M3uSources (
                        Url TEXT PRIMARY KEY,
                        ForcedLanguage TEXT DEFAULT 'und',
                        ForcedCategory TEXT DEFAULT 'TV',
                        AddedDate INTEGER
                    );
                    CREATE TABLE IF NOT EXISTS EpgSources (
                        Url TEXT PRIMARY KEY,
                        AddedDate INTEGER
                    );
                    CREATE TABLE IF NOT EXISTS IptvAccounts (
                        Id TEXT PRIMARY KEY,
                        Name TEXT,
                        ServerUrl TEXT,
                        Username TEXT,
                        Password TEXT,
                        Status TEXT,
                        ExpiryDate TEXT
                    );
                    CREATE TABLE IF NOT EXISTS LogoIndex (
                        Key TEXT PRIMARY KEY,
                        FileName TEXT
                    );
                    CREATE TABLE IF NOT EXISTS MetadataPool (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SearchQuery TEXT,
                        ImdbId TEXT DEFAULT '',
                        Title TEXT,
                        PosterUrl TEXT DEFAULT '',
                        BackdropUrl TEXT DEFAULT '',
                        Overview TEXT DEFAULT '',
                        [Cast] TEXT DEFAULT '',
                        Director TEXT DEFAULT '',
                        TrailerUrl TEXT DEFAULT '',
                        ReleaseDate TEXT DEFAULT '',
                        VoteAverage REAL DEFAULT 0.0,
                        CreatedAt TEXT,
                        MediaType TEXT DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_channels_playlisturl ON Channels (PlaylistUrl);
                    CREATE INDEX IF NOT EXISTS idx_channels_category ON Channels (Category);
                    CREATE INDEX IF NOT EXISTS idx_channels_name ON Channels (Name);
                    CREATE INDEX IF NOT EXISTS idx_channels_favorite ON Channels (IsFavorite);
                    CREATE INDEX IF NOT EXISTS idx_epg_channel_time ON EpgPrograms (ChannelName, StartTime, EndTime);
                    CREATE INDEX IF NOT EXISTS idx_epg_channel_name ON EpgPrograms (ChannelName);
                    CREATE INDEX IF NOT EXISTS idx_epg_time ON EpgPrograms (StartTime, EndTime);
                    CREATE INDEX IF NOT EXISTS idx_epgchannels_name ON EpgChannels (Name);
                    CREATE INDEX IF NOT EXISTS idx_channels_pwc_date ON Channels (PersonalWatchCount, AddedDate);
                    CREATE INDEX IF NOT EXISTS idx_epg_source ON EpgPrograms (SourceUrl);
                    CREATE INDEX IF NOT EXISTS idx_epgchannels_source ON EpgChannels (SourceUrl);
                ";
                command.ExecuteNonQuery();

                // Column Migrations
                string[] newCols = {
                    "ALTER TABLE Channels ADD COLUMN ViewersCount INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN PersonalWatchCount INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN IsPremium INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN IsWatched INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN ImdbId TEXT DEFAULT ''",
                    "ALTER TABLE Channels ADD COLUMN Overview TEXT DEFAULT ''",
                    "ALTER TABLE Channels ADD COLUMN BackdropUrl TEXT DEFAULT ''",
                    "ALTER TABLE Channels ADD COLUMN [Cast] TEXT DEFAULT ''",
                    "ALTER TABLE Channels ADD COLUMN UrlSpeeds TEXT DEFAULT ''",
                    "ALTER TABLE Channels ADD COLUMN PreferredNameIndex INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN PreferredLogoIndex INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN PreferredEpgIndex INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN IsEpgLocked INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN LastPositionMs INTEGER DEFAULT 0",
                    "ALTER TABLE EpgPrograms ADD COLUMN SourceUrl TEXT DEFAULT ''"
                };

                foreach (var sql in newCols)
                {
                    try
                    {
                        var cmdAlter = connection.CreateCommand();
                        cmdAlter.CommandText = sql;
                        cmdAlter.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
                        {
                            LogService.LogWarning($"Database: Column addition warning (likely already exists): {sql}");
                        }
                    }
                }

                if (GetSetting("MigrationV2Done", "false") != "true")
                {
                    Task.Run(async () => await EnsureDataMigrationAsync());
                }

                // V1.8.8: Perform a background cleanup of duplicates on startup (only once)
                if (!_cleanupTriggered)
                {
                    _cleanupTriggered = true;
                    Task.Run(async () => {
                        await Task.Delay(10000); // Wait longer for app to fully load
                        await CleanupDuplicatesAsync();
                    });
                }
            }
        }

        private async Task EnsureDataMigrationAsync()
        {
            try
            {
                string oldDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "database.db");
                if (!File.Exists(oldDbPath)) { SetSetting("MigrationV2Done", "true"); return; }

                var oldChannels = new List<Channel>();
                using (var oldConn = new SqliteConnection($"Data Source={oldDbPath}"))
                {
                    await oldConn.OpenAsync();
                    var cmd = oldConn.CreateCommand();
                    cmd.CommandText = "SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite FROM Channels";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        oldChannels.Add(new Channel {
                            Id = reader.GetString(0), Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Url = reader.IsDBNull(2) ? "" : reader.GetString(2), GroupTitle = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            LogoUrl = reader.IsDBNull(4) ? "" : reader.GetString(4), SourceType = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Category = reader.IsDBNull(6) ? "TV" : reader.GetString(6), Language = reader.IsDBNull(7) ? "und" : reader.GetString(7),
                            PlaylistUrl = reader.IsDBNull(8) ? "" : reader.GetString(8), IsFavorite = !reader.IsDBNull(9) && reader.GetInt32(9) == 1
                        });
                    }
                }
                if (oldChannels.Count > 0) await SyncIncomingChannelsAsync(oldChannels);
                SetSetting("MigrationV2Done", "true");
            }
            catch { }
        }

        public string GetSetting(string key, string defaultValue = "")
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key=@k";
                cmd.Parameters.AddWithValue("@k", key);
                var res = cmd.ExecuteScalar();
                return res != null ? res.ToString() ?? defaultValue : defaultValue;
            }
        }

        public void SetSetting(string key, string value)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES (@k, @v) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
                cmd.Parameters.AddWithValue("@k", key); cmd.Parameters.AddWithValue("@v", value);
                cmd.ExecuteNonQuery();
            }
        }

        public async Task<List<Channel>> GetSeriesEpisodesAsync(string seriesBaseName)
        {
            var list = new List<Channel>();
            if (string.IsNullOrEmpty(seriesBaseName)) return list;

            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredLogoIndex, PreferredEpgIndex, IsWatched, IsVerified, LastPositionMs, IsEpgLocked FROM Channels WHERE Category='Dizi' AND (Name LIKE @q OR Name LIKE @q2)";
                    cmd.Parameters.AddWithValue("@q", seriesBaseName + "%");
                    cmd.Parameters.AddWithValue("@q2", "%" + seriesBaseName + "%");

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var ch = MapReaderToChannel(reader);
                        if (ch.SeriesBaseName == seriesBaseName) // Strict check after loose SQL like
                        {
                            list.Add(ch);
                        }
                    }
                }
            }
            catch { }
            return list.OrderBy(c => c.SeasonNumber).ThenBy(c => c.EpisodeNumber).ToList();
        }

        private Channel MapReaderToChannel(SqliteDataReader reader)
        {
            var ch = new Channel
            {
                Id = reader.IsDBNull(0) ? Guid.NewGuid().ToString() : reader.GetString(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Url = reader.IsDBNull(2) ? "" : reader.GetString(2),
                LogoUrl = reader.IsDBNull(3) ? "" : reader.GetString(3),
                GroupTitle = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Category = reader.IsDBNull(5) ? "TV" : reader.GetString(5),
                Language = reader.IsDBNull(6) ? "und" : reader.GetString(6),
                IsFavorite = !reader.IsDBNull(7) && reader.GetInt32(7) == 1,
                SourceType = reader.IsDBNull(9) ? "M3U" : reader.GetString(9),
                PlaylistUrl = reader.IsDBNull(10) ? "" : reader.GetString(10),
                ImdbId = reader.IsDBNull(11) ? "" : reader.GetString(11),
                Overview = reader.IsDBNull(12) ? "" : reader.GetString(12),
                BackdropUrl = reader.IsDBNull(13) ? "" : reader.GetString(13),
                Cast = reader.IsDBNull(14) ? "" : reader.GetString(14),
                PersonalWatchCount = reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
                ViewersCount = reader.IsDBNull(16) ? 0 : reader.GetInt32(16),
                EpgId = reader.IsDBNull(17) ? "" : reader.GetString(17),
                EpgUrl = reader.IsDBNull(18) ? "" : reader.GetString(18),
                UrlSpeeds = reader.IsDBNull(19) ? "" : reader.GetString(19),
                PreferredNameIndex = reader.IsDBNull(20) ? 0 : reader.GetInt32(20),
                PreferredLogoIndex = reader.IsDBNull(21) ? 0 : reader.GetInt32(21),
                PreferredEpgIndex = reader.IsDBNull(22) ? 0 : reader.GetInt32(22),
                IsWatched = !reader.IsDBNull(23) && reader.GetInt32(23) == 1,
                IsVerified = !reader.IsDBNull(24) && reader.GetInt32(24) == 1,
                LastPositionMs = (reader.FieldCount > 25 && !reader.IsDBNull(25)) ? reader.GetInt64(25) : 0,
                IsEpgLocked = (reader.FieldCount > 26 && !reader.IsDBNull(26)) && reader.GetInt32(26) == 1
            };
            if (!reader.IsDBNull(8)) { try { ch.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(8)).DateTime; } catch { } }
            return ch;
        }

        public async Task<List<Channel>> GetAllChannelsAsync()
        {
            var list = new List<Channel>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredLogoIndex, PreferredEpgIndex, IsWatched, IsVerified, LastPositionMs, IsEpgLocked FROM Channels ORDER BY PersonalWatchCount DESC, AddedDate DESC";

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        try
                        {
                            var ch = MapReaderToChannel(reader);
                            list.Add(ch);
                        }
                        catch (Exception innerEx)
                        {
                            LogService.LogError("DatabaseEngine: Error mapping single channel row", innerEx);
                        }
                    }
                }
                LogService.LogInfo($"DatabaseEngine: GetAllChannelsAsync completed. Found {list.Count} items.");
            }
            catch (Exception ex)
            {
                LogService.LogError("DatabaseEngine.GetAllChannelsAsync failed (Check schema/LastPositionMs column)", ex);
            }
            return list;
        }

        public async Task SaveChannelAsync(Channel ch)
        {
            await AsyncDbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO Channels (Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredLogoIndex, PreferredEpgIndex, IsWatched, IsVerified, LastPositionMs, IsEpgLocked) VALUES (@Id, @Name, @Url, @Logo, @Group, @Cat, @Lang, @Fav, @Date, @Src, @Playlist, @Imdb, @Overview, @Backdrop, @Cast, @Pwc, @Vc, @EpgId, @EpgUrl, @Us, @Pni, @Pli, @Pei, @Watched, @Verified, @Lp, @EpgL) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Url=excluded.Url, LogoUrl=excluded.LogoUrl, GroupTitle=excluded.GroupTitle, Category=excluded.Category, Language=excluded.Language, IsFavorite=excluded.IsFavorite, ImdbId=excluded.ImdbId, Overview=excluded.Overview, BackdropUrl=excluded.BackdropUrl, [Cast]=excluded.Cast, PersonalWatchCount=excluded.PersonalWatchCount, ViewersCount=excluded.ViewersCount, EpgId=excluded.EpgId, EpgUrl=excluded.EpgUrl, UrlSpeeds=excluded.UrlSpeeds, PreferredNameIndex=excluded.PreferredNameIndex, PreferredLogoIndex=excluded.PreferredLogoIndex, PreferredEpgIndex=excluded.PreferredEpgIndex, IsWatched=excluded.IsWatched, IsVerified=excluded.IsVerified, LastPositionMs=excluded.LastPositionMs, IsEpgLocked=excluded.IsEpgLocked";
                    cmd.Parameters.AddWithValue("@Id", ch.Id); cmd.Parameters.AddWithValue("@Name", ch.Name); cmd.Parameters.AddWithValue("@Url", ch.Url);
                    cmd.Parameters.AddWithValue("@Logo", ch.LogoUrl); cmd.Parameters.AddWithValue("@Group", ch.GroupTitle); cmd.Parameters.AddWithValue("@Cat", ch.Category);
                    cmd.Parameters.AddWithValue("@Lang", ch.Language); cmd.Parameters.AddWithValue("@Fav", ch.IsFavorite ? 1 : 0);
                    cmd.Parameters.AddWithValue("@Date", new DateTimeOffset(ch.CreatedAt).ToUnixTimeSeconds()); cmd.Parameters.AddWithValue("@Src", ch.SourceType);
                    cmd.Parameters.AddWithValue("@Playlist", ch.PlaylistUrl); cmd.Parameters.AddWithValue("@Imdb", ch.ImdbId); cmd.Parameters.AddWithValue("@Overview", ch.Overview);
                    cmd.Parameters.AddWithValue("@Backdrop", ch.BackdropUrl); cmd.Parameters.AddWithValue("@Cast", ch.Cast);
                    cmd.Parameters.AddWithValue("@Pwc", ch.PersonalWatchCount); cmd.Parameters.AddWithValue("@Vc", ch.ViewersCount);
                    cmd.Parameters.AddWithValue("@EpgId", ch.EpgId ?? ""); cmd.Parameters.AddWithValue("@EpgUrl", ch.EpgUrl ?? "");
                    cmd.Parameters.AddWithValue("@Us", ch.UrlSpeeds ?? "");
                    cmd.Parameters.AddWithValue("@Pni", ch.PreferredNameIndex); cmd.Parameters.AddWithValue("@Pli", ch.PreferredLogoIndex); cmd.Parameters.AddWithValue("@Pei", ch.PreferredEpgIndex);
                    cmd.Parameters.AddWithValue("@Watched", ch.IsWatched ? 1 : 0);
                    cmd.Parameters.AddWithValue("@Verified", ch.IsVerified ? 1 : 0);
                    cmd.Parameters.AddWithValue("@Lp", ch.LastPositionMs);
                    cmd.Parameters.AddWithValue("@EpgL", ch.IsEpgLocked ? 1 : 0);
                    await cmd.ExecuteNonQueryAsync();
                }
                ClearChannelCache();
                NotifyDatabaseUpdated();
            }
            finally { AsyncDbLock.Release(); }
        }

        public async Task SaveChannelsBatchAsync(List<Channel> channels, bool clearFirst = false)
        {
            if (channels == null || channels.Count == 0)
            {
                if (clearFirst) ExecuteRawNonQuery("DELETE FROM Channels");
                return;
            }
            await AsyncDbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    using var tx = connection.BeginTransaction();

                    if (clearFirst)
                    {
                        var clearCmd = connection.CreateCommand();
                        clearCmd.Transaction = tx;
                        clearCmd.CommandText = "DELETE FROM Channels";
                        await clearCmd.ExecuteNonQueryAsync();
                    }

                    var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO Channels (Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredLogoIndex, PreferredEpgIndex, IsWatched, IsVerified, LastPositionMs, IsEpgLocked) VALUES (@Id, @Name, @Url, @Logo, @Group, @Cat, @Lang, @Fav, @Date, @Src, @Playlist, @Imdb, @Overview, @Backdrop, @Cast, @Pwc, @Vc, @EpgId, @EpgUrl, @Us, @Pni, @Pli, @Pei, @Watched, @Verified, @Lp, @EpgL) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Url=excluded.Url, LogoUrl=excluded.LogoUrl, GroupTitle=excluded.GroupTitle, Category=excluded.Category, Language=excluded.Language, IsFavorite=excluded.IsFavorite, ImdbId=excluded.ImdbId, Overview=excluded.Overview, BackdropUrl=excluded.BackdropUrl, [Cast]=excluded.Cast, PersonalWatchCount=excluded.PersonalWatchCount, ViewersCount=excluded.ViewersCount, EpgId=excluded.EpgId, EpgUrl=excluded.EpgUrl, UrlSpeeds=excluded.UrlSpeeds, PreferredNameIndex=excluded.PreferredNameIndex, PreferredLogoIndex=excluded.PreferredLogoIndex, PreferredEpgIndex=excluded.PreferredEpgIndex, IsWatched=excluded.IsWatched, IsVerified=excluded.IsVerified, LastPositionMs=excluded.LastPositionMs, IsEpgLocked=excluded.IsEpgLocked";

                    var pId = cmd.Parameters.Add("@Id", SqliteType.Text);
                    var pName = cmd.Parameters.Add("@Name", SqliteType.Text);
                    var pUrl = cmd.Parameters.Add("@Url", SqliteType.Text);
                    var pLogo = cmd.Parameters.Add("@Logo", SqliteType.Text);
                    var pGroup = cmd.Parameters.Add("@Group", SqliteType.Text);
                    var pCat = cmd.Parameters.Add("@Cat", SqliteType.Text);
                    var pLang = cmd.Parameters.Add("@Lang", SqliteType.Text);
                    var pFav = cmd.Parameters.Add("@Fav", SqliteType.Integer);
                    var pDate = cmd.Parameters.Add("@Date", SqliteType.Integer);
                    var pSrc = cmd.Parameters.Add("@Src", SqliteType.Text);
                    var pPlaylist = cmd.Parameters.Add("@Playlist", SqliteType.Text);
                    var pImdb = cmd.Parameters.Add("@Imdb", SqliteType.Text);
                    var pOverview = cmd.Parameters.Add("@Overview", SqliteType.Text);
                    var pBackdrop = cmd.Parameters.Add("@Backdrop", SqliteType.Text);
                    var pCast = cmd.Parameters.Add("@Cast", SqliteType.Text);
                    var pPwc = cmd.Parameters.Add("@Pwc", SqliteType.Integer);
                    var pVc = cmd.Parameters.Add("@Vc", SqliteType.Integer);
                    var pEpgId = cmd.Parameters.Add("@EpgId", SqliteType.Text);
                    var pEpgUrl = cmd.Parameters.Add("@EpgUrl", SqliteType.Text);
                    var pUs = cmd.Parameters.Add("@Us", SqliteType.Text);
                    var pPni = cmd.Parameters.Add("@Pni", SqliteType.Integer);
                    var pPli = cmd.Parameters.Add("@Pli", SqliteType.Integer);
                    var pPei = cmd.Parameters.Add("@Pei", SqliteType.Integer);
                    var pWatched = cmd.Parameters.Add("@Watched", SqliteType.Integer);
                    var pVerified = cmd.Parameters.Add("@Verified", SqliteType.Integer);
                    var pLp = cmd.Parameters.Add("@Lp", SqliteType.Integer);
                    var pEpgL = cmd.Parameters.Add("@EpgL", SqliteType.Integer);

                    foreach (var ch in channels)
                    {
                        pId.Value = ch.Id ?? Guid.NewGuid().ToString("N");
                        pName.Value = ch.Name ?? "";
                        pUrl.Value = ch.Url ?? "";
                        pLogo.Value = ch.LogoUrl ?? "";
                        pGroup.Value = ch.GroupTitle ?? "";
                        pCat.Value = ch.Category ?? "TV";
                        pLang.Value = ch.Language ?? "und";
                        pFav.Value = ch.IsFavorite ? 1 : 0;
                        pDate.Value = new DateTimeOffset(ch.CreatedAt).ToUnixTimeSeconds();
                        pSrc.Value = ch.SourceType ?? "M3U";
                        pPlaylist.Value = ch.PlaylistUrl ?? "";
                        pImdb.Value = ch.ImdbId ?? "";
                        pOverview.Value = ch.Overview ?? "";
                        pBackdrop.Value = ch.BackdropUrl ?? "";
                        pCast.Value = ch.Cast ?? "";
                        pPwc.Value = ch.PersonalWatchCount;
                        pVc.Value = ch.ViewersCount;
                        pEpgId.Value = ch.EpgId ?? "";
                        pEpgUrl.Value = ch.EpgUrl ?? "";
                        pUs.Value = ch.UrlSpeeds ?? "";
                        pPni.Value = ch.PreferredNameIndex;
                        pPli.Value = ch.PreferredLogoIndex;
                        pPei.Value = ch.PreferredEpgIndex;
                        pWatched.Value = ch.IsWatched ? 1 : 0;
                        pVerified.Value = ch.IsVerified ? 1 : 0;
                        pLp.Value = ch.LastPositionMs;
                        pEpgL.Value = ch.IsEpgLocked ? 1 : 0;

                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
                ClearChannelCache();
                NotifyDatabaseUpdated();
            }
            finally { AsyncDbLock.Release(); }
        }

        public async Task SyncIncomingChannelsAsync(List<Channel> incoming)
        {
            if (incoming == null || incoming.Count == 0) return;

            // V1.8.8: Aggressive merging before saving
            var urls = incoming.SelectMany(c => c.GetUrlList()).Distinct().ToList();
            var epgs = incoming.SelectMany(c => c.GetEpgIdList()).Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();

            var existing = new List<Channel>();
            if (urls.Count > 0 || epgs.Count > 0)
            {
                try
                {
                    using (var connection = new SqliteConnection(ConnectionString))
                    {
                        await connection.OpenAsync();

                        // Chunking to avoid SQLite limits and syntax errors with massive strings
                        if (urls.Count > 0)
                        {
                            for (int i = 0; i < urls.Count; i += 400)
                            {
                                var chunk = urls.Skip(i).Take(400).ToList();
                                var cmd = connection.CreateCommand();
                                var placeholders = new List<string>();
                                for (int j = 0; j < chunk.Count; j++)
                                {
                                    string pName = $"@u{j}";
                                    placeholders.Add(pName);
                                    cmd.Parameters.AddWithValue(pName, chunk[j]);
                                }
                                string inClause = string.Join(",", placeholders);
                                cmd.CommandText = $"SELECT Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredLogoIndex, PreferredEpgIndex, IsWatched, IsVerified, LastPositionMs, IsEpgLocked FROM Channels WHERE Url IN ({inClause})";
                                using var reader = await cmd.ExecuteReaderAsync();
                                while (await reader.ReadAsync()) existing.Add(MapReaderToChannel(reader));
                            }
                        }

                        if (epgs.Count > 0)
                        {
                            for (int i = 0; i < epgs.Count; i += 400)
                            {
                                var chunk = epgs.Skip(i).Take(400).ToList();
                                var cmd = connection.CreateCommand();
                                var placeholders = new List<string>();
                                for (int j = 0; j < chunk.Count; j++)
                                {
                                    string pName = $"@e{j}";
                                    placeholders.Add(pName);
                                    cmd.Parameters.AddWithValue(pName, chunk[j]);
                                }
                                string inClause = string.Join(",", placeholders);
                                cmd.CommandText = $"SELECT Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredLogoIndex, PreferredEpgIndex, IsWatched, IsVerified, LastPositionMs, IsEpgLocked FROM Channels WHERE EpgId IN ({inClause})";
                                using var reader = await cmd.ExecuteReaderAsync();
                                while (await reader.ReadAsync())
                                {
                                    var ch = MapReaderToChannel(reader);
                                    if (existing.All(x => x.Id != ch.Id)) existing.Add(ch);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("DatabaseEngine.SyncIncomingChannelsAsync: Metadata fetch failed", ex);
                }
            }

            var combined = incoming.Concat(existing).ToList();
            var aggregated = ChannelAggregator.Instance.AggregateChannels(combined);

            await SaveChannelsBatchAsync(aggregated);
        }

        public async Task<int> AutoAggregateDatabaseAsync()
        {
            var existing = await GetAllChannelsAsync();
            if (existing.Count <= 1) return 0;

            var aggregated = StreamMesh.Core.Media.ChannelAggregator.Instance.AggregateChannels(existing);
            int mergedCount = existing.Count - aggregated.Count;

            if (mergedCount > 0)
            {
                // V1.9.9: Safe aggregation - use a single transaction for both delete and insert
                await SaveChannelsBatchAsync(aggregated, true);
            }

            return mergedCount;
        }

        public async Task SaveEpgProgramsAsync(List<EpgProgram> programs)
        {
            if (programs == null || programs.Count == 0) return;
            await AsyncDbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    using var transaction = connection.BeginTransaction();
                    var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "INSERT INTO EpgPrograms (ChannelName, Title, Description, StartTime, EndTime, SourceUrl) VALUES (@ChannelName, @Title, @Description, @StartTime, @EndTime, @SourceUrl)";
                    var pName = cmd.Parameters.Add("@ChannelName", SqliteType.Text);
                    var pTitle = cmd.Parameters.Add("@Title", SqliteType.Text);
                    var pDesc = cmd.Parameters.Add("@Description", SqliteType.Text);
                    var pStart = cmd.Parameters.Add("@StartTime", SqliteType.Text);
                    var pEnd = cmd.Parameters.Add("@EndTime", SqliteType.Text);
                    var pSrc = cmd.Parameters.Add("@SourceUrl", SqliteType.Text);

                    foreach (var prog in programs)
                    {
                        pName.Value = prog.ChannelName ?? "";
                        pTitle.Value = prog.Title ?? "";
                        pDesc.Value = prog.Description ?? "";
                        pStart.Value = prog.StartTime.ToString("o");
                        pEnd.Value = prog.EndTime.ToString("o");
                        pSrc.Value = prog.SourceUrl ?? "";
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
            finally { AsyncDbLock.Release(); }
        }

        public async Task CleanupOldEpgProgramsAsync(int daysBack = 1)
        {
            await AsyncDbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    // Aggressive cleanup: delete everything older than now - limit
                    var cmd = connection.CreateCommand();
                    string limit = DateTime.Now.AddDays(-daysBack).ToString("o");
                    cmd.CommandText = "DELETE FROM EpgPrograms WHERE EndTime < @limit";
                    cmd.Parameters.AddWithValue("@limit", limit);
                    int count = await cmd.ExecuteNonQueryAsync();

                    // Optimization: Vacuum to reclaim space if significant amount of data deleted
                    if (count > 5000)
                    {
                        var vacuumCmd = connection.CreateCommand();
                        vacuumCmd.CommandText = "VACUUM";
                        await vacuumCmd.ExecuteNonQueryAsync();
                    }

                    LogService.LogInfo($"DatabaseEngine: {count} old EPG items cleaned and storage optimized.");
                }
            }
            catch (Exception ex) { LogService.LogError("DatabaseEngine.CleanupOldEpgProgramsAsync failed", ex); }
            finally { AsyncDbLock.Release(); }
        }

        public async Task ClearEpgSourceDataAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            await AsyncDbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM EpgPrograms WHERE SourceUrl = @url; DELETE FROM EpgChannels WHERE SourceUrl = @url;";
                    cmd.Parameters.AddWithValue("@url", url);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            finally { AsyncDbLock.Release(); }
        }

        public async Task SaveEpgChannelsBatchAsync(List<(string epgId, string name, string logo, string url)> channels)
        {
            if (channels == null || channels.Count == 0) return;
            await AsyncDbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    using var transaction = connection.BeginTransaction();
                    var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "INSERT OR REPLACE INTO EpgChannels (EpgId, Name, LogoUrl, SourceUrl) VALUES (@id, @name, @logo, @url)";
                    var pId = cmd.Parameters.Add("@id", SqliteType.Text);
                    var pName = cmd.Parameters.Add("@name", SqliteType.Text);
                    var pLogo = cmd.Parameters.Add("@logo", SqliteType.Text);
                    var pUrl = cmd.Parameters.Add("@url", SqliteType.Text);

                    foreach (var c in channels)
                    {
                        pId.Value = c.epgId ?? "";
                        pName.Value = c.name ?? "";
                        pLogo.Value = c.logo ?? "";
                        pUrl.Value = c.url ?? "";
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
            finally { AsyncDbLock.Release(); }
        }

        public async Task<List<EpgChannelSearchResult>> SearchEpgChannelsAsync(string query, bool allSources)
        {
            var list = new List<EpgChannelSearchResult>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    string rawQuery = (query ?? "").Trim();
                    string cleanQuery = StreamMesh.Core.Media.ChannelUtils.GetCleanName(rawQuery);

                    var epgSources = GetEpgSources();

                    // V1.9.5: Search EpgChannels (Definitions) instead of just Programs.
                    // Join with EpgPrograms to get the current/latest title if it exists.
                    var cmd = connection.CreateCommand();
                    string sql = @"
                        SELECT DISTINCT ec.EpgId, ec.Name, ec.SourceUrl,
                        (SELECT Title FROM EpgPrograms ep WHERE (ep.ChannelName = ec.EpgId OR ep.ChannelName LIKE ec.EpgId || ',%' OR ep.ChannelName LIKE '%, ' || ec.EpgId)
                         AND ep.StartTime <= @now AND ep.EndTime >= @now ORDER BY ep.StartTime DESC LIMIT 1) as LatestTitle
                        FROM EpgChannels ec
                        WHERE 1=1 ";

                    cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));

                    if (!string.IsNullOrWhiteSpace(rawQuery))
                    {
                        sql += " AND (ec.Name LIKE @q OR ec.EpgId LIKE @q OR ec.Name LIKE @cq) ";
                        cmd.Parameters.AddWithValue("@q", "%" + rawQuery + "%");
                        cmd.Parameters.AddWithValue("@cq", "%" + cleanQuery + "%");
                    }

                    if (!allSources && epgSources.Count > 0)
                    {
                        var placeholders = new List<string>();
                        for (int i = 0; i < epgSources.Count; i++)
                        {
                            string pName = $"@src{i}";
                            placeholders.Add(pName);
                            cmd.Parameters.AddWithValue(pName, epgSources[i]);
                        }
                        sql += $" AND ec.SourceUrl IN ({string.Join(",", placeholders)}) ";
                    }

                    sql += " ORDER BY ec.Name ASC LIMIT 100";
                    cmd.CommandText = sql;

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            list.Add(new EpgChannelSearchResult
                            {
                                EpgId = reader.GetString(0),
                                ChannelName = reader.GetString(1),
                                SourceUrl = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                CurrentProgram = reader.IsDBNull(3) ? "Yayın akışı bilgisi yok" : reader.GetString(3),
                                SourceName = allSources ? "Tüm EPG Kaynakları" : "Mevcut EPG Kaynağı"
                            });
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(rawQuery))
                    {
                        list = list.OrderByDescending(r => r.ChannelName.StartsWith(rawQuery, StringComparison.OrdinalIgnoreCase) || r.EpgId.StartsWith(rawQuery, StringComparison.OrdinalIgnoreCase))
                                   .ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                StreamMesh.Core.Utils.LogService.LogError($"[EpgSearch] Error searching EPG channels for query '{query}'", ex);
            }
            return list;
        }

        public async Task<List<EpgProgram>> GetCurrentEpgProgramsAsync()
        {
            var list = new List<EpgProgram>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                var now = DateTime.Now;
                var startWin = now.AddHours(-2).ToString("o");
                var endWin = now.AddHours(3).ToString("o");

            cmd.CommandText = "SELECT ChannelName, Title, Description, StartTime, EndTime, SourceUrl FROM EpgPrograms WHERE StartTime <= @EndWin AND EndTime >= @StartWin";
            cmd.Parameters.AddWithValue("@StartWin", startWin);
            cmd.Parameters.AddWithValue("@EndWin", endWin);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var ep = new EpgProgram { ChannelName = reader.GetString(0), Title = reader.GetString(1), Description = reader.GetString(2), SourceUrl = reader.IsDBNull(5) ? "" : reader.GetString(5) };
                if (DateTime.TryParse(reader.GetString(3), out DateTime st)) ep.StartTime = st;
                if (DateTime.TryParse(reader.GetString(4), out DateTime et)) ep.EndTime = et;
                list.Add(ep);
            }
            }
            return list;
        }

        public async Task<List<EpgProgram>> GetEpgForChannelsAsync(List<string> channelNames)
        {
            var list = new List<EpgProgram>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();

                // V1.9.9: Use LIKE for multiple names or split search
                var conditions = new List<string>();
                for (int i = 0; i < channelNames.Count; i++)
                {
                    conditions.Add($"(ChannelName = @n{i} OR ChannelName LIKE @n{i} || ',%' OR ChannelName LIKE '%, ' || @n{i})");
                    cmd.Parameters.AddWithValue($"@n{i}", channelNames[i]);
                }

                if (conditions.Count == 0) return list;

                cmd.CommandText = $"SELECT ChannelName, Title, Description, StartTime, EndTime, SourceUrl FROM EpgPrograms WHERE {string.Join(" OR ", conditions)}";
                using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var ep = new EpgProgram { ChannelName = reader.GetString(0), Title = reader.GetString(1), Description = reader.GetString(2), SourceUrl = reader.IsDBNull(5) ? "" : reader.GetString(5) };
                if (DateTime.TryParse(reader.GetString(3), out DateTime st)) ep.StartTime = st;
                if (DateTime.TryParse(reader.GetString(4), out DateTime et)) ep.EndTime = et;
                list.Add(ep);
            }
            }
            return list;
        }

        public async Task<List<MetadataResult>> GetMetadataPoolForQueryAsync(string query)
        {
            var list = new List<MetadataResult>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT ImdbId, Title, PosterUrl, BackdropUrl, Overview, [Cast], Director, TrailerUrl, ReleaseDate, VoteAverage, MediaType FROM MetadataPool WHERE SearchQuery = @q";
                cmd.Parameters.AddWithValue("@q", query.ToLowerInvariant());
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new MetadataResult {
                        ImdbId = reader.GetString(0), Title = reader.GetString(1), PosterUrl = reader.GetString(2),
                        BackdropUrl = reader.GetString(3), Overview = reader.GetString(4), Cast = reader.GetString(5),
                        Director = reader.IsDBNull(6) ? "" : reader.GetString(6), TrailerUrl = reader.IsDBNull(7) ? "" : reader.GetString(7),
                        ReleaseDate = reader.IsDBNull(8) ? "" : reader.GetString(8), VoteAverage = reader.GetDouble(9),
                        MediaType = reader.IsDBNull(10) ? "" : reader.GetString(10)
                    });
                }
            }
            return list;
        }

        public async Task SaveMetadataPoolResultsAsync(string query, List<MetadataResult> results)
        {
            if (results == null || results.Count == 0) return;
            await AsyncDbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    using var transaction = connection.BeginTransaction();
                    foreach (var item in results)
                    {
                        var cmd = connection.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText = "INSERT INTO MetadataPool (SearchQuery, ImdbId, Title, PosterUrl, BackdropUrl, Overview, [Cast], Director, TrailerUrl, ReleaseDate, VoteAverage, CreatedAt, MediaType) VALUES (@q, @imdb, @t, @p, @b, @o, @c, @d, @tr, @rd, @v, @ca, @mt)";
                        cmd.Parameters.AddWithValue("@q", query.ToLowerInvariant()); cmd.Parameters.AddWithValue("@imdb", item.ImdbId ?? "");
                        cmd.Parameters.AddWithValue("@t", item.Title ?? ""); cmd.Parameters.AddWithValue("@p", item.PosterUrl ?? "");
                        cmd.Parameters.AddWithValue("@b", item.BackdropUrl ?? ""); cmd.Parameters.AddWithValue("@o", item.Overview ?? "");
                        cmd.Parameters.AddWithValue("@c", item.Cast ?? ""); cmd.Parameters.AddWithValue("@d", item.Director ?? "");
                        cmd.Parameters.AddWithValue("@tr", item.TrailerUrl ?? ""); cmd.Parameters.AddWithValue("@rd", item.ReleaseDate ?? "");
                        cmd.Parameters.AddWithValue("@v", item.VoteAverage); cmd.Parameters.AddWithValue("@ca", DateTime.UtcNow.ToString("o"));
                        cmd.Parameters.AddWithValue("@mt", item.MediaType ?? "");
                        await cmd.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
            }
            finally { AsyncDbLock.Release(); }
        }

        public (int count, DateTime date) GetDailyQueryStats()
        {
            string dateStr = GetSetting("DailyQueryDate", DateTime.Today.ToString("o"));
            int count = int.Parse(GetSetting("DailyQueryCount", "0"));
            if (DateTime.Parse(dateStr).Date != DateTime.Today) { count = 0; SetSetting("DailyQueryDate", DateTime.Today.ToString("o")); SetSetting("DailyQueryCount", "0"); }
            return (count, DateTime.Today);
        }

        public void IncrementDailyQueryCount() { var stats = GetDailyQueryStats(); SetSetting("DailyQueryCount", (stats.count + 1).ToString()); }

        public async Task<int> GetTotalChannelCountAsync()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Channels";
                var res = await cmd.ExecuteScalarAsync();
                return res != null ? Convert.ToInt32(res) : 0;
            }
        }

        public List<string> GetM3uSources()
        {
            var list = new List<string>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Url FROM M3uSources ORDER BY AddedDate DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(reader.GetString(0));
            }
            return list;
        }

        public void AddM3uSource(string url)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO M3uSources (Url, AddedDate) VALUES (@Url, @Date)";
                cmd.Parameters.AddWithValue("@Url", url); cmd.Parameters.AddWithValue("@Date", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                cmd.ExecuteNonQuery();
            }
        }

        public void RemoveM3uSource(string url)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM M3uSources WHERE Url = @Url; DELETE FROM Channels WHERE PlaylistUrl = @Url;";
                cmd.Parameters.AddWithValue("@Url", url); cmd.ExecuteNonQuery();
            }
        }

        public List<string> GetEpgSources()
        {
            var list = new List<string>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Url FROM EpgSources ORDER BY AddedDate DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(reader.GetString(0));
            }
            return list;
        }

        public void AddEpgSource(string url)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO EpgSources (Url, AddedDate) VALUES (@Url, @Date)";
                cmd.Parameters.AddWithValue("@Url", url); cmd.Parameters.AddWithValue("@Date", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                cmd.ExecuteNonQuery();
            }
        }

        public void RemoveEpgSource(string url)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM EpgSources WHERE Url = @Url;";
                cmd.Parameters.AddWithValue("@Url", url); cmd.ExecuteNonQuery();
            }
        }

        // IPTV Account Management
        public List<IptvAccount> GetAllIptvAccounts()
        {
            var list = new List<IptvAccount>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, ServerUrl, Username, Password, Status, ExpiryDate FROM IptvAccounts";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new IptvAccount {
                        Id = reader.GetString(0), Name = reader.GetString(1), ServerUrl = reader.GetString(2),
                        Username = reader.GetString(3),
                        Password = Decrypt(reader.GetString(4)),
                        Status = reader.GetString(5),
                        ExpiryDate = DateTime.TryParse(reader.GetString(6), out DateTime dt) ? dt : DateTime.MinValue
                    });
                }
            }
            return list;
        }

        public void SaveIptvAccount(IptvAccount acc)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO IptvAccounts (Id, Name, ServerUrl, Username, Password, Status, ExpiryDate) VALUES (@Id, @N, @U, @Un, @P, @S, @E) ON CONFLICT(Id) DO UPDATE SET Name=@N, ServerUrl=@U, Username=@Un, Password=@P, Status=@S, ExpiryDate=@E";
                cmd.Parameters.AddWithValue("@Id", acc.Id); cmd.Parameters.AddWithValue("@N", acc.Name);
                cmd.Parameters.AddWithValue("@U", acc.ServerUrl); cmd.Parameters.AddWithValue("@Un", acc.Username);
                cmd.Parameters.AddWithValue("@P", Encrypt(acc.Password)); cmd.Parameters.AddWithValue("@S", acc.Status);
                cmd.Parameters.AddWithValue("@E", acc.ExpiryDate.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }

        private string Encrypt(string clearText)
        {
            if (string.IsNullOrEmpty(clearText)) return "";
            try
            {
                byte[] clearBytes = Encoding.Unicode.GetBytes(clearText);
                using (var encryptor = System.Security.Cryptography.Aes.Create())
                {
                    var pdb = new System.Security.Cryptography.Rfc2898DeriveBytes("StreamMesh_Safe_Pass_2024", new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 }, 1000, System.Security.Cryptography.HashAlgorithmName.SHA256);
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (var ms = new MemoryStream())
                    {
                        using (var cs = new System.Security.Cryptography.CryptoStream(ms, encryptor.CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.Write))
                        {
                            cs.Write(clearBytes, 0, clearBytes.Length);
                            cs.Close();
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch { return clearText; }
        }

        private string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            if (cipherText.Length < 8) return cipherText;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using (var encryptor = System.Security.Cryptography.Aes.Create())
                {
                    var pdb = new System.Security.Cryptography.Rfc2898DeriveBytes("StreamMesh_Safe_Pass_2024", new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 }, 1000, System.Security.Cryptography.HashAlgorithmName.SHA256);
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (var ms = new MemoryStream())
                    {
                        using (var cs = new System.Security.Cryptography.CryptoStream(ms, encryptor.CreateDecryptor(), System.Security.Cryptography.CryptoStreamMode.Write))
                        {
                            cs.Write(cipherBytes, 0, cipherBytes.Length);
                            cs.Close();
                        }
                        return Encoding.Unicode.GetString(ms.ToArray());
                    }
                }
            }
            catch { return cipherText; }
        }

        public void RemoveIptvAccount(string id)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM IptvAccounts WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Id", id); cmd.ExecuteNonQuery();
            }
        }

        public int GetChannelCountBySource(string url)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Channels WHERE PlaylistUrl = @Url";
                cmd.Parameters.AddWithValue("@Url", url);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public List<Channel> GetChannelsBySource(string url)
        {
            var list = new List<Channel>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, Url, LogoUrl, Category, Language FROM Channels WHERE PlaylistUrl = @Url";
                cmd.Parameters.AddWithValue("@Url", url);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new Channel {
                        Id = reader.GetString(0), Name = reader.GetString(1), Url = reader.GetString(2),
                        LogoUrl = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        Category = reader.IsDBNull(4) ? "TV" : reader.GetString(4),
                        Language = reader.IsDBNull(5) ? "und" : reader.GetString(5)
                    });
                }
            }
            return list;
        }

        public void UpdateLogoIndex(List<(string key, string file)> items)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using var trans = connection.BeginTransaction();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO LogoIndex (Key, FileName) VALUES (@k, @f)";
                var pk = cmd.Parameters.Add("@k", SqliteType.Text);
                var pf = cmd.Parameters.Add("@f", SqliteType.Text);
                foreach (var item in items) { pk.Value = item.key; pf.Value = item.file; cmd.ExecuteNonQuery(); }
                trans.Commit();
            }
        }

        public Dictionary<string, string> GetAllLogoIndex()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Key, FileName FROM LogoIndex";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string k = reader.GetString(0);
                        string f = reader.GetString(1);
                        if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(f))
                        {
                            dict[k] = f;
                        }
                    }
                }
            }
            catch { }
            return dict;
        }

        public string? FindLogoInIndex(string key)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT FileName FROM LogoIndex WHERE Key = @k";
                cmd.Parameters.AddWithValue("@k", key);
                return cmd.ExecuteScalar()?.ToString();
            }
        }

        public void ExecuteRawNonQuery(string sql)
        {
            using (var connection = new SqliteConnection(ConnectionString)) { connection.Open(); var cmd = connection.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            ClearChannelCache();
            NotifyDatabaseUpdated();
        }

        public static event EventHandler? OnDatabaseUpdated;

        public static void NotifyDatabaseUpdated()
        {
            if (SuppressEvents) return;
            OnDatabaseUpdated?.Invoke(null, EventArgs.Empty);
        }

        public async Task CleanupDuplicatesAsync()
        {
            LogService.LogInfo("DatabaseEngine: Global aggregation cleanup starting...");
            int merged = await AutoAggregateDatabaseAsync();
            LogService.LogInfo($"DatabaseEngine: Global aggregation cleanup completed. Merged {merged} channels into existing cards.");
        }

        public void ClearAllSources()
        {
            ExecuteRawNonQuery("DELETE FROM M3uSources");
            ExecuteRawNonQuery("DELETE FROM EpgSources");
            ClearChannelCache();
            NotifyDatabaseUpdated();
        }

        public void ClearAllContents()
        {
            ExecuteRawNonQuery("DELETE FROM Channels");
            ExecuteRawNonQuery("DELETE FROM EpgPrograms");
            ClearChannelCache();
            NotifyDatabaseUpdated();
        }

        public async Task DeleteChannelsAsync(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return;
            await AsyncDbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    using var tx = connection.BeginTransaction();

                    // Delete in chunks to avoid SQL parameter limit
                    for (int i = 0; i < ids.Count; i += 500)
                    {
                        var chunk = ids.Skip(i).Take(500).ToList();
                        var cmd = connection.CreateCommand();
                        cmd.Transaction = tx;

                        var placeholders = new List<string>();
                        for (int j = 0; j < chunk.Count; j++)
                        {
                            string pName = $"@id{j}";
                            placeholders.Add(pName);
                            cmd.Parameters.AddWithValue(pName, chunk[j]);
                        }

                        cmd.CommandText = $"DELETE FROM Channels WHERE Id IN ({string.Join(",", placeholders)})";
                        await cmd.ExecuteNonQueryAsync();
                    }
                    tx.Commit();
                }
                ClearChannelCache();
                NotifyDatabaseUpdated();
            }
            finally { AsyncDbLock.Release(); }
        }

        public void SaveChannelSync(Channel ch)
        {
            AsyncDbLock.Wait();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE Channels SET LastPositionMs = @Lp WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Lp", ch.LastPositionMs);
                    cmd.Parameters.AddWithValue("@Id", ch.Id);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
            finally { AsyncDbLock.Release(); }
        }

        public void ClearChannelCache() { /* Cache removed */ }
    }
}
