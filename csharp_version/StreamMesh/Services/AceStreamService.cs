using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace StreamMesh.Services
{
    public class AceStreamService
    {
        private const string ACESTREAM_PATH_C = @"C:\Users\User\AppData\Roaming\ACEStream\engine\ace_engine.exe"; 
        // Gerçek kullanımda regedit'ten veya bilinen yollardan bulunmalı.
        // Şimdilik varsayılan port
        private const int ACESTREAM_PORT = 6878;

        public bool IsRunning()
        {
            Process[] pname = Process.GetProcessesByName("ace_engine");
            return pname.Length > 0;
        }

        public async Task StartEngineAsync()
        {
            if (IsRunning()) return;

            string[] possiblePaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"ACEStream\engine\ace_engine.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ACEStream\engine\ace_engine.exe"),
                @"C:\Program Files\ACEStream\engine\ace_engine.exe",
                @"C:\Program Files (x86)\ACEStream\engine\ace_engine.exe",
                @"C:\ACEStream\engine\ace_engine.exe"
            };

            string aceEnginePath = null;
            foreach (string p in possiblePaths)
            {
                if (File.Exists(p))
                {
                    aceEnginePath = p;
                    break;
                }
            }

            if (aceEnginePath != null)
            {
                LogService.Log($"AceStream Engine found at {aceEnginePath}. Starting...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = aceEnginePath,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                
                // Biraz bekle ki engine başlasın
                await Task.Delay(5000);
                LogService.Log("AceStream Engine start command sent and waited 5s.");
            }
            else
            {
                LogService.Log("AceStream engine not found in common paths.", "WARN");
            }
        }

        public string GetHttpUrl(string contentId)
        {
            // acestream:// kaldır
            if (contentId.StartsWith("acestream://"))
            {
                contentId = contentId.Substring("acestream://".Length);
            }

            return $"http://127.0.0.1:{ACESTREAM_PORT}/ace/getstream?id={contentId}";
        }

        public void KillEngine()
        {
            try
            {
                Process[] pname = Process.GetProcessesByName("ace_engine");
                foreach (var p in pname)
                {
                    p.Kill();
                }
                LogService.Log("AceStream processes killed.");
            }
            catch (Exception ex)
            {
                LogService.LogError("Failed to kill AceStream processes.", ex);
            }
        }
    }
}
