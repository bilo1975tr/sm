using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public partial class DatabaseService
    {
        public void SaveChannel(Channel channel)
        {
            try
            {
                using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO Channels (Id, Name, EpgId, Url, GroupTitle, LogoUrl, SourceType, AddedDate, Category, Language, PlaylistUrl, IsFavorite, IsVerified)
                        VALUES (@Id, @Name, @EpgId, @Url, @GroupTitle, @LogoUrl, @SourceType, @AddedDate, @Category, @Language, @PlaylistUrl, @IsFavorite, @IsVerified)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name=excluded.Name,
                            EpgId=excluded.EpgId,
                            Url=excluded.Url,
                            GroupTitle=excluded.GroupTitle,
                            LogoUrl=excluded.LogoUrl,
                            SourceType=excluded.SourceType,
                            Category=excluded.Category,
                            Language=excluded.Language,
                            PlaylistUrl=excluded.PlaylistUrl,
                            IsFavorite=excluded.IsFavorite,
                            IsVerified=excluded.IsVerified;
                    ";
                    command.Parameters.AddWithValue("@Id", channel.Id);
                    command.Parameters.AddWithValue("@Name", channel.Name ?? string.Empty);
                    command.Parameters.AddWithValue("@EpgId", channel.EpgId ?? string.Empty);
                    command.Parameters.AddWithValue("@Url", channel.Url ?? string.Empty);
                    command.Parameters.AddWithValue("@GroupTitle", channel.GroupTitle ?? string.Empty);
                    command.Parameters.AddWithValue("@LogoUrl", channel.LogoUrl ?? string.Empty);
                    command.Parameters.AddWithValue("@SourceType", channel.SourceType ?? "M3U");
                    command.Parameters.AddWithValue("@AddedDate", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    command.Parameters.AddWithValue("@Category", channel.Category ?? "TV");
                    command.Parameters.AddWithValue("@Language", channel.Language ?? "Bilinmiyor");
                    command.Parameters.AddWithValue("@PlaylistUrl", channel.PlaylistUrl ?? string.Empty);
                    command.Parameters.AddWithValue("@IsFavorite", channel.IsFavorite ? 1 : 0);
                    command.Parameters.AddWithValue("@IsVerified", channel.IsVerified ? 1 : 0);
                    
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"SaveChannel error for {channel.Name}", ex);
            }
        }

        public string SaveChannels(List<Channel> channels, string playlistUrl)
        {
            if (channels == null || channels.Count == 0) return "Hiç kanal bulunamadı.";
            LogService.Log($"Saving {channels.Count} channels to database.");

            int newChannels = 0;
            int existingChannels = 0;

            try
            {
                // URL parçalarını tek tek takip eden bir harita (Map) oluşturuyoruz
                var urlToIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Url, Id FROM Channels";
                    
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string rawUrl = reader.GetString(0);
                            string id = reader.GetString(1);
                            if (!string.IsNullOrEmpty(rawUrl))
                            {
                                foreach(var u in rawUrl.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    string trimmed = u.Trim();
                                    if (!urlToIdMap.ContainsKey(trimmed))
                                        urlToIdMap[trimmed] = id;
                                }
                            }
                        }
                    }

                    var newUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in channels)
                    {
                        if (c.Url != null) 
                        {
                            foreach(var u in c.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                newUrls.Add(u.Trim());
                        }
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        var command = connection.CreateCommand();
                        command.CommandText = @"
                            INSERT INTO Channels (Id, Name, EpgId, Url, GroupTitle, LogoUrl, SourceType, AddedDate, Category, Language, PlaylistUrl, IsFavorite, IsVerified)
                            VALUES (@Id, @Name, @EpgId, @Url, @GroupTitle, @LogoUrl, @SourceType, @AddedDate, @Category, @Language, @PlaylistUrl, @IsFavorite, @IsVerified)
                            ON CONFLICT(Id) DO UPDATE SET
                                Name=excluded.Name,
                                EpgId=excluded.EpgId,
                                Url=excluded.Url,
                                GroupTitle=excluded.GroupTitle,
                                LogoUrl=excluded.LogoUrl,
                                SourceType=excluded.SourceType,
                                Category=excluded.Category,
                                Language=excluded.Language,
                                PlaylistUrl=excluded.PlaylistUrl,
                                IsFavorite=excluded.IsFavorite,
                                IsVerified=excluded.IsVerified;
                        ";

                        var pId = command.Parameters.Add("@Id", SqliteType.Text);
                        var pName = command.Parameters.Add("@Name", SqliteType.Text);
                        var pEpgId = command.Parameters.Add("@EpgId", SqliteType.Text);
                        var pUrl = command.Parameters.Add("@Url", SqliteType.Text);
                        var pGroup = command.Parameters.Add("@GroupTitle", SqliteType.Text);
                        var pLogo = command.Parameters.Add("@LogoUrl", SqliteType.Text);
                        var pSrcType = command.Parameters.Add("@SourceType", SqliteType.Text);
                        var pDate = command.Parameters.Add("@AddedDate", SqliteType.Integer);
                        var pCat = command.Parameters.Add("@Category", SqliteType.Text);
                        var pLang = command.Parameters.Add("@Language", SqliteType.Text);
                        var pPlaylist = command.Parameters.Add("@PlaylistUrl", SqliteType.Text);
                        var pFav = command.Parameters.Add("@IsFavorite", SqliteType.Integer);
                        var pVer = command.Parameters.Add("@IsVerified", SqliteType.Integer);

                        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                        foreach (var channel in channels)
                        {
                            string idToUse = null;
                            if (channel.Url != null)
                            {
                                foreach(var u in channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (urlToIdMap.TryGetValue(u.Trim(), out var existingId))
                                    {
                                        idToUse = existingId;
                                        break;
                                    }
                                }
                            }

                            if (idToUse != null)
                            {
                                existingChannels++;
                            }
                            else
                            {
                                newChannels++;
                                idToUse = channel.Id ?? Guid.NewGuid().ToString("N");
                            }

                            pId.Value = idToUse;
                            pName.Value = channel.Name ?? string.Empty;
                            pEpgId.Value = channel.EpgId ?? string.Empty;
                            pUrl.Value = channel.Url ?? string.Empty;
                            pGroup.Value = channel.GroupTitle ?? string.Empty;
                            pLogo.Value = channel.LogoUrl ?? string.Empty;
                            pSrcType.Value = channel.SourceType ?? "M3U";
                            pDate.Value = now;
                            pCat.Value = channel.Category ?? "TV";
                            pLang.Value = channel.Language ?? "Bilinmiyor";
                            pPlaylist.Value = channel.PlaylistUrl ?? string.Empty;
                            pFav.Value = channel.IsFavorite ? 1 : 0;
                            pVer.Value = channel.IsVerified ? 1 : 0;
                            
                            command.ExecuteNonQuery();
                        }

                        if (!string.IsNullOrEmpty(playlistUrl))
                        {
                            var delCmd = connection.CreateCommand();
                            delCmd.CommandText = "DELETE FROM Channels WHERE PlaylistUrl = @Url AND Url = @DelUrl";
                            delCmd.Parameters.AddWithValue("@Url", playlistUrl);
                            var pDelUrl = delCmd.Parameters.Add("@DelUrl", SqliteType.Text);

                            // Bu kısım artık daha akıllı olabilir ama şimdilik mevcut URL bazlı güvenli silmeyi koruyoruz
                            foreach (var oldUrl in urlToIdMap.Keys)
                            {
                                // Eğer eski URL yeni listede hiç yoksa ve bu playlist'e aitse sil
                                if (!newUrls.Contains(oldUrl))
                                {
                                    pDelUrl.Value = oldUrl;
                                    delCmd.ExecuteNonQuery();
                                }
                            }
                        }

                        transaction.Commit();
                    }
                }
                return $"İşlem tamamlandı.\nToplam: {channels.Count}\nYeni Eklenen: {newChannels}\nMevcut/Güncellenen: {existingChannels}";
            }
            catch (Exception ex)
            {
                LogService.LogError("Bulk SaveChannels error", ex);
                return $"Kayıt sırasında hata oluştu: {ex.Message}";
            }
        }

        public void OptimizeAllChannels()
        {
            try
            {
                var allChannels = GetAllChannels();
                var urlToChannelMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
                var duplicates = new List<(Channel target, Channel source)>();

                foreach (var channel in allChannels)
                {
                    if (string.IsNullOrEmpty(channel.Url)) continue;

                    var urls = channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var u in urls)
                    {
                        var trimmed = u.Trim();
                        if (urlToChannelMap.TryGetValue(trimmed, out var existing))
                        {
                            if (existing.Id != channel.Id)
                            {
                                duplicates.Add((existing, channel));
                                break;
                            }
                        }
                        else
                        {
                            urlToChannelMap[trimmed] = channel;
                        }
                    }
                }

                if (duplicates.Count > 0)
                {
                    LogService.Log($"Found {duplicates.Count} duplicate channels. Merging now...");
                    foreach (var pair in duplicates)
                    {
                        MergeChannels(pair.target.Id, pair.source.Id);
                    }
                }

                // Tekil bazda URL'leri kedi içinde temizle
                using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, Url FROM Channels";
                    var updateCmd = connection.CreateCommand();
                    updateCmd.CommandText = "UPDATE Channels SET Url = @Url WHERE Id = @Id";
                    var pUrl = updateCmd.Parameters.Add("@Url", SqliteType.Text);
                    var pId = updateCmd.Parameters.Add("@Id", SqliteType.Text);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string id = reader.GetString(0);
                            string rawUrl = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            
                            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach(var su in rawUrl.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                set.Add(su.Trim());

                            string cleaned = string.Join(",", set);
                            if (cleaned != rawUrl)
                            {
                                pId.Value = id;
                                pUrl.Value = cleaned;
                                updateCmd.ExecuteNonQuery();
                            }
                        }
                    }
                }
                LogService.Log("Kütüphane optimizasyonu tamamlandı.");
            }
            catch (Exception ex)
            {
                LogService.LogError("OptimizeAllChannels failed", ex);
            }
        }

        public List<Channel> GetAllChannels()
        {
            var channels = new List<Channel>();
            
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite, EpgId, IsVerified, AddedDate FROM Channels ORDER BY AddedDate DESC";
                
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        channels.Add(new Channel
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
                            CreatedAt = reader.IsDBNull(12) ? DateTime.Now : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(12)).DateTime
                        });
                    }
                }
            }
            return channels;
        }

        public List<Channel> GetChannelsByPlaylistUrl(string playlistUrl)
        {
            var channels = new List<Channel>();
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite, EpgId, IsVerified, AddedDate FROM Channels WHERE PlaylistUrl = @Url ORDER BY Name ASC";
                command.Parameters.AddWithValue("@Url", playlistUrl);
                
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        channels.Add(new Channel
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
                            CreatedAt = reader.IsDBNull(12) ? DateTime.Now : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(12)).DateTime
                        });
                    }
                }
            }
            return channels;
        }

        public void BulkUpdateLanguage(List<string> ids, string language)
        {
            if (ids == null || ids.Count == 0) return;

            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE Channels SET Language = @Lang WHERE Id = @Id";
                    var pLang = cmd.Parameters.Add("@Lang", SqliteType.Text);
                    var pId = cmd.Parameters.Add("@Id", SqliteType.Text);

                    pLang.Value = language;
                    foreach (var id in ids)
                    {
                        pId.Value = id;
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        public void DeleteChannel(string id)
        {
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Channels WHERE Id = @Id";
                command.Parameters.AddWithValue("@Id", id);
                command.ExecuteNonQuery();
            }
        }

        public Channel GetChannelById(string id)
        {
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite, EpgId, IsVerified, AddedDate FROM Channels WHERE Id = @Id";
                command.Parameters.AddWithValue("@Id", id);
                
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new Channel
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
                            CreatedAt = reader.IsDBNull(12) ? DateTime.Now : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(12)).DateTime
                        };
                    }
                }
            }
            return null;
        }

        public void MergeChannels(string targetId, string sourceId)
        {
            if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(sourceId) || targetId == sourceId) return;

            try
            {
                var target = GetChannelById(targetId);
                var source = GetChannelById(sourceId);
                
                if (target == null || source == null) return;

                // URL'leri birleştir (virgülle ayrılmış şekilde)
                var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(target.Url))
                {
                    foreach (var u in target.Url.Split(',')) 
                        if (!string.IsNullOrEmpty(u.Trim())) urls.Add(u.Trim());
                }
                if (!string.IsNullOrEmpty(source.Url))
                {
                    foreach (var u in source.Url.Split(',')) 
                        if (!string.IsNullOrEmpty(u.Trim())) urls.Add(u.Trim());
                }
                
                string newCombinedUrl = string.Join(",", urls);

                using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE Channels SET Url = @Url WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Url", newCombinedUrl);
                    cmd.Parameters.AddWithValue("@Id", targetId);
                    cmd.ExecuteNonQuery();
                }

                // Kaynak kanalı sil
                DeleteChannel(sourceId);
                LogService.Log($"Merged channel {source.Name} into {target.Name}. New URL count: {urls.Count}");
            }
            catch (Exception ex)
            {
                LogService.LogError($"MergeChannels error: {sourceId} -> {targetId}", ex);
            }
        }

        public (int total, int verified) GetChannelCountsBySource(string playlistUrl)
        {
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*), SUM(CASE WHEN IsVerified = 1 THEN 1 ELSE 0 END) FROM Channels WHERE PlaylistUrl = @Url";
                command.Parameters.AddWithValue("@Url", playlistUrl);
                
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        int total = reader.GetInt32(0);
                        int verified = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                        return (total, verified);
                    }
                }
            }
            return (0, 0);
        }

        public int GetTotalChannelCount()
        {
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Channels";
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return reader.GetInt32(0);
                    }
                }
            }
            return 0;
        }

        public List<Channel> GetVerifiedChannelsChunk(int offset, int limit)
        {
            var channels = new List<Channel>();
            
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite, EpgId, IsVerified, AddedDate FROM Channels WHERE IsVerified = 1 ORDER BY AddedDate DESC LIMIT @Limit OFFSET @Offset";
                command.Parameters.AddWithValue("@Limit", limit);
                command.Parameters.AddWithValue("@Offset", offset);
                
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        channels.Add(new Channel
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
                            CreatedAt = reader.IsDBNull(12) ? DateTime.Now : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(12)).DateTime
                        });
                    }
                }
            }
            return channels;
        }

        public void ClearAllChannels()
        {
            using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Channels";
                command.ExecuteNonQuery();
            }
            SetSetting("m3u_sources", "");
        }

        public void SyncIncomingP2PChannels(List<Channel> incomingChannels)
        {
            if (incomingChannels == null || incomingChannels.Count == 0) return;

            try
            {
                // To avoid loading massive Channel objects into memory, we only load minimal matching data
                var epgMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var urlMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var existingChannels = new Dictionary<string, Channel>();

                using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    connection.Open();

                    // Step 1: Load lightweight lookup
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Id, Url, EpgId, LogoUrl, IsVerified FROM Channels";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var localId = reader.GetString(0);
                                var localUrl = reader.IsDBNull(1) ? "" : reader.GetString(1);
                                var localEpg = reader.IsDBNull(2) ? "" : reader.GetString(2);
                                var localLogo = reader.IsDBNull(3) ? "" : reader.GetString(3);
                                var localVerified = !reader.IsDBNull(4) && reader.GetInt32(4) == 1;

                                var info = new Channel { Id = localId, Url = localUrl, EpgId = localEpg, LogoUrl = localLogo, IsVerified = localVerified };
                                existingChannels[localId] = info;

                                if (!string.IsNullOrWhiteSpace(localEpg)) epgMap[localEpg.Trim()] = localId;
                                if (!string.IsNullOrWhiteSpace(localUrl))
                                {
                                    foreach (var u in localUrl.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                    {
                                        urlMap[u.Trim()] = localId;
                                    }
                                }
                            }
                        }
                    }

                    // Step 2: Iterate incoming in transaction
                    using (var transaction = connection.BeginTransaction())
                    {
                        var updateCmd = connection.CreateCommand();
                        updateCmd.CommandText = "UPDATE Channels SET Url=@Url, EpgId=@EpgId, LogoUrl=@LogoUrl, IsVerified=@IsVerified WHERE Id=@Id";
                        var pId = updateCmd.Parameters.Add("@Id", SqliteType.Text);
                        var pUrl = updateCmd.Parameters.Add("@Url", SqliteType.Text);
                        var pEpg = updateCmd.Parameters.Add("@EpgId", SqliteType.Text);
                        var pLogo = updateCmd.Parameters.Add("@LogoUrl", SqliteType.Text);
                        var pVer = updateCmd.Parameters.Add("@IsVerified", SqliteType.Integer);

                        var insertCmd = connection.CreateCommand();
                        insertCmd.CommandText = @"INSERT INTO Channels (Id, Name, EpgId, Url, GroupTitle, LogoUrl, SourceType, AddedDate, Category, Language, PlaylistUrl, IsFavorite, IsVerified)
                                                  VALUES (@Id, @Name, @EpgId, @Url, @GroupTitle, @LogoUrl, @SourceType, @AddedDate, @Category, @Language, @PlaylistUrl, 0, @IsVerified)";
                        var iId = insertCmd.Parameters.Add("@Id", SqliteType.Text);
                        var iName = insertCmd.Parameters.Add("@Name", SqliteType.Text);
                        var iEpg = insertCmd.Parameters.Add("@EpgId", SqliteType.Text);
                        var iUrl = insertCmd.Parameters.Add("@Url", SqliteType.Text);
                        var iGrp = insertCmd.Parameters.Add("@GroupTitle", SqliteType.Text);
                        var iLogo = insertCmd.Parameters.Add("@LogoUrl", SqliteType.Text);
                        var iSrc = insertCmd.Parameters.Add("@SourceType", SqliteType.Text);
                        var iDate = insertCmd.Parameters.Add("@AddedDate", SqliteType.Integer);
                        var iCat = insertCmd.Parameters.Add("@Category", SqliteType.Text);
                        var iLang = insertCmd.Parameters.Add("@Language", SqliteType.Text);
                        var iPList = insertCmd.Parameters.Add("@PlaylistUrl", SqliteType.Text);
                        var iVer = insertCmd.Parameters.Add("@IsVerified", SqliteType.Integer);

                        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                        foreach (var c in incomingChannels)
                        {
                            string matchedId = null;

                            if (!string.IsNullOrWhiteSpace(c.EpgId) && epgMap.TryGetValue(c.EpgId.Trim(), out var byEpg))
                                matchedId = byEpg;
                            else if (!string.IsNullOrWhiteSpace(c.Url))
                            {
                                foreach (var u in c.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (urlMap.TryGetValue(u.Trim(), out var byUrl))
                                    {
                                        matchedId = byUrl;
                                        break;
                                    }
                                }
                            }

                            if (matchedId != null)
                            {
                                var existing = existingChannels[matchedId];
                                bool changed = false;

                                var existingUrls = new HashSet<string>(existing.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                                var incomingUrls = (c.Url ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var u in incomingUrls)
                                    if (existingUrls.Add(u.Trim())) changed = true;

                                if (changed) existing.Url = string.Join(",", existingUrls);

                                if (string.IsNullOrWhiteSpace(existing.EpgId) && !string.IsNullOrWhiteSpace(c.EpgId))
                                {
                                    existing.EpgId = c.EpgId;
                                    changed = true;
                                }

                                if (string.IsNullOrWhiteSpace(existing.LogoUrl) && !string.IsNullOrWhiteSpace(c.LogoUrl))
                                {
                                    existing.LogoUrl = c.LogoUrl;
                                    changed = true;
                                }

                                if (!existing.IsVerified && c.IsVerified)
                                {
                                    existing.IsVerified = true;
                                    changed = true;
                                }

                                if (changed)
                                {
                                    pId.Value = existing.Id;
                                    pUrl.Value = existing.Url;
                                    pEpg.Value = existing.EpgId;
                                    pLogo.Value = existing.LogoUrl;
                                    pVer.Value = existing.IsVerified ? 1 : 0;
                                    updateCmd.ExecuteNonQuery();

                                    // Update lookup
                                    if (!string.IsNullOrWhiteSpace(existing.EpgId)) epgMap[existing.EpgId.Trim()] = existing.Id;
                                    foreach (var u in existingUrls) urlMap[u.Trim()] = existing.Id;
                                }
                            }
                            else
                            {
                                // Insert new
                                string newId = c.Id ?? Guid.NewGuid().ToString("N");
                                iId.Value = newId;
                                iName.Value = c.Name ?? string.Empty;
                                iEpg.Value = c.EpgId ?? string.Empty;
                                iUrl.Value = c.Url ?? string.Empty;
                                iGrp.Value = c.GroupTitle ?? string.Empty;
                                iLogo.Value = c.LogoUrl ?? string.Empty;
                                iSrc.Value = c.SourceType ?? "P2P";
                                iDate.Value = now;
                                iCat.Value = c.Category ?? "TV";
                                iLang.Value = c.Language ?? "Bilinmiyor";
                                iPList.Value = c.PlaylistUrl ?? string.Empty;
                                iVer.Value = c.IsVerified ? 1 : 0;
                                insertCmd.ExecuteNonQuery();

                                var newLocal = new Channel { Id = newId, Url = c.Url ?? "", EpgId = c.EpgId ?? "", LogoUrl = c.LogoUrl ?? "", IsVerified = c.IsVerified };
                                existingChannels[newId] = newLocal;
                                if (!string.IsNullOrWhiteSpace(newLocal.EpgId)) epgMap[newLocal.EpgId.Trim()] = newId;
                                foreach (var u in newLocal.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                    urlMap[u.Trim()] = newId;
                            }
                        }

                        transaction.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("SyncIncomingP2PChannels failed", ex);
            }
        }
    }
}
