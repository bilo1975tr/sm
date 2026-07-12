using System;
using System.IO;

namespace StreamMesh.Services
{
    public static class LogService
    {
        public static event Action<string> OnLogMessage;

        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "app.log");
        private static readonly object _lock = new object();

        static LogService()
        {
            try
            {
                string dir = Path.GetDirectoryName(LogFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch { }
        }

        public static void Log(string message, string level = "INFO")
        {
            lock (_lock)
            {
                try
                {
                    string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, logLine);
                    OnLogMessage?.Invoke(logLine);
                }
                catch { }
            }
        }

        public static void LogError(string message, Exception ex = null)
        {
            string detail = ex != null ? $"\nException: {ex.Message}\nStacktrace: {ex.StackTrace}" : "";
            Log($"{message}{detail}", "ERROR");
        }
    }
}
