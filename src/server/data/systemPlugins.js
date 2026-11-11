// System Plugins Registry (synchronized with StreamMesh Core C# plugins)
export const SYSTEM_PLUGINS = [
  {
    id: "stream_resolution",
    name: "Yayın & Çözünürlük Yönlendiricisi",
    category: "Yayın & Oynatıcı",
    version: "1.6.0",
    author: "StreamMesh Media Lab",
    icon: "🎬",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "Akıllı yayın yönlendirici devrede.",
    description: "HLS (.m3u8), TS, MP4, YouTube ve AceStream P2P yayın bağlantılarını dinamik olarak çözümler, yedek bağlantılara yönlendirir ve oynatıcı için optimize eder.",
    settings: [
      { key: "PreferredResolution", label: "Tercih Edilen Çözünürlük", value: "Otomatik (En Yüksek)", defaultValue: "Otomatik (En Yüksek)", type: "select", options: ["Otomatik (En Yüksek)", "1080p Full HD", "720p HD", "480p SD"], description: "HLS master manifest dosyalarında ilk tercih edilecek kalite." },
      { key: "MaxBufferSeconds", label: "Maksimum Oynatma Tamponu (Saniye)", value: "30", defaultValue: "30", type: "number", description: "Düşük bağlantı hızlarında takılmayı önleyen akıllı video arabelleği." },
      { key: "AutoFallbackOnFail", label: "Yayın Kesildiğinde Yedek Kaynağa Geç", value: "true", defaultValue: "true", type: "boolean", description: "Ana yayın donduğunda havuzdaki yedek kaynağa otomatik bağlanır." },
      { key: "ForceProxyAceStreams", label: "AceStream'leri Yerel Proxy Üzerinden İlet", value: "true", defaultValue: "true", type: "boolean", description: "P2P AceStream yayınlarını doğrudan HTTP akışına dönüştürür." }
    ],
    stats: { "Çözümlenen Yayın Sayısı": "48", "Yedek Kaynak Geçişleri": "2", "Hedef Çözünürlük": "Otomatik", "Tampon Süresi": "30 sn" }
  },
  {
    id: "local_network_server",
    name: "Yerel Ağ Medya Sunucusu (Media Server)",
    category: "Ağ & Paylaşım",
    version: "2.1.0",
    author: "StreamMesh Network Lab",
    icon: "🌐",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "Medya sunucusu port 8080 üzerinde hazır.",
    description: "Ev ağınızdaki Smart TV, Android TV, Apple TV, VLC veya telefonlara kanalları canlı aktaran dahili HTTP/HLS proxy sunucusu ve M3U dağıtıcısı.",
    settings: [
      { key: "ServerPort", label: "Sunucu Dinleme Portu", value: "8080", defaultValue: "8080", type: "number", description: "Yerel ağdaki istemcilerin bağlanacağı TCP portu." },
      { key: "EnableCors", label: "Evrensel CORS Başlıkları", value: "true", defaultValue: "true", type: "boolean", description: "Web tarayıcılarının cross-origin yayın isteklerini engellemesini önler." },
      { key: "MaxConcurrentStreams", label: "Maksimum Eşzamanlı Cihaz / Yayın", value: "20", defaultValue: "20", type: "number", description: "Ağ bant genişliğini korumak için eşzamanlı aktif istemci sınırı." },
      { key: "AutoStartOnLaunch", label: "Uygulama Açıldığında Otomatik Başlat", value: "true", defaultValue: "true", type: "boolean", description: "StreamMesh açılır açılmaz medya sunucusunu devreye sokar." }
    ],
    stats: { "Port": "8080", "CORS Durumu": "Etkin", "Maksimum İstemci": "20", "Ağ Durumu": "Açık Ağ Yayını" }
  },
  {
    id: "epg_guide",
    name: "EPG Elektronik Program Rehberi",
    category: "Veri & Rehber",
    version: "1.4.2",
    author: "StreamMesh EPG Team",
    icon: "📅",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "EPG program rehberi motoru hazır.",
    description: "XMLTV ve GZIP sıkıştırılmış EPG kaynaklarını indirir, kanallarla eşleştirir ve anlık/sonraki program akışını sağlar.",
    settings: [
      { key: "AutoRefreshHours", label: "Otomatik Yenileme Aralığı (Saat)", value: "12", defaultValue: "12", type: "number", description: "EPG rehberinin kaç saatte bir güncelleneceği." },
      { key: "TimeShiftHours", label: "Genel Saat Ofseti (Saat)", value: "0", defaultValue: "0", type: "select", options: ["-3", "-2", "-1", "0", "+1", "+2", "+3"], description: "EPG yayın saatleri ileri veya geri kaydırma ofseti." },
      { key: "AutoPurgeExpired", label: "Süresi Dolan Programları Otomatik Temizle", value: "true", defaultValue: "true", type: "boolean", description: "Günü geçen eski program kayıtlarını veritabanından siler." },
      { key: "GzipDecompress", label: "GZIP / Sıkıştırılmış XMLTV Desteği", value: "true", defaultValue: "true", type: "boolean", description: ".gz uzantılı büyük EPG dosyalarını bellek içinde otomatik açar." }
    ],
    stats: { "Eşleşen Kanal": "24", "Saat Ofseti": "0 Saat", "Otomatik Temizlik": "Açık", "Son Senkron": "Bugün 14:00" }
  },
  {
    id: "metadata_enricher",
    name: "Film & Dizi Meta Veri Zenginleştirici (TMDB)",
    category: "Yapay Zeka & Meta",
    version: "1.5.0",
    author: "StreamMesh Metadata Lab",
    icon: "🎬",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "TMDB motoru hazır (tr-TR)",
    description: "VOD filmler ve diziler için TMDB üzerinden kapak afişleri, arka plan görselleri, konu özetleri, oyuncu kadrosu ve IMDb puanlarını otomatik çeker.",
    settings: [
      { key: "TmdbApiKey", label: "TMDB v3 API Anahtarı", value: "3fd2be6f0c70a2a598f084dd23308883", defaultValue: "3fd2be6f0c70a2a598f084dd23308883", type: "text", description: "TheMovieDatabase API anahtarı." },
      { key: "Language", label: "Öncelikli Dil Kodu", value: "tr-TR", defaultValue: "tr-TR", type: "select", options: ["tr-TR", "en-US", "de-DE", "fr-FR"], description: "Film/Dizi özet ve başlıklarının çekileceği dil." },
      { key: "DownloadBackdrops", label: "Yüksek Çözünürlüklü Arka Planları İndir", value: "true", defaultValue: "true", type: "boolean", description: "Detay ekranında gösterilmek üzere geniş format arka planları çeker." },
      { key: "DailyQueryLimit", label: "Günlük Maksimum TMDB Sorgu Limiti", value: "1000", defaultValue: "1000", type: "number", description: "API banlanmasını önlemek için günlük güvenlik sınırı." }
    ],
    stats: { "Zenginleştirilen İçerik": "164", "Seçili Dil": "tr-TR", "Arka Plan İndirme": "Açık", "Günlük Kalan": "836" }
  },
  {
    id: "logo_manager",
    name: "Kanal Logoları & Monogram Yöneticisi",
    category: "Medya & Çalma",
    version: "1.3.1",
    author: "StreamMesh Design Team",
    icon: "🎨",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "Logo önbellek motoru devrede.",
    description: "Yerel ve ulusal TV kanallarının şeffaf SVG/PNG logolarını bulur, diskte önbelleğe alır ve bulunamayan kanallar için renkli dinamik monogramlar üretir.",
    settings: [
      { key: "EnableDiskCache", label: "Logoları Diskte Önbelleğe Al", value: "true", defaultValue: "true", type: "boolean", description: "Her açılışta internetten tekrar indirmek yerine disk önbelleğinden anında yükler." },
      { key: "AutoSearchMissing", label: "Logosu Eksik Kanalları Otomatik Ara", value: "true", defaultValue: "true", type: "boolean", description: "M3U listesinde logosu bulunmayan kanallar için GitHub ve Wikimedia kaynaklarından arar." },
      { key: "FallbackMonogram", label: "Logo Bulunamazsa Renkli Monogram Üret", value: "true", defaultValue: "true", type: "boolean", description: "Logosuz kanallara şık baş harf kartı oluşturur." }
    ],
    stats: { "Önbellek Durumu": "Etkin", "Monogram Üretici": "Aktif", "Kayıtlı Logo": "128" }
  },
  {
    id: "xtream_client",
    name: "Xtream Codes / IPTV API İstemcisi",
    category: "Medya & Çalma",
    version: "2.0.0",
    author: "StreamMesh IPTV Team",
    icon: "⚡",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "Xtream API köprüsü devrede.",
    description: "Xtream Codes API tabanlı IPTV sağlayıcılarına bağlanır, üyelik süresi, canlı kanallar, VOD film ve dizi arşivini kütüphaneye senkronize eder.",
    settings: [
      { key: "RequestTimeoutSeconds", label: "İstek Zaman Aşımı (Saniye)", value: "8", defaultValue: "8", type: "number", description: "Yavaş Xtream sunucuları için bekleme süresi." },
      { key: "UserAgentOverride", label: "Özel User-Agent Başlığı", value: "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0", defaultValue: "IPTVSmartersPro/1.0.0 (Linux;Android 11) StreamMesh/1.0", type: "text", description: "IPTV sağlayıcılarının erişim engellerini aşmak için gönderilen başlık." },
      { key: "IncludeVodMovies", label: "VOD Filmleri Kütüphaneye Dahil Et", value: "true", defaultValue: "true", type: "boolean", description: "Sağlayıcıdaki film kütüphanesini kütüphaneye indirir." },
      { key: "IncludeSeries", label: "Dizileri ve Bölümleri Kütüphaneye Dahil Et", value: "true", defaultValue: "true", type: "boolean", description: "Dizi kategorilerini ve bölümlerini kütüphaneye ekler." }
    ],
    stats: { "Zaman Aşımı": "8 sn", "Film Senkronu": "Açık", "Dizi Senkronu": "Açık" }
  },
  {
    id: "m3u_parser",
    name: "M3U / M3U8 Oynatma Listesi Motoru",
    category: "Medya & Çalma",
    version: "2.2.0",
    author: "StreamMesh Parser Lab",
    icon: "📜",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "M3U ayrıştırıcı devrede.",
    description: "Standart ve genişletilmiş EXTINF etiketli M3U dosyalarını akıllıca çözümler; tvg-id, tvg-logo, group-title ve çözünürlük etiketlerini ayıklar.",
    settings: [
      { key: "AutoDeduplicate", label: "Aynı İsimli Kanalları Akıllı Birleştir", value: "true", defaultValue: "true", type: "boolean", description: "Farklı kaynaklardaki aynı kanalları tek başlık altında toplayıp yedek link havuzu oluşturur." },
      { key: "NormalizeCategories", label: "Kategori İsimlerini Standartlaştır", value: "true", defaultValue: "true", type: "boolean", description: "Farklı yazılan kategori isimlerini standart kategorilere dönüştürür." },
      { key: "MaxConcurrentDownloads", label: "Eşzamanlı Parça İndirme Sayısı", value: "4", defaultValue: "4", type: "number", description: "Büyük M3U dosyaları indirilirken açılacak paralel HTTP kanalı sayısı." }
    ],
    stats: { "Akıllı Birleştirme": "Açık", "Kategori Normalizasyonu": "Açık", "İndirme Kanalı": "4" }
  },
  {
    id: "github_sync",
    name: "Bulut & GitHub Otomatik Senkronizasyon",
    category: "Veri & Senkron",
    version: "1.8.0",
    author: "StreamMesh Cloud Sync Team",
    icon: "🔄",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "Bulut senkronizasyon motoru devrede.",
    description: "GitHub üzerindeki açık kaynaklı ve yasal TV, radyo, VOD ve EPG listelerini periyodik olarak tarar ve kütüphanenizi en güncel yayınlarla otomatik günceller.",
    settings: [
      { key: "SyncIntervalHours", label: "Senkronizasyon Sıklığı (Saat)", value: "6", defaultValue: "6", type: "number", description: "Bulut listelerinin kaç saatte bir denetleneceği." },
      { key: "AutoSyncOnLaunch", label: "Uygulama Açıldığında Otomatik Güncelle", value: "true", defaultValue: "true", type: "boolean", description: "Program her başlatıldığında arka planda bulut güncellemelerini çeker." },
      { key: "PreserveCustomOrder", label: "Kullanıcı Kanal Sıralamasını Koru", value: "true", defaultValue: "true", type: "boolean", description: "Güncelleme yapılırken kullanıcının yaptığı özel kanal sıralamalarını bozmaz." }
    ],
    stats: { "Senkronizasyon Aralığı": "6 Saat", "Açılışta Güncelle": "Açık", "Sıralama Koruması": "Etkin" }
  },
  {
    id: "ssdp_discovery",
    name: "SSDP & UPnP Cihaz Keşif Protokolü",
    category: "Ağ & Paylaşım",
    version: "1.2.0",
    author: "StreamMesh Network Lab",
    icon: "📡",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "Ağda UPnP / SSDP yayını yapılıyor (Port 1900).",
    description: "Yerel ağdaki Smart TV'ler (Samsung Tizen, LG webOS, Android TV) ve UPnP/DLNA oynatıcılar için StreamMesh varlığını yayınlar ve otomatik tanınmasını sağlar.",
    settings: [
      { key: "MulticastAddress", label: "Multicast IP Adresi", value: "239.255.255.250", defaultValue: "239.255.255.250", type: "text", description: "UPnP standardı multicast yayın adresi." },
      { key: "SsdpPort", label: "SSDP Portu", value: "1900", defaultValue: "1900", type: "number", description: "Standart SSDP UDP keşif dinleme portu." },
      { key: "DeviceFriendlyName", label: "Görünen Cihaz Adı", value: "StreamMesh Home Media Center", defaultValue: "StreamMesh Home Media Center", type: "text", description: "Smart TV ekranında görünecek sunucu ismi." }
    ],
    stats: { "Yayın Adresi": "239.255.255.250:1900", "Cihaz Adı": "StreamMesh Media Center" }
  },
  {
    id: "ai_assistant",
    name: "Yapay Zeka Asistanı & Öneri Motoru",
    category: "Yapay Zeka & Meta",
    version: "1.3.0",
    author: "StreamMesh AI Lab",
    icon: "🤖",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "AI servisi hazır (llama3)",
    description: "Yerel LLM (Ollama, LM Studio) veya bulut yapay zeka modelleriyle akıllı içerik önerileri, özetleme ve sohbet asistanı sağlar.",
    settings: [
      { key: "EndpointUrl", label: "AI API URL Endpoint", value: "http://localhost:11434/api/chat", defaultValue: "http://localhost:11434/api/chat", type: "text", description: "Ollama veya OpenAI uyumlu API adresi." },
      { key: "ModelName", label: "Model Adı", value: "llama3", defaultValue: "llama3", type: "text", description: "Kullanılacak dil modeli (örn: llama3, mistral, gemma2)." },
      { key: "SmartRecommendations", label: "İzleme Geçmişine Dayalı Akıllı Öneriler", value: "true", defaultValue: "true", type: "boolean", description: "Kullanıcının izleme alışkanlıklarına göre ana sayfada kişiselleştirilmiş öneriler sunar." }
    ],
    stats: { "Kullanılan Model": "llama3", "Toplam Sorgu": "12", "Son Yanıt Süresi": "180 ms" }
  },
  {
    id: "auto_updater",
    name: "Otomatik Güncelleme & Sürüm Denetleyicisi",
    category: "Sistem & Bakım",
    version: "1.4.0",
    author: "StreamMesh DevOps",
    icon: "🆙",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "Güncelleme denetleyicisi devrede.",
    description: "GitHub Releases üzerinden StreamMesh'in en güncel sürümlerini, hata düzeltmelerini ve yeni özellikleri denetler; tek tıkla güncelleme indirir.",
    settings: [
      { key: "CheckOnStartup", label: "Açılışta Güncellemeleri Denetle", value: "true", defaultValue: "true", type: "boolean", description: "Uygulama başladığında yeni sürüm olup olmadığını sessizce sorgular." },
      { key: "IncludeBeta", label: "Beta & Önizleme Sürümlerini Dahil Et", value: "false", defaultValue: "false", type: "boolean", description: "Geliştirme aşamasındaki deneysel sürümleri gösterir." }
    ],
    stats: { "Son Denetlenen Sürüm": "v0.1.0", "Durum": "Güncel" }
  },
  {
    id: "p2p_stats",
    name: "P2P / AceStream Canlı İstatistik Monitörü",
    category: "Yayın & Oynatıcı",
    version: "1.3.0",
    author: "StreamMesh P2P Lab",
    icon: "📊",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "P2P telemetri izleyici devrede.",
    description: "AceStream ve BitTorrent tabanlı canlı P2P yayınların anlık eş (peer) sayısını, indirme/yükleme hızlarını ve bağlantı sağlığını ölçer.",
    settings: [
      { key: "AcePort", label: "AceStream Engine HTTP Portu", value: "6878", defaultValue: "6878", type: "number", description: "Yerel AceStream çekirdek dinleme portu." },
      { key: "TrackBandwidth", label: "Bant Genişliği Kullanımını Kaydet", value: "true", defaultValue: "true", type: "boolean", description: "Oynatma boyunca harcanan P2P veri miktarını hesaplar." }
    ],
    stats: { "Son Eş Sayısı": "18", "İzlenen P2P Yayın": "6", "Motor Portu": "6878" }
  },
  {
    id: "system_maintenance",
    name: "Sistem Bakımı, Güvenlik Duvarı & Veritabanı Temizleyici",
    category: "Sistem & Bakım",
    version: "2.0.0",
    author: "StreamMesh System Lab",
    icon: "🧹",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "Sistem bakım motoru hazır.",
    description: "SQLite veritabanını sıkıştırır (VACUUM), Windows Güvenlik Duvarı kurallarını onarır, eski günlükleri ve geçici önbellek dosyalarını temizler.",
    settings: [
      { key: "AutoVacuumOnExit", label: "Kapanışta Veritabanını Sıkıştır (VACUUM)", value: "true", defaultValue: "true", type: "boolean", description: "Veritabanı boyutunu küçültür ve disk okuma hızını artırır." },
      { key: "AutoRepairFirewall", label: "Güvenlik Duvarı İzinlerini Otomatik Denetle", value: "true", defaultValue: "true", type: "boolean", description: "Port 8080/8088 için Windows Firewall kurallarını ekler." }
    ],
    stats: { "Veritabanı": "database_v2.db", "Güvenlik Duvarı": "Otomatik Koruma" }
  },
  {
    id: "user_auth",
    name: "Kullanıcı Kimlik Doğrulama & Ebeveyn Kontrolü",
    category: "Sistem & Bakım",
    version: "1.2.0",
    author: "StreamMesh Security Lab",
    icon: "🛡️",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "Profil: Standart Kullanıcı, PIN Koruması: Aktif",
    description: "Çoklu kullanıcı profilleri, yetişkin/kilitli kategoriler için 4 haneli PIN kodu koruması ve favori listesi izolasyonu sağlar.",
    settings: [
      { key: "EnableParentalPin", label: "Ebeveyn Koruması / PIN Kodu", value: "true", defaultValue: "true", type: "boolean", description: "Yetişkin veya kilitli kategorileri şifre ile korur." },
      { key: "ParentalPin", label: "4 Haneli Güvenlik PIN'i", value: "0000", defaultValue: "0000", type: "text", description: "Kilitli kanalları açmak için girilecek şifre." }
    ],
    stats: { "Aktif Profil": "Varsayılan Kullanıcı", "PIN Koruması": "Aktif" }
  },
  {
    id: "global_search",
    name: "Küresel Çoklu Kaynak Arama Motoru",
    category: "Veri & Rehber",
    version: "1.5.0",
    author: "StreamMesh Search Lab",
    icon: "🔍",
    isEnabled: true,
    status: "Aktif",
    statusMessage: "Küresel arama motoru hazır.",
    description: "Açık IPTV havuzları, AceStream ağları, GitHub depoları ve canlı EPG kayıtları arasında eşzamanlı ve yüksek hızlı arama yapar.",
    settings: [
      { key: "MinCharsToSearch", label: "Aramayı Başlatmak İçin Minimum Karakter", value: "2", defaultValue: "2", type: "number", description: "Kullanıcı kaç harf yazdıktan sonra aramanın başlayacağı." },
      { key: "IncludeAceStreamSearch", label: "AceStream P2P Ağında Ara", value: "true", defaultValue: "true", type: "boolean", description: "P2P içerik havuzunda da eşzamanlı tarama yapar." }
    ],
    stats: { "Toplam Arama": "28", "AceStream Taraması": "Açık", "Açık Havuz": "Açık" }
  }
];
