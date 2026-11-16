import { appState } from './state.js';

/**
 * Escapes HTML special characters to prevent XSS when inserting dynamic data into DOM.
 */
export function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

/**
 * Displays a non-blocking toast message.
 */
export function showToast(msg) {
    const t = document.getElementById('toast');
    if (!t) return;
    t.innerText = msg;
    t.classList.add('show');
    clearTimeout(t._hideTimer);
    t._hideTimer = setTimeout(() => t.classList.remove('show'), 2600);
}

/**
 * Resets D-Pad focus index to the top of the current list.
 */
export function resetDpadFocus() {
    appState.focusedDpadIndex = 0;
}

/**
 * Navigates through cards using TV Box remote or keyboard arrow keys.
 */
export function navigateDpad(direction) {
    const cards = document.querySelectorAll('.media-card');
    if (cards.length === 0) return;

    cards.forEach(c => c.classList.remove('dpad-focus'));
    const clampedCurrent = Math.max(0, Math.min(cards.length - 1, appState.focusedDpadIndex));
    appState.focusedDpadIndex = Math.max(0, Math.min(cards.length - 1, clampedCurrent + direction));

    const target = cards[appState.focusedDpadIndex];
    if (target) {
        target.classList.add('dpad-focus');
        target.focus();
        target.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
}

/**
 * Activates (clicks) the currently highlighted D-pad card.
 */
export function activateDpadFocusedItem() {
    const cards = document.querySelectorAll('.media-card');
    if (cards.length === 0) return;
    const idx = Math.max(0, Math.min(cards.length - 1, appState.focusedDpadIndex));
    if (cards[idx]) {
        cards[idx].click();
    }
}
