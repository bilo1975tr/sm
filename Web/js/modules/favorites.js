import { appState } from './state.js';
import { showToast } from './ui.js';

/**
 * Initializes favorites list from localStorage.
 */
export function initFavorites() {
    try {
        const stored = JSON.parse(localStorage.getItem('streammesh_favorites') || '[]');
        appState.favoritesSet = new Set(Array.isArray(stored) ? stored : []);
    } catch (e) {
        appState.favoritesSet = new Set();
    }
}

/**
 * Persists favorites set to localStorage.
 */
export function saveFavorites() {
    try {
        localStorage.setItem('streammesh_favorites', JSON.stringify([...appState.favoritesSet]));
    } catch (e) {}
}

/**
 * Toggles favorite state of a channel by ID.
 */
export function toggleFavorite(id, e) {
    if (e && e.stopPropagation) e.stopPropagation();
    if (appState.favoritesSet.has(id)) {
        appState.favoritesSet.delete(id);
        showToast('Favorilerden çıkarıldı');
    } else {
        appState.favoritesSet.add(id);
        showToast('Favorilere eklendi ⭐');
    }
    saveFavorites();
    if (window.renderChannelList) window.renderChannelList();
    updateActiveFavButton();
}

/**
 * Toggles favorite status for currently playing channel.
 */
export function toggleFavoriteActive() {
    if (appState.activeChannel) {
        toggleFavorite(appState.activeChannel.id);
    }
}

/**
 * Updates favorite button label and color for the currently active channel.
 */
export function updateActiveFavButton() {
    const btn = document.getElementById('favActiveBtn');
    if (!btn || !appState.activeChannel) return;
    const isFav = appState.favoritesSet.has(appState.activeChannel.id);
    btn.innerText = isFav ? '★ Favoride' : '☆ Favori Ekle';
    btn.style.color = isFav ? 'var(--fav-gold)' : '';
}
