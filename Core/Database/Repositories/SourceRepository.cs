using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace StreamMesh.Core.Database.Repositories
{
    public class M3uSourceEntity
    {
        public string Url { get; set; } = "";
        public string ForcedLanguage { get; set; } = "und";
        public string ForcedCategory { get; set; } = "TV";
        public long AddedDate { get; set; }
        public bool IsDefault { get; set; }
    }

    public class SourceRepository
    {
        private readonly string _connectionString;

        public SourceRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public List<string> GetM3uSources()
        {
            var list = new List<string>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Url FROM M3uSources ORDER BY IsDefault DESC, AddedDate DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(reader.GetString(0));
            }
            return list;
        }

        public List<M3uSourceEntity> GetAllM3uSources()
        {
            var list = new List<M3uSourceEntity>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Url, ForcedLanguage, ForcedCategory, AddedDate, IsDefault FROM M3uSources ORDER BY IsDefault DESC, AddedDate DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new M3uSourceEntity
                    {
                        Url = reader.GetString(0),
                        ForcedLanguage = reader.IsDBNull(1) ? "und" : reader.GetString(1),
                        ForcedCategory = reader.IsDBNull(2) ? "TV" : reader.GetString(2),
                        AddedDate = reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                        IsDefault = !reader.IsDBNull(4) && reader.GetInt32(4) == 1
                    });
                }
            }
            return list;
        }

        public void AddM3uSource(string url, bool isDefault = false)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO M3uSources (Url, AddedDate, IsDefault) VALUES (@Url, @Date, @Def) ON CONFLICT(Url) DO UPDATE SET AddedDate=excluded.AddedDate";
                cmd.Parameters.AddWithValue("@Url", url);
                cmd.Parameters.AddWithValue("@Date", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                cmd.Parameters.AddWithValue("@Def", isDefault ? 1 : 0);
                cmd.ExecuteNonQuery();
            }
        }

        public void SetDefaultM3uSource(string url)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using var tx = connection.BeginTransaction();

                var cmdClear = connection.CreateCommand();
                cmdClear.Transaction = tx;
                cmdClear.CommandText = "UPDATE M3uSources SET IsDefault = 0";
                cmdClear.ExecuteNonQuery();

                var cmdSet = connection.CreateCommand();
                cmdSet.Transaction = tx;
                cmdSet.CommandText = "UPDATE M3uSources SET IsDefault = 1 WHERE Url = @Url";
                cmdSet.Parameters.AddWithValue("@Url", url);
                cmdSet.ExecuteNonQuery();

                tx.Commit();
            }
        }

        public string? GetDefaultM3uSource()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Url FROM M3uSources WHERE IsDefault = 1 LIMIT 1";
                var res = cmd.ExecuteScalar();
                return res?.ToString();
            }
        }

        public void RemoveM3uSource(string url)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM M3uSources WHERE Url = @Url; DELETE FROM Channels WHERE PlaylistUrl = @Url;";
                cmd.Parameters.AddWithValue("@Url", url); cmd.ExecuteNonQuery();
            }
        }
    }
}
