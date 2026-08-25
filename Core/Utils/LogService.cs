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
        private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "app.log");
        private static readonly Channel<string> _logChannel;
        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private static StreamWriter? _writer;
        private static readonly object _initLock = new object();
        private static bool _isInitialized = false;

        private const int MaxQueueSize = 50000;

        static LogService()
        {
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
        public static void LogError(string message, Exception? ex = null) => EnqueueLog("ERROR", $"{message} {ex?.Message}");

        private static void EnqueueLog(string level, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

            if (!_logChannel.Writer.TryWrite(line))
            {
                // Fallback for overload: try to write asynchronously without blocking the caller's thread
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
            try
            {
                EnsureWriterInitialized();

                while (await _logChannel.Reader.WaitToReadAsync(_cts.Token))
                {
                    while (_logChannel.Reader.TryRead(out var logLine))
                    {
                        if (_writer != null)
                        {
                            await _writer.WriteLineAsync(logLine);
                        }
                    }
                    // AutoFlush is handled by StreamWriter if configured, but we can flush here to ensure
                    // visibility for the /logs endpoint during idle periods.
                    if (_writer != null) await _writer.FlushAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FATAL LOG WRITER ERROR] {ex.Message}");
            }
            finally
            {
                CloseWriter();
            }
        }

        private static void EnsureWriterInitialized()
        {
            if (_isInitialized && _writer != null) return;

            lock (_initLock)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    // Open with FileShare.ReadWrite to allow /logs endpoint to read while we write
                    var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
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
            // Clearing logs in a persistent writer model requires closing the writer, truncating, and reopening.
            lock (_initLock)
            {
                try
                {
                    CloseWriter();
                    if (File.Exists(LogPath))
                    {
                        using (var fs = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        {
                            fs.SetLength(0);
                        }
                    }
                    _isInitialized = false;
                    EnsureWriterInitialized();
                    LogInfo("=== Yeni Oturum Başlatıldı, Loglar Temizlendi ===");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[LOG CLEAR ERROR] {ex.Message}");
                }
            }
        }

        public static void Shutdown()
        {
            _logChannel.Writer.TryComplete();
            _cts.Cancel();
            // Give it a moment to drain
            Task.Delay(500).Wait();
            CloseWriter();
        }

        private static void CloseWriter()
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
