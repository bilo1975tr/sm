using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public partial class DatabaseService
    {
        private readonly string _dbPath;
        private string ConnectionString => $"Data Source={_dbPath};Default Timeout=10;Pooling=True;";

        private static bool _databaseChecked = false;
        private static readonly object _dbCheckLock = new object();

        public DatabaseService()
        {
            _dbPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StreamMesh", "database.db");
            EnsureDatabaseExists();
        }

        public void EnsureDatabaseExists()
        {
            lock (_dbCheckLock)
            {
                if (_databaseChecked) return;

                var directory = Path.GetDirectoryName(_dbPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    
                    // En yüksek hız için WAL (Write-Ahead Logging) ve Senkronizasyon optimizasyonu
                    using (var pragmaCmd = connection.CreateCommand())
                    {
                        pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
                        pragmaCmd.ExecuteNonQuery();
                    }

                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Settings (
                        Key TEXT PRIMARY KEY,
                        Value TEXT
                    );
                    CREATE TABLE IF NOT EXISTS PendingFirebasePushes (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SafeKey TEXT UNIQUE,
                        JsonPayload TEXT,
                        RetryCount INTEGER DEFAULT 0,
                        CreatedAt TEXT,
                        LastAttemptAt TEXT,
                        Status TEXT DEFAULT 'pending'
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
                        IsPremium INTEGER DEFAULT 0
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
                        DisplayName TEXT,
                        LogoUrl TEXT,
                        SourceUrl TEXT,
                        PRIMARY KEY (EpgId, SourceUrl)
                    );
                    CREATE TABLE IF NOT EXISTS DeadLinkHashes (
                        Hash INTEGER PRIMARY KEY
                    );
                    CREATE TABLE IF NOT EXISTS WatchProgress (
                        ChannelId TEXT PRIMARY KEY,
                        Title TEXT,
                        Seconds INTEGER,
                        Duration INTEGER,
                        LastWatched TEXT
                    );
                    CREATE TABLE IF NOT EXISTS VerificationCache (
                        ChannelId TEXT PRIMARY KEY,
                        VerifiedAt INTEGER,
                        Category TEXT,
                        Resolution TEXT,
                        IsWorking INTEGER
                    );
                ";
                command.ExecuteNonQuery();

                // Add columns to existing DB if necessary
                try {
                    var cmdUpdate = connection.CreateCommand();
                    cmdUpdate.CommandText = "ALTER TABLE Channels ADD COLUMN PersonalWatchCount INTEGER DEFAULT 0;";
                    cmdUpdate.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmdIsLocked = connection.CreateCommand();
                    alterCmdIsLocked.CommandText = "ALTER TABLE Channels ADD COLUMN IsLocked INTEGER DEFAULT 0;";
                    alterCmdIsLocked.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmdNotes = connection.CreateCommand();
                    alterCmdNotes.CommandText = "ALTER TABLE Channels ADD COLUMN Notes TEXT DEFAULT '';";
                    alterCmdNotes.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmd1 = connection.CreateCommand();
                    alterCmd1.CommandText = "ALTER TABLE Channels ADD COLUMN Category TEXT DEFAULT 'TV';";
                    alterCmd1.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmd2 = connection.CreateCommand();
                    alterCmd2.CommandText = "ALTER TABLE Channels ADD COLUMN Language TEXT DEFAULT 'Bilinmiyor';";
                    alterCmd2.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmd3 = connection.CreateCommand();
                    alterCmd3.CommandText = "ALTER TABLE Channels ADD COLUMN PlaylistUrl TEXT DEFAULT '';";
                    alterCmd3.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmd4 = connection.CreateCommand();
                    alterCmd4.CommandText = "ALTER TABLE Channels ADD COLUMN IsFavorite INTEGER DEFAULT 0;";
                    alterCmd4.ExecuteNonQuery();
                } catch { }

                try {
                    var alterEpg1 = connection.CreateCommand();
                    alterEpg1.CommandText = "ALTER TABLE EpgPrograms ADD COLUMN SourceUrl TEXT DEFAULT '';";
                    alterEpg1.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmd5 = connection.CreateCommand();
                    alterCmd5.CommandText = "ALTER TABLE Channels ADD COLUMN EpgId TEXT DEFAULT '';";
                    alterCmd5.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmdIsVerified = connection.CreateCommand();
                    alterCmdIsVerified.CommandText = "ALTER TABLE Channels ADD COLUMN IsVerified INTEGER DEFAULT 0;";
                    alterCmdIsVerified.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmdEpgUrl = connection.CreateCommand();
                    alterCmdEpgUrl.CommandText = "ALTER TABLE Channels ADD COLUMN EpgUrl TEXT DEFAULT '';";
                    alterCmdEpgUrl.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmdIsPremium = connection.CreateCommand();
                    alterCmdIsPremium.CommandText = "ALTER TABLE Channels ADD COLUMN IsPremium INTEGER DEFAULT 0;";
                    alterCmdIsPremium.ExecuteNonQuery();
                } catch { }

                try {
                    var alterPendingStatus = connection.CreateCommand();
                    alterPendingStatus.CommandText = "ALTER TABLE PendingFirebasePushes ADD COLUMN Status TEXT DEFAULT 'pending';";
                    alterPendingStatus.ExecuteNonQuery();
                } catch { }

                try {
                    var indexCmd = connection.CreateCommand();
                    indexCmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_epg_channel_time ON EpgPrograms (ChannelName, StartTime, EndTime);";
                    indexCmd.ExecuteNonQuery();
                } catch { }

                // Performans İyileştirmesi: Büyük listelerde sorguları hızlandırmak için yeni indeksler (Auto Upgrade: Veritabanı Kuralı)
                try {
                    var indexCmd2 = connection.CreateCommand();
                    indexCmd2.CommandText = @"
                        CREATE INDEX IF NOT EXISTS idx_channels_playlisturl ON Channels (PlaylistUrl);
                        CREATE INDEX IF NOT EXISTS idx_channels_isverified_added ON Channels (IsVerified, AddedDate DESC);
                        CREATE INDEX IF NOT EXISTS idx_channels_addeddate ON Channels (AddedDate DESC);
                    ";
                    indexCmd2.ExecuteNonQuery();
                } catch { }

                // Cihaz veri tabanındaki mevcut dilleri ISO 639-1 kodlarına dönüştürme ve normalize etme işlemi
                try {
                    using (var normalizeCmd = connection.CreateCommand())
                    {
                        normalizeCmd.CommandText = @"
                            UPDATE Channels SET Language = 'tr' WHERE Language IN ('tr', 'tur', 'turkish', 'turkce', 'Türkçe', 'Turkish', 'Türkçe (Türkiye)');
                            UPDATE Channels SET Language = 'de' WHERE Language IN ('de', 'ger', 'german', 'deutsch', 'Deutsch', 'German', 'Almanca', 'Deutsch (Deutschland)');
                            UPDATE Channels SET Language = 'en' WHERE Language IN ('en', 'eng', 'english', 'English', 'İngilizce', 'English (United States)');
                            UPDATE Channels SET Language = 'fr' WHERE Language IN ('fr', 'fra', 'french', 'Fransızca', 'French', 'français (France)');
                            UPDATE Channels SET Language = 'es' WHERE Language IN ('es', 'esp', 'spanish', 'İspanyolca', 'Spanish', 'español (España)');
                            UPDATE Channels SET Language = 'ru' WHERE Language IN ('ru', 'rus', 'russian', 'Rusça', 'Russian', 'русский (Россия)');
                            UPDATE Channels SET Language = 'it' WHERE Language IN ('it', 'ita', 'italian', 'İtalyanca', 'Italian', 'italiano (Italia)');
                            UPDATE Channels SET Language = 'ar' WHERE Language IN ('ar', 'ara', 'arabic', 'Arapça', 'Arabic');
                            UPDATE Channels SET Language = 'ku' WHERE Language IN ('ku', 'kur', 'kurdish', 'Kürtçe', 'Kurdish');
                            UPDATE Channels SET Language = 'az' WHERE Language IN ('az', 'aze', 'azeri', 'Azerice', 'Azerbaijani');
                            UPDATE Channels SET Language = 'und' WHERE Language IN ('Bilinmiyor', 'unknown', 'none', 'hiçbiri', 'Unknown', 'None', '', NULL);
                        ";
                        normalizeCmd.ExecuteNonQuery();
                    }
                } catch { }

                // Metadata columns migration for ImdbId, Overview, BackdropUrl, Cast
                try {
                    var alterCmdImdb = connection.CreateCommand();
                    alterCmdImdb.CommandText = "ALTER TABLE Channels ADD COLUMN ImdbId TEXT DEFAULT '';";
                    alterCmdImdb.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmdOverview = connection.CreateCommand();
                    alterCmdOverview.CommandText = "ALTER TABLE Channels ADD COLUMN Overview TEXT DEFAULT '';";
                    alterCmdOverview.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmdBackdrop = connection.CreateCommand();
                    alterCmdBackdrop.CommandText = "ALTER TABLE Channels ADD COLUMN BackdropUrl TEXT DEFAULT '';";
                    alterCmdBackdrop.ExecuteNonQuery();
                } catch { }

                try {
                    var alterCmdCast = connection.CreateCommand();
                    alterCmdCast.CommandText = "ALTER TABLE Channels ADD COLUMN [Cast] TEXT DEFAULT '';";
                    alterCmdCast.ExecuteNonQuery();
                } catch { }

                EnsureNormalizationCacheTableExists();
                NormalizeExistingUnknownChannels();

                try
                {
                    SyncBlacklistWithFile();
                }
                catch (Exception ex)
                {
                    LogService.LogError("SyncBlacklistWithFile failed during DB initialization", ex);
                }
            }
            
            _databaseChecked = true;
            }
        }

        public static long GetFnv1aHash(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash ^= (ushort)c;
                hash *= 1099511628211UL;
            }
            return (long)hash;
        }

        public void AddDeadLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            string trimmedUrl = url.Trim();
            long hash = GetFnv1aHash(trimmedUrl);
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT OR IGNORE INTO DeadLinkHashes (Hash) VALUES (@Hash)";
                    cmd.Parameters.AddWithValue("@Hash", hash);
                    cmd.ExecuteNonQuery();
                }

                AppendToBlacklistFile(trimmedUrl);
            }
            catch (Exception ex)
            {
                LogService.LogError("AddDeadLink error", ex);
            }
        }

        private static readonly string BlacklistFilePath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StreamMesh", "blacklist.txt.gz");
        private static readonly object BlacklistLock = new object();

        public void SyncBlacklistWithFile()
        {
            lock (BlacklistLock)
            {
                try
                {
                    var fileUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (File.Exists(BlacklistFilePath))
                    {
                        using (var fs = new FileStream(BlacklistFilePath, FileMode.Open, FileAccess.Read))
                        using (var gzip = new GZipStream(fs, CompressionMode.Decompress))
                        using (var reader = new StreamReader(gzip, System.Text.Encoding.UTF8))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    fileUrls.Add(line.Trim());
                                }
                            }
                        }
                    }

                    if (fileUrls.Count == 0) return;

                    var dbHashes = GetAllDeadLinkHashes();

                    using (var connection = new SqliteConnection(ConnectionString))
                    {
                        connection.Open();
                        using (var transaction = connection.BeginTransaction())
                        {
                            var cmd = connection.CreateCommand();
                            cmd.CommandText = "INSERT OR IGNORE INTO DeadLinkHashes (Hash) VALUES (@Hash)";
                            var pHash = cmd.Parameters.Add("@Hash", SqliteType.Integer);

                            bool hasNew = false;
                            foreach (var url in fileUrls)
                            {
                                long hash = GetFnv1aHash(url);
                                if (!dbHashes.Contains(hash))
                                {
                                    pHash.Value = hash;
                                    cmd.ExecuteNonQuery();
                                    hasNew = true;
                                }
                            }
                            if (hasNew)
                            {
                                transaction.Commit();
                                LogService.Log($"[Blacklist] {fileUrls.Count} URL'den yeni ölü linkler veritabanına geri yüklendi.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[Blacklist] Dosya senkronizasyonu hatası", ex);
                }
            }
        }

        private void AppendToBlacklistFile(string url)
        {
            lock (BlacklistLock)
            {
                try
                {
                    var directory = Path.GetDirectoryName(BlacklistFilePath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (File.Exists(BlacklistFilePath))
                    {
                        using (var fs = new FileStream(BlacklistFilePath, FileMode.Open, FileAccess.Read))
                        using (var gzip = new GZipStream(fs, CompressionMode.Decompress))
                        using (var reader = new StreamReader(gzip, System.Text.Encoding.UTF8))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (!string.IsNullOrWhiteSpace(line)) existing.Add(line.Trim());
                            }
                        }
                    }

                    if (!existing.Contains(url))
                    {
                        existing.Add(url);
                        using (var fs = new FileStream(BlacklistFilePath, FileMode.Create, FileAccess.Write))
                        using (var gzip = new GZipStream(fs, CompressionMode.Compress))
                        using (var writer = new StreamWriter(gzip, System.Text.Encoding.UTF8))
                        {
                            foreach (var u in existing)
                            {
                                writer.WriteLine(u);
                            }
                        }
                        LogService.Log($"[Blacklist] Yeni silinen URL sıkıştırılmış dosyaya yedeklendi: {url}");
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[Blacklist] Sıkıştırılmış dosyaya yazma hatası", ex);
                }
            }
        }

        public bool IsLinkDead(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            long hash = GetFnv1aHash(url.Trim());
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT 1 FROM DeadLinkHashes WHERE Hash = @Hash";
                    cmd.Parameters.AddWithValue("@Hash", hash);
                    var result = cmd.ExecuteScalar();
                    return result != null;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("IsLinkDead error", ex);
                return false;
            }
        }

        public HashSet<long> GetAllDeadLinkHashes()
        {
            var hashes = new HashSet<long>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Hash FROM DeadLinkHashes";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            hashes.Add(reader.GetInt64(0));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("GetAllDeadLinkHashes error", ex);
            }
            return hashes;
        }

        public List<Dictionary<string, object>> ExecuteRawQuery(string sql)
        {
            var results = new List<Dictionary<string, object>>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            }
                            results.Add(row);
                        }
                    }
                }
            }
            return results;
        }

        public int ExecuteRawNonQuery(string sql)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    return command.ExecuteNonQuery();
                }
            }
        }
    }
}
