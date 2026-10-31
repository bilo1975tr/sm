using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StreamMesh.Models;
using StreamMesh.Core.Utils;
using StreamMesh.Core.Media;
using StreamMesh.Core.Network;
using StreamMesh.Core.Database.Repositories;

namespace StreamMesh.Core.Database
{
    public class DatabaseEngine
    {
        private readonly string _dbPath;
        private string ConnectionString => $"Data Source={_dbPath};Default Timeout=10;Pooling=True;";

        private static readonly System.Threading.SemaphoreSlim AsyncDbLock = new System.Threading.SemaphoreSlim(1, 1);
        private static bool _suppressEvents = false;
        private static bool _hasPendingUpdate = false;
        public static bool SuppressEvents
        {
            get => _suppressEvents;
            set
            {
                _suppressEvents = value;
                if (!_suppressEvents && _hasPendingUpdate)
                {
                    _hasPendingUpdate = false;
                    NotifyDatabaseUpdated();
                }
            }
        }

        private readonly SettingsRepository _settings;
        private readonly ChannelRepository _channels;
        private readonly EpgRepository _epg;
        private readonly MetadataRepository _metadata;
        private readonly SourceRepository _sources;
        private readonly IptvRepository _iptv;
        private readonly LogoRepository _logo;

        private static readonly TaskCompletionSource<bool> _initTcs = new();
        private static bool _isInitializing = false;

        public DatabaseEngine()
        {
            // V1.8.5: Database is now kept in the application folder for better reliability and speed.
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database_v2.db");
            _settings = new SettingsRepository(ConnectionString);
            _channels = new ChannelRepository(ConnectionString, AsyncDbLock);
            _epg = new EpgRepository(ConnectionString, AsyncDbLock);
            _metadata = new MetadataRepository(ConnectionString, AsyncDbLock);
            _sources = new SourceRepository(ConnectionString);
            _iptv = new IptvRepository(ConnectionString);
            _logo = new LogoRepository(ConnectionString);
        }

        public async Task InitializeAsync()
        {
            lock (_initTcs)
            {
                if (_isInitializing) return;
                _isInitializing = true;
            }

            try
            {
                await InitializeDatabase().ConfigureAwait(false);
                _initTcs.TrySetResult(true);
                // Background heavy maintenance
                _ = Task.Run(() => PostStartupMaintenanceAsync());
            }
            catch (Exception ex)
            {
                LogService.LogError("DatabaseEngine: Global Init Failed", ex);
                _initTcs.TrySetException(ex);
                throw;
            }
        }

        public static Task WaitForInitAsync() => _initTcs.Task;

        private async Task InitializeDatabase()
        {
            var directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();

                using (var pragmaCmd = connection.CreateCommand())
                {
                    pragmaCmd.CommandText = @"
                        PRAGMA journal_mode = WAL;
                        PRAGMA synchronous = NORMAL;
                        PRAGMA busy_timeout = 5000;
                        PRAGMA cache_size = -32000;
                        PRAGMA mmap_size = 268435456;
                        PRAGMA temp_store = MEMORY;
                    ";
                    await pragmaCmd.ExecuteNonQueryAsync();
                }

                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AppSettings (
                        Key TEXT PRIMARY KEY,
                        Value TEXT
                    );
                    CREATE TABLE IF NOT EXISTS Channels (
                        Id TEXT PRIMARY KEY,
                        Name TEXT,
                        EpgId TEXT DEFAULT '',
                        EpgUrl TEXT DEFAULT '',
                        Url TEXT,
                        GroupTitle TEXT,
                        LogoUrl TEXT,
                        SourceType TEXT,
                        AddedDate INTEGER,
                        Category TEXT,
                        Language TEXT,
                        PlaylistUrl TEXT,
                        IsFavorite INTEGER DEFAULT 0,
                        IsVerified INTEGER DEFAULT 0,
                        PersonalWatchCount INTEGER DEFAULT 0,
                        IsLocked INTEGER DEFAULT 0,
                        Notes TEXT DEFAULT '',
                        IsPremium INTEGER DEFAULT 0,
                        IsWatched INTEGER DEFAULT 0,
                        ImdbId TEXT DEFAULT '',
                        Overview TEXT DEFAULT '',
                        BackdropUrl TEXT DEFAULT '',
                        [Cast] TEXT DEFAULT '',
                        ViewersCount INTEGER DEFAULT 0,
                        UrlSpeeds TEXT DEFAULT '',
                        PreferredNameIndex INTEGER DEFAULT 0,
                        PreferredUrlIndex INTEGER DEFAULT 0,
                        PreferredLogoIndex INTEGER DEFAULT 0,
                        PreferredEpgIndex INTEGER DEFAULT 0,
                        M3uLineNumber INTEGER DEFAULT 0,
                        RawM3uBlock TEXT DEFAULT ''
                    );
                    CREATE TABLE IF NOT EXISTS EpgPrograms (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ChannelName TEXT,
                        Title TEXT,
                        Description TEXT,
                        StartTime TEXT,
                        EndTime TEXT,
                        SourceUrl TEXT
                    );
                    CREATE TABLE IF NOT EXISTS EpgChannels (
                        EpgId TEXT,
                        Name TEXT,
                        LogoUrl TEXT,
                        SourceUrl TEXT,
                        PRIMARY KEY (EpgId, SourceUrl)
                    );
                    CREATE TABLE IF NOT EXISTS M3uSources (
                        Url TEXT PRIMARY KEY,
                        ForcedLanguage TEXT DEFAULT 'und',
                        ForcedCategory TEXT DEFAULT 'TV',
                        AddedDate INTEGER,
                        IsDefault INTEGER DEFAULT 0
                    );
                    CREATE TABLE IF NOT EXISTS EpgSources (
                        Url TEXT PRIMARY KEY,
                        AddedDate INTEGER,
                        Status TEXT DEFAULT 'Bekliyor',
                        ChannelCount INTEGER DEFAULT 0,
                        ProgramCount INTEGER DEFAULT 0,
                        LastUpdated INTEGER DEFAULT 0,
                        TimeShiftHours INTEGER DEFAULT 0,
                        ErrorMessage TEXT DEFAULT ''
                    );
                    CREATE TABLE IF NOT EXISTS IptvAccounts (
                        Id TEXT PRIMARY KEY,
                        Name TEXT,
                        ServerUrl TEXT,
                        Username TEXT,
                        Password TEXT,
                        Status TEXT,
                        ExpiryDate TEXT
                    );
                    CREATE TABLE IF NOT EXISTS LogoIndex (
                        Key TEXT PRIMARY KEY,
                        FileName TEXT
                    );
                    CREATE TABLE IF NOT EXISTS MetadataPool (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SearchQuery TEXT,
                        ImdbId TEXT DEFAULT '',
                        Title TEXT,
                        PosterUrl TEXT DEFAULT '',
                        BackdropUrl TEXT DEFAULT '',
                        Overview TEXT DEFAULT '',
                        [Cast] TEXT DEFAULT '',
                        Director TEXT DEFAULT '',
                        TrailerUrl TEXT DEFAULT '',
                        ReleaseDate TEXT DEFAULT '',
                        VoteAverage REAL DEFAULT 0.0,
                        CreatedAt TEXT,
                        MediaType TEXT DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_channels_playlisturl ON Channels (PlaylistUrl);
                    CREATE INDEX IF NOT EXISTS idx_channels_category ON Channels (Category);
                    CREATE INDEX IF NOT EXISTS idx_channels_name ON Channels (Name);
                    CREATE INDEX IF NOT EXISTS idx_channels_favorite ON Channels (IsFavorite);
                    CREATE INDEX IF NOT EXISTS idx_epg_channel_time ON EpgPrograms (ChannelName, StartTime, EndTime);
                    CREATE INDEX IF NOT EXISTS idx_epg_channel_name ON EpgPrograms (ChannelName);
                    CREATE INDEX IF NOT EXISTS idx_epg_time ON EpgPrograms (StartTime, EndTime);
                    CREATE INDEX IF NOT EXISTS idx_epgchannels_name ON EpgChannels (Name);
                    CREATE INDEX IF NOT EXISTS idx_channels_pwc_date ON Channels (PersonalWatchCount, AddedDate);
                    CREATE INDEX IF NOT EXISTS idx_epg_source ON EpgPrograms (SourceUrl);
                    CREATE INDEX IF NOT EXISTS idx_epgchannels_source ON EpgChannels (SourceUrl);
                ";
                await command.ExecuteNonQueryAsync();

                // Column Migrations: Check existing columns first to avoid throwing duplicate column exceptions on startup
                var existingChannelCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var infoCmd = connection.CreateCommand())
                {
                    infoCmd.CommandText = "PRAGMA table_info(Channels);";
                    using var infoReader = await infoCmd.ExecuteReaderAsync();
                    while (await infoReader.ReadAsync())
                    {
                        existingChannelCols.Add(infoReader.GetString(1));
                    }
                }

                (string col, string sql)[] channelAlterCols = {
                    ("ViewersCount", "ALTER TABLE Channels ADD COLUMN ViewersCount INTEGER DEFAULT 0"),
                    ("PersonalWatchCount", "ALTER TABLE Channels ADD COLUMN PersonalWatchCount INTEGER DEFAULT 0"),
                    ("IsPremium", "ALTER TABLE Channels ADD COLUMN IsPremium INTEGER DEFAULT 0"),
                    ("IsWatched", "ALTER TABLE Channels ADD COLUMN IsWatched INTEGER DEFAULT 0"),
                    ("ImdbId", "ALTER TABLE Channels ADD COLUMN ImdbId TEXT DEFAULT ''"),
                    ("Overview", "ALTER TABLE Channels ADD COLUMN Overview TEXT DEFAULT ''"),
                    ("BackdropUrl", "ALTER TABLE Channels ADD COLUMN BackdropUrl TEXT DEFAULT ''"),
                    ("Cast", "ALTER TABLE Channels ADD COLUMN [Cast] TEXT DEFAULT ''"),
                    ("UrlSpeeds", "ALTER TABLE Channels ADD COLUMN UrlSpeeds TEXT DEFAULT ''"),
                    ("PreferredNameIndex", "ALTER TABLE Channels ADD COLUMN PreferredNameIndex INTEGER DEFAULT 0"),
                    ("PreferredUrlIndex", "ALTER TABLE Channels ADD COLUMN PreferredUrlIndex INTEGER DEFAULT 0"),
                    ("PreferredLogoIndex", "ALTER TABLE Channels ADD COLUMN PreferredLogoIndex INTEGER DEFAULT 0"),
                    ("PreferredEpgIndex", "ALTER TABLE Channels ADD COLUMN PreferredEpgIndex INTEGER DEFAULT 0"),
                    ("IsEpgLocked", "ALTER TABLE Channels ADD COLUMN IsEpgLocked INTEGER DEFAULT 0"),
                    ("LastPositionMs", "ALTER TABLE Channels ADD COLUMN LastPositionMs INTEGER DEFAULT 0"),
                    ("M3uLineNumber", "ALTER TABLE Channels ADD COLUMN M3uLineNumber INTEGER DEFAULT 0"),
                    ("RawM3uBlock", "ALTER TABLE Channels ADD COLUMN RawM3uBlock TEXT DEFAULT ''")
                };

                foreach (var (col, sql) in channelAlterCols)
                {
                    if (!existingChannelCols.Contains(col))
                    {
                        try
                        {
                            using var cmdAlter = connection.CreateCommand();
                            cmdAlter.CommandText = sql;
                            await cmdAlter.ExecuteNonQueryAsync();
                        }
                        catch (Exception ex)
                        {
                            if (!ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
                            {
                                LogService.LogWarning($"Database: Column addition warning: {sql}");
                            }
                        }
                    }
                }

                // Other table column migrations
                string[] otherCols = {
                    "ALTER TABLE EpgPrograms ADD COLUMN SourceUrl TEXT DEFAULT ''",
                    "ALTER TABLE M3uSources ADD COLUMN IsDefault INTEGER DEFAULT 0",
                    "ALTER TABLE IptvAccounts ADD COLUMN MaxConnections INTEGER DEFAULT 1",
                    "ALTER TABLE IptvAccounts ADD COLUMN ActiveConnections INTEGER DEFAULT 0",
                    "ALTER TABLE IptvAccounts ADD COLUMN TotalLiveStreams INTEGER DEFAULT 0",
                    "ALTER TABLE IptvAccounts ADD COLUMN TotalVodStreams INTEGER DEFAULT 0",
                    "ALTER TABLE IptvAccounts ADD COLUMN TotalSeriesStreams INTEGER DEFAULT 0",
                    "ALTER TABLE IptvAccounts ADD COLUMN ServerVersion TEXT DEFAULT ''",
                    "ALTER TABLE IptvAccounts ADD COLUMN ServerTimezone TEXT DEFAULT ''",
                    "ALTER TABLE IptvAccounts ADD COLUMN AllowedFormats TEXT DEFAULT ''",
                    "ALTER TABLE IptvAccounts ADD COLUMN IsTrial INTEGER DEFAULT 0",
                    "ALTER TABLE IptvAccounts ADD COLUMN LastChecked TEXT DEFAULT ''"
                };

                foreach (var sql in otherCols)
                {
                    try
                    {
                        using var cmdAlter = connection.CreateCommand();
                        cmdAlter.CommandText = sql;
                        await cmdAlter.ExecuteNonQueryAsync();
                    }
                    catch { }
                }

                // V1.8.8: Perform a background cleanup of duplicates on startup (only once)
                // Heavy maintenance tasks moved to background process to accelerate startup.
            }
        }

        private async Task PostStartupMaintenanceAsync()
        {
            if (GetSetting("InitialStartupMaintenanceV2Done", "false") == "true")
            {
                return; // Bakım daha önce yapılmış, her açılışta sistemi yorma
            }

            await Task.Delay(30000); // Kullanıcı arayüzle rahatça etkileşime geçsin diye 30 saniye bekle
            
            try
            {
                SuppressEvents = true;

                // 1. Data Migration
                if (GetSetting("MigrationV2Done", "false") != "true")
                {
                    await EnsureDataMigrationAsync();
                }

                // 2. Kütüphane kategori ve dil normalizasyon düzeltmesi
                if (GetSetting("FixTvMoviesAndLanguagesDone", "") != "true")
                {
                    try
                    {
                        using (var fixConn = new SqliteConnection(ConnectionString))
                        {
                            await fixConn.OpenAsync();
                            using var fixCmd = fixConn.CreateCommand();
                            fixCmd.CommandText = @"
                                UPDATE Channels 
                                SET Category = 'Film' 
                                WHERE (Category = 'TV' OR Category IS NULL OR Category = '') 
                                  AND (Url LIKE '%.mkv%' OR Url LIKE '%.mp4%' OR Url LIKE '%/movie/%' OR Url LIKE '%/movies/%' OR Url LIKE '%/vod/%'
                                       OR GroupTitle LIKE '%ACTION%' OR GroupTitle LIKE '%AKSIYON%' OR GroupTitle LIKE '%HORROR%' OR GroupTitle LIKE '%KORKU%'
                                       OR GroupTitle LIKE '%KOMEDI%' OR GroupTitle LIKE '%COMEDY%' OR GroupTitle LIKE '%FILM%' OR GroupTitle LIKE '%MOVIE%'
                                       OR GroupTitle LIKE '%SINEMA%' OR GroupTitle LIKE '%THRILLER%' OR GroupTitle LIKE '%DRAMA%' OR GroupTitle LIKE '%ABENTEUER%');
                                
                                UPDATE Channels 
                                SET Category = 'Dizi' 
                                WHERE Category = 'TV' AND (Url LIKE '%/series/%' OR GroupTitle LIKE '%DIZI%' OR GroupTitle LIKE '%SERIES%');

                                UPDATE Channels SET Language = 'de' WHERE (Language = 'und' OR Language IS NULL) AND (GroupTitle LIKE 'DE•%' OR GroupTitle LIKE 'DE:%' OR GroupTitle LIKE 'DE -%' OR GroupTitle LIKE '%[DE]%' OR GroupTitle LIKE '%DEUTSCH%');
                                UPDATE Channels SET Language = 'tr' WHERE (Language = 'und' OR Language IS NULL) AND (GroupTitle LIKE 'TR•%' OR GroupTitle LIKE 'TR:%' OR GroupTitle LIKE 'TR -%' OR GroupTitle LIKE '%[TR]%' OR GroupTitle LIKE '%TURK%');
                                UPDATE Channels SET Language = 'en' WHERE (Language = 'und' OR Language IS NULL) AND (GroupTitle LIKE 'EN•%' OR GroupTitle LIKE 'EN:%' OR GroupTitle LIKE 'EN -%' OR GroupTitle LIKE '%[EN]%' OR GroupTitle LIKE '%ENGLISH%');
                                UPDATE Channels SET Language = 'fr' WHERE (Language = 'und' OR Language IS NULL) AND (GroupTitle LIKE 'FR•%' OR GroupTitle LIKE 'FR:%' OR GroupTitle LIKE '%[FR]%' OR GroupTitle LIKE '%FRANCE%');
                                UPDATE Channels SET Language = 'es' WHERE (Language = 'und' OR Language IS NULL) AND (GroupTitle LIKE 'ES•%' OR GroupTitle LIKE 'ES:%' OR GroupTitle LIKE '%[ES]%' OR GroupTitle LIKE '%SPAIN%');
                            ";
                            await fixCmd.ExecuteNonQueryAsync();
                        }
                        SetSetting("FixTvMoviesAndLanguagesDone", "true");
                        LogService.LogInfo("DatabaseEngine: Kütüphane kategori ve dil normalizasyon düzeltmesi başarıyla tamamlandı.");
                    }
                    catch (Exception exFix)
                    {
                        LogService.LogWarning($"DatabaseEngine: Kategori düzeltme adımı atlandı: {exFix.Message}");
                    }
                }

                // 3. Cleanup Duplicates (Only once)
                if (GetSetting("InitialDeduplicationDone", "false") != "true")
                {
                    await CleanupDuplicatesAsync();
                    SetSetting("InitialDeduplicationDone", "true");
                }

                // 4. Discover IPTV Accounts (Only once)
                if (GetSetting("InitialIptvDiscoveryDone", "false") != "true")
                {
                    await DiscoverAndSyncAccountsFromChannelsAsync().ConfigureAwait(false);
                    SetSetting("InitialIptvDiscoveryDone", "true");
                }

                SetSetting("InitialStartupMaintenanceV2Done", "true");
            }
            catch (Exception ex)
            {
                LogService.LogError("DatabaseEngine: PostStartupMaintenanceAsync error", ex);
            }
            finally
            {
                SuppressEvents = false;
            }
        }

        private async Task EnsureDataMigrationAsync()
        {
            try
            {
                string oldDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamMesh", "database.db");
                if (!File.Exists(oldDbPath)) { SetSetting("MigrationV2Done", "true"); return; }

                var oldChannels = new List<Channel>();
                using (var oldConn = new SqliteConnection($"Data Source={oldDbPath}"))
                {
                    await oldConn.OpenAsync();
                    var cmd = oldConn.CreateCommand();
                    cmd.CommandText = "SELECT Id, Name, Url, GroupTitle, LogoUrl, SourceType, Category, Language, PlaylistUrl, IsFavorite FROM Channels";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        oldChannels.Add(new Channel {
                            Id = reader.GetString(0), Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Url = reader.IsDBNull(2) ? "" : reader.GetString(2), GroupTitle = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            LogoUrl = reader.IsDBNull(4) ? "" : reader.GetString(4), SourceType = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Category = reader.IsDBNull(6) ? "TV" : reader.GetString(6), Language = reader.IsDBNull(7) ? "und" : reader.GetString(7),
                            PlaylistUrl = reader.IsDBNull(8) ? "" : reader.GetString(8), IsFavorite = !reader.IsDBNull(9) && reader.GetInt32(9) == 1
                        });
                    }
                }
                if (oldChannels.Count > 0) await SyncIncomingChannelsAsync(oldChannels);
                SetSetting("MigrationV2Done", "true");
            }
            catch (Exception ex)
            {
                LogService.LogError("DatabaseEngine: Data migration exception", ex);
            }
        }

        public void CleanupDuplicateIptvAccounts()
        {
            try
            {
                var allAccs = GetAllIptvAccounts();
                if (allAccs.Count <= 1) return;

                var seen = new Dictionary<string, IptvAccount>(StringComparer.OrdinalIgnoreCase);
                var toDelete = new List<string>();

                foreach (var a in allAccs)
                {
                    string host = IptvAccountHelper.ExtractHostAndPort(a.ServerUrl).ToLowerInvariant();
                    string user = (a.Username ?? "").Trim().ToLowerInvariant();
                    string key = $"{user}@{host}";

                    if (seen.TryGetValue(key, out var existing))
                    {
                        if (a.TotalLiveStreams > existing.TotalLiveStreams || a.LastChecked > existing.LastChecked)
                        {
                            toDelete.Add(existing.Id);
                            seen[key] = a;
                        }
                        else
                        {
                            toDelete.Add(a.Id);
                        }
                    }
                    else
                    {
                        seen[key] = a;
                    }
                }

                if (toDelete.Count > 0)
                {
                    LogService.LogInfo($"[DatabaseEngine] {toDelete.Count} adet mükerrer IPTV hesabı veritabanından temizleniyor...");
                    foreach (var id in toDelete)
                    {
                        RemoveIptvAccount(id);
                    }
                    NotifyDatabaseUpdated();
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("[DatabaseEngine] CleanupDuplicateIptvAccounts hatası", ex);
            }
        }

        public async Task<List<IptvAccount>> DiscoverAndSyncAccountsFromChannelsAsync(Action<string, double>? progressCallback = null)
        {
            try
            {
                CleanupDuplicateIptvAccounts();

                progressCallback?.Invoke("Kayıtlı IPTV hesapları kontrol ediliyor...", 95.0);

                // Temizlik: Durumu Aktif olmayan, giriş başarısız veya hata içeren hesapları IptvAccounts tablosundan kaldır
                try
                {
                    var allCur = GetAllIptvAccounts();
                    foreach (var a in allCur)
                    {
                        if (a.Status != null && (a.Status.Contains("Başarısız") || a.Status.Contains("Hata") || a.Status.Contains("Bağlanılamadı")))
                        {
                            RemoveIptvAccount(a.Id);
                        }
                    }
                }
                catch { }

                var existingAccs = GetAllIptvAccounts();
                var existingKeys = new HashSet<string>(existingAccs.Select(a => $"{a.Username?.Trim()}@{IptvAccountHelper.ExtractHostAndPort(a.ServerUrl)}".ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);

                progressCallback?.Invoke("Veritabanındaki IPTV sağlayıcı adresleri taranıyor...", 95.2);
                var urlList = await _channels.GetAllStreamUrlsForDiscoveryAsync();
                
                // 1. Host bazlı gruplama (Host-First Deduplication)
                // 60.000 link yerine benzersiz sağlayıcı host adresleri altında aday hesapları topla
                var hostCandidates = new Dictionary<string, List<IptvAccountInfo>>(StringComparer.OrdinalIgnoreCase);

                foreach (var u in urlList)
                {
                    if (string.IsNullOrWhiteSpace(u)) continue;
                    var split = u.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var raw in split)
                    {
                        var acc = IptvAccountHelper.ParseAccountFromUrl(raw);
                        if (acc == null || string.IsNullOrWhiteSpace(acc.Username)) continue;

                        string hostKey = IptvAccountHelper.ExtractHostAndPort(acc.HostWithPort).ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(hostKey) || IptvAccountHelper.IsIgnoredHost(hostKey)) continue;

                        string checkKey = $"{acc.Username?.Trim()}@{hostKey}".ToLowerInvariant();
                        if (existingKeys.Contains(checkKey)) continue;

                        if (!hostCandidates.TryGetValue(hostKey, out var accList))
                        {
                            accList = new List<IptvAccountInfo>();
                            hostCandidates[hostKey] = accList;
                        }

                        if (!accList.Any(x => x.SignatureKey == acc.SignatureKey) && accList.Count < 3)
                        {
                            accList.Add(acc);
                        }
                    }
                }

                bool addedAny = false;

                if (hostCandidates.Count > 0)
                {
                    progressCallback?.Invoke($"Keşfedilen {hostCandidates.Count} sağlayıcı sunucusu test ediliyor...", 95.5);

                    var verifiedAccounts = new List<IptvAccountInfo>();
                    int hostIndex = 0;

                    foreach (var kvp in hostCandidates)
                    {
                        hostIndex++;
                        string host = kvp.Key;
                        var accs = kvp.Value;
                        double pct = 95.5 + ((double)hostIndex / hostCandidates.Count) * 1.5;

                        var firstAcc = accs[0];
                        progressCallback?.Invoke($"Sağlayıcı doğrulanıyor ({hostIndex}/{hostCandidates.Count}): {firstAcc.HostWithPort}...", pct);

                        // Host geçerli bir IPTV sunucusu mu?
                        bool isRealIptvHost = await IptvAccountHelper.FastCheckHostIsIptvAsync(firstAcc).ConfigureAwait(false);
                        if (!isRealIptvHost)
                        {
                            // Host IPTV sunucusu değil (örn: sobreatsesuyp.com, storage.diziyou.one).
                            // Host'u kara listeye al ve bu host'a ait TÜM adayları hemen atla!
                            IptvAccountHelper.MarkAccountFailed(firstAcc, markHostFailed: true);
                            continue;
                        }

                        // Host bir IPTV sunucusu! Şimdi aday hesapların kimlik doğrulamasını test et
                        foreach (var acc in accs)
                        {
                            bool isValid = await IptvAccountHelper.FastVerifyAccountAsync(acc, 2500).ConfigureAwait(false);
                            if (isValid)
                            {
                                verifiedAccounts.Add(acc);
                                break; // Bu host için çalışan bir hesap bulundu
                            }
                        }
                    }

                    // Doğrulanan IPTV hesaplarını hızlıca IptvAccounts tablosuna kaydet (Kütüphanede Sağlayıcı Sekmesi oluşsun)
                    foreach (var acc in verifiedAccounts)
                    {
                        string hostOnly = IptvAccountHelper.ExtractHostAndPort(acc.HostWithPort);
                        string checkKey = $"{acc.Username?.Trim()}@{hostOnly}".ToLowerInvariant();

                        if (!existingKeys.Contains(checkKey))
                        {
                            string scheme = acc.HostWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                                            acc.HostWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "" : "http://";
                            string serverUrl = $"{scheme}{acc.HostWithPort.TrimEnd('/')}";
                            var newAcc = new IptvAccount
                            {
                                Id = Guid.NewGuid().ToString(),
                                Name = $"{acc.Username} ({acc.HostWithPort})",
                                ServerUrl = serverUrl,
                                Username = acc.Username ?? "",
                                Password = acc.Password ?? "",
                                Status = "Aktif",
                                ExpiryDate = DateTime.Now.AddYears(1)
                            };
                            SaveIptvAccount(newAcc);
                            existingKeys.Add(checkKey);
                            addedAny = true;
                            LogService.LogInfo($"[DatabaseEngine] IPTV hesabı doğrulandı ve IptvAccounts tablosuna kaydedildi: {newAcc.Name}");
                        }
                    }
                }

                if (addedAny && !SuppressEvents)
                {
                    NotifyDatabaseUpdated();
                }

                return GetAllIptvAccounts();
            }
            catch (Exception ex)
            {
                LogService.LogError("DatabaseEngine: DiscoverAndSyncAccountsFromChannelsAsync hatası", ex);
                return GetAllIptvAccounts();
            }
        }

        // Settings delegators
        public string GetSetting(string key, string defaultValue = "") => _settings.GetSetting(key, defaultValue);
        public void SetSetting(string key, string value) => _settings.SetSetting(key, value);
        public (int count, DateTime date) GetDailyQueryStats() => _settings.GetDailyQueryStats();
        public void IncrementDailyQueryCount() => _settings.IncrementDailyQueryCount();

        // Channel delegators
        public async Task<List<Channel>> GetSeriesEpisodesAsync(string seriesBaseName) => await _channels.GetSeriesEpisodesAsync(seriesBaseName);
        public async Task<List<Channel>> GetAllChannelsAsync() => await _channels.GetAllChannelsAsync();
        public async Task<Channel?> GetChannelByIdAsync(string id) => await _channels.GetChannelByIdAsync(id);
        public async Task SaveChannelAsync(Channel ch) => await _channels.SaveChannelAsync(ch);
        public async Task SaveChannelsBatchAsync(List<Channel> channels, bool clearFirst = false, bool notifyUpdated = true, Action<string, double>? progressCallback = null) => await _channels.SaveChannelsBatchAsync(channels, clearFirst, notifyUpdated, progressCallback);
        public async Task SyncIncomingChannelsAsync(List<Channel> incoming, Action<string, double>? progressCallback = null) => await _channels.SyncIncomingChannelsAsync(incoming, progressCallback);
        public async Task<int> AutoAggregateDatabaseAsync() => await _channels.AutoAggregateDatabaseAsync();
        public async Task<int> GetTotalChannelCountAsync() => await _channels.GetTotalChannelCountAsync();
        public int GetChannelCountBySource(string url) => _channels.GetChannelCountBySource(url);
        public Dictionary<string, int> GetAllChannelCountsByPlaylist() => _channels.GetAllChannelCountsByPlaylist();
        public List<Channel> GetChannelsBySource(string url) => _channels.GetChannelsBySource(url);
        public void DeleteChannelById(string channelId) { _channels.DeleteChannelById(channelId); NotifyDatabaseUpdated(); }
        public async Task DeleteChannelsAsync(List<string> ids) { await _channels.DeleteChannelsAsync(ids); NotifyDatabaseUpdated(); }
        public void SaveChannelSync(Channel ch) => _channels.SaveChannelSync(ch);
        public async Task CleanupDuplicatesAsync() => await _channels.CleanupDuplicatesAsync();
        public async Task<int> CountChannelsByAccountAsync(string hostWithPort, string username) => await _channels.CountChannelsByAccountAsync(hostWithPort, username);
        public async Task<List<Channel>> GetChannelsByAccountAsync(string hostWithPort, string username) => await _channels.GetChannelsByAccountAsync(hostWithPort, username);

        // EPG delegators
        public async Task<int> GetEpgProgramsCountAsync() => await _epg.GetEpgProgramsCountAsync();
        public async Task SaveEpgProgramsAsync(List<EpgProgram> programs) => await _epg.SaveEpgProgramsAsync(programs);
        public async Task CleanupOldEpgProgramsAsync(int daysBack = 1) => await _epg.CleanupOldEpgProgramsAsync(daysBack);
        public async Task ClearEpgSourceDataAsync(string url) => await _epg.ClearEpgSourceDataAsync(url);
        public async Task SaveEpgChannelsBatchAsync(List<(string epgId, string name, string logo, string url)> channels) => await _epg.SaveEpgChannelsBatchAsync(channels);
        public async Task<List<EpgChannelSearchResult>> SearchEpgChannelsAsync(string query, bool allSources) => await _epg.SearchEpgChannelsAsync(query, allSources, GetEpgSources());
        public async Task<List<EpgProgram>> GetCurrentEpgProgramsAsync() => await _epg.GetCurrentEpgProgramsAsync();
        public async Task<List<EpgProgram>> GetEpgForChannelsAsync(List<string> channelNames) => await _epg.GetEpgForChannelsAsync(channelNames);
        public List<string> GetEpgSources() => _epg.GetEpgSources();
        public List<EpgSourceItem> GetEpgSourceItems() => _epg.GetEpgSourceItems();
        public void AddEpgSource(string url) => _epg.AddEpgSource(url);
        public void RemoveEpgSource(string url) => _epg.RemoveEpgSource(url);
        public void UpdateEpgSourceStats(string url, string status, int channelCount, int programCount, long lastUpdated, string errorMessage) => _epg.UpdateEpgSourceStats(url, status, channelCount, programCount, lastUpdated, errorMessage);
        public void SetEpgSourceTimeShift(string url, int timeShiftHours) => _epg.SetEpgSourceTimeShift(url, timeShiftHours);
        public int GetEpgSourceTimeShift(string url) => _epg.GetEpgSourceTimeShift(url);


        // Metadata delegators
        public async Task<List<MetadataResult>> GetMetadataPoolForQueryAsync(string query) => await _metadata.GetMetadataPoolForQueryAsync(query);
        public async Task SaveMetadataPoolResultsAsync(string query, List<MetadataResult> results) => await _metadata.SaveMetadataPoolResultsAsync(query, results);

        // Source delegators
        public List<string> GetM3uSources() => _sources.GetM3uSources();
        public List<M3uSourceEntity> GetAllM3uSources() => _sources.GetAllM3uSources();
        public void AddM3uSource(string url, bool isDefault = false) { _sources.AddM3uSource(url, isDefault); NotifyDatabaseUpdated(); }
        public void SetDefaultM3uSource(string url) { _sources.SetDefaultM3uSource(url); NotifyDatabaseUpdated(); }
        public string? GetDefaultM3uSource() => _sources.GetDefaultM3uSource();
        public void RemoveM3uSource(string url) { _sources.RemoveM3uSource(url); NotifyDatabaseUpdated(); }

        // IPTV delegators
        public List<IptvAccount> GetAllIptvAccounts() => _iptv.GetAllIptvAccounts();
        public void SaveIptvAccount(IptvAccount acc) => _iptv.SaveIptvAccount(acc);
        public void RemoveIptvAccount(string id) => _iptv.RemoveIptvAccount(id);

        // Logo delegators
        public void UpdateLogoIndex(List<(string key, string file)> items) => _logo.UpdateLogoIndex(items);
        public Dictionary<string, string> GetAllLogoIndex() => _logo.GetAllLogoIndex();
        public string? FindLogoInIndex(string key) => _logo.FindLogoInIndex(key);

        // Utility
        public void ExecuteRawNonQuery(string sql)
        {
            using (var connection = new SqliteConnection(ConnectionString)) { connection.Open(); var cmd = connection.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
            NotifyDatabaseUpdated();
        }

        public static event EventHandler? OnDatabaseUpdated;
        public static void NotifyDatabaseUpdated()
        {
            if (SuppressEvents)
            {
                _hasPendingUpdate = true;
                return;
            }
            _hasPendingUpdate = false;
            OnDatabaseUpdated?.Invoke(null, EventArgs.Empty);
        }

        public void ClearAllSources() { ExecuteRawNonQuery("DELETE FROM M3uSources"); ExecuteRawNonQuery("DELETE FROM EpgSources"); NotifyDatabaseUpdated(); }
        public void ClearAllContents() { ExecuteRawNonQuery("DELETE FROM Channels"); ExecuteRawNonQuery("DELETE FROM EpgPrograms"); NotifyDatabaseUpdated(); }
        public void ClearChannelCache() { /* Cache removed */ }
    }
}
