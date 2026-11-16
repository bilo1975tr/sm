import { FALLBACK_B64 } from './constants.js';
import { appState } from './state.js';
import { escapeHtml, showToast } from './ui.js';
import { playMedia } from './player.js';

let cachedGuideItems = [];

export async function openEpgGuideModal() {
    const modal = document.getElementById('epgGuideModal');
    const body = document.getElementById('epgGuideBody');
    const subtitle = document.getElementById('epgGuideSubtitle');
    if (!modal || !body) return;

    modal.style.display = 'flex';
    body.innerHTML = '<div style="color:var(--text-muted); text-align:center; padding:40px 20px;">Yayın rehberi yükleniyor...</div>';

    try {
        const res = await fetch('/api/epg/guide');
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();

        if (data.disabled) {
            cachedGuideItems = [];
            body.innerHTML = '<div style="color:var(--warning); text-align:center; padding:40px 20px;">EPG Rehberi eklentisi (epg_guide) şu anda devre dışı. Eklentiler menüsünden etkinleştirebilirsiniz.</div>';
            return;
        }

        cachedGuideItems = Array.isArray(data.items) ? data.items : [];
        if (subtitle) {
            subtitle.innerText = `${cachedGuideItems.length} kanal için güncel ve sıradaki program akışı`;
        }
        renderGuideItems(cachedGuideItems);
    } catch (err) {
        console.warn('EPG guide load error:', err);
        body.innerHTML = '<div style="color:var(--live-red); text-align:center; padding:40px 20px;">EPG rehberi yüklenemedi.</div>';
    }
}

export function closeEpgGuideModal() {
    const modal = document.getElementById('epgGuideModal');
    if (modal) modal.style.display = 'none';
}

export function filterEpgGuide() {
    const q = (document.getElementById('epgGuideSearchInput')?.value || '').trim().toLowerCase();
    if (!q) {
        renderGuideItems(cachedGuideItems);
        return;
    }
    const filtered = cachedGuideItems.filter((item) =>
        (item.name || '').toLowerCase().includes(q) ||
        (item.group || '').toLowerCase().includes(q) ||
        (item.currentTitle || '').toLowerCase().includes(q) ||
        (item.nextTitle || '').toLowerCase().includes(q)
    );
    renderGuideItems(filtered);
}

function renderGuideItems(items) {
    const body = document.getElementById('epgGuideBody');
    if (!body) return;
    body.innerHTML = '';

    if (!items || items.length === 0) {
        body.innerHTML = '<div style="color:var(--text-muted); text-align:center; padding:40px 20px;">Eşleşen program akışı bulunamadı.</div>';
        return;
    }

    items.forEach((item) => {
        const row = document.createElement('div');
        row.className = 'epg-guide-row';

        const logoSrc = item.logo && item.logo.trim() !== '' ? item.logo : FALLBACK_B64;
        const safeName = escapeHtml(item.name || 'Kanal');
        const safeGroup = escapeHtml(item.group || 'TV');
        const safeCurrent = escapeHtml(item.currentTitle || 'Canlı Yayın');
        const safeNext = escapeHtml(item.nextTitle || '--:--');
        const safeTime = escapeHtml(item.currentTime || '--:--');

        row.innerHTML = `
            <div class="epg-guide-channel">
                <img src="${escapeHtml(logoSrc)}" alt="${safeName}" loading="lazy" referrerpolicy="no-referrer">
                <div style="min-width:0;">
                    <div style="color:#fff; font-weight:700; font-size:13px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis;">${safeName}</div>
                    <div style="color:var(--text-muted); font-size:11px;">${safeGroup}</div>
                </div>
            </div>
            <div class="epg-guide-programs">
                <div class="epg-guide-current">🔴 ${safeCurrent}</div>
                <div class="epg-guide-next">Sonraki: ${safeNext}</div>
            </div>
            <div class="epg-guide-time">${safeTime}</div>
        `;

        const img = row.querySelector('img');
        if (img) {
            img.addEventListener('error', () => {
                if (img.src !== FALLBACK_B64) img.src = FALLBACK_B64;
            });
        }

        row.addEventListener('click', () => {
            const match = appState.allChannels.find((c) => c.id === item.id || c.name === item.name);
            if (match) {
                closeEpgGuideModal();
                playMedia(match);
            } else {
                showToast(`${item.name} oynatma listesinde bulunamadı.`);
            }
        });

        body.appendChild(row);
    });
}
