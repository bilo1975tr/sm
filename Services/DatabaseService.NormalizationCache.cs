using System;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public partial class DatabaseService
    {
        public void EnsureNormalizationCacheTableExists()
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS NormalizationCache (
                            CacheKey TEXT PRIMARY KEY,
                            CacheValue TEXT,
                            ExpiresAt INTEGER
                        );
                    ";
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("EnsureNormalizationCacheTableExists failed", ex);
            }
        }

        public string GetCachedNormalization(string key)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT CacheValue FROM NormalizationCache WHERE CacheKey = @Key AND ExpiresAt > @Now";
                    cmd.Parameters.AddWithValue("@Key", key);
                    cmd.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    var result = cmd.ExecuteScalar();
                    return result?.ToString();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"GetCachedNormalization failed for {key}", ex);
                return null;
            }
        }

        public void SetCachedNormalization(string key, string value, int expireDays = 30)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO NormalizationCache (CacheKey, CacheValue, ExpiresAt)
                        VALUES (@Key, @Value, @ExpiresAt)
                        ON CONFLICT(CacheKey) DO UPDATE SET
                            CacheValue = excluded.CacheValue,
                            ExpiresAt = excluded.ExpiresAt;
                    ";
                    cmd.Parameters.AddWithValue("@Key", key);
                    cmd.Parameters.AddWithValue("@Value", value ?? string.Empty);
                    long expiresAt = DateTimeOffset.UtcNow.AddDays(expireDays).ToUnixTimeSeconds();
                    cmd.Parameters.AddWithValue("@ExpiresAt", expiresAt);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"SetCachedNormalization failed for {key}", ex);
            }
        }

        public void NormalizeExistingUnknownChannels()
        {
            int maxRetries = 5;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (var connection = new SqliteConnection(ConnectionString))
                    {
                        connection.Open();
                        
                        // 1. First, set any 'bilinmitor' or 'Bilinmiyor' to NULL to prepare them
                        using (var updateNullCmd = connection.CreateCommand())
                        {
                            updateNullCmd.CommandText = "UPDATE Channels SET Language = NULL WHERE Language = 'Bilinmiyor' OR Language LIKE '%bilinmi%' OR Language IS NULL;";
                            updateNullCmd.ExecuteNonQuery();
                        }

                        // 2. Fetch all channels where Language is NULL
                        var channelsToUpdate = new System.Collections.Generic.List<Channel>();
                        using (var selectCmd = connection.CreateCommand())
                        {
                            selectCmd.CommandText = "SELECT Id, Name, GroupTitle, PlaylistUrl, Category, Language, LogoUrl FROM Channels WHERE Language IS NULL;";
                            using (var reader = selectCmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    channelsToUpdate.Add(new Channel
                                    {
                                        Id = reader.GetString(0),
                                        Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                        GroupTitle = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                        PlaylistUrl = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                        Category = reader.IsDBNull(4) ? "TV" : reader.GetString(4),
                                        Language = null,
                                        LogoUrl = reader.IsDBNull(6) ? "" : reader.GetString(6)
                                    });
                                }
                            }
                        }

                        if (channelsToUpdate.Count > 0)
                        {
                            LogService.Log($"Found {channelsToUpdate.Count} channels with unknown language. Running Smart Normalization...");
                            using (var transaction = connection.BeginTransaction())
                            {
                                using (var updateCmd = connection.CreateCommand())
                                {
                                    updateCmd.Transaction = transaction;
                                    updateCmd.CommandText = "UPDATE Channels SET Language = @Language, Category = @Category, GroupTitle = @GroupTitle, LogoUrl = @LogoUrl WHERE Id = @Id;";
                                    var pId = updateCmd.Parameters.Add("@Id", SqliteType.Text);
                                    var pLang = updateCmd.Parameters.Add("@Language", SqliteType.Text);
                                    var pCat = updateCmd.Parameters.Add("@Category", SqliteType.Text);
                                    var pGrp = updateCmd.Parameters.Add("@GroupTitle", SqliteType.Text);
                                    var pLogo = updateCmd.Parameters.Add("@LogoUrl", SqliteType.Text);

                                    foreach (var ch in channelsToUpdate)
                                    {
                                        SmartNormalizationEngine.Instance.NormalizeChannel(ch, skipDbCache: true);

                                        pId.Value = ch.Id;
                                        pLang.Value = ch.Language ?? "Bilinmiyor";
                                        pCat.Value = ch.Category ?? "TV";
                                        pGrp.Value = ch.GroupTitle ?? "";
                                        pLogo.Value = ch.LogoUrl ?? "";
                                        updateCmd.ExecuteNonQuery();
                                    }
                                }
                                transaction.Commit();
                            }
                            ClearChannelCache();
                            LogService.Log("Smart Normalization of unknown channels completed successfully.");
                        }
                    }
                    break;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6)
                {
                    if (i == maxRetries - 1)
                    {
                        LogService.LogError("NormalizeExistingUnknownChannels failed (database locked)", ex);
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(1000 * (i + 1));
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("NormalizeExistingUnknownChannels failed", ex);
                    break;
                }
            }
        }
    }
}
