using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public partial class DatabaseService
    {
        public string GetSetting(string key, string defaultValue = "")
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Value FROM Settings WHERE Key = @Key";
                command.Parameters.AddWithValue("@Key", key);
                var result = command.ExecuteScalar();
                return result != null ? result.ToString() : defaultValue;
            }
        }

        public void SetSetting(string key, string value)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Settings (Key, Value) VALUES (@Key, @Value)
                    ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                ";
                command.Parameters.AddWithValue("@Key", key);
                command.Parameters.AddWithValue("@Value", value);
                command.ExecuteNonQuery();
            }
        }

        public List<string> GetM3uSources()
        {
            string current = GetSetting("m3u_sources", "");
            if (string.IsNullOrEmpty(current)) return new List<string>();
            return new List<string>(current.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries));
        }

        public void AddM3uSource(string url)
        {
            var sources = GetM3uSources();
            if (!sources.Contains(url))
            {
                sources.Add(url);
                SetSetting("m3u_sources", string.Join("|||", sources));
            }
        }

        public void RemoveM3uSource(string url)
        {
            var sources = GetM3uSources();
            if (sources.Contains(url))
            {
                sources.Remove(url);
                SetSetting("m3u_sources", string.Join("|||", sources));
                
                try
                {
                    using (var connection = new SqliteConnection(ConnectionString))
                    {
                        connection.Open();
                        var command = connection.CreateCommand();
                        command.CommandText = "DELETE FROM Channels WHERE PlaylistUrl = @Url OR Url = @Url";
                        command.Parameters.AddWithValue("@Url", url);
                        command.ExecuteNonQuery();
                    }
                    ClearChannelCache();
                }
                catch (Exception ex)
                {
                    LogService.LogError("RemoveM3uSource DB cleanup error", ex);
                }
            }
        }

        public List<string> GetEpgSources()
        {
            string current = GetSetting("epg_sources", "");
            if (string.IsNullOrEmpty(current)) return new List<string>();
            return new List<string>(current.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries));
        }

        public void AddEpgSource(string url)
        {
            var sources = GetEpgSources();
            if (!sources.Contains(url))
            {
                sources.Add(url);
                SetSetting("epg_sources", string.Join("|||", sources));
            }
        }

        public void RemoveEpgSource(string url)
        {
            var sources = GetEpgSources();
            if (sources.Contains(url))
            {
                sources.Remove(url);
                SetSetting("epg_sources", string.Join("|||", sources));
                ClearEpgByUrl(url);
            }
        }
    }
}
