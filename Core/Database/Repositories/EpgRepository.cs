using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Database.Repositories
{
    public class EpgRepository
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _dbLock;

        public EpgRepository(string connectionString, SemaphoreSlim dbLock)
        {
            _connectionString = connectionString;
            _dbLock = dbLock;
        }

        public async Task SaveEpgProgramsAsync(List<EpgProgram> programs)
        {
            if (programs == null || programs.Count == 0) return;
            await _dbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using var transaction = connection.BeginTransaction();
                    var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "INSERT INTO EpgPrograms (ChannelName, Title, Description, StartTime, EndTime, SourceUrl) VALUES (@ChannelName, @Title, @Description, @StartTime, @EndTime, @SourceUrl)";
                    var pName = cmd.Parameters.Add("@ChannelName", SqliteType.Text);
                    var pTitle = cmd.Parameters.Add("@Title", SqliteType.Text);
                    var pDesc = cmd.Parameters.Add("@Description", SqliteType.Text);
                    var pStart = cmd.Parameters.Add("@StartTime", SqliteType.Text);
                    var pEnd = cmd.Parameters.Add("@EndTime", SqliteType.Text);
                    var pSrc = cmd.Parameters.Add("@SourceUrl", SqliteType.Text);

                    foreach (var prog in programs)
                    {
                        pName.Value = prog.ChannelName ?? "";
                        pTitle.Value = prog.Title ?? "";
                        pDesc.Value = prog.Description ?? "";
                        pStart.Value = prog.StartTime.ToString("o");
                        pEnd.Value = prog.EndTime.ToString("o");
                        pSrc.Value = prog.SourceUrl ?? "";
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
            finally { _dbLock.Release(); }
        }

        public async Task CleanupOldEpgProgramsAsync(int daysBack = 1)
        {
            await _dbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    string limit = DateTime.Now.AddDays(-daysBack).ToString("o");
                    cmd.CommandText = "DELETE FROM EpgPrograms WHERE EndTime < @limit";
                    cmd.Parameters.AddWithValue("@limit", limit);
                    int count = await cmd.ExecuteNonQueryAsync();

                    if (count > 5000)
                    {
                        var vacuumCmd = connection.CreateCommand();
                        vacuumCmd.CommandText = "VACUUM";
                        await vacuumCmd.ExecuteNonQueryAsync();
                    }
                    LogService.LogInfo($"EpgRepository: {count} old EPG items cleaned.");
                }
            }
            catch (Exception ex) { LogService.LogError("EpgRepository.CleanupOldEpgProgramsAsync failed", ex); }
            finally { _dbLock.Release(); }
        }

        public async Task ClearEpgSourceDataAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            await _dbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM EpgPrograms WHERE SourceUrl = @url; DELETE FROM EpgChannels WHERE SourceUrl = @url;";
                    cmd.Parameters.AddWithValue("@url", url);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            finally { _dbLock.Release(); }
        }

        public async Task SaveEpgChannelsBatchAsync(List<(string epgId, string name, string logo, string url)> channels)
        {
            if (channels == null || channels.Count == 0) return;
            await _dbLock.WaitAsync();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using var transaction = connection.BeginTransaction();
                    var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "INSERT OR REPLACE INTO EpgChannels (EpgId, Name, LogoUrl, SourceUrl) VALUES (@id, @name, @logo, @url)";
                    var pId = cmd.Parameters.Add("@id", SqliteType.Text);
                    var pName = cmd.Parameters.Add("@name", SqliteType.Text);
                    var pLogo = cmd.Parameters.Add("@logo", SqliteType.Text);
                    var pUrl = cmd.Parameters.Add("@url", SqliteType.Text);

                    foreach (var c in channels)
                    {
                        pId.Value = c.epgId ?? "";
                        pName.Value = c.name ?? "";
                        pLogo.Value = c.logo ?? "";
                        pUrl.Value = c.url ?? "";
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
            finally { _dbLock.Release(); }
        }

        public async Task<List<EpgChannelSearchResult>> SearchEpgChannelsAsync(string query, bool allSources, List<string> epgSources)
        {
            var list = new List<EpgChannelSearchResult>();
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    string rawQuery = (query ?? "").Trim();
                    string cleanQuery = StreamMesh.Core.Media.ChannelUtils.GetCleanName(rawQuery);

                    var cmd = connection.CreateCommand();
                    string sql = @"
                        SELECT DISTINCT ec.EpgId, ec.Name, ec.SourceUrl,
                        (SELECT Title FROM EpgPrograms ep WHERE (ep.ChannelName = ec.EpgId OR ep.ChannelName LIKE ec.EpgId || ',%' OR ep.ChannelName LIKE '%, ' || ec.EpgId)
                         AND ep.StartTime <= @now AND ep.EndTime >= @now ORDER BY ep.StartTime DESC LIMIT 1) as LatestTitle
                        FROM EpgChannels ec
                        WHERE 1=1 ";

                    cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));

                    if (!string.IsNullOrWhiteSpace(rawQuery))
                    {
                        sql += " AND (ec.Name LIKE @q OR ec.EpgId LIKE @q OR ec.Name LIKE @cq) ";
                        cmd.Parameters.AddWithValue("@q", "%" + rawQuery + "%");
                        cmd.Parameters.AddWithValue("@cq", "%" + cleanQuery + "%");
                    }

                    if (!allSources && epgSources != null && epgSources.Count > 0)
                    {
                        var placeholders = new List<string>();
                        for (int i = 0; i < epgSources.Count; i++)
                        {
                            string pName = $"@src{i}";
                            placeholders.Add(pName);
                            cmd.Parameters.AddWithValue(pName, epgSources[i]);
                        }
                        sql += $" AND ec.SourceUrl IN ({string.Join(",", placeholders)}) ";
                    }

                    sql += " ORDER BY ec.Name ASC LIMIT 100";
                    cmd.CommandText = sql;

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            list.Add(new EpgChannelSearchResult
                            {
                                EpgId = reader.GetString(0),
                                ChannelName = reader.GetString(1),
                                SourceUrl = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                CurrentProgram = reader.IsDBNull(3) ? "Yayın akışı bilgisi yok" : reader.GetString(3),
                                SourceName = allSources ? "Tüm EPG Kaynakları" : "Mevcut EPG Kaynağı"
                            });
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(rawQuery))
                    {
                        list = list.OrderByDescending(r => r.ChannelName.StartsWith(rawQuery, StringComparison.OrdinalIgnoreCase) || r.EpgId.StartsWith(rawQuery, StringComparison.OrdinalIgnoreCase))
                                   .ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"EpgRepository: Error searching EPG channels", ex);
            }
            return list;
        }

        public async Task<List<EpgProgram>> GetCurrentEpgProgramsAsync()
        {
            var list = new List<EpgProgram>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                var now = DateTime.Now;
                var startWin = now.AddHours(-2).ToString("o");
                var endWin = now.AddHours(3).ToString("o");

                cmd.CommandText = "SELECT ChannelName, Title, Description, StartTime, EndTime, SourceUrl FROM EpgPrograms WHERE StartTime <= @EndWin AND EndTime >= @StartWin";
                cmd.Parameters.AddWithValue("@StartWin", startWin);
                cmd.Parameters.AddWithValue("@EndWin", endWin);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var ep = new EpgProgram { ChannelName = reader.GetString(0), Title = reader.GetString(1), Description = reader.GetString(2), SourceUrl = reader.IsDBNull(5) ? "" : reader.GetString(5) };
                    if (DateTime.TryParse(reader.GetString(3), out DateTime st)) ep.StartTime = st;
                    if (DateTime.TryParse(reader.GetString(4), out DateTime et)) ep.EndTime = et;
                    list.Add(ep);
                }
            }
            return list;
        }

        public async Task<List<EpgProgram>> GetEpgForChannelsAsync(List<string> channelNames)
        {
            var list = new List<EpgProgram>();
            if (channelNames == null || channelNames.Count == 0) return list;

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();

                var conditions = new List<string>();
                for (int i = 0; i < channelNames.Count; i++)
                {
                    conditions.Add($"(ChannelName = @n{i} OR ChannelName LIKE @n{i} || ',%' OR ChannelName LIKE '%, ' || @n{i})");
                    cmd.Parameters.AddWithValue($"@n{i}", channelNames[i]);
                }

                cmd.CommandText = $"SELECT ChannelName, Title, Description, StartTime, EndTime, SourceUrl FROM EpgPrograms WHERE {string.Join(" OR ", conditions)}";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var ep = new EpgProgram { ChannelName = reader.GetString(0), Title = reader.GetString(1), Description = reader.GetString(2), SourceUrl = reader.IsDBNull(5) ? "" : reader.GetString(5) };
                    if (DateTime.TryParse(reader.GetString(3), out DateTime st)) ep.StartTime = st;
                    if (DateTime.TryParse(reader.GetString(4), out DateTime et)) ep.EndTime = et;
                    list.Add(ep);
                }
            }
            return list;
        }

        public List<string> GetEpgSources()
        {
            var list = new List<string>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Url FROM EpgSources ORDER BY AddedDate DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(reader.GetString(0));
            }
            return list;
        }

        public void AddEpgSource(string url)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO EpgSources (Url, AddedDate) VALUES (@Url, @Date)";
                cmd.Parameters.AddWithValue("@Url", url); cmd.Parameters.AddWithValue("@Date", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                cmd.ExecuteNonQuery();
            }
        }

        public void RemoveEpgSource(string url)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM EpgSources WHERE Url = @Url;";
                cmd.Parameters.AddWithValue("@Url", url); cmd.ExecuteNonQuery();
            }
        }
    }
}
