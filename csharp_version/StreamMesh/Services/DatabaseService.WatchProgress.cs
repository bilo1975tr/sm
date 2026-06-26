using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public partial class DatabaseService
    {
        public void SaveWatchProgress(string channelId, string title, long seconds, long duration)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO WatchProgress (ChannelId, Title, Seconds, Duration, LastWatched)
                        VALUES ($channelId, $title, $seconds, $duration, $lastWatched)
                        ON CONFLICT(ChannelId) DO UPDATE SET
                            Title = EXCLUDED.Title,
                            Seconds = EXCLUDED.Seconds,
                            Duration = EXCLUDED.Duration,
                            LastWatched = EXCLUDED.LastWatched;
                    ";
                    command.Parameters.AddWithValue("$channelId", channelId ?? string.Empty);
                    command.Parameters.AddWithValue("$title", title ?? string.Empty);
                    command.Parameters.AddWithValue("$seconds", seconds);
                    command.Parameters.AddWithValue("$duration", duration);
                    command.Parameters.AddWithValue("$lastWatched", DateTime.Now.ToString("o"));
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"SaveWatchProgress failed for channel {channelId}", ex);
            }
        }

        public WatchProgress GetWatchProgress(string channelId)
        {
            if (string.IsNullOrEmpty(channelId)) return null;
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT ChannelId, Title, Seconds, Duration, LastWatched FROM WatchProgress WHERE ChannelId = $channelId LIMIT 1;";
                    command.Parameters.AddWithValue("$channelId", channelId);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var wp = new WatchProgress
                            {
                                ChannelId = reader.GetString(0),
                                Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Seconds = reader.GetInt64(2),
                                Duration = reader.GetInt64(3),
                            };
                            if (!reader.IsDBNull(4) && DateTime.TryParse(reader.GetString(4), out DateTime parsedDate))
                            {
                                wp.LastWatched = parsedDate;
                            }
                            return wp;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"GetWatchProgress failed for channel {channelId}", ex);
            }
            return null;
        }

        public Dictionary<string, WatchProgress> GetAllWatchProgress()
        {
            var dict = new Dictionary<string, WatchProgress>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT ChannelId, Title, Seconds, Duration, LastWatched FROM WatchProgress;";
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var wp = new WatchProgress
                            {
                                ChannelId = reader.GetString(0),
                                Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Seconds = reader.GetInt64(2),
                                Duration = reader.GetInt64(3),
                            };
                            if (!reader.IsDBNull(4) && DateTime.TryParse(reader.GetString(4), out DateTime parsedDate))
                            {
                                wp.LastWatched = parsedDate;
                            }
                            dict[wp.ChannelId] = wp;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("GetAllWatchProgress failed", ex);
            }
            return dict;
        }
    }
}
