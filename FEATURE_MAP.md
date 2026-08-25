# StreamMesh - Uygulama Özellikleri Ağacı & Mimari Haritası (Feature Map)

Bu doküman, StreamMesh projesindeki tüm özelliklerin, modüllerin, servislerin ve kullanıcı arayüzü bileşenlerinin tam dosya ve satır aralıkları haritasıdır. Yeni bir özellik eklerken veya mevcut bir özelliği güncellerken **regresyon (bozulma) yaşanmaması için** bu ağaç referans alınmalıdır.

---

## 1. Uygulama Mimarisi Genel Bakış
```
StreamMesh (WPF / .NET 8 / C#)
├── UI Layer (XAML + Codebehind Views & Converters)
│   ├── HomeView (Kanal Listesi, Grid, Arama, Kategori, Oynatma Başlatma)
│   ├── PlayerView (Flyleaf Oynatıcı, OSD, Timeshift/DVR, Ses/Görüntü Efektleri, Parça Seçimi)
│   ├── SettingsView & Modals (Kaynaklar, EPG Kaynakları, Bağış, Hesaplar)
│   └── Converters (LogoCacheConverter, TimeAgoConverter, MultiValueConverters)
├── Core Engine & Media (Medya Yönetimi ve Arabellekleme)
│   ├── HlsProxyEngine (30-60 dk Yerel TS HLS Proxy & Timeshift/DVR Motoru)
│   ├── AceEngine & AceStreamService (AceStream Motor Entegrasyonu & Peer/İzleyici İstatistiği)
│   ├── YoutubeEngine & YoutubeService (YouTube Akış Çözümleme & İzleyici İstatistiği)
│   ├── EpgEngine & EpgService (XMLTV / GZ Ayrıştırma, Otomatik EPG Eşleme, Zaman Dilimi Düzeltme)
│   ├── LogoSyncService & LogoSearchEngine (Logo İndirme, Eşleme ve Yerel Önbellekleme)
│   └── ChannelAggregator & ChannelEnricher (Kanal Birleştirme, Mükerrer Temizleme, Zenginleştirme)
├── Database & Storage
│   └── DatabaseEngine (SQLite WAL Modu, Kanallar, EPG Programları, Kaynaklar, Ayarlar)
└── Deployment & Packaging
    ├── StreamMesh.csproj (Proje Yapılandırması, NuGet Bağımlılıkları, Asset Dağıtımı)
    └── setup.iss (Inno Setup Yükleyici, Masaüstü Kısayolu, İkon Eşleme)
```

---

## 2. Detaylı Özellik Ağacı (Feature Tree & Code Map)

### 📼 2.1. Oynatıcı, DVR / Timeshift & HLS / TS Arabellekleme
*   **Amaç:** Canlı yayınları 30-60 dakika boyunca yerel bellekte arabelleğe alma, canlı yayını durdurup kaldığı yerden devam ettirme, zaman çizelgesinde serbestçe geri/ileri sarma, geride kalınan süreyi (`-01:15`, `-00:03` vb.) yayının durdurulduğu veya 1 saniyeden fazla kaydığı an itibariyle OSD üzerinde sarı rozetle gösterme ve tek tıkla canlı yayına (`🔴 CANLI`) dönebilme.
*   **İlgili Dosyalar & Satır Aralıkları:**
    *   `/Core/Media/HlsProxyEngine.cs` (Satır: 1 - 740)
        *   `HlsSegment` & `HlsSessionInfo`: Segment zaman damgası, süre ve ProgramDateTime takibi.
        *   `StartLivePoller()`: Geçerli HLS akışlarını arabelleğe alıp sliding window oluşturan motor.
        *   `ClearChannelCache()`: Kanal değiştiğinde veya yayın durduğunda arka plan poller iş parçacıklarını anında temizleme.
        *   `InspectAndPrepareHlsAsync()`: Akışın geçerli segmentlere sahip olduğunu doğrulayıp proxy'ye alma.
        *   `GenerateManifest()`: Oyuncunun istediği zamana göre sanal HLS manifesti oluşturan HTTP uç noktası.
    *   `/UI/Views/PlayerView.xaml.cs` (Satır: 1 - 1440)
        *   `PrepareLiveTimeshiftStreamAsync()` & `LoadChannel()`: Yayını HlsProxy DVR motoru üzerinden başlatma ve eski oturumu temizleme.
        *   `UpdateOsdTimeAndBadge()`: Canlı yayından 2 saniye ve üzeri her kaymada/durdurmada anlık sarı `-mm:ss` sayacını çalıştırma.
        *   `GoLive_Click()` & `TimeSlider_ValueChanged()`: Canlı kenara dönme ve DVR tamponunda arama (Seek).
        *   `UpdateChannelLogo()` & `GenerateMonogram()`: Logosu eksik kanallara otomatik renkli monogram fallback üretimi.
    *   `/UI/Views/PlayerView.xaml`
        *   Timeshift Kontrol Çubuğu, Canlı/DVR Rozeti (`TxtLiveBadge`, `BtnGoLive`), Zaman Slider'ı, OSD Logo Fallback Grid.
*   **Bağlı Olduğu Modüller:** `FlyleafLib`, `DatabaseEngine`, `Channel.cs`.
*   **Değişiklik Yaparken Korunacak Kurallar:**
    *   Manifest'te `#EXT-X-PLAYLIST-TYPE:EVENT` veya sliding window dengesi korunmalıdır.
    *   `InspectAndPrepareHlsAsync` yalnızca segmentleri doğrulanmış (`session.Segments.Count > 0`) HLS akışlarını proxy'ye yönlendirmelidir.
    *   Doğrudan IPTV / TS akışlarında ve proxy zaman aşımı durumunda Flyleaf'in doğrudan orijinal URL'yi oynatması ve sıfır gecikmeli donanım hızlandırma önceliği korunmalıdır.

---

### 🌐 2.2. AceStream Motoru & P2P Akış Yönetimi
*   **Amaç:** `acestream://` ve hash kimliklerini tespit etme, yerel Ace Engine (port 6878) ile iletişim kurma, akışı HTTP / HLS formatına çevirme ve canlı peer/eş sayısını çekip izleyici sayısına ekleme.
*   **İlgili Dosyalar & Satır Aralıkları:**
    *   `/Core/Media/AceEngine.cs` (Satır: 1 - 647)
        *   `GetHttpUrlsWithTokenAsync()` / `OpenStreamAsync()` (Satır: 526 - 565): Akış URL'si oluşturma.
        *   `ExtractHash()` (Satır: 602 - 630): URL ve protokollerden 40 karakterlik hash çıkarma.
        *   `GetStreamStatsAsync()`: Eş / Peer sayısını `http://127.0.0.1:6878/ace/stat?id={hash}` üzerinden sorgulama.
    *   `/Core/Media/AceStreamService.cs` (Satır: 1 - 21)
    *   `/UI/Views/PlayerView.xaml.cs`: AceStream bağlantı durumu ve OSD göstergeleri.
*   **Bağlı Olduğu Modüller:** `Channel.cs`, `PlayerView.xaml.cs`.

---

### 📺 2.3. YouTube Akış Motoru & İzleyici İstatistikleri
*   **Amaç:** YouTube linklerinden canlı/VOD video akışını (`YoutubeExplode`) çözümleme, canlı izleyici sayısını çekme ve alternatif kaynaklar arasındaki toplam izleyici/peer sayısını hesaplama.
*   **İlgili Dosyalar & Satır Aralıkları:**
    *   `/Core/Media/YoutubeEngine.cs` (Satır: 1 - 78)
        *   `GetChannelsFromUrlAsync()` (Satır: 16 - 53): Playlist veya video bilgisini ayrıştırma.
        *   `GetStreamUrlAsync()` (Satır: 54 - 76): 1080p/720p akış manifestini çözme.
    *   `/Core/Media/YoutubeService.cs` (Satır: 1 - 26)
    *   `/Models/Channel.cs` (Satır: 200 - 240): `TotalViewersOrPeers` hesaplama özelliği.
*   **Bağlı Olduğu Modüller:** `YoutubeExplode`, `Channel.cs`.

---

### 📅 2.4. EPG (Elektronik Program Rehberi) Motoru & Eşleme
*   **Amaç:** XMLTV ve GZ formatındaki EPG verilerini indirme, SQLite `EpgPrograms` ve `EpgChannels` tablolarına kaydetme, kanalları isim ve dille akıllı eşleme (`SmartEPG`), şimdiki ve sonraki programı OSD'de gösterme.
*   **İlgili Dosyalar & Satır Aralıkları:**
    *   `/Core/Media/EpgEngine.cs` (Satır: 1 - 420)
        *   `DownloadAndParseEpgAsync()` (Satır: 40 - 180): XML/GZ akış ayrıştırma ve timezone dönüştürme.
    *   `/Core/Media/EpgService.cs` (Satır: 1 - 317)
        *   `EnrichChannelsAsync()` & `EnrichBatchEpgAsync()` (Satır: 60 - 200): Kanallara aktif EPG verisini bağlama.
        *   `PerformSmartEpgMatchAsync()` (Satır: 272 - 315): EPG ID'si olmayan kanalları temiz isim ve dil kontrolüyle otomatik eşleme.
    *   `/Core/Database/DatabaseEngine.cs` (Satır: 92 - 107, 589 - 680): `EpgPrograms` ve `EpgChannels` indeksleri ve sorguları.
*   **Bağlı Olduğu Modüller:** `Channel.cs`, `PlayerView.xaml`, `HomeView.xaml`.

---

### 🖼️ 2.5. Logo Önbellekleme, Senkronizasyon & UI Dönüştürücü
*   **Amaç:** Kanalların logolarını yerel `logos/` klasöründen, pack URI'den veya internetten yükleme, WPF UI donmalarını engellemek için arka planda önbellekleme ve alternatif logolar arasında geçiş imkanı.
*   **İlgili Dosyalar & Satır Aralıkları:**
    *   `/Converters/LogoCacheConverter.cs` (Satır: 1 - 180)
        *   `Convert()` & `LoadLocalFallback()`: Pack URI, disk dosya yolu veya web URL'sini güvenli SkiaSharp/WPF Bitmap'e dönüştürme.
    *   `/Core/Media/LogoSyncService.cs` & `/Core/Media/LogoSearchEngine.cs`
    *   `/Models/Channel.cs` (Satır: 130 - 170): `GetLogoList()`, `ActiveLogo`, `AddAlternativeLogo()`.
*   **Bağlı Olduğu Modüller:** `SkiaSharp`, `WPF Image Binding`.

---

### 🔄 2.6. Kanal Birleştirme (Aggregator) & Mükerrer Önleme
*   **Amaç:** Farklı M3U/Xtream listelerinden gelen aynı kanalları (örn. TRT 1, beIN Sports) tek bir kart altında toplama, alternatif URL ve logolarını koruma, favori/izleme istatistiklerini kaybetmeden `MergeWith` ile güncelleme.
*   **İlgili Dosyalar & Satır Aralıkları:**
    *   `/Core/Media/ChannelAggregator.cs` (Satır: 1 - 250)
        *   `AggregateChannels()`: URL, EPG ID ve temiz isim benzerliğine göre kanalları birleştirme.
    *   `/Core/Media/ChannelEnricher.cs` (Satır: 1 - 120): Kategori, dil ve çözünürlük etiketleme.
    *   `/Models/Channel.cs` (Satır: 100 - 260): `MergeWith()`, `AlternativeUrls`, `AlternativeNames`, `AlternativeLogos`.
    *   `/Core/Database/DatabaseEngine.cs` (Satır: 497 - 588): `SyncIncomingChannelsAsync` ve `AutoAggregateDatabaseAsync`.

---

### 🗄️ 2.7. Veritabanı & Kalıcılık (Database Engine)
*   **Amaç:** SQLite WAL modunda kanal listesi, EPG programları, M3U/EPG kaynakları, IPTV hesapları ve izleme geçmişini sıfır kilitlenme riskiyle saklama.
*   **İlgili Dosyalar & Satır Aralıkları:**
    *   `/Core/Database/DatabaseEngine.cs` (Satır: 1 - 1323)
        *   Tablo şemaları ve migration'lar (Satır: 58 - 212).
        *   Kanal okuma/yazma: `GetAllChannelsAsync`, `SaveChannelAsync`, `SaveChannelsBatchAsync` (Satır: 335 - 495).
        *   EPG sorgulama: `GetEpgForChannelsAsync`, `SearchEpgChannelsAsync`.
*   **Bağlı Olduğu Modüller:** `Microsoft.Data.Sqlite`, `Channel.cs`.

---

### 📦 2.8. Kurulum & Masaüstü İkon Yapılandırması (Setup & Packaging)
*   **Amaç:** Uygulamanın Inno Setup ile Windows'a kurulması, kurulum sırasında `logos/` klasörünün eksiksiz kopyalanması ve masaüstü kısayolunun varsayılan olarak seçili gelip doğru ikonla (`StreamMesh_Icon.ico`) oluşturulması.
*   **İlgili Dosyalar & Satır Aralıkları:**
    *   `/setup.iss` (Satır: 1 - 45)
        *   `[Tasks]` - `desktopicon` (Varsayılan olarak işaretli).
        *   `[Files]` - `logos\*` hedef dizine tam kopyalama.
        *   `[Icons]` - Masaüstü ve başlat menüsü için `IconFilename: "{app}\logos\StreamMesh_Icon.ico"`.
    *   `/StreamMesh.csproj` (Satır: 1 - 54)
        *   `<Resource Include="logos\**" />` ve `<Content Include="logos\**" CopyToOutputDirectory="PreserveNewest" />`.

---

## 3. Kod Güncelleme ve Regresyon Önleme Protokolü

Bir dosya değiştirilmeden önce bu kontrol listesi takip edilmelidir:
1. **Hedef Özelliği Belirle:** İlgili özelliğin `FEATURE_MAP.md` içindeki satırlarını incele.
2. **Kesişen Modülleri Kontrol Et:** Değişiklik başka bir modülün (örn. EPG, Timeshift, Aggregator) veri yapısını etkiliyor mu?
3. **MergeWith Bütünlüğü:** `Channel.cs` üzerindeki alanlar değiştirilirse veya yeni alan eklenirse `DatabaseEngine.cs` ve `Channel.MergeWith()` eşzamanlı güncellenmelidir.
4. **Haritayı Güncelle:** Yapılan değişiklik sonrası `FEATURE_MAP.md` dosyasındaki satır ve mantık açıklamalarını güncelle.
