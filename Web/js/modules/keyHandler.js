import { togglePlayPause, toggleFullscreen, toggleMute, seekVideo } from './player.js';
import { navigateDpad, activateDpadFocusedItem } from './ui.js';

/**
 * Initializes global TV remote D-Pad & keyboard hotkeys.
 */
export function initKeyHandler() {
    window.addEventListener('keydown', (e) => {
        // Ignore when typing inside search or input
        if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') {
            if (e.key === 'Escape') {
                e.target.blur();
            }
            return;
        }

        if (e.key === ' ') {
            e.preventDefault();
            togglePlayPause();
        } else if (e.key === 'f' || e.key === 'F') {
            e.preventDefault();
            toggleFullscreen();
        } else if (e.key === 'm' || e.key === 'M') {
            e.preventDefault();
            toggleMute();
        } else if (e.key === '/') {
            e.preventDefault();
            document.getElementById('filterInput')?.focus();
        } else if (e.key === 'ArrowLeft') {
            e.preventDefault();
            seekVideo(-10);
        } else if (e.key === 'ArrowRight') {
            e.preventDefault();
            seekVideo(10);
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            navigateDpad(-1);
        } else if (e.key === 'ArrowDown') {
            e.preventDefault();
            navigateDpad(1);
        } else if (e.key === 'Enter') {
            e.preventDefault();
            activateDpadFocusedItem();
        }
    });
}
