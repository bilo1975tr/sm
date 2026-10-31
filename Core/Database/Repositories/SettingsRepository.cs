using System;
using Microsoft.Data.Sqlite;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Database.Repositories
{
    public class SettingsRepository
    {
        private readonly string _connectionString;

        public SettingsRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string GetSetting(string key, string defaultValue = "")
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key=@k";
                    cmd.Parameters.AddWithValue("@k", key);
                    var res = cmd.ExecuteScalar();
                    return res != null ? res.ToString() ?? defaultValue : defaultValue;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"SettingsRepository: Error getting setting '{key}'", ex);
                return defaultValue;
            }
        }

        public void SetSetting(string key, string value)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES (@k, @v) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
                    cmd.Parameters.AddWithValue("@k", key); cmd.Parameters.AddWithValue("@v", value);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"SettingsRepository: Error setting '{key}'", ex);
            }
        }

        public (int count, DateTime date) GetDailyQueryStats()
        {
            string dateStr = GetSetting("DailyQueryDate", DateTime.Today.ToString("o"));
            int count = 0;
            int.TryParse(GetSetting("DailyQueryCount", "0"), out count);

            if (DateTime.TryParse(dateStr, out DateTime parsedDate) && parsedDate.Date != DateTime.Today)
            {
                count = 0;
                SetSetting("DailyQueryDate", DateTime.Today.ToString("o"));
                SetSetting("DailyQueryCount", "0");
            }
            return (count, DateTime.Today);
        }

        public void IncrementDailyQueryCount()
        {
            var stats = GetDailyQueryStats();
            SetSetting("DailyQueryCount", (stats.count + 1).ToString());
        }
    }
}
