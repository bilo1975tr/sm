import { appState } from './state.js';
import { showToast } from './ui.js';

export async function loadWebPlugins() {
    try {
        const res = await fetch('/api/plugins');
        if (res.ok) {
            const data = await res.json();
            appState.webPlugins = data.plugins || [];
            updatePluginsBadge();
            renderWebPluginsList();
            if (!appState.selectedPluginId && appState.webPlugins.length > 0) {
                selectWebPlugin(appState.webPlugins[0].id);
            }
        }
    } catch (e) {
        console.error("Eklentiler yüklenirken hata:", e);
    }
}

export function updatePluginsBadge() {
    const activeCount = appState.webPlugins.filter(p => p.isEnabled).length;
    const badge = document.getElementById('pluginsActiveCountBadge');
    if (badge) {
        badge.innerText = `${activeCount} / ${appState.webPlugins.length} Aktif`;
    }
}

export function openPluginsModal() {
    const modal = document.getElementById('pluginsModal');
    if (modal) {
        modal.style.display = 'flex';
        loadWebPlugins();
    }
}

export function closePluginsModal() {
    const modal = document.getElementById('pluginsModal');
    if (modal) modal.style.display = 'none';
}

export function setPluginCategoryFilter(category, buttonEl) {
    appState.activePluginFilterCategory = category;
    const pills = document.querySelectorAll('#pluginCategoryPills .sub-pill');
    pills.forEach(p => p.classList.remove('active'));
    if (buttonEl) buttonEl.classList.add('active');
    renderWebPluginsList();
}

export function filterWebPlugins() {
    renderWebPluginsList();
}

export function renderWebPluginsList() {
    const container = document.getElementById('pluginsList');
    if (!container) return;

    const search = (document.getElementById('pluginSearchInput')?.value || '').trim().toLowerCase();
    const filtered = appState.webPlugins.filter(p => {
        const matchesCategory = appState.activePluginFilterCategory === 'Tümü' || p.category === appState.activePluginFilterCategory;
        const matchesSearch = !search ||
            p.name.toLowerCase().includes(search) ||
            p.description.toLowerCase().includes(search) ||
            p.id.toLowerCase().includes(search);
        return matchesCategory && matchesSearch;
    });

    if (filtered.length === 0) {
        container.innerHTML = '<div style="color:var(--text-muted); text-align:center; padding:30px 10px; font-size:12px;">Eşleşen eklenti bulunamadı.</div>';
        return;
    }

    let html = '';
    filtered.forEach(p => {
        const isSelected = p.id === appState.selectedPluginId;
        const statusColor = p.isEnabled ? '#10b981' : '#64748b';
        html += `
            <div class="plugin-item-card ${isSelected ? 'active' : ''}" onclick="selectWebPlugin('${p.id}')">
                <div style="font-size:22px; width:34px; height:34px; border-radius:6px; background:#090d16; display:flex; align-items:center; justify-content:center; flex-shrink:0;">
                    ${p.icon || '🧩'}
                </div>
                <div style="flex:1; min-width:0;">
                    <div style="font-size:13px; font-weight:700; color:#fff; white-space:nowrap; overflow:hidden; text-overflow:ellipsis;">${p.name}</div>
                    <div style="display:flex; align-items:center; gap:6px; margin-top:3px; font-size:10px;">
                        <span style="background:#0284c7; color:#fff; padding:1px 5px; border-radius:4px; font-weight:bold;">${p.category}</span>
                        <span style="color:${statusColor}; font-weight:bold;">${p.isEnabled ? '🟢 Aktif' : '⚪ Kapalı'}</span>
                        <span style="color:var(--text-muted);">v${p.version}</span>
                    </div>
                </div>
                <label class="plugin-toggle-switch" onclick="event.stopPropagation()">
                    <input type="checkbox" ${p.isEnabled ? 'checked' : ''} onchange="toggleWebPlugin('${p.id}', this.checked)">
                    <span class="plugin-toggle-slider"></span>
                </label>
            </div>
        `;
    });

    container.innerHTML = html;
}

export function selectWebPlugin(pluginId) {
    appState.selectedPluginId = pluginId;
    renderWebPluginsList();

    const p = appState.webPlugins.find(x => x.id === pluginId);
    const container = document.getElementById('pluginDetails');
    if (!container || !p) return;

    const statusBg = p.isEnabled ? 'rgba(16, 185, 129, 0.2)' : 'rgba(100, 116, 139, 0.2)';
    const statusColor = p.isEnabled ? '#34d399' : '#94a3b8';

    let statsHtml = '';
    if (p.stats) {
        Object.entries(p.stats).forEach(([k, v]) => {
            statsHtml += `
                <div class="stats-metric-badge">
                    <span style="font-size:10px; color:var(--text-muted);">${k}</span>
                    <span style="font-size:13px; font-weight:bold; color:#fff;">${v}</span>
                </div>
            `;
        });
    }

    let settingsHtml = '';
    if (p.settings && p.settings.length > 0) {
        p.settings.forEach(s => {
            let inputControl = '';
            if (s.type === 'boolean') {
                const isChecked = s.value === 'true' || s.value === true;
                inputControl = `
                    <label style="display:inline-flex; align-items:center; gap:8px; cursor:pointer; color:#fff; font-size:13px;">
                        <input type="checkbox" id="setting_${s.key}" ${isChecked ? 'checked' : ''} style="width:16px; height:16px; accent-color:var(--primary);">
                        <span>Etkinleştir</span>
                    </label>
                `;
            } else if (s.type === 'select') {
                const options = (s.options || []).map(opt => `<option value="${opt}" ${opt === s.value ? 'selected' : ''}>${opt}</option>`).join('');
                inputControl = `
                    <select id="setting_${s.key}" style="background:#090d16; border:1px solid var(--border); border-radius:6px; color:#fff; padding:8px 10px; font-size:13px; width:100%; outline:none;">
                        ${options}
                    </select>
                `;
            } else {
                inputControl = `
                    <input type="${s.type === 'number' ? 'number' : 'text'}" id="setting_${s.key}" value="${s.value || ''}" style="background:#090d16; border:1px solid var(--border); border-radius:6px; color:#fff; padding:8px 10px; font-size:13px; width:100%; outline:none;">
                `;
            }

            settingsHtml += `
                <div class="setting-field-card">
                    <label style="font-size:13px; font-weight:700; color:#fff;">${s.label}</label>
                    <span style="font-size:11px; color:var(--text-muted);">${s.description || ''}</span>
                    <div style="margin-top:4px;">${inputControl}</div>
                </div>
            `;
        });
    } else {
        settingsHtml = '<div style="color:var(--text-muted); font-size:12px;">Bu eklenti için özel yapılandırma parametresi bulunmuyor.</div>';
    }

    container.innerHTML = `
        <div class="plugin-header-card">
            <div style="font-size:36px; width:52px; height:52px; border-radius:10px; background:#090d16; display:flex; align-items:center; justify-content:center; flex-shrink:0;">
                ${p.icon || '🧩'}
            </div>
            <div style="flex:1;">
                <div style="display:flex; align-items:center; justify-content:space-between; gap:10px; flex-wrap:wrap;">
                    <h3 style="font-size:16px; font-weight:800; color:#fff;">${p.name}</h3>
                    <span style="font-size:11px; font-weight:bold; padding:3px 10px; border-radius:12px; background:${statusBg}; color:${statusColor};">
                        ${p.isEnabled ? '🟢 Aktif' : '⚪ Devre Dışı'}
                    </span>
                </div>
                <div style="display:flex; align-items:center; gap:8px; margin:4px 0 8px 0; font-size:11px;">
                    <span style="background:#0284c7; color:#fff; padding:2px 6px; border-radius:4px; font-weight:bold;">${p.category}</span>
                    <span style="color:var(--primary-glow); font-weight:600;">v${p.version}</span>
                    <span style="color:var(--text-muted);">Geliştirici: ${p.author || 'StreamMesh'}</span>
                </div>
                <p style="font-size:12px; color:#cbd5e1; line-height:1.5;">${p.description}</p>
            </div>
        </div>

        <!-- Statistics -->
        <div>
            <h4 style="font-size:13px; font-weight:700; color:var(--primary-glow); margin-bottom:10px;">📊 Canlı Metrikler &amp; Durum</h4>
            <div style="display:flex; flex-wrap:wrap; gap:10px;">
                ${statsHtml}
            </div>
        </div>

        <!-- Settings -->
        <div>
            <div style="display:flex; align-items:center; justify-content:space-between; margin-bottom:10px;">
                <h4 style="font-size:13px; font-weight:700; color:var(--primary-glow);">⚙️ Eklenti Parametreleri</h4>
                <button class="action-link" style="padding:4px 10px; font-size:11px; background:#0284c7;" onclick="executeWebPluginAction('${p.id}')">⚡ Test Et / Çalıştır</button>
            </div>
            <div style="display:flex; flex-direction:column; gap:10px;">
                ${settingsHtml}
            </div>
        </div>

        <!-- Save Action Buttons -->
        <div style="display:flex; justify-content:flex-end; gap:10px; margin-top:10px; padding-top:14px; border-top:1px solid var(--border);">
            <button class="page-action-btn" onclick="resetWebPluginSettings('${p.id}')">↩️ Sıfırla</button>
            <button class="page-action-btn" style="background:#059669; border-color:#10b981; color:#fff;" onclick="saveWebPluginSettings('${p.id}')">💾 Değişiklikleri Kaydet</button>
        </div>
    `;
}

export async function toggleWebPlugin(pluginId, isChecked) {
    try {
        const res = await fetch('/api/plugins/toggle', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id: pluginId, enabled: isChecked })
        });
        if (res.ok) {
            const target = appState.webPlugins.find(p => p.id === pluginId);
            if (target) {
                target.isEnabled = isChecked;
                target.status = isChecked ? 'Aktif' : 'Devre Dışı';
            }
            updatePluginsBadge();
            if (appState.selectedPluginId === pluginId) {
                selectWebPlugin(pluginId);
            } else {
                renderWebPluginsList();
            }
            showToast(isChecked ? '✅ Eklenti etkinleştirildi' : '⚪ Eklenti devre dışı bırakıldı');
        }
    } catch (e) {
        showToast('Hata: Durum değiştirilemedi');
    }
}

export async function saveWebPluginSettings(pluginId) {
    const p = appState.webPlugins.find(x => x.id === pluginId);
    if (!p || !p.settings) return;

    const updatedSettings = p.settings.map(s => {
        const el = document.getElementById(`setting_${s.key}`);
        let val = s.value;
        if (el) {
            if (s.type === 'boolean') {
                val = el.checked ? 'true' : 'false';
            } else {
                val = el.value;
            }
        }
        return { key: s.key, value: val };
    });

    try {
        const res = await fetch('/api/plugins/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id: pluginId, settings: updatedSettings })
        });
        if (res.ok) {
            updatedSettings.forEach(us => {
                const target = p.settings.find(s => s.key === us.key);
                if (target) target.value = us.value;
            });
            showToast(`💾 '${p.name}' ayarları başarıyla kaydedildi!`);
            selectWebPlugin(pluginId);
        }
    } catch (e) {
        showToast('Hata: Ayarlar kaydedilemedi');
    }
}

export function resetWebPluginSettings(pluginId) {
    const p = appState.webPlugins.find(x => x.id === pluginId);
    if (!p || !p.settings) return;

    p.settings.forEach(s => {
        s.value = s.defaultValue;
    });
    saveWebPluginSettings(pluginId);
    showToast(`↩️ '${p.name}' varsayılan değerlere döndürüldü.`);
}

export async function executeWebPluginAction(pluginId) {
    showToast('⚡ Eklenti işlemi yürütülüyor...');
    try {
        const res = await fetch('/api/plugins/action', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id: pluginId, action: 'test' })
        });
        if (res.ok) {
            const data = await res.json();
            showToast(data.result || 'İşlem tamamlandı.');
        }
    } catch (e) {
        showToast('İşlem çalıştırılırken hata oluştu.');
    }
}
