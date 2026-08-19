using System;
using System.IO;

namespace StreamMesh.Core.Utils
{
    public static class LogService
    {
        private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "app.log");

        public static void LogInfo(string message)
        {
            Log("INFO", message);
        }

        public static void LogWarning(string message)
        {
            Log("WARN", message);
        }

        public static void LogError(string message, Exception? ex = null)
        {
            Log("ERROR", $"{message} {ex?.Message}");
        }

        private static readonly object _lock = new object();

        public static void ClearLogs()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(LogPath)) File.Delete(LogPath);
                    Log("INFO", "=== Yeni Oturum Başlatıldı, Loglar Temizlendi ===");
                }
            }
            catch { }
        }

        private static void Log(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    string dir = Path.GetDirectoryName(LogPath) ?? "";
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogPath, line);
                }
            }
            catch { }
        }
    }
}
