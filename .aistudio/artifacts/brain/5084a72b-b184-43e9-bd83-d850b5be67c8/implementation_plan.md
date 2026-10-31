# GitHub M3U & Medya Kaynaklarını Otomatik Derin Tarama (Deep Repo Crawler) Revizyon Planı

Uygulamanın eskiden bulduğu **~1.944 kaynak** yerine şu anda yalnızca **194 kaynak** bulmasının teknik analizi, GitHub depolarındaki tüm `.m3u`, `.m3u8` ve video dosyalarını (`.mp4` vb.) otomatik tarama yeteneği ve **tüm yeni kaynaklardaki IPTV sağlayıcılarının bulut güncellemesiyle tam entegre otomatik tespiti/doğrulanması** revizyon planıdır.

---

## Kullanıcı İncelemesi & Kritik Kararlar

> [!IMPORTANT]
> **Kullanıcı Sorusu ve Çözüm Yaklaşımı:**
> *"Bulduğu yeni kaynaklarda da IPTV sağlayıcılarını kontrol edecek değil mi? Bunu ayrı mı yapacaksın, bulut güncellemesine mi ilave edeceksin?"*
> 
> **Cevap & Kesin Tasarım:**
> 1. **Bulut Güncellemesine Doğrudan İlave Edilecek (Tek Akış):** Derin tarayıcı harici/ayrı bir işlem olarak bırakılmayacak; doğrudan mevcut **"Bulut Güncelleme" (`GitHubSyncEngine`)** sürecine entegre edilecek. Böylece kullanıcı tek bir "Bulut Güncelleme" butonuna bastığında:
>    - Depolar derin taranıp 1.944 kaynak toplanacak,
>    - İçerikler indirilirken `IptvAccountHelper` ve `XtreamService` devreye girerek yeni kaynaklardaki **tüm IPTV sağlayıcıları ve hesapları otomatik olarak yakalayacak, doğrulayacak ve aktif olanları kütüphaneye bağlayacaktır.**
> 2. **Ayarlar Sekmesinde Bağımsız Denetim Butonu (Ek Güvenlik):** Kullanıcı sadece depoları veya sadece IPTV sağlayıcılarını tek başına yenilemek isterse, Ayarlar menüsünden bağımsız olarak da tetikleyebilecektir.

- **Onaylanan Karar 1 (Kök Neden):** GitHub'daki `out/full_cleaned_playlist.m3u` master dosyasının 404 vermesi ve sistemin doğrudan `auto_update.json`'daki tekil 194 linke düşmesi.
- **Onaylanan Karar 2 (Entegre Derin Tarama):** 5 ana depodan (`UzunMuhalefet/Legal-IPTV` [1.520 dosya], `iptv-org` [439], `don24crk` [69], vb.) toplam 1.944+ kaynağın `api.github.com/repos/.../git/trees/` ile tek seferde çekilmesi.
- **Onaylanan Karar 3 (IPTV Sağlayıcı Keşfi):** Bulunan 1.944 kaynaktan akan akış linkleri parse edilirken Xtream/IPTV hesaplarının (`username`, `password`, `host`) otomatik süzülüp `IptvAccounts` tablosunda doğrulanması.

---

## 1. Genel Bakış ve Temel Konsept

- **Ne Sağlar?**: Bulut güncellemesi tek bir hamlede çalışır: Hem GitHub üzerindeki 1.944 kaynak derinlemesine taranır, hem de bu kaynakların içerisindeki tüm açık IPTV sağlayıcıları otomatik olarak test edilip aktif hesaplar tespit edilir.
- **Kullanıcı Kolaylığı**: Kullanıcı hiçbir ek işlem yapmadan, sadece "Bulut Güncelleme"ye bastığında tüm dizi arşivleri, film listeleri, tematik TV kanalları ve IPTV hesapları eksiksiz yenilenir.
- **Sayısal Doğrulama**: 194 kaynak sınırından çıkılarak tam **~1.944 kaynak** hedefine ulaşılır.

---

## 2. Kullanıcı Deneyimi ve Arayüz Tasarımı

### A. Birleşik Bulut Güncelleme İlerleme Süreci
Kullanıcı Ana Sayfa veya Ayarlar ekranından "Bulut Güncelleme"yi başlattığında sırayla şu adımlar işler:

```
[Adım 1/4] 🌐 GitHub yapılandırması alınıyor (auto_update.json)...
[Adım 2/4] 🔍 GitHub depoları derin taranıyor (UzunMuhalefet: 1.520, iptv-org: 439, don24crk: 69)...
            -> Toplam 1.944 kaynak başarıyla tespit edildi!
[Adım 3/4] ⚡ 1.944 kaynak paralel işleniyor (Diziler, Filmler, Canlı TV)...
[Adım 4/4] 🔑 Yeni kaynaklardaki IPTV sağlayıcıları ve hesapları taranıyor...
            -> [Aktif] Xtream IPTV sağlayıcısı doğrulandı ve bağlandı.
🎉 Senkronizasyon tamamlandı! Toplam 1.944 kaynak ve IPTV sağlayıcıları güncellendi.
```

### B. Ayarlar > IPTV Hesapları Sekmesi
- Yeni taranan 1.944 kaynaktan yakalanan tüm çalışan IPTV sağlayıcıları yeşil "Aktif" rozetiyle kart olarak görünür.
- Kalan süre, aktif bağlantı sayısı ve kanal sayıları otomatik listelenir.

---

## 3. Temel Ürün Kararları & Mimari Detaylar

| Konu | Seçilen Yaklaşım | Tercih Nedeni |
| :--- | :--- | :--- |
| **IPTV Kontrolünün Yeri** | **Bulut Güncellemesi İçinde Otomatik** | Kullanıcıyı birden fazla butona basma zahmetinden kurtarır. M3U listeleri indirilirken hesaplar anında keşfedilir. |
| **Ekstra Bağımsız Tetikleyici** | **Ayarlar > IPTV > "Hesapları Tara"** | Kullanıcı dilediğinde tüm kütüphaneyi baştan indirmeden sadece hesap durumlarını hızlıca yeniden test edebilir. |
| **GitHub API Kota Yönetimi** | **Git Trees API (Tek İstek / Depo)** | Toplam 5 depo için sadece 5 HTTP isteği yapılır; saatlik 60 API limitinin %10'u bile kullanılmaz. |
| **Kategori Zenginleştirme** | **Dosya Yolu Bazlı Otomatik Ayrıştırma** | `UzunMuhalefet/Legal-IPTV` içindeki 1.520 dosya dosya yolundaki `/diziler/`, `/filmler/`, `/belgeseller/` klasörlerine göre doğru sekmelere yerleştirilir. |

---

## 4. Teknik Mimari ve Uygulama Adımları

```
┌────────────────────────────────────────────────────────────────────────┐
│             Bulut Güncelleme Akışı (GitHubSyncEngine.cs)                │
├────────────────────────────────────────────────────────────────────────┤
│ 1. auto_update.json'dan tohum depolar belirlenir                      │
│ 2. GitHubRepoCrawler devreye girer:                                    │
│    • UzunMuhalefet/Legal-IPTV (1.520 dosya)                            │
│    • iptv-org/iptv (439 dosya)                                         │
│    • don24crk/Don24crk-Repository (69 dosya)                           │
│    • Zerk1903/zerkfilm (19 dosya)                                      │
│    • hayatiptv/iptv (19 dosya)                                         │
│ 3. Toplam 1.944 M3U/Video adresi oluşturulur                           │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Paralel İndirme & Ayrıştırma
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                   M3uEngine & IptvAccountHelper                        │
├────────────────────────────────────────────────────────────────────────┤
│ • 1.944 kaynaktaki kanallar çözümlenir                                │
│ • Her URL içerisinden Xtream/IPTV hesap parametreleri çekilir          │
│ • Host, Port, Username, Password ayrıştırılır                         │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Keşfedilen IPTV Sağlayıcıları
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│            XtreamService & DatabaseEngine (SQLite)                     │
├────────────────────────────────────────────────────────────────────────┤
│ • Keşfedilen sağlayıcılar test edilir (auth / player_api)              │
│ • Çalışan IPTV hesapları IptvAccounts tablosuna eklenir                │
│ • Canlı TV, VOD Film, Dizi ve IPTV kütüphanesi senkronize edilir       │
└────────────────────────────────────────────────────────────────────────┘
```

### Uygulama Sırası:

1. **`Core/Media/GitHubRepoCrawler.cs` (Derin Tarayıcı Motoru):**
   - GitHub linklerinden repo ve dal adını çıkaran ayrıştırıcı.
   - GitHub Trees API üzerinden özyinelemeli (recursive) dosya tarama ve `.m3u`, `.m3u8`, `.mp4` dosyalarını 1.944 linke dönüştürme.

2. **`Core/Media/GitHubSyncEngine.cs` (Bulut Güncelleme İlavesi):**
   - Bulut güncellemesine derin tarama aşamasını ekleme (194 -> 1.944 kaynak).
   - İndirme tamamlandıktan hemen sonra `_db.DiscoverAndSyncAccountsFromChannelsAsync()` ve `XtreamService` ile sağlayıcı doğrulamasını otomatik tetikleme.

3. **`Core/Media/IptvAccountHelper.cs` & `XtreamService.cs`:**
   - 1.944 kaynaktan gelen URL'lerdeki sağlayıcıları süzme ve mükerrerleri ayıklayarak doğrulama.

4. **UI & İlerleme Bildirimleri:**
   - Senkronizasyon barında derin tarama ve IPTV sağlayıcı kontrolünün adım adım durum mesajlarıyla kullanıcıya aktarılması.

5. **Derleme & Doğrulama:**
   - `compile_applet` ile projenin hatasız derlenmesi ve kaynak sayısının 1.944 olarak doğrulanması.
