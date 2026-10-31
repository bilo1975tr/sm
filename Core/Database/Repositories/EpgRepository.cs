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
                        var candidates = StreamMesh.Core.Media.ChannelUtils.GetEpgSearchCandidateNames(rawQuery);
                        var orClauses = new List<string>();
                        for (int ci = 0; ci < candidates.Count; ci++)
                        {
                            string p = $"@qc{ci}";
                            orClauses.Add($"ec.Name LIKE {p} OR ec.EpgId LIKE {p}");
                            cmd.Parameters.AddWithValue(p, "%" + candidates[ci] + "%");
                        }
                        if (orClauses.Count > 0)
                        {
                            sql += $" AND ({string.Join(" OR ", orClauses)}) ";
                        }
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

        public async Task<int> GetEpgProgramsCountAsync()
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM EpgPrograms";
                    var val = await cmd.ExecuteScalarAsync();
                    return val != null ? Convert.ToInt32(val) : 0;
                }
            }
            catch { return 0; }
        }

        private Dictionary<string, int> GetSourceTimeShifts(SqliteConnection connection)
        {
            var shifts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Url, TimeShiftHours FROM EpgSources";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string url = reader.GetString(0);
                    int shift = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    shifts[url] = shift;
                }
            }
            catch { }
            return shifts;
        }

        public async Task<List<EpgProgram>> GetCurrentEpgProgramsAsync()
        {
            var list = new List<EpgProgram>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var shifts = GetSourceTimeShifts(connection);
                var cmd = connection.CreateCommand();
                var now = DateTime.Now;
                var startWin = now.AddHours(-6).ToString("o");
                var endWin = now.AddHours(6).ToString("o");

                cmd.CommandText = "SELECT ChannelName, Title, Description, StartTime, EndTime, SourceUrl FROM EpgPrograms WHERE StartTime <= @EndWin AND EndTime >= @StartWin";
                cmd.Parameters.AddWithValue("@StartWin", startWin);
                cmd.Parameters.AddWithValue("@EndWin", endWin);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string srcUrl = reader.IsDBNull(5) ? "" : reader.GetString(5);
                    int shift = shifts.TryGetValue(srcUrl, out int s) ? s : 0;

                    var ep = new EpgProgram { ChannelName = reader.GetString(0), Title = reader.GetString(1), Description = reader.GetString(2), SourceUrl = srcUrl };
                    if (DateTime.TryParse(reader.GetString(3), out DateTime st)) ep.StartTime = st.AddHours(shift);
                    if (DateTime.TryParse(reader.GetString(4), out DateTime et)) ep.EndTime = et.AddHours(shift);
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
                var shifts = GetSourceTimeShifts(connection);
                var cmd = connection.CreateCommand();

                var now = DateTime.Now;
                var startWin = now.AddHours(-12).ToString("o");
                var endWin = now.AddHours(48).ToString("o");

                var conditions = new List<string>();
                for (int i = 0; i < channelNames.Count; i++)
                {
                    conditions.Add($"(ChannelName = @n{i} COLLATE NOCASE OR ChannelName LIKE @n{i} || ',%' OR ChannelName LIKE '%, ' || @n{i} || ',%' OR ChannelName LIKE '%, ' || @n{i})");
                    cmd.Parameters.AddWithValue($"@n{i}", channelNames[i]);
                }

                cmd.Parameters.AddWithValue("@StartWin", startWin);
                cmd.Parameters.AddWithValue("@EndWin", endWin);

                cmd.CommandText = $"SELECT ChannelName, Title, Description, StartTime, EndTime, SourceUrl FROM EpgPrograms WHERE EndTime >= @StartWin AND StartTime <= @EndWin AND ({string.Join(" OR ", conditions)})";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string srcUrl = reader.IsDBNull(5) ? "" : reader.GetString(5);
                    int shift = shifts.TryGetValue(srcUrl, out int s) ? s : 0;

                    var ep = new EpgProgram { ChannelName = reader.GetString(0), Title = reader.GetString(1), Description = reader.GetString(2), SourceUrl = srcUrl };
                    if (DateTime.TryParse(reader.GetString(3), out DateTime st)) ep.StartTime = st.AddHours(shift);
                    if (DateTime.TryParse(reader.GetString(4), out DateTime et)) ep.EndTime = et.AddHours(shift);
                    list.Add(ep);
                }
            }
            return list;
        }

        public List<EpgSourceItem> GetEpgSourceItems()
        {
            var list = new List<EpgSourceItem>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Url, Status, ChannelCount, ProgramCount, LastUpdated, TimeShiftHours, ErrorMessage FROM EpgSources ORDER BY AddedDate DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string url = reader.GetString(0);
                    bool isCloud = url.Contains("github") || url.Contains("raw.githubusercontent");
                    list.Add(new EpgSourceItem
                    {
                        Url = url,
                        Origin = isCloud ? "Bulut" : "Yerel",
                        Color = isCloud ? "#0369a1" : "#1e293b",
                        Status = reader.IsDBNull(1) ? "Bekliyor" : reader.GetString(1),
                        ChannelCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        ProgramCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                        LastUpdatedUnix = reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                        TimeShiftHours = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                        ErrorMessage = reader.IsDBNull(6) ? "" : reader.GetString(6)
                    });
                }
            }
            return list;
        }

        public void UpdateEpgSourceStats(string url, string status, int channelCount, int programCount, long lastUpdated, string errorMessage)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE EpgSources SET Status = @Status, ChannelCount = @ChannelCount, ProgramCount = @ProgramCount, LastUpdated = @LastUpdated, ErrorMessage = @ErrorMessage WHERE Url = @Url";
                cmd.Parameters.AddWithValue("@Url", url);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@ChannelCount", channelCount);
                cmd.Parameters.AddWithValue("@ProgramCount", programCount);
                cmd.Parameters.AddWithValue("@LastUpdated", lastUpdated);
                cmd.Parameters.AddWithValue("@ErrorMessage", errorMessage);
                cmd.ExecuteNonQuery();
            }
        }

        public void SetEpgSourceTimeShift(string url, int timeShiftHours)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE EpgSources SET TimeShiftHours = @Shift WHERE Url = @Url";
                cmd.Parameters.AddWithValue("@Url", url);
                cmd.Parameters.AddWithValue("@Shift", timeShiftHours);
                cmd.ExecuteNonQuery();
            }
        }

        public int GetEpgSourceTimeShift(string sourceUrl)
        {
            if (string.IsNullOrEmpty(sourceUrl)) return 0;
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT TimeShiftHours FROM EpgSources WHERE Url = @Url";
                cmd.Parameters.AddWithValue("@Url", sourceUrl);
                var val = cmd.ExecuteScalar();
                return val != null && val != DBNull.Value ? Convert.ToInt32(val) : 0;
            }
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
                cmd.CommandText = "INSERT OR IGNORE INTO EpgSources (Url, AddedDate, Status) VALUES (@Url, @Date, 'Bekliyor')";
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
                cmd.CommandText = "DELETE FROM EpgSources WHERE Url = @Url; DELETE FROM EpgPrograms WHERE SourceUrl = @Url; DELETE FROM EpgChannels WHERE SourceUrl = @Url;";
                cmd.Parameters.AddWithValue("@Url", url); cmd.ExecuteNonQuery();
            }
        }
    }
}
