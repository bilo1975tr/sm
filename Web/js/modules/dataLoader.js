import { DEFAULT_CATALOG } from './constants.js';
import { appState } from './state.js';
import { normalizeChannel } from './channelNormalizer.js';
import { initFavorites } from './favorites.js';
import { renderChannelList } from './channelList.js';
import { playMedia, initContinueWatching } from './player.js';

/**
 * Updates footer version badge from /api/version.
 */
async function syncAppVersion() {
    try {
        const res = await fetch('/api/version');
        if (res.ok) {
            const data = await res.json();
            const label = document.getElementById('appVersionLabel');
            if (label && data && data.version) {
                label.innerText = 'v' + String(data.version).replace(/^v/i, '');
            }
        }
    } catch {
        // Ignore version sync failure
    }
}

/**
 * Loads channel data from Node.js / C# API with seamless fallback to built-in catalog.
 */
export async function loadChannelData() {
    initFavorites();
    initContinueWatching();
    syncAppVersion();
    let loaded = false;

    // Try primary /api/channels endpoint
    try {
        let res = await fetch('/api/channels');
        if (!res.ok) res = await fetch('/channels');
        if (res.ok) {
            const data = await res.json();
            let items = [];
            if (Array.isArray(data)) {
                items = data;
            } else if (data && Array.isArray(data.channels)) {
                items = data.channels;
            }
            if (items.length > 0) {
                appState.allChannels = items.map(normalizeChannel);
                loaded = true;
            }
        }
    } catch (e) {
        console.warn('API kanalları alınamadı, yerleşik katalog kullanılıyor:', e);
    }

    // Fallback to rich built-in catalog if API was empty/unavailable
    if (!loaded || appState.allChannels.length === 0) {
        appState.allChannels = DEFAULT_CATALOG.map(normalizeChannel);
    }

    renderChannelList();

    // Auto-select first channel
    if (appState.allChannels.length > 0 && !appState.activeChannel) {
        playMedia(appState.allChannels[0]);
    }
}
