import http from 'http';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PORT = 3000;

const server = http.createServer((req, res) => {
  if (req.url === '/api/version') {
    let version = '0.0.1';
    try {
      if (fs.existsSync(path.join(__dirname, 'version.txt'))) {
        version = fs.readFileSync(path.join(__dirname, 'version.txt'), 'utf8').trim();
      }
    } catch (e) {}
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ version }));
    return;
  }

  let version = '0.0.1';
  try {
    if (fs.existsSync(path.join(__dirname, 'version.txt'))) {
      version = fs.readFileSync(path.join(__dirname, 'version.txt'), 'utf8').trim();
    }
  } catch (e) {}

  const html = `<!DOCTYPE html>
<html lang="tr">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>StreamMesh Hybrid v${version}</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
        body { background-color: #0f0f12; color: #f3f4f6; padding: 24px; min-height: 100vh; }
        .header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #27272a; padding-bottom: 16px; margin-bottom: 24px; }
        .logo { font-size: 22px; font-weight: bold; background: linear-gradient(135deg, #38bdf8, #818cf8); -webkit-background-clip: text; -webkit-text-fill-color: transparent; }
        .badge { background: #1f2937; color: #38bdf8; border: 1px solid #374151; padding: 4px 12px; border-radius: 12px; font-size: 13px; font-weight: 600; }
        .update-banner { background: linear-gradient(90deg, #991b1b, #be123c); color: white; padding: 12px 20px; border-radius: 8px; font-weight: 600; margin-bottom: 24px; display: flex; justify-content: space-between; align-items: center; box-shadow: 0 4px 12px rgba(225,29,72,0.3); }
        .update-btn { background: #ffffff; color: #991b1b; border: none; padding: 8px 16px; border-radius: 6px; font-weight: bold; cursor: pointer; transition: transform 0.1s; }
        .update-btn:hover { transform: scale(1.05); }
        .card-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 16px; margin-bottom: 24px; }
        .card { background: #18181b; border: 1px solid #27272a; border-radius: 10px; padding: 20px; }
        .card h3 { color: #f9fafb; font-size: 16px; margin-bottom: 8px; display: flex; align-items: center; gap: 8px; }
        .card p { color: #9ca3af; font-size: 14px; line-height: 1.5; }
        .status-dot { width: 8px; height: 8px; background-color: #22c55e; border-radius: 50%; display: inline-block; }
        .code-block { background: #09090b; padding: 12px; border-radius: 6px; font-family: monospace; font-size: 13px; color: #a1a1aa; border: 1px solid #18181b; }
    </style>
</head>
<body>
    <div class="header">
        <div class="logo">📺 StreamMesh Hybrid Desktop Engine</div>
        <div class="badge">Sürüm: v${version}</div>
    </div>

    <div class="update-banner">
        <div>⚡ Otomatik Güncelleme Sistemi Aktif! Sürüm bilgisi <code>version.txt</code> dosyasından çekiliyor.</div>
        <button class="update-btn" onclick="alert('Masaüstü uygulamasında sağ üst köşedeki günceleme butonuna tıklayarak GitHub entegrasyonuyla otomatik güncelleyebilirsiniz.')">Güncelleme Bilgisi</button>
    </div>

    <div class="card-grid">
        <div class="card">
            <h3><span class="status-dot"></span> C# WPF & Windows Entegrasyonu</h3>
            <p>Uygulama C# .NET WPF mimarisinde hazırlandı. Tepsi (Tray) simgesi, AceStream motoru ve LibVLC oynatıcı entegre edildi.</p>
        </div>
        <div class="card">
            <h3><span class="status-dot"></span> P2P AceStream & Global Arama</h3>
            <p>Kanal arama motoru IP-TV Cat, AceStream Hash/Content ID, search-ace.stream ve yerel motor üzerinden genişletildi.</p>
        </div>
        <div class="card">
            <h3><span class="status-dot"></span> Sürüm Takibi (version.txt)</h3>
            <p>Uygulama versiyonu <strong>version.txt</strong> ve <strong>VERSION</strong> dosyalarından okunmakta olup v${version} ile günceldir.</p>
        </div>
    </div>

    <div class="card">
        <h3>📂 Proje Konfigürasyonu</h3>
        <p style="margin-bottom: 12px;">C# Solution ve WPF Derleme Bilgileri:</p>
        <div class="code-block">
            StreamMesh.csproj -> TargetFramework: net8.0-windows<br>
            Version: ${version}<br>
            AutoUpdate Source: raw.githubusercontent.com/bilo1975tr/sm/refs/heads/main/version.txt
        </div>
    </div>
</body>
</html>`;

  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(html);
});

server.listen(PORT, '0.0.0.0', () => {
  console.log(`StreamMesh Dev Server running on http://0.0.0.0:${PORT}`);
});
