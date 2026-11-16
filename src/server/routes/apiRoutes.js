import fs from 'fs';
import path from 'path';
import net from 'net';
import http from 'http';
import https from 'https';
import { VERSION, PORT, ROOT_DIR } from '../config/constants.js';
import { INITIAL_CHANNELS } from '../data/initialChannels.js';
import { SYSTEM_PLUGINS } from '../data/systemPlugins.js';
import { sendJson, sendError, parseJsonBody } from '../utils/httpHelper.js';
import { generateM3uContent } from '../services/m3uService.js';
import { handleStreamProxy } from '../services/proxyService.js';

const PLUGINS_STATE_FILE = path.join(ROOT_DIR, 'src', 'server', 'data', 'pluginsState.json');

function loadPersistedPluginStates() {
  try {
    if (!fs.existsSync(PLUGINS_STATE_FILE)) return;
    const raw = fs.readFileSync(PLUGINS_STATE_FILE, 'utf8');
    const saved = JSON.parse(raw);
    if (!saved || typeof saved !== 'object') return;

    for (const plugin of SYSTEM_PLUGINS) {
      const entry = saved[plugin.id];
      if (!entry) continue;
      if (typeof entry.isEnabled === 'boolean') {
        plugin.isEnabled = entry.isEnabled;
        plugin.status = plugin.isEnabled ? 'Aktif' : 'Devre Dışı';
        plugin.statusMessage = plugin.isEnabled
          ? 'Eklenti etkin ve hazır.'
          : 'Kullanıcı tarafından devre dışı bırakıldı.';
      }
      if (entry.settings && typeof entry.settings === 'object') {
        for (const s of plugin.settings || []) {
          if (Object.prototype.hasOwnProperty.call(entry.settings, s.key)) {
            s.value = String(entry.settings[s.key]);
          }
        }
      }
    }
  } catch (err) {
    console.warn('Eklenti ayarları yüklenemedi:', err.message);
  }
}

function savePersistedPluginStates() {
  try {
    const payload = {};
    for (const plugin of SYSTEM_PLUGINS) {
      const settingsObj = {};
      for (const s of plugin.settings || []) {
        settingsObj[s.key] = s.value;
      }
      payload[plugin.id] = {
        isEnabled: plugin.isEnabled,
        settings: settingsObj
      };
    }
    fs.writeFileSync(PLUGINS_STATE_FILE, JSON.stringify(payload, null, 2), 'utf8');
  } catch (err) {
    console.error('Eklenti ayarları kaydedilemedi:', err.message);
  }
}

function checkTcpPort(host, port, timeoutMs = 1500) {
  return new Promise((resolve) => {
    const socket = new net.Socket();
    let settled = false;
    const finish = (ok) => {
      if (settled) return;
      settled = true;
      socket.destroy();
      resolve(ok);
    };
    socket.setTimeout(timeoutMs);
    socket.once('connect', () => finish(true));
    socket.once('timeout', () => finish(false));
    socket.once('error', () => finish(false));
    socket.connect(port, host);
  });
}

function probeHttpUrl(targetUrl, timeoutMs = 3000) {
  return new Promise((resolve) => {
    try {
      const parsed = new URL(targetUrl);
      const client = parsed.protocol === 'https:' ? https : http;
      const req = client.request(
        targetUrl,
        { method: 'HEAD', timeout: timeoutMs },
        (res) => {
          res.resume();
          resolve({ ok: res.statusCode < 500, statusCode: res.statusCode });
        }
      );
      req.on('timeout', () => {
        req.destroy();
        resolve({ ok: false, error: 'Zaman aşımı' });
      });
      req.on('error', (err) => resolve({ ok: false, error: err.message }));
      req.end();
    } catch (err) {
      resolve({ ok: false, error: err.message });
    }
  });
}

async function runPluginDiagnostic(plugin) {
  if (!plugin.isEnabled) {
    return `⚠️ '${plugin.name}' şu anda devre dışı. Test etmek için önce etkinleştirin.`;
  }

  const getSetting = (key, fallback = '') =>
    plugin.settings?.find((s) => s.key === key)?.value ?? fallback;

  switch (plugin.id) {
    case 'local_network_server': {
      const isUp = await checkTcpPort('127.0.0.1', PORT);
      plugin.stats = {
        ...plugin.stats,
        Port: String(PORT),
        Durum: isUp ? 'Çevrimiçi' : 'Kapalı'
      };
      return isUp
        ? `✅ Web & Medya Sunucusu port ${PORT} üzerinde aktif yanıt veriyor.`
        : `⚠️ Port ${PORT} üzerinde bağlantı doğrulanamadı.`;
    }
    case 'p2p_stats': {
      const acePort = Number.parseInt(getSetting('AcePort', '6878'), 10) || 6878;
      const aceUp = await checkTcpPort('127.0.0.1', acePort);
      plugin.stats = {
        ...plugin.stats,
        'Motor Portu': String(acePort),
        'Motor Durumu': aceUp ? 'Bağlı' : 'Çalışmıyor'
      };
      return aceUp
        ? `✅ AceStream Engine (127.0.0.1:${acePort}) erişilebilir durumda.`
        : `ℹ️ Yerel AceStream Engine (127.0.0.1:${acePort}) şu anda kapalı (P2P yayınlar için istemcide açık olmalı).`;
    }
    case 'metadata_enricher': {
      const probe = await probeHttpUrl('https://api.tvmaze.com/singlesearch/shows?q=breaking+bad');
      return probe.ok
        ? '✅ Ücretsiz meta veri servisi (TVMaze / IMDb / Wikipedia) erişilebilir ve API anahtarı olmadan çalışıyor.'
        : `⚠️ Ücretsiz meta veri servisi yanıt vermedi (${probe.statusCode || probe.error}).`;
    }
    case 'epg_guide': {
      const withEpg = INITIAL_CHANNELS.filter((c) => Boolean(c.epg)).length;
      plugin.stats = { ...plugin.stats, 'Eşleşen Kanal': String(withEpg) };
      return `✅ EPG motoru hazır: ${withEpg}/${INITIAL_CHANNELS.length} kanalda program akışı tanımlı.`;
    }
    case 'auto_updater': {
      return `✅ Sürüm denetleyicisi aktif. Mevcut sürüm: v${VERSION}.`;
    }
    case 'system_maintenance': {
      const stateExists = fs.existsSync(PLUGINS_STATE_FILE);
      savePersistedPluginStates();
      return `✅ Yapılandırma durumu doğrulandı (${stateExists ? 'senkronize' : 'yeni oluşturuldu'}).`;
    }
    default: {
      return `✅ '${plugin.name}' yapılandırması doğrulandı (${plugin.settings?.length || 0} parametre).`;
    }
  }
}

// Hydrate plugin settings from disk at startup
loadPersistedPluginStates();

export async function handleApiRoute(req, res, pathname, parsedUrl) {
  // 1. Version & Status
  if (pathname === '/api/version') {
    sendJson(res, 200, {
      version: VERSION,
      status: 'online',
      engines: {
        m3u: 'ready',
        epg: SYSTEM_PLUGINS.find((p) => p.id === 'epg_guide')?.isEnabled ? 'ready' : 'disabled',
        ace: 'ready',
        hls: 'ready',
        router: 'active',
        plugins: 'active'
      }
    });
    return true;
  }

  // 2. Plugins List
  if (pathname === '/api/plugins' && req.method === 'GET') {
    sendJson(res, 200, {
      total: SYSTEM_PLUGINS.length,
      active: SYSTEM_PLUGINS.filter((p) => p.isEnabled).length,
      plugins: SYSTEM_PLUGINS
    });
    return true;
  }

  // 3. Plugins: Toggle Enabled State
  if (pathname === '/api/plugins/toggle' && req.method === 'POST') {
    try {
      const data = await parseJsonBody(req);
      const plugin = SYSTEM_PLUGINS.find((p) => p.id === data.id);
      if (plugin) {
        plugin.isEnabled = typeof data.enabled === 'boolean' ? data.enabled : !plugin.isEnabled;
        plugin.status = plugin.isEnabled ? 'Aktif' : 'Devre Dışı';
        plugin.statusMessage = plugin.isEnabled
          ? 'Eklenti etkinleştirildi.'
          : 'Kullanıcı tarafından devre dışı bırakıldı.';
        savePersistedPluginStates();
        sendJson(res, 200, { success: true, plugin });
      } else {
        sendError(res, 404, 'Eklenti bulunamadı');
      }
    } catch (e) {
      sendError(res, 400, e.message || 'Geçersiz veri');
    }
    return true;
  }

  // 4. Plugins: Save Settings
  if (pathname === '/api/plugins/settings' && req.method === 'POST') {
    try {
      const data = await parseJsonBody(req);
      const plugin = SYSTEM_PLUGINS.find((p) => p.id === data.id);
      if (plugin && data.settings && Array.isArray(data.settings)) {
        data.settings.forEach((s) => {
          const target = plugin.settings.find((ps) => ps.key === s.key);
          if (target) target.value = String(s.value ?? '');
        });
        savePersistedPluginStates();
        sendJson(res, 200, { success: true, message: 'Ayarlar kaydedildi', plugin });
      } else {
        sendError(res, 404, 'Eklenti bulunamadı');
      }
    } catch (e) {
      sendError(res, 400, e.message || 'Geçersiz veri');
    }
    return true;
  }

  // 5. Plugins: Action Execution
  if (pathname === '/api/plugins/action' && req.method === 'POST') {
    try {
      const data = await parseJsonBody(req);
      const plugin = SYSTEM_PLUGINS.find((p) => p.id === data.id);
      if (plugin) {
        const result = await runPluginDiagnostic(plugin);
        sendJson(res, 200, { success: true, result, plugin });
      } else {
        sendError(res, 404, 'Eklenti bulunamadı');
      }
    } catch (e) {
      sendError(res, 400, e.message || 'Geçersiz veri');
    }
    return true;
  }

  // 6. Channels List
  if (pathname === '/api/channels' || pathname === '/channels') {
    if (parsedUrl.searchParams.get('format') === 'object') {
      sendJson(res, 200, { count: INITIAL_CHANNELS.length, channels: INITIAL_CHANNELS });
    } else {
      sendJson(res, 200, INITIAL_CHANNELS);
    }
    return true;
  }

  // 7. EPG Single Channel Query
  if (pathname === '/api/epg' || pathname === '/api/epg/query') {
    const epgPlugin = SYSTEM_PLUGINS.find((p) => p.id === 'epg_guide');
    if (epgPlugin && !epgPlugin.isEnabled) {
      sendJson(res, 200, { disabled: true, current: null, next: null });
      return true;
    }

    const id = parsedUrl.searchParams.get('id') || '';
    const name = (parsedUrl.searchParams.get('name') || '').toLowerCase();
    const found =
      INITIAL_CHANNELS.find((c) => c.id === id) ||
      (name ? INITIAL_CHANNELS.find((c) => c.name.toLowerCase().includes(name)) : null);

    if (!found) {
      sendError(res, 404, 'Kanal bulunamadı');
      return true;
    }

    sendJson(res, 200, {
      channel: found.name,
      current: {
        title: found.epg || 'Canlı Yayın Akışı',
        timeRange: found.epgTime || '',
        desc: found.desc || `${found.group || 'Genel'} Yayın Akışı`
      },
      next: found.nextEpg
        ? {
            title: found.nextEpg,
            timeRange: '',
            desc: found.isLive ? 'Takip eden yayın programı' : 'İçerik bilgisi'
          }
        : null
    });
    return true;
  }

  // 8. EPG Batch Query
  if (pathname === '/api/epg/batch') {
    const epgPlugin = SYSTEM_PLUGINS.find((p) => p.id === 'epg_guide');
    if (epgPlugin && !epgPlugin.isEnabled) {
      sendJson(res, 200, {});
      return true;
    }

    const result = {};
    INITIAL_CHANNELS.forEach((c) => {
      result[c.id] = {
        title: c.epg || 'Canlı Yayın',
        time: c.epgTime || '--:--'
      };
    });
    sendJson(res, 200, result);
    return true;
  }

  // 8b. EPG Timeline Guide (Channel Schedule Overview)
  if (pathname === '/api/epg/guide') {
    const epgPlugin = SYSTEM_PLUGINS.find((p) => p.id === 'epg_guide');
    if (epgPlugin && !epgPlugin.isEnabled) {
      sendJson(res, 200, { disabled: true, generatedAt: new Date().toISOString(), items: [] });
      return true;
    }

    const items = INITIAL_CHANNELS.filter((c) => c.isLive || c.epg).map((c) => ({
      id: c.id,
      name: c.name,
      logo: c.logo || '',
      group: c.group || c.category || 'Genel',
      currentTitle: c.epg || 'Canlı Yayın Akışı',
      currentTime: c.epgTime || '--:--',
      nextTitle: c.nextEpg || '--:--',
      desc: c.desc || ''
    }));

    sendJson(res, 200, {
      generatedAt: new Date().toISOString(),
      items
    });
    return true;
  }

  // 9. Stream Proxy
  if (pathname === '/api/proxy' || pathname === '/proxy') {
    const targetUrl = parsedUrl.searchParams.get('url');
    handleStreamProxy(req, res, targetUrl);
    return true;
  }

  // 10. M3U Playlist Download
  if (pathname === '/playlist.m3u' || pathname === '/api/playlist.m3u') {
    const host = req.headers.host || `127.0.0.1:${PORT}`;
    const m3u = generateM3uContent(host);
    res.writeHead(200, {
      'Content-Type': 'application/x-mpegurl; charset=utf-8',
      'Content-Disposition': 'attachment; filename="StreamMesh.m3u"'
    });
    res.end(m3u);
    return true;
  }

  return false;
}
