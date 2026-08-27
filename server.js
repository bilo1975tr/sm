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

  const version = getAppVersion();

  // HIGH-PERFORMANCE 20-ITEM DOM-CONSTRAINED WEB PORTAL
  const html = `<!DOCTYPE html>
<html lang="tr">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0">
    <title>StreamMesh Smart Router & Web Player - v${version}</title>
    <script src="https://cdn.jsdelivr.net/npm/hls.js@1.5.8/dist/hls.min.js"></script>
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
            --warning: #f59e0b;
            --fav-gold: #fbbf24;
        }

        * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Oxygen, Ubuntu, Cantarell, sans-serif; }
        html, body { background-color: var(--bg-base); color: var(--text-main); height: 100%; min-height: 100%; }
        body { display: flex; flex-direction: column; overflow-x: hidden; }

        /* Top Header */
        .top-nav {
            background: var(--bg-surface);
            border-bottom: 1px solid var(--border);
            padding: 8px 16px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            z-index: 50;
            gap: 12px;
            flex-wrap: wrap;
            flex-shrink: 0;
        }
        .brand-section { display: flex; align-items: center; gap: 10px; }
        .logo-icon {
            width: 32px; height: 32px;
            display: flex; align-items: center; justify-content: center;
            background: linear-gradient(135deg, #0284c7, #0f172a);
            border-radius: 8px;
            border: 1px solid rgba(56, 189, 248, 0.4);
            color: #fff;
            font-weight: 800;
            font-size: 14px;
        }
        .brand-title { font-size: 15px; font-weight: 800; }
        .brand-title span { color: var(--primary-glow); }
        .version-badge {
            background: rgba(56, 189, 248, 0.12);
            color: var(--primary-glow);
            border: 1px solid rgba(56, 189, 248, 0.25);
            font-size: 10px;
            font-weight: 700;
            padding: 2px 7px;
            border-radius: 12px;
        }

        /* Top Category Navigation Chips */
        .nav-categories { display: flex; gap: 5px; overflow-x: auto; scrollbar-width: none; padding: 2px 0; max-width: 65vw; }
        .nav-categories::-webkit-scrollbar { display: none; }
        .cat-btn {
            background: var(--bg-card);
            border: 1px solid var(--border);
            color: var(--text-muted);
            padding: 5px 12px;
            border-radius: 16px;
            font-size: 11px;
            font-weight: 700;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 4px;
            transition: all 0.15s ease;
            white-space: nowrap;
        }
        .cat-btn:hover { color: #fff; background: var(--bg-hover); border-color: rgba(56, 189, 248, 0.3); }
        .cat-btn.active {
            background: var(--primary);
            color: #fff;
            border-color: var(--primary-glow);
            box-shadow: 0 2px 8px rgba(2, 132, 199, 0.4);
        }
        .cat-btn.fav-btn.active {
            background: linear-gradient(135deg, #d97706, #f59e0b);
            border-color: #fbbf24;
            color: #000;
        }

        .top-actions { display: flex; align-items: center; gap: 8px; }
        .action-btn {
            background: var(--bg-card);
            border: 1px solid var(--border);
            color: var(--text-main);
            padding: 5px 10px;
            border-radius: 6px;
            font-size: 11px;
            font-weight: 600;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 4px;
            text-decoration: none;
        }
        .action-btn:hover { background: var(--bg-hover); border-color: var(--primary-glow); }
        .action-btn.primary { background: linear-gradient(135deg, var(--primary), var(--accent)); border: none; color: #fff; }

        /* Main Layout */
        .main-container {
            display: grid;
            grid-template-columns: 360px 1fr;
            flex: 1;
            min-height: 0;
            overflow: hidden;
        }

        @media (max-width: 860px) {
            .main-container { grid-template-columns: 1fr; height: auto; overflow: visible; display: flex; flex-direction: column-reverse; }
            body { overflow-y: auto; }
            .left-sidebar { height: auto !important; min-height: 520px; max-height: none !important; }
            .nav-categories { max-width: 100%; }
        }

        /* Left Sidebar: 20-Item Strictly Paginated Channel List */
        .left-sidebar {
            background: var(--bg-surface);
            border-right: 1px solid var(--border);
            display: flex;
            flex-direction: column;
            overflow: hidden;
            height: 100%;
            min-height: 0;
        }
        .sidebar-header {
            padding: 10px 12px;
            border-bottom: 1px solid var(--border);
            display: flex;
            flex-direction: column;
            gap: 8px;
            flex-shrink: 0;
        }
        .search-row {
            display: flex;
            align-items: center;
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 6px;
            padding: 0 10px;
        }
        .search-row input {
            width: 100%;
            background: transparent;
            border: none;
            padding: 8px 6px;
            color: #fff;
            font-size: 13px;
            outline: none;
        }
        .search-row input::placeholder { color: var(--text-muted); }

        .list-meta-bar {
            display: flex;
            justify-content: space-between;
            align-items: center;
            font-size: 11px;
            color: var(--text-muted);
            padding: 0 2px;
        }

        /* Paginated 20-item DOM list with smooth vertical scroll */
        .media-list {
            flex: 1;
            overflow-y: auto;
            padding: 8px;
            display: flex;
            flex-direction: column;
            gap: 6px;
            min-height: 0;
        }
        .media-item {
            background: var(--bg-card);
            border: 1px solid transparent;
            padding: 8px 10px;
            border-radius: 8px;
            display: flex;
            align-items: center;
            gap: 10px;
            cursor: pointer;
            transition: all 0.15s;
            position: relative;
            user-select: none;
        }
        .media-item:hover { background: var(--bg-hover); border-color: rgba(56, 189, 248, 0.3); }
        .media-item.active {
            background: rgba(2, 132, 199, 0.18);
            border-color: var(--primary);
        }
        .media-logo-wrap {
            width: 38px; height: 38px;
            background: #0f172a;
            border-radius: 6px;
            display: flex; align-items: center; justify-content: center;
            overflow: hidden;
            border: 1px solid var(--border);
            flex-shrink: 0;
        }
        .media-logo-wrap img { width: 100%; height: 100%; object-fit: contain; }
        .media-meta { flex: 1; min-width: 0; }
        .media-title-row {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 2px;
        }
        .media-name {
            font-size: 13px; font-weight: 700; color: #fff;
            white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
            padding-right: 6px;
        }
        .media-sub-row {
            font-size: 11px; color: var(--text-muted);
            display: flex; align-items: center; gap: 6px;
            white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
        }
        .fav-star-btn {
            background: transparent;
            border: none;
            color: #64748b;
            font-size: 16px;
            cursor: pointer;
            padding: 4px;
            line-height: 1;
            transition: transform 0.15s, color 0.15s;
        }
        .fav-star-btn:hover { transform: scale(1.2); color: var(--fav-gold); }
        .fav-star-btn.is-fav { color: var(--fav-gold); }

        /* Pagination Controls (Strict 20-item paging) */
        .pagination-container {
            padding: 10px 12px;
            border-top: 1px solid var(--border);
            background: var(--bg-surface);
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 6px;
            flex-shrink: 0;
        }
        .page-btn {
            background: var(--bg-card);
            border: 1px solid var(--border);
            color: var(--text-main);
            padding: 6px 12px;
            border-radius: 6px;
            font-size: 11px;
            font-weight: 700;
            cursor: pointer;
        }
        .page-btn:disabled { opacity: 0.3; cursor: not-allowed; }
        .page-btn:not(:disabled):hover { background: var(--bg-hover); border-color: var(--primary-glow); }
        .page-numbers { display: flex; gap: 4px; align-items: center; }
        .page-num {
            min-width: 28px; height: 28px;
            display: flex; align-items: center; justify-content: center;
            background: var(--bg-card);
            border: 1px solid var(--border);
            color: var(--text-muted);
            border-radius: 5px;
            font-size: 11px;
            font-weight: 700;
            cursor: pointer;
        }
        .page-num.active { background: var(--primary); color: #fff; border-color: var(--primary-glow); }

        /* Center Workspace: Player */
        .player-workspace {
            display: flex;
            flex-direction: column;
            background: #000;
            position: relative;
            overflow-y: auto;
            min-height: 0;
        }
        .video-container {
            position: relative;
            background: #000;
            width: 100%;
            aspect-ratio: 16 / 9;
            max-height: 64vh;
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

        /* Player Initial / Loading Overlay */
        .player-state-overlay {
            position: absolute;
            inset: 0;
            background: rgba(10,12,16,0.92);
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            gap: 12px;
            z-index: 10;
            padding: 20px;
            text-align: center;
        }
        .spinner {
            width: 44px; height: 44px;
            border: 3px solid rgba(56, 189, 248, 0.2);
            border-top-color: var(--primary-glow);
            border-radius: 50%;
            animation: spin 0.8s linear infinite;
            display: none;
        }
        @keyframes spin { 100% { transform: rotate(360deg); } }
        .state-icon { font-size: 38px; color: var(--primary-glow); }
        .state-title { font-size: 16px; font-weight: 700; color: #fff; }
        .state-desc { font-size: 12px; color: var(--text-muted); max-width: 420px; line-height: 1.4; }

        /* Player Controls & Info Bar */
        .player-controls-bar {
            background: var(--bg-surface);
            border-bottom: 1px solid var(--border);
            padding: 10px 16px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-wrap: wrap;
            gap: 8px;
            flex-shrink: 0;
        }
        .playing-media-desc h2 { font-size: 15px; font-weight: 700; color: #fff; display: flex; align-items: center; gap: 8px; }
        .playing-media-desc p { font-size: 12px; color: var(--text-muted); margin-top: 2px; }

        .player-actions { display: flex; gap: 6px; flex-wrap: wrap; }
        .ctrl-btn {
            background: var(--bg-card);
            border: 1px solid var(--border);
            color: #fff;
            padding: 6px 11px;
            border-radius: 6px;
            font-size: 11px;
            font-weight: 600;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 4px;
        }
        .ctrl-btn:hover { background: var(--bg-hover); border-color: var(--primary-glow); }

        .stream-details-panel {
            padding: 14px 16px;
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 10px;
            background: var(--bg-base);
        }
        .metric-card {
            background: var(--bg-surface);
            border: 1px solid var(--border);
            padding: 10px 12px;
            border-radius: 7px;
        }
        .metric-card span { font-size: 10px; color: var(--text-muted); text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; }
        .metric-card h4 { font-size: 12px; font-weight: 700; margin-top: 3px; color: var(--text-main); word-break: break-all; }

        /* Badges */
        .badge { font-size: 10px; padding: 2px 6px; border-radius: 4px; font-weight: 700; }
        .badge-direct { background: rgba(16,185,129,0.15); color: #10b981; border: 1px solid #059669; }
        .badge-p2p { background: rgba(56,189,248,0.15); color: #38bdf8; border: 1px solid #0284c7; }
        .badge-multi { background: rgba(245,158,11,0.15); color: #f59e0b; border: 1px solid #d97706; }
        .badge-yt { background: rgba(239,68,68,0.15); color: #ef4444; border: 1px solid #dc2626; }

        /* Toast */
        .toast {
            position: fixed; bottom: 20px; right: 20px;
            background: #1e293b; color: #fff; border: 1px solid var(--primary-glow);
            padding: 8px 14px; border-radius: 6px; font-size: 12px; font-weight: 600;
            z-index: 9999; transform: translateY(80px); opacity: 0; transition: all 0.2s; pointer-events: none;
        }
        .toast.show { transform: translateY(0); opacity: 1; }
    </style>
</head>
<body>

    <div id="toast" class="toast"></div>

    <!-- Header Navigation with 11 Categories -->
    <header class="top-nav">
        <div class="brand-section">
            <div class="logo-icon">SM</div>
            <div class="brand-title">Stream<span>Mesh</span></div>
            <span class="version-badge">v${version} Router</span>
        </div>

        <!-- 11 Clean Categories -->
        <nav class="nav-categories">
            <button class="cat-btn active" onclick="setCategory('TÜMÜ')">📺 TÜMÜ</button>
            <button class="cat-btn" onclick="setCategory('TV')">📡 TV</button>
            <button class="cat-btn" onclick="setCategory('FİLM')">🎬 FİLM</button>
            <button class="cat-btn" onclick="setCategory('DİZİ')">🍿 DİZİ</button>
            <button class="cat-btn" onclick="setCategory('RADYO')">📻 RADYO</button>
            <button class="cat-btn" onclick="setCategory('SPOR')">⚽ SPOR</button>
            <button class="cat-btn" onclick="setCategory('HABER')">📰 HABER</button>
            <button class="cat-btn" onclick="setCategory('ÇOCUK')">🎈 ÇOCUK</button>
            <button class="cat-btn" onclick="setCategory('MÜZİK')">🎵 MÜZİK</button>
            <button class="cat-btn" onclick="setCategory('DİĞER')">📁 DİĞER</button>
            <button class="cat-btn fav-btn" onclick="setCategory('FAVORİLER')">⭐ FAVORİLER</button>
        </nav>

        <div class="top-actions">
            <a class="action-btn primary" href="/api/playlist.m3u" download="StreamMesh_SmartRouter.m3u">📥 M3U İndir</a>
        </div>
    </header>

    <!-- Main Workspace -->
    <main class="main-container">
        
        <!-- Left Sidebar: 20-Item DOM-Constrained Channel List -->
        <aside class="left-sidebar">
            <div class="sidebar-header">
                <div class="search-row">
                    <span>🔍</span>
                    <input type="text" id="searchInput" placeholder="Kanal, film veya tür ara..." oninput="handleSearchDebounced()">
                </div>
                <div class="list-meta-bar">
                    <span id="resultCountLabel">Kanallar hazırlanıyor...</span>
                    <span id="pageRangeLabel">Sayfa 1 / 1</span>
                </div>
            </div>

            <!-- ONLY 20 CARDS RENDERED IN DOM AT ONCE -->
            <div class="media-list" id="mediaListContainer"></div>

            <!-- Strict 20-item Pagination Controls -->
            <div class="pagination-container">
                <button class="page-btn" id="prevPageBtn" onclick="changePage(-1)">‹ Önceki</button>
                <div class="page-numbers" id="pageNumbersContainer"></div>
                <button class="page-btn" id="nextPageBtn" onclick="changePage(1)">Sonraki ›</button>
            </div>
        </aside>

        <!-- Center Player Workspace -->
        <section class="player-workspace">
            <div class="video-container">
                <video id="videoPlayer" controls playsinline></video>
                
                <!-- Player Initial Prompt & Error Overlay -->
                <div class="player-state-overlay" id="playerOverlay">
                    <div class="state-icon" id="playerStateIcon">▶</div>
                    <div class="spinner" id="playerSpinner"></div>
                    <div class="state-title" id="playerStateTitle">Kanal Seçin</div>
                    <div class="state-desc" id="playerStateDesc">Oynatmak istediğiniz kanal veya içeriğe sol listeden tıklayın.</div>
                </div>
            </div>

            <div class="player-controls-bar">
                <div class="playing-media-desc">
                    <h2 id="currentMediaHeader">
                        <span id="currentMediaTitle">Kanal Bekleniyor</span>
                        <span id="mediaBadge" class="badge badge-direct">Doğrudan Akış</span>
                    </h2>
                    <p id="currentMediaSub">Smart Router HLS & P2P motoru hazır</p>
                </div>

                <div class="player-actions">
                    <button class="ctrl-btn" onclick="toggleFavoriteCurrent()">⭐ Favori</button>
                    <button class="ctrl-btn" onclick="reloadStream()">🔄 Yenile</button>
                    <button class="ctrl-btn" onclick="copyStreamLink()">📋 URL Kopyala</button>
                </div>
            </div>

            <div class="stream-details-panel">
                <div class="metric-card">
                    <span>Yayın Durumu</span>
                    <h4 id="streamStatusVal" style="color:var(--text-muted);">Beklemede</h4>
                </div>
                <div class="metric-card">
                    <span>Yönlendirilen Akış URL</span>
                    <h4 id="streamUrlVal">-</h4>
                </div>
                <div class="metric-card">
                    <span>Smart Router Modu</span>
                    <h4 id="streamModeVal">Doğrudan HLS / MPEG-TS Proxy</h4>
                </div>
            </div>
        </section>

    </main>

    <script>
        const RAW_DATABASE = ${JSON.stringify(getAllMediaItems())};
        const PAGE_SIZE = 20;

        let allChannels = RAW_DATABASE;
        let selectedCategory = 'TÜMÜ';
        let searchQuery = '';
        let currentPage = 1;
        let activeMedia = null;
        let hlsInstance = null;
        let favoritesSet = new Set();
        let searchTimeout = null;

        // Safe Base64 SVG icons - 0 network overhead, zero syntax error risk
        const FALLBACK_B64 = "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIzOCIgaGVpZ2h0PSIzOCIgdmlld0JveD0iMCAwIDM4IDM4Ij48cmVjdCB3aWR0aD0iMzgiIGhlaWdodD0iMzgiIGZpbGw9IiMxOTFkMjYiIHJ4PSI2Ii8+PHRleHQgeD0iMTkiIHk9IjI0IiBmb250LXNpemU9IjEyIiBmb250LXdlaWdodD0iODAwIiBmaWxsPSIjMzhiZGY4IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIj5TTTwvdGV4dD48L3N2Zz4=";

        function initFavorites() {
            try {
                const stored = localStorage.getItem('streammesh_favorites');
                if (stored) {
                    favoritesSet = new Set(JSON.parse(stored));
                }
            } catch(e) {
                favoritesSet = new Set();
            }
        }

        function saveFavorites() {
            try {
                localStorage.setItem('streammesh_favorites', JSON.stringify([...favoritesSet]));
            } catch(e) {}
        }

        function toggleFavorite(id, e) {
            if (e) e.stopPropagation();
            if (favoritesSet.has(id)) {
                favoritesSet.delete(id);
                showToast('Favorilerden çıkarıldı');
            } else {
                favoritesSet.add(id);
                showToast('Favorilere eklendi ⭐');
            }
            saveFavorites();
            renderChannelList();
        }

        function toggleFavoriteCurrent() {
            if (activeMedia) toggleFavorite(activeMedia.id);
        }

        function setCategory(cat) {
            selectedCategory = cat;
            currentPage = 1;
            document.querySelectorAll('.cat-btn').forEach(b => {
                b.classList.toggle('active', b.innerText.includes(cat));
            });
            renderChannelList();
            document.getElementById('mediaListContainer').scrollTop = 0;
        }

        function handleSearchDebounced() {
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(() => {
                searchQuery = document.getElementById('searchInput').value.toLowerCase().trim();
                currentPage = 1;
                renderChannelList();
                document.getElementById('mediaListContainer').scrollTop = 0;
            }, 180);
        }

        function getFilteredChannels() {
            return allChannels.filter(c => {
                // Category Filter
                if (selectedCategory === 'FAVORİLER') {
                    if (!favoritesSet.has(c.id)) return false;
                } else if (selectedCategory === 'DİĞER') {
                    const known = ['TV', 'FİLM', 'DİZİ', 'RADYO', 'SPOR', 'HABER', 'ÇOCUK', 'MÜZİK'];
                    if (known.includes(c.category)) return false;
                } else if (selectedCategory !== 'TÜMÜ') {
                    const cCat = (c.category || '').toUpperCase();
                    const cGenre = (c.genre || '').toUpperCase();
                    const cSub = (c.subCategory || '').toUpperCase();
                    const target = selectedCategory.toUpperCase();
                    if (cCat !== target && cGenre !== target && !cSub.includes(target)) return false;
                }

                // Search Filter
                if (searchQuery) {
                    const matchName = (c.name || '').toLowerCase().includes(searchQuery);
                    const matchSub = (c.subCategory || '').toLowerCase().includes(searchQuery);
                    const matchCat = (c.category || '').toLowerCase().includes(searchQuery);
                    return matchName || matchSub || matchCat;
                }

                return true;
            });
        }

        // STRICT 20-ITEM DOM RENDERER
        function renderChannelList() {
            const container = document.getElementById('mediaListContainer');
            const filtered = getFilteredChannels();
            const totalCount = filtered.length;
            const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

            if (currentPage > totalPages) currentPage = totalPages;
            if (currentPage < 1) currentPage = 1;

            // Slice exactly 20 items for DOM
            const startIndex = (currentPage - 1) * PAGE_SIZE;
            const pageItems = filtered.slice(startIndex, startIndex + PAGE_SIZE);

            // Update Meta & Pagination UI
            const rangeStart = totalCount > 0 ? startIndex + 1 : 0;
            const rangeEnd = Math.min(startIndex + PAGE_SIZE, totalCount);

            document.getElementById('resultCountLabel').innerText = totalCount + ' içerik (' + rangeStart + '–' + rangeEnd + ')';
            document.getElementById('pageRangeLabel').innerText = 'Sayfa ' + currentPage + ' / ' + totalPages;
            document.getElementById('prevPageBtn').disabled = (currentPage <= 1);
            document.getElementById('nextPageBtn').disabled = (currentPage >= totalPages);

            renderPageNumbers(totalPages);

            // Render ONLY the 20 items in DOM
            container.innerHTML = '';

            if (pageItems.length === 0) {
                const emptyDiv = document.createElement('div');
                emptyDiv.style.padding = '24px 14px';
                emptyDiv.style.textAlign = 'center';
                emptyDiv.style.color = 'var(--text-muted)';
                emptyDiv.style.fontSize = '12px';
                emptyDiv.innerHTML = selectedCategory === 'FAVORİLER' 
                    ? 'Henüz favori içerik eklenmedi.<br>Kartlardaki yıldız ikonuna (☆) basarak ekleyebilirsiniz.' 
                    : 'Arama kriterlerine uygun içerik bulunamadı.';
                container.appendChild(emptyDiv);
                return;
            }

            pageItems.forEach(ch => {
                const isActive = activeMedia && activeMedia.id === ch.id;
                const isFav = favoritesSet.has(ch.id);
                const logoSrc = ch.logo && ch.logo.trim() !== '' ? ch.logo : FALLBACK_B64;

                const item = document.createElement('div');
                item.className = 'media-item' + (isActive ? ' active' : '');
                item.onclick = () => playChannel(ch);

                const logoWrap = document.createElement('div');
                logoWrap.className = 'media-logo-wrap';

                const img = document.createElement('img');
                img.loading = 'lazy';
                img.src = logoSrc;
                img.alt = '';
                img.onerror = function() {
                    this.onerror = null;
                    this.src = FALLBACK_B64;
                };
                logoWrap.appendChild(img);
                item.appendChild(logoWrap);

                const meta = document.createElement('div');
                meta.className = 'media-meta';

                const titleRow = document.createElement('div');
                titleRow.className = 'media-title-row';

                const nameSpan = document.createElement('span');
                nameSpan.className = 'media-name';
                nameSpan.innerText = ch.name;
                titleRow.appendChild(nameSpan);

                const favBtn = document.createElement('button');
                favBtn.className = 'fav-star-btn' + (isFav ? ' is-fav' : '');
                favBtn.innerText = isFav ? '★' : '☆';
                favBtn.onclick = (e) => toggleFavorite(ch.id, e);
                titleRow.appendChild(favBtn);

                meta.appendChild(titleRow);

                const subRow = document.createElement('div');
                subRow.className = 'media-sub-row';

                const badge = document.createElement('span');
                badge.className = 'badge ' + (ch.sourceType === 'ACESTREAM' ? 'badge-p2p' : (ch.sourceType === 'MULTI_SOURCE' ? 'badge-multi' : 'badge-direct'));
                badge.innerText = ch.subCategory || ch.category;
                subRow.appendChild(badge);

                const qualSpan = document.createElement('span');
                qualSpan.innerText = ch.quality || 'HD';
                subRow.appendChild(qualSpan);

                meta.appendChild(subRow);
                item.appendChild(meta);

                container.appendChild(item);
            });
        }

        function renderPageNumbers(totalPages) {
            const container = document.getElementById('pageNumbersContainer');
            container.innerHTML = '';

            let start = Math.max(1, currentPage - 2);
            let end = Math.min(totalPages, start + 4);
            if (end - start < 4) start = Math.max(1, end - 4);

            for (let i = start; i <= end; i++) {
                const num = document.createElement('div');
                num.className = 'page-num' + (i === currentPage ? ' active' : '');
                num.innerText = i;
                num.onclick = () => { 
                    currentPage = i; 
                    renderChannelList(); 
                    document.getElementById('mediaListContainer').scrollTop = 0;
                };
                container.appendChild(num);
            }
        }

        function changePage(delta) {
            const filtered = getFilteredChannels();
            const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
            const newPage = currentPage + delta;
            if (newPage >= 1 && newPage <= totalPages) {
                currentPage = newPage;
                renderChannelList();
                document.getElementById('mediaListContainer').scrollTop = 0;
            }
        }

        // USER INTERACTION DRIVEN PLAYBACK LIFECYCLE
        function playChannel(ch) {
            activeMedia = ch;
            renderChannelList();

            const video = document.getElementById('videoPlayer');
            const overlay = document.getElementById('playerOverlay');
            const icon = document.getElementById('playerStateIcon');
            const spinner = document.getElementById('playerSpinner');
            const stateTitle = document.getElementById('playerStateTitle');
            const stateDesc = document.getElementById('playerStateDesc');

            // UI metadata update
            document.getElementById('currentMediaTitle').innerText = ch.name;
            const routedUrl = '/stream/' + ch.id;
            document.getElementById('streamUrlVal').innerText = routedUrl;
            document.getElementById('currentMediaSub').innerText = (ch.subCategory || ch.category) + ' • ' + (ch.quality || 'HD');
            document.getElementById('streamStatusVal').innerText = 'Bağlantı kuruluyor...';
            document.getElementById('streamStatusVal').style.color = 'var(--warning)';

            // Cleanly destroy previous Hls.js instance to prevent memory leaks
            if (hlsInstance) {
                try {
                    hlsInstance.destroy();
                } catch(e) {}
                hlsInstance = null;
            }

            video.pause();
            video.removeAttribute('src');
            video.load();

            // Show loading state
            overlay.style.display = 'flex';
            icon.style.display = 'none';
            spinner.style.display = 'block';
            stateTitle.innerText = 'Yayın Başlatılıyor...';
            stateDesc.innerText = ch.name + ' akış kaynağı hazırlanıyor...';

            const streamUrl = routedUrl;
            const isHls = ch.url.includes('.m3u8') || ch.category === 'TV' || ch.sourceType === 'MULTI_SOURCE' || ch.sourceType === 'ACESTREAM';

            if (isHls) {
                if (window.Hls && Hls.isSupported()) {
                    hlsInstance = new Hls({
                        enableWorker: true,
                        lowLatencyMode: true,
                        manifestLoadingMaxRetry: 4,
                        manifestLoadingRetryDelay: 1000
                    });

                    hlsInstance.loadSource(streamUrl);
                    hlsInstance.attachMedia(video);

                    hlsInstance.on(Hls.Events.MANIFEST_PARSED, () => {
                        overlay.style.display = 'none';
                        document.getElementById('streamStatusVal').innerText = 'Canlı Yayın Aktif';
                        document.getElementById('streamStatusVal').style.color = 'var(--success)';
                        video.play().catch(e => {
                            if (e.name === 'NotAllowedError') {
                                overlay.style.display = 'flex';
                                icon.style.display = 'block';
                                spinner.style.display = 'none';
                                stateTitle.innerText = 'Oynatmak İçin Tıklayın';
                                stateDesc.innerText = 'Tarayıcı güvenlik politikası nedeniyle oynatıcıya dokunarak başlatın.';
                                overlay.onclick = () => {
                                    overlay.style.display = 'none';
                                    video.play();
                                };
                            }
                        });
                    });

                    hlsInstance.on(Hls.Events.ERROR, (event, data) => {
                        if (data.fatal) {
                            spinner.style.display = 'none';
                            icon.style.display = 'block';
                            icon.innerText = '⚠️';
                            stateTitle.innerText = 'Yayın Akışı Alınamadı';
                            if (data.response && data.response.code === 403) {
                                stateDesc.innerText = 'Kaynak sunucu yayını reddetti (HTTP 403 - Erişim Kısıtı).';
                            } else if (data.response && data.response.code === 404) {
                                stateDesc.innerText = 'Yayın kanalı şu an çevrimdışı (HTTP 404).';
                            } else {
                                stateDesc.innerText = 'Yayın akışı alınamadı. Farklı bir kaynak deneniyor...';
                            }
                            document.getElementById('streamStatusVal').innerText = 'Bağlantı Hatası';
                            document.getElementById('streamStatusVal').style.color = 'var(--live-red)';
                        }
                    });
                } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
                    // Native Safari HLS
                    video.src = streamUrl;
                    video.play().then(() => {
                        overlay.style.display = 'none';
                        document.getElementById('streamStatusVal').innerText = 'Canlı Yayın Aktif';
                        document.getElementById('streamStatusVal').style.color = 'var(--success)';
                    }).catch(e => {
                        spinner.style.display = 'none';
                        icon.style.display = 'block';
                        stateTitle.innerText = 'Oynatma Uyarısı';
                        stateDesc.innerText = e.message;
                    });
                }
            } else {
                // Direct MP4, MP3, AAC
                video.src = streamUrl;
                video.onloadeddata = () => {
                    overlay.style.display = 'none';
                    document.getElementById('streamStatusVal').innerText = 'Yayın Aktif';
                    document.getElementById('streamStatusVal').style.color = 'var(--success)';
                };
                video.onerror = () => {
                    spinner.style.display = 'none';
                    icon.style.display = 'block';
                    icon.innerText = '⚠️';
                    stateTitle.innerText = 'Yayın Oynatılamadı';
                    stateDesc.innerText = 'Medya akışı yüklenirken hata oluştu.';
                    document.getElementById('streamStatusVal').innerText = 'Akış Hatası';
                    document.getElementById('streamStatusVal').style.color = 'var(--live-red)';
                };
                video.play().catch(e => {
                    if (e.name === 'NotAllowedError') {
                        overlay.style.display = 'flex';
                        icon.style.display = 'block';
                        spinner.style.display = 'none';
                        stateTitle.innerText = 'Oynatmak İçin Tıklayın';
                        stateDesc.innerText = 'Oynatıcıya dokunarak başlatın.';
                        overlay.onclick = () => {
                            overlay.style.display = 'none';
                            video.play();
                        };
                    }
                });
            }
        }

        function reloadStream() {
            if (activeMedia) playChannel(activeMedia);
        }

        function copyStreamLink() {
            if (activeMedia) {
                const fullUrl = window.location.origin + '/stream/' + activeMedia.id;
                if (navigator.clipboard) {
                    navigator.clipboard.writeText(fullUrl).then(() => {
                        showToast('Yayın URL kopyalandı');
                    }).catch(() => {
                        showToast(fullUrl);
                    });
                } else {
                    showToast(fullUrl);
                }
            }
        }

        function showToast(msg) {
            const t = document.getElementById('toast');
            t.innerText = msg;
            t.classList.add('show');
            setTimeout(() => t.classList.remove('show'), 2500);
        }

        window.onload = () => {
            initFavorites();
            renderChannelList();
            // User requested: DO NOT autoplay without explicit click
            document.getElementById('playerOverlay').style.display = 'flex';
            document.getElementById('playerStateIcon').style.display = 'block';
            document.getElementById('playerSpinner').style.display = 'none';
        };
    </script>
</body>
</html>`;

  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(html);
});

server.listen(PORT, '0.0.0.0', () => {
  console.log(`StreamMesh High-Performance Paginated Web Portal running on http://0.0.0.0:${PORT}`);
});
