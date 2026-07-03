using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public partial class DatabaseService
    {
        public void ClearEpg()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM EpgPrograms";
                command.ExecuteNonQuery();
            }
        }

        public void ClearEpgByUrl(string url)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM EpgPrograms WHERE SourceUrl = @Url";
                command.Parameters.AddWithValue("@Url", url);
                command.ExecuteNonQuery();
            }
        }

        public void SaveEpgPrograms(List<EpgProgram> programs)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO EpgPrograms (ChannelName, Title, Description, StartTime, EndTime, SourceUrl)
                        VALUES (@ChannelName, @Title, @Description, @StartTime, @EndTime, @SourceUrl)
                    ";

                    var pChannelName = command.Parameters.Add("@ChannelName", SqliteType.Text);
                    var pTitle = command.Parameters.Add("@Title", SqliteType.Text);
                    var pDescription = command.Parameters.Add("@Description", SqliteType.Text);
                    var pStartTime = command.Parameters.Add("@StartTime", SqliteType.Text);
                    var pEndTime = command.Parameters.Add("@EndTime", SqliteType.Text);
                    var pSourceUrl = command.Parameters.Add("@SourceUrl", SqliteType.Text);

                    foreach (var prog in programs)
                    {
                        pChannelName.Value = prog.ChannelName ?? string.Empty;
                        pTitle.Value = prog.Title ?? string.Empty;
                        pDescription.Value = prog.Description ?? string.Empty;
                        pStartTime.Value = prog.StartTime.ToString("o");
                        pEndTime.Value = prog.EndTime.ToString("o");
                        pSourceUrl.Value = prog.SourceUrl ?? string.Empty;
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
        }

        public List<string> GetUniqueEpgChannelNames()
        {
            var names = new List<string>();
            try
                {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT DISTINCT ChannelName FROM EpgPrograms ORDER BY ChannelName ASC";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            names.Add(reader.GetString(0));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"[DatabaseService] EPG isimleri alınırken hata: {ex.Message}");
            }
            return names;
        }

        public int GetEpgSourceProgramCount(string url)
        {
            try
                {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM EpgPrograms WHERE SourceUrl = @Url";
                    command.Parameters.AddWithValue("@Url", url);
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
            catch { return 0; }
        }

        public int GetEpgSourceChannelCount(string url)
        {
            try
                {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(DISTINCT ChannelName) FROM EpgPrograms WHERE SourceUrl = @Url";
                    command.Parameters.AddWithValue("@Url", url);
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
            catch { return 0; }
        }

        public Dictionary<string, EpgProgram> GetCurrentEpgsForChannels(List<Channel> channels)
        {
            var dict = new Dictionary<string, EpgProgram>(StringComparer.OrdinalIgnoreCase);
            if (channels == null || channels.Count == 0) return dict;

            string nowStr = DateTime.Now.ToString("o");
            var allPlayingPrograms = new List<EpgProgram>();

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = $@"
                    SELECT Id, ChannelName, Title, Description, StartTime, EndTime
                    FROM EpgPrograms
                    WHERE StartTime <= '{nowStr}' AND EndTime >= '{nowStr}'
                ";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var prog = new EpgProgram
                        {
                            Id = reader.GetInt32(0),
                            ChannelName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            Title = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                        };

                        if (!reader.IsDBNull(4) && DateTime.TryParse(reader.GetString(4), out DateTime st)) prog.StartTime = st;
                        if (!reader.IsDBNull(5) && DateTime.TryParse(reader.GetString(5), out DateTime et)) prog.EndTime = et;
                        
                        allPlayingPrograms.Add(prog);
                    }
                }
            }

            foreach (var ch in channels)
            {
                var chName = ch.Name;
                var epgId = ch.EpgId;

                if (!string.IsNullOrEmpty(epgId))
                {
                    var idMatch = allPlayingPrograms.FirstOrDefault(p => p.ChannelName.Equals(epgId, StringComparison.OrdinalIgnoreCase));
                    if (idMatch != null)
                    {
                        dict[ch.Id] = idMatch;
                        continue;
                    }
                }

                var exactMatch = allPlayingPrograms.FirstOrDefault(p => p.ChannelName.Equals(chName, StringComparison.OrdinalIgnoreCase));
                if (exactMatch != null)
                {
                    dict[ch.Id] = exactMatch;
                    continue;
                }

                var cleanName = CleanChannelName(chName);
                var cleanMatch = allPlayingPrograms.FirstOrDefault(p => CleanChannelName(p.ChannelName).Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                if (cleanMatch != null)
                {
                    dict[ch.Id] = cleanMatch;
                    continue;
                }

                var partialMatch = allPlayingPrograms.FirstOrDefault(p => 
                    p.ChannelName.IndexOf(chName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    chName.IndexOf(p.ChannelName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.ChannelName.IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0);
                    
                if (partialMatch != null)
                {
                    dict[ch.Id] = partialMatch;
                }
            }

            return dict;
        }

        public EpgProgram GetCurrentEpgForChannel(Channel channel)
        {
            if (channel == null || string.IsNullOrEmpty(channel.Name)) return null;

            string nowStr = DateTime.Now.ToString("o");
            string channelName = channel.Name;
            string epgId = channel.EpgId;
            string cleanName = CleanChannelName(channelName);
            
            LogService.Log($"[EPG-Match] GetCurrentEpgForChannel: '{channelName}' kanalı için EPG aranıyor. EpgId: '{(string.IsNullOrEmpty(epgId) ? "(yok)" : epgId)}', Temizlenmiş İsim: '{cleanName}'");

            var allPrograms = new List<EpgProgram>();

            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT Id, ChannelName, Title, Description, StartTime, EndTime FROM EpgPrograms WHERE StartTime <= @Now AND EndTime >= @Now";
                    command.Parameters.AddWithValue("@Now", nowStr);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var prog = new EpgProgram
                            {
                                Id = reader.GetInt32(0),
                                ChannelName = reader.GetString(1),
                                Title = reader.GetString(2),
                                Description = reader.GetString(3)
                            };
                            if (DateTime.TryParse(reader.GetString(4), out DateTime st)) prog.StartTime = st;
                            if (DateTime.TryParse(reader.GetString(5), out DateTime et)) prog.EndTime = et;
                            allPrograms.Add(prog);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"[EPG-Match] Şu anki EPG programları veritabanından okunurken hata oluştu. Kanal: '{channelName}'", ex);
            }

            LogService.Log($"[EPG-Match] Veritabanında şu an yayınlanan toplam EPG programı sayısı: {allPrograms.Count}");

            if (allPrograms.Count == 0)
            {
                LogService.Log($"[EPG-Match] Veritabanında şu an için aktif hiçbir EPG programı bulunamadı. EPG kaynağı eklenmemiş veya süresi dolmuş olabilir.", "WARN");
                return null;
            }

            // 1. Aşama: EpgId ile Birebir Eşleme
            if (!string.IsNullOrEmpty(epgId))
            {
                var match = allPrograms.FirstOrDefault(p => p.ChannelName.Equals(epgId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    LogService.Log($"[EPG-Match] -> 1. Aşama Başarılı (EpgId Eşleşmesi): Kanal '{channelName}' (EpgId: '{epgId}') == Program Kanalı '{match.ChannelName}' -> Program: '{match.Title}'");
                    return match;
                }
            }

            // 2. Aşama: Kanal İsmiyle Birebir Eşleme
            var exactMatch = allPrograms.FirstOrDefault(p => p.ChannelName.Equals(channelName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                LogService.Log($"[EPG-Match] -> 2. Aşama Başarılı (Birebir İsim Eşleşmesi): Kanal '{channelName}' == Program Kanalı '{exactMatch.ChannelName}' -> Program: '{exactMatch.Title}'");
                return exactMatch;
            }

            // 3. Aşama: Temizlenmiş Kanal İsmiyle Eşleme
            var cleanMatch = allPrograms.FirstOrDefault(p => CleanChannelName(p.ChannelName).Equals(cleanName, StringComparison.OrdinalIgnoreCase));
            if (cleanMatch != null)
            {
                LogService.Log($"[EPG-Match] -> 3. Aşama Başarılı (Temizlenmiş İsim Eşleşmesi): Kanal '{channelName}' (Temiz: '{cleanName}') == Program Kanalı '{cleanMatch.ChannelName}' (Temiz: '{CleanChannelName(cleanMatch.ChannelName)}') -> Program: '{cleanMatch.Title}'");
                return cleanMatch;
            }

            // 4. Aşama: Kısmi / İçerme Eşleşmesi
            var partialMatch = allPrograms.FirstOrDefault(p => 
                channelName.IndexOf(p.ChannelName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.ChannelName.IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (partialMatch != null)
            {
                LogService.Log($"[EPG-Match] -> 4. Aşama Başarılı (Kısmi/İçerme Eşleşmesi): Kanal '{channelName}' ile Program Kanalı '{partialMatch.ChannelName}' eşleşti -> Program: '{partialMatch.Title}'");
                return partialMatch;
            }

            LogService.Log($"[EPG-Match] -> '{channelName}' kanalı için veritabanındaki {allPrograms.Count} program arasından hiçbir eşleşme bulunamadı.");
            return null;
        }

        private string CleanChannelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string[] tags = { "HD", "SD", "FHD", "4K", "UHD", "TR", "GE", "FR", "EN", "HE", "BACKUP", "YEDEK", "|", "-" };
            string result = name;
            foreach (var tag in tags) result = result.Replace(tag, "", StringComparison.OrdinalIgnoreCase);
            return result.Replace("  ", " ").Trim();
        }

        public EpgProgram GetNextEpgForChannel(Channel channel)
        {
            if (channel == null || string.IsNullOrEmpty(channel.Name)) return null;

            string nowStr = DateTime.Now.ToString("o");
            string channelName = channel.Name;
            string epgId = channel.EpgId;
            string cleanName = CleanChannelName(channelName);

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, ChannelName, Title, Description, StartTime, EndTime FROM EpgPrograms WHERE StartTime > @Now ORDER BY StartTime ASC LIMIT 500";
                command.Parameters.AddWithValue("@Now", nowStr);
                
                var potentials = new List<EpgProgram>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var prog = new EpgProgram {
                            Id = reader.GetInt32(0),
                            ChannelName = reader.GetString(1),
                            Title = reader.GetString(2),
                            Description = reader.GetString(3)
                        };
                        if (DateTime.TryParse(reader.GetString(4), out DateTime st)) prog.StartTime = st;
                        if (DateTime.TryParse(reader.GetString(5), out DateTime et)) prog.EndTime = et;
                        potentials.Add(prog);
                    }
                }

                if (!string.IsNullOrEmpty(epgId)) {
                    var m = potentials.FirstOrDefault(p => p.ChannelName.Equals(epgId, StringComparison.OrdinalIgnoreCase));
                    if (m != null) return m;
                }

                var m2 = potentials.FirstOrDefault(p => p.ChannelName.Equals(channelName, StringComparison.OrdinalIgnoreCase));
                if (m2 != null) return m2;

                var m3 = potentials.FirstOrDefault(p => CleanChannelName(p.ChannelName).Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                if (m3 != null) return m3;

                return potentials.FirstOrDefault(p => channelName.IndexOf(p.ChannelName, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        public List<Tuple<string, string>> GetEpgChannelsWithSource()
        {
            var list = new List<Tuple<string, string>>();
            try
            {
                using (var connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT ChannelName, MAX(SourceUrl) FROM EpgPrograms GROUP BY ChannelName ORDER BY ChannelName ASC";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string name = reader.GetString(0);
                            string url = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                            list.Add(new Tuple<string, string>(name, url));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Log($"[DatabaseService] GetEpgChannelsWithSource error: {ex.Message}");
            }
            return list;
        }
    }
}
