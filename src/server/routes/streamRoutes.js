import { INITIAL_CHANNELS } from '../data/initialChannels.js';
import { SYSTEM_PLUGINS } from '../data/systemPlugins.js';
import { sendError } from '../utils/httpHelper.js';

function getAcePort() {
  const p2pPlugin = SYSTEM_PLUGINS.find((p) => p.id === 'p2p_stats');
  const setting = p2pPlugin?.settings?.find((s) => s.key === 'AcePort');
  const port = Number.parseInt(setting?.value || '6878', 10);
  return Number.isFinite(port) && port > 0 && port <= 65535 ? port : 6878;
}

/**
 * Handles /stream/:id and /stream/ace?infohash=... routes.
 */
export function handleStreamRoute(req, res, pathname, parsedUrl) {
  if (!pathname.startsWith('/stream/')) {
    return false;
  }

  const channelId = pathname.substring(8).trim().replace(/\/$/, '');
  const acePort = getAcePort();

  // Direct manual AceStream hash resolution: /stream/ace?infohash=...
  if (channelId.toLowerCase() === 'ace') {
    const rawHash = (parsedUrl?.searchParams?.get('infohash') || '').replace('acestream://', '').trim();
    if (!/^[a-fA-F0-9]{40}$/.test(rawHash)) {
      sendError(res, 400, 'Geçersiz AceStream infohash parametresi (40 karakterlik hex bekleniyor)');
      return true;
    }
    res.writeHead(302, { Location: `http://127.0.0.1:${acePort}/ace/getstream?infohash=${rawHash}` });
    res.end();
    return true;
  }

  const found = INITIAL_CHANNELS.find(
    (c) => c.id === channelId || c.id.toLowerCase() === channelId.toLowerCase()
  );

  if (!found) {
    sendError(res, 404, `Kanal bulunamadı: ${channelId}`);
    return true;
  }

  // AceStream stream redirection
  if (found.sourceType === 'ACESTREAM' || found.url.startsWith('acestream://')) {
    let hash = found.url.replace('acestream://', '').trim();
    if (found.url.includes('infohash=')) {
      hash = found.url.split('infohash=')[1].split('&')[0];
    }
    const aceProxyUrl = `http://127.0.0.1:${acePort}/ace/getstream?infohash=${encodeURIComponent(hash)}`;
    res.writeHead(302, { Location: aceProxyUrl });
    res.end();
    return true;
  }

  // Direct stream (HLS .m3u8 or MP4)
  res.writeHead(302, { Location: found.url });
  res.end();
  return true;
}
