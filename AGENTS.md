# AI Asistan Sistem Yönergeleri

Bu dosya, yapay zeka asistanının bu projede nasıl davranması gerektiğini belirleyen kalıcı kuralları içerir. Tüm geçmiş güncellemeler ve mimari kararlar eritilerek net kural standartlarına dönüştürülmüştür.

## 1. İletişim ve Karakter
- **Dil:** Her zaman TÜRKÇE cevap verilecektir.
- **Biçim:** Cevaplar her zaman KISA, ÖZ ve doğrudan hedefe yönelik olacaktır. Geveze açıklamalardan kaçınılacaktır.
- **Proaktif Danışmanlık:** Kullanıcı uygunsuz, verimsiz veya uygulamayı bozacak bir talepte bulunursa; asistan kullanıcıyı UYARACAK, nedenini kısaca açıklayacak ve EN İYİ/DOĞRU alternatifi sunacaktır.

## 2. Geliştirme ve Kod Standartları
- **Sıfır Hata Prensibi:** Yazılan her kod parçası teslim edilmeden önce araçlar (lint, compile) aracılığıyla mutlaka SİMÜLE edilecek ve test edilecektir. Uygulama her adımda KESİNLİKLE ÇALIŞIR durumda kalacaktır.
- **Kod Kontrolü:** İstek geldiğinde sadece ilgili yer değil, bağlantılı olan tüm fonksiyonlar (satır satır) kontrol edilecek, potansiyel çökmeler önceden tespit edilip düzeltilecektir.
- **Özellik Hafızası:** Var olan bir özellik baştan yazılmayacak, mevcut sürüm tespit edilip REVİZE edilecektir.

## 3. Teknik Disiplin ve Platform Standartları
- **Platform Uyumu:** Native kütüphaneler (VLC) nedeniyle proje her zaman `x64` mimarisinde tutulacaktır.
- **LibVLCSharp API:** `MediaPlayer.VideoTracks` mevcut değildir; çözünürlük listesi için her zaman `VideoTrackDescription` kullanılacaktır.
- **Yayın Yönetimi:** Timeshift ve tampon (buffer) güvenliği için `SetPause()` kullanılacak, yayın dondurulduğunda canlıya atlamaması sağlanacaktır.
- **Hata Yakalama:** `App.xaml.cs` ve `PlayerView.xaml.cs` dosyalarında mutlaka `fatal_error.log` kaydı ve `MessageBox` uyarısı bulunacaktır.
- **Resource Yönetimi:** XAML içindeki `StaticResource` tanımları (Slider stili vb.) her zaman kontrol edilecek, eksik tanım nedeniyle uygulamanın açılmaması önlenecektir.

## 4. Uygulama Mimarisi ve Veri Kurgusu
- **Çoklu Veri Altyapısı:** Kanal adı, yayın URL'si, EPG ve Logolar tek bir statik satıra bağlı değildir. Sistem, çoklu (birden fazla logo/URL barındırabilen) esnek bir yapıya sahiptir. URL'ler ve logolar kendi içinde varsayılan yapılabilir, yıldızlanabilir veya silinebilir.
- **Drag & Drop (Sürükle-Bırak) ile Kanal Birleştirme:** Kullanıcılar çift veya aynı yayını yapan kanalları birbirleri üzerine sürükleyerek birleştirebilir. Birleştirilen kanalların yayın URL'leri ve logoları tek kanal altında cüzdan gibi toplanır (`MergeChannels`).
- **Kanal/Yayın Doğrulama Sistemi (Stream Checker):** Sadece "HTTP 200 OK" dönmesi kabul edilemez! Videonun gerçekten aktığı (frame test / paket alımı ile ilk 8KB'lık TS blok testi yapılarak) KESİNLİKLE kontrol edilmelidir.
- **Onaylı/Onaysız Ayrımı ve Doğrulama Menüleri:** Kanal modelinde `IsVerified` alanı mevcuttur. Ayarlarda "Tüm Kanalları Kontrol Et" ve "Onaysız Yayınları Kontrol Et" olmak üzere iki ayrı doğrulama özelliği çalışacaktır. Çalışmayan kanallar havuzdan veya onaydan kaldırılacaktır.
- **Kişisel İzleme ve Sizin Çok İzledikleriniz:** `Channel` modeline `PersonalWatchCount` ve `HasPersonalWatch` alanları eklenmiştir. Ana ekranda bu verilere dayalı "Sizin Çok İzledikleriniz" filtresi ve kişisel izleme rozetleri aktiftir.

## 5. Hibrit Bulut (CQRS) ve Veri Senkronizasyonu
- **P2P Ağının Kaldırılması:** TCP ve UDP tabanlı tüm P2P (Peer-to-Peer) ağ, port dinlemeleri, UDP discovery, STUN, UPnP ve düğüm yönetim servisleri kalıcı olarak uygulamadan temizlenmiştir. Ağ üzerinde herhangi bir P2P mekanizması KESİNLİKLE kullanılmayacaktır.
- **Yazma İşlemleri (Firebase Havuz):** Doğrulanan veya yeni eklenen kanallar asenkron olarak Firebase bekleme havuzuna gönderilir. Mükerrer kayıtları engellemek için rastgele ID'ler yerine, yayın URL'sinden türetilen benzersiz MD5 (URL Hash) kullanılarak doğrudan PATCH yöntemiyle gönderim yapılır.
- **Okuma İşlemleri (GitHub Raw CDN):** İstemciler sunucu maliyetini sıfırlamak için tüm kanal listesini doğrudan GitHub Raw CDN (`channels.json`) üzerinden çeker. İstemciler devasa tekilleştirilmiş tek dosya yerine, sadece seçtikleri ana dil ve ek dillere ait ayrıştırılmış dosyaları çekerek az veri tüketimiyle çalışır.
- **Veri Koruma:** `.gitignore` dosyasında, her türlü GitHub ve Firebase senkronizasyonunda/aktarımında `channels.json`, `channels_almanca.json`, `channels_bilinmiyor.json`, `channels_deutsch.json`, `channels_deutsch_deutschland.json`, `channels_english.json` ve `channels_türkçe.json` (ve `channels_*.json`, `kanallar_*.m3u`) gibi kritik veritabanı, ayrıştırılmış dil ve çıktı dosyalarının KESİNLİKLE silinmemesi, korunması ve kayba uğramaması garanti altına alınmalıdır. Sürüm kontrolü ve veri akışı güvenliği bu kurallara tabidir.

## 6. Video, Ses ve Performans Optimizasyonu
- **YouTube ve VOD Oynatımı:** Yavaş açılmaları ve sarmalardaki donmaları aşmak için "Single Muxed Stream" (Tümleşik Ses+Video MP4 akışı) yapısı kullanılacak, OSD üzerinde toplam süre ve hassas zaman çizgisi (Seekbar) anlık çözümlenecektir.
- **HLS Canlı Yayın Tespiti:** Tekil `.m3u8` yayınlarında `#EXT-X-TARGETDURATION`, `#EXT-X-STREAM-INF` veya `#EXT-X-MEDIA-SEQUENCE` belirteçleri tespit edildiğinde sistem m3u listesi gibi parçalamayı reddedip doğrudan tek canlı yayın url'si olarak tarayacaktır.
- **Dynamic Range Compressor (OSN):** Ses normalizasyonunda `.normvol` yerine kararlı olan `.compressor` kullanılacaktır (`:compressor-makeup-gain`, `:compressor-ratio=4.0`). OSN butonu tıklantısı yayını kesmeyecek, bir sonraki yayında aktif olacak şekilde çalışırken, kullanıcının anlık reaksiyonu hissetmesi adına oynatıcı ses düzeyini yazılımsal olarak 50 birim artırıp/azaltacaktır.
- **WPF Aspect Ratio & Crop Entegrasyonu:** Oranlamalar (16:9, 4:3 vb.) WPF'nin yerel yerleşim transformatörü (`VideoImage.LayoutTransform` -> `ScaleTransform`) ve `.CropGeometry` kuralları ile işlenecektir.
- **Sistem Tepsisi ve AceStream Yönetimi:** Uygulama (X) ile kapatıldığında Sistem Tepsisine (`Tray`) küçültülecektir. Küçültüldüğünde veya çıkıldığında tüm aktif VLC oynatımları sonlandırılacak (`StopPlayback`) ve arka planda çalışan `ace_engine` işlemleri (Process.Kill) zorunlu olarak kapatılacaktır.

## 7. Ülke, Dil ve Kültür Standartları
- **Sistem Kültür Entegrasyonu:** Giriş, ayarlar, kaynak ve kanal düzenleme pencerelerindeki tüm dil ve ülke listeleri global kültür listesinden (`System.Globalization.CultureInfo.GetCultures`) çekilen dinamik dillerle (`SystemCultures`) senkronize çalışmalıdır.
- **Dil Normalizasyonu:** `NormalizeLanguage` metodu `tr`, `en`, `de` gibi iki/üç harfli ISO kodlarını ve "Turkish", "English", "German" gibi dilleri kusursuz bir şekilde Türkçe yerel isimlerine çevirmelidir.
- **"Bilinmiyor" ve "Hiçbiri" Standartları:** Düzenleme ekranlarındaki ComboBox yapılarında (`LangCombo`, `LanguageCombo`, `BulkLanguageCombo`) listelerin en üstünde "Hiçbiri" ve hemen onun altında "Bilinmiyor" etiketleri bulunmalı, kullanıcıların diledikleri kanalları bu esnek etiketlerle yönetmesi korunmalıdır.

## 8. Otodisiplin ve Versiyonlama
- **AGENTS.md Senkronizasyonu:** Uygulamada yapılan her türlü kod değişikliği veya yapısal yenilikten sonra bu dosyadaki kurallar gözden geçirilerek korunacaktır.
- **Mikro-Dosya Yapısı (Auto-Split):** Dosya boyutlarının büyümesini engellemek için büyük dosyalar (örn. 500 satır üstü veya mantıksal olarak ayrılabilir bölümler) otomatik olarak mikro dosyalara/parçalara bölünecektir.
- **Sürekli Versiyonlama:** Uygulama versiyonu `0.0 alfa [VERSİYON]` formatında tutulacak, her bir asistan turunda/değişikliğinde son hane artırılacak, `/VERSION` dosyasından okunup güncellenecektir.
- **Tam Yedekleme (Backup):** Her versiyon artışında veya kritik değişiklik öncesinde, o anki çalışan yapının tam yedeği `/backups/v[VERSİYON]/` klasörü altında tutulacaktır.
