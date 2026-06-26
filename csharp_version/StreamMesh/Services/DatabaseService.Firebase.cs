using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public partial class DatabaseService
    {
        public class PendingFirebasePush
        {
            public int Id { get; set; }
            public string SafeKey { get; set; }
            public string JsonPayload { get; set; }
            public int RetryCount { get; set; }
            public string CreatedAt { get; set; }
            public string LastAttemptAt { get; set; }
            public string Status { get; set; }
        }

        public void AddPendingFirebasePush(string safeKey, string jsonPayload)
        {
            if (string.IsNullOrEmpty(safeKey) || string.IsNullOrEmpty(jsonPayload)) return;

            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO PendingFirebasePushes (SafeKey, JsonPayload, RetryCount, CreatedAt, LastAttemptAt, Status)
                        VALUES (@SafeKey, @JsonPayload, 0, @CreatedAt, @LastAttemptAt, 'pending')
                        ON CONFLICT(SafeKey) DO UPDATE SET
                            JsonPayload = excluded.JsonPayload,
                            RetryCount = 0,
                            Status = 'pending',
                            LastAttemptAt = excluded.LastAttemptAt;
                    ";
                    cmd.Parameters.AddWithValue("@SafeKey", safeKey);
                    cmd.Parameters.AddWithValue("@JsonPayload", jsonPayload);
                    cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@LastAttemptAt", DateTime.UtcNow.ToString("o"));
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"AddPendingFirebasePush error for key {safeKey}", ex);
            }
        }

        public List<PendingFirebasePush> GetPendingFirebasePushes(int limit)
        {
            var list = new List<PendingFirebasePush>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        SELECT Id, SafeKey, JsonPayload, RetryCount, CreatedAt, LastAttemptAt, Status
                        FROM PendingFirebasePushes
                        WHERE Status = 'pending' OR Status IS NULL
                        ORDER BY Id ASC
                        LIMIT @Limit
                    ";
                    cmd.Parameters.AddWithValue("@Limit", limit);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new PendingFirebasePush
                            {
                                Id = reader.GetInt32(0),
                                SafeKey = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                JsonPayload = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                RetryCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                CreatedAt = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                                LastAttemptAt = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                                Status = reader.IsDBNull(6) ? "pending" : reader.GetString(6)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("GetPendingFirebasePushes error", ex);
            }
            return list;
        }

        public void UpdatePendingFirebasePushAttempt(int id, int retryCount)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        UPDATE PendingFirebasePushes
                        SET RetryCount = @RetryCount, LastAttemptAt = @LastAttemptAt
                        WHERE Id = @Id
                    ";
                    cmd.Parameters.AddWithValue("@RetryCount", retryCount);
                    cmd.Parameters.AddWithValue("@LastAttemptAt", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"UpdatePendingFirebasePushAttempt error for ID {id}", ex);
            }
        }

        public void UpdatePendingFirebasePushStatus(int id, string status)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        UPDATE PendingFirebasePushes
                        SET Status = @Status, LastAttemptAt = @LastAttemptAt
                        WHERE Id = @Id
                    ";
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@LastAttemptAt", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"UpdatePendingFirebasePushStatus error for ID {id}", ex);
            }
        }

        public void DeletePendingFirebasePush(int id)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM PendingFirebasePushes WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"DeletePendingFirebasePush error for ID {id}", ex);
            }
        }

        public void DeletePendingFirebasePushes(List<int> ids)
        {
            if (ids == null || ids.Count == 0) return;
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        var cmd = connection.CreateCommand();
                        cmd.CommandText = "DELETE FROM PendingFirebasePushes WHERE Id = @Id";
                        var pId = cmd.Parameters.Add("@Id", SqliteType.Integer);

                        foreach (var id in ids)
                        {
                            pId.Value = id;
                            cmd.ExecuteNonQuery();
                        }
                        transaction.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("DeletePendingFirebasePushes bulk error", ex);
            }
        }

        public int GetPendingFirebasePushesCount()
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM PendingFirebasePushes WHERE Status = 'pending' OR Status IS NULL";
                    var result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : 0;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("GetPendingFirebasePushesCount error", ex);
                return 0;
            }
        }
    }
}
