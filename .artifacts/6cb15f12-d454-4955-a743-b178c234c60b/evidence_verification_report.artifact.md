# STREAMMESH – İKİNCİ AŞAMA KANIT DOĞRULAMA RAPORU

Bu rapor, ilk aşamada tespit edilen bulguların kod akışı üzerinden kesin kanıtlarla doğrulanması ve yeniden sınıflandırılması amacıyla hazırlanmıştır.

## 1. BULGU DOĞRULAMA TABLOSU

| ID | Özellik | İlk Karar | İkinci Doğrulama | Sonuç | Güven |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **P1-001** | **HLS DVR** | HIGH | **CONFIRMED PROTOCOL VIOLATION** | **RUNTIME BUG** | %100 |
| **P1-002** | **AceStream** | HIGH | **DESIGN TRADE-OFF** | **POTENTIAL RISK** | %90 |
| **P1-003** | **YouTube** | HIGH | **FEATURE LIMITATION** | **STABLE** | %100 |
| **P2-001** | **Database** | MEDIUM | **CONFIRMED BUG** | **RACE CONDITION** | %100 |
| **P2-002** | **EPG Time** | MEDIUM | **CONFIRMED PROTOCOL VIOLATION** | **DATA INCONSISTENCY** | %100 |
| **P2-003** | **PlayerView**| MEDIUM | **CONFIRMED ARCHITECTURAL ISSUE**| **TECHNICAL DEBT** | %100 |
| **P3-001** | **Lifecycle** | LOW | **CONFIRMED RESOURCE LEAK** | **MEMORY/PORT LEAK** | %100 |
| **P3-002** | **server.js** | LOW | **DEAD CODE CANDIDATE** | **UNUSED** | %100 |

---

## 2. DETAYLI KANIT VE ANALİZ

### **[P1-001] HLS EVENT vs. Sliding Window (KRİTİK)**
*   **Doğrulama:** `HlsProxyEngine.cs:L624` satırında her manifest için kayıtsız şartsız `#EXT-X-PLAYLIST-TYPE:EVENT` yazılıyor.
*   **Kanıt:** Aynı dosyanın 520. satırında `MaxTrackedHistorySegments` (15.000) aşıldığında `RemoveRange(0, 500)` ile segmentler siliniyor.
*   **Protokol İhlali:** Apple HLS spec (RFC 8216) gereği, `EVENT` tipi playlistler asla kısalmamalı, sadece sonuna ekleme yapılmalıdır. Eğer playlist kısalıyorsa tip `VOD` olmalı veya tip belirtilmemelidir (Sliding Window).
*   **Runtime Bug:** `GenerateManifest` metodunda `segmentsToServe` koleksiyonu kırpılmış listeyi kullandığı için, `MEDIA-SEQUENCE` numarası geriden gelen bir oyuncu için aniden atlayacak veya eski segmentler HTTP 404 verecektir.
*   **DVR Etkisi:** Kullanıcı 24 saat boyunca uygulamayı açık bırakırsa, 15.001. segmentte playlist'in başı silindiği için oyuncu hata verip duracaktır.

### **[P1-002] AceStream Web Scraping**
*   **Doğrulama:** `AceEngine.cs:L120-170` arama (Search) işlemi için `search-ace.stream` ve `ace-stream.net` sitelerini Regex ile tarıyor.
*   **Analiz:** Bu bir "Bug" değil, "Design Trade-off"dur. Playback pipeline'ı (Oynatıcı) bu sitelere bağımlı değildir; doğrudan `127.0.0.1:6878` API'sini kullanır.
*   **Karar:** P1 seviyesinden **P2 (Orta)** seviyesine düşürülmüştür. Sadece "Arama" özelliğini bozar, mevcut kanalları etkilemez.

### **[P1-003] YouTube 1080p**
*   **Doğrulama:** `YoutubeEngine.cs:L55-78` sadece `GetMuxedStreams()` kullanıyor.
*   **Analiz:** Muxed stream'ler YouTube tarafında genellikle 720p ile sınırlıdır. 1080p için DASH (ayrı ses/video) gereklidir.
*   **Karar:** `FEATURE_MAP` üzerinde "1080p zorunluluğu" belirtilmediği için bu bir hata değil, **FEATURE LIMITATION** (Özellik Kısıtı) olarak sınıflandırılmıştır.

### **[P2-001] Database Migration Race Condition**
*   **Doğrulama:** `DatabaseEngine.cs` constructor'ı içinde `Task.Run` ile migration başlatılıyor ancak **await edilmiyor**.
*   **Kanıt:** `App.xaml.cs` startup sırasında `new DatabaseEngine()` yaptıktan hemen sonra diğer servisleri başlatıyor. `GetAllChannelsAsync` gibi metotlar `AsyncDbLock` kullanmadığı için migration devam ederken (batch save sırasında) database'e erişebilir.
*   **Sonuç:** Uygulama ilk açılışta boş veya eksik kanal listesi gösterebilir.

### **[P2-002] EPG Timezone Heuristiği**
*   **Doğrulama:** `EpgEngine.cs:L205` `TryParseXmlTime` metodu.
*   **Kanıt:** Kod, standarda uygun "Z" (UTC) son ekini tanımıyor (offset kısmı sadece + veya - bekliyor). Sadece "turk" veya "iptv-epg.org" içeren URL'leri `Local` kabul ediyor.
*   **Sonuç:** Diğer kaynaklardan gelen UTC tabanlı rehberler hatalı saatte (3 saat kayma ile) gösterilir.

### **[P3-001] HLS Lifecycle / Port Leak**
*   **Doğrulama:** `App.xaml.cs.OnExit` içinde `AceStream` durduruluyor ancak `HlsProxyEngine.Instance.Stop()` çağrılmıyor.
*   **Kanıt:** `HlsProxyEngine` içinde çalışan bir `HttpListener` ve `ListenLoop` (Task) bulunmaktadır. Uygulama kapandığında bu portlar "TIME_WAIT" durumunda takılı kalabilir veya background task'ler zombie process oluşturabilir.

---

## 3. SINIFLANDIRILMIŞ ÖNCELİK LİSTESİ

### **GERÇEKTE DÜZELTİLMESİ GEREKENLER (MUST FIX)**
1.  **HLS EVENT Etiketi:** `EVENT` yerine sliding window mantığına geçilmeli (P1).
2.  **Database Startup:** Migration işlemi startup'ta bloklayıcı (blocking) hale getirilmeli veya await edilmeli (P2).
3.  **EPG Timezone:** `DateTimeOffset` standardına tam uyumlu parse mantığına geçilmeli (P2).
4.  **HLS Shutdown:** `OnExit` event'ine `HlsProxyEngine.Stop()` eklenmeli (P3).

### **SADECE ÖZELLİK GELİŞTİRMESİ OLANLAR (FEATURE GAP)**
1.  **YouTube 1080p:** Mevcut mimaride 720p yeterliyse dokunulmamalı.
2.  **AceStream Provider:** Scraping yerine API tabanlı provider'lar eklenmesi.

### **SADECE TEKNİK BORÇ OLANLAR (TECHNICAL DEBT)**
1.  **PlayerView Bloat:** 1441 satırlık code-behind'ın ViewModels/Services katmanına kaydırılması.
2.  **server.js:** Kullanılmıyorsa deployment paketinden çıkarılmalı.

### **YANLIŞ POZİTİF / REDDEDİLEN BULGULAR**
*   *Yok. Önceki rapordaki tüm bulguların bir risk teşkil ettiği doğrulandı, sadece önem seviyeleri (P1/P2) güncellendi.*

---

## 4. ŞU ANDA DOKUNULMAMASI GEREKEN STABİL ALANLAR

*   **Flyleaf Demuxer Ayarları:** Mevcut timeout ve buffer ayarları (low-delay) canlı yayın performansı için mükemmel durumda.
*   **ChannelAggregator Eşleme Mantığı:** AceHash bazlı birleştirme oldukça stabil.
*   **MPEG-TS 188-byte Alignment:** `StartTsLiveStreamChunker` içindeki buffer yapısı DVR kararlılığı için korunmalı.

---

### SONUÇ:
Uygulama çalışır durumda olsa da, **HLS Protokol İhlali (P1)** ve **Database Startup Yarışı (P2)** en öncelikli risklerdir. Bu iki konu düzeltilmeden yapılacak büyük değişiklikler regresyona neden olabilir.

**BU AŞAMADA HİÇBİR KOD DEĞİŞİKLİĞİ YAPILMAMIŞTIR.**
