// StreamMesh Web Portal State Store

export const appState = {
    allChannels: [],
    currentTab: 'TV', // 'TV', 'Filmler', 'Diziler', 'ACE', 'FAVORİLER'
    currentSubFilter: 'Tümü',
    searchQuery: '',
    currentPage: 1,
    activeChannel: null,
    hlsInstance: null,
    favoritesSet: new Set(),
    aspectRatioMode: 0, // 0: 16:9, 1: 4:3, 2: Cover/Zoom
    webPlugins: [],
    activePluginFilterCategory: 'Tümü',
    selectedPluginId: null,
    focusedDpadIndex: 0
};
