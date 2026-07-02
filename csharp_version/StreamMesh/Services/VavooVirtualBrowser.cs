using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StreamMesh.Models;
using Newtonsoft.Json;

namespace StreamMesh.Services
{
    public class VavooVirtualBrowser
    {
        private static VavooVirtualBrowser _instance;
        public static VavooVirtualBrowser Instance => _instance ??= new VavooVirtualBrowser();

        private WebView2 _webView;
        private bool _isInitialized = false;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);
        private const int MinRequestIntervalSeconds = 10;
        private readonly System.Net.Http.HttpClient _httpClient;
        
        // Hafızaya alınan kanallar
        public List<Channel> CachedChannels { get; private set; } = new List<Channel>();

        private VavooVirtualBrowser()
        {
            var handler = new System.Net.Http.HttpClientHandler
            {
                UseCookies = true,
                AllowAutoRedirect = true
            };

            _httpClient = new System.Net.Http.HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
        }

        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized) return;
            LogService.Log("[Vavoo] EnsureInitializedAsync started. Creating WebView2...");

            var tcs = new TaskCompletionSource<bool>();
            _ = Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    _webView = new WebView2();
                    var window = new Window
                    {
                        Width = 1024,
                        Height = 768,
                        WindowStyle = WindowStyle.SingleBorderWindow,
                        ShowInTaskbar = true,
                        Visibility = Visibility.Visible,
                        ShowActivated = true,
                        Title = "Vavoo Browser (Macro & Intercept Mode) - StreamMesh",
                        Topmost = true
                    };
                    window.Content = _webView;
                    window.Show();

                    string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StreamMesh_Vavoo");
                    LogService.Log($"[Vavoo] WebView2 environment path: {tempPath}");
                    var env = await CoreWebView2Environment.CreateAsync(null, tempPath);
                    await _webView.EnsureCoreWebView2Async(env);
                    
                    // Bot korumasını aşmak için user agent ve detaylı takip eventleri
                    _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                    
                    // Detaylı WebView logları
                    _webView.CoreWebView2.NavigationStarting += (s, args) =>
                    {
                        LogService.Log($"[Vavoo Debug] Navigation Starting to: {args.Uri}");
                    };

                    _webView.CoreWebView2.SourceChanged += (s, args) =>
                    {
                        LogService.Log($"[Vavoo Debug] Source Changed to: {_webView.Source}");
                    };

                    _webView.CoreWebView2.ContentLoading += (s, args) =>
                    {
                        LogService.Log("[Vavoo Debug] Content is loading...");
                    };

                    _webView.CoreWebView2.HistoryChanged += (s, args) =>
                    {
                        LogService.Log("[Vavoo Debug] History changed.");
                    };

                    _webView.CoreWebView2.NewWindowRequested += (s, args) =>
                    {
                        LogService.Log($"[Vavoo Debug] New Window Requested to: {args.Uri} | Handled and redirected to main view.");
                        args.Handled = true;
                        _webView.CoreWebView2.Navigate(args.Uri);
                    };

                    _webView.CoreWebView2.WebResourceResponseReceived += async (s, args) =>
                    {
                        try
                        {
                            string uri = args.Request?.Uri;
                            int statusCode = args.Response != null ? args.Response.StatusCode : 0;
                            if (!string.IsNullOrEmpty(uri))
                            {
                                if (uri.Contains("mediahubmx-catalog.json") && statusCode == 200)
                                {
                                    LogService.Log($"[Vavoo Intercept] Detected catalog URL: {uri}");
                                    var response = args.Response;
                                    var contentStream = await response.GetContentAsync();
                                    if (contentStream != null)
                                    {
                                        using (var reader = new System.IO.StreamReader(contentStream))
                                        {
                                            string json = await reader.ReadToEndAsync();
                                            LogService.Log($"[Vavoo Intercept] Intercepted mediahubmx-catalog.json. Size: {json.Length} chars.");
                                            ProcessCatalogJson(json);
                                        }
                                    }
                                }
                                else if (uri.Contains(".m3u8") || uri.Contains(".mpd") || uri.Contains("api") || uri.Contains("json") || uri.Contains("vavoo.to/live") || uri.Contains("play"))
                                {
                                    LogService.Log($"[Vavoo Debug Network] URI: {uri} | Status: {statusCode}");
                                    foreach (var header in args.Response.Headers)
                                    {
                                        if (header.Key.ToLower() == "location" || header.Key.ToLower() == "referrer" || header.Key.ToLower() == "referer")
                                        {
                                            LogService.Log($"[Vavoo Debug Network Header] {header.Key}: {header.Value}");
                                        }
                                    }
                                }
                            }
                        }
                        catch {}
                    };
                    _isInitialized = true;
                    LogService.Log("[Vavoo] WebView2 successfully initialized.");
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    LogService.LogError("[Vavoo] WebView2 initialization failed.", ex);
                    tcs.TrySetException(ex);
                }
            });
            
            await tcs.Task;
        }

        private static readonly string CacheFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vavoo_channels_cache.json");

        public async Task<string> FetchChannelLinkAsync(string channelUrl)
        {
            LogService.Log($"[Vavoo] FetchChannelLinkAsync invoked for: {channelUrl}");
            await _requestLock.WaitAsync();
            try
            {
                LogService.Log("[Vavoo] Lock acquired. Ensuring initialization...");
                await EnsureInitializedAsync();
                LogService.Log("[Vavoo] Initialization verified.");

                // 10 saniye limit kuralı
                var timeSinceLastRequest = DateTime.Now - _lastRequestTime;
                if (timeSinceLastRequest.TotalSeconds < MinRequestIntervalSeconds)
                {
                    int delayMs = (int)((MinRequestIntervalSeconds - timeSinceLastRequest.TotalSeconds) * 1000);
                    LogService.Log($"[Vavoo] Rate limit hit. Waiting {delayMs} ms before next request.");
                    await Task.Delay(delayMs);
                }

                _lastRequestTime = DateTime.Now;

                var tcs = new TaskCompletionSource<string>();
                LogService.Log($"[Vavoo] Navigating WebView to {channelUrl}");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs> handler = null;
                    EventHandler<CoreWebView2NavigationCompletedEventArgs> navHandler = null;

                    handler = (s, e) =>
                    {
                        string url = e.Request.Uri;
                        if (url.Contains(".m3u8"))
                        {
                            LogService.Log($"[Vavoo] .m3u8 found in web resources: {url}");
                            _webView.CoreWebView2.WebResourceResponseReceived -= handler;
                            if (navHandler != null) _webView.CoreWebView2.NavigationCompleted -= navHandler;
                            tcs.TrySetResult(url);
                            
                            // Videonun arka planda oynamaya devam etmesini ve ses yapmasını engellemek için boş sayfaya yönlendiriyoruz
                            _webView.CoreWebView2.Navigate("about:blank");
                        }
                    };

                    navHandler = async (s, e) =>
                    {
                        LogService.Log($"[Vavoo] NavigationCompleted. IsSuccess: {e.IsSuccess}, ErrorStatus: {e.WebErrorStatus}");
                        if (e.IsSuccess)
                        {
                            LogService.Log("[Vavoo] Navigation successful. Triggering play-simulation macro...");
                            try
                            {
                                // Give the page 1 second to parse DOM, then try clicking/playing
                                await Task.Delay(1000);
                                
                                string macroScript = @"
                                    (async function() {
                                        try {
                                            const wait = ms => new Promise(r => setTimeout(r, ms));
                                            let startTime = Date.now();
                                            
                                            while (Date.now() - startTime < 8000) {
                                                // Find play button, players, or icons
                                                let playButtons = Array.from(document.querySelectorAll('button, div, svg, path, span, a'))
                                                    .filter(el => {
                                                        let text = (el.innerText || '').toLowerCase();
                                                        let cls = (el.className || '');
                                                        if (typeof cls !== 'string') cls = '';
                                                        cls = cls.toLowerCase();
                                                        return cls.includes('play') || cls.includes('player') || text.includes('play') || el.getAttribute('aria-label')?.toLowerCase()?.includes('play');
                                                    });
                                                    
                                                for (let btn of playButtons) {
                                                    try { btn.click(); } catch(e) {}
                                                }
                                                
                                                // Try playing any video tags
                                                let videos = document.querySelectorAll('video');
                                                for (let video of videos) {
                                                    try {
                                                        video.play().catch(e => console.log('Autoplay play() blocked:', e));
                                                        video.click();
                                                    } catch(e) {}
                                                }
                                                
                                                await wait(1000);
                                            }
                                            return 'Macro finished';
                                        } catch(err) {
                                            return 'Macro error: ' + err.message;
                                        }
                                    })();
                                ";
                                var macroResult = await _webView.CoreWebView2.ExecuteScriptAsync(macroScript);
                                LogService.Log($"[Vavoo Macro] Play click macro result: {macroResult}");
                            }
                            catch (Exception macroEx)
                            {
                                LogService.LogError("[Vavoo Macro] Macro execution failed", macroEx);
                            }
                        }
                        else
                        {
                            LogService.Log($"[Vavoo] Navigation failed for {channelUrl}");
                        }
                    };

                    _webView.CoreWebView2.WebResourceResponseReceived += handler;
                    _webView.CoreWebView2.NavigationCompleted += navHandler;
                    
                    // 15 saniye zaman aşımı - m3u8 bulunamazsa döngüye girmesin
                    Task.Delay(15000).ContinueWith(_ => 
                    {
                        LogService.Log("[Vavoo] 15s timeout reached while waiting for .m3u8");
                        _webView.CoreWebView2.WebResourceResponseReceived -= handler;
                        if (navHandler != null) _webView.CoreWebView2.NavigationCompleted -= navHandler;
                        tcs.TrySetResult(null);
                    }, TaskScheduler.FromCurrentSynchronizationContext());

                    _webView.CoreWebView2.Navigate(channelUrl);
                });

                var resultUrl = await tcs.Task;
                LogService.Log($"[Vavoo] FetchChannelLinkAsync returning: {(string.IsNullOrEmpty(resultUrl) ? "null" : resultUrl)}");
                return resultUrl;
            }
            catch (Exception ex)
            {
                LogService.LogError($"[Vavoo] VavooVirtualBrowser FetchChannelLinkAsync error for {channelUrl}", ex);
                return null;
            }
            finally
            {
                _requestLock.Release();
                LogService.Log("[Vavoo] Lock released.");
            }
        }

        public event EventHandler CatalogLoaded;
        private readonly object _channelLock = new object();

        private void ProcessCatalogJson(string json)
        {
            try
            {
                var catalog = JsonConvert.DeserializeObject<MediaHubCatalog>(json);
                if (catalog?.Metas != null && catalog.Metas.Count > 0)
                {
                    lock (_channelLock)
                    {
                        CachedChannels.Clear();
                        foreach (var meta in catalog.Metas)
                        {
                            string category = "GENERAL";
                            if (meta.Genres != null && meta.Genres.Count > 0)
                            {
                                category = meta.Genres[0].ToUpper();
                            }

                            // Watch/Play URL on vavoo
                            string playUrl = $"https://vavoo.to/play/{meta.Id}";

                            CachedChannels.Add(new Channel
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                Name = meta.Name,
                                Url = playUrl,
                                Category = category,
                                SourceType = "VAVOO",
                                LogoUrl = meta.Poster
                            });
                        }
                    }
                    LogService.Log($"[Vavoo] Intercepted and parsed {CachedChannels.Count} channels from mediahubmx-catalog.json!");
                    
                    // Save to local cache!
                    try
                    {
                        System.IO.File.WriteAllText(CacheFilePath, JsonConvert.SerializeObject(CachedChannels, Formatting.Indented));
                        LogService.Log("[Vavoo Cache] Saved intercepted channels to local cache.");
                    }
                    catch (Exception cacheEx)
                    {
                        LogService.LogError("[Vavoo Cache] Failed to save intercepted channels to cache.", cacheEx);
                    }

                    CatalogLoaded?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[Vavoo] ProcessCatalogJson failed", ex);
            }
        }

        public async Task LoadChannelsToMemoryAsync()
        {
            LogService.Log("[Vavoo] LoadChannelsToMemoryAsync invoked.");
            
            // 1. Check local cache first!
            if (System.IO.File.Exists(CacheFilePath))
            {
                try
                {
                    var fileInfo = new System.IO.FileInfo(CacheFilePath);
                    // Caching is large and long (15 days) as requested!
                    if (DateTime.Now - fileInfo.LastWriteTime < TimeSpan.FromDays(15))
                    {
                        string cachedJson = System.IO.File.ReadAllText(CacheFilePath);
                        var channels = JsonConvert.DeserializeObject<List<Channel>>(cachedJson);
                        if (channels != null && channels.Count > 0)
                        {
                            lock (_channelLock)
                            {
                                CachedChannels = channels;
                            }
                            LogService.Log($"[Vavoo Cache] Loaded {CachedChannels.Count} channels from local cache successfully! Cache age: {(DateTime.Now - fileInfo.LastWriteTime).TotalDays:F1} days.");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("[Vavoo Cache] Failed to load channels from local cache. Rebuilding cache...", ex);
                }
            }

            // 2. If no valid cache, load from web/scraping
            await _requestLock.WaitAsync();
            try
            {
                if (CachedChannels.Count > 0)
                {
                    LogService.Log("[Vavoo] Channels already loaded in memory, skipping navigation.");
                    return;
                }

                await EnsureInitializedAsync();

                var tcs = new TaskCompletionSource<bool>();
                EventHandler catalogLoadedHandler = null;
                catalogLoadedHandler = (s, e) =>
                {
                    CatalogLoaded -= catalogLoadedHandler;
                    tcs.TrySetResult(true);
                };
                CatalogLoaded += catalogLoadedHandler;

                // 25 seconds timeout for scraping/loading
                _ = Task.Delay(25000).ContinueWith(_ =>
                {
                    CatalogLoaded -= catalogLoadedHandler;
                    tcs.TrySetResult(false);
                });

                LogService.Log("[Vavoo] Navigating WebView to https://vavoo.to/live to scrape or trigger catalog load...");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Register NavigationCompleted to run custom scraping macro as a fallback!
                    EventHandler<CoreWebView2NavigationCompletedEventArgs> navCompletedScraper = null;
                    navCompletedScraper = async (s, e) =>
                    {
                        _webView.CoreWebView2.NavigationCompleted -= navCompletedScraper;
                        if (e.IsSuccess)
                        {
                            LogService.Log("[Vavoo Scraper] Page loaded. Starting scraping macro in 3 seconds...");
                            try
                            {
                                await Task.Delay(3000);
                                
                                string scrapeScript = @"
                                    (async function() {
                                        try {
                                            const wait = ms => new Promise(r => setTimeout(r, ms));
                                            
                                            const openDropdown = async () => {
                                                let buttons = Array.from(document.querySelectorAll('div, button, span, a')).filter(el => {
                                                    let txt = (el.innerText || '').toUpperCase().trim();
                                                    return txt === 'UNITED KINGDOM' || txt === 'GERMANY' || txt === 'TURKEY' || txt === 'ALL COUNTRIES' || txt === 'ALL' || txt === 'TRENDING TV CHANNELS' || el.className?.toLowerCase()?.includes('select') || el.className?.toLowerCase()?.includes('dropdown');
                                                });
                                                for (let el of buttons) {
                                                    try {
                                                        el.click();
                                                        await wait(600);
                                                        let hasOptions = Array.from(document.querySelectorAll('div, li, span, a')).some(opt => {
                                                            let t = (opt.innerText || '').toLowerCase().trim();
                                                            return t === 'germany' || t === 'turkey' || t === 'france' || t === 'united kingdom';
                                                        });
                                                        if (hasOptions) return true;
                                                    } catch(e) {}
                                                }
                                                return false;
                                            };

                                            const extractChannelsOnPage = (categoryName) => {
                                                let list = [];
                                                let anchors = Array.from(document.querySelectorAll('a'));
                                                for (let a of anchors) {
                                                    let href = a.getAttribute('href') || '';
                                                    let hrefLower = href.toLowerCase();
                                                    
                                                    if ((hrefLower.includes('/play/') || hrefLower.includes('/watch/') || hrefLower.includes('/live/') || hrefLower.includes('live=')) &&
                                                        hrefLower !== '/live' && hrefLower !== '/live/' && !hrefLower.includes('/live-list') &&
                                                        !hrefLower.includes('/search') && !hrefLower.includes('/series') && !hrefLower.includes('/movies')) {
                                                        
                                                        let name = a.innerText.trim();
                                                        if (name) {
                                                            list.push({
                                                                Name: name,
                                                                Url: a.href,
                                                                Category: categoryName
                                                            });
                                                        }
                                                    }
                                                }
                                                return list;
                                            };

                                            let resultChannels = [];
                                            
                                            let initialList = extractChannelsOnPage('United Kingdom');
                                            if (initialList.length > 0) {
                                                resultChannels = resultChannels.concat(initialList);
                                            }

                                            let targetCountries = ['Turkey', 'Germany', 'United Kingdom', 'France', 'Italy', 'Spain', 'Albania', 'Poland', 'Russia', 'Portugal'];

                                            for (let country of targetCountries) {
                                                let opened = await openDropdown();
                                                if (!opened) {
                                                    let selectDiv = document.querySelector('[class*=\"select\"], [class*=\"dropdown\"], [class*=\"trigger\"], button');
                                                    if (selectDiv) {
                                                        try {
                                                            selectDiv.click();
                                                            await wait(600);
                                                        } catch(e) {}
                                                    }
                                                }

                                                let option = Array.from(document.querySelectorAll('div, li, span, a')).find(opt => {
                                                    let txt = (opt.innerText || '').toLowerCase().trim();
                                                    return txt === country.toLowerCase();
                                                });

                                                if (option) {
                                                    try {
                                                        option.click();
                                                        await wait(2000);
                                                        let countryChannels = extractChannelsOnPage(country);
                                                        if (countryChannels.length > 0) {
                                                            resultChannels = resultChannels.concat(countryChannels);
                                                        }
                                                    } catch(e) {}
                                                }
                                            }

                                            return JSON.stringify(resultChannels);
                                        } catch(err) {
                                            return JSON.stringify([]);
                                        }
                                    })();
                                ";
                                string scrapeResultJson = await _webView.CoreWebView2.ExecuteScriptAsync(scrapeScript);
                                if (!string.IsNullOrEmpty(scrapeResultJson) && scrapeResultJson != "null")
                                {
                                    List<ChannelData> scrapedList = null;
                                    try
                                    {
                                        // 1. Try to deserialize directly
                                        scrapedList = JsonConvert.DeserializeObject<List<ChannelData>>(scrapeResultJson);
                                    }
                                    catch
                                    {
                                        try
                                        {
                                            // 2. Try unescaping first
                                            string unescaped = JsonConvert.DeserializeObject<string>(scrapeResultJson);
                                            if (!string.IsNullOrEmpty(unescaped))
                                            {
                                                scrapedList = JsonConvert.DeserializeObject<List<ChannelData>>(unescaped);
                                            }
                                        }
                                        catch (Exception ex2)
                                        {
                                            LogService.LogError("[Vavoo Scraper] Failed both direct and unescaped deserialization.", ex2);
                                        }
                                    }

                                    if (scrapedList != null && scrapedList.Count > 0)
                                    {
                                        lock (_channelLock)
                                        {
                                            CachedChannels.Clear();
                                            foreach (var item in scrapedList)
                                            {
                                                CachedChannels.Add(new Channel
                                                {
                                                    Id = Guid.NewGuid().ToString("N"),
                                                    Name = item.Name,
                                                    Url = item.Url,
                                                    Category = item.Category,
                                                    SourceType = "VAVOO"
                                                });
                                            }
                                        }
                                        LogService.Log($"[Vavoo Scraper] Scraped {CachedChannels.Count} channels via DOM macro!");
                                        
                                        try
                                        {
                                            System.IO.File.WriteAllText(CacheFilePath, JsonConvert.SerializeObject(CachedChannels, Formatting.Indented));
                                            LogService.Log("[Vavoo Cache] Saved scraped channels to local cache.");
                                        }
                                        catch (Exception cacheEx)
                                        {
                                            LogService.LogError("[Vavoo Cache] Failed to save channels to local cache.", cacheEx);
                                        }

                                        tcs.TrySetResult(true);
                                    }
                                }
                            }
                            catch (Exception scrapeEx)
                            {
                                LogService.LogError("[Vavoo Scraper] Scraper macro failed", scrapeEx);
                            }
                        }
                    };

                    _webView.CoreWebView2.NavigationCompleted += navCompletedScraper;
                    _webView.CoreWebView2.Navigate("https://vavoo.to/live");
                });

                bool loaded = await tcs.Task;
                LogService.Log($"[Vavoo] LoadChannelsToMemoryAsync completed. Success: {loaded}, Count: {CachedChannels.Count}");
            }
            catch (Exception ex)
            {
                LogService.LogError("[Vavoo] LoadChannelsToMemoryAsync error", ex);
            }
            finally
            {
                _requestLock.Release();
                LogService.Log("[Vavoo] LoadChannelsToMemoryAsync lock released.");
            }
        }

        public class MediaHubCatalog
        {
            [JsonProperty("metas")]
            public List<MediaHubMeta> Metas { get; set; }
        }

        public class MediaHubMeta
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("genres")]
            public List<string> Genres { get; set; }

            [JsonProperty("poster")]
            public string Poster { get; set; }
        }

        private class ChannelData 
        {
            public string Name { get; set; }
            public string Url { get; set; }
            public string Category { get; set; }
        }
    }
}
