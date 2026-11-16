import { toggleFavoriteActive } from './modules/favorites.js';
import { switchMediaTab, setSubFilter, handleSearch } from './modules/categoryFilter.js';
import { renderChannelList, changePage } from './modules/channelList.js';
import {
    playMedia,
    resumeActivePlayback,
    togglePlayPause,
    seekVideo,
    toggleMute,
    cycleAspectRatio,
    togglePiP,
    toggleFullscreen,
    copyCurrentStreamUrl,
    playManualStream,
    loadSampleAce,
    loadSampleUrl
} from './modules/player.js';
import {
    loadWebPlugins,
    openPluginsModal,
    closePluginsModal,
    setPluginCategoryFilter,
    filterWebPlugins
} from './modules/pluginsManager.js';
import { loadChannelData } from './modules/dataLoader.js';
import { initKeyHandler } from './modules/keyHandler.js';
import { openEpgGuideModal, closeEpgGuideModal, filterEpgGuide } from './modules/epgGuide.js';

// Bind methods globally on window for HTML inline handlers and cross-module hooks
window.toggleFavoriteActive = toggleFavoriteActive;
window.switchMediaTab = switchMediaTab;
window.setSubFilter = setSubFilter;
window.handleSearch = handleSearch;
window.renderChannelList = renderChannelList;
window.changePage = changePage;

window.playMedia = playMedia;
window.resumeActivePlayback = resumeActivePlayback;
window.togglePlayPause = togglePlayPause;
window.seekVideo = seekVideo;
window.toggleMute = toggleMute;
window.cycleAspectRatio = cycleAspectRatio;
window.togglePiP = togglePiP;
window.toggleFullscreen = toggleFullscreen;
window.copyCurrentStreamUrl = copyCurrentStreamUrl;
window.playManualStream = playManualStream;
window.loadSampleAce = loadSampleAce;
window.loadSampleUrl = loadSampleUrl;

window.openPluginsModal = openPluginsModal;
window.closePluginsModal = closePluginsModal;
window.setPluginCategoryFilter = setPluginCategoryFilter;
window.filterWebPlugins = filterWebPlugins;

window.openEpgGuideModal = openEpgGuideModal;
window.closeEpgGuideModal = closeEpgGuideModal;
window.filterEpgGuide = filterEpgGuide;

// Bootstrap application on DOM ready
window.addEventListener('DOMContentLoaded', () => {
    initKeyHandler();
    loadChannelData();
    loadWebPlugins();
});
