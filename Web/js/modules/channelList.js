import { PAGE_SIZE, FALLBACK_B64 } from './constants.js';
import { appState } from './state.js';
import { getFilteredItems } from './categoryFilter.js';

/**
 * Renders the channel cards for the current page and active filters.
 */
export function renderChannelList() {
    const container = document.getElementById('mediaList');
    if (!container) return;

    const filtered = getFilteredItems();
    const totalCount = filtered.length;
    const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

    if (appState.currentPage > totalPages) appState.currentPage = totalPages;
    if (appState.currentPage < 1) appState.currentPage = 1;

    const startIndex = (appState.currentPage - 1) * PAGE_SIZE;
    const pageItems = filtered.slice(startIndex, startIndex + PAGE_SIZE);

    const rangeStart = totalCount > 0 ? startIndex + 1 : 0;
    const rangeEnd = Math.min(startIndex + PAGE_SIZE, totalCount);

    const itemLbl = document.getElementById('itemCountLabel');
    const pageCountLbl = document.getElementById('pageCountLabel');
    const pageInd = document.getElementById('pageIndicator');
    const prevBtn = document.getElementById('prevBtn');
    const nextBtn = document.getElementById('nextBtn');

    if (itemLbl) itemLbl.innerText = `${totalCount} İçerik (${rangeStart}–${rangeEnd})`;
    if (pageCountLbl) pageCountLbl.innerText = `Sayfa ${appState.currentPage} / ${totalPages}`;
    if (pageInd) pageInd.innerText = `${appState.currentPage} / ${totalPages}`;

    if (prevBtn) prevBtn.disabled = (appState.currentPage <= 1);
    if (nextBtn) nextBtn.disabled = (appState.currentPage >= totalPages);

    container.innerHTML = '';

    if (pageItems.length === 0) {
        const empty = document.createElement('div');
        empty.style.padding = '36px 16px';
        empty.style.textAlign = 'center';
        empty.style.color = 'var(--text-muted)';
        empty.style.fontSize = '13px';
        empty.innerHTML = appState.currentTab === 'FAVORİLER'
            ? 'Henüz favori içerik eklenmedi.<br>Kartlardaki yıldız ikonuna (☆) dokunarak ekleyebilirsiniz.'
            : 'Arama kriterlerine uygun içerik bulunamadı.';
        container.appendChild(empty);
        return;
    }

    pageItems.forEach((ch) => {
        const isActive = appState.activeChannel && appState.activeChannel.id === ch.id;
        const isFav = appState.favoritesSet.has(ch.id);
        const logoSrc = ch.logo && ch.logo.trim() !== '' ? ch.logo : FALLBACK_B64;

        const card = document.createElement('div');
        card.className = 'media-card' + (isActive ? ' active' : '');
        card.tabIndex = 0; // Accessible for TV Box D-Pad focus
        card.setAttribute('data-nav', 'card');
        card.onclick = () => {
            if (window.playMedia) window.playMedia(ch);
        };

        let badgeTypeClass = 'type-direct';
        let badgeText = ch.sourceType;
        if (ch.sourceType === 'ACESTREAM') {
            badgeTypeClass = 'type-ace';
            badgeText = 'AceStream P2P';
        } else if (ch.category === 'Filmler' || ch.category === 'Diziler') {
            badgeTypeClass = 'type-vod';
            badgeText = 'VOD';
        }

        card.innerHTML = `
            <div class="card-thumb-wrap">
                <img src="${logoSrc}" alt="${ch.name}" loading="lazy" referrerpolicy="no-referrer" onerror="this.onerror=null;this.src='${FALLBACK_B64}'">
            </div>
            <div class="card-body">
                <div class="card-top">
                    <span class="card-name">${ch.name}</span>
                    <button class="card-fav-btn ${isFav ? 'is-fav' : ''}" onclick="toggleFavorite('${ch.id}', event)" title="Favori">
                        ${isFav ? '★' : '☆'}
                    </button>
                </div>
                <div class="card-sub">
                    <span class="type-tag ${badgeTypeClass}">${badgeText}</span>
                    <span>•</span>
                    <span>${ch.group || ch.category}</span>
                    <span>•</span>
                    <span>${ch.quality}</span>
                </div>
                <div class="card-epg">
                    ${ch.epg ? '🔴 ' + ch.epg + (ch.epgTime ? ' (' + ch.epgTime + ')' : '') : 'Canlı Yayın Akışı'}
                </div>
            </div>
        `;

        container.appendChild(card);
    });
}

/**
 * Changes channel page by delta (-1 or +1).
 */
export function changePage(delta) {
    const filtered = getFilteredItems();
    const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
    const newPage = appState.currentPage + delta;
    if (newPage >= 1 && newPage <= totalPages) {
        appState.currentPage = newPage;
        renderChannelList();
        const mediaList = document.getElementById('mediaList');
        if (mediaList) mediaList.scrollTop = 0;
    }
}
