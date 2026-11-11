using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Database
{
    /// <summary>
    /// Handles SQLite database initial table creation, PRAGMA optimizations, and column migrations.
    /// </summary>
    public static class DatabaseSchemaMigrator
    {
        public static async Task InitializeSchemaAsync(SqliteConnection connection)
        {
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
                await pragmaCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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
                    PreferredUrlIndex INTEGER DEFAULT 0,
                    PreferredLogoIndex INTEGER DEFAULT 0,
                    PreferredEpgIndex INTEGER DEFAULT 0,
                    M3uLineNumber INTEGER DEFAULT 0,
                    RawM3uBlock TEXT DEFAULT ''
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
                    AddedDate INTEGER,
                    IsDefault INTEGER DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS EpgSources (
                    Url TEXT PRIMARY KEY,
                    AddedDate INTEGER,
                    Status TEXT DEFAULT 'Bekliyor',
                    ChannelCount INTEGER DEFAULT 0,
                    ProgramCount INTEGER DEFAULT 0,
                    LastUpdated INTEGER DEFAULT 0,
                    TimeShiftHours INTEGER DEFAULT 0,
                    ErrorMessage TEXT DEFAULT ''
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
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);

            await ApplyColumnMigrationsAsync(connection).ConfigureAwait(false);
        }

        private static async Task ApplyColumnMigrationsAsync(SqliteConnection connection)
        {
            var existingChannelCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var infoCmd = connection.CreateCommand())
            {
                infoCmd.CommandText = "PRAGMA table_info(Channels);";
                using var infoReader = await infoCmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await infoReader.ReadAsync().ConfigureAwait(false))
                {
                    existingChannelCols.Add(infoReader.GetString(1));
                }
            }

            (string col, string sql)[] channelAlterCols = {
                ("ViewersCount", "ALTER TABLE Channels ADD COLUMN ViewersCount INTEGER DEFAULT 0"),
                ("PersonalWatchCount", "ALTER TABLE Channels ADD COLUMN PersonalWatchCount INTEGER DEFAULT 0"),
                ("IsPremium", "ALTER TABLE Channels ADD COLUMN IsPremium INTEGER DEFAULT 0"),
                ("IsWatched", "ALTER TABLE Channels ADD COLUMN IsWatched INTEGER DEFAULT 0"),
                ("ImdbId", "ALTER TABLE Channels ADD COLUMN ImdbId TEXT DEFAULT ''"),
                ("Overview", "ALTER TABLE Channels ADD COLUMN Overview TEXT DEFAULT ''"),
                ("BackdropUrl", "ALTER TABLE Channels ADD COLUMN BackdropUrl TEXT DEFAULT ''"),
                ("Cast", "ALTER TABLE Channels ADD COLUMN [Cast] TEXT DEFAULT ''"),
                ("UrlSpeeds", "ALTER TABLE Channels ADD COLUMN UrlSpeeds TEXT DEFAULT ''"),
                ("PreferredNameIndex", "ALTER TABLE Channels ADD COLUMN PreferredNameIndex INTEGER DEFAULT 0"),
                ("PreferredUrlIndex", "ALTER TABLE Channels ADD COLUMN PreferredUrlIndex INTEGER DEFAULT 0"),
                ("PreferredLogoIndex", "ALTER TABLE Channels ADD COLUMN PreferredLogoIndex INTEGER DEFAULT 0"),
                ("PreferredEpgIndex", "ALTER TABLE Channels ADD COLUMN PreferredEpgIndex INTEGER DEFAULT 0"),
                ("IsEpgLocked", "ALTER TABLE Channels ADD COLUMN IsEpgLocked INTEGER DEFAULT 0"),
                ("LastPositionMs", "ALTER TABLE Channels ADD COLUMN LastPositionMs INTEGER DEFAULT 0"),
                ("M3uLineNumber", "ALTER TABLE Channels ADD COLUMN M3uLineNumber INTEGER DEFAULT 0"),
                ("RawM3uBlock", "ALTER TABLE Channels ADD COLUMN RawM3uBlock TEXT DEFAULT ''")
            };

            foreach (var (col, sql) in channelAlterCols)
            {
                if (!existingChannelCols.Contains(col))
                {
                    try
                    {
                        using var cmdAlter = connection.CreateCommand();
                        cmdAlter.CommandText = sql;
                        await cmdAlter.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
                        {
                            LogService.LogWarning($"Database: Column addition warning: {sql}");
                        }
                    }
                }
            }

            string[] otherCols = {
                "ALTER TABLE EpgPrograms ADD COLUMN SourceUrl TEXT DEFAULT ''",
                "ALTER TABLE M3uSources ADD COLUMN IsDefault INTEGER DEFAULT 0",
                "ALTER TABLE IptvAccounts ADD COLUMN MaxConnections INTEGER DEFAULT 1",
                "ALTER TABLE IptvAccounts ADD COLUMN ActiveConnections INTEGER DEFAULT 0",
                "ALTER TABLE IptvAccounts ADD COLUMN TotalLiveStreams INTEGER DEFAULT 0",
                "ALTER TABLE IptvAccounts ADD COLUMN TotalVodStreams INTEGER DEFAULT 0",
                "ALTER TABLE IptvAccounts ADD COLUMN TotalSeriesStreams INTEGER DEFAULT 0",
                "ALTER TABLE IptvAccounts ADD COLUMN ServerVersion TEXT DEFAULT ''",
                "ALTER TABLE IptvAccounts ADD COLUMN ServerTimezone TEXT DEFAULT ''",
                "ALTER TABLE IptvAccounts ADD COLUMN AllowedFormats TEXT DEFAULT ''",
                "ALTER TABLE IptvAccounts ADD COLUMN IsTrial INTEGER DEFAULT 0",
                "ALTER TABLE IptvAccounts ADD COLUMN LastChecked TEXT DEFAULT ''"
            };

            foreach (var sql in otherCols)
            {
                try
                {
                    using var cmdAlter = connection.CreateCommand();
                    cmdAlter.CommandText = sql;
                    await cmdAlter.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                catch { }
            }
        }
    }
}
