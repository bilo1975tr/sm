# StreamMesh Geliştirici Yönergeleri (AGENTS.md)

Bu belge, StreamMesh projesinde çalışan yapay zeka kodlama asistanları ve geliştiriciler için standart geliştirme standartlarını, proje mimarisini ve kurallarını tanımlar. Lütfen projede değişiklik yaparken bu kurallara harfiyen uyun.

---

## 1. Proje Genel Tanımı ve Teknolojiler

StreamMesh; C#, .NET ve WPF (Windows Presentation Foundation) kullanılarak geliştirilmiş, gelişmiş bir IPTV, EPG (Elektronik Program Rehberi), AceStream/P2P oynatıcı ve medya yönetim uygulamasıdır.

- **Platform:** .NET / WPF (Windows Masaüstü Uygulaması)
- **Veritabanı & Kalıcılık:** SQLite (Yerel) ve Firebase Firestore (P2P tünelleme, senkronizasyon ve veri saklama için)
- **P2P Tünelleme:** WebRTC, STUN (örneğin `stun.l.google.com`) ve TURN servisleri
- **Yapay Zeka Entegrasyonu:** Ollama / LM Studio (Yerel LLM entegrasyonu)
- **Dış Servisler:** GitHub (kanal listeleri senkronizasyonu), VLC Native (medya oynatım motoru)

---

## 2. Geliştirme Kuralları ve Kodlama Standartları

### A. C# ve .NET Standartları
1. **Asenkron Programlama:** UI iş parçacığının (UI Thread) kilitlenmesini önlemek için tüm disk, ağ, Firebase ve veritabanı işlemlerinde mutlaka `async/await` mimarisi kullanılmalıdır. Bloklayıcı `.Result`, `.Wait()` veya `Thread.Sleep()` kullanmaktan kaçının.
2. **Tip Güvenliği ve Temiz Kod:** Kod yazarken gereksiz tip dönüşümlerinden kaçının. Güçlü tipler (strongly-typed models) kullanın. Model sınıfları `/Models` dizininde, servisler ise `/Services` dizininde yer almalıdır.
3. **Loglama (`LogService`):** Uygulama içerisindeki tüm kritik adımlar, hata durumları ve bilgi mesajları `LogService` aracılığıyla kaydedilmelidir.
   - Bilgi mesajları için: `LogService.LogInfo("...")` veya ilgili sınıfa özgü log metotları.
   - Hata durumları için: `LogService.LogError("...", exception)`

### B. WPF ve XAML Kuralları
1. **MVVM ve Veri Bağlama (Data Binding):** Görünümler (Views) ile iş mantığı (Services/Models) arasındaki bağ olabildiğince gevşek olmalıdır. UI elemanlarının durumları kod arkasından (code-behind) doğrudan manipüle edilmek yerine veri bağlama (Binding) ile yönetilmelidir.
2. **Duyarlı ve Temiz Arayüz Tasarımı:** XAML tasarımlarında sabit genişlik/yükseklik değerleri yerine esnek düzen belirteçleri (Grid, Auto, *, DockPanel, StackPanel) tercih edilmelidir.
3. **Kullanıcı Deneyimi:** Ağır işlemler yapılırken kullanıcıya mutlaka bir yükleniyor (Loading/ProgressBar) göstergesi sunulmalıdır.

### C. Altyapı ve Yapay Süslendirme Karşıtlığı (Anti-AI-Slop)
1. **Gerçekçi Arayüzler:** Kullanıcı arayüzüne veya log ekranlarına uydurma sistem verileri, hayali konsol çıktıları (örn: `"CORE_NODE_ONLINE"`, `"PORT: 3000"`, `"SYSTEM_ACTIVE"`) veya süs amaçlı anlamsız durum göstergeleri eklemeyin.
2. **Sade ve Doğal İsimlendirmeler:** UI bileşenleri için süslü, yapay veya dramatik isimler yerine (örn. *"Chronos Engine"*), sade ve anlaşılır insan dilinde isimlendirmeler (örn. *"Zamanlayıcı"* veya *"Yayın Akışı Servisi"*) kullanın.

### D. Firebase ve Ağ Yönetimi
1. **Firebase Yapılandırması:** Firebase işlemlerini yürütürken kimlik bilgilerinin (Credentials) eksik olabileceği veya ağ hatası alınabileceği senaryoları her zaman `try-catch` blokları ile sarmalayın ve kullanıcıya veya log sistemine anlamlı hata detayları sunun.
2. **Soket ve Bağlantı Yönetimi:** STUN/TURN veya TCP soket bağlantıları kurarken IPv4 ve IPv6 adres ailelerinin uyumluluğuna dikkat edin. Soket oluşturulurken hedef IP adresinin ailesi (`AddressFamily`) dinamik olarak tespit edilmeli ve soket buna göre başlatılmalıdır.

---

## 3. Değişiklik ve Sürüm Yönetimi

- Projede yapılan her anlamlı güncelleme sonrasında `/VERSION` dosyasındaki sürüm numarası uygun şekilde (örn: `1.8.2` -> `1.8.3`) artırılmalıdır.
- Kod eklerken mevcut sınıfların yapısını bozmamaya, özellikle kısmi sınıfları (partial classes) ve event handler yapılarını doğru korumaya özen gösterin.
