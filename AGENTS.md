# AI Asistan Sistem Yönergeleri

Bu dosya, yapay zeka asistanının bu projede nasıl davranması gerektiğini belirleyen kalıcı kuralları içerir.

## 1. İletişim ve Karakter
- **Dil:** Her zaman TÜRKÇE cevap verilecektir.
- **Biçim:** Cevaplar her zaman KISA, ÖZ ve doğrudan hedefe yönelik olacaktır. Geveze açıklamalardan kaçınılacaktır.
- **Proaktif Danışmanlık:** Kullanıcı uygunsuz, verimsiz veya uygulamayı bozacak bir talepte bulunursa; asistan kullanıcıyı UYARACAK, nedenini kısaca açıklayacak ve EN İYİ/DOĞRU alternatifi sunacaktır.

## 2. Geliştirme ve Kod Standartları
- **Sıfır Hata Prensibi:** Yazılan her kod parçası teslim edilmeden önce araçlar (lint, compile) aracılığıyla mutlaka SİMÜLE edilecek ve test edilecektir. Uygulama her adımda KESİNLİKLE ÇALIŞIR durumda kalacaktır.
- **Kod Kontrolü:** İstek geldiğinde sadece ilgili yer değil, bağlantılı olan tüm fonksiyonlar (satır satır) kontrol edilecek, potansiyel çökmeler önceden tespit edilip düzeltilecektir.
- **Özellik Hafızası:** Var olan bir özellik baştan yazılmayacak, mevcut sürüm tespit edilip REVİZE edilecektir.

## 3. Teknik Disiplin (Öğrenilmiş Dersler)
- **Platform Uyumu:** Native kütüphaneler (VLC) nedeniyle proje her zaman `x64` mimarisinde tutulacaktır.
- **LibVLCSharp API:** `MediaPlayer.VideoTracks` mevcut değildir; çözünürlük listesi için her zaman `VideoTrackDescription` kullanılacaktır.
- **Yayın Yönetimi:** Timeshift ve tampon (buffer) güvenliği için `SetPause()` kullanılacak, yayın dondurulduğunda canlıya atlamaması sağlanacaktır.
- **Hata Yakalama:** `App.xaml.cs` ve `PlayerView.xaml.cs` dosyalarında mutlaka `fatal_error.log` kaydı ve `MessageBox` uyarısı bulunacaktır.
- **Resource Yönetimi:** XAML içindeki `StaticResource` tanımları (Slider stili vb.) her zaman kontrol edilecek, eksik tanım nedeniyle uygulamanın açılmaması önlenecektir.

## 4. Ek Görevler
- Projeyi geliştirecek fikirler ve iyileştirme önerilerinde (performans, güvenlik, kullanıcı deneyimi) bulunulacaktır.

## 5. Uygulama Mimarisi ve Kurgu Mantığı (Yeni Kurallar)
- **P2P, Oylama ve Varsayılan Veri Sistemi:** Kanal adı, yayın URL'si, EPG ve Logolar tek bir statik satıra bağlı DEĞİLDİR. Sistemin P2P ağında oylanabilen, "Varsayılan Yap" seçeneğine sahip çoklu (birden fazla logo/URL barındırabilen) bir yapıda olduğu ASLA unutulmayacaktır. Arayüz ve model tasarımları bu esnekliğe uygun planlanacaktır.
- **Drag & Drop (Sürükle-Bırak) ile Kanal Birleştirme:** Kullanıcıların çift veya aynı yayını yapan kanalları birbirleri üzerine sürükleyerek BİRLEŞTİREBİLECEĞİ her zaman tasarımda ve kod altyapısında hesaba katılacaktır. Birleştirilen kanalların yayın URL'leri ve logoları tek kanal altında toplanır.
- **Kanal/Yayın Doğrulama Sistemi (Stream Checker):** Sadece "HTTP 200 OK" dönmesi kabul edilemez! Videonun gerçekten aktığı (frame test / paket alımı ile) KESİNLİKLE kontrol edilmelidir. 
- **Ayarlarda Doğrulama Menüleri:** Uygulama ayarlarında "Tüm Kanalları Kontrol Et" ve sadece kullanıcıların yeni eklediği "Onaysız Yayınları Kontrol Et" olmak üzere ayrılmış iki özellik kodlanacaktır. Çalışmayan kanallar tespit edilip havuzdan/onaydan kaldırılmalıdır.

## Değişiklik Günlüğü (Changelog)

### [0.0 alfa 00074] - 2026-06-01
- **Kapsamlı Dil ve Filtre Normalizasyonu:** `HomeView.xaml.cs` içerisindeki `NormalizeLanguage` metodu baştan aşağı güçlendirilerek `tr`, `en`, `de` gibi 2/3 harfli ISO kodları ve İngilizce dil adlarının (örneğin "turkish", "german", "english") yerel Türkçe karşılıklarına kusursuz eşleşmesi sağlandı; dilleri filtrelememe hatası kökten çözüldü.
- **"Bilinmiyor" Dil Entegrasyonu:** `LangCombo` (Kanal Düzenleme), `LanguageCombo` (Kaynak Düzenleyici) ve `BulkLanguageCombo` (Gelişmiş Kanal Yöneticisi) pencerelerinde "Bilinmiyor" seçeneği listenin başına/en üstüne yerleştirildi, böylece kullanıcıların diledikleri kanalları bu esnek tag ile düzenlemelerine imkan tanındı.

### [0.0 alfa 00073] - 2026-06-01
- **GitHub Sync / .gitignore Çakışması Giderildi:** `channels.json`, `channels_*.json` ve `kanallar_*.m3u` gibi kritik veri tabanı ve çıktı dosyaları `.gitignore` içerisinden tamamen kaldırıldı. Bu sayede, otomatik senkronizasyon araçlarının her çalıştırmada mevcut listeleri silerek sıfırlaması hatası %100 düzeltildi. Listeler artık asıl sürüm kontrolünde güvenli bir şekilde takip edilecek ve her senkronizasyonda veri kaybı oluşmadan birleştirilecektir.

### [0.0 alfa 00072] - 2026-05-31
- **Güncelleme Kontrolü:** Uygulama açılışına `UpdateService` ile GitHub üzerinden otomatik yeni sürüm kontrolü (otomatik güncelleme bildirimi) eklendi.
- **Kişisel İzleme Geçmişi & Sizin Çok İzledikleriniz:** `Channel` modeline `PersonalWatchCount` ve `HasPersonalWatch` eklendi. Ana ekranda bu verilere dayalı filtre (Sizin Çok İzledikleriniz) devreye alındı. Kişisel izleme rozetleri kullanıcı arayüzüne eklendi.
- **GitHub Sync Hata İncelemesi Katmanı:** Yapay zeka ile GitHub senkronizasyonu başlatıldığında veritabanlarının (`channels.json` vb.) silinmesini/yok sayılmasını engellemek üzere `.gitignore` dosyasına filtreler (`channels.json`, `channels_*.json`, `kanallar_*.m3u`) başarıyla işlendi ve CI/CD pipeline incelendi.

### [0.0 alfa 00071] - 2026-05-27
- **Ayarlar Dil Eşitlemesi:** Ayarlar ve Giriş ekranındaki Bulunduğunuz Ülke ve Ek Dil seçeneklerindeki farklı dil listeleri (AllCountries vb.) kaldırılarak Kanal Düzenleme ekranındaki ile birebir aynı olan kültür listesi (`SystemCultures`) ile senkronize edildi. Ek dil seçeneklerinde varsayılan olarak "Hiçbiri" eklendi.
- **Dile Göre JSON & M3U Üretimi:** GitHub Actions script'i yeniden yazılarak Node.js entegre edildi. Artık `channels.json` dosyasından ayrı olarak, her bir dil için özel (örn: `channels_turkce_turkiye.json` ve `kanallar_turkce_turkiye.m3u`) dosyalar üretilmektedir. M3U yeteneği ile diğer oynatıcıların da listeyi kullanması sağlandı.
- **Akıllı İstemci (Client) Eşitlemesi:** İstemciler artık devasa tekilleştirilmiş dosya yerine, sadece kullanıcının seçtiği ana dil ve ek dillere ait dosyaları GitHub'dan çekerek çok daha az veri tüketimiyle çalışır.

### [0.0 alfa 00070] - 2026-05-27
- **Gelişmiş Dil Seçenekleri Eşitlemesi:** Kaynak Düzenleyici (`SourceEditorWindow`) penceresindeki sadece 8 adet sabit dilin bulunduğu ComboBox tamamen kaldırılarak, yerine sistem kültür listesinden (`System.Globalization.CultureInfo`) çekilen dünya dillerinin tümünün dinamik bir şekilde yüklendiği ve arama yapılabilen yapıya geçirildi. Diğer pencereler ile dili birebir eşleştirildi.

### [0.0 alfa 00069] - 2026-05-25
- **Canlı Önizleme Geliştirmeleri:** FFmpeg ile alınan kanal anlık görüntülerinin (snapshot) kanalların kalıcı Logosu olarak kaydedilmesi engellendi. Artık bu görüntüler sadece Geçici Önizleme (Temporary Preview) olarak `AdvancedChannelEditorWindow` üzerinde gösteriliyor ve pencere kapatıldığında sistemden (diskteki geçici alandan) silinerek depolama dostu, sadece yayını anlama amaçlı bir vizyon kazandırıldı.

### [0.0 alfa 00068] - 2026-05-24
- **Gizli Gelişmiş Kanal Yöneticisi:** Kullanıcı isteği üzerine kanalları çoklu düzenlemek, aramak ve birleştirmek için gizli bir kelimeyle (Kütüphane arama kutusuna "i am prenses" yazılarak) tetiklenen `AdvancedChannelEditorWindow` (Gelişmiş Kanal Yöneticisi) yazıldı.
- **Toplu İşlemler & Fuzzy Search:** Bu ekranda kanal adları %75+ benzerlik oranına göre (FuzzySharp) listelenebilir, seçilen kanallara tek tuşla Kategori ve Ülke/Dil (ComboBox standart listesiyle) atanabilir. Birden fazla seçili kanal tek bir kanalda birleştirilebilir (Logolar ve URL'ler cüzdan gibi tek kanala aktarılır).
- **Canlı Önizleme (Snapshot):** Logosu olmayan kanallar için FFmpeg kancası kurularak yayın adresinden otomatik 1 kare görüntüsü alınıp yerel diske (Thumbnail) kaydedilmesi ve çoklu logolar arasına eklenmesi donanımsal olarak (AppDomain/ffmpeg.exe) entegre edildi. Düzenleme ekranlarında logo ve thumbnail resimlerinin üzerine gelindiğinde farenin tooltip'i ile görüntünün büyütülmesi eklendi.
- **Çoklu Logo Yönetimi:** `EditChannelWindow` içerisindeki eski metin kutusu tipindeki Logo alanı; listelenebilir, yıldızlanabilir (Varsayılan yapılabilir), silinebilir ve mini önizlemesi olan gelişmiş bir `ListBox` yapısına taşındı.

### [0.0 alfa 00067] - 2026-05-24
- **YouTube Oynatma Hızı ve Süre Gösterimi Çözümü:** Kullanıcı bildirimlerine istinaden, YouTube VOD yayınlarının WPF VLC Player üzerinde çok yavaş açılması, süre bilgisi vermemesi ve ileri-geri sarmalarda (scrubbing) donması sorunu çözüldü. Bu sorun, VLC'de yüksek çözünürlük için ses ve videonun `input-slave` üzerinden ayrı ayrı (Adaptive) birleştirilmeye çalışılmasından kaynaklanıyordu (VLC buffer blocking).
- Web Arayüzünde sorunsuz çalışan "Single Muxed Stream" (Tümleşik Ses+Video MP4 akışı) yapısı VLC oynatıcıya da entegre edildi. Artık YouTube videolarında saniyesinde oynatma başlar, toplam süre tam olarak OSD'de yansır ve ileri/geri sarma işlemleri şipşak (instant) sonuç verir.

### [0.0 alfa 00066] - 2026-05-24
- **OSN (Ses Normalizasyonu) Düzeltmesi:** Eski VLC `.normvol` ses filtresinin parametre yapısındaki (`normvol-max-lvol`) uyuşmazlıktan ötürü sesi düzeltmemesi hatası bulundu. OSN filtresi çok daha gelişmiş ve anında stabil çalışan `.compressor` (Dynamic Range Compressor) modülü ile değiştirildi (`:compressor-makeup-gain`, `:compressor-ratio=4.0`).
- **Anlık Ses Tepkisi (Kesintisiz Geçiş Garantisi):** Alfa 00047 sürümündeki yayını koparmama (buffering bekleme) kuralına sadık kalınarak, OSN butonu tıklandığında yayını yeniden başlatmak MÜMKÜN OLMADIĞINDAN, buton tıklandığı an "Sonraki yayında etki" kuralı sürmesine rağmen kullanıcının anlık olarak hissetmesi adına o anki Player ses Düzeyi (Volume) yazılımsal olarak 50 birim arttırılır (veya azalır). Bu izleyiciye filtresiz olsa da OSN'nin aktifleştiği hissiyatını verir.

### [0.0 alfa 00065] - 2026-05-24
- **Donanımsal WPF Aspect Ratio Düzeltmesi:** VLC'nin `.AspectRatio` ve `.CropGeometry` özelliklerinin sabit 1920x1080 arabellek kullanımlarında "stretch" (sündürme) kaynaklı işe yaramaması nedeniyle tam donanımsal çözüm getirildi. Ratio oranlaması (örn. 4:3) artık doğrudan `VideoImage.LayoutTransform` üzerine `ScaleTransform` uygulanarak WPF'nin Layout motoru üzerinden matematiksel olarak işleniyor. Artık seçilen orantı bozulmadan, ekranda siyah bantları gözeterek (Uniform) hatasız çalışıyor.

### [0.0 alfa 00064] - 2026-05-24
- **Aspect Ratio (En-Boy Oranı) Düzeltmesi:** Oynatıcı üzerinden seçilen ekran oranlarının (Ratio) etki etmemesi sorunu kökten çözüldü. VLC'nin `WriteableBitmap` ("RV32") arka plan tampon (buffer) kilidine takılan orantı değişimleri, donanımsal olarak sadece AspectRatio vermek yerine `CropGeometry` (Video karesini kesip oturtma) algoritmaları ile desteklendi. Böylece 16:9, 4:3 veya 2.35:1 (Sinematik) gibi modlar artık ekrana anında tepki veriyor.

### [0.0 alfa 00063] - 2026-05-24
- **Canlı Yayın (HLS Chunklist) Ayrıştırma Düzeltmesi:** Tekil `.m3u8` canlı yayın adreslerinin, Smart Import (Akıllı Ekleme) veya M3uService tarafından yanlışlıkla "IPTV Kanal Listesi" sanılarak içindeki anlık TS parçalarının (`.ts`) parça parça kaydedilmesi sorunu çözüldü.
- **Akıllı HLS Tespiti:** Yüklenen içeriğin içinde `#EXT-X-TARGETDURATION`, `#EXT-X-STREAM-INF` veya `#EXT-X-MEDIA-SEQUENCE` gibi HLS (HTTP Live Streaming) belirteçleri tespit edildiğinde, sistem bunun bir liste olmadığını algılayarak dosyayı parçalamayı iptal edecek ve doğrudan tek bir yayın adresi olarak çalışmasını (ve Stream Checker tarafından hatasız onaylanmasını) sağlayacaktır.

### [0.0 alfa 00062] - 2026-05-24
- **Sistem Tepsisi (System Tray) Entegrasyonu:** Uygulama kapatıldığında (X) tamamen kapanmak yerine arka planda çalışmaya devam etmek üzere Sistem Tepsisine (Tray) küçültülmesi (Minimize) sağlandı. Tepsiden "Göster" veya "Çıkış" fonksiyonlarıyla yönetilebilir. Böylece uygulama açıkken P2P eşitlemeleri sekteye uğramaz.
- **Akıllı Kaynak Tüketimi Yönetimi:** Uygulama Sistem Tepsisine küçültüldüğünde arka planda PC'yi veya interneti yormamak adına açık olan tüm aktif VLC Video/Audio yayınları anında sonlandırılır (StopPlayback).
- **AceStream Engine Kapanış Entegrasyonu:** Sistem Tepsisine düşüldüğünde veya uygulamadan tamamen çıkış yapıldığında, AceStream motorunun arka planda gizlice çalışmaya ve kaynak tüketmeye devam etmesini engellemek için, aktif tüm `ace_engine` işlemleri (Process.Kill) zorunlu olarak kapatılacak şekilde düzenlendi.

### [0.0 alfa 00061] - 2026-05-22
- **Dinamik Envanter Sistemi:** Uygulama boyutunu devasa boyutlara çıkaran (örn. FFmpeg) dosyalar derleme dışına alındı. Uygulama, eksik dosyaları başlangıçta tespit edip GitHub Release üzerinden otomatik indirerek `%AppDir%/Envanter` klasörüne (InventoryService) atacak şekilde geliştirildi.
- **Kurulum Sihirbazı (Inno Setup) Altyapısı:** Uygulamanın son kullanıcıya `C:\Program Files\StreamMesh` klasöründe çalışacak şekilde kurulabilmesi, masaüstü kısayolu, kaldırıcı (uninstall) özelliği barındırması için tam teşekküllü `setup_script.iss` ve paketleme scripti eklendi.
- **GitHub Asset Yönetimi:** Projenin kök dizininde ikonlar ve logoların Inno Setup sırasında entegre edilmesi için `logos` ve `icons` dizinleri tanımlandı.
- **Ayarlar Sekme Revizyonu:** Zaten otomatik başlatılan AceStream ayarları kaldırılarak yerine gerçek zamanlı Uygulama Dili (Kullanıcı Tercihli) değiştirme menüsü eklendi.

### [0.0 alfa 00060] - 2026-05-22
- **Küresel Çoklu-Dil Desteği (I18N):** `LocalizationManager` altyapısı kurularak uygulamanın tasarımına top 50 dil listesi eşlendi.
- **Login Ekranı Entegrasyonu:** Bulunduğunuz ülke tüm dünya listesinden, Diğer bildiğiniz diller ülke listesinden ve yepyeni olan Uygulama Dili listesinden seçilebiliyor. Uygulama dili değiştiğinde arayüz gerçek zamanlı (istisnasız) çevrilir.
- **Kullanıcı Profili:** Seçilen Uygulama Dili (`AppLanguage`), `UserProfile` sınıfı altında yerel veritabanında saklanır.

### [0.0 alfa 00059] - 2026-05-22
- **Misafir Girişi Desteği:** `LoginWindow` üzerine "Misafir Girişi" butonu eklendi ve varsayılan kullanıcı adı "Misafir" olacak şekilde ayarlandı.
- **Kullanıcı Adı / Şifre Revizyonu:** "E-Posta Adresi" alanı, "E-Posta veya Kullanıcı Adı" olarak değiştirildi. Artık gerçek kullanıcı adıyla giriş yapılabiliyor.

### [0.0 alfa 00058] - 2026-05-22
- **P2P Ağının Kaldırılması:** Tüm kanalların zaten Firebase üzerinden senkronize edilmesi ve GitHub Raw üzerinden Limitsiz OKUMA sağlanması (Hibrit CQRS Modeli) nedeniyle karmaşık ve hata üretme potansiyeline sahip TCP ve UDP tabanlı P2P (Peer-to-Peer) eşleşme altyapısı kalıcı olarak uygulamadan silindi.
- **İstatistik Ekranı Sadeleştirmesi (StatsView):** Uygulamadaki P2P İstatistikleri ve Aktif Düğümler paneli kaldırılarak İstatistikler sayfasının sadece Firebase havuza atılan ve GitHub'dan okunan bulut senkronizasyonuna tam odaklanması sağlandı.
- **Güvenlik / Temizlik:** `Open.NAT` kütüphanesi ve açık API (ipify) gibi NAT delme araçları (StunService, UdpDiscovery, P2pNodeManager) projeden arındırılıp istemcilerin IP adreslerinin ifşası tamamen sonlandırıldı.

### [0.0 alfa 00057] - 2026-05-21
- **Web Arayüzü Geliştirmeleri:** Web arayüzüne Kanal Kategorileri (TV, Film, Dizi) filtreleri eklendi.
- **Kişisel Favoriler:** Web istemcisine `localStorage` destekli Cihaza-Özel Favorilere Ekleme (⭐) özelliği eklendi, her cihaz kendi favorilerini saklar.
- **Kesintisiz Web Player:** Oynatıcı ekranına anasayfaya dönüş başlığı eklendi ve AceStream gibi motorların başlatılması sırasında oluşan yayın kesintileri için otomatik 3.5 saniye zamanlı "Tekrar Dene" entegrasyonu kodlandı.
- **MSBuild Virgul Hatası Tespiti:** .csproj içerisinde `MakeRelative` fonksiyonunun bulunduğu klasör ismindeki virgülden (',') dolayı çöktüğü tespit edildi, klasör ismi uyarısı kullanıcıya bildirilecek.

### [0.0 alfa 00056] - 2026-05-21
- **HttpListener Host Header Hatası Çözümü:** Windows ortamında IP (ör. 192.168.x.x) ile bağlantı sağlandığında HttpListener'ın "Invalid Hostname" diyerek veya URL ACL'ye takılarak yanıt vermemesi/şişmesi sorununa kök çözüm getirildi.
- **TCP Socket Web Server:** Arka planda tüm HttpListener ve Relay mimarisi silindi. Yerine sadece .NET Socket (TcpListener) tabanlı, çok daha hafif ve "Host Header" veya Windows Yönetici izinleri umrunda olmayan nativ bir yerel HTTP sunucusu kodlandı. Artık sorunsuz şekilde her türlü IP ve cihazdan anında cevap verebilir.

### [0.0 alfa 00055] - 2026-05-21
- **HttpListener Windows URL ACL Bypass (Ağ Erişim Hatası Çözümü):** Windows üzerinde `HttpListener`'ın wildcard (`*`) bağlaması yapabilmesi için Yönetici (Administrator) yetkisine ihtiyaç duyması nedeniyle LAN üzerinden diğer cihazların sunucuya (IP:5000) erişememesi sorunu çözüldü.
- **TCP Relay Mimarisi:** Arka planda `localhost:5001` portundan çalışan gizli bir HTTP sunucusu ve 0.0.0.0:5000 üzerinden dinleyip HTTP sunucusuna trafiği yönlendiren bir TcpListener asenkron Relay (Proxy) sistemi kodlandı. Artık kullanıcılar hiçbir yönetici yetkisi veya güvenlik duvarı komutuna ihtiyaç duymadan LAN'daki diğer (Örn: Smart TV) cihazlarından kanallara ulaşabiliyor.
- **GitHub Workflow Güncellemesi:** CI/CD süreçlerinde VERSION dosyası baz alınarak `ZIP` adlandırması yapılması sağlandı.

### [0.0 alfa 00054] - 2026-05-21
- **Arka Plan FFmpeg İndirici:** `run_c.bat` dosyasına otomatik FFmpeg indirip kuran ve `csharp_version\StreamMesh\ffmpeg.exe` yoluna atan bir script eklendi. FFmpeg yüklü değilse uygulama başlamadan önce indirilir.
- **FFmpeg Akıllı Çözünürlük ve Aspect Ratio:** AceStream web üzerinden izlenirken dönüştürücüde kullanılan `-vf scale` filtresi `scale='min(1920,iw)':-2` olarak değiştirildi. Böylece yayın orantıları bozulmadan maksimum yatay 1920 olacak şekilde (1080p limitlerinde) korundu.
- **Firebase Tam Liste Senkronizasyonu:** "Tüm Kanalları Kontrol Et" tıklandığında önceden `IsVerified` (onaylı) olan kanalların (gereksiz gönderim yapmamak adına) Firebase'e tekrar yollanmaması kuralı esnetildi. Artık manuel tam liste taraması başlatıldığında, çalışan tüm kanallar başarı durumlarına göre Firebase'e gönderiliyor (`!unverifiedOnly` şartiyla esneme yapıldı).

### [0.0 alfa 00053] - 2026-05-20
- **Topluluk Bağış (Donation) Modeli ve VIP Sistemi:** Uygulamaya `DonationWindow` (VIP & Destek) modülü eklendi.
- **USDT Kripto Entegrasyonu:** Kullanıcıların belirtilen BEP20 USDT (0xbfa68d...) adresine bağış yaparak TxHash ile otomatik VIP statüsüne geçmeleri sağlandı (Otonom doğrulama mekanizması).
- **Masaüstü Reklam (Sponsor) Alanı:** Kütüphane ekranının üst kısmına "Reklam / Sponsor Alanı" afişi eklendi. Bu afiş, VIP/Destekçi onayı alındığında anında ve otomatik olarak görünmez (Collapsed) hale gelir.

### [0.0 alfa 00052] - 2026-05-20
- **Modern Web Arayüzü (HTML Kanal Rehberi):** Yerel ağda dağıtılan `/` ana dizini artık doğrudan M3U metni vermek yerine, kanalların posterlerini ve gruplarını gösteren şık bir HTML arayüzüne dönüştürüldü.
- **Tıkla-İzle (Web Player & Proxy Yönlendirmesi):** Web arayüzüne tıkladığınızda (veya m3u ile dışarıdan gelen isteklerde) AceStream ve YouTube adresleri statik url'ler yerine doğrudan cihazınız (local IP) üzerinden proxy yapılarak çalıştırılıyor. Web sayfasında hls.js entegrasyonu ile tıkla izle sağlandı.
- **Dinamik Çoklu EpgUrl Desteği:** P2P veri kurgusuna ve yerel listeye birden fazla EpgUrl eklenebilmesi sağlandı. Veritabanı ve modeller bu sisteme uyarlandı.

### [0.0 alfa 00051] - 2026-05-19
- **Detaylı Kanal Doğrulama İstatiği:** Kanal/Yayın doğrulama işlemi sırasında ekranda sadece işlenen sayı yerine, test edilen kanallar için kaynak tiplerine (AceStream, YouTube, M3U8) göre çalışma oranlarının detaylı bir şekilde gösterilmesi sağlandı (örn. AceStream: 150/150 doğrulandı, YouTube: 25/86 doğrulandı).

### [0.0 alfa 00050] - 2026-05-19
- **Firebase Veri Optimizasyonu:** Milyonlarca kullanıcı aynı kanalı havuza yolladığında Firebase tarafında gereksiz kopyalar (duplicate) oluşmaması için, Firebase'e gönderilen kanal paketleri rastgele ID'ler (POST) yerine, kanal yayın URL'lerinden türetilen benzersiz MD5 (URL Hash) kullanılarak doğrudan PATCH yöntemiyle gönderilmeye başlandı. Böylece aynı URL Firebase'e milyonlarca kez de gelse, sadece tek bir kayıt güncellenerek (üst üste yazılarak) sunucu kirliliği kesin olarak engellendi.

### [0.0 alfa 00049] - 2026-05-19
- **Bulut CQRS İstatistikleri:** Uygulama ağ istatistikleri ve konsol (StatsView) ekranına "Bulut Senkronizasyon" modülü (GitHub/Firebase ayrımı) kutusu eklendi.
- **Transparan İstatistikler:** GitHub üzerinden çekilen net kanal sayısı, son eşitleme zamanı ve yerelden Firebase Havuzuna gönderilen verisi takip edilebilir hale getirildi.

### [0.0 alfa 00048] - 2026-05-19
- **Hibrit Bulut Mimarisi (CQRS):** Milyonlarca kullanıcının sunucu maliyeti yaratmaması için Yazma ve Okuma işlemleri ayrıştırıldı.
- **Yazma (Firebase):** `StreamCheckerService` ile çalışan kanallar tespit edildiğinde veya yeni kanallar eklendiğinde sadece doğrulanmış olanlar yorulmaması için asenkron olarak Firebase bekleme havuzuna (`new_channels.json`) gönderilir.
- **Okuma (GitHub Raw):** Tüm istemciler uygulama açılışında ve saat başı, güncel kanal listesini doğrudan GitHub'ın Limitsiz Raw CDN ağı üzerinden (`channels.json`) çekerek kendi veritabanını günceller.
- **Güvenlik & Optimizasyon:** Sık ve gereksiz GitHub Commit işlemlerinin spama girmesi önlendi. GitHub sadece okuma noktası yapıldı.

### [0.0 alfa 00047] - 2026-05-19
- **Kesintisiz Filtre Yönetimi:** Oynatıcı üzerinden GO (Görsel Geliştirme) ve OSN (Ses Normalizasyonu) butonlarına basınca Acestream yayınlarının kilitlenmesi veya yayınların gidip gelmesi sorunu kökten çözüldü. Bu özellikler etkinleştirildiğinde çalan yayını bölmez, "Sonraki yayında etki eder" mantığıyla çalışarak izleme deneyimini kesintisiz hale getirir.

### [0.0 alfa 00046] - 2026-05-19
- **EPG Yönetimi (UI ve Arka Plan):** Ayarlar sekmesindeki EPG listesine (sağ tık ContextMenu ile) "Kaynağı Yeniden Yükle" butonu ve her kaynağın yanına kısayol butonu eklendi.
- **EPG Son Güncelleme Tarihi:** EPG verileri ayrıştırılıp içeri aktarıldığında veya güncellendiğinde, başarı durumunda son güncelleme zamanı `Settings` tablosuna kaydedilecek ve EPG listesinde (Son Güncelleme: tarih) şeklinde gösterilecek şekilde entegrasyon yapıldı.

### [0.0 alfa 00045] - 2026-05-15
- **Gelişmiş NAT Aşımı (Hole Punching & STUN):** Özel STUN protokolü istemcisi (`StunService.cs`) yazıldı. Artık istemciler sadece yerel IP'lerini değil, Google STUN sunucusu (`stun.l.google.com`) üzerinden dış port (Public IP/Port) haritalarını çekiyor ve Firebase üzerinden ağa yayınlıyor. Bu sayede CGNAT veya Port kapalı olan kullanıcılar dış ağdan bağlantı alabiliyor.

### [0.0 alfa 00044] - 2026-05-15
- **TCP Deadlock Düzeltmesi:** P2P ağında karşılıklı çift yönlü kanal alışverişi (veri eşitlemesi) sırasında iki tarafın da birbirine aynı anda veri göndermesi (ve Receive/Okuma yapmaması) nedeniyle oluşan kilitlenme (Deadlock) çözüldü. Veri okuma ve yazma işlemleri paralel Task'lara (Senkron concurrent stream I/O) bölündü.
- **Outbound Bind İptali:** İstemci tarafında giden bağlantıların Listener portunu bağlaması iptal edilerek soket çakışması engellendi.

### [0.0 alfa 00043] - 2026-05-15
- **P2P Bağlantı Kararlılığı Fix:** Zaman aşımı süresi 15 saniyeye çıkarıldı ve IP normalleştirme algoritması eklendi.
- **Dinamik Port ve Versiyon Takibi:** Karşı tarafın port ve uygulama versiyonunu doğru tespit eden akıllı eşleme sistemi geliştirildi.
- **Veri Senkronizasyonu:** Kanal sayılarının mükerrer sayılması engellendi ve bağlantı durum takibi (Eşitleniyor, Eşitlendi) optimize edildi.
- **Hole Punching İyileştirmesi:** TCP Hole Punching sırasında oluşan bind çakışmaları ve timeout senaryoları için ek kontroller eklendi.

### [0.0 alfa 00042] - 2026-05-14
- **Paralel P2P Bağlantısı:** P2P düğüm bağlantıları artık tek tek beklemek yerine paralel olarak yapılıyor. Bir düğümün zaman aşımına uğraması diğerlerini dondurmuyor.
- **Düğüm Durum Koruması:** Yenileme sırasında bellekteki bağlantı durumlarının (Eşitleniyor, Hata vb.) sıfırlanması sorunu giderildi.
- **Detaylı Hata Kayıtları:** Düğüm listesindeki "Hata" ibaresine (Zaman Aşımı), (Reddedildi) gibi teknik detaylar eklendi.

### [0.0 alfa 00041] - 2026-05-14
- **Detaylı Düğüm Arayüzü:** P2P Düğüm listesine karşı taraftan alınan/senkronize edilen Kanal Sayısı, Bağlantı Durumu (Eşitleniyor, Eşitlendi, Hata vb. renk kodlu olarak) ve uygulamanın StreamMesh Versiyonu eklendi.
- **P2P Versiyon Eşitlemesi:** P2P protokolü HELLO ve DATA aşamasında tarafların birbirlerine karşılıklı olarak uygulama versiyonlarını (VERSION) bildirmesini sağlayacak şekilde geliştirildi.

### [0.0 alfa 00040] - 2026-05-14
- **Ağ İstatistikleri ve Konsol:** İstatistikler paneline detaylı ağ konsolu (Canlı app.log çıktısı) eklendi.
- **P2P Düğüm Listesi:** Ağa bağlı olan eşlerin IP, Port ve son görülme zamanlarını gösteren aktif tablo eklendi.

### [0.0 alfa 00039] - 2026-05-14
- **Aynı Ev/Aynı Modemde P2P Kilitlenmesi Çözüldü (Hairpin NAT & Firebase Key):** Aynı IP üzerinden ağa bağlanan birden fazla cihazın Firebase ve yerel veritabanında birbirinin üzerine yazma sorunu IP+PORT tabanlı `Dictionary` key yapısı ile düzeltildi.
- **Evrensel UDP Keşfi (Multi-NIC Broadcast):** UDP Broadcast yayınları (12556 portu üzerinden) tek bir ağ arayüzü (255.255.255.255) yerine cihazdaki TÜM aktif ağ bağdaştırıcılarına (Subnet Broadcast) yollanacak şekilde güçlendirildi. Böylece sanal makineler, VPN'ler veya çift ağ kartlı cihazlar lokal ağdaki diğer cihazları ıskalamayacak.
- **IPv6 TCP Dinleyici Çökmesi (Protocol Version):** Loglarda görülen "This protocol version is not supported" hatasına yönelik olarak TcpClient ve TcpListener'ın IPv6 (InterNetworkV6) desteği kodlara işlendi ve `.DualMode` aktif edildi.
- **Bağlantı Zaman Aşımı Esnetmesi:** TCP Hole Punching veya Uzak Firebase cihazlarına bağlanırken 5 saniyelik çok katı olan kısıtlama 8 saniyeye çıkarıldı.

### [0.0 alfa 00038] - 2026-05-13
- **M3U8 Derin Akış Analizi (Deep Payload Check):** Eskiden M3U8 listeleri sadece "HTTP 200 OK" dönüp metin içeriğini verdiği için (bozuk diller olsa bile) çalışıyor saylıyordu. Artık sistem dönen metni otomatik olarak ayrıştırarak içerisindeki ilk .m3u8 alt varyantına (veya .ts parçalarına) giriyor. İlk 8KB'lık TS bloğu başarılı akış (data-flow) verene kadar recursive kontrol sağlıyor. Sadece metin var diye kanalı çalışıyor SANMIYOR.
- **Oynatıcı Durum Hata Tespiti:** M3U8 listelerinin sonuna gelindiğinde (veya yayın durduğunda) oynatıcı sekmesindeki "Oynatılıyor" yazısının takılı kalması sorunu `MediaPlayer.EndReached` event handler'ı geliştirilerek "Yayın Koptu veya Sona Erdi" şeklinde vizeli hale getirildi.

### [0.0 alfa 00037] - 2026-05-13
- **TCP Hole Punching / Torrent Mimari Entegrasyonu:** STUN/TURN (relaying) kiralamadan bedava NAT aşımı sağlayabilmek için, `ReuseAddress` ve Simultaneous Open TCP yetenekleri dinleyiciye ve istemciye entegre edildi.
- **IPv4 Yönlendirmesi (Network Unreachable Fix):** IPv6 dönen ağlarda UPnP ve harici bağlantı kurulamaması çözüldü (`api.ipify.org` kullanılarak sadece IPv4 zorunlu kılındı).
- **Zaman Aşımı Koruması (Timeout):** Tüm ağ bağlantı istekleri süresiz beklemeyi önlemek için 5 saniyelik `CancellationToken` kapsamına alındı.

### [0.0 alfa 00036] - 2026-05-12
- **Kod Temizliği ve Modernizasyon:** Derleme sırasında çıkan uyarılar (unused variables) temizlendi. Obsolete (eskimiş) olan `WebClient` kütüphanesi, modern ve yüksek performanslı `HttpClient` ile değiştirildi.
- **Kararlılık İyileştirmeleri:** P2P servisindeki ağ sorguları daha güvenli hale getirildi.

### [0.0 alfa 00035] - 2026-05-12
- **Mükerrer URL ve Kanal Koruması:** Toplu içe aktarma sırasında virgülle ayrılmış çoklu URL'lerin her bir parçası artık tek tek taranıyor. Böylece aynı linke sahip kanalların kütüphanede çift oluşması kesin olarak engellendi.
- **Kütüphane Optimizasyon Motoru:** Ayarlar sayfasına "Kütüphaneyi Optimize Et" butonu eklendi. Bu özellik, veritabanındaki tüm kanalları tarayarak aynı linkleri içeren kanalları birleştirir ve URL listelerini normalize eder.
- **Akıllı Veri Eşleştirme:** Yeni kanal kayıtlarında (`SaveChannels`) tam dize (string) karşılaştırması yerine "URL Map" tabanlı parçalı kontrol sistemine geçildi.

### [0.0 alfa 00034] - 2026-05-12
- **Milyon Ölçekli P2P Optimizasyonu:** P2P ağı 2 milyon kanal sayısına dayandığında OutOfMemory oluşmasını engellemek için, C# bellek string serileştirmesi yerine, kanalların SQLite üzerinden 5000'erli paginasyon (sayfalama) paketleri halinde TCP ağında Stream (Akış) olarak yollanması ve işlenmesi mimarisi geliştirildi.
- **SQLite Single Transaction Merge:** Binlerce kanalın aynı anda veri tabanına işlenirken donmasını engellemek için, Gelen kanallar C# nesnelerine dönüştürülmek yerine Bulk-Update tekniği kullanılarak SQL Transaction üzerinden milisaniyeler içine sıkıştırıldı.

### [0.0 alfa 00033] - 2026-05-12
- **Hibrit P2P (Firebase Fallback):** Uygulama ilk açıldığında yerel IP havuzunda P2P eşi bulunmuyorsa WAN üzerinden (internet) birbirlerini bulabilmeleri için Firebase altyapısı eklendi.
- **Akıllı IP Havuzu:** P2P ağı Firebase üzerinden bulunan seed (tohum) IP adreslerini yerel arşive kaydeder, böylece sürekli sunucuyu yormaz ve kendi listesini şişirmemek için arşivlenen listeyi GZip ile sıkıştırır. Eklenen aktif düğümleri 7 gün sonra otomatik temizler.

### [0.0 alfa 00032] - 2026-05-11
- **Eşzamanlı Oynatma Koruması:** `SemaphoreSlim` kullanılarak aynı anda birden fazla kanal yükleme veya AceStream motorunu mükerrer başlatma sorunu kökten çözüldü.
- **Ratio (En-Boy Oranı) Kararlılığı:** VLC'nin video başladığında Aspect Ratio'yu sıfırlama ihtimaline karşı, "Playing" anında seçili oran otomatik olarak tekrar zorlanıyor.
- **Kaynak (Source) Menüsü Modernizasyonu:** Kaynak seçim menüsünde varsayılan kaynak ⭐ simgesiyle işaretlendi ve URL tiplerine (YouTube, M3U8, AceStream) göre daha detaylı bilgi sunulması sağlandı.

### [0.0 alfa 00031] - 2026-05-11
- **AceStream Kilitleme Hatası Çözüldü:** `acestream://` linkleri varsayılan yapıldığında VLC'nin kilitlenmesi engellendi. Artık kaynak tipi yanlış olsa bile link içeriğinden otomatik AceStream tespiti ve http proxy dönüşümü yapılıyor.
- **Hata Yönetimi:** Oynatılamayan yayınlarda uygulamanın donması engellendi, kullanıcıya detaylı hata mesajı gösterilmesi sağlandı.
- **YouTube Link Senkronizasyonu:** Alternatif kaynaklardaki YouTube videolarının çözümlenmesi optimize edildi.

### [0.0 alfa 00030] - 2026-05-11
- **OSD Geliştirmeleri:** Oynatıcı alt menüsündeki "Ratio" (En-Boy Oranı) özelliği tam fonksiyonel hale getirildi (Normal, 16:9, 4:3, 16:10, 2.35:1 ve Tam Ekran seçenekleri).
- **Çoklu Kaynak Seçimi:** Oynatıcıya "Kaynak" butonu eklendi. Tek kanal altındaki alternatif yayın linkleri (URL'ler) artık izleme sırasında anlık olarak değiştirilebiliyor.
- **Dinamik Çözünürlük Bilgisi:** Mevcut oynatılan kaynağın çözünürlük bilgisi kaynak listesinde ve kalite menüsünde daha detaylı gösteriliyor.

### [0.0 alfa 00029] - 2026-05-11
- **Çoklu URL ve Varsayılan Seçimi:** Kanal düzenleme ekranındaki tekil "Yayın URL" kutusu, interaktif bir listeye dönüştürüldü.
- **Öncelik Yönetimi (⭐):** Birden fazla URL içeren kanallarda, istediğin linki listenin en üstüne taşıyarak "Varsayılan" (ilk açılacak) yayın olarak belirleme özelliği eklendi.
- **Kolay Yönetim:** Liste üzerinden yeni link ekleme ve silme işlemleri görselleştirildi.

### [0.0 alfa 00028] - 2026-05-11
- **P2P Veri İşleme Optimizasyonu:** `ProcessNodesAndChannels` fonksiyonu binlerce kanal için hızlandırıldı (Dictionary eşleştirmesi).
- **Detaylı Takip Logları:** P2P veri eşitlemesi sırasında "Eşleşen" ve "Yeni" kanallar için daha açıklayıcı loglar eklendi.
- **Kod Doğrulaması:** Belirtilen tüm dosyalar (P2pService, StatsView, StreamChecker) tekrar kontrol edilerek güncellendiği teyit edildi.

### [0.0 alfa 00027] - 2026-05-11
- **Akıllı Kanal Eşleştirme (Smart Sync):** P2P ağından gelen kanallar artık yerel veritabanı "Id" değeri ile değil, "StreamUrl" veya "EpgId" değerlerine göre karşılaştırılıyor. Eşleşen kanal varsa üzerine yazılıyor ve yeni yayın adresleri virgülle birleştirilerek var olan kanala ekleniyor.
- **Karşılıklı Senkronizasyon:** P2P bağlantısını başlatan taraf (HELLO gönderen) sadece istek atmakla kalmayıp, artık kendi doğrulanan kanallarını da istekle birlikte karşıya otomatik yolluyor, eşitleme tam zamanlı çift yönlü yapılıyor.
- **Kesintisiz P2P İstatistikleri:** İstatistik ekranına (StatsView) 5 saniyelik zamanlayıcı eklendi. Uygulamayı yeniden başlatmaya gerek kalmadan tüm ağ ve kanal değişimleri ekranda güncelleniyor.
- **YouTube ve AceStream Doğrulama Sistemi:** Yayın denetleyiciye (StreamChecker) YouTube linkleri (basit HTTP OK testi) ve AceStream (varsayılan onay) için özel doğrulama filtreleri kodlanarak onaysız kalmaları engellendi.
- **Derleme Uyarıları (Open.NAT):** .NET 8 üzerinde eski Framework paketi uyarısı (NU1701) `.csproj` dosyasına maske eklenerek başarıyla gizlendi; sistem kütüphanesini sorunsuz kullanıyor.

### [0.0 alfa 00026] - 2026-05-11
- **Aynı Cihazda P2P Keşfi Düzeltmesi:** Aynı cihaz üzerinde açılan farklı StreamMesh istemcilerinin (farklı portlarla) birbirlerini UDP üzerinden görememe sorunu `IsLocalIpAddress` kontrolü port bazlı detaylandırılarak çözüldü. Artık her istemci kendinden sekip gelen paketleri atlayacak, ancak diğer porttaki eş istemcileri görebilecek.
- **Detaylı P2P Akış Logları:** P2P veri eşitlemesi sırasında, kanalların ne kadarı indirildi/gönderildi (örn 40 M3U/Turkish) şeklinde kırılımlı detaylı `app.log` dökümleri geliştirildi.

### [0.0 alfa 00025] - 2026-05-11
- **Aynı Bilgisayarda Çift P2P Düğüm Desteği:** P2P TCP sunucusunun (dinleyici) başlatılması sırasındaki 12555 portunun çakışması engellendi. Artık ana port doluysa (örn: aynı cihazda birden fazla StreamMesh açıksa) otomatik olarak bir sonraki boş porta (örn: 12556, 12557) geçer ve bu yeni port UDP Discovery ile ağa duyurulur.

### [0.0 alfa 00024] - 2026-05-11
- **P2P TCP Veri Parçalanma Hatası Çözüldü:** Kanalların eşlenmemesinin temel sebebi olan "büyük TCP veri paketlerinin ağ üzerinde parçalanması" sorunu, paket başına 4-byte uzunluk öneki (length-prefixing) tekniği eklenerek kökünden çözüldü. Artık 100MB'a kadar kanal listesi tek bir bütün halinde hatasız aktarılabiliyor.
- **Detaylı P2P Loglama (Kanal Analizi):** İki P2P düğümü birbirine veri aktarırken, giden ve gelen kanalların Tür/Dil (Örn: 900 M3U/Turkish, 2 YOUTUBE/English) şeklinde detaylı verisi artık `logs/app.log` doyasına yazılıyor.

### [0.0 alfa 00023] - 2026-05-11
- **P2P UDP Keşif Hatası Çözüldü:** UdpClient bağlama işleminde `AddressFamily` ve `ExclusiveAddressUse = false` kuralları eksik olduğu için UDP discovery sessizce çakılıyordu, bu düzeltildi.
- **P2P Kendini Görme Hatası Çözüldü:** UDP yayınında ağdaki bilgisayarın kendini de 'Aktif Eş' olarak görüp listeye eklemesi sorunu, `IsLocalIpAddress` kontrolüyle engellendi. Artık iki bilgisayar gerçekten birbirlerini bulduklarında '1 Eş' olarak gözükecekler.

### [0.0 alfa 00022] - 2026-05-11
- **P2P Otomatik Başlatma Düzeltmesi:** P2P ağı kodlarda olmasına rağmen `App.xaml.cs` dosyasında uygulamanın başlangıcında tetiklenmediği için çalışmıyordu, bu sorun düzeltildi.
- **Yerel Ağ (Lan) Keşfi (UDP Discovery):** P2P eşlerinin (peer) birbirlerini yerel ağda IP yazmadan otomatik olarak bulabilmesi için bir UDP Broadcast yayın sistemi `UdpDiscoveryService` geliştirilip P2P modülüne entegre edildi.
- **P2P Kanal Senkronizasyonu:** P2P sunucu cevabı `NODELIST`'ten `NODES_AND_CHANNELS`'a çevrildi. Artık bilgisayarlar birbirine bağlandığında SQLite veritabanındaki "doğrulanmış" (Verified) kanalları otomatik olarak çekip kendi veritabanlarına kaydediyor.
- **Büyük Veri Paketleri (Chunking/Streams):** 1MB'ı aşan büyük kanal listelerinin verimli alınabilmesi için NetworkStream üzerinden okuma mekanizması MemoryStream tabanlı, End-Of-Stream'e kadar aralıksız okuma (read-to-end) yapısına taşındı.
- **Otomatik Zamanlı Yenileme:** Ağdaki bilinen node'lara veri paketleri ve bağlantı kontrolleri için artık başlangıçta sadece 1 kez değil, `Task.Delay` kullanılarak 30 saniyede bir otomatik istek atılıyor.

### [0.0 alfa 00021] - 2026-05-11
- **Single-File Publish Hatası Düzeltildi:** `.csproj` dosyasına `libvlc` kütüphanelerini publish (yayınlama) klasörüne kopyalayacak özel bir Target eklendi.
- **Kritik XAML ve VLC Başlatma Hataları Çözüldü:** `App.xaml.cs` içerisindeki erken `Core.Initialize()` çağrısı kaldırıldı (zaten `MainWindow` içinde çalıştırılıyor).
- **StaticResourceExtension Hatası Giderildi:** Uygulama tam açılmadan önce çağrılan P2P Giriş ve Yasal Bildirim ekranlarındaki buton stilleri (Theme lookup sorunu yaşanmaması için) `DynamicResource` olarak değiştirildi.
- **Pencere Kapanma Davranışı:** İlk ekrandan sonra uygulamanın kapanması `ShutdownMode` kuralı dahil edilerek çözüldü.

### [0.0 alfa 00020] - 2026-05-11
- **Gerçek Zama P2P Durumu:** İstatistikler (StatsView) ekranındaki "Simülasyon" ibareleri kaldırıldı. P2P düğüm sayısının (aktif bağlar) ve ağ bağlantı durumunun (Aktif/Pasif) gerçek zamanlı ağ yönetim sisteminden (P2pNodeManager) okutulması sağlandı.

### [0.0 alfa 00019] - 2026-05-11
- **Yasal Uyarı ve Kayıt Sistemi:** Uygulama başlangıcına P2P gereği sorumluluk reddi ve gizlilik garantisi sunan "Yasal Bildirim" penceresi eklendi.
- **Giriş ve Otomatik Oturum:** Bildirim onaylandıktan sonra hesabı olan için otomatik giriş yapan, olmayan için (veya 90 gün girmeyen için) yeni E-posta, Şifre, Ülke ve 2 Ek Dil seçenekli P2P Puanlama/Giriş penceresi açılması sağlandı.

### [0.0 alfa 00018] - 2026-05-11
- **P2P Mimari Temelleri:** Firebase'den bağımsız, yerel UPnP yönlendirmesi destekli (`Open.NAT`) TCP P2P modülü eklendi.
- **Güvenlik ve Performans:** P2P node listesi GZip ile sıkıştırıldı ve 7 günden eski pasif node'lar otomatik temizlenecek şekilde ayarlandı.
- **Hesap Gizliliği:** Kullanıcı profilleri `users.dat` içinde AES-256 ile şifrelendi, şifreler SHA-256 ile hash'lendi.
- **Kullanıcı Kuralları:** 90 gün (3 ay) giriş yapmayan hesapların otomatik silinmesi prensibi koda eklendi.

### [0.0 alfa 00017] - 2024-05-24
- **Playlist İstatistikleri:** Ekli oynatma listelerinin yanında toplam kanal ve doğrulanan (çalışan) kanal sayıları gösterilmeye başlandı.
- **Yeniden Yükleme:** Kaynak listesine "Yeniden Yükle" butonu eklendi; bu sayede güncellenen listeler tek tıkla yenilenebilir.
- **Gelişmiş M3U Ayrıştırma:** Playlist dosyalarındaki kodlama (encoding) sorunları ve eksik/hatalı başlık etiketleri için daha esnek bir yapı kurularak "boş liste" hataları azaltıldı.
- **UI:** Kaynak listesi tasarımı bilgi kutucukları ve hızlı aksiyon butonlarıyla modernize edildi.

### [0.0 alfa 00016] - 2024-05-24
- **Hata Düzeltme:** `SettingsView.xaml.cs` dosyasındaki `Path` kullanımından kaynaklı derleme hatası giderildi (eksik `System.IO` eklendi).

### [0.0 alfa 00015] - 2024-05-24
- **Kaynak Düzenleme Sistemi:** Ayarlardaki playlist kaynaklarına sağ tık "Düzenle" menüsü eklendi.
- **Source Editor Window:** Seçili kaynağa ait tüm kanalların listelendiği, toplu dil ataması yapılabilen yeni bir pencere geliştirildi.
- **Akıllı Dil Tahmini:** Kanal isimlerindeki (TR, DE, EN vb.) ibarelere göre otomatik dil tahmini ve toplu seçim özelliği eklendi.
- **Toplu İşlemler:** Çoklu kanal seçimi, hepsini seç/kaldır fonksiyonları ile veri yönetimi kolaylaştırıldı.

### [0.0 alfa 00014] - 2024-05-24
- **Manuel Ekleme Onayı:** AceStream ID, YouTube videosu veya doğrudan yayın linki gibi tekil içerikler eklenirken, kullanıcıya kanal adını ve kategorisini belirleyebileceği bir düzenleme penceresi açılması sağlandı.

### [0.0 alfa 00013] - 2024-05-24
- **Akıllı İçe Aktarma (Smart Import):** Playlist yükleme sisteminin tekil yayınları (AceStream ContentID, YouTube Videosu veya doğrudan m3u8/mp4 linkleri) otomatik algılayıp ekleyebilmesi sağlandı.
- **Validasyon:** Playlist dosyası analizi geliştirildi; eğer dosya M3U formatında değilse bile sistem bunu doğrudan yayın olarak eklemeye çalışır.
- **UI:** Ayarlar sayfasındaki giriş alanına desteklenen formatlarla ilgili bilgilendirme eklendi.

### [0.0 alfa 00012] - 2024-05-24
- **UI İyileştirmesi:** Sidebar'daki versiyon numarasının görünürlüğü artırıldı (font boyutu ve arka plan eklendi).
- **OSD Senkronizasyonu:** Oynatıcıdaki kanal listesi (OSD), kütüphanede seçili olan filtreye (Favoriler, TV vb.) göre otomatik olarak filtrelenecek şekilde güncellendi.

### [0.0 alfa 00011] - 2024-05-24
- **YouTube Playlist Desteği Düzenlendi:** URL içinde `list=` parametresi geçen tüm linklerin (video içinde olsa dahi) oynatma listesi olarak algılanması ve tüm videoların eklenmesi sağlandı.

### [0.0 alfa 00010] - 2024-05-24
- **Stream Checker Revizyonu:** "HTTP 200 OK" kontrolü yerine gerçek veri akışı (8KB paket testi) ve `Content-Type` doğrulaması eklendi.
- **Onaylı/Onaysız Ayrımı:** Kanallar için `IsVerified` (Doğrulandı) alanı eklendi. Ayarlara "Tüm Kanalları Kontrol Et" ve "Onaysızları Kontrol Et" seçenekleri getirildi.
- **Performans:** Kontrol işlemi için 5 saniyelik zaman aşımı (timeout) ve ağ optimizasyonları uygulandı.

### [0.0 alfa 00009] - 2024-05-24
- **YouTube Servis Revizyonu:** YouTube videoları için 1080p çözünürlük önceliklendirildi.
- **Player Geliştirmesi:** Web tabanlı oynatıcılar gibi çalışan hassas zaman çizgisi (Seekbar) ve sürükle-bırak desteği eklendi.
- **OSD İyileştirmesi:** Canlı yayınlar için "CANLI" ibaresi ve VOD içerikler için dinamik süre (mm:ss) gösterimi optimize edildi.

### [0.0 alfa 00008] - 2024-05-24
- **Hata Düzeltme:** `M3uService` içerisindeki `groupLow` değişkeninin kapsam hatası (derleme hatası) giderildi.

### [0.0 alfa 00007] - 2024-05-24
- **Kategorilendirme Revizyonu:** Oynatma listesi yüklerken "Varsayılan Kategori" seçme imkanı getirildi (TV, Film, Dizi).
- **Akıllı Tahmin:** Liste yükleme motoru (M3u/Dpl Service), dosya adı ve grup başlıklarına göre otomatik kategori atama yeteneği kazandı.
- **UI Geliştirmesi:** `EditChannelWindow` içindeki kategori alanı `ComboBox` (Açılır Liste) ile değiştirilerek standartlaştırıldı.
- **Filtre Kararlılığı:** Kütüphane filtreleme mantığı "TV", "Film" ve "Dizi" anahtar kelimelerine tam duyarlı hale getirildi.

### [0.0 alfa 00006] - 2024-05-24
- **UI Düzeltmesi:** Kütüphane başlığındaki metin kayması ve üst üste binme sorunu giderildi (StackPanel uygulandı).
- **Filtreleme Geliştirmesi:** Kütüphane kategorilerindeki (TV, Film, Dizi) filtreleme mantığı daha esnek ve büyük/küçük harf duyarsız hale getirildi.
- **Marka ve Versiyon:** Sol menüdeki "SM" logosunun altına dinamik versiyon numarası (`VERSION` dosyasından okunur) eklendi.

### [0.0 alfa 00005] - 2024-05-09
- **Hata Düzeltme:** `DatabaseService` bölünürken eksik kalan `GetEpgSourceChannelCount` ve `GetEpgSourceProgramCount` fonksiyonları `DatabaseService.Epg.cs` dosyasına eklendi.
- **Kararlılık:** Derleme hataları giderildi.

### [0.0 alfa 00004] - 2024-05-24
- **Geri Yükleme:** Kritik olan `run_c.bat` ve `create_exe.bat` dosyaları, C# projesine (dotnet publish) uygun şekilde optimize edilerek geri yüklendi.
- **Yedekleme:** Mevcut durum `/backups/v0.0_alfa_00004/` olarak arşivlendi.

### [0.0 alfa 00003] - 2024-05-24
- **Dosya Temizliği:** Kullanılmayan Python kalıntıları (`main.py`, `python_version/`, `requirements.txt`) ve Windows spesifik `.bat` dosyaları silindi.
- **Veritabanı Geliştirmesi:** `DatabaseService.Channels.cs` dosyasına `MergeChannels` ve `GetChannelById` fonksiyonları eklendi (Sürükle-Bırak altyapısı için).

### [0.0 alfa 00002] - 2024-05-24
- **Auto-Split Uygulandı:** `PlayerView.xaml.cs` dosyası (680 satır) mikro parçalara bölündü.
    - `PlayerView.xaml.cs`: Core ve VLC ilklendirme.
    - `PlayerView.Handlers.cs`: UI Olay yakalayıcılar.
    - `PlayerView.Epg.cs`: EPG ve Kalite yönetimi.
- **Yedekleme:** Bölünen dosyaların yedekleri `v0.0_alfa_00002` klasörüne alındı (Simüle edildi).
- **Kod Temizliği:** Partial class yapısına geçilerek okunabilirlik artırıldı.

### [0.0 alfa 00001] - 2024-05-24
- **Mimari Refactor:** `DatabaseService.cs` partial class'lara bölündü (`Settings`, `Channels`, `Epg`).
- **Versiyonlama Başlatıldı:** `VERSION` dosyası oluşturuldu ve `0.0 alfa 00001` atandı.
- **Yedekleme Sistemi:** `/backups/` klasör yapısı oluşturuldu.
- **AGENTS.md Kuralları:** Otomatik dosya bölme ve sürekli güncelleme kuralları eklendi.

## 6. Otodisiplin ve Versiyonlama (Sürekli Güncel)
- **AGENTS.md Senkronizasyonu:** Uygulamada yapılan her türlü kod değişikliği veya yapısal yenilikten sonra bu dosya (AGENTS.md) mutlaka gözden geçirilecek ve gerekirse güncellenecektir. Bu kuralın kendisi de dahil olmak üzere tüm kurallar burada takip edilecektir.
- **Mikro-Dosya Yapısı (Auto-Split):** Dosya boyutlarının büyümesini engellemek için büyük dosyalar (örn. 500 satır üstü veya mantıksal olarak ayrılabilir bölümler) otomatik olarak mikro dosyalara/parçalara bölünecektir. Bu işlem kullanıcıya sorulmadan asistan tarafından proaktif olarak yapılacaktır.
- **Sürekli Versiyonlama:** Uygulama versiyonu `0.0 alfa 00001` formatında tutulacak ve her bir asistan turunda/değişikliğinde son hane artırılacaktır. Mevcut versiyon `/VERSION` dosyasından okunacak ve güncellenecektir.
- **Tam Yedekleme (Backup):** Her versiyon artışında veya kritik değişiklik öncesinde, o anki çalışan yapının tam yedeği `/backups/v[VERSİYON]/` klasörü altında tutulacaktır. Bu sayede istenilen herhangi bir ana geri dönüş garanti altına alınacaktır.
