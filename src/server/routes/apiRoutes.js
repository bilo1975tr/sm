import { VERSION, PORT } from '../config/constants.js';
import { INITIAL_CHANNELS } from '../data/initialChannels.js';
import { SYSTEM_PLUGINS } from '../data/systemPlugins.js';
import { sendJson, sendError, parseJsonBody } from '../utils/httpHelper.js';
import { generateM3uContent } from '../services/m3uService.js';
import { handleStreamProxy } from '../services/proxyService.js';

export async function handleApiRoute(req, res, pathname, parsedUrl) {
  // 1. Version & Status
  if (pathname === '/api/version') {
    sendJson(res, 200, {
      version: VERSION,
      status: 'online',
      engines: { m3u: 'ready', epg: 'ready', ace: 'ready', hls: 'ready', router: 'active', plugins: 'active' }
    });
    return true;
  }

  // 2. Plugins List
  if (pathname === '/api/plugins' && req.method === 'GET') {
    sendJson(res, 200, {
      total: SYSTEM_PLUGINS.length,
      active: SYSTEM_PLUGINS.filter(p => p.isEnabled).length,
      plugins: SYSTEM_PLUGINS
    });
    return true;
  }

  // 3. Plugins: Toggle Enabled State
  if (pathname === '/api/plugins/toggle' && req.method === 'POST') {
    try {
      const data = await parseJsonBody(req);
      const plugin = SYSTEM_PLUGINS.find(p => p.id === data.id);
      if (plugin) {
        plugin.isEnabled = typeof data.enabled === 'boolean' ? data.enabled : !plugin.isEnabled;
        plugin.status = plugin.isEnabled ? 'Aktif' : 'Devre Dışı';
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
      const plugin = SYSTEM_PLUGINS.find(p => p.id === data.id);
      if (plugin && data.settings && Array.isArray(data.settings)) {
        data.settings.forEach(s => {
          const target = plugin.settings.find(ps => ps.key === s.key);
          if (target) target.value = s.value;
        });
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
      const plugin = SYSTEM_PLUGINS.find(p => p.id === data.id);
      if (plugin) {
        const action = data.action || 'test';
        sendJson(res, 200, {
          success: true,
          result: `✅ '${plugin.name}' eklentisi '${action}' işlemini başarıyla tamamladı!`
        });
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
    const id = parsedUrl.searchParams.get('id') || '';
    const name = (parsedUrl.searchParams.get('name') || '').toLowerCase();
    const found = INITIAL_CHANNELS.find(c => c.id === id || c.name.toLowerCase().includes(name)) || INITIAL_CHANNELS[0];

    sendJson(res, 200, {
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
    });
    return true;
  }

  // 8. EPG Batch Query
  if (pathname === '/api/epg/batch') {
    const result = {};
    INITIAL_CHANNELS.forEach(c => {
      result[c.id] = {
        title: c.epg || "Canlı Yayın",
        time: c.epgTime || "--:--"
      };
    });
    sendJson(res, 200, result);
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
