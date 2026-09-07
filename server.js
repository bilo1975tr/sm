import http from 'http';
import https from 'https';
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
  return '2.1.0';
}

// Master Media Catalog with Rich Categories & Standard Test Streams
const MEDIA_DATABASE = {
  live_tv: [
    {
      id: 'ch-trt1',
      name: 'TRT 1 HD',
      category: 'TV',
      subCategory: 'Ulusal & Dizi',
      genre: 'TV',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-1-tr.png',
      url: 'https://tv-trt1.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'TRT 1 canlı yayını - Ulusal kanal, dizi ve programlar.'
    },
    {
      id: 'ch-trthaber',
      name: 'TRT Haber HD',
      category: 'HABER',
      subCategory: 'Haber & Gündem',
      genre: 'HABER',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-haber-tr.png',
      url: 'https://tv-trthaber.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'TRT Haber canlı yayını - Son dakika haberleri ve canlı bağlantılar.'
    },
    {
      id: 'ch-trtspor',
      name: 'TRT Spor HD (Smart Router)',
      category: 'SPOR',
      subCategory: 'Canlı Maç & Spor',
      genre: 'SPOR',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-spor-tr.png',
      url: 'https://tv-trtspor1.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'MULTI_SOURCE',
      requiresStreamMesh: true,
      sourcesCount: 3,
      desc: 'TRT Spor canlı yayını - Çoklu kaynak yedeklemeli akış.'
    },
    {
      id: 'ch-trtspor2',
      name: 'TRT Spor Yıldız HD',
      category: 'SPOR',
      subCategory: 'Olimpiyat & Branş Sporları',
      genre: 'SPOR',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-spor-yildiz-tr.png',
      url: 'https://tv-trtspor2.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Voleybol, basketbol, güreş ve tüm olimpik branşlar.'
    },
    {
      id: 'ch-trtbelgesel',
      name: 'TRT Belgesel HD',
      category: 'TV',
      subCategory: 'Belgesel & Doğa',
      genre: 'TV',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-belgesel-tr.png',
      url: 'https://tv-trtbelgesel.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Kültür, doğa, bilim ve insan hikayeleri.'
    },
    {
      id: 'ch-trtcocuk',
      name: 'TRT Çocuk HD',
      category: 'ÇOCUK',
      subCategory: 'Çizgi Film & Eğlence',
      genre: 'ÇOCUK',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-cocuk-tr.png',
      url: 'https://tv-trtcocuk.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Rafadan Tayfa, İbi, Ege ile Gaga ve çocuk programları.'
    },
    {
      id: 'ch-trtturk',
      name: 'TRT Türk HD',
      category: 'TV',
      subCategory: 'Kültür & Diasporalar',
      genre: 'TV',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-turk-tr.png',
      url: 'https://tv-trtturk.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Türk dünyası ve yurt dışı vatandaşlarımıza yönelik yayın.'
    },
    {
      id: 'ch-trtmuzik',
      name: 'TRT Müzik HD',
      category: 'MÜZİK',
      subCategory: 'Türk Sanat & Halk Müziği',
      genre: 'MÜZİK',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-muzik-tr.png',
      url: 'https://tv-trtmuzik.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Geleneksel ve modern müziğin en seçkin programları.'
    },
    {
      id: 'ch-redbull',
      name: 'Red Bull TV HD',
      category: 'SPOR',
      subCategory: 'Ekstrem Sporlar & Aksiyon',
      genre: 'SPOR',
      logo: 'https://upload.wikimedia.org/wikipedia/en/thumb/e/e4/Red_Bull_TV_logo.svg/320px-Red_Bull_TV_logo.svg.png',
      url: 'https://rbmn-live.akamaized.net/hls/live/590964/BoRB-AT/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Formula 1, Cliff Diving, MTB ve ekstrem spor yayınları.'
    },
    {
      id: 'ch-nasa',
      name: 'NASA TV Live HD',
      category: 'TV',
      subCategory: 'Uzay & Bilim',
      genre: 'TV',
      logo: 'https://upload.wikimedia.org/wikipedia/commons/thumb/e/e5/NASA_logo.svg/300px-NASA_logo.svg.png',
      url: 'https://ntv1.akamaized.net/hls/live/2014075/NASA-NTV1-HLS/master.m3u8',
      quality: '720p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Uluslararası Uzay İstasyonu (ISS) canlı kamera görüntüleri.'
    },
    {
      id: 'ace-demo1',
      name: 'AceStream P2P Test Kanalı 1',
      category: 'TV',
      subCategory: 'P2P Canlı',
      genre: 'TV',
      logo: '',
      url: 'https://rbmn-live.akamaized.net/hls/live/590964/BoRB-AT/master.m3u8',
      contentId: '0a48b895ed0994a11fccf487aada3808446bb932',
      quality: '1080p 60fps',
      type: 'video',
      sourceType: 'ACESTREAM',
      requiresStreamMesh: true,
      sourcesCount: 1,
      desc: 'Standart HTTP MPEG-TS köprüsü üzerinden paylaşımlı P2P AceEngine akışı.'
    },
    {
      id: 'ace-demo2',
      name: 'AceStream P2P Spor Arenası',
      category: 'SPOR',
      subCategory: 'P2P Spor',
      genre: 'SPOR',
      logo: '',
      url: 'https://tv-trtspor1.medya.trt.com.tr/master.m3u8',
      contentId: 'd3b07384d113edec49eaa6238ad5ff00f7b1e4c2',
      quality: '1080p',
      type: 'video',
      sourceType: 'ACESTREAM',
      requiresStreamMesh: true,
      sourcesCount: 1,
      desc: 'Çoklu istemci oturum çoğullayıcı ile tek P2P oturumunu paylaşan akış.'
    },
    {
      id: 'yt-demo1',
      name: 'NASA Live (YouTube Bridge)',
      category: 'TV',
      subCategory: 'YouTube Canlı',
      genre: 'TV',
      logo: '',
      url: 'https://ntv1.akamaized.net/hls/live/2014075/NASA-NTV1-HLS/master.m3u8',
      ytUrl: 'https://www.youtube.com/watch?v=21X5lGlDOfg',
      quality: '1080p',
      type: 'video',
      sourceType: 'YOUTUBE',
      requiresStreamMesh: true,
      sourcesCount: 1,
      desc: 'StreamMesh YoutubeEngine ve HLS Proxy aracılığıyla HTTP üzerinden çözümlenen YouTube canlı yayını.'
    },
    {
      id: 'yt-demo2',
      name: 'TRT World Live (YouTube Bridge)',
      category: 'HABER',
      subCategory: 'YouTube Haber',
      genre: 'HABER',
      logo: '',
      url: 'https://tv-trthaber.medya.trt.com.tr/master.m3u8',
      ytUrl: 'https://www.youtube.com/watch?v=k-V31xW5-Zk',
      quality: '1080p',
      type: 'video',
      sourceType: 'YOUTUBE',
      requiresStreamMesh: true,
      sourcesCount: 1,
      desc: 'Doğrudan HTTP akışına dönüştürülen YouTube canlı yayını.'
    },
    {
      id: 'ch-trtworld',
      name: 'TRT World International',
      category: 'HABER',
      subCategory: 'Uluslararası Haber (İngilizce)',
      genre: 'HABER',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-world-tr.png',
      url: 'https://tv-trtworld.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: '24 saat kesintisiz uluslararası İngilizce haber kanalı.'
    },
    {
      id: 'ch-trtavaz',
      name: 'TRT Avaz HD',
      category: 'TV',
      subCategory: 'Balkanlar & Kafkaslar',
      genre: 'TV',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-avaz-tr.png',
      url: 'https://tv-trtavaz.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Balkanlar, Kafkaslar ve Orta Asya coğrafyasının ortak sesi.'
    },
    {
      id: 'ch-trtkurdi',
      name: 'TRT Kurdî HD',
      category: 'TV',
      subCategory: 'Kültür & Sanat',
      genre: 'TV',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-kurdi-tr.png',
      url: 'https://tv-trtkurdi.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'TRT Kürtçe yayın yapan kültür, müzik ve sinema kanalı.'
    },
    {
      id: 'ch-trtarabi',
      name: 'TRT Arabi HD',
      category: 'HABER',
      subCategory: 'Ortadoğu & Haber',
      genre: 'HABER',
      logo: 'https://raw.githubusercontent.com/tv-logo/tv-logos/main/countries/turkey/trt-arabi-tr.png',
      url: 'https://tv-trtarabi.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Arap coğrafyasına yönelik 24 saat haber ve belgesel yayını.'
    },
    {
      id: 'ch-diyanet',
      name: 'Diyanet TV HD',
      category: 'TV',
      subCategory: 'Dini & Eğitici',
      genre: 'TV',
      logo: '',
      url: 'https://tv-trt1.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Dini sohbetler, Kur-an tilaveti ve eğitici yayınlar.'
    },
    {
      id: 'ch-bloomberg',
      name: 'Bloomberg HT',
      category: 'HABER',
      subCategory: 'Ekonomi & Finans',
      genre: 'HABER',
      logo: '',
      url: 'https://tv-trthaber.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Borsa, döviz, altın ve küresel piyasa analizleri.'
    },
    {
      id: 'ch-eko-turk',
      name: 'Ekotürk TV HD',
      category: 'HABER',
      subCategory: 'Ekonomi & İş Dünyası',
      genre: 'HABER',
      logo: '',
      url: 'https://tv-trthaber.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'İş dünyası röportajları ve ekonomi gündemi.'
    },
    {
      id: 'ch-tjk',
      name: 'TJK TV HD',
      category: 'SPOR',
      subCategory: 'At Yarışı & Canlı Koşular',
      genre: 'SPOR',
      logo: '',
      url: 'https://tv-trtspor1.medya.trt.com.tr/master.m3u8',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Türkiye ve yurtdışı hipodromlarından canlı koşular.'
    }
  ],

  movies: [
    {
      id: 'mov-sintel',
      name: 'Sintel (Açık Kaynak Animasyon)',
      category: 'FİLM',
      subCategory: 'Animasyon / Macera',
      genre: 'FİLM',
      year: '2010',
      duration: '15 dk',
      rating: '8.4',
      director: 'Colin Levy',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/Sintel.mp4',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Ejderha yavrusu Scales\'i kurtarmak için tehlikeli dağları aşan genç bir kızın hikayesi.'
    },
    {
      id: 'mov-bbb',
      name: 'Big Buck Bunny',
      category: 'FİLM',
      subCategory: 'Animasyon / Komedi',
      genre: 'FİLM',
      year: '2008',
      duration: '10 dk',
      rating: '8.1',
      director: 'Sacha Goedegebure',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Ormanın sevimli dev tavşanı Bunny, ormanı kirletenlere ders verir.'
    },
    {
      id: 'mov-tears',
      name: 'Tears of Steel',
      category: 'FİLM',
      subCategory: 'Bilim Kurgu / Macera',
      genre: 'FİLM',
      year: '2012',
      duration: '12 dk',
      rating: '7.6',
      director: 'Ian Hubert',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/TearsOfSteel.mp4',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Kıyamet sonrası distopik gelecekte robot kıyametini durdurmaya çalışan bilim insanları.'
    },
    {
      id: 'mov-elephants',
      name: 'Elephants Dream',
      category: 'FİLM',
      subCategory: 'Bilim Kurgu / Animasyon',
      genre: 'FİLM',
      year: '2006',
      duration: '11 dk',
      rating: '7.2',
      director: 'Bassam Kurdali',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ElephantsDream.mp4',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Devasa bir mekanik dünyanın içindeki iki gezginin fantastik yolculuğu.'
    },
    {
      id: 'mov-for-bigger-blazes',
      name: 'For Bigger Blazes (Action Demo)',
      category: 'FİLM',
      subCategory: 'Aksiyon & Dublör',
      genre: 'FİLM',
      year: '2021',
      duration: '5 dk',
      rating: '7.5',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerBlazes.mp4',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Yüksek tempolu aksiyon sahneleri ve özel efekt gösterimi.'
    },
    {
      id: 'mov-bullrun',
      name: 'Going on Bullrun',
      category: 'FİLM',
      subCategory: 'Macera & Belgesel',
      genre: 'FİLM',
      year: '2020',
      duration: '8 dk',
      rating: '7.9',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/WeAreGoingOnBullrun.mp4',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Büyük bozkır yolculuğu ve tarihi keşif belgeseli.'
    }
  ],

  series: [
    {
      id: 'ser-alparslan',
      name: 'Alparslan: Büyük Selçuklu',
      category: 'DİZİ',
      subCategory: 'Tarih / Macera',
      genre: 'DİZİ',
      seasonCount: '2 Sezon',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/WeAreGoingOnBullrun.mp4',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Anadolu kapılarını açacak büyük yürüyüş ve Sultan Tuğrul Bey emaneti.'
    },
    {
      id: 'ser-gonul',
      name: 'Gönül Dağı',
      category: 'DİZİ',
      subCategory: 'Dram / Komedi',
      genre: 'DİZİ',
      seasonCount: '4 Sezon',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerBlazes.mp4',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Bozkırda hayallerinin peşinden koşan amcaoğullarının sıcacık hikayesi.'
    },
    {
      id: 'ser-teskilat',
      name: 'Teşkilat (Özel Görev)',
      category: 'DİZİ',
      subCategory: 'Aksiyon & İstihbarat',
      genre: 'DİZİ',
      seasonCount: '3 Sezon',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/TearsOfSteel.mp4',
      quality: '1080p',
      type: 'video',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Vatan savunmasında görünmez kahramanların yürüttüğü gizli operasyonlar.'
    }
  ],

  radios: [
    {
      id: 'rad-trtfm',
      name: 'TRT FM Canlı',
      category: 'RADYO',
      subCategory: 'Pop & Türkçe Müzik',
      genre: 'RADYO',
      freq: '91.4 MHz',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/SubaruOutbackSeeTheWorld.mp4',
      quality: '320 kbps',
      type: 'audio',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Canlı Yayın - Yol Manzaraları & Popüler Türkçe Müzik.'
    },
    {
      id: 'rad-trtradyo1',
      name: 'TRT Radyo 1',
      category: 'RADYO',
      subCategory: 'Kültür & Haber',
      genre: 'RADYO',
      freq: '89.0 MHz',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/TearsOfSteel.mp4',
      quality: '256 kbps',
      type: 'audio',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Günün Raporu, Bilim Dünyası ve Radyo Tiyatrosu kuşağı.'
    },
    {
      id: 'rad-trtradyo3',
      name: 'TRT Radyo 3',
      category: 'RADYO',
      subCategory: 'Klasik & Caz',
      genre: 'RADYO',
      freq: '88.2 MHz',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/Sintel.mp4',
      quality: '320 kbps',
      type: 'audio',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Dünya klasikleri, caz ve senfonik müzik yayınları.'
    },
    {
      id: 'rad-trtturku',
      name: 'TRT Türkü',
      category: 'RADYO',
      subCategory: 'Halk Müziği & Türküler',
      genre: 'RADYO',
      freq: '99.8 MHz',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4',
      quality: '256 kbps',
      type: 'audio',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Anadolu ezgileri ve usta aşıklardan türküler.'
    },
    {
      id: 'rad-trtnağme',
      name: 'TRT Nağme',
      category: 'RADYO',
      subCategory: 'Sanat Müziği',
      genre: 'RADYO',
      freq: '101.5 MHz',
      logo: '',
      url: 'https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ElephantsDream.mp4',
      quality: '256 kbps',
      type: 'audio',
      sourceType: 'DIRECT',
      requiresStreamMesh: false,
      sourcesCount: 1,
      desc: 'Klasik Türk Sanat Müziğinin seçkin makamları.'
    }
  ]
};

// Flattened media list
function getAllMediaItems() {
  return [
    ...MEDIA_DATABASE.live_tv,
    ...MEDIA_DATABASE.movies,
    ...MEDIA_DATABASE.series,
    ...MEDIA_DATABASE.radios
  ];
}

// Rewrites M3U8 manifest content so relative URIs point safely through /proxy?url=
function rewriteHlsManifest(manifestText, baseUrl, host) {
  const lines = manifestText.split(/\r?\n/);
  const rewritten = [];

  for (let i = 0; i < lines.length; i++) {
    let line = lines[i];
    const trimmed = line.trim();

    if (!trimmed) {
      rewritten.push(line);
      continue;
    }

    // Rewrite tags containing URI="..." e.g. #EXT-X-KEY, #EXT-X-MAP, #EXT-X-MEDIA
    if (trimmed.startsWith('#EXT-X-KEY') || trimmed.startsWith('#EXT-X-MAP') || trimmed.startsWith('#EXT-X-MEDIA')) {
      line = line.replace(/URI="([^"]+)"/g, (match, uri) => {
        try {
          const abs = new URL(uri, baseUrl).toString();
          return `URI="http://${host}/proxy?url=${encodeURIComponent(abs)}"`;
        } catch (e) {
          return match;
        }
      });
      rewritten.push(line);
      continue;
    }

    // Comment line
    if (trimmed.startsWith('#')) {
      rewritten.push(line);
      continue;
    }

    // Segment or sub-playlist URI line
    try {
      const absUrl = new URL(trimmed, baseUrl).toString();
      rewritten.push(`http://${host}/proxy?url=${encodeURIComponent(absUrl)}`);
    } catch (e) {
      rewritten.push(line);
    }
  }

  return rewritten.join('\n');
}

// Proxy stream request with automatic M3U8 manifest rewriting and CORS support
function proxyStreamRequest(targetUrl, clientReq, clientRes) {
  try {
    const u = new URL(targetUrl);
    const isHttps = u.protocol === 'https:';
    const client = isHttps ? https : http;
    const host = clientReq.headers.host || `127.0.0.1:${PORT}`;

    const headers = {
      'User-Agent': 'StreamMesh/2.1 (Web; SmartRouter)',
      'Accept': '*/*',
      ...(clientReq.headers['range'] ? { 'Range': clientReq.headers['range'] } : {})
    };

    const proxyReq = client.request(targetUrl, {
      method: clientReq.method,
      headers: headers,
      timeout: 15000
    }, (proxyRes) => {
      const statusCode = proxyRes.statusCode || 200;

      // Follow redirects up to 1 hop
      if ((statusCode === 301 || statusCode === 302 || statusCode === 307 || statusCode === 308) && proxyRes.headers.location) {
        const redirectUrl = new URL(proxyRes.headers.location, targetUrl).toString();
        proxyStreamRequest(redirectUrl, clientReq, clientRes);
        return;
      }

      const contentType = (proxyRes.headers['content-type'] || '').toLowerCase();
      const isM3u8 = contentType.includes('mpegurl') || 
                     contentType.includes('application/x-mpegurl') || 
                     contentType.includes('application/vnd.apple.mpegurl') || 
                     targetUrl.toLowerCase().includes('.m3u8');

      if (isM3u8) {
        // Read full manifest and rewrite relative URIs
        const chunks = [];
        proxyRes.on('data', chunk => chunks.push(chunk));
        proxyRes.on('end', () => {
          const rawManifest = Buffer.concat(chunks).toString('utf8');
          const rewrittenManifest = rewriteHlsManifest(rawManifest, targetUrl, host);
          const manifestBuf = Buffer.from(rewrittenManifest, 'utf8');

          clientRes.writeHead(200, {
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Methods': 'GET, OPTIONS, HEAD',
            'Access-Control-Allow-Headers': '*',
            'Content-Type': 'application/vnd.apple.mpegurl; charset=utf-8',
            'Content-Length': manifestBuf.length,
            'Cache-Control': 'no-cache, no-store, must-revalidate'
          });
          clientRes.end(manifestBuf);
        });
      } else {
        // Binary media stream (TS segment, MP4, AAC, MP3)
        const responseHeaders = {
          'Access-Control-Allow-Origin': '*',
          'Access-Control-Allow-Methods': 'GET, OPTIONS, HEAD',
          'Access-Control-Allow-Headers': '*',
          'Access-Control-Expose-Headers': 'Content-Range, Accept-Ranges, Content-Length, Content-Type',
          'Content-Type': proxyRes.headers['content-type'] || (targetUrl.endsWith('.ts') ? 'video/MP2T' : 'video/mp4'),
          ...(proxyRes.headers['content-length'] ? { 'Content-Length': proxyRes.headers['content-length'] } : {}),
          ...(proxyRes.headers['content-range'] ? { 'Content-Range': proxyRes.headers['content-range'] } : {}),
          ...(proxyRes.headers['accept-ranges'] ? { 'Accept-Ranges': proxyRes.headers['accept-ranges'] } : {})
        };

        clientRes.writeHead(statusCode, responseHeaders);
        proxyRes.pipe(clientRes);
      }
    });

    proxyReq.on('error', (err) => {
      if (!clientRes.headersSent) {
        clientRes.writeHead(502, { 
          'Access-Control-Allow-Origin': '*',
          'Content-Type': 'text/plain; charset=utf-8' 
        });
        clientRes.end(`StreamMesh Proxy Hatası: ${err.message}`);
      }
    });

    proxyReq.end();
  } catch (e) {
    if (!clientRes.headersSent) {
      clientRes.writeHead(500, { 
        'Access-Control-Allow-Origin': '*',
        'Content-Type': 'text/plain; charset=utf-8' 
      });
      clientRes.end(`Stream URL geçersiz: ${e.message}`);
    }
  }
}

const server = http.createServer((req, res) => {
  const host = req.headers.host || `localhost:${PORT}`;
  const parsedUrl = new URL(req.url, `http://${host}`);
  const pathname = parsedUrl.pathname;

  // Set universal permissive CORS headers
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS, HEAD');
  res.setHeader('Access-Control-Allow-Headers', '*');
  res.setHeader('Access-Control-Expose-Headers', 'Content-Range, Accept-Ranges, Content-Length, Content-Type');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  // API: Version
  if (pathname === '/api/version') {
    const version = getAppVersion();
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ 
      version, 
      status: 'online', 
      engine: 'StreamMesh Smart Router & Paginated Web Portal',
      features: ['20-Item Strictly Constrained DOM', 'Universal HLS Manifest Rewriter', 'Zero-Crash Safe Logos', 'Mobile Responsive Scroll']
    }));
    return;
  }

  // API: Ping
  if (pathname === '/ping' || pathname === '/api/ping') {
    res.writeHead(200, { 'Content-Type': 'text/plain' });
    res.end('pong');
    return;
  }

  // API: Channels JSON
  if (pathname === '/channels' || pathname === '/api/channels') {
    const all = getAllMediaItems().map(c => ({
      ...c,
      StreamUrl: `/stream/${c.id}`
    }));
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify(all));
    return;
  }

  // API: Stream Router `/stream/:id`
  if (pathname.startsWith('/stream/')) {
    const channelId = pathname.replace('/stream/', '').trim();
    const all = getAllMediaItems();
    const ch = all.find(c => c.id.toLowerCase() === channelId.toLowerCase());

    if (!ch) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('StreamMesh: Kanal bulunamadı (HTTP 404)');
      return;
    }

    // Direct proxy to stream URL with manifest rewrite
    proxyStreamRequest(ch.url, req, res);
    return;
  }

  // API: Stream Proxy `/proxy`
  if (pathname === '/proxy') {
    const targetUrl = parsedUrl.searchParams.get('url');
    if (!targetUrl) {
      res.writeHead(400, { 'Content-Type': 'text/plain' });
      res.end('Missing url parameter');
      return;
    }
    proxyStreamRequest(targetUrl, req, res);
    return;
  }

  // API: Smart Router M3U Playlist
  if (pathname === '/api/playlist.m3u' || pathname === '/playlist.m3u') {
    let m3u = '#EXTM3U name="StreamMesh Smart Router Playlist"\n';
    const allChannels = getAllMediaItems();

    allChannels.forEach(c => {
      const isReq = c.requiresStreamMesh ? 'true' : 'false';
      const type = c.sourceType || 'DIRECT';
      const groupSuffix = c.sourceType === 'ACESTREAM' ? ' [StreamMesh P2P]' :
                          c.sourceType === 'YOUTUBE' ? ' [StreamMesh YouTube]' :
                          c.sourceType === 'MULTI_SOURCE' ? ' [StreamMesh Smart Router]' :
                          ' [Doğrudan IPTV]';
      
      const groupTitle = `${c.category}${groupSuffix}`;
      const streamUrl = `http://${host}/stream/${c.id}`;

      m3u += `#EXTINF:-1 tvg-id="${c.id}" tvg-name="${c.name}" tvg-logo="${c.logo}" group-title="${groupTitle}" streammesh-required="${isReq}" streammesh-type="${type}",${c.name}\n${streamUrl}\n`;
    });

    res.writeHead(200, {
      'Content-Type': 'application/x-mpegURL; charset=utf-8',
      'Content-Disposition': 'attachment; filename="StreamMesh_SmartRouter.m3u"'
    });
    res.end(m3u);
    return;
  }

  // Unified Web Player: Serve /Web/index.html
  const webIndexPath = path.join(__dirname, 'Web', 'index.html');
  try {
    if (fs.existsSync(webIndexPath)) {
      const html = fs.readFileSync(webIndexPath, 'utf8');
      res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
      res.end(html);
      return;
    }
  } catch (err) {
    console.error('Web/index.html okunamadı:', err);
  }

  res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
  res.end('StreamMesh: Web/index.html bulunamadı (HTTP 404)');
});

server.listen(PORT, '0.0.0.0', () => {
  console.log(`StreamMesh High-Performance Paginated Web Portal running on http://0.0.0.0:${PORT}`);
});
