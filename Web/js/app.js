import { appState } from './modules/state.js';
import { showToast, navigateDpad, activateDpadFocusedItem } from './modules/ui.js';
import { initFavorites, saveFavorites, toggleFavorite, toggleFavoriteActive, updateActiveFavButton } from './modules/favorites.js';
import { switchMediaTab, updateSubCategoryPills, setSubFilter, handleSearch, getFilteredItems } from './modules/categoryFilter.js';
import { renderChannelList, changePage } from './modules/channelList.js';
import {
    playMedia,
    handleAutoplayBlock,
    resumeActivePlayback,
    showOverlayError,
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
    updatePluginsBadge,
    openPluginsModal,
    closePluginsModal,
    setPluginCategoryFilter,
    filterWebPlugins,
    renderWebPluginsList,
    selectWebPlugin,
    toggleWebPlugin,
    saveWebPluginSettings,
    resetWebPluginSettings,
    executeWebPluginAction
} from './modules/pluginsManager.js';
import { loadChannelData } from './modules/dataLoader.js';
import { initKeyHandler } from './modules/keyHandler.js';

// Bind methods globally on window for backwards compatibility with HTML inline onclick handlers
window.appState = appState;
window.showToast = showToast;
window.navigateDpad = navigateDpad;
window.activateDpadFocusedItem = activateDpadFocusedItem;

window.initFavorites = initFavorites;
window.saveFavorites = saveFavorites;
window.toggleFavorite = toggleFavorite;
window.toggleFavoriteActive = toggleFavoriteActive;
window.updateActiveFavButton = updateActiveFavButton;

window.switchMediaTab = switchMediaTab;
window.updateSubCategoryPills = updateSubCategoryPills;
window.setSubFilter = setSubFilter;
window.handleSearch = handleSearch;
window.getFilteredItems = getFilteredItems;

window.renderChannelList = renderChannelList;
window.changePage = changePage;

window.playMedia = playMedia;
window.handleAutoplayBlock = handleAutoplayBlock;
window.resumeActivePlayback = resumeActivePlayback;
window.showOverlayError = showOverlayError;
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

window.loadWebPlugins = loadWebPlugins;
window.updatePluginsBadge = updatePluginsBadge;
window.openPluginsModal = openPluginsModal;
window.closePluginsModal = closePluginsModal;
window.setPluginCategoryFilter = setPluginCategoryFilter;
window.filterWebPlugins = filterWebPlugins;
window.renderWebPluginsList = renderWebPluginsList;
window.selectWebPlugin = selectWebPlugin;
window.toggleWebPlugin = toggleWebPlugin;
window.saveWebPluginSettings = saveWebPluginSettings;
window.resetWebPluginSettings = resetWebPluginSettings;
window.executeWebPluginAction = executeWebPluginAction;

window.loadChannelData = loadChannelData;

// Bootstrap application on DOM ready
window.addEventListener('DOMContentLoaded', () => {
    initKeyHandler();
    loadChannelData();
    loadWebPlugins();
});
