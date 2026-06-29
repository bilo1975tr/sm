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
            if (channel == null) return;
            SmartNormalizationEngine.Instance.NormalizeChannel(channel);
            
            // Smart Single Channel Merging
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    
                    // Check if this channel ID already exists in the database
                    bool idExists = false;
                    using (var checkIdCmd = connection.CreateCommand())
                    {
                        checkIdCmd.CommandText = "SELECT COUNT(*) FROM Channels WHERE Id = @Id";
                        checkIdCmd.Parameters.AddWithValue("@Id", channel.Id);
                        idExists = Convert.ToInt32(checkIdCmd.ExecuteScalar()) > 0;
                    }

                    // If it is a new channel being added, look for existing channels with the same normalized name and language
                    if (!idExists && !string.IsNullOrEmpty(channel.Name))
                    {
                        string normName = NormalizeChannelName(channel.Name);
                        channel.Language = Channel.NormalizeLanguage(channel.Language);

                        using (var findCmd = connection.CreateCommand())
                        {
                            findCmd.CommandText = "SELECT Id, Name, Url, Language, EpgId, EpgUrl, LogoUrl, Category, GroupTitle, IsFavorite, IsVerified FROM Channels";
                            using (var reader = findCmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string dbId = reader.GetString(0);
                                    string dbName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                                    string dbUrl = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                                    string dbLang = reader.IsDBNull(3) ? "Bilinmiyor" : reader.GetString(3);

                                    if (NormalizeChannelName(dbName) == normName && Channel.NormalizeLanguage(dbLang) == channel.Language)
                                    {
                                        // Merge urls
                                        var mergedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                        if (!string.IsNullOrEmpty(dbUrl))
                                        {
                                            foreach (var u in dbUrl.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                                mergedUrls.Add(u.Trim());
                                        }
                                        if (!string.IsNullOrEmpty(channel.Url))
                                        {
                                            foreach (var u in channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                                mergedUrls.Add(u.Trim());
                                        }

                                        // Reuse existing channel ID to trigger an UPDATE on conflict / overwrite
                                        channel.Id = dbId;
                                        channel.Url = string.Join(",", mergedUrls);

                                        // Merge attributes safely
                                        if (string.IsNullOrEmpty(channel.EpgId))
                                            channel.EpgId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                                        if (string.IsNullOrEmpty(channel.EpgUrl))
                                            channel.EpgUrl = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                                        if (string.IsNullOrEmpty(channel.LogoUrl))
                                            channel.LogoUrl = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
                                        if (string.IsNullOrEmpty(channel.Category))
                                            channel.Category = reader.IsDBNull(7) ? "TV" : reader.GetString(7);
                                        if (string.IsNullOrEmpty(channel.GroupTitle))
                                            channel.GroupTitle = reader.IsDBNull(8) ? "Genel" : reader.GetString(8);
                                        
                                        bool dbIsFavorite = !reader.IsDBNull(9) && reader.GetInt32(9) == 1;
                                        bool dbIsVerified = !reader.IsDBNull(10) && reader.GetInt32(10) == 1;
                                        
                                        if (dbIsFavorite) channel.IsFavorite = true;
                                        if (dbIsVerified) channel.IsVerified = true;
                                        
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"SaveChannel pre-merge failed for {channel.Name}", ex);
            }

            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO Channels (Id, Name, EpgId, EpgUrl, Url, GroupTitle, LogoUrl, SourceType, AddedDate, Category, Language, PlaylistUrl, IsFavorite, IsVerified, IsLocked, Notes, IsPremium)
                        VALUES (@Id, @Name, @EpgId, @EpgUrl, @Url, @GroupTitle, @LogoUrl, @SourceType, @AddedDate, @Category, @Language, @PlaylistUrl, @IsFavorite, @IsVerified, @IsLocked, @Notes, @IsPremium)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name=excluded.Name,
                            EpgId=excluded.EpgId,
                            EpgUrl=excluded.EpgUrl,
                            Url=excluded.Url,
                            GroupTitle=excluded.GroupTitle,
                            LogoUrl=excluded.LogoUrl,
                            SourceType=excluded.SourceType,
                            Category=excluded.Category,
                            Language=excluded.Language,
                            PlaylistUrl=excluded.PlaylistUrl,
                            IsFavorite=excluded.IsFavorite,
                            IsVerified=excluded.IsVerified,
                            IsLocked=excluded.IsLocked,
                            Notes=excluded.Notes,
                            IsPremium=excluded.IsPremium;
                    ";
                    command.Parameters.AddWithValue("@Id", channel.Id);
                    command.Parameters.AddWithValue("@Name", channel.Name ?? string.Empty);
                    command.Parameters.AddWithValue("@EpgId", channel.EpgId ?? string.Empty);
                    command.Parameters.AddWithValue("@EpgUrl", channel.EpgUrl ?? string.Empty);
                    command.Parameters.AddWithValue("@Url", channel.Url ?? string.Empty);
                    command.Parameters.AddWithValue("@GroupTitle", channel.GroupTitle ?? string.Empty);
                    command.Parameters.AddWithValue("@LogoUrl", channel.LogoUrl ?? string.Empty);
                    command.Parameters.AddWithValue("@SourceType", channel.SourceType ?? "M3U");
                    command.Parameters.AddWithValue("@AddedDate", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    command.Parameters.AddWithValue("@Category", channel.Category ?? "TV");
                    channel.Language = Channel.NormalizeLanguage(channel.Language);
                    command.Parameters.AddWithValue("@Language", channel.Language);
                    command.Parameters.AddWithValue("@PlaylistUrl", channel.PlaylistUrl ?? string.Empty);
                    command.Parameters.AddWithValue("@IsFavorite", channel.IsFavorite ? 1 : 0);
                    command.Parameters.AddWithValue("@IsVerified", channel.IsVerified ? 1 : 0);
                    command.Parameters.AddWithValue("@IsLocked", channel.IsLocked ? 1 : 0);
                    command.Parameters.AddWithValue("@Notes", channel.Notes ?? string.Empty);
                    command.Parameters.AddWithValue("@IsPremium", channel.IsPremium ? 1 : 0);
                    
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"SaveChannel error for {channel.Name}", ex);
            }
        }

        private static string NormalizeChannelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            
            // Standardize case using Turkish culture
            string lower = name.ToLower(new System.Globalization.CultureInfo("tr-TR")).Trim();
            
            // Keep only alphanumeric characters (removes spaces and special characters)
            var sb = new System.Text.StringBuilder();
            foreach (char c in lower)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public string SaveChannels(List<Channel> channels, string playlistUrl)
        {
            if (channels == null || channels.Count == 0) return "Hiç kanal bulunamadı.";
            LogService.Log($"Saving {channels.Count} channels to database.");

            int newChannels = 0;
            int existingChannels = 0;

            try
            {
                var idToChannelMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
                var urlToChannelMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
                var nameAndLangToChannelMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);

                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, Name, Language, Url, EpgId, EpgUrl, LogoUrl, Category, GroupTitle, IsFavorite, IsVerified FROM Channels";
                    
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var dbCh = new Channel
                            {
                                Id = reader.GetString(0),
                                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                Language = reader.IsDBNull(2) ? "Bilinmiyor" : reader.GetString(2),
                                Url = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                EpgId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                                EpgUrl = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                                LogoUrl = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                                Category = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                                GroupTitle = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                                IsFavorite = !reader.IsDBNull(9) && reader.GetInt32(9) == 1,
                                IsVerified = !reader.IsDBNull(10) && reader.GetInt32(10) == 1
                            };

                            dbCh.Language = Channel.NormalizeLanguage(dbCh.Language);

                            if (!idToChannelMap.ContainsKey(dbCh.Id))
                                idToChannelMap[dbCh.Id] = dbCh;

                            if (!string.IsNullOrEmpty(dbCh.Url))
                            {
                                foreach (var u in dbCh.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    string trimmedUrl = u.Trim();
                                    if (!urlToChannelMap.ContainsKey(trimmedUrl))
                                        urlToChannelMap[trimmedUrl] = dbCh;
                                }
                            }

                            if (!string.IsNullOrEmpty(dbCh.Name))
                            {
                                string key = $"{NormalizeChannelName(dbCh.Name)}|{dbCh.Language}";
                                if (!nameAndLangToChannelMap.ContainsKey(key))
                                    nameAndLangToChannelMap[key] = dbCh;
                            }
                        }
                    }

                    var newUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    using (var transaction = connection.BeginTransaction())
                    {
                        var command = connection.CreateCommand();
                        command.CommandText = @"
                            INSERT INTO Channels (Id, Name, EpgId, EpgUrl, Url, GroupTitle, LogoUrl, SourceType, AddedDate, Category, Language, PlaylistUrl, IsFavorite, IsVerified, IsLocked, Notes)
                            VALUES (@Id, @Name, @EpgId, @EpgUrl, @Url, @GroupTitle, @LogoUrl, @SourceType, @AddedDate, @Category, @Language, @PlaylistUrl, @IsFavorite, @IsVerified, 0, '')
                            ON CONFLICT(Id) DO UPDATE SET
                                Name=excluded.Name,
                                EpgId=excluded.EpgId,
                                EpgUrl=excluded.EpgUrl,
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
                        var pEpgUrl = command.Parameters.Add("@EpgUrl", SqliteType.Text);
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
                            SmartNormalizationEngine.Instance.NormalizeChannel(channel);

                            // Ölü link filtreleme
                            if (!string.IsNullOrEmpty(channel.Url))
                            {
                                var parts = channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                var aliveParts = new List<string>();
                                foreach (var p in parts)
                                {
                                    if (!IsLinkDead(p))
                                    {
                                        aliveParts.Add(p.Trim());
                                        newUrls.Add(p.Trim());
                                    }
                                }
                                if (aliveParts.Count == 0) continue; // Tüm linkler ölü ise ekleme
                                channel.Url = string.Join(",", aliveParts);
                            }

                            // Match search
                            Channel matchedChannel = null;

                            // 1. Try URL matching
                            if (!string.IsNullOrEmpty(channel.Url))
                            {
                                foreach (var u in channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (urlToChannelMap.TryGetValue(u.Trim(), out var existingByUrl))
                                    {
                                        matchedChannel = existingByUrl;
                                        break;
                                    }
                                }
                            }

                            // 2. Try Name + Language matching if no URL match
                            if (matchedChannel == null && !string.IsNullOrEmpty(channel.Name))
                            {
                                string normName = NormalizeChannelName(channel.Name);
                                string key = $"{normName}|{channel.Language}";
                                if (nameAndLangToChannelMap.TryGetValue(key, out var existingByNameAndLang))
                                {
                                    matchedChannel = existingByNameAndLang;
                                }
                            }

                            Channel finalChannel = null;

                            if (matchedChannel != null)
                            {
                                existingChannels++;
                                finalChannel = matchedChannel;

                                // Merge fields!
                                // Merge URLs
                                var mergedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                if (!string.IsNullOrEmpty(finalChannel.Url))
                                {
                                    foreach (var u in finalChannel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                        mergedUrls.Add(u.Trim());
                                }
                                if (!string.IsNullOrEmpty(channel.Url))
                                {
                                    foreach (var u in channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                        mergedUrls.Add(u.Trim());
                                }
                                finalChannel.Url = string.Join(",", mergedUrls);

                                // EPG merging is disabled per rules - only assign if target lacks it
                                if (string.IsNullOrEmpty(finalChannel.EpgId))
                                    finalChannel.EpgId = channel.EpgId;
                                if (string.IsNullOrEmpty(finalChannel.EpgUrl))
                                    finalChannel.EpgUrl = channel.EpgUrl;

                                // Logo merging is disabled per rules - only assign if target lacks it
                                if (string.IsNullOrEmpty(finalChannel.LogoUrl))
                                    finalChannel.LogoUrl = channel.LogoUrl;

                                // Merge Category/GroupTitle if target lacks them
                                if (string.IsNullOrEmpty(finalChannel.Category))
                                    finalChannel.Category = channel.Category;
                                if (string.IsNullOrEmpty(finalChannel.GroupTitle))
                                    finalChannel.GroupTitle = channel.GroupTitle;

                                // Merge flags
                                if (channel.IsFavorite) finalChannel.IsFavorite = true;
                                if (channel.IsVerified) finalChannel.IsVerified = true;
                            }
                            else
                            {
                                newChannels++;
                                finalChannel = new Channel
                                {
                                    Id = channel.Id ?? Guid.NewGuid().ToString("N"),
                                    Name = channel.Name,
                                    Language = channel.Language,
                                    Url = channel.Url,
                                    EpgId = channel.EpgId,
                                    EpgUrl = channel.EpgUrl,
                                    LogoUrl = channel.LogoUrl,
                                    Category = channel.Category,
                                    GroupTitle = channel.GroupTitle,
                                    PlaylistUrl = channel.PlaylistUrl,
                                    IsFavorite = channel.IsFavorite,
                                    IsVerified = channel.IsVerified
                                };

                                idToChannelMap[finalChannel.Id] = finalChannel;

                                if (!string.IsNullOrEmpty(finalChannel.Name))
                                {
                                    string key = $"{NormalizeChannelName(finalChannel.Name)}|{finalChannel.Language}";
                                    if (!nameAndLangToChannelMap.ContainsKey(key))
                                        nameAndLangToChannelMap[key] = finalChannel;
                                }
                            }

                            // Always update urlToChannelMap with all current/new URLs
                            if (!string.IsNullOrEmpty(finalChannel.Url))
                            {
                                foreach (var u in finalChannel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    urlToChannelMap[u.Trim()] = finalChannel;
                                }
                            }

                            pId.Value = finalChannel.Id;
                            pName.Value = finalChannel.Name ?? string.Empty;
                            pEpgId.Value = finalChannel.EpgId ?? string.Empty;
                            pEpgUrl.Value = finalChannel.EpgUrl ?? string.Empty;
                            pUrl.Value = finalChannel.Url ?? string.Empty;
                            pGroup.Value = finalChannel.GroupTitle ?? string.Empty;
                            pLogo.Value = finalChannel.LogoUrl ?? string.Empty;
                            pSrcType.Value = finalChannel.SourceType ?? "M3U";
                            pDate.Value = now;
                            pCat.Value = finalChannel.Category ?? "TV";
                            pLang.Value = finalChannel.Language;
                            pPlaylist.Value = finalChannel.PlaylistUrl ?? string.Empty;
                            pFav.Value = finalChannel.IsFavorite ? 1 : 0;
                            pVer.Value = finalChannel.IsVerified ? 1 : 0;

                            command.ExecuteNonQuery();
                        }

                        if (!string.IsNullOrEmpty(playlistUrl))
                        {
                            // Fetch current channel IDs and their raw URLs from this playlist URL in database
                            var playlistChannelsInDb = new Dictionary<string, string>();
                            var getCmd = connection.CreateCommand();
                            getCmd.CommandText = "SELECT Id, Url FROM Channels WHERE PlaylistUrl = @Url";
                            getCmd.Parameters.AddWithValue("@Url", playlistUrl);
                            using (var reader = getCmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string id = reader.GetString(0);
                                    string url = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                                    playlistChannelsInDb[id] = url;
                                }
                            }

                            var delCmd = connection.CreateCommand();
                            delCmd.CommandText = "DELETE FROM Channels WHERE Id = @Id AND IsLocked = 0";
                            var pDelId = delCmd.Parameters.Add("@Id", SqliteType.Text);

                            foreach (var kvp in playlistChannelsInDb)
                            {
                                string channelId = kvp.Key;
                                string rawUrl = kvp.Value;
                                
                                bool hasAnyActiveUrl = false;
                                if (!string.IsNullOrEmpty(rawUrl))
                                {
                                    foreach (var u in rawUrl.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                    {
                                        if (newUrls.Contains(u.Trim()))
                                        {
                                            hasAnyActiveUrl = true;
                                            break;
                                        }
                                    }
                                }

                                if (!hasAnyActiveUrl)
                                {
                                    pDelId.Value = channelId;
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
                using (var connection = new SqliteConnection(ConnectionString))
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
            
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite, EpgId, IsVerified, AddedDate, EpgUrl, PersonalWatchCount, IsLocked, Notes, IsPremium FROM Channels ORDER BY AddedDate DESC";
                
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
            return channels;
        }

        public List<Channel> GetChannelsByPlaylistUrl(string playlistUrl)
        {
            var channels = new List<Channel>();
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite, EpgId, IsVerified, AddedDate, EpgUrl, PersonalWatchCount, IsLocked, Notes, IsPremium FROM Channels WHERE PlaylistUrl = @Url ORDER BY Name ASC";
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
            return channels;
        }

        public void BulkUpdateLanguage(List<string> ids, string language)
        {
            if (ids == null || ids.Count == 0) return;

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE Channels SET Language = @Lang WHERE Id = @Id";
                    var pLang = cmd.Parameters.Add("@Lang", SqliteType.Text);
                    var pId = cmd.Parameters.Add("@Id", SqliteType.Text);

                    pLang.Value = Channel.NormalizeLanguage(language);
                    foreach (var id in ids)
                    {
                        pId.Value = id;
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        public void DeleteChannel(string id, bool force = false, bool isMerge = false)
        {
            if (!isMerge)
            {
                try
                {
                    var ch = GetChannelById(id);
                    if (ch != null && !string.IsNullOrEmpty(ch.Url))
                    {
                        foreach (var u in ch.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            AddDeadLink(u.Trim());
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError($"DeleteChannel URL logging failed for ID: {id}", ex);
                }
            }

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                if (force)
                {
                    command.CommandText = "DELETE FROM Channels WHERE Id = @Id";
                }
                else
                {
                    command.CommandText = "DELETE FROM Channels WHERE Id = @Id AND IsLocked = 0";
                }
                command.Parameters.AddWithValue("@Id", id);
                command.ExecuteNonQuery();
            }
        }

        public Channel GetChannelById(string id)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite, EpgId, IsVerified, AddedDate, EpgUrl, PersonalWatchCount, IsLocked, Notes, IsPremium FROM Channels WHERE Id = @Id";
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
                            CreatedAt = reader.IsDBNull(12) ? DateTime.Now : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(12)).DateTime,
                            EpgUrl = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                            PersonalWatchCount = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
                            IsLocked = !reader.IsDBNull(15) && reader.GetInt32(15) == 1,
                            Notes = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                            IsPremium = !reader.IsDBNull(17) && reader.GetInt32(17) == 1
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

                var epgIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(target.EpgId)) { foreach (var u in target.EpgId.Split(',')) if (!string.IsNullOrEmpty(u.Trim())) epgIds.Add(u.Trim()); }
                if (!string.IsNullOrEmpty(source.EpgId)) { foreach (var u in source.EpgId.Split(',')) if (!string.IsNullOrEmpty(u.Trim())) epgIds.Add(u.Trim()); }
                
                var epgUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(target.EpgUrl)) { foreach (var u in target.EpgUrl.Split(',')) if (!string.IsNullOrEmpty(u.Trim())) epgUrls.Add(u.Trim()); }
                if (!string.IsNullOrEmpty(source.EpgUrl)) { foreach (var u in source.EpgUrl.Split(',')) if (!string.IsNullOrEmpty(u.Trim())) epgUrls.Add(u.Trim()); }

                var logoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(target.LogoUrl)) { foreach (var u in target.LogoUrl.Split(',')) if (!string.IsNullOrEmpty(u.Trim())) logoUrls.Add(u.Trim()); }
                if (!string.IsNullOrEmpty(source.LogoUrl)) { foreach (var u in source.LogoUrl.Split(',')) if (!string.IsNullOrEmpty(u.Trim())) logoUrls.Add(u.Trim()); }

                string newCombinedEpgId = string.Join(",", epgIds);
                string newCombinedEpgUrl = string.Join(",", epgUrls);
                string newCombinedLogoUrl = string.Join(",", logoUrls);

                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE Channels SET Url = @Url, EpgId = @EpgId, EpgUrl = @EpgUrl, LogoUrl = @LogoUrl WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Url", newCombinedUrl);
                    cmd.Parameters.AddWithValue("@EpgId", newCombinedEpgId);
                    cmd.Parameters.AddWithValue("@EpgUrl", newCombinedEpgUrl);
                    cmd.Parameters.AddWithValue("@LogoUrl", newCombinedLogoUrl);
                    cmd.Parameters.AddWithValue("@Id", targetId);
                    cmd.ExecuteNonQuery();
                }

                // Kaynak kanalı sil
                DeleteChannel(sourceId, force: false, isMerge: true);
                LogService.Log($"Merged channel {source.Name} into {target.Name}. New URL count: {urls.Count}");
            }
            catch (Exception ex)
            {
                LogService.LogError($"MergeChannels error: {sourceId} -> {targetId}", ex);
            }
        }

        public (int total, int verified) GetChannelCountsBySource(string playlistUrl)
        {
            using (var connection = new SqliteConnection(ConnectionString))
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
            using (var connection = new SqliteConnection(ConnectionString))
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
            
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite, EpgId, IsVerified, AddedDate, EpgUrl, PersonalWatchCount, IsLocked, Notes, IsPremium FROM Channels WHERE IsVerified = 1 ORDER BY AddedDate DESC LIMIT @Limit OFFSET @Offset";
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
            return channels;
        }

        public void IncrementPersonalWatchCount(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "UPDATE Channels SET PersonalWatchCount = COALESCE(PersonalWatchCount, 0) + 1 WHERE Id = @Id";
                    command.Parameters.AddWithValue("@Id", id);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"IncrementPersonalWatchCount error: {id}", ex);
            }
        }

        public void ClearAllChannels()
        {
            using (var connection = new SqliteConnection(ConnectionString))
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

                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();

                    // Step 1: Load lightweight lookup
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Id, Url, EpgId, LogoUrl, IsVerified, EpgUrl FROM Channels";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var localId = reader.GetString(0);
                                var localUrl = reader.IsDBNull(1) ? "" : reader.GetString(1);
                                var localEpg = reader.IsDBNull(2) ? "" : reader.GetString(2);
                                var localLogo = reader.IsDBNull(3) ? "" : reader.GetString(3);
                                var localVerified = !reader.IsDBNull(4) && reader.GetInt32(4) == 1;
                                var localEpgUrl = reader.IsDBNull(5) ? "" : reader.GetString(5);

                                var info = new Channel { Id = localId, Url = localUrl, EpgId = localEpg, LogoUrl = localLogo, IsVerified = localVerified, EpgUrl = localEpgUrl };
                                existingChannels[localId] = info;

                                if (!string.IsNullOrWhiteSpace(localEpg)) {
                                    foreach(var u in localEpg.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) epgMap[u.Trim()] = localId;
                                }
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
                        updateCmd.CommandText = "UPDATE Channels SET Url=@Url, EpgId=@EpgId, LogoUrl=@LogoUrl, IsVerified=@IsVerified, EpgUrl=@EpgUrl WHERE Id=@Id";
                        var pId = updateCmd.Parameters.Add("@Id", SqliteType.Text);
                        var pUrl = updateCmd.Parameters.Add("@Url", SqliteType.Text);
                        var pEpg = updateCmd.Parameters.Add("@EpgId", SqliteType.Text);
                        var pLogo = updateCmd.Parameters.Add("@LogoUrl", SqliteType.Text);
                        var pVer = updateCmd.Parameters.Add("@IsVerified", SqliteType.Integer);
                        var pEpgUrl = updateCmd.Parameters.Add("@EpgUrl", SqliteType.Text);

                        var insertCmd = connection.CreateCommand();
                        insertCmd.CommandText = @"INSERT INTO Channels (Id, Name, EpgId, EpgUrl, Url, GroupTitle, LogoUrl, SourceType, AddedDate, Category, Language, PlaylistUrl, IsFavorite, IsVerified)
                                                  VALUES (@Id, @Name, @EpgId, @EpgUrl, @Url, @GroupTitle, @LogoUrl, @SourceType, @AddedDate, @Category, @Language, @PlaylistUrl, 0, @IsVerified)";
                        var iId = insertCmd.Parameters.Add("@Id", SqliteType.Text);
                        var iName = insertCmd.Parameters.Add("@Name", SqliteType.Text);
                        var iEpg = insertCmd.Parameters.Add("@EpgId", SqliteType.Text);
                        var iEpgUrl = insertCmd.Parameters.Add("@EpgUrl", SqliteType.Text);
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

                            if (!string.IsNullOrWhiteSpace(c.EpgId))
                            {
                                foreach (var u in c.EpgId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (epgMap.TryGetValue(u.Trim(), out var byEpg))
                                    {
                                        matchedId = byEpg;
                                        break;
                                    }
                                }
                            }
                            if (matchedId == null && !string.IsNullOrWhiteSpace(c.Url))
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

                                var existingEpgIds = new HashSet<string>((existing.EpgId ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                                var incomingEpgIds = (c.EpgId ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var u in incomingEpgIds)
                                    if (existingEpgIds.Add(u.Trim())) changed = true;

                                var existingLogos = new HashSet<string>((existing.LogoUrl ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                                var incomingLogos = (c.LogoUrl ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var u in incomingLogos)
                                    if (existingLogos.Add(u.Trim())) changed = true;

                                var existingEpgUrls = new HashSet<string>((existing.EpgUrl ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                                var incomingEpgUrls = (c.EpgUrl ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var u in incomingEpgUrls)
                                    if (existingEpgUrls.Add(u.Trim())) changed = true;


                                if (changed) 
                                {
                                    existing.Url = string.Join(",", existingUrls);
                                    existing.EpgId = string.Join(",", existingEpgIds);
                                    existing.LogoUrl = string.Join(",", existingLogos);
                                    existing.EpgUrl = string.Join(",", existingEpgUrls);
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
                                    pEpgUrl.Value = existing.EpgUrl;
                                    pVer.Value = existing.IsVerified ? 1 : 0;
                                    updateCmd.ExecuteNonQuery();

                                    // Update lookup
                                    if (!string.IsNullOrWhiteSpace(existing.EpgId)) {
                                        foreach(var u in existingEpgIds) epgMap[u.Trim()] = existing.Id;
                                    }
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
                                iEpgUrl.Value = c.EpgUrl ?? string.Empty;
                                iUrl.Value = c.Url ?? string.Empty;
                                iGrp.Value = c.GroupTitle ?? string.Empty;
                                iLogo.Value = c.LogoUrl ?? string.Empty;
                                iSrc.Value = c.SourceType ?? "P2P";
                                iDate.Value = now;
                                iCat.Value = c.Category ?? "TV";
                                c.Language = Channel.NormalizeLanguage(c.Language);
                                iLang.Value = c.Language;
                                iPList.Value = c.PlaylistUrl ?? string.Empty;
                                iVer.Value = c.IsVerified ? 1 : 0;
                                insertCmd.ExecuteNonQuery();

                                var newLocal = new Channel { Id = newId, Url = c.Url ?? "", EpgId = c.EpgId ?? "", LogoUrl = c.LogoUrl ?? "", IsVerified = c.IsVerified, EpgUrl = c.EpgUrl ?? "" };
                                existingChannels[newId] = newLocal;
                                if (!string.IsNullOrWhiteSpace(newLocal.EpgId)) {
                                     foreach(var u in newLocal.EpgId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) epgMap[u.Trim()] = newId;
                                }
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

        public void SyncAndCleanPremiumChannels()
        {
            bool hasPremium = false;
            if (StreamMesh.Services.P2P.UserService.CurrentUser != null && 
                StreamMesh.Services.P2P.UserService.CurrentUser.IsPremium && 
                StreamMesh.Services.P2P.UserService.CurrentUser.PremiumExpiry > DateTime.UtcNow)
            {
                hasPremium = true;
            }

            if (!hasPremium)
            {
                try
                {
                    var premiumUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "https://www.youtube.com/watch?v=S08cKk_I-90",
                        "https://www.youtube.com/watch?v=68T9Fsk3_zI",
                        "https://www.youtube.com/watch?v=v=live_ssport",
                        "http://premium.streams.xyz/live/ucl.m3u8",
                        "https://www.youtube.com/watch?v=live_tribun"
                    };

                    var allChannels = GetAllChannels();
                    foreach (var channel in allChannels)
                    {
                        bool hadPremiumUrl = false;
                        var parts = !string.IsNullOrEmpty(channel.Url) 
                            ? channel.Url.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) 
                            : Array.Empty<string>();

                        var remainingParts = new List<string>();
                        foreach (var part in parts)
                        {
                            if (premiumUrls.Contains(part.Trim()))
                            {
                                hadPremiumUrl = true;
                            }
                            else
                            {
                                remainingParts.Add(part.Trim());
                            }
                        }

                        if (channel.IsPremium || hadPremiumUrl)
                        {
                            if (remainingParts.Count == 0)
                            {
                                DeleteChannel(channel.Id);
                            }
                            else
                            {
                                channel.Url = string.Join(",", remainingParts);
                                channel.IsPremium = false;
                                SaveChannel(channel);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("SyncAndCleanPremiumChannels failed", ex);
                }
            }
        }

        public void InsertPremiumChannels()
        {
            var premiumChannels = new List<Channel>
            {
                new Channel
                {
                    Id = "GS_TV_PREMIUM",
                    Name = "🦁 Galatasaray TV HD (Premium)",
                    Url = "https://www.youtube.com/watch?v=S08cKk_I-90",
                    GroupTitle = "Galatasaray Premium",
                    Category = "Premium",
                    Language = "Türkçe",
                    LogoUrl = "https://upload.wikimedia.org/wikipedia/commons/f/f6/Galatasaray_Sports_Club_Logo.svg",
                    SourceType = "YOUTUBE",
                    IsPremium = true,
                    IsVerified = true,
                    Notes = "Galatasaray TV Resmi Canlı Yayını - Ultra HD"
                },
                new Channel
                {
                    Id = "BEIN_SPORTS_PREMIUM",
                    Name = "⚽ beIN Sports Haber (Premium)",
                    Url = "https://www.youtube.com/watch?v=68T9Fsk3_zI",
                    GroupTitle = "Spor Premium",
                    Category = "Premium",
                    Language = "Türkçe",
                    LogoUrl = "https://upload.wikimedia.org/wikipedia/commons/e/e0/BeIN_Sports_logo.svg",
                    SourceType = "YOUTUBE",
                    IsPremium = true,
                    IsVerified = true,
                    Notes = "beIN Sports Haber Canlı Maç & Özet Analizleri"
                },
                new Channel
                {
                    Id = "S_SPORT_PREMIUM",
                    Name = "🏆 S Sport Haber (F1 & Premier League)",
                    Url = "https://www.youtube.com/watch?v=v=live_ssport",
                    GroupTitle = "Spor Premium",
                    Category = "Premium",
                    Language = "Türkçe",
                    LogoUrl = "https://ssportplus.com/wp-content/uploads/2021/04/ssport_logo.png",
                    SourceType = "YOUTUBE",
                    IsPremium = true,
                    IsVerified = true,
                    Notes = "İngiltere Premier Lig ve Formula 1 Özel Canlı Yayınları"
                },
                new Channel
                {
                    Id = "CHAMPIONS_LEAGUE_HD",
                    Name = "🌟 Champions League 4K (Premium)",
                    Url = "http://premium.streams.xyz/live/ucl.m3u8",
                    GroupTitle = "Avrupa Kupaları",
                    Category = "Premium",
                    Language = "Türkçe",
                    LogoUrl = "https://upload.wikimedia.org/wikipedia/en/b/bf/UEFA_Champions_League_logo_2.svg",
                    SourceType = "M3U",
                    IsPremium = true,
                    IsVerified = true,
                    Notes = "UEFA Şampiyonlar Ligi Özel Canlı Yayın Kanalı"
                },
                new Channel
                {
                    Id = "TRIBUN_GS_PREMIUM",
                    Name = "📣 RAMS Park Tribün Canlı Taraftar (Premium)",
                    Url = "https://www.youtube.com/watch?v=live_tribun",
                    GroupTitle = "Galatasaray Premium",
                    Category = "Premium",
                    Language = "Türkçe",
                    LogoUrl = "https://upload.wikimedia.org/wikipedia/commons/f/f6/Galatasaray_Sports_Club_Logo.svg",
                    SourceType = "YOUTUBE",
                    IsPremium = true,
                    IsVerified = true,
                    Notes = "Ali Sami Yen Spor Kompleksi RAMS Park Canlı Yayın"
                }
            };

            foreach (var ch in premiumChannels)
            {
                SaveChannel(ch);
            }
        }

        public void UpdateChannelEpg(string id, string epgId)
        {
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE Channels SET EpgId = @EpgId WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@EpgId", epgId ?? string.Empty);
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"UpdateChannelEpg error: {id}", ex);
            }
        }

        public class VerificationResultBatchItem
        {
            public Channel Channel { get; set; }
            public string Category { get; set; }
            public string Resolution { get; set; }
            public bool IsWorking { get; set; }
            public List<string> DeadUrls { get; set; }
        }

        public void SaveVerificationResultsBatch(List<VerificationResultBatchItem> items)
        {
            if (items == null || items.Count == 0) return;
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        var channelCmd = connection.CreateCommand();
                        channelCmd.Transaction = transaction;
                        channelCmd.CommandText = @"
                            INSERT INTO Channels (Id, Name, EpgId, EpgUrl, Url, GroupTitle, LogoUrl, SourceType, AddedDate, Category, Language, PlaylistUrl, IsFavorite, IsVerified, IsLocked, Notes, IsPremium)
                            VALUES (@Id, @Name, @EpgId, @EpgUrl, @Url, @GroupTitle, @LogoUrl, @SourceType, @AddedDate, @Category, @Language, @PlaylistUrl, @IsFavorite, @IsVerified, @IsLocked, @Notes, @IsPremium)
                            ON CONFLICT(Id) DO UPDATE SET
                                Name=excluded.Name,
                                EpgId=excluded.EpgId,
                                EpgUrl=excluded.EpgUrl,
                                Url=excluded.Url,
                                GroupTitle=excluded.GroupTitle,
                                LogoUrl=excluded.LogoUrl,
                                SourceType=excluded.SourceType,
                                Category=excluded.Category,
                                Language=excluded.Language,
                                PlaylistUrl=excluded.PlaylistUrl,
                                IsFavorite=excluded.IsFavorite,
                                IsVerified=excluded.IsVerified,
                                IsLocked=excluded.IsLocked,
                                Notes=excluded.Notes,
                                IsPremium=excluded.IsPremium;
                        ";
                        var pId = channelCmd.Parameters.Add("@Id", SqliteType.Text);
                        var pName = channelCmd.Parameters.Add("@Name", SqliteType.Text);
                        var pEpgId = channelCmd.Parameters.Add("@EpgId", SqliteType.Text);
                        var pEpgUrl = channelCmd.Parameters.Add("@EpgUrl", SqliteType.Text);
                        var pUrl = channelCmd.Parameters.Add("@Url", SqliteType.Text);
                        var pGroupTitle = channelCmd.Parameters.Add("@GroupTitle", SqliteType.Text);
                        var pLogoUrl = channelCmd.Parameters.Add("@LogoUrl", SqliteType.Text);
                        var pSourceType = channelCmd.Parameters.Add("@SourceType", SqliteType.Text);
                        var pAddedDate = channelCmd.Parameters.Add("@AddedDate", SqliteType.Integer);
                        var pCategory = channelCmd.Parameters.Add("@Category", SqliteType.Text);
                        var pLanguage = channelCmd.Parameters.Add("@Language", SqliteType.Text);
                        var pPlaylistUrl = channelCmd.Parameters.Add("@PlaylistUrl", SqliteType.Text);
                        var pIsFavorite = channelCmd.Parameters.Add("@IsFavorite", SqliteType.Integer);
                        var pIsVerified = channelCmd.Parameters.Add("@IsVerified", SqliteType.Integer);
                        var pIsLocked = channelCmd.Parameters.Add("@IsLocked", SqliteType.Integer);
                        var pNotes = channelCmd.Parameters.Add("@Notes", SqliteType.Text);
                        var pIsPremium = channelCmd.Parameters.Add("@IsPremium", SqliteType.Integer);

                        var cacheCmd = connection.CreateCommand();
                        cacheCmd.Transaction = transaction;
                        cacheCmd.CommandText = @"
                            INSERT INTO VerificationCache (ChannelId, VerifiedAt, Category, Resolution, IsWorking)
                            VALUES (@ChannelId, @VerifiedAt, @Category, @Resolution, @IsWorking)
                            ON CONFLICT(ChannelId) DO UPDATE SET
                                VerifiedAt = @VerifiedAt,
                                Category = @Category,
                                Resolution = @Resolution,
                                IsWorking = @IsWorking;
                        ";
                        var pCacheChannelId = cacheCmd.Parameters.Add("@ChannelId", SqliteType.Text);
                        var pCacheVerifiedAt = cacheCmd.Parameters.Add("@VerifiedAt", SqliteType.Integer);
                        var pCacheCategory = cacheCmd.Parameters.Add("@Category", SqliteType.Text);
                        var pCacheResolution = cacheCmd.Parameters.Add("@Resolution", SqliteType.Text);
                        var pCacheIsWorking = cacheCmd.Parameters.Add("@IsWorking", SqliteType.Integer);

                        var deadLinkCmd = connection.CreateCommand();
                        deadLinkCmd.Transaction = transaction;
                        deadLinkCmd.CommandText = "INSERT OR IGNORE INTO DeadLinkHashes (Hash) VALUES (@Hash);";
                        var pDeadLinkHash = deadLinkCmd.Parameters.Add("@Hash", SqliteType.Integer);

                        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                        foreach (var item in items)
                        {
                            var ch = item.Channel;
                            if (ch == null) continue;

                            SmartNormalizationEngine.Instance.NormalizeChannel(ch);

                            pId.Value = ch.Id;
                            pName.Value = ch.Name ?? string.Empty;
                            pEpgId.Value = ch.EpgId ?? string.Empty;
                            pEpgUrl.Value = ch.EpgUrl ?? string.Empty;
                            pUrl.Value = ch.Url ?? string.Empty;
                            pGroupTitle.Value = ch.GroupTitle ?? string.Empty;
                            pLogoUrl.Value = ch.LogoUrl ?? string.Empty;
                            pSourceType.Value = ch.SourceType ?? "M3U";
                            pAddedDate.Value = now;
                            pCategory.Value = ch.Category ?? "TV";
                            ch.Language = Channel.NormalizeLanguage(ch.Language);
                            pLanguage.Value = ch.Language;
                            pPlaylistUrl.Value = ch.PlaylistUrl ?? string.Empty;
                            pIsFavorite.Value = ch.IsFavorite ? 1 : 0;
                            pIsVerified.Value = ch.IsVerified ? 1 : 0;
                            pIsLocked.Value = ch.IsLocked ? 1 : 0;
                            pNotes.Value = ch.Notes ?? string.Empty;
                            pIsPremium.Value = ch.IsPremium ? 1 : 0;

                            channelCmd.ExecuteNonQuery();

                            pCacheChannelId.Value = ch.Id;
                            pCacheVerifiedAt.Value = now;
                            pCacheCategory.Value = item.Category ?? string.Empty;
                            pCacheResolution.Value = item.Resolution ?? string.Empty;
                            pCacheIsWorking.Value = item.IsWorking ? 1 : 0;

                            cacheCmd.ExecuteNonQuery();

                            if (item.DeadUrls != null && item.DeadUrls.Count > 0)
                            {
                                foreach (var failedUrl in item.DeadUrls)
                                {
                                    if (string.IsNullOrWhiteSpace(failedUrl)) continue;
                                    pDeadLinkHash.Value = GetFnv1aHash(failedUrl.Trim());
                                    deadLinkCmd.ExecuteNonQuery();
                                }
                            }
                        }

                        transaction.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("SaveVerificationResultsBatch error", ex);
            }
        }
    }
}
