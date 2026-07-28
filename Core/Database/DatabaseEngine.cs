using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Database
{
    public class DatabaseEngine
    {
        private readonly string _dbPath;
        private string ConnectionString => $"Data Source={_dbPath};Default Timeout=10;Pooling=True;";

        private static readonly System.Threading.SemaphoreSlim AsyncDbLock = new System.Threading.SemaphoreSlim(1, 1);
        private static List<Channel>? _channelCache = null;
        private static readonly object _cacheLock = new object();

        public DatabaseEngine()
        {
            // V1.8.5: Database is now kept in the application folder for better reliability and speed.
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database_v2.db");
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            var directory = Path.GetDirectoryName(_dbPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                using (var pragmaCmd = connection.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
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
                        Cast TEXT DEFAULT '',
                        Director TEXT DEFAULT '',
                        TrailerUrl TEXT DEFAULT '',
                        ReleaseDate TEXT DEFAULT '',
                        VoteAverage REAL DEFAULT 0.0,
                        CreatedAt TEXT,
                        MediaType TEXT DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_channels_playlisturl ON Channels (PlaylistUrl);
                    CREATE INDEX IF NOT EXISTS idx_epg_channel_time ON EpgPrograms (ChannelName, StartTime, EndTime);
                ";
                command.ExecuteNonQuery();

                // Column Migrations
                string[] newCols = {
                    "ALTER TABLE Channels ADD COLUMN ViewersCount INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN PersonalWatchCount INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN IsPremium INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN ImdbId TEXT DEFAULT ''",
                    "ALTER TABLE Channels ADD COLUMN Overview TEXT DEFAULT ''",
                    "ALTER TABLE Channels ADD COLUMN BackdropUrl TEXT DEFAULT ''",
                    "ALTER TABLE Channels ADD COLUMN [Cast] TEXT DEFAULT ''",
                    "ALTER TABLE Channels ADD COLUMN UrlSpeeds TEXT DEFAULT ''",
                    "ALTER TABLE Channels ADD COLUMN PreferredNameIndex INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN PreferredLogoIndex INTEGER DEFAULT 0",
                    "ALTER TABLE Channels ADD COLUMN PreferredEpgIndex INTEGER DEFAULT 0"
                };

                foreach (var sql in newCols)
                {
                    try
                    {
                        var cmdAlter = connection.CreateCommand();
                        cmdAlter.CommandText = sql;
                        cmdAlter.ExecuteNonQuery();
                    }
                    catch { }
                }

                if (GetSetting("MigrationV2Done", "false") != "true")
                {
                    Task.Run(async () => await EnsureDataMigrationAsync());
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

        public async Task<List<Channel>> GetAllChannelsAsync()
        {
            var list = new List<Channel>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredLogoIndex, PreferredEpgIndex FROM Channels";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new Channel {
                        Id = reader.GetString(0), Name = reader.GetString(1), Url = reader.GetString(2),
                        LogoUrl = reader.IsDBNull(3) ? "" : reader.GetString(3), GroupTitle = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        Category = reader.IsDBNull(5) ? "TV" : reader.GetString(5), Language = reader.IsDBNull(6) ? "und" : reader.GetString(6),
                        IsFavorite = reader.GetInt32(7) == 1, CreatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(8)).DateTime,
                        SourceType = reader.IsDBNull(9) ? "M3U" : reader.GetString(9), PlaylistUrl = reader.IsDBNull(10) ? "" : reader.GetString(10),
                        ImdbId = reader.IsDBNull(11) ? "" : reader.GetString(11), Overview = reader.IsDBNull(12) ? "" : reader.GetString(12),
                        BackdropUrl = reader.IsDBNull(13) ? "" : reader.GetString(13), Cast = reader.IsDBNull(14) ? "" : reader.GetString(14),
                        PersonalWatchCount = reader.GetInt32(15), ViewersCount = reader.GetInt32(16),
                        EpgId = reader.IsDBNull(17) ? "" : reader.GetString(17), EpgUrl = reader.IsDBNull(18) ? "" : reader.GetString(18),
                        UrlSpeeds = reader.IsDBNull(19) ? "" : reader.GetString(19),
                        PreferredNameIndex = reader.GetInt32(20), PreferredLogoIndex = reader.GetInt32(21), PreferredEpgIndex = reader.GetInt32(22)
                    });
                }
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
                    cmd.CommandText = "INSERT INTO Channels (Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredLogoIndex, PreferredEpgIndex) VALUES (@Id, @Name, @Url, @Logo, @Group, @Cat, @Lang, @Fav, @Date, @Src, @Playlist, @Imdb, @Overview, @Backdrop, @Cast, @Pwc, @Vc, @EpgId, @EpgUrl, @Us, @Pni, @Pli, @Pei) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Url=excluded.Url, LogoUrl=excluded.LogoUrl, GroupTitle=excluded.GroupTitle, Category=excluded.Category, Language=excluded.Language, IsFavorite=excluded.IsFavorite, ImdbId=excluded.ImdbId, Overview=excluded.Overview, BackdropUrl=excluded.BackdropUrl, [Cast]=excluded.Cast, PersonalWatchCount=excluded.PersonalWatchCount, ViewersCount=excluded.ViewersCount, EpgId=excluded.EpgId, EpgUrl=excluded.EpgUrl, UrlSpeeds=excluded.UrlSpeeds, PreferredNameIndex=excluded.PreferredNameIndex, PreferredLogoIndex=excluded.PreferredLogoIndex, PreferredEpgIndex=excluded.PreferredEpgIndex";
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
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            finally { AsyncDbLock.Release(); }
        }

        public async Task SyncIncomingChannelsAsync(List<Channel> incoming)
        {
            if (incoming == null || incoming.Count == 0) return;

            var existing = await GetAllChannelsAsync();
            var combined = new List<Channel>(existing);
            combined.AddRange(incoming);

            var aggregated = StreamMesh.Core.Media.ChannelAggregator.Instance.AggregateChannels(combined);

            foreach (var ch in aggregated)
            {
                await SaveChannelAsync(ch);
            }
        }

        public async Task<int> AutoAggregateDatabaseAsync()
        {
            var existing = await GetAllChannelsAsync();
            if (existing.Count <= 1) return 0;

            var aggregated = StreamMesh.Core.Media.ChannelAggregator.Instance.AggregateChannels(existing);
            int mergedCount = existing.Count - aggregated.Count;

            if (mergedCount > 0)
            {
                await AsyncDbLock.WaitAsync();
                try
                {
                    using (var connection = new SqliteConnection(ConnectionString))
                    {
                        await connection.OpenAsync();
                        using var tx = connection.BeginTransaction();

                        var clearCmd = connection.CreateCommand();
                        clearCmd.CommandText = "DELETE FROM Channels";
                        await clearCmd.ExecuteNonQueryAsync();

                        tx.Commit();
                    }
                }
                finally { AsyncDbLock.Release(); }

                foreach (var ch in aggregated)
                {
                    await SaveChannelAsync(ch);
                }
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
                    cmd.CommandText = "INSERT INTO EpgPrograms (ChannelName, Title, Description, StartTime, EndTime, SourceUrl) VALUES (@ChannelName, @Title, @Description, @StartTime, @EndTime, @SourceUrl)";
                    var pName = cmd.Parameters.Add("@ChannelName", SqliteType.Text); var pTitle = cmd.Parameters.Add("@Title", SqliteType.Text);
                    var pDesc = cmd.Parameters.Add("@Description", SqliteType.Text); var pStart = cmd.Parameters.Add("@StartTime", SqliteType.Text);
                    var pEnd = cmd.Parameters.Add("@EndTime", SqliteType.Text); var pSrc = cmd.Parameters.Add("@SourceUrl", SqliteType.Text);
                    foreach (var prog in programs)
                    {
                        pName.Value = prog.ChannelName; pTitle.Value = prog.Title; pDesc.Value = prog.Description;
                        pStart.Value = prog.StartTime.ToString("o"); pEnd.Value = prog.EndTime.ToString("o"); pSrc.Value = prog.SourceUrl;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
            }
            finally { AsyncDbLock.Release(); }
        }

        public async Task<List<EpgProgram>> GetEpgForChannelsAsync(List<string> channelNames)
        {
            var list = new List<EpgProgram>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                string names = string.Join("','", channelNames.Select(n => n.Replace("'", "''")));
                cmd.CommandText = $"SELECT ChannelName, Title, Description, StartTime, EndTime FROM EpgPrograms WHERE ChannelName IN ('{names}')";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var ep = new EpgProgram { ChannelName = reader.GetString(0), Title = reader.GetString(1), Description = reader.GetString(2) };
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
                cmd.CommandText = "SELECT ImdbId, Title, PosterUrl, BackdropUrl, Overview, Cast, Director, TrailerUrl, ReleaseDate, VoteAverage, MediaType FROM MetadataPool WHERE SearchQuery = @q";
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
                        cmd.CommandText = "INSERT INTO MetadataPool (SearchQuery, ImdbId, Title, PosterUrl, BackdropUrl, Overview, Cast, Director, TrailerUrl, ReleaseDate, VoteAverage, CreatedAt, MediaType) VALUES (@q, @imdb, @t, @p, @b, @o, @c, @d, @tr, @rd, @v, @ca, @mt)";
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
                        Username = reader.GetString(3), Password = reader.GetString(4), Status = reader.GetString(5),
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
                cmd.Parameters.AddWithValue("@P", acc.Password); cmd.Parameters.AddWithValue("@S", acc.Status);
                cmd.Parameters.AddWithValue("@E", acc.ExpiryDate.ToString("o"));
                cmd.ExecuteNonQuery();
            }
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
        }

        public void ClearChannelCache() { lock (_cacheLock) { _channelCache = null; } }
    }
}
