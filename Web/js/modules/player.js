import { FALLBACK_B64 } from './constants.js';
import { appState } from './state.js';
import { showToast, escapeHtml } from './ui.js';
import { updateActiveFavButton } from './favorites.js';
import { switchMediaTab } from './categoryFilter.js';

let videoEventsBound = false;
let lastSavedProgressAt = 0;

const CONTINUE_STORAGE_KEY = 'streammesh_continue_watching';

export function initContinueWatching() {
    try {
        const raw = localStorage.getItem(CONTINUE_STORAGE_KEY);
        const list = raw ? JSON.parse(raw) : [];
        appState.continueWatchingMap = new Map();
        if (Array.isArray(list)) {
            list.forEach((entry) => {
                if (entry && entry.id) {
                    appState.continueWatchingMap.set(entry.id, {
                        id: entry.id,
                        timestamp: Number(entry.timestamp) || Date.now(),
                        lastTimeSec: Number(entry.lastTimeSec) || 0
                    });
                }
            });
        }
    } catch {
        appState.continueWatchingMap = new Map();
    }
}

function persistContinueWatching() {
    try {
        const list = [...appState.continueWatchingMap.values()]
            .sort((a, b) => b.timestamp - a.timestamp)
            .slice(0, 30);
        appState.continueWatchingMap = new Map(list.map((e) => [e.id, e]));
        localStorage.setItem(CONTINUE_STORAGE_KEY, JSON.stringify(list));
    } catch {}
}

function recordWatchHistory(ch, timeSec = null) {
    if (!ch || !ch.id) return;
    const prev = appState.continueWatchingMap.get(ch.id);
    const nextSec = typeof timeSec === 'number' && !Number.isNaN(timeSec)
        ? Math.floor(timeSec)
        : (prev ? prev.lastTimeSec : 0);
    appState.continueWatchingMap.set(ch.id, {
        id: ch.id,
        timestamp: Date.now(),
        lastTimeSec: nextSec
    });
    persistContinueWatching();
}

/**
 * Gets video element safely.
 */
function getVideoElement() {
    return document.getElementById('playerVideo');
}

function syncTransportButtons(video) {
    const playPauseBtn = document.getElementById('playPauseBtn');
    const muteBtn = document.getElementById('muteBtn');
    if (playPauseBtn && video) {
        playPauseBtn.innerText = video.paused ? '▶ Oynat' : '⏸ Duraklat';
    }
    if (muteBtn && video) {
        muteBtn.innerText = (video.muted || video.volume === 0) ? '🔇 Sessiz' : '🔊 Ses';
    }
}

function bindVideoStateEvents(video) {
    if (!video || videoEventsBound) return;
    videoEventsBound = true;
    video.addEventListener('play', () => syncTransportButtons(video));
    video.addEventListener('pause', () => syncTransportButtons(video));
    video.addEventListener('volumechange', () => syncTransportButtons(video));
}

/**
 * Initiates playback with intelligent candidate failover.
 */
export function playMedia(ch) {
    appState.activeChannel = ch;
    recordWatchHistory(ch);
    if (window.renderChannelList) window.renderChannelList();
    updateActiveFavButton();

    const video = getVideoElement();
    if (!video) return;
    bindVideoStateEvents(video);

    const overlay = document.getElementById('playerOverlay');
    const icon = document.getElementById('overlayIcon');
    const spinner = document.getElementById('overlaySpinner');
    const title = document.getElementById('overlayTitle');
    const desc = document.getElementById('overlayDesc');
    const btn = document.getElementById('overlayBtn');

    const activeTitle = document.getElementById('activeTitle');
    const activeLogo = document.getElementById('activeLogo');
    const activeSub = document.getElementById('activeSub');
    const liveTagText = document.getElementById('liveTagText');
    const resolutionBadge = document.getElementById('resolutionBadge');
    const epgNowTitle = document.getElementById('epgNowTitle');
    const epgNowTime = document.getElementById('epgNowTime');
    const epgNextTitle = document.getElementById('epgNextTitle');

    if (activeTitle) activeTitle.innerText = ch.name;
    if (activeLogo) {
        activeLogo.onerror = () => {
            if (activeLogo.src !== FALLBACK_B64) activeLogo.src = FALLBACK_B64;
        };
        activeLogo.src = ch.logo && ch.logo.trim() !== '' ? ch.logo : FALLBACK_B64;
    }
    if (activeSub) {
        activeSub.innerHTML = `Kategori: <strong>${escapeHtml(ch.group || ch.category)}</strong> • ${escapeHtml(ch.quality)} • ${escapeHtml(ch.sourceType)}`;
    }
    if (liveTagText) liveTagText.innerText = ch.isLive ? 'CANLI YAYIN' : 'VOD VİDEO';
    if (resolutionBadge) resolutionBadge.innerText = ch.quality + ' · ' + (ch.url && ch.url.endsWith('.mp4') ? 'MP4' : 'HLS/VOD');

    if (epgNowTitle) epgNowTitle.innerText = ch.epg || (ch.isLive ? 'Canlı Yayın' : ch.name);
    if (epgNowTime) epgNowTime.innerText = ch.epgTime ? `(${ch.epgTime})` : '';
    if (epgNextTitle) {
        epgNextTitle.innerText = ch.nextEpg
            ? `Sonraki: ${ch.nextEpg}`
            : (ch.isLive ? 'Sonraki: Yayın Akışı' : 'İsteğe Bağlı İçerik (VOD)');
    }

    // Clean previous Hls.js instance
    if (appState.hlsInstance) {
        try { appState.hlsInstance.destroy(); } catch (e) {}
        appState.hlsInstance = null;
    }

    video.pause();
    video.removeAttribute('src');
    video.load();
    syncTransportButtons(video);

    // Display loading overlay without clickable error/resume trap
    if (overlay) {
        overlay.style.display = 'flex';
        overlay.style.cursor = 'default';
        overlay.onclick = null;
    }
    if (icon) icon.style.display = 'none';
    if (spinner) spinner.style.display = 'block';
    if (btn) btn.style.display = 'none';
    if (title) title.innerText = 'Yayın Başlatılıyor...';
    if (desc) desc.innerText = `${ch.name} akış kaynağı hazırlanıyor...`;

    // Build priority stream candidates list
    const candidates = [];

    // 1. AceStream handling
    if (ch.sourceType === 'ACESTREAM' || (ch.url && ch.url.startsWith('acestream://'))) {
        let hash = (ch.url || '').replace('acestream://', '').trim();
        if (hash.includes('infohash=')) {
            hash = hash.split('infohash=')[1].split('&')[0];
        }
        candidates.push(`http://127.0.0.1:6878/ace/getstream?infohash=${encodeURIComponent(hash)}`);
        if (ch.streamUrl) candidates.push(ch.streamUrl);
    } else {
        // 2. Regular TV / VOD
        if (ch.streamUrl) candidates.push(ch.streamUrl);
        if (ch.url && ch.url !== ch.streamUrl) candidates.push(ch.url);
        if (ch.url) candidates.push(`/api/proxy?url=${encodeURIComponent(ch.url)}`);
    }

    let candidateIndex = 0;

    function hideOverlay() {
        const ov = document.getElementById('playerOverlay');
        if (ov) {
            ov.style.display = 'none';
            ov.onclick = null;
        }
    }

    video.onplaying = () => {
        hideOverlay();
        syncTransportButtons(video);
        const saved = appState.continueWatchingMap.get(ch.id);
        if (saved && !ch.isLive && saved.lastTimeSec > 5 && video.currentTime < 2 && Number.isFinite(video.duration) && saved.lastTimeSec < video.duration - 10) {
            try { video.currentTime = saved.lastTimeSec; } catch {}
        }
    };
    video.oncanplay = () => hideOverlay();
    video.ontimeupdate = () => {
        if (video.currentTime > 0) hideOverlay();
        if (!ch.isLive && video.currentTime > 5) {
            const nowMs = Date.now();
            if (nowMs - lastSavedProgressAt >= 5000) {
                lastSavedProgressAt = nowMs;
                recordWatchHistory(ch, video.currentTime);
            }
        }
    };

    function startPlaybackWithGracefulFallback() {
        video.play().then(() => {
            hideOverlay();
            syncTransportButtons(video);
        }).catch(err => {
            console.warn('[StreamMesh Player] Sesli otomatik oynatma kısıtlandı, sessiz deneniyor...', err);
            video.muted = true;
            syncTransportButtons(video);
            video.play().then(() => {
                hideOverlay();
                syncTransportButtons(video);
                showToast('Otomatik oynatma için ses kısıldı. Açmak için [🔊 Ses] butonuna dokunun.');
            }).catch(handleAutoplayBlock);
        });
    }

    function loadDirectVideo(streamUrl, onFail) {
        let loaded = false;

        video.onloadeddata = () => {
            loaded = true;
            hideOverlay();
            startPlaybackWithGracefulFallback();
        };

        video.onerror = () => {
            const err = video.error;
            const msg = err ? `Hata Kodu: ${err.code}` : 'Video yüklenemedi';
            console.warn('[Native Video Error]', msg);
            if (!loaded) onFail(msg);
        };

        video.src = streamUrl;
        video.load();
    }

    function loadStreamUrl(streamUrl, onFail) {
        if (appState.hlsInstance) {
            try { appState.hlsInstance.destroy(); } catch (e) {}
            appState.hlsInstance = null;
        }

        const isExplicitDirectFile = (streamUrl.endsWith('.mp4') || streamUrl.endsWith('.mkv') || streamUrl.endsWith('.webm') || streamUrl.endsWith('.avi')) && !streamUrl.includes('.m3u8');
        const shouldUseHls = !isExplicitDirectFile && window.Hls && window.Hls.isSupported();

        if (shouldUseHls) {
            let hasResolved = false;
            appState.hlsInstance = new window.Hls({
                enableWorker: true,
                lowLatencyMode: true,
                manifestLoadingMaxRetry: 4,
                manifestLoadingRetryDelay: 800,
                fragLoadingMaxRetry: 4,
                maxBufferLength: 30,
                maxMaxBufferLength: 60
            });

            appState.hlsInstance.on(window.Hls.Events.MANIFEST_PARSED, () => {
                hasResolved = true;
                hideOverlay();
                startPlaybackWithGracefulFallback();
            });

            appState.hlsInstance.on(window.Hls.Events.FRAG_BUFFERED, () => {
                hasResolved = true;
                hideOverlay();
            });

            appState.hlsInstance.on(window.Hls.Events.FRAG_LOADED, () => {
                hasResolved = true;
                hideOverlay();
            });

            appState.hlsInstance.on(window.Hls.Events.ERROR, (event, data) => {
                if (data.fatal) {
                    switch (data.type) {
                        case window.Hls.ErrorTypes.NETWORK_ERROR:
                            if (!hasResolved) {
                                try { appState.hlsInstance.destroy(); } catch (e) {}
                                appState.hlsInstance = null;
                                onFail(data.details || 'Ağ Hatası');
                            } else {
                                appState.hlsInstance.startLoad();
                            }
                            break;
                        case window.Hls.ErrorTypes.MEDIA_ERROR:
                            appState.hlsInstance.recoverMediaError();
                            break;
                        default:
                            if (data.details === 'manifestParsingError') {
                                try { appState.hlsInstance.destroy(); } catch (e) {}
                                appState.hlsInstance = null;
                                loadDirectVideo(streamUrl, onFail);
                                return;
                            }
                            if (!hasResolved) {
                                try { appState.hlsInstance.destroy(); } catch (e) {}
                                appState.hlsInstance = null;
                                onFail(data.details || 'HLS Akış Hatası');
                            }
                            break;
                    }
                }
            });

            appState.hlsInstance.attachMedia(video);
            appState.hlsInstance.loadSource(streamUrl);
        } else if (video.canPlayType('application/vnd.apple.mpegurl') && !isExplicitDirectFile) {
            video.src = streamUrl;
            video.onloadedmetadata = () => {
                hideOverlay();
                startPlaybackWithGracefulFallback();
            };
            video.onerror = () => onFail('Safari HLS Hatası');
        } else {
            loadDirectVideo(streamUrl, onFail);
        }
    }

    function tryNextCandidate(reason) {
        if (candidateIndex >= candidates.length) {
            console.error('All playback candidates exhausted for channel:', ch.name);
            showOverlayError('Yayın Akışı Alınamadı', `${ch.name} kaynağı yanıt vermedi. (${reason || 'Hata'}).`);
            return;
        }

        const currentUrl = candidates[candidateIndex++];
        if (desc) desc.innerText = `Kaynak [${candidateIndex}/${candidates.length}] bağlanıyor...`;

        loadStreamUrl(currentUrl, (errReason) => {
            console.warn(`Candidate failed (${errReason}), switching to next...`);
            tryNextCandidate(errReason);
        });
    }

    tryNextCandidate('İlk Başlatma');
}

export function handleAutoplayBlock() {
    const overlay = document.getElementById('playerOverlay');
    const icon = document.getElementById('overlayIcon');
    const spinner = document.getElementById('overlaySpinner');
    const title = document.getElementById('overlayTitle');
    const desc = document.getElementById('overlayDesc');
    const btn = document.getElementById('overlayBtn');

    if (overlay) {
        overlay.style.display = 'flex';
        overlay.style.cursor = 'pointer';
        overlay.onclick = resumeActivePlayback;
    }
    if (icon) {
        icon.style.display = 'block';
        icon.innerText = '▶';
    }
    if (spinner) spinner.style.display = 'none';
    if (btn) {
        btn.style.display = 'inline-block';
        btn.innerText = 'Oynat';
        btn.onclick = (e) => {
            if (e && e.stopPropagation) e.stopPropagation();
            resumeActivePlayback();
        };
    }
    if (title) title.innerText = 'Oynatmak İçin Dokunun';
    if (desc) desc.innerText = 'Tarayıcı güvenlik kısıtı nedeniyle yayını başlatmak için lütfen ekrana veya butona tıklayın.';
}

export function resumeActivePlayback() {
    const video = getVideoElement();
    const overlay = document.getElementById('playerOverlay');
    if (overlay) {
        overlay.style.display = 'none';
        overlay.onclick = null;
    }
    if (video) {
        video.muted = false;
        video.play().then(() => syncTransportButtons(video)).catch(() => {
            video.muted = true;
            video.play().then(() => syncTransportButtons(video)).catch(() => {});
        });
    }
}

export function showOverlayError(titleText, descText) {
    const overlay = document.getElementById('playerOverlay');
    const icon = document.getElementById('overlayIcon');
    const spinner = document.getElementById('overlaySpinner');
    const title = document.getElementById('overlayTitle');
    const desc = document.getElementById('overlayDesc');
    const btn = document.getElementById('overlayBtn');

    if (overlay) {
        overlay.style.display = 'flex';
        overlay.style.cursor = 'default';
        overlay.onclick = null;
    }
    if (icon) {
        icon.style.display = 'block';
        icon.innerText = '⚠️';
    }
    if (spinner) spinner.style.display = 'none';
    if (btn) {
        btn.style.display = 'inline-block';
        btn.innerText = 'Yeniden Dene';
        btn.onclick = (e) => {
            if (e && e.stopPropagation) e.stopPropagation();
            if (appState.activeChannel) playMedia(appState.activeChannel);
        };
    }
    if (title) title.innerText = titleText;
    if (desc) desc.innerText = descText;
}

export function togglePlayPause() {
    const video = getVideoElement();
    if (!video) return;
    if (video.paused) {
        video.play().catch(handleAutoplayBlock);
    } else {
        video.pause();
    }
    syncTransportButtons(video);
}

export function seekVideo(seconds) {
    const video = getVideoElement();
    if (!video) return;
    video.currentTime = Math.max(0, video.currentTime + seconds);
    showToast(`${seconds > 0 ? '+' : ''}${seconds}s`);
}

export function toggleMute() {
    const video = getVideoElement();
    if (!video) return;
    video.muted = !video.muted;
    syncTransportButtons(video);
}

export function cycleAspectRatio() {
    const video = getVideoElement();
    if (!video) return;
    appState.aspectRatioMode = (appState.aspectRatioMode + 1) % 3;
    const btn = document.getElementById('aspectBtn');
    if (appState.aspectRatioMode === 0) {
        video.style.objectFit = 'contain';
        if (btn) btn.innerText = '📐 16:9';
        showToast('En-Boy: 16:9');
    } else if (appState.aspectRatioMode === 1) {
        video.style.objectFit = 'fill';
        if (btn) btn.innerText = '📐 Doldur';
        showToast('En-Boy: Ekrana Yay');
    } else {
        video.style.objectFit = 'cover';
        if (btn) btn.innerText = '📐 Zoom';
        showToast('En-Boy: Kırp / Büyüt');
    }
}

export async function togglePiP() {
    const video = getVideoElement();
    if (!video) return;
    try {
        if (document.pictureInPictureElement) {
            await document.exitPictureInPicture();
        } else if (document.pictureInPictureEnabled) {
            await video.requestPictureInPicture();
        }
    } catch (e) {
        showToast('PiP desteklenmiyor');
    }
}

export function toggleFullscreen() {
    const box = document.getElementById('videoBox');
    if (!box) return;
    if (!document.fullscreenElement) {
        box.requestFullscreen().catch(() => {});
    } else {
        document.exitFullscreen().catch(() => {});
    }
}

export function copyCurrentStreamUrl() {
    if (!appState.activeChannel) return;
    const fullUrl = appState.activeChannel.url || (window.location.origin + appState.activeChannel.streamUrl);
    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(fullUrl).then(() => {
            showToast('Yayın URL kopyalandı 📋');
        }).catch(() => {
            showToast(fullUrl);
        });
    } else {
        showToast(fullUrl);
    }
}

export function playManualStream() {
    const aceInput = (document.getElementById('manualAceInput')?.value || '').trim();
    const urlInput = (document.getElementById('manualUrlInput')?.value || '').trim();

    if (aceInput) {
        let cleanHash = aceInput.replace('acestream://', '').trim();
        const customCh = {
            id: 'manual_ace_' + Math.random().toString(36).substring(2, 6),
            name: 'AceStream: ' + cleanHash.substring(0, 10) + '...',
            category: 'TV',
            group: 'AceStream Manuel',
            sourceType: 'ACESTREAM',
            quality: 'HD',
            url: `http://127.0.0.1:6878/ace/getstream?infohash=${encodeURIComponent(cleanHash)}`,
            streamUrl: `/stream/ace?infohash=${encodeURIComponent(cleanHash)}`,
            epg: 'Kullanıcı Tanımlı AceStream Akışı',
            isLive: true,
            logo: FALLBACK_B64
        };
        appState.allChannels.unshift(customCh);
        switchMediaTab('TV');
        playMedia(customCh);
        showToast('AceStream akışı başlatılıyor ⚡');
    } else if (urlInput) {
        const isMp4 = urlInput.toLowerCase().includes('.mp4');
        const customCh = {
            id: 'manual_url_' + Math.random().toString(36).substring(2, 6),
            name: 'Özel Akış (' + (isMp4 ? 'MP4' : 'HLS') + ')',
            category: isMp4 ? 'Filmler' : 'TV',
            group: 'Manuel Link',
            sourceType: 'DIRECT',
            quality: 'HD',
            url: urlInput,
            streamUrl: urlInput,
            epg: 'Manuel Akış Yayını',
            isLive: !isMp4,
            logo: FALLBACK_B64
        };
        appState.allChannels.unshift(customCh);
        switchMediaTab(customCh.category);
        playMedia(customCh);
        showToast('Özel akış başlatılıyor 🚀');
    } else {
        showToast('Lütfen geçerli bir Hash veya URL girin.');
    }
}

export function loadSampleAce(hash) {
    const aceInput = document.getElementById('manualAceInput');
    const urlInput = document.getElementById('manualUrlInput');
    if (aceInput) aceInput.value = hash;
    if (urlInput) urlInput.value = '';
}

export function loadSampleUrl(url) {
    const aceInput = document.getElementById('manualAceInput');
    const urlInput = document.getElementById('manualUrlInput');
    if (urlInput) urlInput.value = url;
    if (aceInput) aceInput.value = '';
}
