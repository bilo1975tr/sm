using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;
using StreamMesh.Core.Utils;
using StreamMesh.Core.Media;

namespace StreamMesh.Core.Database.Repositories
{
    public class ChannelRepository
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _dbLock;

        public ChannelRepository(string connectionString, SemaphoreSlim dbLock)
        {
            _connectionString = connectionString;
            _dbLock = dbLock;
        }

        public async Task<List<Channel>> GetAllChannelsAsync()
        {
            var list = new List<Channel>();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredUrlIndex, PreferredLogoIndex, PreferredEpgIndex, IsWatched, IsVerified, LastPositionMs, IsEpgLocked FROM Channels ORDER BY PersonalWatchCount DESC, AddedDate DESC";

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        try
                        {
                            var ch = MapReaderToChannel(reader);
                            list.Add(ch);
                        }
                        catch (Exception innerEx)
                        {
                            LogService.LogError("ChannelRepository: Error mapping single channel row", innerEx);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("ChannelRepository.GetAllChannelsAsync failed", ex);
            }
            return list;
        }

        public async Task SaveChannelAsync(Channel ch)
        {
            await _dbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO Channels (Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredUrlIndex, PreferredLogoIndex, PreferredEpgIndex, IsWatched, IsVerified, LastPositionMs, IsEpgLocked) VALUES (@Id, @Name, @Url, @Logo, @Group, @Cat, @Lang, @Fav, @Date, @Src, @Playlist, @Imdb, @Overview, @Backdrop, @Cast, @Pwc, @Vc, @EpgId, @EpgUrl, @Us, @Pni, @Pui, @Pli, @Pei, @Watched, @Verified, @Lp, @EpgL) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Url=excluded.Url, LogoUrl=excluded.LogoUrl, GroupTitle=excluded.GroupTitle, Category=excluded.Category, Language=excluded.Language, IsFavorite=excluded.IsFavorite, ImdbId=excluded.ImdbId, Overview=excluded.Overview, BackdropUrl=excluded.BackdropUrl, [Cast]=excluded.Cast, PersonalWatchCount=excluded.PersonalWatchCount, ViewersCount=excluded.ViewersCount, EpgId=excluded.EpgId, EpgUrl=excluded.EpgUrl, UrlSpeeds=excluded.UrlSpeeds, PreferredNameIndex=excluded.PreferredNameIndex, PreferredUrlIndex=excluded.PreferredUrlIndex, PreferredLogoIndex=excluded.PreferredLogoIndex, PreferredEpgIndex=excluded.PreferredEpgIndex, IsWatched=excluded.IsWatched, IsVerified=excluded.IsVerified, LastPositionMs=excluded.LastPositionMs, IsEpgLocked=excluded.IsEpgLocked";

                    AddChannelParameters(cmd, ch);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            finally { _dbLock.Release(); }
        }

        public async Task SaveChannelsBatchAsync(List<Channel> channels, bool clearFirst = false)
        {
            if (channels == null || channels.Count == 0)
            {
                if (clearFirst) await ExecuteRawNonQueryAsync("DELETE FROM Channels");
                return;
            }
            await _dbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using var tx = connection.BeginTransaction();

                    if (clearFirst)
                    {
                        var clearCmd = connection.CreateCommand();
                        clearCmd.Transaction = tx;
                        clearCmd.CommandText = "DELETE FROM Channels";
                        await clearCmd.ExecuteNonQueryAsync();
                    }

                    var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO Channels (Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredUrlIndex, PreferredLogoIndex, PreferredEpgIndex, IsWatched, IsVerified, LastPositionMs, IsEpgLocked) VALUES (@Id, @Name, @Url, @Logo, @Group, @Cat, @Lang, @Fav, @Date, @Src, @Playlist, @Imdb, @Overview, @Backdrop, @Cast, @Pwc, @Vc, @EpgId, @EpgUrl, @Us, @Pni, @Pui, @Pli, @Pei, @Watched, @Verified, @Lp, @EpgL) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Url=excluded.Url, LogoUrl=excluded.LogoUrl, GroupTitle=excluded.GroupTitle, Category=excluded.Category, Language=excluded.Language, IsFavorite=excluded.IsFavorite, ImdbId=excluded.ImdbId, Overview=excluded.Overview, BackdropUrl=excluded.BackdropUrl, [Cast]=excluded.Cast, PersonalWatchCount=excluded.PersonalWatchCount, ViewersCount=excluded.ViewersCount, EpgId=excluded.EpgId, EpgUrl=excluded.EpgUrl, UrlSpeeds=excluded.UrlSpeeds, PreferredNameIndex=excluded.PreferredNameIndex, PreferredUrlIndex=excluded.PreferredUrlIndex, PreferredLogoIndex=excluded.PreferredLogoIndex, PreferredEpgIndex=excluded.PreferredEpgIndex, IsWatched=excluded.IsWatched, IsVerified=excluded.IsVerified, LastPositionMs=excluded.LastPositionMs, IsEpgLocked=excluded.IsEpgLocked";

                    var pId = cmd.Parameters.Add("@Id", SqliteType.Text);
                    var pName = cmd.Parameters.Add("@Name", SqliteType.Text);
                    var pUrl = cmd.Parameters.Add("@Url", SqliteType.Text);
                    var pLogo = cmd.Parameters.Add("@Logo", SqliteType.Text);
                    var pGroup = cmd.Parameters.Add("@Group", SqliteType.Text);
                    var pCat = cmd.Parameters.Add("@Cat", SqliteType.Text);
                    var pLang = cmd.Parameters.Add("@Lang", SqliteType.Text);
                    var pFav = cmd.Parameters.Add("@Fav", SqliteType.Integer);
                    var pDate = cmd.Parameters.Add("@Date", SqliteType.Integer);
                    var pSrc = cmd.Parameters.Add("@Src", SqliteType.Text);
                    var pPlaylist = cmd.Parameters.Add("@Playlist", SqliteType.Text);
                    var pImdb = cmd.Parameters.Add("@Imdb", SqliteType.Text);
                    var pOverview = cmd.Parameters.Add("@Overview", SqliteType.Text);
                    var pBackdrop = cmd.Parameters.Add("@Backdrop", SqliteType.Text);
                    var pCast = cmd.Parameters.Add("@Cast", SqliteType.Text);
                    var pPwc = cmd.Parameters.Add("@Pwc", SqliteType.Integer);
                    var pVc = cmd.Parameters.Add("@Vc", SqliteType.Integer);
                    var pEpgId = cmd.Parameters.Add("@EpgId", SqliteType.Text);
                    var pEpgUrl = cmd.Parameters.Add("@EpgUrl", SqliteType.Text);
                    var pUs = cmd.Parameters.Add("@Us", SqliteType.Text);
                    var pPni = cmd.Parameters.Add("@Pni", SqliteType.Integer);
                    var pPui = cmd.Parameters.Add("@Pui", SqliteType.Integer);
                    var pPli = cmd.Parameters.Add("@Pli", SqliteType.Integer);
                    var pPei = cmd.Parameters.Add("@Pei", SqliteType.Integer);
                    var pWatched = cmd.Parameters.Add("@Watched", SqliteType.Integer);
                    var pVerified = cmd.Parameters.Add("@Verified", SqliteType.Integer);
                    var pLp = cmd.Parameters.Add("@Lp", SqliteType.Integer);
                    var pEpgL = cmd.Parameters.Add("@EpgL", SqliteType.Integer);

                    foreach (var ch in channels)
                    {
                        pId.Value = ch.Id ?? Guid.NewGuid().ToString("N");
                        pName.Value = ch.Name ?? "";
                        pUrl.Value = ch.Url ?? "";
                        pLogo.Value = ch.LogoUrl ?? "";
                        pGroup.Value = ch.GroupTitle ?? "";
                        pCat.Value = ch.Category ?? "TV";
                        pLang.Value = ch.Language ?? "und";
                        pFav.Value = ch.IsFavorite ? 1 : 0;
                        pDate.Value = new DateTimeOffset(ch.CreatedAt).ToUnixTimeSeconds();
                        pSrc.Value = ch.SourceType ?? "M3U";
                        pPlaylist.Value = ch.PlaylistUrl ?? "";
                        pImdb.Value = ch.ImdbId ?? "";
                        pOverview.Value = ch.Overview ?? "";
                        pBackdrop.Value = ch.BackdropUrl ?? "";
                        pCast.Value = ch.Cast ?? "";
                        pPwc.Value = ch.PersonalWatchCount;
                        pVc.Value = ch.ViewersCount;
                        pEpgId.Value = ch.EpgId ?? "";
                        pEpgUrl.Value = ch.EpgUrl ?? "";
                        pUs.Value = ch.UrlSpeeds ?? "";
                        pPni.Value = ch.PreferredNameIndex;
                        pPui.Value = ch.PreferredUrlIndex;
                        pPli.Value = ch.PreferredLogoIndex;
                        pPei.Value = ch.PreferredEpgIndex;
                        pWatched.Value = ch.IsWatched ? 1 : 0;
                        pVerified.Value = ch.IsVerified ? 1 : 0;
                        pLp.Value = ch.LastPositionMs;
                        pEpgL.Value = ch.IsEpgLocked ? 1 : 0;

                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
            }
            finally { _dbLock.Release(); }
        }

        public async Task SyncIncomingChannelsAsync(List<Channel> incoming)
        {
            if (incoming == null || incoming.Count == 0) return;

            try
            {
                var existing = await GetAllChannelsAsync();
                var combined = existing.Concat(incoming).ToList();
                var aggregated = ChannelAggregator.Instance.AggregateChannels(combined);

                await SaveChannelsBatchAsync(aggregated, true);
            }
            catch (Exception ex)
            {
                LogService.LogError("ChannelRepository.SyncIncomingChannelsAsync failed", ex);
            }
        }

        public async Task<int> AutoAggregateDatabaseAsync()
        {
            var existing = await GetAllChannelsAsync();
            if (existing.Count <= 1) return 0;

            var aggregated = ChannelAggregator.Instance.AggregateChannels(existing);
            int mergedCount = existing.Count - aggregated.Count;

            if (mergedCount > 0)
            {
                await SaveChannelsBatchAsync(aggregated, true);
            }

            return mergedCount;
        }

        public async Task<List<Channel>> GetSeriesEpisodesAsync(string seriesBaseName)
        {
            var list = new List<Channel>();
            if (string.IsNullOrEmpty(seriesBaseName)) return list;

            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, Name, Url, LogoUrl, GroupTitle, Category, Language, IsFavorite, AddedDate, SourceType, PlaylistUrl, ImdbId, Overview, BackdropUrl, [Cast], PersonalWatchCount, ViewersCount, EpgId, EpgUrl, UrlSpeeds, PreferredNameIndex, PreferredUrlIndex, PreferredLogoIndex, PreferredEpgIndex, IsWatched, IsVerified, LastPositionMs, IsEpgLocked FROM Channels WHERE Category='Dizi' AND (Name LIKE @q OR Name LIKE @q2)";
                    cmd.Parameters.AddWithValue("@q", seriesBaseName + "%");
                    cmd.Parameters.AddWithValue("@q2", "%" + seriesBaseName + "%");

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var ch = MapReaderToChannel(reader);
                        if (ch.SeriesBaseName == seriesBaseName)
                        {
                            list.Add(ch);
                        }
                    }
                }
            }
            catch { }
            return list.OrderBy(c => c.SeasonNumber).ThenBy(c => c.EpisodeNumber).ToList();
        }

        public async Task<int> GetTotalChannelCountAsync()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Channels";
                var res = await cmd.ExecuteScalarAsync();
                return res != null ? Convert.ToInt32(res) : 0;
            }
        }

        public int GetChannelCountBySource(string url)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Channels WHERE PlaylistUrl = @Url";
                cmd.Parameters.AddWithValue("@Url", url);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public List<Channel> GetChannelsBySource(string url)
        {
            var list = new List<Channel>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, Url, LogoUrl, Category, Language FROM Channels WHERE PlaylistUrl = @Url";
                cmd.Parameters.AddWithValue("@Url", url);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new Channel {
                        Id = reader.GetString(0), Name = reader.GetString(1), Url = reader.GetString(2),
                        LogoUrl = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        Category = reader.IsDBNull(4) ? "TV" : reader.GetString(4),
                        Language = reader.IsDBNull(5) ? "und" : reader.GetString(5)
                    });
                }
            }
            return list;
        }

        public void DeleteChannelById(string channelId)
        {
            if (string.IsNullOrEmpty(channelId)) return;
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM Channels WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", channelId);
                cmd.ExecuteNonQuery();
            }
        }

        public async Task DeleteChannelsAsync(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return;
            await _dbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using var tx = connection.BeginTransaction();

                    for (int i = 0; i < ids.Count; i += 500)
                    {
                        var chunk = ids.Skip(i).Take(500).ToList();
                        var cmd = connection.CreateCommand();
                        cmd.Transaction = tx;

                        var placeholders = new List<string>();
                        for (int j = 0; j < chunk.Count; j++)
                        {
                            string pName = $"@id{j}";
                            placeholders.Add(pName);
                            cmd.Parameters.AddWithValue(pName, chunk[j]);
                        }

                        cmd.CommandText = $"DELETE FROM Channels WHERE Id IN ({string.Join(",", placeholders)})";
                        await cmd.ExecuteNonQueryAsync();
                    }
                    tx.Commit();
                }
            }
            finally { _dbLock.Release(); }
        }

        public void SaveChannelSync(Channel ch)
        {
            _dbLock.Wait();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE Channels SET LastPositionMs = @Lp WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Lp", ch.LastPositionMs);
                    cmd.Parameters.AddWithValue("@Id", ch.Id);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
            finally { _dbLock.Release(); }
        }

        public async Task CleanupDuplicatesAsync()
        {
            LogService.LogInfo("ChannelRepository: Global aggregation and cleanup starting...");
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        DELETE FROM Channels
                        WHERE Url IS NULL
                           OR length(trim(Url)) < 5
                           OR Url LIKE '%<%'
                           OR Url LIKE '%>%'
                           OR Url LIKE '%{%'
                           OR Url LIKE '%}%'
                           OR Url LIKE '%;%'
                           OR Url LIKE '%var %'
                           OR Url LIKE '%function%';
                    ";
                    int purged = await cmd.ExecuteNonQueryAsync();
                    if (purged > 0)
                    {
                        LogService.LogInfo($"ChannelRepository: Purged {purged} corrupt/non-stream channels from database.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("ChannelRepository: Error cleaning corrupt channel rows", ex);
            }

            int merged = await AutoAggregateDatabaseAsync();
            LogService.LogInfo($"ChannelRepository: Global aggregation cleanup completed. Merged {merged} channels into existing cards.");
        }

        private Channel MapReaderToChannel(SqliteDataReader reader)
        {
            var ch = new Channel
            {
                Id = reader.IsDBNull(0) ? Guid.NewGuid().ToString() : reader.GetString(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Url = reader.IsDBNull(2) ? "" : reader.GetString(2),
                LogoUrl = reader.IsDBNull(3) ? "" : reader.GetString(3),
                GroupTitle = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Category = reader.IsDBNull(5) ? "TV" : reader.GetString(5),
                Language = reader.IsDBNull(6) ? "und" : reader.GetString(6),
                IsFavorite = !reader.IsDBNull(7) && reader.GetInt32(7) == 1,
                SourceType = reader.IsDBNull(9) ? "M3U" : reader.GetString(9),
                PlaylistUrl = reader.IsDBNull(10) ? "" : reader.GetString(10),
                ImdbId = reader.IsDBNull(11) ? "" : reader.GetString(11),
                Overview = reader.IsDBNull(12) ? "" : reader.GetString(12),
                BackdropUrl = reader.IsDBNull(13) ? "" : reader.GetString(13),
                Cast = reader.IsDBNull(14) ? "" : reader.GetString(14),
                PersonalWatchCount = reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
                ViewersCount = reader.IsDBNull(16) ? 0 : reader.GetInt32(16),
                EpgId = reader.IsDBNull(17) ? "" : reader.GetString(17),
                EpgUrl = reader.IsDBNull(18) ? "" : reader.GetString(18),
                UrlSpeeds = reader.IsDBNull(19) ? "" : reader.GetString(19),
                PreferredNameIndex = reader.IsDBNull(20) ? 0 : reader.GetInt32(20),
                PreferredUrlIndex = reader.IsDBNull(21) ? 0 : reader.GetInt32(21),
                PreferredLogoIndex = reader.IsDBNull(22) ? 0 : reader.GetInt32(22),
                PreferredEpgIndex = reader.IsDBNull(23) ? 0 : reader.GetInt32(23),
                IsWatched = !reader.IsDBNull(24) && reader.GetInt32(24) == 1,
                IsVerified = !reader.IsDBNull(25) && reader.GetInt32(25) == 1,
                LastPositionMs = (reader.FieldCount > 26 && !reader.IsDBNull(26)) ? reader.GetInt64(26) : 0,
                IsEpgLocked = (reader.FieldCount > 27 && !reader.IsDBNull(27)) && reader.GetInt32(27) == 1
            };
            if (!reader.IsDBNull(8)) { try { ch.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(8)).DateTime; } catch { } }
            return ch;
        }

        private void AddChannelParameters(SqliteCommand cmd, Channel ch)
        {
            cmd.Parameters.AddWithValue("@Id", ch.Id); cmd.Parameters.AddWithValue("@Name", ch.Name); cmd.Parameters.AddWithValue("@Url", ch.Url);
            cmd.Parameters.AddWithValue("@Logo", ch.LogoUrl); cmd.Parameters.AddWithValue("@Group", ch.GroupTitle); cmd.Parameters.AddWithValue("@Cat", ch.Category);
            cmd.Parameters.AddWithValue("@Lang", ch.Language); cmd.Parameters.AddWithValue("@Fav", ch.IsFavorite ? 1 : 0);
            cmd.Parameters.AddWithValue("@Date", new DateTimeOffset(ch.CreatedAt).ToUnixTimeSeconds()); cmd.Parameters.AddWithValue("@Src", ch.SourceType);
            cmd.Parameters.AddWithValue("@Playlist", ch.PlaylistUrl); cmd.Parameters.AddWithValue("@Imdb", ch.ImdbId); cmd.Parameters.AddWithValue("@Overview", ch.Overview);
            cmd.Parameters.AddWithValue("@Backdrop", ch.BackdropUrl); cmd.Parameters.AddWithValue("@Cast", ch.Cast);
            cmd.Parameters.AddWithValue("@Pwc", ch.PersonalWatchCount); cmd.Parameters.AddWithValue("@Vc", ch.ViewersCount);
            cmd.Parameters.AddWithValue("@EpgId", ch.EpgId ?? ""); cmd.Parameters.AddWithValue("@EpgUrl", ch.EpgUrl ?? "");
            cmd.Parameters.AddWithValue("@Us", ch.UrlSpeeds ?? "");
            cmd.Parameters.AddWithValue("@Pni", ch.PreferredNameIndex);
            cmd.Parameters.AddWithValue("@Pui", ch.PreferredUrlIndex);
            cmd.Parameters.AddWithValue("@Pli", ch.PreferredLogoIndex);
            cmd.Parameters.AddWithValue("@Pei", ch.PreferredEpgIndex);
            cmd.Parameters.AddWithValue("@Watched", ch.IsWatched ? 1 : 0);
            cmd.Parameters.AddWithValue("@Verified", ch.IsVerified ? 1 : 0);
            cmd.Parameters.AddWithValue("@Lp", ch.LastPositionMs);
            cmd.Parameters.AddWithValue("@EpgL", ch.IsEpgLocked ? 1 : 0);
        }

        private async Task ExecuteRawNonQueryAsync(string sql)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
