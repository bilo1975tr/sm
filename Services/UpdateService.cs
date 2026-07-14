using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using StreamMesh.Services;

namespace StreamMesh.Services
{
    public class UpdateProgressWindow : Window
    {
        private ProgressBar _progressBar;
        private TextBlock _statusText;

        public UpdateProgressWindow()
        {
            this.Title = "StreamMesh Güncelleme Servisi";
            this.Width = 420;
            this.Height = 140;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18181C"));
            this.Foreground = Brushes.White;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3F3F46"));
            this.BorderThickness = new Thickness(1);

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Title
            var title = new TextBlock
            {
                Text = "StreamMesh Güncelleme",
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Status Text
            _statusText = new TextBlock
            {
                Text = "Güncelleme bilgileri alınıyor...",
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A1A1AA")),
                Margin = new Thickness(0, 4, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_statusText, 1);
            grid.Children.Add(_statusText);

            // Progress Bar
            _progressBar = new ProgressBar
            {
                Height = 8,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27272A")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
                BorderThickness = new Thickness(0)
            };
            Grid.SetRow(_progressBar, 2);
            grid.Children.Add(_progressBar);

            this.Content = grid;
        }

        public void UpdateProgress(double percentage, string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(percentage, status));
                return;
            }
            _progressBar.Value = percentage;
            _statusText.Text = status;
        }
    }

    public class UpdateService
    {
        private const string VersionUrl = "https://raw.githubusercontent.com/bilo1975tr/sm/main/VERSION";
        private const string ReleaseUrl = "https://github.com/bilo1975tr/sm/releases/latest";
        private const string GithubApiUrl = "https://api.github.com/repos/bilo1975tr/sm/releases/latest";

        public static string GetCurrentVersion()
        {
            try
            {
                string versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../VERSION");
                if (File.Exists(versionFile))
                {
                    var lines = File.ReadAllLines(versionFile);
                    if (lines.Length > 0) return lines[0].Trim();
                }
                
                versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VERSION");
                if (File.Exists(versionFile))
                {
                    var lines = File.ReadAllLines(versionFile);
                    if (lines.Length > 0) return lines[0].Trim();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("Version okuma hatasi", ex);
            }
            return "1.0.8"; // Fallback to current major version
        }

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                string currentVersionStr = GetCurrentVersion();
                LogService.Log($"[Güncelleme] Mevcut sürüm kontrol ediliyor: {currentVersionStr}");
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("StreamMesh-Updater");
                    var response = await client.GetAsync(VersionUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string fullContent = await response.Content.ReadAsStringAsync();
                        string remoteVersionStr = "";
                        using (var reader = new StringReader(fullContent)) {
                            remoteVersionStr = reader.ReadLine()?.Trim() ?? "";
                        }

                        LogService.Log($"[Güncelleme] GitHub üzerindeki uzak sürüm: {remoteVersionStr}");

                        if (!string.IsNullOrEmpty(remoteVersionStr))
                        {
                            if (IsNewerVersion(currentVersionStr, remoteVersionStr))
                            {
                                LogService.Log("[Güncelleme] Yeni bir güncelleme mevcut! Kullanıcıya bildiriliyor...");
                                ShowUpdatePrompt(remoteVersionStr);
                            }
                            else
                            {
                                LogService.Log("[Güncelleme] Uygulama güncel.");
                            }
                        }
                    }
                    else
                    {
                        LogService.Log($"[Güncelleme] Uzak sürüm dosyası alınamadı. Durum kodu: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("Guncelleme kontrol hatasi", ex);
            }
        }

        private static bool IsNewerVersion(string currentVersionStr, string remoteVersionStr)
        {
            try
            {
                if (string.IsNullOrEmpty(currentVersionStr) || string.IsNullOrEmpty(remoteVersionStr))
                    return false;

                string cleanCurrent = NormalizeVersionString(currentVersionStr);
                string cleanRemote = NormalizeVersionString(remoteVersionStr);

                if (Version.TryParse(cleanCurrent, out var currentVer) && Version.TryParse(cleanRemote, out var remoteVer))
                {
                    return remoteVer > currentVer;
                }

                // Fallback to integer conversion
                int curNum = ExtractFallbackVersionNumber(cleanCurrent);
                int remNum = ExtractFallbackVersionNumber(cleanRemote);
                return remNum > curNum;
            }
            catch (Exception ex)
            {
                LogService.LogError("IsNewerVersion hatasi", ex);
            }
            return false;
        }

        private static string NormalizeVersionString(string versionStr)
        {
            if (string.IsNullOrEmpty(versionStr)) return "";
            string val = versionStr.Trim().TrimStart('v', 'V');
            int spaceIdx = val.IndexOf(' ');
            if (spaceIdx > 0) val = val.Substring(0, spaceIdx);
            int dashIdx = val.IndexOf('-');
            if (dashIdx > 0) val = val.Substring(0, dashIdx);
            return val;
        }

        private static int ExtractFallbackVersionNumber(string cleanVersionStr)
        {
            if (int.TryParse(cleanVersionStr, out int num))
            {
                return num;
            }
            string digits = "";
            foreach (char c in cleanVersionStr)
            {
                if (char.IsDigit(c)) digits += c;
            }
            if (int.TryParse(digits, out int res)) return res;
            return 0;
        }

        private static void ShowUpdatePrompt(string newVersion)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var result = MessageBox.Show(
                    $"Uygulamanın yeni bir sürümü mevcut (v{newVersion}).\n\nŞimdi otomatik olarak indirip güncellemek ister misiniz?\n\n(Güncelleme indirilirken uygulama açık kalacaktır.)",
                    "StreamMesh Güncelleme",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    _ = DownloadAndInstallUpdateAsync(newVersion);
                }
            });
        }

        private static async Task DownloadAndInstallUpdateAsync(string remoteVersion)
        {
            UpdateProgressWindow progressWindow = null;
            try
            {
                // Create progress window on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    progressWindow = new UpdateProgressWindow();
                    progressWindow.Show();
                });

                string downloadUrl = null;
                bool isZip = true;
                string downloadName = "";

                // 1. Fetch release assets from GitHub API to get the ZIP or EXE URL
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("StreamMesh-Updater");
                    progressWindow.UpdateProgress(5, "Güncelleme paketi bilgileri alınıyor...");
                    
                    var resString = await client.GetStringAsync(GithubApiUrl);
                    var json = JObject.Parse(resString);
                    var assets = json["assets"] as JArray;
                    
                    if (assets != null)
                    {
                        // First search for .zip asset
                        foreach (var asset in assets)
                        {
                            string name = asset["name"]?.ToString();
                            if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset["browser_download_url"]?.ToString();
                                isZip = true;
                                downloadName = name;
                                break;
                            }
                        }

                        // If no .zip asset is found, search for standalone .exe asset
                        if (string.IsNullOrEmpty(downloadUrl))
                        {
                            foreach (var asset in assets)
                            {
                                string name = asset["name"]?.ToString();
                                if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                                {
                                    downloadUrl = asset["browser_download_url"]?.ToString();
                                    isZip = false;
                                    downloadName = name;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new Exception("GitHub sürümünde ZIP veya EXE paketi bulunamadı.");
                }

                // 2. Download file to temporary path
                string tempFilePath = Path.Combine(Path.GetTempPath(), isZip ? "StreamMesh_Update.zip" : "StreamMesh_Update.exe");
                
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("StreamMesh-Updater");
                    using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        var totalBytes = response.Content.Headers.ContentLength;
                        
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0;
                            int read;
                            
                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;
                                
                                double percent = totalBytes.HasValue ? ((double)totalRead / totalBytes.Value * 100) : 0;
                                string mbRead = (totalRead / 1024.0 / 1024.0).ToString("F1");
                                string mbTotal = totalBytes.HasValue ? (totalBytes.Value / 1024.0 / 1024.0).ToString("F1") : "?";
                                
                                progressWindow.UpdateProgress(percent, $"İndiriliyor: %{percent:F0} ({mbRead} MB / {mbTotal} MB)");
                            }
                        }
                    }
                }

                // 3. Write and launch PowerShell updater script to target AppData (permission-free)
                progressWindow.UpdateProgress(100, "Güncelleme yükleniyor, uygulama kapatılıyor...");
                await Task.Delay(1500); // Give user a moment to read the finished state

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string installDir = Path.Combine(localAppData, "StreamMesh").Replace("\\", "/");
                string currentBaseDir = AppDomain.CurrentDomain.BaseDirectory.Replace("\\", "/");
                string updaterScriptPath = Path.Combine(Path.GetTempPath(), "StreamMesh_Updater.ps1");
                int currentPid = Process.GetCurrentProcess().Id;
                string cleanTempFilePath = tempFilePath.Replace("\\", "/");

                string psScript = $@"
Start-Sleep -Seconds 1

# Processin kapandigindan emin olalim
while (Get-Process -Id {currentPid} -ErrorAction SilentlyContinue) {{
    Start-Sleep -Milliseconds 200
}}

try {{
    # Hedef klasoru olustur (yetki gerektirmez)
    if (!(Test-Path ""{installDir}"")) {{
        New-Item -ItemType Directory -Path ""{installDir}"" -Force | Out-Null
    }}

    # Mevcut klasordeki dosyalari yedekle/kopyala (Eski exe ve pdb haric her seyi kopyala)
    $currentBase = ""{currentBaseDir}""
    if ($currentBase -ne ""{installDir}"") {{
        Get-ChildItem -Path $currentBase -Exclude ""StreamMesh.exe"", ""StreamMesh.pdb"" | ForEach-Object {{
            $dest = Join-Path ""{installDir}"" $_.Name
            Copy-Item -Path $_.FullName -Destination $dest -Recurse -Force -ErrorAction SilentlyContinue | Out-Null
        }}
    }}

    if (""{isZip.ToString().ToLower()}"" -eq ""true"") {{
        $tempExtract = Join-Path $env:TEMP ""StreamMesh_Extract""
        if (Test-Path $tempExtract) {{ Remove-Item -Recurse -Force $tempExtract }}
        New-Item -ItemType Directory -Path $tempExtract | Out-Null

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory(""{cleanTempFilePath}"", $tempExtract)

        $subDirs = Get-ChildItem -Path $tempExtract
        $sourcePath = $tempExtract
        if ($subDirs.Count -eq 1 -and $subDirs[0].PSIsContainer) {{
            $sourcePath = $subDirs[0].FullName
        }}

        Start-Sleep -Seconds 1

        # Dosyalari kopyala ve uzerine yaz
        Copy-Item -Path ""$sourcePath\*"" -Destination ""{installDir}"" -Recurse -Force | Out-Null

        # Temizlik
        Remove-Item -Recurse -Force $tempExtract | Out-Null
        Remove-Item -Force ""{cleanTempFilePath}"" | Out-Null
    }} else {{
        # Tek bir EXE indirildiyse dogrudan kopyala
        Copy-Item -Path ""{cleanTempFilePath}"" -Destination (Join-Path ""{installDir}"" ""StreamMesh.exe"") -Force | Out-Null
        Remove-Item -Force ""{cleanTempFilePath}"" | Out-Null
    }}
}} catch {{
    [System.Windows.MessageBox]::Show(""Güncelleme yüklenirken bir hata oluştu: "" + $_.Exception.Message, ""StreamMesh Güncelleme Hatası"")
}}

# Yeniden baslat
Start-Process -FilePath (Join-Path ""{installDir}"" ""StreamMesh.exe"")

# Kendi kendini yok et
Remove-Item -Force ""{updaterScriptPath}""
";

                File.WriteAllText(updaterScriptPath, psScript, System.Text.Encoding.UTF8);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{updaterScriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);
                
                // Shutdown the current application instance safely
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("Otomatik guncelleme hatasi", ex);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (progressWindow != null) progressWindow.Close();
                    MessageBox.Show(
                        $"Güncelleme indirilirken bir hata oluştu. Lütfen manuel olarak indirmeyi deneyin.\n\nHata: {ex.Message}",
                        "Güncelleme Hatası",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }
    }
}
