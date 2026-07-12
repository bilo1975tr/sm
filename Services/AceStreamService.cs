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
            RegisterBrowserExtension();

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

        public void RegisterBrowserExtension()
        {
            try
            {
                string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string aceStreamDir = Path.Combine(appDataRoaming, "AceStream");
                if (!Directory.Exists(aceStreamDir))
                {
                    aceStreamDir = Path.Combine(appDataRoaming, "ACEStream");
                }

                string engineDir = Path.Combine(aceStreamDir, "engine");
                string nativeHostExe = Path.Combine(engineDir, "ace_chrome_native_messaging_host.exe");

                if (!Directory.Exists(engineDir) || !File.Exists(nativeHostExe))
                {
                    LogService.Log("ace_chrome_native_messaging_host.exe bulunamadı, tarayıcı eklenti kaydı yapılamadı.", "WARN");
                    return;
                }

                string manifestPath = Path.Combine(engineDir, "org.acestream.engine.json");
                string escapedExePath = nativeHostExe.Replace("\\", "\\\\");

                string manifestContent = @"{
  ""name"": ""org.acestream.engine"",
  ""description"": ""Ace Stream Engine"",
  ""path"": """ + escapedExePath + @""",
  ""type"": ""stdio"",
  ""allowed_origins"": [
    ""chrome-extension://mjbepbhonbojpoaenhckjocchgfiaofo/"",
    ""chrome-extension://jgbnehibmelahoclnbljocnknpndcekl/"",
    ""chrome-extension://ieidgknbghmbihihlgjedmhmdfbgieng/""
  ]
}";

                File.WriteAllText(manifestPath, manifestContent);
                LogService.Log($"AceStream manifest yazıldı: {manifestPath}");

                using (var chromeKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Google\Chrome\NativeMessagingHosts\org.acestream.engine"))
                {
                    chromeKey?.SetValue("", manifestPath);
                }
                using (var edgeKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Edge\NativeMessagingHosts\org.acestream.engine"))
                {
                    edgeKey?.SetValue("", manifestPath);
                }
                using (var firefoxKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Mozilla\NativeMessagingHosts\org.acestream.engine"))
                {
                    firefoxKey?.SetValue("", manifestPath);
                }

                LogService.Log("AceStream tarayıcı eklentisi entegrasyonu (Native Messaging Host) başarıyla kaydedildi.");
            }
            catch (Exception ex)
            {
                LogService.LogError("AceStream tarayıcı eklenti kaydı başarısız oldu.", ex);
            }
        }
    }
}
