import { appState } from './state.js';
import { resetDpadFocus } from './ui.js';

/**
 * Filters allChannels based on current tab, sub-category, and search query.
 */
export function getFilteredItems() {
    const items = appState.allChannels.filter(c => {
        // Tab filter
        if (appState.currentTab === 'FAVORİLER') {
            if (!appState.favoritesSet.has(c.id)) return false;
        } else if (appState.currentTab === 'DEVAM_ET') {
            if (!appState.continueWatchingMap.has(c.id)) return false;
        } else if (appState.currentTab === 'TV') {
            if (c.category !== 'TV') return false;
        } else if (appState.currentTab === 'Filmler') {
            if (c.category !== 'Filmler') return false;
        } else if (appState.currentTab === 'Diziler') {
            if (c.category !== 'Diziler') return false;
        }

        // Sub-category filter
        if (appState.currentSubFilter !== 'Tümü') {
            const subLower = appState.currentSubFilter.toLowerCase();
            const groupMatch = (c.group || '').toLowerCase().includes(subLower);
            const nameMatch = (c.name || '').toLowerCase().includes(subLower);
            const sourceMatch = (c.sourceType || '').toLowerCase().includes(subLower);
            if (!groupMatch && !nameMatch && !sourceMatch) return false;
        }

        // Search query
        if (appState.searchQuery) {
            const n = (c.name || '').toLowerCase();
            const g = (c.group || '').toLowerCase();
            const e = (c.epg || '').toLowerCase();
            if (!n.includes(appState.searchQuery) && !g.includes(appState.searchQuery) && !e.includes(appState.searchQuery)) {
                return false;
            }
        }

        return true;
    });

    if (appState.currentTab === 'DEVAM_ET') {
        items.sort((a, b) => {
            const ta = appState.continueWatchingMap.get(a.id)?.timestamp || 0;
            const tb = appState.continueWatchingMap.get(b.id)?.timestamp || 0;
            return tb - ta;
        });
    }

    return items;
}

/**
 * Handles switching between TV, Filmler, Diziler, DEVAM_ET, ACE, and FAVORİLER tabs.
 */
export function switchMediaTab(tabName) {
    appState.currentTab = tabName;
    appState.currentPage = 1;
    resetDpadFocus();

    document.querySelectorAll('.tab-btn').forEach(b => {
        b.classList.remove('active');
    });

    if (tabName === 'TV') document.getElementById('tabLiveTv')?.classList.add('active');
    else if (tabName === 'Filmler') document.getElementById('tabMovies')?.classList.add('active');
    else if (tabName === 'Diziler') document.getElementById('tabSeries')?.classList.add('active');
    else if (tabName === 'DEVAM_ET') document.getElementById('tabContinue')?.classList.add('active');
    else if (tabName === 'ACE') document.getElementById('tabAce')?.classList.add('active');
    else if (tabName === 'FAVORİLER') document.getElementById('tabFavs')?.classList.add('active');

    const pills = document.getElementById('subCategoryPills');
    const mediaList = document.getElementById('mediaList');
    const quickPlay = document.getElementById('quickPlayArea');
    const pagination = document.getElementById('paginationBar');

    if (tabName === 'ACE') {
        if (pills) pills.style.display = 'none';
        if (mediaList) mediaList.style.display = 'none';
        if (quickPlay) quickPlay.style.display = 'flex';
        if (pagination) pagination.style.display = 'none';
        const countLbl = document.getElementById('itemCountLabel');
        const pageLbl = document.getElementById('pageCountLabel');
        if (countLbl) countLbl.innerText = 'AceStream & Manuel Oynatıcı';
        if (pageLbl) pageLbl.innerText = 'Doğrudan Giriş';
        return;
    } else {
        if (pills) pills.style.display = 'flex';
        if (mediaList) mediaList.style.display = 'flex';
        if (quickPlay) quickPlay.style.display = 'none';
        if (pagination) pagination.style.display = 'flex';
    }

    updateSubCategoryPills(tabName);
    if (window.renderChannelList) window.renderChannelList();
    if (mediaList) mediaList.scrollTop = 0;
}

/**
 * Dynamically updates subcategory filter pills depending on active media tab.
 */
export function updateSubCategoryPills(tabName) {
    const container = document.getElementById('subCategoryPills');
    if (!container) return;
    appState.currentSubFilter = 'Tümü';

    if (tabName === 'TV') {
        container.innerHTML = `
            <button class="sub-pill active" onclick="setSubFilter('Tümü', this)">Tümü</button>
            <button class="sub-pill" onclick="setSubFilter('Ulusal', this)">Ulusal</button>
            <button class="sub-pill" onclick="setSubFilter('Haber', this)">Haber</button>
            <button class="sub-pill" onclick="setSubFilter('Spor', this)">Spor</button>
            <button class="sub-pill" onclick="setSubFilter('Belgesel', this)">Belgesel</button>
            <button class="sub-pill" onclick="setSubFilter('Müzik', this)">Müzik</button>
            <button class="sub-pill" onclick="setSubFilter('Çocuk', this)">Çocuk</button>
            <button class="sub-pill" onclick="setSubFilter('AceStream', this)">AceStream</button>
        `;
    } else if (tabName === 'Filmler') {
        container.innerHTML = `
            <button class="sub-pill active" onclick="setSubFilter('Tümü', this)">Tümü</button>
            <button class="sub-pill" onclick="setSubFilter('Animasyon', this)">Animasyon</button>
            <button class="sub-pill" onclick="setSubFilter('Bilim Kurgu', this)">Bilim Kurgu</button>
            <button class="sub-pill" onclick="setSubFilter('Macera', this)">Macera</button>
        `;
    } else if (tabName === 'Diziler') {
        container.innerHTML = `
            <button class="sub-pill active" onclick="setSubFilter('Tümü', this)">Tümü</button>
            <button class="sub-pill" onclick="setSubFilter('Dizi', this)">Diziler</button>
        `;
    } else {
        container.innerHTML = `<button class="sub-pill active" onclick="setSubFilter('Tümü', this)">Tümü</button>`;
    }
}

/**
 * Sets active sub-category filter.
 */
export function setSubFilter(filterName, btnEl) {
    appState.currentSubFilter = filterName;
    appState.currentPage = 1;
    resetDpadFocus();
    document.querySelectorAll('#subCategoryPills .sub-pill').forEach(b => b.classList.remove('active'));
    if (btnEl) btnEl.classList.add('active');
    if (window.renderChannelList) window.renderChannelList();
    const mediaList = document.getElementById('mediaList');
    if (mediaList) mediaList.scrollTop = 0;
}

/**
 * Handles text input search filtering.
 */
export function handleSearch() {
    const input = document.getElementById('filterInput');
    appState.searchQuery = input ? input.value.toLowerCase().trim() : '';
    appState.currentPage = 1;
    resetDpadFocus();
    if (window.renderChannelList) window.renderChannelList();
}
