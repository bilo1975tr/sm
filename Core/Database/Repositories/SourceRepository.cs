using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace StreamMesh.Core.Database.Repositories
{
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
                cmd.CommandText = "SELECT Url FROM M3uSources ORDER BY AddedDate DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(reader.GetString(0));
            }
            return list;
        }

        public void AddM3uSource(string url)
        {
            using (var connection = new SqliteConnection(_connectionString))
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
