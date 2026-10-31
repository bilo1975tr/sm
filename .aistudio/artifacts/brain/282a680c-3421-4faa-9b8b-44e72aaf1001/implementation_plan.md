# AceStream Yayınlarında Kesintisiz Oynatma, Durdur/Devam Et ve Timeshift (Geri Sarma) Mimari Uygulama Planı

StreamMesh üzerinde AceStream P2P canlı yayınlarının oynatılması sırasında yaşanan veri alamama (HTTP 500 / soket çakışması) sorununu giderirken; kullanıcının talep ettiği **Durdur / Kaldığın Yerden Devam Et (Pause/Resume)** ve **Geri Sar (Timeshift DVR / Geçmişe Dönüş)** yeteneklerini eksiksiz koruyan, mimari olarak simüle edilmiş çözüm planıdır.

---

## Kullanıcı İncelemesi ve Kritik Mimari Kararlar

> [!IMPORTANT]
> Kullanıcımızın açık talebi: *"AceStream yayınlarında ben durdur, kaldığın yerden devam et ve geri sar özellikleri kalsın istiyorum."*
> Bu özelliklerin korunması için doğrudan ham stream (`getstream`) vermek tek başına yetersizdir; çünkü ham MPEG-TS soketinde oynatıcı duraklatıldığında arka plandaki P2P motoru akışı keser veya arabellek taşması yaşar. Geri sarmak ise ancak geçmiş paketlerin yerel olarak indekslenip saklanmasıyla mümkündür.

- **Onaylanan Yaklaşım (Simüle Edilen Hibrit Mimari):**
  - **AceStream Doğal Manifest Entegrasyonu (`/ace/manifest.m3u8?id=...`):** AceStream motorunun kendi ürettiği resmi HLS manifestosu, StreamMesh'in yerel `HlsProxyEngine` arabelleğine bağlanır.
  - **Sıfır Soket Çakışması (Tek Tüketici Garantisi):** AceStream motoruna (port 6878) **yalnızca tek bir istemci** (`HlsProxyEngine`) bağlanır. Flyleaf video oynatıcısı motora asla doğrudan bağlanmaz; yalnızca yerel Timeshift portuna (`127.0.0.1:48931/playlist.m3u8`) bağlanır.
  - **Genişletilmiş P2P Isınma Penceresi (Watchdog 25s):** P2P eşlerinin bulunması ve ilk HLS segmentlerinin oluşması için gereken süre (10-18 sn) OSD üzerinde net aşamalarla ("Eşler aranıyor...", "P2P arabelleği hazırlanıyor...") kullanıcıya yansıtılır, erken zaman aşımı engellenir.
  - **Durdur / Devam Et ve Geri Sarma Mekanizması:** Yayın durdurulduğunda `HlsProxyEngine` arka planda AceStream segmentlerini çekmeye ve hafızadaki LRU segment havuzunda saklamaya devam eder. Kullanıcı devam ettirdiğinde yayın kaldığı saniyeden başlar; zaman çizelgesini sola çektiğinde geçmiş segmentler oynatıcıya sunulur.

---

## 1. Genel Bakış ve Temel Konsept

* **Ne İşe Yarar:** Kullanıcı herhangi bir `acestream://` linkine veya kütüphanedeki AceStream kanalına tıkladığında, StreamMesh yerel AceStream Engine ile senkronize olur, yayını çakışma olmadan başlatır ve canlı yayını sanki bir video dosyasıymış gibi duraklatılabilir, geri sarılabilir (Timeshift) ve anlık canlıya dönülebilir hale getirir.
* **Hedef Kitle:** Spor müsabakalarını ve canlı TV yayınlarını P2P (AceStream) üzerinden takip eden, önemli anları geri sarıp tekrar izlemek veya yayını duraklatıp sonra devam etmek isteyen kullanıcılar.
* **Sağlanan Temel Değer:** 
  1. CMD üzerinden alınan yüksek verim ve kararlılık uygulama içerisine taşınır (0 adet 500 Server Error).
  2. Standart bir oynatıcıda yapılamayan canlı AceStream yayınını **durdurma, 15 dakikaya kadar geri sarma ve kaldığı yerden devam ettirme** lüksü korunur.

---

## 2. Kullanıcı Deneyimi ve Arayüz Akışı (UX Flow)

```
[Kanal Seçimi: acestream://...]
       │
       ▼
[OSD Durum Bildirimi]: "AceStream: P2P Eşleri Aranıyor... (127.0.0.1:6878)"
       │
       ▼
[AceStream Engine]: /ace/manifest.m3u8?id={ContentId} başlatılır (Tek Soket)
       │
       ▼
[HlsProxyEngine]: İlk 2 segment indirildi -> "Yayın Başlatılıyor..."
       │
       ▼
[Flyleaf Oynatıcı]: http://127.0.0.1:48931/playlist.m3u8 üzerinden akış başlar
       │
       ├─────────────────────────────────────────────────────────────┐
       ▼                                                             ▼
[Kullanıcı "DURDUR"a Bastı]                             [Kullanıcı Çizelgeyi Geri Sardı]
       │                                                             │
Flyleaf durur; HlsProxyEngine arkada segmentleri              HlsProxy geçmiş segmentleri
hafızada (Timeshift buffer) toplamaya devam eder.             Flyleaf'e anında besler.
       │                                                             │
[Kullanıcı "OYNAT"a Bastı]                              [OSD Göstergesi]: "-02:45 Gecikmeli"
Yayın durduğu kareden pürüzsüz devam eder.                     [CANLIYA DÖN Butonu Aktif]
```

* **OSD Bildirimleri ve Şeffaflık:** 
  - İlk bağlantı esnasında "Sinyal Yok" yanılgısını önlemek için OSD başlığında sırasıyla: 
    1. *`"AceStream: Motor Hazırlanıyor..."`*
    2. *`"AceStream: P2P Eşleri Aranıyor (Eşleşme bekleniyor)..."`*
    3. *`"AceStream: Timeshift Arabelleği Hazır"`* adımları gösterilir.
* **Timeshift Kontrolleri:**
  - Canlı yayında OSD üzerinde `🔴 CANLI` rozeti yer alır.
  - Yayın duraklatıldığında veya geri sarıldığında rozet turuncu `⚡ -01:30` (gecikme süresi) haline gelir ve `⚡ CANLIYA DÖN` butonu belirir. Bu butona basıldığında oynatıcı doğrudan canlı uca zıplar.

---

## 3. Temel Ürün Kararları ve Tercihler (Trade-Offs)

### Karar 1: Yapay C# TS Chunker Yerine AceStream Doğal HLS API Kullanımı
* **Seçilen Yaklaşım:** AceStream motorunun kendi yerel `/ace/manifest.m3u8?id=...` uç noktası `HlsProxyEngine` poller'ına bağlanacaktır.
* **Neden:** Eski kodda ham TS soketi C# içinde `1200 * 188` baytlık yapay bloklara kesilmeye çalışılmış, bu da PTS/DTS zaman damgası bozulmalarına, soket kilitlenmelerine ve FFmpeg'in çökmesine neden olmuştur. AceStream'in kendi manifestosu ise standart HLS paketlemesi yapar.
* **Alternatif:** Ham TS akışını doğrudan Flyleaf'e vermek. *Neden elendi:* Bu seçenek geri sarma (rewind) ve duraklatıp devam etme yeteneğini tamamen yok ederdi.

### Karar 2: Tek Tüketici İzolasyonu (Single-Consumer Socket Isolation)
* **Seçilen Yaklaşım:** Flyleaf oynatıcısı AceStream portuna (6878) asla doğrudan bağlanmayacaktır. Tüm iletişim `StreamMesh HlsProxy` üzerinden geçecektir.
* **Neden:** Loglarda açıkça görülen `Server returned 5XX Server Error reply (-1482175992)` hatasının sebebi, hem `HlsProxy`'nin hem de Flyleaf'in aynı anda motora istek atmasıdır. Tek soket kullanıldığında AceStream CMD'deki gibi tek bir istemciyle konuşur ve sıfır hata üretir.

### Karar 3: Sayısal `channel_id` İçin Zorunlu Ön Çözümleme
* **Seçilen Yaklaşım:** `channel_id` içeren linkler oynatma döngüsüne girmeden önce `get_content_ids` servisi ile 40 haneli Content ID'ye çözümlenecektir.
* **Neden:** AceStream Engine `/ace/getstream` veya `/ace/manifest.m3u8` uç noktalarında `channel_id` parametresini desteklemez ve doğrudan 500 Server Error verir.

---

## 4. Teknik Mimari ve Veri Akışı Diyagramı

```
┌────────────────────────────────────────────────────────────────────────┐
│                        StreamMesh Uygulaması                           │
│                                                                        │
│   ┌────────────────────┐            ┌──────────────────────────────┐   │
│   │  PlayerViewModel   │            │        PlayerView (UI)       │   │
│   │                    │            │  - FlyleafHost Video Surface │   │
│   │  - Akıllı URL Seç. │            │  - OSD & DVR Slider          │   │
│   │  - channel_id Res. │            │  - 25s P2P Watchdog Guard    │   │
│   └─────────┬──────────┘            └──────────────▲───────────────┘   │
│             │                                      │ (HLS Playback)    │
│             ▼                                      │ 127.0.0.1:48931   │
│   ┌────────────────────────────────────────────────┴───────────────┐   │
│   │                       HlsProxyEngine                           │   │
│   │  - LruSegmentCache (Hafızada 320 segment DVR arabelleği)       │   │
│   │  - LivePoller: AceStream /ace/manifest.m3u8 takibi             │   │
│   │  - Dynamic Sliding Window Playlist Generator (#EXT-X-MEDIA)    │   │
│   └──────────────────────────────┬─────────────────────────────────┘   │
│                                  │ (Tek HTTP Soketi / Polling)         │
│                                  │ http://127.0.0.1:6878               │
└──────────────────────────────────┼─────────────────────────────────────┘
                                   │
                                   ▼
┌────────────────────────────────────────────────────────────────────────┐
│                     AceStream Engine (ace_engine.exe)                  │
│                                                                        │
│   ┌────────────────────────────────────────────────────────────────┐   │
│   │  HTTP Gateway (Port 6878):                                     │   │
│   │  - /ace/manifest.m3u8?id={ContentID}                           │   │
│   │  - /ace/c/{SegmentHash}.ts                                     │   │
│   └──────────────────────────────▲─────────────────────────────────┘   │
│                                  │                                     │
│                                  ▼                                     │
│   ┌────────────────────────────────────────────────────────────────┐   │
│   │  P2P Swarm Engine (BitTorrent / DHT / Trackers):               │   │
│   │  - Eş Bulma, Parça İndirme, Arabellek Doldurma                 │   │
│   └────────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 5. Değiştirilecek Dosyalar ve Yapılacak İşler Matrisi

### 1. `Core/Media/AceEngine.cs`
- [ ] `ExtractAceId` mantığını güçlendirip `channel_id=` içeren URL'leri tespit etmek.
- [ ] `ResolveChannelIdToContentIdAsync` metodunu kesinleştirmek; dönen cevabın doğrudan 40 karakterlik Content ID olmasını sağlamak.
- [ ] `GetHttpUrlsWithTokenAsync` içindeki URL şablonlarını revize etmek:
  - `infohash=` yerine standart **`id={ContentId}`** ve **`content_id={ContentId}`** önceliğini yerleştirmek.
  - Manifest URL'sini birincil hedef yapmak: `http://127.0.0.1:6878/ace/manifest.m3u8?id={id}`.
- [ ] `IsEngineHttpAliveAsync` kontrolünü 6878 portuna `GET /server/api?method=get_version` ile güvenilir kılmak; yanıt vermeyen zombi süreçleri temizleme desteği eklemek.

### 2. `Core/Media/HlsProxyEngine.cs`
- [ ] `PrepareAceStreamHlsAsync` metodunu baştan tasarlamak:
  - Hatalı `StartTsLiveStreamChunker` (ham TS'yi rastgele parçalama) mekanizmasını AceStream için devre dışı bırakmak.
  - Bunun yerine AceStream'in resmi `http://127.0.0.1:6878/ace/manifest.m3u8?id={hash}` manifestosunu standart HLS oturumu olarak `InspectAndPrepareHlsAsync` içerisine beslemek.
- [ ] P2P gecikmesine uygun olarak ilk manifest bekleme süresini (timeout) 20-25 saniyeye çıkarmak.
- [ ] Kanal değiştiğinde veya durdurulduğunda eski AceStream oturumunu ve soketini kesin olarak `Cancel` etmek.

### 3. `UI/ViewModels/PlayerViewModel.cs`
- [ ] `GetSmartOrderedCandidatesAsync` içinde AceStream için aday URL türetilirken `infohash` yerine:
  - `http://127.0.0.1:6878/ace/manifest.m3u8?id={id}` (HLS Timeshift için 1. aday)
  - `http://127.0.0.1:6878/ace/getstream?id={id}` (Doğrudan yedek akış için 2. aday) sıralamasını oluşturmak.
- [ ] `PrepareSingleStreamUrlAsync` içinde, HLS Timeshift hazırlanırken kullanıcıya adım adım ilerleme mesajları iletmek (`onStatusUpdate`).

### 4. `UI/Views/PlayerView.xaml.cs`
- [ ] `OpenAndAwaitPlaybackAsync` içinde AceStream yayınları için watchdog süresini 25000ms (25 saniye) olarak ayarlamak.
- [ ] `_config.Demuxer.FormatOpt` ayarlarını HLS canlı Timeshift oynatımına tam uyumlu hale getirmek (`reconnect=1`, `probesize=5000000`).
- [ ] Oynatıcı duraklatıldığında (`TogglePause`) ve geri sarıldığında (`Slider_DragCompleted`) Timeshift arabelleğinin kesilmeden akmaya devam etmesini güvenceye almak.

---

## 6. Doğrulama ve Test Senaryoları (Verification Plan)

Plan onaylandıktan ve uygulandıktan sonra aşağıdaki senaryolar adım adım test edilecektir:

1. **Doğrudan Content ID Açma:** `acestream://9b127536c9841e69bf03ff78e15b873b21bc22ab` kanalı seçilecek. 10-15 saniyelik P2P ısınma süresi sonrasında yayının açıldığı ve loglarda `500 Server Error` oluşmadığı doğrulanacak.
2. **Durdur / Devam Et (Pause/Resume) Testi:** Yayın oynarken OSD üzerinden "Durdur" butonuna basılacak. 30 saniye beklenecek. Ardından "Oynat" butonuna basıldığında yayının kesilmeden, durduğu kareden devam ettiği doğrulanacak.
3. **Geri Sarma (Timeshift DVR) Testi:** Yayın 2 dakika aktıktan sonra OSD zaman çizelgesi 1 dakika geriye sürüklenecek. Yayının 1 dakika önceki anı pürüzsüzce oynattığı ve `⚡ CANLIYA DÖN` butonunun belirdiği test edilecek.
4. **Canlıya Dönüş Testi:** `⚡ CANLIYA DÖN` butonuna tıklandığında oynatıcının anlık canlı yayına sıçradığı ve `🔴 CANLI` rozetinin yandığı kontrol edilecek.
5. **Kanal Değişimi & Soket İzolasyonu:** Bir AceStream kanalından başka bir AceStream kanalına geçildiğinde eski oturumun temizlendiği ve yeni kanalın kilitlenmeden açıldığı teyit edilecek.
