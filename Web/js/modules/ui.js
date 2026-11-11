import { appState } from './state.js';

/**
 * Displays a non-blocking toast message.
 */
export function showToast(msg) {
    const t = document.getElementById('toast');
    if (!t) return;
    t.innerText = msg;
    t.classList.add('show');
    setTimeout(() => t.classList.remove('show'), 2600);
}

/**
 * Navigates through cards using TV Box remote or keyboard arrow keys.
 */
export function navigateDpad(direction) {
    const cards = document.querySelectorAll('.media-card');
    if (cards.length === 0) return;

    cards.forEach(c => c.classList.remove('dpad-focus'));
    appState.focusedDpadIndex = Math.max(0, Math.min(cards.length - 1, appState.focusedDpadIndex + direction));

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
    if (cards[appState.focusedDpadIndex]) {
        cards[appState.focusedDpadIndex].click();
    }
}
