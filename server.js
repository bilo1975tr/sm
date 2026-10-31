import http from 'http';
import https from 'https';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PORT = 3000;

// Read version
let version = '0.0.1';
try {
  if (fs.existsSync(path.join(__dirname, 'version.txt'))) {
    version = fs.readFileSync(path.join(__dirname, 'version.txt'), 'utf8').trim();
  }
} catch (e) {}

// Initial curated free, public, and legal test channels & VODs
const INITIAL_CHANNELS = [
  {
    id: "trt1",
    name: "TRT 1 HD",
    category: "TV",
    group: "Ulusal",
    groupTitle: "Ulusal",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/c/ca/TRT_1_logo.svg/320px-TRT_1_logo.svg.png",
    url: "https://tv-trt1.medya.trt.com.tr/master.m3u8",
    streamUrl: "/stream/trt1",
    epg: "Gönül Dağı",
    epgTime: "20:00 - 23:15",
    nextEpg: "Gece Bülteni (23:15)",
    resolution: "1080p",
    desc: "Türkiye'nin ulusal kanalı TRT 1 canlı yayını",
    isLive: true
  },
  {
    id: "trt_haber",
    name: "TRT Haber",
    category: "TV",
    group: "Haber",
    groupTitle: "Haber",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/4/49/TRT_Haber_logo.svg/320px-TRT_Haber_logo.svg.png",
    url: "https://tv-trthaber.medya.trt.com.tr/master.m3u8",
    streamUrl: "/stream/trt_haber",
    epg: "Canlı Haber Bülteni",
    epgTime: "14:00 - 15:00",
    nextEpg: "Ekonomi Dünyası (15:00)",
    resolution: "1080p",
    desc: "Son dakika haberler ve canlı gelişmeler",
    isLive: true
  },
  {
    id: "trt_spor",
    name: "TRT Spor",
    category: "TV",
    group: "Spor",
    groupTitle: "Spor",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/1/1a/TRT_Spor_logo_2021.svg/320px-TRT_Spor_logo_2021.svg.png",
    url: "https://tv-trtsporyildiz.medya.trt.com.tr/master.m3u8",
    streamUrl: "/stream/trt_spor",
    epg: "Spor Bülteni & Maç Özetleri",
    epgTime: "13:00 - 14:30",
    nextEpg: "Gündem Spor (14:30)",
    resolution: "1080p",
    desc: "Canlı spor müsabakaları ve analizler",
    isLive: true
  },
  {
    id: "trt_belgesel",
    name: "TRT Belgesel HD",
    category: "TV",
    group: "Belgesel",
    groupTitle: "Belgesel",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/3/30/TRT_Belgesel_logo_2021.svg/320px-TRT_Belgesel_logo_2021.svg.png",
    url: "https://tv-trtbelgesel.medya.trt.com.tr/master.m3u8",
    streamUrl: "/stream/trt_belgesel",
    epg: "Doğu'nun Kayıp Gizemleri",
    epgTime: "19:30 - 20:30",
    nextEpg: "Doğadaki İnsan (20:30)",
    resolution: "1080p",
    desc: "Doğa, tarih ve insan belgeselleri",
    isLive: true
  },
  {
    id: "trt_muzik",
    name: "TRT Müzik",
    category: "TV",
    group: "Müzik",
    groupTitle: "Müzik",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/5/52/TRT_M%C3%BCzik_logo_2021.svg/320px-TRT_M%C3%BCzik_logo_2021.svg.png",
    url: "https://tv-trtmuzik.medya.trt.com.tr/master.m3u8",
    streamUrl: "/stream/trt_muzik",
    epg: "Akşam Sefası Konseri",
    epgTime: "20:00 - 22:00",
    nextEpg: "Gönülden Dile (22:00)",
    resolution: "1080p",
    desc: "Geleneksel ve çağdaş müzik performansları",
    isLive: true
  },
  {
    id: "trt_cocuk",
    name: "TRT Çocuk HD",
    category: "TV",
    group: "Çocuk",
    groupTitle: "Çocuk",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/d/d4/TRT_%C3%87ocuk_logo_2021.svg/320px-TRT_%C3%87ocuk_logo_2021.svg.png",
    url: "https://tv-trtcocuk.medya.trt.com.tr/master.m3u8",
    streamUrl: "/stream/trt_cocuk",
    epg: "Rafadan Tayfa Dehliz Macerası",
    epgTime: "16:00 - 17:00",
    nextEpg: "Pırıl ve Arkadaşları (17:00)",
    resolution: "1080p",
    desc: "Eğitici çizgi film ve çocuk programları",
    isLive: true
  },
  {
    id: "nasa_tv",
    name: "NASA TV Live",
    category: "TV",
    group: "Belgesel & Bilim",
    groupTitle: "Belgesel & Bilim",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/e/e5/NASA_logo.svg/300px-NASA_logo.svg.png",
    url: "https://ntv1.akamaized.net/hls/live/2014075/NASA-NTV1-HLS/master.m3u8",
    streamUrl: "/stream/nasa_tv",
    epg: "ISS Live Views & Earth Orbit Stream",
    epgTime: "12:00 - 16:00",
    nextEpg: "Artemis Mission Briefing (16:00)",
    resolution: "1080p",
    desc: "Uluslararası Uzay İstasyonu ve NASA canlı yayını",
    isLive: true
  },
  {
    id: "redbull_tv",
    name: "Red Bull TV",
    category: "TV",
    group: "Spor",
    groupTitle: "Spor",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/en/thumb/f/f5/Red_Bull_TV_logo.svg/320px-Red_Bull_TV_logo.svg.png",
    url: "https://rbmn-live.akamaized.net/hls/live/590964/BoRB-AT/master.m3u8",
    streamUrl: "/stream/redbull_tv",
    epg: "Extreme Sports Worldwide & F1 Paddock",
    epgTime: "14:00 - 16:30",
    nextEpg: "Mountain Bike World Series",
    resolution: "1080p",
    desc: "Ekstrem sporlar, motor sporları ve canlı etkinlikler",
    isLive: true
  },
  {
    id: "bloomberg",
    name: "Bloomberg Quicktake",
    category: "TV",
    group: "Haber",
    groupTitle: "Finans & Haber",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/4/4b/Bloomberg_L.P._logo.svg/320px-Bloomberg_L.P._logo.svg.png",
    url: "https://bloomberg.com/media-manifest/streams/us.m3u8",
    streamUrl: "/stream/bloomberg",
    epg: "Global Markets & Tech News Today",
    epgTime: "14:00 - 15:00",
    nextEpg: "Closing Bell Discussion",
    resolution: "1080p",
    desc: "Küresel piyasalar, finansal analizler ve teknoloji haberleri",
    isLive: true
  },
  {
    id: "acestream_sample_1",
    name: "AceStream P2P Canlı Spor Yayını",
    category: "TV",
    group: "AceStream P2P",
    groupTitle: "AceStream P2P",
    sourceType: "ACESTREAM",
    quality: "720p HD",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/1/1a/TRT_Spor_logo_2021.svg/320px-TRT_Spor_logo_2021.svg.png",
    url: "http://127.0.0.1:6878/ace/getstream?infohash=d80e1a3f6b9c5d2e1a3f6b9c5d2e1a3f6b9c5d2e",
    streamUrl: "/stream/acestream_sample_1",
    epg: "Süper Lig P2P Canlı Yayın",
    epgTime: "20:00 - 22:30",
    nextEpg: "Maç Sonu Röportajlar",
    resolution: "720p",
    desc: "P2P BitTorrent protokolü üzerinden yüksek hızlı AceStream yayını",
    isLive: true,
    isStreamMeshRequired: true
  },
  {
    id: "acestream_web_1",
    name: "AceStream Web Portal Kanalı",
    category: "TV",
    group: "AceStream Web",
    groupTitle: "AceStream Web",
    sourceType: "ACESTREAM_WEB",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/e/e0/TRT_World_logo.svg/320px-TRT_World_logo.svg.png",
    url: "https://tv-trtworld.medya.trt.com.tr/master.m3u8",
    streamUrl: "/stream/acestream_web_1",
    epg: "Web P2P Transcode Stream Feed",
    epgTime: "19:00 - 21:00",
    nextEpg: "Global Newsfeed Live",
    resolution: "1080p",
    desc: "Tarayıcı uyumlu HTTP Live Streaming formatında AceStream aktarımı",
    isLive: true
  },
  // VOD Movies
  {
    id: "big_buck_bunny",
    name: "Big Buck Bunny",
    category: "Filmler",
    group: "Animasyon",
    groupTitle: "Animasyon Film",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/c/c5/Big_buck_bunny_poster_big.jpg/300px-Big_buck_bunny_poster_big.jpg",
    url: "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4",
    streamUrl: "/stream/big_buck_bunny",
    epg: "Blender Foundation Açık Film Projesi",
    epgTime: "00:00 - 10:34",
    nextEpg: "Süre: 10 Dakika 34 Saniye",
    resolution: "1080p",
    desc: "Dev tavşanın ormandaki komik intikam macerası. Blender Foundation ödüllü animasyon.",
    isLive: false
  },
  {
    id: "tears_of_steel",
    name: "Tears of Steel",
    category: "Filmler",
    group: "Bilim Kurgu",
    groupTitle: "Bilim Kurgu",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/2/23/Tears_of_steel_poster.jpg/300px-Tears_of_steel_poster.jpg",
    url: "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/TearsOfSteel.mp4",
    streamUrl: "/stream/tears_of_steel",
    epg: "VFX Açık Kaynak Bilim Kurgu Filmi",
    epgTime: "00:00 - 12:14",
    nextEpg: "Süre: 12 Dakika 14 Saniye",
    resolution: "1080p",
    desc: "Distopik gelecekte robotlar ve savaşçılar arasındaki heyecan dolu mücadele.",
    isLive: false
  },
  {
    id: "sintel",
    name: "Sintel (Ejderha Arayışı)",
    category: "Filmler",
    group: "Macera & Fantezi",
    groupTitle: "Macera",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/8/8f/Sintel_poster.jpg/300px-Sintel_poster.jpg",
    url: "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/Sintel.mp4",
    streamUrl: "/stream/sintel",
    epg: "Blender Foundation Bağımsız Projesi",
    epgTime: "00:00 - 14:48",
    nextEpg: "Süre: 14 Dakika 48 Saniye",
    resolution: "1080p",
    desc: "Yavru bir ejderhayı kurtarmak için tehlikeli yolculuğa çıkan genç bir savaşçının hikayesi.",
    isLive: false
  },
  // VOD Series
  {
    id: "series_elephants_dream",
    name: "Elephants Dream - Pilot Bölüm",
    category: "Diziler",
    group: "Bilim Kurgu Dizi",
    groupTitle: "Diziler (Sezon 1)",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/e/e8/Elephants_Dream_poster.jpg/300px-Elephants_Dream_poster.jpg",
    url: "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ElephantsDream.mp4",
    streamUrl: "/stream/series_elephants_dream",
    epg: "Sezon 1 • Bölüm 1: Dev Makine",
    epgTime: "00:00 - 10:53",
    nextEpg: "Sonraki Bölüm: Labirent",
    resolution: "1080p",
    desc: "Devasa ve gizemli bir makinenin içinde kaybolan iki karakterin gerçeküstü yolculuğu.",
    isLive: false
  },
  {
    id: "series_cosmos_1",
    name: "Kozmos ve Yıldızlar - Bölüm 1",
    category: "Diziler",
    group: "Belgesel Dizi",
    groupTitle: "Diziler (Sezon 1)",
    sourceType: "DIRECT",
    quality: "1080p",
    logo: "https://upload.wikimedia.org/wikipedia/commons/thumb/e/e5/NASA_logo.svg/300px-NASA_logo.svg.png",
    url: "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerBlazes.mp4",
    streamUrl: "/stream/series_cosmos_1",
    epg: "Sezon 1 • Bölüm 1: Samanyolu Yolculuğu",
    epgTime: "00:00 - 15:00",
    nextEpg: "Sonraki Bölüm: Karadelikler",
    resolution: "1080p",
    desc: "Evrenin derinlikleri ve güneş sisteminin gizemlerini anlatan mini belgesel dizi.",
    isLive: false
  }
];

// System Plugins Registry (synchronized with StreamMesh Core C# plugins)
const SYSTEM_PLUGINS = [
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

// Helper to fetch remote URL with timeout and redirect following
function fetchRemoteText(targetUrl, timeoutMs = 15000) {
  return new Promise((resolve, reject) => {
    try {
      const parsed = new URL(targetUrl);
      const client = parsed.protocol === 'https:' ? https : http;
      const req = client.get(targetUrl, {
        timeout: timeoutMs,
        headers: {
          'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMesh/1.0'
        }
      }, (res) => {
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          return resolve(fetchRemoteText(res.headers.location, timeoutMs));
        }
        if (res.statusCode < 200 || res.statusCode >= 300) {
          return reject(new Error(`HTTP Hata: ${res.statusCode}`));
        }
        let data = '';
        res.on('data', chunk => {
          data += chunk;
          if (data.length > 20 * 1024 * 1024) {
            res.destroy();
            resolve(data);
          }
        });
        res.on('end', () => resolve(data));
      });
      req.on('timeout', () => { req.destroy(); reject(new Error('Zaman aşımı')); });
      req.on('error', (err) => reject(err));
    } catch (e) {
      reject(e);
    }
  });
}

// Generate M3U playlist format
function generateM3uContent(host) {
  let m3u = '#EXTM3U name="StreamMesh Web Playlist"\n';
  INITIAL_CHANNELS.forEach(ch => {
    const playUrl = ch.url.startsWith('http') ? ch.url : `http://${host}/stream/${ch.id}`;
    m3u += `#EXTINF:-1 tvg-id="${ch.id}" tvg-name="${ch.name}" tvg-logo="${ch.logo}" group-title="${ch.group || ch.category}",${ch.name}\n`;
    m3u += `${playUrl}\n`;
  });
  return m3u;
}

const server = http.createServer(async (req, res) => {
  const parsedUrl = new URL(req.url, `http://${req.headers.host || 'localhost:3000'}`);
  const pathname = parsedUrl.pathname;

  // Global CORS headers
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS, HEAD');
  res.setHeader('Access-Control-Allow-Headers', '*');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  // 1. API: Version & Status
  if (pathname === '/api/version') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({
      version,
      status: 'online',
      engines: { m3u: 'ready', epg: 'ready', ace: 'ready', hls: 'ready', router: 'active', plugins: 'active' }
    }));
    return;
  }

  // 1.5. API: Plugins List & Configuration
  if (pathname === '/api/plugins') {
    if (req.method === 'GET') {
      res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify({
        total: SYSTEM_PLUGINS.length,
        active: SYSTEM_PLUGINS.filter(p => p.isEnabled).length,
        plugins: SYSTEM_PLUGINS
      }));
      return;
    }
  }

  // API: Toggle Plugin Enabled State
  if (pathname === '/api/plugins/toggle' && req.method === 'POST') {
    let body = '';
    req.on('data', chunk => body += chunk);
    req.on('end', () => {
      try {
        const data = JSON.parse(body);
        const plugin = SYSTEM_PLUGINS.find(p => p.id === data.id);
        if (plugin) {
          plugin.isEnabled = typeof data.enabled === 'boolean' ? data.enabled : !plugin.isEnabled;
          plugin.status = plugin.isEnabled ? 'Aktif' : 'Devre Dışı';
          res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
          res.end(JSON.stringify({ success: true, plugin }));
        } else {
          res.writeHead(404, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ error: 'Eklenti bulunamadı' }));
        }
      } catch (e) {
        res.writeHead(400, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Geçersiz veri' }));
      }
    });
    return;
  }

  // API: Save Plugin Settings
  if (pathname === '/api/plugins/settings' && req.method === 'POST') {
    let body = '';
    req.on('data', chunk => body += chunk);
    req.on('end', () => {
      try {
        const data = JSON.parse(body);
        const plugin = SYSTEM_PLUGINS.find(p => p.id === data.id);
        if (plugin && data.settings && Array.isArray(data.settings)) {
          data.settings.forEach(s => {
            const target = plugin.settings.find(ps => ps.key === s.key);
            if (target) target.value = s.value;
          });
          res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
          res.end(JSON.stringify({ success: true, message: 'Ayarlar kaydedildi', plugin }));
        } else {
          res.writeHead(404, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ error: 'Eklenti bulunamadı' }));
        }
      } catch (e) {
        res.writeHead(400, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Geçersiz veri' }));
      }
    });
    return;
  }

  // API: Execute Plugin Action
  if (pathname === '/api/plugins/action' && req.method === 'POST') {
    let body = '';
    req.on('data', chunk => body += chunk);
    req.on('end', () => {
      try {
        const data = JSON.parse(body);
        const plugin = SYSTEM_PLUGINS.find(p => p.id === data.id);
        if (plugin) {
          const action = data.action || 'test';
          res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
          res.end(JSON.stringify({
            success: true,
            result: `✅ '${plugin.name}' eklentisi '${action}' işlemini başarıyla tamamladı!`
          }));
        } else {
          res.writeHead(404, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ error: 'Eklenti bulunamadı' }));
        }
      } catch (e) {
        res.writeHead(400, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Geçersiz veri' }));
      }
    });
    return;
  }

  // 2. API: Channels (supports both array and object formats for universal frontend compatibility)
  if (pathname === '/api/channels' || pathname === '/channels') {
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    // If format=object query is given, return object; otherwise return the direct array as expected by MediaServer.cs
    if (parsedUrl.searchParams.get('format') === 'object') {
      res.end(JSON.stringify({ count: INITIAL_CHANNELS.length, channels: INITIAL_CHANNELS }));
    } else {
      res.end(JSON.stringify(INITIAL_CHANNELS));
    }
    return;
  }

  // 3. Smart Stream Router: /stream/:id (Direct playback & proxy router)
  if (pathname.startsWith('/stream/')) {
    const channelId = pathname.substring(8).trim().replace(/\/$/, '');
    const found = INITIAL_CHANNELS.find(c => c.id === channelId || c.id.toLowerCase() === channelId.toLowerCase());

    if (!found) {
      res.writeHead(404, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: `Kanal bulunamadı: ${channelId}` }));
      return;
    }

    // AceStream stream redirection / proxy
    if (found.sourceType === 'ACESTREAM' || found.url.startsWith('acestream://')) {
      let hash = found.url.replace('acestream://', '').trim();
      if (found.url.includes('infohash=')) {
        hash = found.url.split('infohash=')[1].split('&')[0];
      }
      const aceProxyUrl = `http://127.0.0.1:6878/ace/getstream?infohash=${hash}`;
      res.writeHead(302, { 'Location': aceProxyUrl });
      res.end();
      return;
    }

    // Direct stream (HLS .m3u8 or MP4)
    res.writeHead(302, { 'Location': found.url });
    res.end();
    return;
  }

  // 4. API: EPG Query
  if (pathname === '/api/epg' || pathname === '/api/epg/query') {
    const id = parsedUrl.searchParams.get('id') || '';
    const name = (parsedUrl.searchParams.get('name') || '').toLowerCase();
    const found = INITIAL_CHANNELS.find(c => c.id === id || c.name.toLowerCase().includes(name)) || INITIAL_CHANNELS[0];
    
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({
      channel: found.name,
      current: {
        title: found.epg || "Canlı Yayın Akışı",
        timeRange: found.epgTime || "14:00 - 16:00",
        desc: found.desc || ((found.group || "Genel") + " Canlı Yayın Akışı")
      },
      next: {
        title: found.nextEpg || "Bülten & Özel Program",
        timeRange: "16:00 - 18:00",
        desc: "Takip eden yayın programı"
      }
    }));
    return;
  }

  // 5. API: EPG Batch
  if (pathname === '/api/epg/batch') {
    const result = {};
    INITIAL_CHANNELS.forEach(c => {
      result[c.id] = {
        title: c.epg || "Canlı Yayın",
        time: c.epgTime || "--:--"
      };
    });
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify(result));
    return;
  }

  // 6. API: Stream Proxy (solves CORS & mixed content issues)
  if (pathname === '/api/proxy' || pathname === '/proxy') {
    const targetUrl = parsedUrl.searchParams.get('url');
    if (!targetUrl) {
      res.writeHead(400, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('URL parametresi eksik');
      return;
    }
    try {
      const parsed = new URL(targetUrl);
      const client = parsed.protocol === 'https:' ? https : http;
      const proxyHeaders = {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 StreamMesh/1.0'
      };
      if (req.headers.range) {
        proxyHeaders.range = req.headers.range;
      }
      client.get(targetUrl, {
        headers: proxyHeaders
      }, (proxyRes) => {
        const responseHeaders = {
          'Content-Type': proxyRes.headers['content-type'] || 'application/vnd.apple.mpegurl',
          'Access-Control-Allow-Origin': '*',
          'Accept-Ranges': 'bytes'
        };
        if (proxyRes.headers['content-range']) {
          responseHeaders['Content-Range'] = proxyRes.headers['content-range'];
        }
        if (proxyRes.headers['content-length']) {
          responseHeaders['Content-Length'] = proxyRes.headers['content-length'];
        }
        res.writeHead(proxyRes.statusCode || 200, responseHeaders);
        proxyRes.pipe(res);
      }).on('error', (e) => {
        res.writeHead(502, { 'Content-Type': 'text/plain; charset=utf-8' });
        res.end('Proxy bağlantı hatası: ' + e.message);
      });
      return;
    } catch (e) {
      res.writeHead(400, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('Geçersiz URL');
      return;
    }
  }

  // 7. API: Playlist M3U Download
  if (pathname === '/playlist.m3u' || pathname === '/api/playlist.m3u') {
    const host = req.headers.host || `127.0.0.1:${PORT}`;
    const m3u = generateM3uContent(host);
    res.writeHead(200, {
      'Content-Type': 'application/x-mpegurl; charset=utf-8',
      'Content-Disposition': 'attachment; filename="StreamMesh.m3u"'
    });
    res.end(m3u);
    return;
  }

  // 8. Serve Main Web Application UI from /Web/index.html
  if (pathname === '/' || pathname === '/index.html' || pathname === '/web' || pathname === '/web/') {
    try {
      const indexPath = path.join(__dirname, 'Web', 'index.html');
      if (fs.existsSync(indexPath)) {
        const htmlContent = fs.readFileSync(indexPath, 'utf8');
        res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
        res.end(htmlContent);
        return;
      }
    } catch (e) {
      console.error('Web/index.html okuma hatası:', e);
    }
  }

  // 9. Static file fallback for /logos or /Web assets
  const staticPath = path.normalize(path.join(__dirname, pathname));
  if (staticPath.startsWith(__dirname) && fs.existsSync(staticPath) && fs.statSync(staticPath).isFile()) {
    const ext = path.extname(staticPath).toLowerCase();
    const mimeMap = {
      '.html': 'text/html; charset=utf-8',
      '.js': 'application/javascript; charset=utf-8',
      '.css': 'text/css; charset=utf-8',
      '.json': 'application/json; charset=utf-8',
      '.png': 'image/png',
      '.jpg': 'image/jpeg',
      '.svg': 'image/svg+xml',
      '.ico': 'image/x-icon'
    };
    res.writeHead(200, { 'Content-Type': mimeMap[ext] || 'application/octet-stream' });
    fs.createReadStream(staticPath).pipe(res);
    return;
  }

  res.writeHead(404, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify({ error: 'Endpoint bulunamadı', path: pathname }));
});

server.listen(PORT, '0.0.0.0', () => {
  console.log(`StreamMesh Hybrid Server running on http://0.0.0.0:${PORT}`);
});
