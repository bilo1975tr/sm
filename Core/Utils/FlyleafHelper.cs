using System;
using System.IO;
using System.Windows;
using FlyleafLib;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Utils
{
    public static class FlyleafHelper
    {
        private static bool _isStarted = false;
        private static readonly object _lock = new object();

        public static void SafeStart()
        {
            if (_isStarted) return;

            lock (_lock)
            {
                if (_isStarted) return;

                try
                {
                    string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");

                    // Wait up to 35 seconds for FFmpeg download to complete if missing
                    for (int i = 0; i < 35; i++)
                    {
                        if (Directory.Exists(ffmpegPath) && Directory.GetFiles(ffmpegPath, "avcodec*.dll").Length > 0)
                        {
                            break;
                        }
                        Task.Delay(1000).Wait();
                    }

                    Engine.Start(new EngineConfig()
                    {
                        FFmpegPath = ffmpegPath,
                        PluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins"),
                        UIRefresh = true,
                        UIRefreshInterval = 250
                    });

                    _isStarted = true;
                    LogService.LogInfo("FlyleafHelper: Engine started successfully.");
                }
                catch (Exception ex)
                {
                    LogService.LogError("FlyleafHelper: Failed to start Engine", ex);
                }
            }
        }
    }
}
