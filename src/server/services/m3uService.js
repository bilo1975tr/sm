import { INITIAL_CHANNELS } from '../data/initialChannels.js';

/**
 * Generates an M3U playlist string containing all channels.
 */
export function generateM3uContent(host, channels = INITIAL_CHANNELS) {
  let m3u = '#EXTM3U name="StreamMesh Web Playlist"\n';
  channels.forEach(ch => {
    const playUrl = ch.url.startsWith('http') ? ch.url : `http://${host}/stream/${ch.id}`;
    m3u += `#EXTINF:-1 tvg-id="${ch.id}" tvg-name="${ch.name}" tvg-logo="${ch.logo}" group-title="${ch.group || ch.category}",${ch.name}\n`;
    m3u += `${playUrl}\n`;
  });
  return m3u;
}
