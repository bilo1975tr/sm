import express from 'express';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const app = express();
const PORT = process.env.PORT || 3000;

app.use(express.json());

// Get project version
function getVersion(): string {
  try {
    return fs.readFileSync(path.join(__dirname, 'VERSION'), 'utf-8').trim();
  } catch {
    return '1.8.8';
  }
}

// Get playlists
function getPlaylists() {
  try {
    const data = fs.readFileSync(path.join(__dirname, 'auto_update.json'), 'utf-8');
    return JSON.parse(data);
  } catch {
    return { tv: [], film: [], dizi: [], epg: [] };
  }
}

// API Routes
app.get('/api/status', (req, res) => {
  res.json({
    appName: 'StreamMesh',
    version: getVersion(),
    platform: '.NET / WPF (Windows Masaüstü)',
    database: 'SQLite & Firebase Firestore',
    p2pModes: ['Direct IPv4/IPv6', 'STUN (NAT Hole Punching)', 'TURN Relay'],
    stunServer: 'stun.l.google.com:19302',
    status: 'Hazır / Aktif'
  });
});

app.get('/api/playlists', (req, res) => {
  res.json(getPlaylists());
});

app.post('/api/playlists', (req, res) => {
  try {
    const updatedData = req.body;
    fs.writeFileSync(path.join(__dirname, 'auto_update.json'), JSON.stringify(updatedData, null, 2));
    res.json({ success: true, message: 'Liste güncellendi.' });
  } catch (err: any) {
    res.status(500).json({ success: false, error: err.message });
  }
});

// HTML Web Dashboard
app.get('/', (req, res) => {
  const version = getVersion();
  const playlists = getPlaylists();

  const html = `<!DOCTYPE html>
<html lang="tr">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>StreamMesh Portal v${version}</title>
  <script src="https://cdn.tailwindcss.com"></script>
  <link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700&display=swap" rel="stylesheet">
  <style>
    body { font-family: 'Plus Jakarta Sans', sans-serif; }
  </style>
</head>
<body class="bg-slate-900 text-slate-100 min-h-screen">
  <header class="border-b border-slate-800 bg-slate-950/80 backdrop-blur sticky top-0 z-50">
    <div class="max-w-7xl mx-auto px-6 py-4 flex items-center justify-between">
      <div class="flex items-center space-x-3">
        <div class="w-10 h-10 rounded-xl bg-blue-600 flex items-center justify-center font-bold text-lg text-white shadow-lg shadow-blue-500/20">
          SM
        </div>
        <div>
          <h1 class="text-xl font-bold tracking-tight text-white">StreamMesh Portal</h1>
          <p class="text-xs text-slate-400">P2P, EPG & Medya Yönetim Konsolu</p>
        </div>
      </div>
      <div class="flex items-center space-x-3">
        <span class="inline-flex items-center px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
          ● v${version} Yayında
        </span>
      </div>
    </div>
  </header>

  <main class="max-w-7xl mx-auto px-6 py-8 space-y-8">
    <!-- Status Grid -->
    <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
      <div class="bg-slate-800/60 border border-slate-700/50 rounded-2xl p-5">
        <span class="text-xs font-medium text-slate-400 uppercase tracking-wider">Uygulama Mimarisi</span>
        <div class="text-lg font-bold text-white mt-1">.NET / WPF</div>
        <div class="text-xs text-slate-400 mt-1">Windows Masaüstü</div>
      </div>
      <div class="bg-slate-800/60 border border-slate-700/50 rounded-2xl p-5">
        <span class="text-xs font-medium text-slate-400 uppercase tracking-wider">P2P Bağlantı Hiyerarşisi</span>
        <div class="text-lg font-bold text-white mt-1">IPv4/IPv6 → STUN → TURN</div>
        <div class="text-xs text-emerald-400 mt-1">Dinamik Geçiş Etkin</div>
      </div>
      <div class="bg-slate-800/60 border border-slate-700/50 rounded-2xl p-5">
        <span class="text-xs font-medium text-slate-400 uppercase tracking-wider">Veritabanı & Senkronizasyon</span>
        <div class="text-lg font-bold text-white mt-1">SQLite + Firestore</div>
        <div class="text-xs text-slate-400 mt-1">P2P Tünelleme Verisi</div>
      </div>
      <div class="bg-slate-800/60 border border-slate-700/50 rounded-2xl p-5">
        <span class="text-xs font-medium text-slate-400 uppercase tracking-wider">TV/Film/Dizi Kanalları</span>
        <div class="text-lg font-bold text-white mt-1">${(playlists.tv?.length || 0) + (playlists.film?.length || 0) + (playlists.dizi?.length || 0)} Kaynak</div>
        <div class="text-xs text-slate-400 mt-1">Otomatik Senkronizasyon</div>
      </div>
    </div>

    <!-- P2P Tunnel Logic Overview -->
    <div class="bg-slate-800/40 border border-slate-700/50 rounded-2xl p-6">
      <h2 class="text-lg font-bold text-white mb-3">P2P Tünelleme ve Bağlantı Mantığı</h2>
      <p class="text-sm text-slate-300 leading-relaxed mb-4">
        StreamMesh istemcileri P2P bağlantısı kurarken sırasıyla aşağıdaki adım kademelerini izler:
      </p>
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4 text-sm">
        <div class="bg-slate-900/80 p-4 rounded-xl border border-slate-800">
          <div class="font-semibold text-blue-400 mb-1">1. Doğrudan Bağlantı (Direct IPv4 / IPv6)</div>
          <p class="text-slate-400 text-xs leading-normal">
            İstemciler öncelikle doğrudan IPv4 ve IPv6 soket geri bağlantılarını test eder. Başarılı olursa tünel masrafsız direkt ağ üzerinden kurulur.
          </p>
        </div>
        <div class="bg-slate-900/80 p-4 rounded-xl border border-slate-800">
          <div class="font-semibold text-emerald-400 mb-1">2. STUN Delme (NAT Hole Punching)</div>
          <p class="text-slate-400 text-xs leading-normal">
            Doğrudan erişim kapalıysa Google STUN (<code class="text-slate-300">stun.l.google.com:19302</code>) üzerinden NAT delme gerçekleştirilir.
          </p>
        </div>
        <div class="bg-slate-900/80 p-4 rounded-xl border border-slate-800">
          <div class="font-semibold text-amber-400 mb-1">3. TURN Röle Servisi</div>
          <p class="text-slate-400 text-xs leading-normal">
            Kıdemli NAT engellerinde röle sunucusuna geçilir. İstemciler daha sonra doğrudan veya STUN bağlantısı sağladığında otomatik olarak TURN'ü bırakır.
          </p>
        </div>
      </div>
    </div>

    <!-- Playlists Section -->
    <div class="bg-slate-800/40 border border-slate-700/50 rounded-2xl p-6">
      <div class="flex items-center justify-between mb-4">
        <div>
          <h2 class="text-lg font-bold text-white">Otomatik Güncellenen Liste Kaynakları (auto_update.json)</h2>
          <p class="text-xs text-slate-400">StreamMesh masaüstü uygulamasının çektiği varsayılan IPTV, Film, Dizi ve EPG bağlantıları</p>
        </div>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div>
          <h3 class="text-sm font-semibold text-blue-400 mb-2 flex items-center justify-between">
            <span>📺 TV Kanalları (${playlists.tv?.length || 0})</span>
          </h3>
          <div class="bg-slate-900/90 rounded-xl p-3 max-h-48 overflow-y-auto space-y-1 text-xs text-slate-300 border border-slate-800">
            ${(playlists.tv || []).map((url: string) => `<div class="truncate text-slate-300 hover:text-white py-1 px-2 rounded hover:bg-slate-800/50">${url}</div>`).join('')}
          </div>
        </div>

        <div>
          <h3 class="text-sm font-semibold text-purple-400 mb-2 flex items-center justify-between">
            <span>🎬 Film Kaynakları (${playlists.film?.length || 0})</span>
          </h3>
          <div class="bg-slate-900/90 rounded-xl p-3 max-h-48 overflow-y-auto space-y-1 text-xs text-slate-300 border border-slate-800">
            ${(playlists.film || []).map((url: string) => `<div class="truncate text-slate-300 hover:text-white py-1 px-2 rounded hover:bg-slate-800/50">${url}</div>`).join('')}
          </div>
        </div>

        <div>
          <h3 class="text-sm font-semibold text-pink-400 mb-2 flex items-center justify-between">
            <span>🍿 Dizi Kaynakları (${playlists.dizi?.length || 0})</span>
          </h3>
          <div class="bg-slate-900/90 rounded-xl p-3 max-h-48 overflow-y-auto space-y-1 text-xs text-slate-300 border border-slate-800">
            ${(playlists.dizi || []).map((url: string) => `<div class="truncate text-slate-300 hover:text-white py-1 px-2 rounded hover:bg-slate-800/50">${url}</div>`).join('')}
          </div>
        </div>

        <div>
          <h3 class="text-sm font-semibold text-emerald-400 mb-2 flex items-center justify-between">
            <span>📅 EPG Rehber Kaynakları (${playlists.epg?.length || 0})</span>
          </h3>
          <div class="bg-slate-900/90 rounded-xl p-3 max-h-48 overflow-y-auto space-y-1 text-xs text-slate-300 border border-slate-800">
            ${(playlists.epg || []).map((url: string) => `<div class="truncate text-slate-300 hover:text-white py-1 px-2 rounded hover:bg-slate-800/50">${url}</div>`).join('')}
          </div>
        </div>
      </div>
    </div>
  </main>
</body>
</html>`;

  res.send(html);
});

app.listen(PORT, () => {
  console.log(`StreamMesh Portal sunucusu ${PORT} portunda çalışıyor.`);
});
