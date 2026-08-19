import http from 'http';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PORT = 3000;

function getAppVersion() {
  try {
    if (fs.existsSync(path.join(__dirname, 'version.txt'))) {
      return fs.readFileSync(path.join(__dirname, 'version.txt'), 'utf8').trim();
    }
  } catch (e) {}
  return '0.1.0';
}

// Comprehensive data store for TV, Movies, Series, Radio
const MEDIA_DATABASE = {
  live_tv: [
    {
      id: 'ch-trt1',
      name: 'TRT 1 HD',
      category: 'Ulusal',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-1-tr.png',
      url: 'https://tv-trt1.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      epgSchedule: [
        { startMinutes: 360, endMinutes: 570, title: 'Kudüs Fatihi Selahaddin Eyyubi (Tekrar)', desc: 'Tarihi dönem dizisi tekrar yayını.' },
        { startMinutes: 570, endMinutes: 780, title: 'Alişan ile Hayata Gülümse', desc: 'Canlı stüdyo konukları, yemek tarifleri ve müzik.' },
        { startMinutes: 780, endMinutes: 870, title: 'Gönül Dağı (Tekrar)', desc: 'Bozkırın ortasındaki samimi hayat hikayeleri.' },
        { startMinutes: 870, endMinutes: 1140, title: 'Alparslan: Büyük Selçuklu', desc: 'Büyük Selçuklu İmparatorluğu\'nun altın çağını anlatan tarihi macera.' },
        { startMinutes: 1140, endMinutes: 1200, title: 'Ana Haber Bülteni (Canlı)', desc: 'Günün tüm sıcak gelişmeleri, tarafsız haber bülteni.' },
        { startMinutes: 1200, endMinutes: 1410, title: 'Teşkilat (Yeni Bölüm)', desc: 'Milli İstihbarat Teşkilatı\'nın kahramanlık dolu operasyonları.' },
        { startMinutes: 1410, endMinutes: 1530, title: '3\'te 3 Tarih Yarışması', desc: 'Prof. Dr. Tufan Gündüz danışmanlığında tarih bilgi yarışması.' },
        { startMinutes: 1530, endMinutes: 1800, title: 'Gece Sineması Kuşağı', desc: 'Ödüllü yerli ve yabancı sinema filmleri.' }
      ]
    },
    {
      id: 'ch-trthaber',
      name: 'TRT Haber HD',
      category: 'Haber',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-haber-tr.png',
      url: 'https://tv-trthaber.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      epgSchedule: [
        { startMinutes: 360, endMinutes: 540, title: 'Dün Bugün & Manşetler', desc: 'Gazete manşetleri ve sabahın ilk sıcak gelişmeleri.' },
        { startMinutes: 540, endMinutes: 720, title: 'Haber Saati & Canlı Bağlantılar', desc: 'Ankara ve İstanbul stüdyolarından anlık gelişmeler.' },
        { startMinutes: 720, endMinutes: 780, title: 'Ekonomi 7/24', desc: 'Borsa, altın, döviz ve piyasa analizleri.' },
        { startMinutes: 780, endMinutes: 1020, title: 'Öğle Haberleri & Gündem', desc: 'Türkiye ve dünya gündeminin sıcak başlıkları.' },
        { startMinutes: 1020, endMinutes: 1140, title: 'Sıcak Nokta & Dış Politika', desc: 'Dünya diplomasisi ve bölgesel krizler.' },
        { startMinutes: 1140, endMinutes: 1260, title: 'Akşam Ana Haber', desc: 'Günün en önemli gelişmeleri ve özel dosyalar.' },
        { startMinutes: 1260, endMinutes: 1440, title: 'Stratejik Analiz & Tartışma', desc: 'Uzman konuklarla derinlemesine gündem değerlendirmesi.' },
        { startMinutes: 1440, endMinutes: 1800, title: 'Gece Bülteni & Dünya Raporu', desc: 'Gece yarısı haberleri ve dünya özetleri.' }
      ]
    },
    {
      id: 'ch-trtspor',
      name: 'TRT Spor HD',
      category: 'Spor',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-spor-tr.png',
      url: 'https://tv-trtspor1.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      epgSchedule: [
        { startMinutes: 420, endMinutes: 600, title: 'Sabah Sporu & Gazete Turu', desc: 'Spor basınının öne çıkan başlıkları ve transfer dedikoduları.' },
        { startMinutes: 600, endMinutes: 780, title: 'Spor Stüdyosu', desc: 'Süper Lig maç analizleri ve antrenman raporları.' },
        { startMinutes: 780, endMinutes: 960, title: 'Günün Maçları & Canlı Skor', desc: 'Voleybol, Basketbol ve Futbol karşılaşmaları.' },
        { startMinutes: 960, endMinutes: 1140, title: 'Spor Manşet (Canlı)', desc: 'Usta yorumcularla günün spor olayları.' },
        { startMinutes: 1140, endMinutes: 1380, title: 'Futbol Arenası & Maç Sonu', desc: 'Gecenin kritik pozisyonları ve canlı bağlantılar.' },
        { startMinutes: 1380, endMinutes: 1800, title: 'Özetler & Gece Sporu', desc: 'Haftanın tüm golleri ve nefes kesen özet görüntüleri.' }
      ]
    },
    {
      id: 'ch-trtbelgesel',
      name: 'TRT Belgesel HD',
      category: 'Belgesel',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-belgesel-tr.png',
      url: 'https://tv-trtbelgesel.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      epgSchedule: [
        { startMinutes: 360, endMinutes: 540, title: 'Doğadaki İnsan', desc: 'Serdar Kılıç ile doğada hayatta kalma ve geleneksel yaşam.' },
        { startMinutes: 540, endMinutes: 720, title: 'Usta Ellerin Masalı', desc: 'Kaybolmaya yüz tutmuş geleneksel zanaatların hikayesi.' },
        { startMinutes: 720, endMinutes: 900, title: 'Vahşi Yaşamın İzinde: Afrika', desc: 'Savananın yırtıcıları ve doğal döngü.' },
        { startMinutes: 900, endMinutes: 1080, title: 'Büyük Mühendislik Harikaları', desc: 'Dünyanın en zorlu mega inşaat projeleri.' },
        { startMinutes: 1080, endMinutes: 1260, title: 'Tarihin Efsaneleri', desc: 'Tarihe yön veren antik krallıklar ve komutanlar.' },
        { startMinutes: 1260, endMinutes: 1440, title: 'Aysel\'in Doğa Yolculuğu', desc: 'Kutup soğuklarından yağmur ormanlarına keşif.' },
        { startMinutes: 1440, endMinutes: 1800, title: 'Derin Uzay ve Kozmos', desc: 'Galaksiler, karadelikler ve evrenin gizemleri.' }
      ]
    },
    {
      id: 'ch-redbull',
      name: 'Red Bull TV',
      category: 'Spor',
      logo: 'https://upload.wikimedia.org/wikipedia/en/thumb/e/e4/Red_Bull_TV_logo.svg/320px-Red_Bull_TV_logo.svg.png',
      url: 'https://rbmn-live.akamaized.net/hls/live/590964/BoRB-AT/master.m3u8',
      quality: '1080p',
      type: 'video',
      epgSchedule: [
        { startMinutes: 0, endMinutes: 360, title: 'Rampage Freeride MTB Classics', desc: 'Extreme downhill mountain biking.' },
        { startMinutes: 360, endMinutes: 720, title: 'Cliff Diving World Series', desc: 'High adrenaline platform dives from 27 meters.' },
        { startMinutes: 720, endMinutes: 1080, title: 'F1 Track Stories & Pit Secrets', desc: 'Behind the scenes with championship drivers.' },
        { startMinutes: 1080, endMinutes: 1440, title: 'BC One Street Breakdance Battles', desc: 'World finals breakdance competitions.' }
      ]
    },
    {
      id: 'ch-nasa',
      name: 'NASA TV Live',
      category: 'Belgesel',
      logo: 'https://upload.wikimedia.org/wikipedia/commons/thumb/e/e5/NASA_logo.svg/300px-NASA_logo.svg.png',
      url: 'https://ntv1.akamaized.net/hls/live/2014075/NASA-NTV1-HLS/master.m3u8',
      quality: '720p',
      type: 'video',
      epgSchedule: [
        { startMinutes: 0, endMinutes: 480, title: 'ISS Earth Views from Orbit', desc: 'High definition real-time live cameras on Space Station.' },
        { startMinutes: 480, endMinutes: 960, title: 'Artemis Moon & Mars Science Briefing', desc: 'Lunar exploration milestones and astronaut science.' },
        { startMinutes: 960, endMinutes: 1440, title: 'James Webb Telescope Discoveries', desc: 'Deep cosmic imagery and galaxy formation.' }
      ]
    },
    {
      id: 'ch-trtworld',
      name: 'TRT World HD',
      category: 'Uluslararası',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-world-tr.png',
      url: 'https://tv-trtworld.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      epgSchedule: [
        { startMinutes: 0, endMinutes: 480, title: 'World News Headlines', desc: 'Global breaking stories and diplomatic updates.' },
        { startMinutes: 480, endMinutes: 960, title: 'The Newsmakers & Debate', desc: 'Key figures dissecting global affairs.' },
        { startMinutes: 960, endMinutes: 1440, title: 'Beyond the Headlines', desc: 'Documentary style reporting on human interest stories.' }
      ]
    },
    {
      id: 'ch-trtmuzik',
      name: 'TRT Müzik HD',
      category: 'Müzik',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-muzik-tr.png',
      url: 'https://tv-trtmuzik.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      epgSchedule: [
        { startMinutes: 360, endMinutes: 600, title: 'Sabahın Ezgileri & Akustik', desc: 'Enstrümantal huzur veren tınılar.' },
        { startMinutes: 600, endMinutes: 900, title: 'Gönülden Dile Türk Sanat Müziği', desc: 'Klasik besteler ve usta solistler.' },
        { startMinutes: 900, endMinutes: 1200, title: 'Türkülerle Anadolu Kuşağı', desc: 'Koro ve solo halk müziği konserleri.' },
        { startMinutes: 1200, endMinutes: 1440, title: 'Akşam Canlı Konser Kuşağı', desc: 'Canlı stüdyo performansı ve özel orkestra.' }
      ]
    },
    {
      id: 'ch-trtcocuk',
      name: 'TRT Çocuk HD',
      category: 'Çocuk',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-cocuk-tr.png',
      url: 'https://tv-trtcocuk.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      epgSchedule: [
        { startMinutes: 360, endMinutes: 600, title: 'Ege ile Gaga & Maysa ve Bulut', desc: 'Eğitici çizgi diziler.' },
        { startMinutes: 600, endMinutes: 900, title: 'Rafadan Tayfa Maceraları', desc: 'Mahalle arkadaşlığı ve nostaljik maceralar.' },
        { startMinutes: 900, endMinutes: 1200, title: 'Pırdino, Aslan & Kaptan Pengu', desc: 'Bilim, çevre bilinci ve keşif animasyonları.' },
        { startMinutes: 1200, endMinutes: 1440, title: 'İbi ile Tosi & Kare Takımı', desc: 'Matematik ve macera dolu dünyalar.' }
      ]
    }
  ],

  movies: [
    {
      id: 'mov-sintel',
      name: 'Sintel (Açık Kaynak Animasyon)',
      category: 'Animasyon / Macera',
      year: '2010',
      duration: '15 dk',
      rating: '8.4',
      director: 'Colin Levy',
      logo: 'https://upload.wikimedia.org/wikipedia/commons/thumb/8/8f/Sintel_poster.jpg/320px-Sintel_poster.jpg',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/Sintel.mp4',
      quality: '1080p',
      type: 'video',
      desc: 'Ejderha yavrusu Scales\'i kurtarmak için tehlikeli dağları aşan genç bir kızın duygu dolu hikayesi.'
    },
    {
      id: 'mov-bbb',
      name: 'Big Buck Bunny',
      category: 'Animasyon / Komedi',
      year: '2008',
      duration: '10 dk',
      rating: '8.1',
      director: 'Sacha Goedegebure',
      logo: 'https://upload.wikimedia.org/wikipedia/commons/thumb/c/c5/Big_buck_bunny_poster_big.jpg/320px-Big_buck_bunny_poster_big.jpg',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4',
      quality: '1080p',
      type: 'video',
      desc: 'Ormanın sevimli dev tavşanı Bunny, ormanı kirleten ve zorbalık yapan üç haylaz kemirgene unutamayacakları bir ders verir.'
    },
    {
      id: 'mov-tears',
      name: 'Tears of Steel (Sci-Fi)',
      category: 'Bilim Kurgu / Aksiyon',
      year: '2012',
      duration: '12 dk',
      rating: '7.6',
      director: 'Ian Hubert',
      logo: 'https://upload.wikimedia.org/wikipedia/commons/thumb/1/18/Tears_of_Steel_poster.jpg/320px-Tears_of_Steel_poster.jpg',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/TearsOfSteel.mp4',
      quality: '1080p',
      type: 'video',
      desc: 'Kıyamet sonrası distopik gelecekte, insanlığı yok olmaktan kurtarmak için geçmişteki bir aşk anısını yeniden canlandıran bilim insanları.'
    },
    {
      id: 'mov-elephants',
      name: 'Elephants Dream',
      category: 'Bilim Kurgu / Felsefe',
      year: '2006',
      duration: '11 dk',
      rating: '7.5',
      director: 'Bassam Kurdali',
      logo: 'https://upload.wikimedia.org/wikipedia/commons/thumb/f/f6/Elephants_Dream_poster.jpg/320px-Elephants_Dream_poster.jpg',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ElephantsDream.mp4',
      quality: '1080p',
      type: 'video',
      desc: 'Devasa karmaşık bir makinenin içinde gerçeklik ve algı sınırlarını sorgulayan iki karakterin fantastik yolculuğu.'
    }
  ],

  series: [
    {
      id: 'ser-alparslan',
      name: 'Alparslan: Büyük Selçuklu',
      category: 'Tarih / Macera',
      seasonCount: '2 Sezon',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-1-tr.png',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/WeAreGoingOnBullrun.mp4',
      quality: '1080p',
      type: 'video',
      episodes: [
        { episode: '1. Bölüm', title: 'Fetih Yolu', duration: '45 dk', desc: 'Anadolu kapılarını açacak büyük yürüyüş başlar.' },
        { episode: '2. Bölüm', title: 'Tuğrul Bey\'in Emaneti', duration: '48 dk', desc: 'Saraydaki entrikalar ve sınır boylarındaki çarpışma.' },
        { episode: '3. Bölüm', title: 'Vaspurakan Kuşatması', duration: '50 dk', desc: 'Bizans ordusuna karşı kurulan dahi strateji.' },
        { episode: '4. Bölüm', title: 'Malazgirt\'e Doğru', duration: '52 dk', desc: 'Tarihin seyrini değiştiren tarihi dönüm noktası.' }
      ]
    },
    {
      id: 'ser-gonul',
      name: 'Gönül Dağı',
      category: 'Dram / Komedi',
      seasonCount: '4 Sezon',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-1-tr.png',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerBlazes.mp4',
      quality: '1080p',
      type: 'video',
      episodes: [
        { episode: '1. Bölüm', title: 'Bozkırın Hayalleri', duration: '42 dk', desc: 'Amcaoğullarının uçak yapma hayali kasabayı ayağa kaldırır.' },
        { episode: '2. Bölüm', title: 'Dilek\'in Dönüşü', duration: '44 dk', desc: 'Yıllar sonra kasabaya dönen Dilek ve çocukluk aşkı.' },
        { episode: '3. Bölüm', title: 'Gedelli\'de Bahar', duration: '40 dk', desc: 'Kasabanın neşeli ve duygusal hikayeleri.' }
      ]
    },
    {
      id: 'ser-teskilat',
      name: 'Teşkilat',
      category: 'Aksiyon / İstihbarat',
      seasonCount: '4 Sezon',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-1-tr.png',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerEscapes.mp4',
      quality: '1080p',
      type: 'video',
      episodes: [
        { episode: '1. Bölüm', title: 'Görünmez Kahramanlar', duration: '55 dk', desc: 'Vatan için kendi hayatlarından vazgeçen özel ekip kuruluyor.' },
        { episode: '2. Bölüm', title: 'Sıcak Takip', duration: '50 dk', desc: 'Avrupa başkentlerindeki nefes kesen operasyon.' },
        { episode: '3. Bölüm', title: 'Köstebek', duration: '54 dk', desc: 'Şebekenin kalbine sızma görevi.' }
      ]
    }
  ],

  radios: [
    {
      id: 'rad-trtfm',
      name: 'TRT FM',
      category: 'Pop & Türkçe Müzik',
      freq: '91.4 MHz',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-fm-tr.png',
      url: 'https://listen.radionomy.com/radiodemo', // Fallback MP3/AAC
      quality: '320 kbps',
      type: 'audio',
      currentShow: 'Canlı Yayın - Yol Manzaraları & Popüler Şarkılar',
      nextShow: 'Akşam Kuşağı İstekler'
    },
    {
      id: 'rad-trtradyo1',
      name: 'TRT Radyo 1',
      category: 'Kültür & Haber',
      freq: '89.0 MHz',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-radyo-1-tr.png',
      url: 'https://listen.radionomy.com/radiodemo2',
      quality: '256 kbps',
      type: 'audio',
      currentShow: 'Günün Raporu & Radyo Tiyatrosu',
      nextShow: 'Bilim ve Toplum Kuşağı'
    },
    {
      id: 'rad-trtradyo3',
      name: 'TRT Radyo 3',
      category: 'Klasik Müzik & Caz',
      freq: '96.2 MHz',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-radyo-3-tr.png',
      url: 'https://listen.radionomy.com/radiodemo3',
      quality: '320 kbps',
      type: 'audio',
      currentShow: 'Senfoni & Dünya Caz Klasikleri',
      nextShow: 'Barok Dönem Eserleri'
    },
    {
      id: 'rad-trtnagme',
      name: 'TRT Nağme',
      category: 'Türk Sanat Müziği',
      freq: '101.8 MHz',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-nagme-tr.png',
      url: 'https://listen.radionomy.com/radiodemo4',
      quality: '256 kbps',
      type: 'audio',
      currentShow: 'Gönül Nağmeleri & Fasıl Heyeti',
      nextShow: 'Unutulmayan Bestekarlar'
    },
    {
      id: 'rad-trtturku',
      name: 'TRT Türkü',
      category: 'Halk Müziği',
      freq: '99.4 MHz',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-turku-tr.png',
      url: 'https://listen.radionomy.com/radiodemo5',
      quality: '256 kbps',
      type: 'audio',
      currentShow: 'Bozkırın Sesi & Yöresel Türküler',
      nextShow: 'Aşıkların Dilinden'
    }
  ]
};

const server = http.createServer((req, res) => {
  const host = req.headers.host || `localhost:${PORT}`;
  const parsedUrl = new URL(req.url, `http://${host}`);
  const pathname = parsedUrl.pathname;

  // CORS headers
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  // API: Version
  if (pathname === '/api/version') {
    const version = getAppVersion();
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ version, status: 'online', engine: 'StreamMesh Hybrid' }));
    return;
  }

  // API: Media by Module
  if (pathname === '/api/media') {
    const module = parsedUrl.searchParams.get('module') || 'live_tv';
    const data = MEDIA_DATABASE[module] || MEDIA_DATABASE.live_tv;
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ module, items: data, count: data.length }));
    return;
  }

  // API: Dynamic M3U Playlist Download
  if (pathname === '/api/playlist.m3u' || pathname === '/playlist.m3u') {
    let m3u = '#EXTM3U\n';
    MEDIA_DATABASE.live_tv.forEach(c => {
      m3u += `#EXTINF:-1 tvg-id="${c.id}" tvg-name="${c.name}" tvg-logo="${c.logo}" group-title="${c.category}",${c.name}\n${c.url}\n`;
    });
    res.writeHead(200, {
      'Content-Type': 'application/x-mpegURL',
      'Content-Disposition': 'attachment; filename="StreamMesh_All.m3u"'
    });
    res.end(m3u);
    return;
  }

  const version = getAppVersion();

  const html = `<!DOCTYPE html>
<html lang="tr">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>StreamMesh Web Player & Media Center - v${version}</title>
    <!-- HLS.js for Live Stream Playback -->
    <script src="https://cdn.jsdelivr.net/npm/hls.js@latest"></script>
    <style>
        :root {
            --bg-base: #0a0c10;
            --bg-surface: #12151c;
            --bg-card: #191d26;
            --bg-hover: #232936;
            --primary: #0284c7;
            --primary-glow: #38bdf8;
            --accent: #6366f1;
            --text-main: #f8fafc;
            --text-muted: #94a3b8;
            --border: #242938;
            --live-red: #ef4444;
            --success: #10b981;
        }

        * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
        body { background-color: var(--bg-base); color: var(--text-main); height: 100vh; display: flex; flex-direction: column; overflow: hidden; }

        /* Top Header */
        .top-nav {
            background: var(--bg-surface);
            border-bottom: 1px solid var(--border);
            padding: 8px 18px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            z-index: 50;
        }
        .brand-section { display: flex; align-items: center; gap: 10px; }
        .logo-icon {
            width: 36px; height: 36px;
            display: flex; align-items: center; justify-content: center;
            filter: drop-shadow(0 0 8px rgba(56, 189, 248, 0.4));
        }
        .logo-icon svg { width: 100%; height: 100%; }
        .brand-title { font-size: 17px; font-weight: 800; }
        .brand-title span { color: var(--primary-glow); }
        .version-badge {
            background: rgba(56, 189, 248, 0.12);
            color: var(--primary-glow);
            border: 1px solid rgba(56, 189, 248, 0.25);
            font-size: 11px;
            font-weight: 700;
            padding: 2px 7px;
            border-radius: 12px;
        }

        /* 4 Main Module Tabs (Canlı TV, Film, Dizi, Radyo) */
        .nav-tabs { display: flex; gap: 6px; background: rgba(0,0,0,0.3); padding: 4px; border-radius: 10px; border: 1px solid var(--border); }
        .nav-tab {
            background: transparent;
            border: 1px solid transparent;
            color: var(--text-muted);
            padding: 7px 16px;
            border-radius: 7px;
            font-size: 13px;
            font-weight: 700;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 7px;
            transition: all 0.15s ease;
        }
        .nav-tab:hover { color: #fff; background: var(--bg-card); }
        .nav-tab.active {
            background: var(--primary);
            color: #fff;
            box-shadow: 0 2px 8px rgba(2, 132, 199, 0.4);
        }

        .top-actions { display: flex; align-items: center; gap: 8px; }
        .action-btn {
            background: var(--bg-card);
            border: 1px solid var(--border);
            color: var(--text-main);
            padding: 7px 12px;
            border-radius: 7px;
            font-size: 12px;
            font-weight: 600;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 6px;
            transition: all 0.15s;
            text-decoration: none;
        }
        .action-btn:hover { background: var(--bg-hover); border-color: var(--primary-glow); }
        .action-btn.primary {
            background: linear-gradient(135deg, var(--primary), var(--accent));
            border: none;
            color: #fff;
        }

        /* Layout Grid */
        .main-container {
            display: grid;
            grid-template-columns: 340px 1fr 340px;
            flex: 1;
            height: calc(100vh - 51px);
            overflow: hidden;
        }

        @media (max-width: 1200px) {
            .main-container { grid-template-columns: 300px 1fr; }
            .right-panel { display: none !important; }
        }
        @media (max-width: 768px) {
            .main-container { grid-template-columns: 1fr; }
            .left-sidebar { display: none; }
        }

        /* Left Sidebar */
        .left-sidebar {
            background: var(--bg-surface);
            border-right: 1px solid var(--border);
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }
        .sidebar-header { padding: 12px 14px; border-bottom: 1px solid var(--border); }
        .search-box {
            position: relative;
            margin-bottom: 8px;
        }
        .search-box input {
            width: 100%;
            background: var(--bg-card);
            border: 1px solid var(--border);
            padding: 8px 12px 8px 32px;
            border-radius: 7px;
            color: #fff;
            font-size: 13px;
            outline: none;
        }
        .search-box input:focus { border-color: var(--primary-glow); }
        .search-icon { position: absolute; left: 10px; top: 9px; font-size: 12px; color: var(--text-muted); }

        .category-chips {
            display: flex;
            gap: 5px;
            overflow-x: auto;
            padding-bottom: 3px;
            scrollbar-width: none;
        }
        .category-chips::-webkit-scrollbar { display: none; }
        .cat-chip {
            white-space: nowrap;
            background: var(--bg-card);
            border: 1px solid var(--border);
            color: var(--text-muted);
            font-size: 11px;
            font-weight: 600;
            padding: 3px 9px;
            border-radius: 12px;
            cursor: pointer;
        }
        .cat-chip.active { background: rgba(56, 189, 248, 0.15); color: var(--primary-glow); border-color: var(--primary); }

        .media-list {
            flex: 1;
            overflow-y: auto;
            padding: 8px;
            display: flex;
            flex-direction: column;
            gap: 5px;
        }
        .media-item {
            background: var(--bg-card);
            border: 1px solid transparent;
            padding: 10px 12px;
            border-radius: 8px;
            display: flex;
            align-items: center;
            gap: 12px;
            cursor: pointer;
            transition: all 0.15s;
        }
        .media-item:hover { background: var(--bg-hover); border-color: rgba(56, 189, 248, 0.3); }
        .media-item.active {
            background: rgba(2, 132, 199, 0.18);
            border-color: var(--primary);
        }
        .media-logo-wrap {
            width: 44px; height: 44px;
            background: #000;
            border-radius: 8px;
            display: flex; align-items: center; justify-content: center;
            overflow: hidden;
            border: 1px solid var(--border);
            flex-shrink: 0;
        }
        .media-logo-wrap img { width: 100%; height: 100%; object-fit: contain; }
        .media-meta { flex: 1; min-width: 0; }
        .media-title {
            font-size: 13px; font-weight: 700; color: #fff;
            display: flex; justify-content: space-between; align-items: center;
            margin-bottom: 3px;
        }
        .quality-tag {
            font-size: 10px; background: #0f172a; color: var(--primary-glow);
            padding: 1px 5px; border-radius: 4px; border: 1px solid rgba(56, 189, 248, 0.2);
        }
        .media-sub {
            font-size: 11px; color: var(--text-muted);
            white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
        }

        /* Center Player Workspace */
        .player-workspace {
            display: flex;
            flex-direction: column;
            background: #000;
            position: relative;
            overflow-y: auto;
        }
        .video-container {
            position: relative;
            background: #000;
            width: 100%;
            aspect-ratio: 16 / 9;
            max-height: 62vh;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        video {
            width: 100%;
            height: 100%;
            background: #000;
            object-fit: contain;
        }
        
        /* Audio Radio Mode Visualizer */
        .radio-visualizer {
            display: none;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            width: 100%;
            height: 100%;
            background: radial-gradient(circle at center, #1e293b 0%, #020617 100%);
            gap: 16px;
        }
        .radio-pulse-circle {
            width: 120px; height: 120px;
            border-radius: 50%;
            background: rgba(56, 189, 248, 0.1);
            border: 2px solid var(--primary-glow);
            display: flex; align-items: center; justify-content: center;
            box-shadow: 0 0 35px rgba(56, 189, 248, 0.3);
            animation: radioGlow 2s infinite alternate;
        }
        @keyframes radioGlow {
            0% { transform: scale(0.96); box-shadow: 0 0 20px rgba(56, 189, 248, 0.2); }
            100% { transform: scale(1.04); box-shadow: 0 0 45px rgba(56, 189, 248, 0.5); }
        }
        .radio-pulse-circle img { width: 70px; height: 70px; object-fit: contain; }

        .video-overlay-info {
            position: absolute;
            top: 14px; left: 14px;
            display: flex; align-items: center; gap: 8px;
            background: rgba(0,0,0,0.7); backdrop-filter: blur(8px);
            padding: 5px 12px; border-radius: 20px;
            border: 1px solid rgba(255,255,255,0.12);
            pointer-events: none;
        }
        .live-dot {
            width: 8px; height: 8px; border-radius: 50%;
            background: var(--live-red);
            box-shadow: 0 0 8px var(--live-red);
            animation: pulse 1.5s infinite;
        }
        @keyframes pulse { 0% { opacity: 1; } 50% { opacity: 0.4; } 100% { opacity: 1; } }

        .player-controls-bar {
            background: var(--bg-surface);
            border-bottom: 1px solid var(--border);
            padding: 12px 18px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-wrap: wrap;
            gap: 10px;
        }
        .playing-media-desc h2 { font-size: 16px; font-weight: 700; color: #fff; }
        .playing-media-desc p { font-size: 12px; color: var(--text-muted); margin-top: 2px; }

        .player-actions { display: flex; gap: 6px; }
        .ctrl-btn {
            background: var(--bg-card);
            border: 1px solid var(--border);
            color: #fff;
            padding: 6px 12px;
            border-radius: 6px;
            font-size: 12px;
            font-weight: 600;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 5px;
        }
        .ctrl-btn:hover { background: var(--bg-hover); border-color: var(--primary-glow); }

        .stream-details-panel {
            padding: 16px;
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 12px;
            background: var(--bg-base);
        }
        .metric-card {
            background: var(--bg-surface);
            border: 1px solid var(--border);
            padding: 12px 14px;
            border-radius: 8px;
        }
        .metric-card span { font-size: 11px; color: var(--text-muted); text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; }
        .metric-card h4 { font-size: 13px; font-weight: 700; margin-top: 4px; color: var(--text-main); word-break: break-all; }

        /* Right Panel: Dynamic Realtime EPG & Episodes */
        .right-panel {
            background: var(--bg-surface);
            border-left: 1px solid var(--border);
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }
        .panel-header {
            padding: 12px 14px;
            border-bottom: 1px solid var(--border);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .panel-header h3 { font-size: 13px; font-weight: 700; display: flex; align-items: center; gap: 6px; }
        .current-clock { font-size: 12px; color: var(--primary-glow); font-weight: 700; font-variant-numeric: tabular-nums; }

        .epg-timeline {
            flex: 1;
            overflow-y: auto;
            padding: 12px;
            display: flex;
            flex-direction: column;
            gap: 8px;
        }
        .epg-card {
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 10px 12px;
            position: relative;
        }
        .epg-card.active-show {
            border-color: var(--primary);
            background: rgba(2, 132, 199, 0.12);
            box-shadow: inset 0 0 10px rgba(56, 189, 248, 0.1);
        }
        .epg-card.past-show {
            opacity: 0.55;
        }
        .epg-time {
            font-size: 11px; font-weight: 700; color: var(--primary-glow);
            display: flex; justify-content: space-between; margin-bottom: 4px;
        }
        .epg-title { font-size: 12px; font-weight: 700; margin-bottom: 3px; color: #fff; }
        .epg-desc { font-size: 11px; color: var(--text-muted); line-height: 1.35; }
        .epg-progress-bar {
            width: 100%; height: 3px; background: rgba(255,255,255,0.1);
            border-radius: 2px; margin-top: 6px; overflow: hidden;
        }
        .epg-progress-fill { height: 100%; width: 50%; background: var(--primary-glow); }

        /* Modal */
        .modal-backdrop {
            position: fixed; inset: 0; background: rgba(0,0,0,0.75);
            display: none; align-items: center; justify-content: center; z-index: 100;
        }
        .modal-content {
            background: var(--bg-surface);
            border: 1px solid var(--border);
            border-radius: 12px;
            width: 90%; max-width: 460px;
            padding: 20px;
        }
        .modal-title { font-size: 15px; font-weight: 700; margin-bottom: 14px; display: flex; justify-content: space-between; }
        .form-group { margin-bottom: 12px; }
        .form-group label { display: block; font-size: 11px; font-weight: 600; color: var(--text-muted); margin-bottom: 5px; }
        .form-group input, .form-group select {
            width: 100%; background: var(--bg-card); border: 1px solid var(--border);
            padding: 8px 10px; border-radius: 6px; color: #fff; font-size: 12px; outline: none;
        }
        .modal-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; }

        /* Toast notification */
        .toast-notification {
            position: fixed;
            bottom: 24px;
            right: 24px;
            background: #1e293b;
            color: #f8fafc;
            border: 1px solid var(--primary-glow);
            padding: 10px 18px;
            border-radius: 8px;
            font-size: 13px;
            font-weight: 600;
            box-shadow: 0 4px 16px rgba(0,0,0,0.5);
            z-index: 9999;
            transform: translateY(100px);
            opacity: 0;
            transition: all 0.25s ease-in-out;
            pointer-events: none;
        }
        .toast-notification.show {
            transform: translateY(0);
            opacity: 1;
        }
    </style>
</head>
<body>

    <!-- Toast Notification Element -->
    <div id="toastNotification" class="toast-notification"></div>

    <!-- Header Navigation -->
    <header class="top-nav">
        <div class="brand-section">
            <div class="logo-icon">
                <svg viewBox="0 0 256 256" xmlns="http://www.w3.org/2000/svg">
                    <defs>
                        <linearGradient id="iconGrad" x1="0%" y1="0%" x2="0%" y2="100%">
                            <stop offset="0%" style="stop-color:#0284c7;stop-opacity:1" />
                            <stop offset="100%" style="stop-color:#0f172a;stop-opacity:1" />
                        </linearGradient>
                    </defs>
                    <rect width="256" height="256" rx="60" fill="#0a0c10"/>
                    <circle cx="128" cy="128" r="70" fill="url(#iconGrad)" stroke="#38bdf8" stroke-width="4"/>
                    <polygon points="110,95 110,161 160,128" fill="white"/>
                    <circle cx="60" cy="60" r="15" fill="#38bdf8"/>
                    <line x1="60" y1="60" x2="90" y2="90" stroke="#38bdf8" stroke-width="4"/>
                    <circle cx="196" cy="60" r="15" fill="#6366f1"/>
                    <line x1="196" y1="60" x2="166" y2="90" stroke="#6366f1" stroke-width="4"/>
                </svg>
            </div>
            <div class="brand-title">Stream<span>Mesh</span></div>
            <span class="version-badge">v${version} Live</span>
        </div>

        <!-- 4 Core Functional Modules -->
        <nav class="nav-tabs">
            <button class="nav-tab active" id="tab-live_tv" onclick="switchModule('live_tv')">📺 Canlı TV</button>
            <button class="nav-tab" id="tab-movies" onclick="switchModule('movies')">🎬 Film</button>
            <button class="nav-tab" id="tab-series" onclick="switchModule('series')">🍿 Dizi</button>
            <button class="nav-tab" id="tab-radios" onclick="switchModule('radios')">📻 Radyo</button>
        </nav>

        <div class="top-actions">
            <button class="action-btn" onclick="openAddModal()">➕ Kaynak Ekle</button>
            <a class="action-btn primary" href="/api/playlist.m3u" download="StreamMesh.m3u">📥 M3U İndir</a>
        </div>
    </header>

    <!-- Main Workspace -->
    <main class="main-container">
        
        <!-- Left Sidebar: Channels / VOD Items -->
        <aside class="left-sidebar">
            <div class="sidebar-header">
                <div class="search-box">
                    <span class="search-icon">🔍</span>
                    <input type="text" id="searchInput" placeholder="İçerik ara..." oninput="renderMediaList()">
                </div>
                <div class="category-chips" id="categoryChips">
                    <!-- Populated dynamically per module -->
                </div>
            </div>

            <div class="media-list" id="mediaListContainer">
                <!-- Dynamically Populated -->
            </div>
        </aside>

        <!-- Center Workspace: Player -->
        <section class="player-workspace">
            <div class="video-container">
                <video id="videoPlayer" controls autoplay playsinline></video>
                
                <!-- Radio Visualizer mode -->
                <div class="radio-visualizer" id="radioVisualizer">
                    <div class="radio-pulse-circle">
                        <img id="radioLogo" src="https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-fm-tr.png" alt="Radio">
                    </div>
                    <h3 id="radioStationTitle" style="font-size: 18px; font-weight: 700; color: #fff;">TRT FM</h3>
                    <p id="radioFreqBadge" style="font-size: 12px; color: var(--primary-glow); font-weight: 600;">91.4 MHz - 320 kbps Canlı Ses</p>
                </div>

                <div class="video-overlay-info" id="playerOverlay">
                    <div class="live-dot"></div>
                    <span id="overlayMediaTitle" style="font-size:12px; font-weight:700;">TRT 1 HD</span>
                    <span class="quality-tag" id="overlayQuality">1080p</span>
                </div>
            </div>

            <div class="player-controls-bar">
                <div class="playing-media-desc">
                    <h2 id="currentMediaHeader">TRT 1 HD</h2>
                    <p id="currentMediaSub">Canlı Yayın Akışı</p>
                </div>

                <div class="player-actions">
                    <button class="ctrl-btn" onclick="reloadCurrentMedia()">🔄 Yenile</button>
                    <button class="ctrl-btn" onclick="copyStreamUrl()">📋 URL Kopyala</button>
                    <button class="ctrl-btn" onclick="openExternal()">🚀 Harici Oynat</button>
                    <button class="ctrl-btn" onclick="togglePiP()">🪟 PiP</button>
                </div>
            </div>

            <div class="stream-details-panel">
                <div class="metric-card">
                    <span>Yayın Modu & Protokol</span>
                    <h4 id="streamProtocolLabel">HLS Canlı Akış (m3u8)</h4>
                </div>
                <div class="metric-card">
                    <span>Masaüstü LibVLC / AceStream</span>
                    <h4>StreamMesh Native Node v${version}</h4>
                </div>
                <div class="metric-card">
                    <span>Kategori / Tür</span>
                    <h4 id="streamCategoryLabel">Ulusal TV</h4>
                </div>
                <div class="metric-card">
                    <span>Kaynak Bağlantısı</span>
                    <h4 id="streamSourceUrl">https://tv-trt1.medya.trt.com.tr/master.m3u8</h4>
                </div>
            </div>
        </section>

        <!-- Right Panel: Realtime Dynamic EPG / Info / Episodes -->
        <aside class="right-panel">
            <div class="panel-header">
                <h3 id="rightPanelTitle">📅 Gerçek Zamanlı EPG</h3>
                <span class="current-clock" id="liveClock">--:--:--</span>
            </div>

            <div class="epg-timeline" id="epgTimelineContainer">
                <!-- Dynamically Loaded EPG / Episode Timeline -->
            </div>
        </aside>

    </main>

    <!-- Modal Custom Media -->
    <div class="modal-backdrop" id="addModal">
        <div class="modal-content">
            <div class="modal-title">
                <span>➕ Özel Yayın Linki Ekle</span>
                <span style="cursor:pointer;" onclick="closeAddModal()">✕</span>
            </div>
            <div class="form-group">
                <label>Başlık / Kanal Adı</label>
                <input type="text" id="customName" placeholder="Örn: Özel Akış TV">
            </div>
            <div class="form-group">
                <label>Yayın URL (m3u8, mp4 veya mp3)</label>
                <input type="text" id="customUrl" placeholder="https://domain.com/live.m3u8">
            </div>
            <div class="form-group">
                <label>Modül Türü</label>
                <select id="customModule">
                    <option value="live_tv">Canlı TV</option>
                    <option value="movies">Film</option>
                    <option value="series">Dizi</option>
                    <option value="radios">Radyo</option>
                </select>
            </div>
            <div class="form-group">
                <label>Kategori</label>
                <input type="text" id="customCategory" placeholder="Örn: Ulusal, Spor, Müzik">
            </div>
            <div class="modal-actions">
                <button class="action-btn" onclick="closeAddModal()">İptal</button>
                <button class="action-btn primary" onclick="saveCustomMedia()">Ekle ve Başlat</button>
            </div>
        </div>
    </div>

    <script>
        const DB = ${JSON.stringify(MEDIA_DATABASE)};
        const FALLBACK_LOGO = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='40' height='40'%3E%3Crect width='40' height='40' fill='%23222'/%3E%3Ctext x='20' y='25' font-size='12' fill='%23888' text-anchor='middle'%3ESM%3C/text%3E%3C/svg%3E";
        let currentModule = 'live_tv';
        let currentCategory = 'Tümü';
        let activeMedia = DB.live_tv[0];
        let hlsInstance = null;
        let toastTimeout = null;

        const video = document.getElementById('videoPlayer');
        const radioVisualizer = document.getElementById('radioVisualizer');

        function showToast(message) {
            const toast = document.getElementById('toastNotification');
            if (!toast) return;
            toast.innerText = message;
            toast.classList.add('show');
            if (toastTimeout) clearTimeout(toastTimeout);
            toastTimeout = setTimeout(() => {
                toast.classList.remove('show');
            }, 3000);
        }

        // Clock & Realtime updater
        function startLiveClock() {
            function update() {
                const now = new Date();
                const timeStr = now.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
                document.getElementById('liveClock').innerText = timeStr;
            }
            setInterval(update, 1000);
            update();
        }

        function switchModule(mod) {
            currentModule = mod;
            currentCategory = 'Tümü';

            document.querySelectorAll('.nav-tab').forEach(t => t.classList.remove('active'));
            const tabBtn = document.getElementById('tab-' + mod);
            if (tabBtn) tabBtn.classList.add('active');

            // Render category chips
            renderCategoryChips();

            // Select first item
            const items = DB[mod] || [];
            if (items.length > 0) {
                renderMediaList();
                playMedia(items[0]);
            }
        }

        function renderCategoryChips() {
            const container = document.getElementById('categoryChips');
            const items = DB[currentModule] || [];
            const categories = ['Tümü', ...new Set(items.map(i => i.category))];

            container.innerHTML = '';
            categories.forEach(cat => {
                const chip = document.createElement('div');
                chip.className = 'cat-chip' + (currentCategory === cat ? ' active' : '');
                chip.innerText = cat;
                chip.onclick = () => {
                    currentCategory = cat;
                    document.querySelectorAll('.cat-chip').forEach(c => c.classList.remove('active'));
                    chip.classList.add('active');
                    renderMediaList();
                };
                container.appendChild(chip);
            });
        }

        function renderMediaList() {
            const container = document.getElementById('mediaListContainer');
            const search = document.getElementById('searchInput').value.toLowerCase().trim();
            const items = DB[currentModule] || [];

            const filtered = items.filter(m => {
                const matchesCat = (currentCategory === 'Tümü') || (m.category.toLowerCase() === currentCategory.toLowerCase());
                const matchesSearch = m.name.toLowerCase().includes(search) || m.category.toLowerCase().includes(search);
                return matchesCat && matchesSearch;
            });

            container.innerHTML = '';
            filtered.forEach(m => {
                const isActive = activeMedia && activeMedia.id === m.id;
                const item = document.createElement('div');
                item.className = 'media-item' + (isActive ? ' active' : '');
                item.onclick = () => playMedia(m);

                const logoSrc = m.logo || 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-1-tr.png';
                
                let subText = m.category;
                if (currentModule === 'live_tv') {
                    const realtimeShow = getRealtimeLiveShow(m);
                    subText = realtimeShow ? realtimeShow.title : m.category;
                } else if (currentModule === 'movies') {
                    subText = m.year + ' • ' + m.duration + ' • ⭐ ' + m.rating;
                } else if (currentModule === 'series') {
                    subText = m.seasonCount + ' • ' + (m.episodes ? m.episodes.length + ' Bölüm' : '');
                } else if (currentModule === 'radios') {
                    subText = m.freq + ' • ' + (m.currentShow || 'Canlı Radyo');
                }

                item.innerHTML = 
                    '<div class="media-logo-wrap">' +
                        '<img src="' + logoSrc + '" alt="' + m.name + '" onerror="this.onerror=null;this.src=FALLBACK_LOGO" />' +
                    '</div>' +
                    '<div class="media-meta">' +
                        '<div class="media-title">' +
                            '<span>' + m.name + '</span>' +
                            '<span class="quality-tag">' + (m.quality || 'HD') + '</span>' +
                        '</div>' +
                        '<div class="media-sub">' + subText + '</div>' +
                    '</div>';
                container.appendChild(item);
            });
        }

        // Get currently airing program based on current time (minutes of day)
        function getRealtimeLiveShow(channel) {
            if (!channel.epgSchedule || channel.epgSchedule.length === 0) return null;
            const now = new Date();
            const currentMinutes = now.getHours() * 60 + now.getMinutes();

            for (const item of channel.epgSchedule) {
                // If schedule spans across midnight
                if (item.startMinutes <= item.endMinutes) {
                    if (currentMinutes >= item.startMinutes && currentMinutes < item.endMinutes) {
                        return item;
                    }
                } else {
                    if (currentMinutes >= item.startMinutes || currentMinutes < item.endMinutes) {
                        return item;
                    }
                }
            }
            return channel.epgSchedule[0];
        }

        function formatMinutes(min) {
            const normalized = ((min % 1440) + 1440) % 1440;
            const h = Math.floor(normalized / 60);
            const m = normalized % 60;
            return String(h).padStart(2, '0') + ':' + String(m).padStart(2, '0');
        }

        function playMedia(m) {
            activeMedia = m;
            document.getElementById('currentMediaHeader').innerText = m.name;
            document.getElementById('overlayMediaTitle').innerText = m.name;
            document.getElementById('overlayQuality').innerText = m.quality || 'HD';
            document.getElementById('streamCategoryLabel').innerText = m.category;
            document.getElementById('streamSourceUrl').innerText = m.url;

            // Handle Video vs Radio Mode
            if (currentModule === 'radios') {
                video.style.display = 'none';
                radioVisualizer.style.display = 'flex';
                const radioImg = document.getElementById('radioLogo');
                radioImg.onerror = function() { this.onerror = null; this.src = FALLBACK_LOGO; };
                radioImg.src = m.logo;
                document.getElementById('radioStationTitle').innerText = m.name;
                document.getElementById('radioFreqBadge').innerText = m.freq + ' • ' + m.quality + ' Canlı Ses Akışı';
                document.getElementById('streamProtocolLabel').innerText = 'Icecast / Direct Audio Stream';
                document.getElementById('currentMediaSub').innerText = m.currentShow || 'Canlı Radyo Yayını';
            } else {
                video.style.display = 'block';
                radioVisualizer.style.display = 'none';
                document.getElementById('streamProtocolLabel').innerText = m.url.includes('.m3u8') ? 'HLS Canlı Akış (m3u8)' : 'MP4 Direct Stream';

                if (currentModule === 'live_tv') {
                    const currentShow = getRealtimeLiveShow(m);
                    document.getElementById('currentMediaSub').innerText = currentShow ? ('Canlı: ' + currentShow.title) : 'Canlı Yayın';
                } else if (currentModule === 'movies') {
                    document.getElementById('currentMediaSub').innerText = m.category + ' • ' + m.year + ' • Yönetmen: ' + (m.director || 'N/A');
                } else if (currentModule === 'series') {
                    document.getElementById('currentMediaSub').innerText = m.category + ' • ' + m.seasonCount;
                }
            }

            renderMediaList();
            renderRightPanel(m);

            // Stream Loading
            if (m.url.includes('.m3u8')) {
                if (Hls.isSupported()) {
                    if (hlsInstance) hlsInstance.destroy();
                    hlsInstance = new Hls({ enableWorker: true, lowLatencyMode: true });
                    hlsInstance.loadSource(m.url);
                    hlsInstance.attachMedia(video);
                    hlsInstance.on(Hls.Events.MANIFEST_PARSED, () => video.play().catch(() => {}));
                } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
                    video.src = m.url;
                    video.play().catch(() => {});
                }
            } else {
                if (hlsInstance) { hlsInstance.destroy(); hlsInstance = null; }
                video.src = m.url;
                video.play().catch(() => {});
            }
        }

        function renderRightPanel(m) {
            const container = document.getElementById('epgTimelineContainer');
            const titleEl = document.getElementById('rightPanelTitle');
            container.innerHTML = '';

            if (currentModule === 'live_tv') {
                titleEl.innerHTML = '📅 Gerçek Zamanlı EPG';
                const schedule = m.epgSchedule || [];
                const now = new Date();
                const currentMinutes = now.getHours() * 60 + now.getMinutes();

                schedule.forEach(item => {
                    let isCurrent = false;
                    let isPast = false;

                    if (item.startMinutes <= item.endMinutes) {
                        isCurrent = (currentMinutes >= item.startMinutes && currentMinutes < item.endMinutes);
                        isPast = currentMinutes >= item.endMinutes;
                    } else {
                        isCurrent = (currentMinutes >= item.startMinutes || currentMinutes < item.endMinutes);
                        isPast = currentMinutes >= item.endMinutes && currentMinutes < item.startMinutes;
                    }

                    // Calculate real-time progress percentage
                    let progressPercent = 0;
                    if (isCurrent) {
                        const totalDuration = item.endMinutes - item.startMinutes;
                        const elapsed = currentMinutes - item.startMinutes;
                        progressPercent = Math.min(100, Math.max(5, Math.floor((elapsed / totalDuration) * 100)));
                    }

                    const card = document.createElement('div');
                    card.className = 'epg-card' + (isCurrent ? ' active-show' : '') + (isPast ? ' past-show' : '');
                    
                    const timeRange = formatMinutes(item.startMinutes) + ' - ' + formatMinutes(item.endMinutes);

                    card.innerHTML = 
                        '<div class="epg-time">' +
                            '<span>' + timeRange + '</span>' +
                            (isCurrent ? '<span style="color:var(--live-red); font-weight:800; animation:pulse 1s infinite;">● CANLI YAYINDA</span>' : '') +
                        '</div>' +
                        '<div class="epg-title">' + item.title + '</div>' +
                        '<div class="epg-desc">' + (item.desc || '') + '</div>' +
                        (isCurrent ? ('<div class="epg-progress-bar"><div class="epg-progress-fill" style="width:' + progressPercent + '%"></div></div>') : '');
                    container.appendChild(card);
                });
            } else if (currentModule === 'movies') {
                titleEl.innerHTML = '🎬 Film Detayları & Bilgi';
                const card = document.createElement('div');
                card.className = 'epg-card active-show';
                card.innerHTML = 
                    '<div class="epg-title" style="font-size:14px; margin-bottom:8px;">' + m.name + '</div>' +
                    '<div class="epg-desc" style="margin-bottom:10px;">' + (m.desc || '') + '</div>' +
                    '<div style="font-size:12px; color:var(--primary-glow); margin-bottom:4px;"><b>Yıl:</b> ' + m.year + '</div>' +
                    '<div style="font-size:12px; color:var(--primary-glow); margin-bottom:4px;"><b>Süre:</b> ' + m.duration + '</div>' +
                    '<div style="font-size:12px; color:var(--primary-glow); margin-bottom:4px;"><b>Yönetmen:</b> ' + m.director + '</div>' +
                    '<div style="font-size:12px; color:#fbbf24;"><b>IMDb / Puan:</b> ⭐ ' + m.rating + '</div>';
                container.appendChild(card);
            } else if (currentModule === 'series') {
                titleEl.innerHTML = '🍿 Bölüm Listesi';
                (m.episodes || []).forEach(ep => {
                    const card = document.createElement('div');
                    card.className = 'epg-card';
                    card.style.cursor = 'pointer';
                    card.onclick = () => {
                        showToast(ep.episode + ': ' + ep.title + ' seçildi');
                    };
                    card.innerHTML = 
                        '<div class="epg-time"><span>' + ep.episode + '</span><span>' + ep.duration + '</span></div>' +
                        '<div class="epg-title">' + ep.title + '</div>' +
                        '<div class="epg-desc">' + ep.desc + '</div>';
                    container.appendChild(card);
                });
            } else if (currentModule === 'radios') {
                titleEl.innerHTML = '📻 Yayın Akışı & Bilgi';
                const card = document.createElement('div');
                card.className = 'epg-card active-show';
                card.innerHTML = 
                    '<div class="epg-title" style="font-size:14px;">' + m.name + '</div>' +
                    '<div class="epg-time" style="margin-top:6px;"><span>Şu An: ' + (m.currentShow || 'Canlı Kuşak') + '</span></div>' +
                    '<div class="epg-desc" style="margin-top:4px;">Sıradaki: ' + (m.nextShow || 'Gece Müzikleri') + '</div>';
                container.appendChild(card);
            }
        }

        function reloadCurrentMedia() { if (activeMedia) playMedia(activeMedia); }
        function copyStreamUrl() {
            if (activeMedia) {
                navigator.clipboard.writeText(activeMedia.url).then(() => {
                    showToast('Yayın bağlantısı kopyalandı: ' + activeMedia.name);
                }).catch(() => {
                    showToast('Yayın bağlantısı: ' + activeMedia.url);
                });
            }
        }
        function openExternal() {
            if (activeMedia) {
                window.location.href = 'streammesh://play?url=' + encodeURIComponent(activeMedia.url);
                showToast('Masaüstü oynatıcı başlatılıyor...');
            }
        }
        function togglePiP() {
            if (document.pictureInPictureElement) {
                document.exitPictureInPicture();
            } else if (document.pictureInPictureEnabled && video.style.display !== 'none') {
                video.requestPictureInPicture().catch(() => {
                    showToast('PiP modu başlatılamadı');
                });
            }
        }

        function openAddModal() { document.getElementById('addModal').style.display = 'flex'; }
        function closeAddModal() { document.getElementById('addModal').style.display = 'none'; }

        function saveCustomMedia() {
            const name = document.getElementById('customName').value.trim();
            const url = document.getElementById('customUrl').value.trim();
            const mod = document.getElementById('customModule').value;
            const cat = document.getElementById('customCategory').value.trim() || 'Özel';

            if (!name || !url) {
                showToast('Lütfen başlık ve yayın URL adresini girin.');
                return;
            }

            const item = {
                id: 'cust-' + Date.now(),
                name: name,
                category: cat,
                url: url,
                quality: 'HD',
                logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-1-tr.png'
            };

            if (!DB[mod]) DB[mod] = [];
            DB[mod].unshift(item);

            closeAddModal();
            switchModule(mod);
            playMedia(item);
            showToast('Özel yayın eklendi: ' + name);
        }

        window.onload = () => {
            startLiveClock();
            switchModule('live_tv');
        };
    </script>
</body>
</html>`;

  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(html);
});

server.listen(PORT, '0.0.0.0', () => {
  console.log(`StreamMesh Hybrid Web & Media Portal running on http://0.0.0.0:${PORT}`);
});
