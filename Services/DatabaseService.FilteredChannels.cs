using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public partial class DatabaseService
    {
        public class FilteredChannelsResult
        {
            public List<Channel> Channels { get; set; } = new List<Channel>();
            public int TotalCount { get; set; }
            public List<string> Categories { get; set; } = new List<string>();
            public List<string> Groups { get; set; } = new List<string>();
            public List<string> SourceTypes { get; set; } = new List<string>();
            public List<string> Languages { get; set; } = new List<string>();
        }

        public void SetFavorite(string id, bool isFavorite)
        {
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "UPDATE Channels SET IsFavorite = @IsFav WHERE Id = @Id";
                    command.Parameters.AddWithValue("@IsFav", isFavorite ? 1 : 0);
                    command.Parameters.AddWithValue("@Id", id);
                    command.ExecuteNonQuery();
                }
                ClearChannelCache();
            }
            catch (Exception ex)
            {
                LogService.LogError($"SetFavorite failed for ID: {id}", ex);
            }
        }

        public FilteredChannelsResult GetFilteredChannels(int page, int pageSize, string search, string category, string group, string sourceType, string language)
        {
            var result = new FilteredChannelsResult();

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                // 1. Get unique filter options (Distinct)
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT Category FROM Channels WHERE Category IS NOT NULL AND Category != '' ORDER BY Category ASC";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read()) result.Categories.Add(r.GetString(0));
                    }
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT GroupTitle FROM Channels WHERE GroupTitle IS NOT NULL AND GroupTitle != '' ORDER BY GroupTitle ASC";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read()) result.Groups.Add(r.GetString(0));
                    }
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT SourceType FROM Channels WHERE SourceType IS NOT NULL AND SourceType != '' ORDER BY SourceType ASC";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read()) result.SourceTypes.Add(r.GetString(0));
                    }
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT Language FROM Channels WHERE Language IS NOT NULL AND Language != '' ORDER BY Language ASC";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read()) result.Languages.Add(r.GetString(0));
                    }
                }

                // 2. Build the WHERE clause for filtered channels
                var whereClauses = new List<string>();
                var parameters = new Dictionary<string, object>();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    whereClauses.Add("(Name LIKE @Search OR GroupTitle LIKE @Search OR Category LIKE @Search OR Language LIKE @Search)");
                    parameters.Add("@Search", $"%{search}%");
                }
                
                if (!string.IsNullOrWhiteSpace(category))
                {
                    if (category.Equals("Fav", StringComparison.OrdinalIgnoreCase))
                    {
                        whereClauses.Add("IsFavorite = 1");
                    }
                    else
                    {
                        whereClauses.Add("Category = @Category");
                        parameters.Add("@Category", category);
                    }
                }
                
                if (!string.IsNullOrWhiteSpace(group))
                {
                    whereClauses.Add("GroupTitle = @Group");
                    parameters.Add("@Group", group);
                }
                if (!string.IsNullOrWhiteSpace(sourceType))
                {
                    whereClauses.Add("SourceType = @SourceType");
                    parameters.Add("@SourceType", sourceType);
                }
                if (!string.IsNullOrWhiteSpace(language))
                {
                    whereClauses.Add("Language = @Language");
                    parameters.Add("@Language", language);
                }

                string whereSection = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

                // 3. Get total count of filtered channels
                using (var countCmd = connection.CreateCommand())
                {
                    countCmd.CommandText = $"SELECT COUNT(*) FROM Channels {whereSection}";
                    foreach (var p in parameters)
                    {
                        countCmd.Parameters.AddWithValue(p.Key, p.Value);
                    }
                    result.TotalCount = Convert.ToInt32(countCmd.ExecuteScalar());
                }

                // 4. Get paginated channels
                int offset = (page - 1) * pageSize;
                using (var dataCmd = connection.CreateCommand())
                {
                    dataCmd.CommandText = $@"
                        SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite, EpgId, IsVerified, AddedDate, EpgUrl, PersonalWatchCount, IsLocked, Notes, IsPremium 
                        FROM Channels 
                        {whereSection} 
                        ORDER BY AddedDate DESC 
                        LIMIT @Limit OFFSET @Offset";

                    foreach (var p in parameters)
                    {
                        dataCmd.Parameters.AddWithValue(p.Key, p.Value);
                    }
                    dataCmd.Parameters.AddWithValue("@Limit", pageSize);
                    dataCmd.Parameters.AddWithValue("@Offset", offset);

                    using (var reader = dataCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Channels.Add(new Channel
                            {
                                Id = reader.GetString(0),
                                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                Url = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                GroupTitle = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                LogoUrl = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                                SourceType = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                                Category = reader.IsDBNull(6) ? "TV" : reader.GetString(6),
                                Language = reader.IsDBNull(7) ? "Bilinmiyor" : reader.GetString(7),
                                PlaylistUrl = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                                IsFavorite = !reader.IsDBNull(9) && reader.GetInt32(9) == 1,
                                EpgId = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                                IsVerified = !reader.IsDBNull(11) && reader.GetInt32(11) == 1,
                                CreatedAt = reader.IsDBNull(12) ? DateTime.Now : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(12)).DateTime,
                                EpgUrl = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                                PersonalWatchCount = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
                                IsLocked = !reader.IsDBNull(15) && reader.GetInt32(15) == 1,
                                Notes = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                                IsPremium = !reader.IsDBNull(17) && reader.GetInt32(17) == 1
                            });
                        }
                    }
                }
            }

            return result;
        }
    }
}
