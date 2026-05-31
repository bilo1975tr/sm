using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using StreamMesh.Services;

namespace StreamMesh.Services
{
    public class UpdateService
    {
        private const string VersionUrl = "https://raw.githubusercontent.com/bilo1975tr/sm/main/VERSION";
        private const string ReleaseUrl = "https://github.com/bilo1975tr/sm/releases/latest";

        public static string GetCurrentVersion()
        {
            try
            {
                string versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../VERSION");
                if (File.Exists(versionFile))
                    return File.ReadAllText(versionFile).Trim();
                
                versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VERSION");
                if (File.Exists(versionFile))
                    return File.ReadAllText(versionFile).Trim();
            }
            catch (Exception ex)
            {
                LogService.LogError("Version okuma hatasi", ex);
            }
            return "0.0 alfa 00000";
        }

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                string currentVersionStr = GetCurrentVersion();
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(VersionUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string remoteVersionStr = (await response.Content.ReadAsStringAsync()).Trim();

                        if (!string.IsNullOrEmpty(remoteVersionStr) && remoteVersionStr != currentVersionStr)
                        {
                            // Basit numara kontrolu, ornegin "0.0 alfa 00071" -> "71"
                            int currentNum = ExtractVersionNumber(currentVersionStr);
                            int remoteNum = ExtractVersionNumber(remoteVersionStr);

                            if (remoteNum > currentNum)
                            {
                                ShowUpdatePrompt(remoteVersionStr);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("Guncelleme kontrol hatasi", ex);
            }
        }

        private static int ExtractVersionNumber(string versionStr)
        {
            try
            {
                var parts = versionStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    string lastPart = parts[parts.Length - 1];
                    if (int.TryParse(lastPart, out int num))
                    {
                        return num;
                    }
                }
            }
            catch { }
            return 0;
        }

        private static void ShowUpdatePrompt(string newVersion)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var result = MessageBox.Show(
                    $"Uygulamanın yeni bir sürümü mevcut (v{newVersion}).\n\nŞimdi indirip kurmak ister misiniz?",
                    "Güncelleme Uyarı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = ReleaseUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("Release sayfasi acilamadi", ex);
                    }
                }
            });
        }
    }
}
