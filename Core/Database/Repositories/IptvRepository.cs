using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Core.Database.Repositories
{
    public class IptvRepository
    {
        private readonly string _connectionString;

        public IptvRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private static bool _schemaEnsured = false;
        private static readonly object _schemaLock = new object();

        private void EnsureSchema(SqliteConnection connection)
        {
            if (_schemaEnsured) return;
            lock (_schemaLock)
            {
                if (_schemaEnsured) return;
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS IptvAccounts (
                            Id TEXT PRIMARY KEY,
                            Name TEXT,
                            ServerUrl TEXT,
                            Username TEXT,
                            Password TEXT,
                            Status TEXT,
                            ExpiryDate TEXT
                        );";
                    cmd.ExecuteNonQuery();

                    var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var infoCmd = connection.CreateCommand())
                    {
                        infoCmd.CommandText = "PRAGMA table_info(IptvAccounts);";
                        using var r = infoCmd.ExecuteReader();
                        while (r.Read())
                        {
                            existingCols.Add(r.GetString(1));
                        }
                    }

                    (string col, string alterSql)[] colsToAdd = {
                        ("MaxConnections", "ALTER TABLE IptvAccounts ADD COLUMN MaxConnections INTEGER DEFAULT 1"),
                        ("ActiveConnections", "ALTER TABLE IptvAccounts ADD COLUMN ActiveConnections INTEGER DEFAULT 0"),
                        ("TotalLiveStreams", "ALTER TABLE IptvAccounts ADD COLUMN TotalLiveStreams INTEGER DEFAULT 0"),
                        ("TotalVodStreams", "ALTER TABLE IptvAccounts ADD COLUMN TotalVodStreams INTEGER DEFAULT 0"),
                        ("TotalSeriesStreams", "ALTER TABLE IptvAccounts ADD COLUMN TotalSeriesStreams INTEGER DEFAULT 0"),
                        ("ServerVersion", "ALTER TABLE IptvAccounts ADD COLUMN ServerVersion TEXT DEFAULT ''"),
                        ("ServerTimezone", "ALTER TABLE IptvAccounts ADD COLUMN ServerTimezone TEXT DEFAULT ''"),
                        ("AllowedFormats", "ALTER TABLE IptvAccounts ADD COLUMN AllowedFormats TEXT DEFAULT ''"),
                        ("IsTrial", "ALTER TABLE IptvAccounts ADD COLUMN IsTrial INTEGER DEFAULT 0"),
                        ("LastChecked", "ALTER TABLE IptvAccounts ADD COLUMN LastChecked TEXT DEFAULT ''"),
                        ("HasServerIndex", "ALTER TABLE IptvAccounts ADD COLUMN HasServerIndex INTEGER DEFAULT 0"),
                        ("LocalChannelsCount", "ALTER TABLE IptvAccounts ADD COLUMN LocalChannelsCount INTEGER DEFAULT 0"),
                        ("PreferredStreamFormat", "ALTER TABLE IptvAccounts ADD COLUMN PreferredStreamFormat TEXT DEFAULT 'm3u8'")
                    };

                    foreach (var (col, sql) in colsToAdd)
                    {
                        if (!existingCols.Contains(col))
                        {
                            try
                            {
                                using var alterCmd = connection.CreateCommand();
                                alterCmd.CommandText = sql;
                                alterCmd.ExecuteNonQuery();
                            }
                            catch { }
                        }
                    }

                    _schemaEnsured = true;
                }
                catch { }
            }
        }

        public static string NormalizeServerUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            string s = url.Trim().TrimEnd('/');
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                s = "http://" + s;
            }
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri))
            {
                string scheme = uri.Scheme;
                string host = uri.Host;
                if (uri.IsDefaultPort)
                {
                    return $"{scheme}://{host}";
                }
                return $"{scheme}://{host}:{uri.Port}";
            }
            if (s.EndsWith(":80")) s = s.Substring(0, s.Length - 3);
            else if (s.EndsWith(":443")) s = s.Substring(0, s.Length - 4);
            return s;
        }

        private void CleanDuplicateAccounts(List<IptvAccount> list)
        {
            if (list == null || list.Count <= 1) return;
            var groups = list.GroupBy(a => $"{NormalizeServerUrl(a.ServerUrl)}/{(a.Username ?? "").Trim()}".ToLowerInvariant()).Where(g => g.Count() > 1).ToList();

            foreach (var g in groups)
            {
                var keeper = g.OrderByDescending(a => a.TotalContentCount)
                              .ThenByDescending(a => a.LocalChannelsCount)
                              .ThenBy(a => a.ServerUrl.Contains(":80") ? 1 : 0)
                              .First();

                var duplicates = g.Where(a => a.Id != keeper.Id).ToList();
                foreach (var dup in duplicates)
                {
                    try { RemoveIptvAccount(dup.Id); } catch { }
                    list.Remove(dup);
                }

                string normUrl = NormalizeServerUrl(keeper.ServerUrl);
                if (keeper.ServerUrl != normUrl)
                {
                    keeper.ServerUrl = normUrl;
                    keeper.Name = $"{keeper.Username} ({StreamMesh.Core.Media.IptvAccountHelper.ExtractHostAndPort(normUrl)})";
                    SaveIptvAccount(keeper);
                }
            }
        }

        public List<IptvAccount> GetAllIptvAccounts()
        {
            var list = new List<IptvAccount>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                EnsureSchema(connection);

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"SELECT Id, Name, ServerUrl, Username, Password, Status, ExpiryDate, 
                                    MaxConnections, ActiveConnections, TotalLiveStreams, TotalVodStreams, TotalSeriesStreams, 
                                    ServerVersion, ServerTimezone, AllowedFormats, IsTrial, LastChecked,
                                    HasServerIndex, LocalChannelsCount, PreferredStreamFormat
                                    FROM IptvAccounts";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    try
                    {
                        var acc = new IptvAccount
                        {
                            Id = reader.GetString(0),
                            Name = reader.GetString(1),
                            ServerUrl = reader.GetString(2),
                            Username = reader.GetString(3),
                            Password = Decrypt(reader.GetString(4)),
                            Status = reader.GetString(5),
                            ExpiryDate = DateTime.TryParse(reader.GetString(6), out DateTime dt) ? dt : DateTime.MinValue,
                            MaxConnections = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7),
                            ActiveConnections = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                            TotalLiveStreams = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                            TotalVodStreams = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                            TotalSeriesStreams = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                            ServerVersion = reader.IsDBNull(12) ? "" : reader.GetString(12),
                            ServerTimezone = reader.IsDBNull(13) ? "" : reader.GetString(13),
                            AllowedFormats = reader.IsDBNull(14) ? "" : reader.GetString(14),
                            IsTrial = !reader.IsDBNull(15) && reader.GetInt32(15) == 1,
                            LastChecked = DateTime.TryParse(reader.IsDBNull(16) ? "" : reader.GetString(16), out DateTime lc) ? lc : DateTime.MinValue,
                            HasServerIndex = !reader.IsDBNull(17) && reader.GetInt32(17) == 1,
                            LocalChannelsCount = reader.IsDBNull(18) ? 0 : reader.GetInt32(18),
                            PreferredStreamFormat = reader.IsDBNull(19) ? "m3u8" : reader.GetString(19)
                        };
                        list.Add(acc);
                    }
                    catch { }
                }
            }
            catch
            {
                // Fallback for base table if altered columns fail
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, Name, ServerUrl, Username, Password, Status, ExpiryDate FROM IptvAccounts";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(new IptvAccount
                        {
                            Id = reader.GetString(0),
                            Name = reader.GetString(1),
                            ServerUrl = reader.GetString(2),
                            Username = reader.GetString(3),
                            Password = Decrypt(reader.GetString(4)),
                            Status = reader.GetString(5),
                            ExpiryDate = DateTime.TryParse(reader.GetString(6), out DateTime dt) ? dt : DateTime.MinValue
                        });
                    }
                }
                catch { }
            }

            CleanDuplicateAccounts(list);
            return list;
        }

        public void SaveIptvAccount(IptvAccount acc)
        {
            if (acc == null) return;
            try
            {
                acc.ServerUrl = NormalizeServerUrl(acc.ServerUrl);
                if (string.IsNullOrWhiteSpace(acc.Name) || acc.Name == "Yeni IPTV Hesabı")
                {
                    acc.Name = $"{acc.Username} ({StreamMesh.Core.Media.IptvAccountHelper.ExtractHostAndPort(acc.ServerUrl)})";
                }

                // Check for duplicate account with different Id and reuse Id
                try
                {
                    var existing = GetAllIptvAccounts().FirstOrDefault(a => a.Id != acc.Id && 
                        NormalizeServerUrl(a.ServerUrl).Equals(acc.ServerUrl, StringComparison.OrdinalIgnoreCase) && 
                        (a.Username ?? "").Equals(acc.Username ?? "", StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        acc.Id = existing.Id;
                    }
                }
                catch { }

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                EnsureSchema(connection);

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"INSERT INTO IptvAccounts 
                    (Id, Name, ServerUrl, Username, Password, Status, ExpiryDate, 
                     MaxConnections, ActiveConnections, TotalLiveStreams, TotalVodStreams, TotalSeriesStreams, 
                     ServerVersion, ServerTimezone, AllowedFormats, IsTrial, LastChecked,
                     HasServerIndex, LocalChannelsCount, PreferredStreamFormat) 
                    VALUES (@Id, @N, @U, @Un, @P, @S, @E, @MaxC, @ActC, @TLive, @TVod, @TSeries, @SVer, @STz, @AFmt, @Trial, @LC, @HIdx, @LCount, @PFmt) 
                    ON CONFLICT(Id) DO UPDATE SET 
                    Name=@N, ServerUrl=@U, Username=@Un, Password=@P, Status=@S, ExpiryDate=@E,
                    MaxConnections=@MaxC, ActiveConnections=@ActC, TotalLiveStreams=@TLive, TotalVodStreams=@TVod, TotalSeriesStreams=@TSeries,
                    ServerVersion=@SVer, ServerTimezone=@STz, AllowedFormats=@AFmt, IsTrial=@Trial, LastChecked=@LC,
                    HasServerIndex=@HIdx, LocalChannelsCount=@LCount, PreferredStreamFormat=@PFmt";

                cmd.Parameters.AddWithValue("@Id", acc.Id);
                cmd.Parameters.AddWithValue("@N", acc.Name ?? "");
                cmd.Parameters.AddWithValue("@U", acc.ServerUrl ?? "");
                cmd.Parameters.AddWithValue("@Un", acc.Username ?? "");
                cmd.Parameters.AddWithValue("@P", Encrypt(acc.Password ?? ""));
                cmd.Parameters.AddWithValue("@S", acc.Status ?? "Aktif");
                cmd.Parameters.AddWithValue("@E", acc.ExpiryDate.ToString("o"));
                cmd.Parameters.AddWithValue("@MaxC", acc.MaxConnections.HasValue ? (object)acc.MaxConnections.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@ActC", acc.ActiveConnections);
                cmd.Parameters.AddWithValue("@TLive", acc.TotalLiveStreams);
                cmd.Parameters.AddWithValue("@TVod", acc.TotalVodStreams);
                cmd.Parameters.AddWithValue("@TSeries", acc.TotalSeriesStreams);
                cmd.Parameters.AddWithValue("@SVer", acc.ServerVersion ?? "");
                cmd.Parameters.AddWithValue("@STz", acc.ServerTimezone ?? "");
                cmd.Parameters.AddWithValue("@AFmt", acc.AllowedFormats ?? "");
                cmd.Parameters.AddWithValue("@Trial", acc.IsTrial ? 1 : 0);
                cmd.Parameters.AddWithValue("@LC", acc.LastChecked.ToString("o"));
                cmd.Parameters.AddWithValue("@HIdx", acc.HasServerIndex ? 1 : 0);
                cmd.Parameters.AddWithValue("@LCount", acc.LocalChannelsCount);
                cmd.Parameters.AddWithValue("@PFmt", acc.PreferredStreamFormat ?? "m3u8");
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Fallback simple save
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO IptvAccounts (Id, Name, ServerUrl, Username, Password, Status, ExpiryDate) VALUES (@Id, @N, @U, @Un, @P, @S, @E) ON CONFLICT(Id) DO UPDATE SET Name=@N, ServerUrl=@U, Username=@Un, Password=@P, Status=@S, ExpiryDate=@E";
                    cmd.Parameters.AddWithValue("@Id", acc.Id);
                    cmd.Parameters.AddWithValue("@N", acc.Name ?? "");
                    cmd.Parameters.AddWithValue("@U", acc.ServerUrl ?? "");
                    cmd.Parameters.AddWithValue("@Un", acc.Username ?? "");
                    cmd.Parameters.AddWithValue("@P", Encrypt(acc.Password ?? ""));
                    cmd.Parameters.AddWithValue("@S", acc.Status ?? "Aktif");
                    cmd.Parameters.AddWithValue("@E", acc.ExpiryDate.ToString("o"));
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        public void RemoveIptvAccount(string id)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM IptvAccounts WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Id", id); cmd.ExecuteNonQuery();
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
    }
}
