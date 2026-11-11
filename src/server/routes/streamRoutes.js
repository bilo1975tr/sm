import { INITIAL_CHANNELS } from '../data/initialChannels.js';
import { sendError } from '../utils/httpHelper.js';

/**
 * Handles /stream/:id route for direct playback or AceStream resolution.
 */
export function handleStreamRoute(req, res, pathname) {
  if (!pathname.startsWith('/stream/')) {
    return false;
  }

  const channelId = pathname.substring(8).trim().replace(/\/$/, '');
  const found = INITIAL_CHANNELS.find(c => c.id === channelId || c.id.toLowerCase() === channelId.toLowerCase());

  if (!found) {
    sendError(res, 404, `Kanal bulunamadı: ${channelId}`);
    return true;
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
    return true;
  }

  // Direct stream (HLS .m3u8 or MP4)
  res.writeHead(302, { 'Location': found.url });
  res.end();
  return true;
}
