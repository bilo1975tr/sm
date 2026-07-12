using System;
using Microsoft.Data.Sqlite;

namespace StreamMesh.Services
{
    public partial class DatabaseService
    {
        public void SaveVerificationCache(string channelId, string category, string resolution, bool isWorking)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO VerificationCache (ChannelId, VerifiedAt, Category, Resolution, IsWorking)
                        VALUES (@ChannelId, @VerifiedAt, @Category, @Resolution, @IsWorking)
                        ON CONFLICT(ChannelId) DO UPDATE SET
                            VerifiedAt = @VerifiedAt,
                            Category = @Category,
                            Resolution = @Resolution,
                            IsWorking = @IsWorking;
                    ";
                    command.Parameters.AddWithValue("@ChannelId", channelId);
                    command.Parameters.AddWithValue("@VerifiedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    command.Parameters.AddWithValue("@Category", category ?? string.Empty);
                    command.Parameters.AddWithValue("@Resolution", resolution ?? string.Empty);
                    command.Parameters.AddWithValue("@IsWorking", isWorking ? 1 : 0);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception)
            {
                // Silent
            }
        }

        public (long VerifiedAt, string Category, string Resolution, bool IsWorking)? GetVerificationCache(string channelId)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT VerifiedAt, Category, Resolution, IsWorking FROM VerificationCache WHERE ChannelId = @ChannelId;";
                    command.Parameters.AddWithValue("@ChannelId", channelId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            long verifiedAt = reader.GetInt64(0);
                            string category = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                            string resolution = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                            bool isWorking = reader.GetInt32(3) == 1;
                            return (verifiedAt, category, resolution, isWorking);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silent
            }
            return null;
        }

        public void ClearVerificationCache()
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM VerificationCache;";
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception)
            {
                // Silent
            }
        }
    }
}
