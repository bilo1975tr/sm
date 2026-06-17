using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public partial class DatabaseService
    {
        private readonly string _dbPath;

        public DatabaseService()
        {
            _dbPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "StreamMesh", "database.db");
            EnsureDatabaseExists();
        }

        public void EnsureDatabaseExists()
        {
            var directory = Path.GetDirectoryName(_dbPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                
                // En yüksek hız için WAL (Write-Ahead Logging) ve Senkronizasyon optimizasyonu
                using (var pragmaCmd = connection.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                    pragmaCmd.ExecuteNonQuery();
                }

                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Settings (
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
                        PersonalWatchCount INTEGER DEFAULT 0
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
                    CREATE TABLE IF NOT EXISTS DeadLinkHashes (
                        Hash INTEGER PRIMARY KEY
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
                    var indexCmd = connection.CreateCommand();
                    indexCmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_epg_channel_time ON EpgPrograms (ChannelName, StartTime, EndTime);";
                    indexCmd.ExecuteNonQuery();
                } catch { }

                // Cihaz veri tabanındaki mevcut dilleri normalize etme işlemi (SQL performans optimizasyonu)
                try {
                    using (var normalizeCmd = connection.CreateCommand())
                    {
                        normalizeCmd.CommandText = @"
                            UPDATE Channels SET Language = 'Türkçe' WHERE Language IN ('tr', 'tur', 'turkish', 'turkce', 'Türkçe', 'Turkish', 'Türkçe (Türkiye)');
                            UPDATE Channels SET Language = 'Almanca' WHERE Language IN ('de', 'ger', 'german', 'deutsch', 'Deutsch', 'German', 'Almanca', 'Deutsch (Deutschland)');
                            UPDATE Channels SET Language = 'İngilizce' WHERE Language IN ('en', 'eng', 'english', 'English', 'İngilizce', 'English (United States)');
                            UPDATE Channels SET Language = 'Fransızca' WHERE Language IN ('fr', 'fra', 'french', 'Fransızca', 'French', 'français (France)');
                            UPDATE Channels SET Language = 'İspanyolca' WHERE Language IN ('es', 'esp', 'spanish', 'İspanyolca', 'Spanish', 'español (España)');
                            UPDATE Channels SET Language = 'Rusça' WHERE Language IN ('ru', 'rus', 'russian', 'Rusça', 'Russian', 'русский (Россия)');
                            UPDATE Channels SET Language = 'İtalyanca' WHERE Language IN ('it', 'ita', 'italian', 'İtalyanca', 'Italian', 'italiano (Italia)');
                            UPDATE Channels SET Language = 'Arapça' WHERE Language IN ('ar', 'ara', 'arabic', 'Arapça', 'Arabic');
                            UPDATE Channels SET Language = 'Kürtçe' WHERE Language IN ('ku', 'kur', 'kurdish', 'Kürtçe', 'Kurdish');
                            UPDATE Channels SET Language = 'Azerice' WHERE Language IN ('az', 'aze', 'azeri', 'Azerice', 'Azerbaijani');
                            UPDATE Channels SET Language = 'Bilinmiyor' WHERE Language IN ('Bilinmiyor', 'unknown', 'none', 'hiçbiri', 'Unknown', 'None', '', NULL);
                        ";
                        normalizeCmd.ExecuteNonQuery();
                    }
                } catch { }
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
            long hash = GetFnv1aHash(url.Trim());
            try
            {
                using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT OR IGNORE INTO DeadLinkHashes (Hash) VALUES (@Hash)";
                    cmd.Parameters.AddWithValue("@Hash", hash);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("AddDeadLink error", ex);
            }
        }

        public bool IsLinkDead(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            long hash = GetFnv1aHash(url.Trim());
            try
            {
                using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
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
    }
}
