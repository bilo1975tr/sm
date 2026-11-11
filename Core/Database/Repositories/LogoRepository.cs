using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace StreamMesh.Core.Database.Repositories
{
    public class LogoRepository
    {
        private readonly string _connectionString;

        public LogoRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void UpdateLogoIndex(List<(string key, string file)> items)
        {
            using (var connection = new SqliteConnection(_connectionString))
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
                using (var connection = new SqliteConnection(_connectionString))
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
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT FileName FROM LogoIndex WHERE Key = @k";
                cmd.Parameters.AddWithValue("@k", key);
                return cmd.ExecuteScalar()?.ToString();
            }
        }
    }
}
