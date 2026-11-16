import { togglePlayPause, toggleFullscreen, toggleMute, seekVideo } from './player.js';
import { navigateDpad, activateDpadFocusedItem } from './ui.js';
import { closePluginsModal } from './pluginsManager.js';
import { closeEpgGuideModal } from './epgGuide.js';

/**
 * Initializes global TV remote D-Pad & keyboard hotkeys.
 */
export function initKeyHandler() {
    window.addEventListener('keydown', (e) => {
        const pluginsModal = document.getElementById('pluginsModal');
        const epgModal = document.getElementById('epgGuideModal');
        const isPluginsOpen = pluginsModal && pluginsModal.style.display !== 'none';
        const isEpgOpen = epgModal && epgModal.style.display !== 'none';
        const isModalOpen = isPluginsOpen || isEpgOpen;

        if (e.key === 'Escape') {
            if (isPluginsOpen) {
                e.preventDefault();
                closePluginsModal();
                return;
            }
            if (isEpgOpen) {
                e.preventDefault();
                closeEpgGuideModal();
                return;
            }
            if (document.fullscreenElement) {
                document.exitFullscreen().catch(() => {});
                return;
            }
            if (document.activeElement && document.activeElement !== document.body) {
                document.activeElement.blur();
            }
            return;
        }

        // Do not hijack player/D-Pad keys while a modal dialog is open
        if (isModalOpen) {
            return;
        }

        // Ignore when typing inside search, input, or select controls
        if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT') {
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
