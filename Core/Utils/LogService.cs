using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Diagnostics;

namespace StreamMesh.Core.Utils
{
    public static class LogService
    {
        private static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh");
        private static readonly string LogPath = Path.Combine(AppDataDir, "app.log");
        private static readonly string CrashPath = Path.Combine(AppDataDir, "crash.log");
        private static readonly string LocalCrashPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");

        private static readonly Channel<string> _logChannel;
        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private static StreamWriter? _writer;
        private static readonly object _fileLock = new object();
        private static bool _isInitialized = false;

        private const int MaxQueueSize = 50000;

        static LogService()
        {
            try
            {
                if (!Directory.Exists(AppDataDir))
                {
                    Directory.CreateDirectory(AppDataDir);
                }
            }
            catch { }

            _logChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(MaxQueueSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

            Task.Run(ProcessLogsAsync);
        }

        public static void LogInfo(string message) => EnqueueLog("INFO", message);
        public static void LogWarning(string message) => EnqueueLog("WARN", message);
        
        public static void LogError(string message, Exception? ex = null)
        {
            string detail = ex != null ? $"{message}\n[EXCEPTION] {ex}" : message;
            EnqueueLog("ERROR", detail);

            // Hatalarda crash loguna da senkron olarak garantili yaz
            WriteDirectCrashLog($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {detail}");
        }

        public static void LogFatal(string message, Exception? ex = null)
        {
            string detail = $"{message}\n[FATAL CRASH] {ex?.ToString() ?? "Bilinmeyen kritik istisna"}";
            WriteDirectCrashLog($"[FATAL] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {detail}");
            EnqueueLog("FATAL", detail);
        }

        public static void WriteDirectCrashLog(string text)
        {
            try
            {
                Console.Error.WriteLine(text);
                Trace.WriteLine(text);
            }
            catch { }

            lock (_fileLock)
            {
                try
                {
                    if (!Directory.Exists(AppDataDir)) Directory.CreateDirectory(AppDataDir);
                    File.AppendAllText(LogPath, text + Environment.NewLine, Encoding.UTF8);
                    File.AppendAllText(CrashPath, text + Environment.NewLine, Encoding.UTF8);
                    File.AppendAllText(LocalCrashPath, text + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void EnqueueLog(string level, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

            try
            {
                Console.WriteLine(line);
            }
            catch { }

            if (!_logChannel.Writer.TryWrite(line))
            {
                // Fallback for overload
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                        await _logChannel.Writer.WriteAsync(line, timeoutCts.Token);
                    }
                    catch
                    {
                        Trace.WriteLine($"[LOG OVERLOAD] Dropped: {line}");
                    }
                });
            }
        }

        private static async Task ProcessLogsAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    EnsureWriterInitialized();

                    while (await _logChannel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                    {
                        while (_logChannel.Reader.TryRead(out var logLine))
                        {
                            lock (_fileLock)
                            {
                                _writer?.WriteLine(logLine);
                            }
                        }
                        lock (_fileLock)
                        {
                            _writer?.Flush();
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[FATAL LOG WRITER ERROR] {ex.Message}");
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }

            CloseWriter();
        }

        private static void EnsureWriterInitialized()
        {
            if (_isInitialized && _writer != null) return;

            lock (_fileLock)
            {
                try
                {
                    if (!Directory.Exists(AppDataDir)) Directory.CreateDirectory(AppDataDir);
                    var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: false);
                    _writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };
                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[LOG INIT ERROR] {ex.Message}");
                }
            }
        }

        public static void ClearLogs()
        {
            // Eski log dosyasını silip sıfırlamak yerine temiz bir oturum başlığı atıyoruz.
            // Böylece başlangıç çökmelerinde loglar uçmaz!
            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 10 * 1024 * 1024)
                    {
                        // 10MB'tan büyükse temizle
                        CloseWriter();
                        using (var fs = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        {
                            fs.SetLength(0);
                        }
                        EnsureWriterInitialized();
                    }
                    
                    LogInfo("======================================================================");
                    LogInfo($"=== StreamMesh Oturumu Başlatıldı (PID: {Process.GetCurrentProcess().Id}) ===");
                    LogInfo("======================================================================");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[LOG CLEAR ERROR] {ex.Message}");
                }
            }
        }

        public static void Shutdown()
        {
            try
            {
                _logChannel.Writer.TryComplete();
                _cts.Cancel();
                Task.Delay(200).Wait();
                CloseWriter();
            }
            catch { }
        }

        private static void CloseWriter()
        {
            lock (_fileLock)
            {
                try
                {
                    _writer?.Dispose();
                    _writer = null;
                    _isInitialized = false;
                }
                catch { }
            }
        }
    }
}
