/**
 * Normalizes channels across both C# MediaServer and Node.js API formats.
 */
export function normalizeChannel(ch) {
    const id = String(ch.id ?? ch.Id ?? 'ch_' + Math.random().toString(36).substring(2, 7));
    const name = ch.name ?? ch.Name ?? ch.PrimaryName ?? 'İsimsiz Kanal';
    const logo = ch.logo ?? ch.LogoUrl ?? ch.PrimaryLogoUrl ?? '';
    let category = ch.category ?? ch.Category ?? 'TV';
    const group = ch.group ?? ch.GroupTitle ?? ch.groupTitle ?? category;
    const sourceType = String(ch.sourceType ?? ch.SourceType ?? 'DIRECT').toUpperCase();
    const quality = ch.quality ?? ch.Quality ?? '1080p';
    const isLive = Boolean(ch.isLive ?? (category === 'TV' || sourceType === 'ACESTREAM'));
    const epg = ch.epg ?? ch.Epg ?? 'Canlı Yayın Akışı';
    const epgTime = ch.epgTime ?? ch.EpgTime ?? '';
    const nextEpg = ch.nextEpg ?? ch.NextEpg ?? '';

    // Categorization heuristic
    const catLower = (category + ' ' + group).toLowerCase();
    if (catLower.includes('film') || catLower.includes('movie') || catLower.includes('vod')) {
        category = 'Filmler';
    } else if (catLower.includes('dizi') || catLower.includes('series')) {
        category = 'Diziler';
    }

    let url = ch.url ?? ch.Url ?? '';
    let streamUrl = ch.streamUrl ?? ch.StreamUrl ?? url;
    if (!streamUrl) {
        streamUrl = '/stream/' + encodeURIComponent(id);
    }

    return {
        id,
        name,
        logo,
        category,
        group,
        sourceType,
        quality,
        isLive,
        url,
        streamUrl,
        epg,
        epgTime,
        nextEpg
    };
}
