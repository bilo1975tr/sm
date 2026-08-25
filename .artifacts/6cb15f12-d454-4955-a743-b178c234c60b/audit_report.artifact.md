# STREAMMESH TEKNİK DENETİM VE REGRESYON ANALİZ RAPORU

## A. EXECUTIVE SUMMARY

*   **Genel Sağlık:** Uygulama, hibrit bir mimariyle (WPF + Local HTTP Proxy + Node.js portal) kurgulanmış, oldukça yetenekli ve "low-latency" öncelikli bir yapıya sahiptir. Kritik akışlar (HLS/DVR, AceStream, EPG) modülerdir.
*   **Kritik Sorun Sayısı (P0):** 0
*   **Yüksek Önemli Sorun Sayısı (P1):** 3 (HLS Sliding Window/EVENT çelişkisi, AceStream API bağımlılıkları, YouTube 1080p kısıtı)
*   **Orta Önemli Sorun Sayısı (P2):** 4 (Database Migration race condition, Logo Search kırılganlığı, EPG Timezone heuristiği, UI/Logic iç içeliği)
*   **Düşük Önemli Sorun Sayısı (P3):** 5+ (Dead code - server.js belirsizliği, Log spam riski, UI thread blokaj riskleri)
*   **Testing Gap:** Mevcut projede Unit/Integration testleri bulunmamaktadır.
*   **Mimari Durum:** Proje MVVM paternini (ViewModels) benimsemiş olsa da, özellikle `PlayerView` ve `SettingsView` gibi kompleks görünümlerde yoğun Code-behind kullanımı mevcuttur.

---

## B. CRITICAL & HIGH FINDINGS (P1)

### ID: P1-001 | HLS EVENT vs Sliding Window Çelişkisi
*   **Severity:** HIGH
*   **Category:** MEDIA / RUNTIME
*   **File:** [HlsProxyEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Media/HlsProxyEngine.cs) (Satır: 620-666)
*   **Evidence:** `GenerateManifest` metodunda `#EXT-X-PLAYLIST-TYPE:EVENT` etiketi zorunlu olarak ekleniyor. Ancak `HlsSessionInfo` içindeki segmentler `MaxTrackedHistorySegments` (15.000) değerini aştığında `RemoveRange(0, 500)` ile siliniyor.
*   **Root Cause:** HLS spekülasyonuna göre `EVENT` playlistleri asla kısalmaz (sadece sonuna eklenir). Eğer playlist kısalırsa ve player eski bir segmenti isterse 404 alır.
*   **Impact:** Uzun süreli (24 saat+) izlemelerde veya player buffer'ı geride kaldığında oynatma durabilir.
*   **Affected Features:** DVR, Timeshift, 30-60 dk Buffer.
*   **Recommended Solution:** Eğer bir kayan pencere (sliding window) kullanılıyorsa `EVENT` etiketi kaldırılmalı ve `MEDIA-SEQUENCE` doğru şekilde artırılmalıdır. Veya `EVENT` kullanılacaksa segmentler diskte kalıcı olmalıdır.

### ID: P1-002 | AceStream API & Web Scraper Kırılganlığı
*   **Severity:** HIGH
*   **Category:** NETWORK / EXTERNAL
*   **File:** [AceEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Media/AceEngine.cs) (Satır: 120-170, 250-300)
*   **Evidence:** P2P arama motorları (`search-ace.stream`, `ace-stream.net`) Regex ile HTML parse edilerek çalışıyor. Ayrıca `test_api_key` gibi hardcoded değerler mevcut.
*   **Root Cause:** Resmi bir API yerine web scraping kullanımı.
*   **Impact:** Web siteleri tasarım değiştirdiğinde AceStream arama özelliği anında bozulur.
*   **Affected Features:** AceStream Search, P2P Playback.
*   **Recommended Solution:** Mümkünse resmi AceStream Engine API'si önceliklendirilmeli, scraping işlemleri için bir "Provider" soyutlaması yapılmalıdır.

### ID: P1-003 | YouTube 1080p Oynatma Kısıtı
*   **Severity:** HIGH
*   **Category:** MEDIA
*   **File:** [YoutubeEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Media/YoutubeEngine.cs) (Satır: 55-78)
*   **Evidence:** `GetStreamUrlAsync` metodu yalnızca `GetMuxedStreams()` üzerinden en yüksek kaliteyi dönüyor.
*   **Root Cause:** YouTube 1080p ve üzeri çözünürlükleri sadece "DASH" (ses ve görüntü ayrı) olarak sunar. Muxed akışlar genellikle 720p ile sınırlıdır.
*   **Impact:** Kullanıcılar YouTube kaynaklı kanalları 1080p izleyemez.
*   **Affected Features:** YouTube Integration.
*   **Recommended Solution:** FlyleafLib'in çoklu giriş (multi-input) desteği kullanılarak video ve audio stream'leri ayrı ayrı bağlanmalıdır.

---

## C. ARCHITECTURE AUDIT

*   **View-Logic Coupling:** `PlayerView.xaml.cs` (1441 satır) gereğinden fazla sorumluluk taşıyor (Flyleaf yönetimi, OSD zamanlayıcıları, AceStream kontrolü, EPG refresh, DSP filtreleri). Bu durum UI thread üzerinde beklenmedik donmalara yol açabilir.
*   **Singleton/Global State:** `HlsProxyEngine.Instance`, `ChannelAggregator.Instance` gibi singletonlar doğru kullanılmış ancak `DatabaseEngine` hem singleton gibi davranıp hem de sürekli yeni instance'lar üzerinden (`new DatabaseEngine()`) repository'lere erişiyor. Bu durum `AsyncDbLock` (Semaphore) sayesinde güvenli olsa da karmaşık bir yapı oluşturuyor.
*   **Lifecycle Management:** `App.xaml.cs` içinde `OnExit` sırasında AceStream durduruluyor ancak `HlsProxyEngine` poller'ları ve HTTP listener'ı için explicit bir `Dispose` zinciri tüm servislerde tam kapsama sahip değil.

---

## D. PLAYER / HLS / DVR AUDIT

*   **Positive:** `StartTsLiveStreamChunker` MPEG-TS akışlarını başarıyla HLS'e çeviriyor ve DVR imkanı sağlıyor.
*   **Risk:** `InspectAndPrepareHlsAsync` içindeki 2.5s timeout (`cts.CancelAfter(2500)`) yavaş server'larda HLS'in proxy'ye girmesini engelleyip doğrudan (Timeshift'siz) oynatmaya zorlayabilir.
*   **Evidence:** [PlayerView.xaml.cs:L485-525](file:///C:/Users/Administrator/Downloads/streammesh/UI/Views/PlayerView.xaml.cs#L485-L525)

---

## E. DATABASE & AGGREGATION AUDIT

*   **Migration Risk:** `DatabaseEngine` constructor'ında `EnsureDataMigrationAsync` bir `Task.Run` içinde başlatılıyor. Eğer uygulama çok hızlı açılır ve bu task bitmeden database'e yazmaya çalışırsa (özellikle favoriler/ayarlar için) race condition oluşabilir.
*   **Aggregation:** `ChannelAggregator` URL, EPG ID ve Hash bazlı eşleme yaparak mükerrer kanalları başarıyla engelliyor. `MergeWith` metodu tüm alanları kapsıyor.
*   **Cleanup:** `CleanupDuplicatesAsync` içindeki SQL, URL içinde `var ` veya `function` geçenleri siliyor. Bazı meşru IPTV URL'leri query parametrelerinde bu kelimeleri içerebilir (nadir bir risk).

---

## F. UI/UX AUDIT

*   **Logo Fallback:** Logo bulunamadığında renkli monogram üretimi (`GenerateMonogram`) UX açısından çok başarılı.
*   **OSD Sync:** Timeshift sırasında sarı rozetle geriden gelinen sürenin gösterilmesi (`UpdateOsdTimeAndBadge`) FEATURE_MAP ile tam uyumlu.
*   **Binding Errors:** `LogService` üzerinde yoğun binding hatası raporlanmamış olsa da, Converter'larda (`LogoCacheConverter`) exception handling "silent" durumda.

---

## G. REGRESSION RISK MATRIX

| Değişiklik | Etkilenebilecek Özellik | Risk Seviyesi | Kanıt |
| :--- | :--- | :--- | :--- |
| `Channel.cs` alan değişikliği | Aggregator / Database | HIGH | `MergeWith` ve `Channels` tablosu senkron olmalı. |
| `HlsProxyEngine` port değişimi | Player / MediaServer | MEDIUM | Local HTTP URL'ler bozulabilir. |
| `AceEngine` hash regex değişimi | Aggregation / Playback | HIGH | Kanalların birleşmemesine veya oynatılmamasına neden olur. |
| `EpgEngine` timezone heuristiği | OSD / Program Rehberi | MEDIUM | Yayın saatlerinin kaymasına neden olur. |

---

## H. FEATURE_MAP ACCURACY

*   **DatabaseEngine Satır Sayısı:** FEATURE_MAP'ta 1-1323 satır denmiş, ancak mevcut dosya 324 satır. Kodun repository'lere bölündüğü (ChannelRepository vb.) ancak haritanın güncellenmediği görülüyor.
*   **server.js:** Haritada bu dosyaya dair bir referans yok. Projenin web portalı mı yoksa test amaçlı bir mock server mı olduğu belirsiz.
*   **HlsProxyEngine:** Haritadaki "30-60 dk" vaadi, koddaki `MaxMemoryCachedSegments` (320) ve `MaxTrackedHistorySegments` (15000) ile doğrulanıyor.

---

## I. FINAL PRIORITY LIST (P0 - P3)

1.  **P1 - HLS Fix:** `EVENT` playlist tipinin sliding window ile uyumlu hale getirilmesi (Etiket değişikliği veya TRUNCATE yönetimi).
2.  **P1 - YouTube Fix:** 1080p desteği için FlyleafLib'e DASH (dual-stream) desteği eklenmesi.
3.  **P2 - Database Migration:** `InitializeDatabase` içindeki migration task'inin await edilmesi veya uygulama startup'ında kritik bölüm olarak işaretlenmesi.
4.  **P2 - Epg Timezone:** `TryParseXmlTime` metodunun daha standart bir parse kütüphanesiyle güçlendirilmesi.
5.  **P3 - Code Cleanup:** `server.js` dosyasının amacının netleştirilmesi, kullanılmıyorsa kaldırılması.
6.  **INFO - Testing:** Kritik veri akışları için (Aggregation, M3U Parsing) unit testlerin eklenmesi.

---

## J. DOKUNULMAMASI GEREKENLER (Kritik Stabilite Alanları)

*   **FlyleafLib Config:** `demuxer` üzerindeki low-delay ve reconnect ayarları çok hassas kurgulanmış, kanal değişim hızı için kritik.
*   **ChannelAggregator:** Mevcut eşleme mantığı (AceHash > URL > EpgId) stabil çalışıyor.
*   **TS Chunking Logic:** `StartTsLiveStreamChunker` içindeki 188-byte alignment mantığı DVR'ın düzgün çalışması için elzemdir.

---

### NOT: Kod değişikliği yapmadan önce bu raporun kullanıcı tarafından onaylanması ve hangi önceliklerin (P1, P2 vb.) işleme alınacağının belirlenmesi gerekmektedir.
