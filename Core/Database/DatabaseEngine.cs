using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;
using StreamMesh.Core.Utils;
using StreamMesh.Core.Media;
using StreamMesh.Core.Database.Repositories;

namespace StreamMesh.Core.Database
{
    public class DatabaseEngine
    {
        private readonly string _dbPath;
        private string ConnectionString => $"Data Source={_dbPath};Default Timeout=10;Pooling=True;";

        private static readonly System.Threading.SemaphoreSlim AsyncDbLock = new System.Threading.SemaphoreSlim(1, 1);
        public static bool SuppressEvents { get; set; } = false;

        private static bool _cleanupTriggered = false;

        private readonly SettingsRepository _settings;
        private readonly ChannelRepository _channels;
        private readonly EpgRepository _epg;
        private readonly MetadataRepository _metadata;
        private readonly SourceRepository _sources;
        private readonly IptvRepository _iptv;
        private readonly LogoRepository _logo;

        private static readonly TaskCompletionSource<bool> _initTcs = new();
        private static bool _isInitializing = false;

        public DatabaseEngine()
        {
            // V1.8.5: Database is now kept in the application folder for better reliability and speed.
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database_v2.db");
            _settings = new SettingsRepository(ConnectionString);
            _channels = new ChannelRepository(ConnectionString, AsyncDbLock);
            _epg = new EpgRepository(ConnectionString, AsyncDbLock);
            _metadata = new MetadataRepository(ConnectionString, AsyncDbLock);
            _sources = new SourceRepository(ConnectionString);
            _iptv = new IptvRepository(ConnectionString);
            _logo = new LogoRepository(ConnectionString);
        }

        public async Task InitializeAsync()
        {
            lock (_initTcs)
            {
                if (_isInitializing) return;
                _isInitializing = true;
            }

            try
            {
                await InitializeDatabase().ConfigureAwait(false);
                _initTcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                LogService.LogError("DatabaseEngine: Global Init Failed", ex);
                _initTcs.TrySetException(ex);
                throw;
            }
        }

        public static Task WaitForInitAsync() => _initTcs.Task;

        private async Task InitializeDatabase()
        {
            var directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();

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
                    await pragmaCmd.ExecuteNonQueryAsync();
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
                await command.ExecuteNonQueryAsync();

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
                        await cmdAlter.ExecuteNonQueryAsync();
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
                    await EnsureDataMigrationAsync();
                }

                // V1.8.8: Perform a background cleanup of duplicates on startup (only once)
                if (!_cleanupTriggered)
                {
                    _cleanupTriggered = true;
                    _ = Task.Run(async () => {
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

        // Settings delegators
        public string GetSetting(string key, string defaultValue = "") => _settings.GetSetting(key, defaultValue);
        public void SetSetting(string key, string value) => _settings.SetSetting(key, value);
        public (int count, DateTime date) GetDailyQueryStats() => _settings.GetDailyQueryStats();
        public void IncrementDailyQueryCount() => _settings.IncrementDailyQueryCount();

        // Channel delegators
        public async Task<List<Channel>> GetSeriesEpisodesAsync(string seriesBaseName) => await _channels.GetSeriesEpisodesAsync(seriesBaseName);
        public async Task<List<Channel>> GetAllChannelsAsync() => await _channels.GetAllChannelsAsync();
        public async Task SaveChannelAsync(Channel ch) => await _channels.SaveChannelAsync(ch);
        public async Task SaveChannelsBatchAsync(List<Channel> channels, bool clearFirst = false) => await _channels.SaveChannelsBatchAsync(channels, clearFirst);
        public async Task SyncIncomingChannelsAsync(List<Channel> incoming) => await _channels.SyncIncomingChannelsAsync(incoming);
        public async Task<int> AutoAggregateDatabaseAsync() => await _channels.AutoAggregateDatabaseAsync();
        public async Task<int> GetTotalChannelCountAsync() => await _channels.GetTotalChannelCountAsync();
        public int GetChannelCountBySource(string url) => _channels.GetChannelCountBySource(url);
        public List<Channel> GetChannelsBySource(string url) => _channels.GetChannelsBySource(url);
        public void DeleteChannelById(string channelId) { _channels.DeleteChannelById(channelId); NotifyDatabaseUpdated(); }
        public async Task DeleteChannelsAsync(List<string> ids) { await _channels.DeleteChannelsAsync(ids); NotifyDatabaseUpdated(); }
        public void SaveChannelSync(Channel ch) => _channels.SaveChannelSync(ch);
        public async Task CleanupDuplicatesAsync() => await _channels.CleanupDuplicatesAsync();

        // EPG delegators
        public async Task SaveEpgProgramsAsync(List<EpgProgram> programs) => await _epg.SaveEpgProgramsAsync(programs);
        public async Task CleanupOldEpgProgramsAsync(int daysBack = 1) => await _epg.CleanupOldEpgProgramsAsync(daysBack);
        public async Task ClearEpgSourceDataAsync(string url) => await _epg.ClearEpgSourceDataAsync(url);
        public async Task SaveEpgChannelsBatchAsync(List<(string epgId, string name, string logo, string url)> channels) => await _epg.SaveEpgChannelsBatchAsync(channels);
        public async Task<List<EpgChannelSearchResult>> SearchEpgChannelsAsync(string query, bool allSources) => await _epg.SearchEpgChannelsAsync(query, allSources, GetEpgSources());
        public async Task<List<EpgProgram>> GetCurrentEpgProgramsAsync() => await _epg.GetCurrentEpgProgramsAsync();
        public async Task<List<EpgProgram>> GetEpgForChannelsAsync(List<string> channelNames) => await _epg.GetEpgForChannelsAsync(channelNames);
        public List<string> GetEpgSources() => _epg.GetEpgSources();
        public void AddEpgSource(string url) => _epg.AddEpgSource(url);
        public void RemoveEpgSource(string url) => _epg.RemoveEpgSource(url);

        // Metadata delegators
        public async Task<List<MetadataResult>> GetMetadataPoolForQueryAsync(string query) => await _metadata.GetMetadataPoolForQueryAsync(query);
        public async Task SaveMetadataPoolResultsAsync(string query, List<MetadataResult> results) => await _metadata.SaveMetadataPoolResultsAsync(query, results);

        // Source delegators
        public List<string> GetM3uSources() => _sources.GetM3uSources();
        public void AddM3uSource(string url) => _sources.AddM3uSource(url);
        public void RemoveM3uSource(string url) { _sources.RemoveM3uSource(url); NotifyDatabaseUpdated(); }

        // IPTV delegators
        public List<IptvAccount> GetAllIptvAccounts() => _iptv.GetAllIptvAccounts();
        public void SaveIptvAccount(IptvAccount acc) => _iptv.SaveIptvAccount(acc);
        public void RemoveIptvAccount(string id) => _iptv.RemoveIptvAccount(id);

        // Logo delegators
        public void UpdateLogoIndex(List<(string key, string file)> items) => _logo.UpdateLogoIndex(items);
        public Dictionary<string, string> GetAllLogoIndex() => _logo.GetAllLogoIndex();
        public string? FindLogoInIndex(string key) => _logo.FindLogoInIndex(key);

        // Utility
        public void ExecuteRawNonQuery(string sql)
        {
            using (var connection = new SqliteConnection(ConnectionString)) { connection.Open(); var cmd = connection.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            NotifyDatabaseUpdated();
        }

        public static event EventHandler? OnDatabaseUpdated;
        public static void NotifyDatabaseUpdated() { if (SuppressEvents) return; OnDatabaseUpdated?.Invoke(null, EventArgs.Empty); }

        public void ClearAllSources() { ExecuteRawNonQuery("DELETE FROM M3uSources"); ExecuteRawNonQuery("DELETE FROM EpgSources"); NotifyDatabaseUpdated(); }
        public void ClearAllContents() { ExecuteRawNonQuery("DELETE FROM Channels"); ExecuteRawNonQuery("DELETE FROM EpgPrograms"); NotifyDatabaseUpdated(); }
        public void ClearChannelCache() { /* Cache removed */ }
    }
}
