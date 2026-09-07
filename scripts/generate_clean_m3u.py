#!/usr/bin/env python3
"""
generate_clean_m3u.py
---------------------
- auto_update.json dosyasındaki M3U/M3U8 ve EPG (XML) kaynaklarını kontrol eder.
- Ardışık 2-3 başarısızlıkta (erişilememe veya 0 çalışan yayın) kaynakları auto_update.json dosyasından otomatik çıkarır.
- GitHub üzerindeki public repository'lerde bulunan yeni, kaliteli M3U/M3U8 kaynaklarını otomatik keşfeder ve auto_update.json'a ekler.
- Aynı kaynağın farklı URL/path formatlarıyla tekrar eklenmesini canonicalization ile engeller.
- Canlı TV, Film, Dizi, Radyo ve tüm VOD içerik türlerini destekler, kategori ve metadata bilgilerini (group-title, tvg-name, tvg-logo, tvg-id vb.) korur.
- HLS (.m3u8) ve HTTP akışlarını kısa timeout ile derinlemesine doğrular.
- Tekilleştirme (de-duplication) ve EPG / Logo eşleştirme zincirini çalıştırır.
- cleaned_playlist.m3u, auto_update.json ve report.json dosyalarını atomik ve güvenli şekilde günceller.
"""

import argparse
import json
import re
import os
import sys
import time
import unicodedata
import xml.etree.ElementTree as ET
from urllib.request import Request, urlopen
from urllib.parse import quote, urljoin
from concurrent.futures import ThreadPoolExecutor

EXTINF_RE = re.compile(r'#EXTINF:(?P<duration>[-0-9]+)?(?P<attrs>.*?),(?P<name>.*)')
ATTR_RE = re.compile(r'([a-zA-Z0-9\-]+?)="([^"]*)"')

DEFAULT_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

_TURKISH_CHAR_MAP = str.maketrans({
    'ş': 's', 'Ş': 's',
    'ı': 'i', 'İ': 'i',
    'ç': 'c', 'Ç': 'c',
    'ü': 'u', 'Ü': 'u',
    'ö': 'o', 'Ö': 'o',
    'ğ': 'g', 'Ğ': 'g'
})

_SUFFIX_REGEX = re.compile(
    r'\b('
    r'uhd|fhd|hd|sd|4k|8k|1080p|1080i|720p|576i|480p|2160p|'
    r'hevc|h265|h264|avc|10bit|'
    r'canli|live|yayin|stream|yedek|backup|test|vip|premium|plus|\+1|\+2|'
    r'turk|turkiye|turkce|azerbaycan|tr|az|de|ger|uk|usa|fr|fra|es|esp|it|ita|ru|rus'
    r')\b',
    re.IGNORECASE
)

# Global Memory Caches
_LOGO_VALIDATION_CACHE = {}
_STREAM_CHECK_CACHE = {}

def canonicalize_source_url(url: str) -> str:
    """
    URL'yi kanonik formata dönüştürür.
    Aynı kaynağın farklı URL/path biçimleriyle (github blob vs raw, http vs https) tekrar eklenmesini engeller.
    """
    if not url:
        return ""
    url = url.strip()

    m = re.match(r'https?://github\.com/([^/]+)/([^/]+)/(?:blob|raw)/([^/]+)/(.*)', url, re.IGNORECASE)
    if m:
        owner, repo, ref, path = m.groups()
        url = f"https://raw.githubusercontent.com/{owner}/{repo}/{ref}/{path}"

    if url.startswith("http://raw.githubusercontent.com/"):
        url = "https://" + url[7:]

    url = url.rstrip('/')
    return url

def normalize_name(s: str) -> str:
    """Metni küçük harfe çevirir, Türkçe karakterleri ve aksanları temizler."""
    if not s:
        return ''
    s = s.strip().lower()
    s = s.translate(_TURKISH_CHAR_MAP)
    s = unicodedata.normalize('NFKD', s)
    s = ''.join(c for c in s if not unicodedata.combining(c))
    s = re.sub(r'[^a-z0-9]+', ' ', s)
    return re.sub(r'\s+', ' ', s).strip()

def canonical_channel_name(s: str) -> str:
    """
    Kanal adından parantezleri ve yayın takılarını (HD, FHD, 4K, [TR], CANLI vb.)
    güvenle temizleyip temel kanal ismini döner.
    """
    if not s:
        return ''
    s_clean = re.sub(r'\[.*?\]|\(.*?\)', ' ', s)
    norm = normalize_name(s_clean)
    tokens = norm.split()
    meaningful = [t for t in tokens if not _SUFFIX_REGEX.fullmatch(t)]
    if meaningful:
        return ' '.join(meaningful)
    return norm

def sanitize_url(url: str) -> str:
    """URL içindeki özel karakterleri safe quote eder. Sadece http/https kabul eder."""
    if not url:
        return ""
    url = url.strip()
    if not (url.startswith('http://') or url.startswith('https://')):
        return ""
    try:
        return quote(url, safe=":/%#?=@[]!$&'()*+,;")
    except Exception:
        return url

def fetch_text_with_retry(url: str, max_retries: int = 2, timeout: int = 20) -> tuple:
    """
    URL içeriğini retry ve backoff ile indirir.
    Dönüş: (content: str, success: bool, error_msg: str)
    """
    clean_url = sanitize_url(url)
    if not clean_url:
        return "", False, "Geçersiz URL"

    req = Request(clean_url, headers={'User-Agent': DEFAULT_USER_AGENT})
    last_err = ""
    for attempt in range(max_retries + 1):
        try:
            with urlopen(req, timeout=timeout) as resp:
                charset = resp.headers.get_content_charset() or 'utf-8'
                content = resp.read().decode(charset, errors='ignore')
                return content, True, ""
        except Exception as e:
            last_err = str(e)
            if attempt < max_retries:
                time.sleep(1.0 * (attempt + 1))

    return "", False, last_err

def validate_logo_url(url: str, timeout: int = 4) -> bool:
    """
    Logo URL'sinin gerçekte çalışıp çalışmadığını test eder.
    HTTP status < 400, Content-Type görsel formatı veya geçerli bayt kontrolü yapar.
    """
    if not url:
        return False
    clean_url = sanitize_url(url)
    if not clean_url:
        return False
    if clean_url in _LOGO_VALIDATION_CACHE:
        return _LOGO_VALIDATION_CACHE[clean_url]

    headers = {
        'User-Agent': DEFAULT_USER_AGENT,
        'Accept': 'image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8'
    }

    is_valid = False
    try:
        req_head = Request(clean_url, headers=headers, method='HEAD')
        with urlopen(req_head, timeout=timeout) as resp:
            if resp.status < 400:
                ct = resp.headers.get('Content-Type', '').lower()
                cl = resp.headers.get('Content-Length')
                if 'image' in ct or any(clean_url.lower().endswith(ext) for ext in ('.png', '.jpg', '.jpeg', '.svg', '.webp', '.ico')):
                    if cl is None or int(cl) > 0:
                        is_valid = True
    except Exception:
        pass

    if not is_valid:
        try:
            req_get = Request(clean_url, headers={**headers, 'Range': 'bytes=0-512'})
            with urlopen(req_get, timeout=timeout) as resp:
                if resp.status < 400:
                    chunk = resp.read(512)
                    if len(chunk) > 0:
                        is_valid = True
        except Exception:
            is_valid = False

    _LOGO_VALIDATION_CACHE[clean_url] = is_valid
    return is_valid

def check_stream_sync(url: str, custom_headers: dict = None, timeout: int = 6) -> tuple:
    """
    Akış bağlantısını test eder. HLS (.m3u8) ise variant playlist ve ilk segmenti de test eder.
    Dönüş: (is_alive: bool, info: str)
    """
    clean_url = sanitize_url(url)
    if not clean_url:
        return False, "Geçersiz URL formatı"

    if clean_url in _STREAM_CHECK_CACHE:
        return _STREAM_CHECK_CACHE[clean_url]

    headers = {'User-Agent': DEFAULT_USER_AGENT}
    if custom_headers:
        headers.update(custom_headers)

    req = Request(clean_url, headers=headers)
    is_hls = '.m3u8' in clean_url.lower()

    try:
        with urlopen(req, timeout=timeout) as resp:
            if resp.status >= 400:
                res = (False, f"HTTP {resp.status}")
                _STREAM_CHECK_CACHE[clean_url] = res
                return res

            ct = resp.headers.get('Content-Type', '').lower()
            if 'mpegurl' in ct or 'hls' in ct or 'apple' in ct:
                is_hls = True

            if not is_hls:
                chunk = resp.read(256)
                if len(chunk) > 0:
                    res = (True, f"HTTP {resp.status} (Stream OK)")
                else:
                    res = (False, "Boş yanıt")
                _STREAM_CHECK_CACHE[clean_url] = res
                return res

            charset = resp.headers.get_content_charset() or 'utf-8'
            m3u8_text = resp.read(8192).decode(charset, errors='ignore')

        if not m3u8_text.startswith('#EXTM3U') and '#EXTM3U' not in m3u8_text:
            res = (False, "Geçersiz HLS Manifest (#EXTM3U eksik)")
            _STREAM_CHECK_CACHE[clean_url] = res
            return res

        lines = [l.strip() for l in m3u8_text.splitlines() if l.strip()]
        target_sub_url = None

        for line in lines:
            if not line.startswith('#'):
                target_sub_url = urljoin(clean_url, line)
                break

        if not target_sub_url:
            res = (False, "HLS Manifest boş veya segment bulunamadı")
            _STREAM_CHECK_CACHE[clean_url] = res
            return res

        sub_req = Request(target_sub_url, headers={**headers, 'Range': 'bytes=0-1024'})
        try:
            with urlopen(sub_req, timeout=timeout) as sub_resp:
                if sub_resp.status < 400:
                    sub_chunk = sub_resp.read(512)
                    if len(sub_chunk) > 0:
                        res = (True, "HLS (Segment OK)")
                    else:
                        res = (False, "HLS Segment boş veri")
                else:
                    res = (False, f"HLS Segment HTTP {sub_resp.status}")
        except Exception as e:
            res = (False, f"HLS Segment hatası: {e}")

        _STREAM_CHECK_CACHE[clean_url] = res
        return res

    except Exception as e:
        res = (False, str(e))
        _STREAM_CHECK_CACHE[clean_url] = res
        return res

def map_category(cat_key: str, original_group: str, name: str) -> str:
    """Kategoriyi TV, Film, Dizi, Radyo olarak öncelik sırasına göre standardize eder."""
    og = (original_group or "").lower()
    nm = (name or "").lower()
    ck = (cat_key or "").lower()

    if 'dizi' in og or 'series' in og or 'sezon' in og or 'episode' in og or 'bölüm' in og or 'bolum' in og or re.search(r'(?i)\bs\d+\s?e\d+\b|\b\d+x\d+\b', nm):
        return "Dizi"
    if 'film' in og or 'movie' in og or 'sinema' in og or 'vod' in og or 'movie' in nm or 'film' in nm:
        return "Film"
    if 'radyo' in og or 'radio' in og or 'radyo' in nm:
        return "Radyo"
    if 'tv' in og or 'canli' in og or 'live' in og or 'kanal' in og:
        return "TV"

    if ck in ('series', 'dizi', 'diziler'):
        return "Dizi"
    elif ck in ('movies', 'film', 'filmler', 'sinema'):
        return "Film"
    elif ck in ('radio', 'radyo', 'radios'):
        return "Radyo"
    elif ck in ('channels', 'tv', 'canli', 'live'):
        return "TV"

    if re.search(r'(?i)\bs\d+\s?e\d+\b|\b\d+x\d+\b', nm) or 'bölüm' in nm or 'bolum' in nm or 'sezon' in nm:
        return "Dizi"
    if 'radyo' in nm or 'radio' in nm:
        return "Radyo"

    return "TV"

def parse_m3u(content: str, source_url: str, default_category: str = "TV"):
    """
    M3U içeriğini parse eder.
    Metadata bilgilerini (group-title, tvg-name, tvg-logo, tvg-id) ve direktifleri korur.
    Film ve Dizi içeriklerini Canlı TV ile karıştırmadan orijinal grup/kategori bilgisini saklar.
    """
    channels = []
    lines = content.splitlines()
    i = 0
    current_directives = []

    while i < len(lines):
        line = lines[i].strip()
        if not line:
            i += 1
            continue

        if line.startswith('#EXTVLCOPT') or line.startswith('#EXTHTTP') or line.startswith('#KODIPROP'):
            current_directives.append(line)
            i += 1
            continue

        if line.startswith('#EXTINF'):
            m = EXTINF_RE.match(line)
            if not m:
                i += 1
                continue
            attrs_raw = m.group('attrs') or ''
            attrs = {}
            for attr_match in ATTR_RE.finditer(attrs_raw):
                attrs[attr_match.group(1).lower()] = attr_match.group(2)

            name = m.group('name').strip() if m else ''

            j = i + 1
            url = ''
            while j < len(lines):
                nxt = lines[j].strip()
                if nxt and not nxt.startswith('#'):
                    if any(nxt.startswith(p) for p in ('http://', 'https://', 'rtmp://', 'udp://', 'acestream://', 'rtsp://')):
                        url = nxt
                    break
                elif nxt.startswith('#EXTVLCOPT') or nxt.startswith('#EXTHTTP') or nxt.startswith('#KODIPROP'):
                    current_directives.append(nxt)
                j += 1

            if url:
                orig_group = attrs.get('group-title', '')
                cat = map_category(default_category, orig_group, name)
                final_group = orig_group if orig_group else cat

                channel = {
                    'name': name,
                    'tvg-id': attrs.get('tvg-id') or attrs.get('tvg-name') or None,
                    'tvg-name': attrs.get('tvg-name') or name,
                    'tvg-logo': attrs.get('tvg-logo') or None,
                    'group-title': final_group,
                    'category': cat,
                    'url': url,
                    'source': source_url,
                    'normalized_name': normalize_name(name),
                    'canonical_name': canonical_channel_name(name),
                    'directives': list(current_directives)
                }
                channels.append(channel)
                current_directives.clear()
            i = j
        else:
            i += 1
    return channels

def parse_epg_xml(xml_content: str):
    """EPG XML verisini parse eder."""
    channels = {}
    if not xml_content or not xml_content.strip():
        return channels
    try:
        root = ET.fromstring(xml_content)
        for ch in root.findall('.//'):
            if ch.tag.split('}')[-1] != 'channel':
                continue
            ch_id = ch.get('id') or ch.get('channel')
            if not ch_id:
                continue

            display_names = []
            for dn in ch.findall('.//'):
                if dn.tag.split('}')[-1] == 'display-name' and dn.text:
                    display_names.append(dn.text.strip())
            primary_name = display_names[0] if display_names else ch_id

            icon = None
            for ic in ch.findall('.//'):
                if ic.tag.split('}')[-1] == 'icon':
                    icon = ic.get('src') or ic.get('url') or None
                    if icon:
                        break

            channels[ch_id] = {
                'id': ch_id,
                'display_name': primary_name,
                'icon': icon,
                'normalized_name': normalize_name(primary_name),
                'canonical_name': canonical_channel_name(primary_name)
            }
    except Exception as e:
        print(f"[!] EPG XML parse uyarısı: {e}")
    return channels

def index_github_repo_logos(repo: str, branch: str = 'main', github_token: str = None) -> dict:
    """GitHub repository ağacını indeksler."""
    logos = {}
    api_url = f"https://api.github.com/repos/{repo}/git/trees/{branch}?recursive=1"
    headers = {'User-Agent': DEFAULT_USER_AGENT}
    if github_token:
        headers['Authorization'] = f"token {github_token}"

    try:
        req = Request(api_url, headers=headers)
        with urlopen(req, timeout=12) as resp:
            data = json.loads(resp.read().decode('utf-8'))
            tree = data.get('tree', [])
            for item in tree:
                path = item.get('path', '')
                if item.get('type') == 'blob' and any(path.lower().endswith(ext) for ext in ('.png', '.jpg', '.jpeg', '.svg', '.webp', '.ico')):
                    base_name = os.path.splitext(os.path.basename(path))[0]
                    raw_url = f"https://raw.githubusercontent.com/{repo}/{branch}/{path}"

                    norm_k = normalize_name(base_name)
                    canon_k = canonical_channel_name(base_name)
                    slug_k = base_name.lower().replace(' ', '-').strip('-')

                    if norm_k and norm_k not in logos:
                        logos[norm_k] = raw_url
                    if canon_k and canon_k not in logos:
                        logos[canon_k] = raw_url
                    if slug_k and slug_k not in logos:
                        logos[slug_k] = raw_url
    except Exception:
        if branch == 'main':
            return index_github_repo_logos(repo, branch='master', github_token=github_token)
    return logos

def fetch_all_logo_databases(github_token: str = None) -> tuple:
    """Logo veritabanlarını indeksler."""
    print("[*] bilo1975tr/tv-logos (Birincil Logo Deposu) indeksleniyor...")
    bilo_db = index_github_repo_logos("bilo1975tr/tv-logos", branch="main", github_token=github_token)
    print(f"  [+] bilo1975tr/tv-logos: {len(bilo_db)} adet logo indeksi hazır.")

    print("[*] tv-logo/tv-logos (Fallback Logo Deposu) indeksleniyor...")
    fallback_db = index_github_repo_logos("tv-logo/tv-logos", branch="main", github_token=github_token)
    print(f"  [+] tv-logo/tv-logos: {len(fallback_db)} adet logo indeksi hazır.")

    return bilo_db, fallback_db

def find_best_logo_match(norm_name: str, canon_name: str, logo_db: dict) -> str:
    """Logo veritabanında en iyi eşleşmeyi bulur."""
    if not norm_name and not canon_name:
        return ""

    if norm_name in logo_db:
        return logo_db[norm_name]
    if canon_name in logo_db:
        return logo_db[canon_name]

    slug = norm_name.replace(' ', '-')
    if slug in logo_db:
        return logo_db[slug]
    canon_slug = canon_name.replace(' ', '-')
    if canon_slug in logo_db:
        return logo_db[canon_slug]

    norm_tokens = set(norm_name.split())
    canon_tokens = set(canon_name.split())
    for lk, lurl in logo_db.items():
        lk_tokens = set(lk.split())
        if lk_tokens and (lk_tokens == norm_tokens or lk_tokens == canon_tokens):
            return lurl

    return ""

def match_channel_with_epg(ch: dict, epg_by_id: dict, epg_by_name: dict, epg_by_canon: dict) -> tuple:
    """Kanalı EPG kayıtlarıyla eşleştirir."""
    tvg_id = ch.get('tvg-id')
    norm_name = ch.get('normalized_name')
    canon_name = ch.get('canonical_name')

    if tvg_id and tvg_id in epg_by_id:
        return epg_by_id[tvg_id], "exact_tvg_id"

    if tvg_id:
        canon_id = canonical_channel_name(tvg_id)
        if canon_id in epg_by_canon:
            return epg_by_canon[canon_id], "canonical_tvg_id"

    if norm_name and norm_name in epg_by_name:
        return epg_by_name[norm_name], "exact_name"

    if canon_name and canon_name in epg_by_canon:
        return epg_by_canon[canon_name], "canonical_name"

    if norm_name:
        condensed = norm_name.replace(' ', '')
        for cname, cdata in epg_by_canon.items():
            if cname.replace(' ', '') == condensed:
                return cdata, "safe_alias"

    return None, "none"

def discover_github_m3u_sources(github_token: str = None, existing_canonical_urls: set = None, max_candidates: int = 10) -> list:
    """
    GitHub üzerindeki PUBLIC repository'lerde bulunan M3U/M3U8 kaynaklarını otomatik keşfeder.
    Rate-limit duyarlıdır.
    Dönüş: [(category, raw_url, parsed_channels)]
    """
    if existing_canonical_urls is None:
        existing_canonical_urls = set()

    headers = {'User-Agent': 'StreamMesh-AutoCleaner/1.0'}
    if github_token:
        headers['Authorization'] = f"token {github_token}"

    queries = [
        "filename:m3u+iptv",
        "filename:m3u+turk",
        "extension:m3u8+playlist"
    ]

    candidate_urls = []

    for q in queries:
        if len(candidate_urls) >= max_candidates:
            break
        api_url = f"https://api.github.com/search/code?q={q}&per_page=10"
        try:
            req = Request(api_url, headers=headers)
            with urlopen(req, timeout=10) as resp:
                if resp.status == 200:
                    data = json.loads(resp.read().decode('utf-8'))
                    for item in data.get('items', []):
                        html_url = item.get('html_url', '')
                        if html_url:
                            raw_url = canonicalize_source_url(html_url)
                            if raw_url and raw_url not in existing_canonical_urls and raw_url not in candidate_urls:
                                candidate_urls.append(raw_url)
                                existing_canonical_urls.add(raw_url)
                                if len(candidate_urls) >= max_candidates:
                                    break
        except Exception as e:
            print(f"[*] GitHub Arama uyarısı ({q}): {e}")
            break

    print(f"[*] GitHub araması sonucu {len(candidate_urls)} yeni aday M3U kaynağı tespit edildi.")

    discovered_valid = []

    for url in candidate_urls:
        content, ok, err = fetch_text_with_retry(url, max_retries=1, timeout=12)
        if not ok or not content or '#EXTM3U' not in content:
            continue

        parsed = parse_m3u(content, url, default_category="TV")
        if not parsed or len(parsed) < 3 or len(parsed) > 5000:
            continue

        sample_channels = parsed[:10]
        alive_count = 0
        for ch in sample_channels:
            custom_headers = {}
            for d in ch.get('directives', []):
                if 'http-user-agent=' in d:
                    custom_headers['User-Agent'] = d.split('http-user-agent=', 1)[1].strip()
                elif 'http-referrer=' in d:
                    custom_headers['Referer'] = d.split('http-referrer=', 1)[1].strip()
            is_alive, _ = check_stream_sync(ch['url'], custom_headers=custom_headers, timeout=5)
            if is_alive:
                alive_count += 1

        if alive_count >= 2 or (len(sample_channels) > 0 and alive_count / len(sample_channels) >= 0.25):
            cat_counts = {}
            for ch in parsed:
                cat = map_category("TV", ch.get('group-title', ''), ch.get('name', ''))
                cat_counts[cat.lower()] = cat_counts.get(cat.lower(), 0) + 1

            best_cat = max(cat_counts, key=cat_counts.get) if cat_counts else "tv"
            if best_cat not in ("tv", "film", "dizi", "radyo", "karma"):
                best_cat = "tv"

            discovered_valid.append((best_cat, url, parsed))
            print(f"  [+] Keşfedildi ve Doğrulandı ({best_cat.upper()}): {url} ({alive_count}/{len(sample_channels)} çalışan yayın)")

    return discovered_valid

def main():
    parser = argparse.ArgumentParser(description="M3U Otomatik Temizleme, Sağlık Kontrolü, EPG ve Logo Entegrasyonu")
    parser.add_argument('--source', default='auto_update.json', help='auto_update.json dosya yolu veya URL')
    parser.add_argument('--outdir', default='.', help='Çıktı klasörü')
    parser.add_argument('--fetch-logos', action='store_true', default=False, help='bilo1975tr ve tv-logos depolarından logo çek')
    parser.add_argument('--github-token', default=None, help='GitHub Personal Access Token')
    parser.add_argument('--remove-dead', action='store_true', default=False, help='Çalışmayan ölü linkleri kaldır')
    parser.add_argument('--check-streams', action='store_true', default=False, help='Canlılık kontrolü yap')
    parser.add_argument('--stream-timeout', type=int, default=8, help='Akış kontrolü zaman aşımı (sn)')
    parser.add_argument('--max-workers', type=int, default=15, help='Eşzamanlı işlem sayısı')
    parser.add_argument('--update-auto-json', action='store_true', default=True, help='auto_update.json dosyasını güncelle')
    parser.add_argument('--discover-github', action='store_true', default=True, help='GitHub M3U kaynak keşfini çalıştır')

    args = parser.parse_args()

    data = {}
    is_local_source = False

    if args.source.startswith('http://') or args.source.startswith('https://'):
        txt, ok, err = fetch_text_with_retry(args.source)
        if ok and txt:
            try:
                data = json.loads(txt)
            except Exception as e:
                print(f"[x] JSON parse hatası: {e}")
                sys.exit(1)
        else:
            print(f"[x] Kaynak JSON indirilemedi: {err}")
            sys.exit(1)
    else:
        if os.path.exists(args.source):
            is_local_source = True
            with open(args.source, 'r', encoding='utf-8') as f:
                data = json.load(f)
        else:
            print(f"[x] Kaynak dosya bulunamadı: {args.source}")
            sys.exit(1)

    fail_counts = data.get("_fail_counts", {})
    if not isinstance(fail_counts, dict):
        fail_counts = {}

    m3u_urls = []
    epg_urls = []
    existing_canonical_urls = set()

    for category, urls in data.items():
        if category.startswith('_'):
            continue
        if category.lower() in ('epg', 'xml', 'epgs'):
            for u in urls:
                if isinstance(u, str):
                    epg_urls.append(u)
                    existing_canonical_urls.add(canonicalize_source_url(u))
        else:
            for u in urls:
                if isinstance(u, str):
                    canon_u = canonicalize_source_url(u)
                    existing_canonical_urls.add(canon_u)
                    if u.strip().lower().endswith('.xml'):
                        epg_urls.append(u)
                    else:
                        m3u_urls.append((category, u))

    print(f"[*] Başlangıç: {len(m3u_urls)} M3U playlist adresi ve {len(epg_urls)} EPG adresi işlenecek.")

    github_token = args.github_token or os.environ.get("GITHUB_TOKEN")
    if args.discover_github and github_token:
        print("[*] GitHub M3U Kaynak Keşfi başlatılıyor...")
        new_discovered = discover_github_m3u_sources(
            github_token=github_token,
            existing_canonical_urls=existing_canonical_urls,
            max_candidates=10
        )
        for cat, new_url, parsed_ch in new_discovered:
            if cat not in data or not isinstance(data[cat], list):
                data[cat] = []
            data[cat].append(new_url)
            m3u_urls.append((cat, new_url))
            print(f"  [+] auto_update.json içine yeni kaliteli kaynak eklendi: {new_url} ({cat})")

    print("[*] M3U listeleri indiriliyor...")
    m3u_channels = []
    failed_sources = []
    sources_summary = []
    source_channel_counts = {}

    def fetch_m3u_task(item):
        cat, url = item
        content, ok, err = fetch_text_with_retry(url, max_retries=2, timeout=20)
        return cat, url, content, ok, err

    with ThreadPoolExecutor(max_workers=min(args.max_workers, 10)) as executor:
        m3u_results = list(executor.map(fetch_m3u_task, m3u_urls))

    for cat, u, content, ok, err in m3u_results:
        if ok and content and '#EXTM3U' in content:
            parsed = parse_m3u(content, u, default_category=cat)
            m3u_channels.extend(parsed)
            source_channel_counts[u] = len(parsed)
            sources_summary.append({'url': u, 'category': cat, 'status': 'success', 'channels_count': len(parsed)})
            print(f"  [+] {cat.upper()}: {u} -> {len(parsed)} içerik")
        else:
            source_channel_counts[u] = 0
            failed_sources.append({'url': u, 'category': cat, 'error': err or 'Boş veya geçersiz M3U'})
            sources_summary.append({'url': u, 'category': cat, 'status': 'failed', 'error': err or 'Boş veya geçersiz M3U'})
            print(f"  [-] İndirilemedi / Geçersiz M3U: {u} ({err})")

    print(f"[*] Toplam çekilen ham içerik sayısı: {len(m3u_channels)}")

    print("[*] EPG verileri indiriliyor...")
    epg_channels_by_id = {}
    epg_channels_by_name = {}
    epg_channels_by_canon = {}

    def fetch_epg_task(url):
        content, ok, err = fetch_text_with_retry(url, max_retries=2, timeout=25)
        return url, content, ok, err

    with ThreadPoolExecutor(max_workers=min(args.max_workers, 5)) as executor:
        epg_results = list(executor.map(fetch_epg_task, epg_urls))

    for u, content, ok, err in epg_results:
        if ok and content:
            parsed = parse_epg_xml(content)
            print(f"  [+] EPG ({u}): {len(parsed)} kanal bulundu.")
            for cid, ch_data in parsed.items():
                epg_channels_by_id[cid] = ch_data
                if ch_data.get('normalized_name'):
                    epg_channels_by_name[ch_data['normalized_name']] = ch_data
                if ch_data.get('canonical_name'):
                    epg_channels_by_canon[ch_data['canonical_name']] = ch_data
        else:
            failed_sources.append({'url': u, 'category': 'epg', 'error': err})
            print(f"  [-] EPG İndirilemedi: {u} ({err})")

    bilo_logos_db = {}
    fallback_logos_db = {}
    if args.fetch_logos:
        bilo_logos_db, fallback_logos_db = fetch_all_logo_databases(github_token=github_token)

    print("[*] Kanallar normalize ediliyor, EPG ve Logo zinciri çalıştırılıyor...")
    processed_channels = []
    seen_urls = set()
    unique_channel_map = {}

    epg_match_stats = {
        'exact_tvg_id': 0,
        'canonical_tvg_id': 0,
        'exact_name': 0,
        'canonical_name': 0,
        'safe_alias': 0,
        'none': 0
    }

    logo_stats = {
        'existing_valid': 0,
        'from_epg': 0,
        'from_bilo1975tr': 0,
        'from_fallback': 0,
        'broken_replaced': 0,
        'unmatched': 0
    }

    for ch in m3u_channels:
        url = ch.get('url')
        if not url or url in seen_urls:
            continue
        seen_urls.add(url)

        epg_match, match_method = match_channel_with_epg(
            ch, epg_channels_by_id, epg_channels_by_name, epg_channels_by_canon
        )
        epg_match_stats[match_method] = epg_match_stats.get(match_method, 0) + 1

        if epg_match:
            ch['epg_matched'] = True
            ch['epg_match_method'] = match_method
            if not ch.get('tvg-id'):
                ch['tvg-id'] = epg_match['id']
            if not ch.get('tvg-name'):
                ch['tvg-name'] = epg_match['display_name']
        else:
            ch['epg_matched'] = False
            ch['epg_match_method'] = 'none'

        assigned_logo = ""
        raw_logo = ch.get('tvg-logo')
        had_initial_logo = bool(raw_logo)

        if raw_logo and validate_logo_url(raw_logo, timeout=3):
            assigned_logo = raw_logo
            logo_stats['existing_valid'] += 1
        else:
            if had_initial_logo:
                logo_stats['broken_replaced'] += 1

            if epg_match and epg_match.get('icon') and validate_logo_url(epg_match['icon'], timeout=3):
                assigned_logo = epg_match['icon']
                logo_stats['from_epg'] += 1
            else:
                norm_n = ch.get('normalized_name', '')
                canon_n = ch.get('canonical_name', '')
                bilo_candidate = find_best_logo_match(norm_n, canon_n, bilo_logos_db)
                if bilo_candidate and validate_logo_url(bilo_candidate, timeout=3):
                    assigned_logo = bilo_candidate
                    logo_stats['from_bilo1975tr'] += 1
                else:
                    fallback_candidate = find_best_logo_match(norm_n, canon_n, fallback_logos_db)
                    if fallback_candidate and validate_logo_url(fallback_candidate, timeout=3):
                        assigned_logo = fallback_candidate
                        logo_stats['from_fallback'] += 1
                    else:
                        logo_stats['unmatched'] += 1

        ch['tvg-logo'] = assigned_logo or ""

        cat = ch.get('category', 'TV')
        if cat == 'TV':
            dedup_key = f"id:{ch['tvg-id'].lower()}" if ch.get('tvg-id') and ch['epg_matched'] else f"name:{ch['canonical_name']}#{ch['group-title']}"
        else:
            dedup_key = f"cat:{cat}#name:{ch['canonical_name']}#{ch['url']}"

        if dedup_key not in unique_channel_map:
            ch['backup_urls'] = []
            unique_channel_map[dedup_key] = ch
            processed_channels.append(ch)
        else:
            existing = unique_channel_map[dedup_key]
            existing.setdefault('backup_urls', []).append(ch['url'])

    print(f"[*] İşlenen tekil içerik sayısı: {len(processed_channels)} (Toplam ham URL: {len(seen_urls)})")

    alive_channels = []
    dead_channels = []
    hls_checked = 0
    hls_verified = 0
    source_working_streams = {}

    if args.check_streams and processed_channels:
        print(f"[*] {len(processed_channels)} içerik için derinlikli canlılık testi başlatılıyor...")

        def check_task(channel_item):
            custom_headers = {}
            for d in channel_item.get('directives', []):
                if 'http-user-agent=' in d:
                    custom_headers['User-Agent'] = d.split('http-user-agent=', 1)[1].strip()
                elif 'http-referrer=' in d:
                    custom_headers['Referer'] = d.split('http-referrer=', 1)[1].strip()

            is_hls = '.m3u8' in channel_item['url'].lower()
            ok, info = check_stream_sync(channel_item['url'], custom_headers=custom_headers, timeout=args.stream_timeout)
            channel_item['alive'] = ok
            channel_item['check_info'] = info
            channel_item['is_hls'] = is_hls
            return channel_item

        with ThreadPoolExecutor(max_workers=min(args.max_workers, 25)) as executor:
            results = list(executor.map(check_task, processed_channels))

        for ch in results:
            src = ch.get('source', '')
            if ch.get('is_hls'):
                hls_checked += 1
                if ch.get('alive'):
                    hls_verified += 1

            if ch.get('alive'):
                alive_channels.append(ch)
                source_working_streams[src] = source_working_streams.get(src, 0) + 1
            else:
                dead_channels.append(ch)

        print(f"  [+] Canlı yayın: {len(alive_channels)}, Ölü yayın: {len(dead_channels)} (HLS Doğrulanan: {hls_verified}/{hls_checked})")
    else:
        alive_channels = processed_channels
        for ch in processed_channels:
            src = ch.get('source', '')
            source_working_streams[src] = source_working_streams.get(src, 0) + 1

    removed_sources = []
    preserved_failed_sources = []

    for cat_name, u in list(m3u_urls):
        ch_count = source_channel_counts.get(u, 0)
        working_count = source_working_streams.get(u, 0)

        is_source_dead = (ch_count == 0) or (args.check_streams and working_count == 0)

        if is_source_dead:
            c_fails = fail_counts.get(u, 0) + 1
            fail_counts[u] = c_fails

            if c_fails >= 3:
                if cat_name in data and isinstance(data[cat_name], list) and u in data[cat_name]:
                    data[cat_name].remove(u)
                fail_counts.pop(u, None)
                removed_sources.append({'url': u, 'category': cat_name, 'fail_count': c_fails})
                print(f"  [!] Kaynak 3 kez üst üste başarısız oldu ve auto_update.json'dan çıkarıldı: {u}")
            else:
                preserved_failed_sources.append({'url': u, 'category': cat_name, 'fail_count': c_fails})
                print(f"  [!] Kaynak başarısız oldu ({c_fails}/3), henüz çıkarılmadı: {u}")
        else:
            fail_counts.pop(u, None)

    if is_local_source and args.update_auto_json:
        data["_fail_counts"] = fail_counts
        temp_json_path = f"{args.source}.tmp"
        with open(temp_json_path, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
            f.flush()
            os.fsync(f.fileno())
        os.replace(temp_json_path, args.source)
        print(f"[*] auto_update.json başarıyla güncellendi (Atomik). Çıkarılan ölü kaynak sayısı: {len(removed_sources)}")

    os.makedirs(args.outdir, exist_ok=True)
    output_m3u_path = os.path.join(args.outdir, 'cleaned_playlist.m3u')
    temp_m3u_path = f"{output_m3u_path}.tmp"

    channels_to_write = alive_channels if args.remove_dead else processed_channels

    if len(channels_to_write) == 0 and os.path.exists(output_m3u_path):
        print(f"[!] UYARI: 0 çalışan kanal bulundu. Mevcut {output_m3u_path} korundu, üzerine yazılmadı!")
    else:
        epg_header_str = f' url-tvg="{",".join(epg_urls)}"' if epg_urls else ''
        m3u_lines = [f"#EXTM3U{epg_header_str}"]

        for ch in channels_to_write:
            attrs = []
            if ch.get('tvg-id'):
                attrs.append(f'tvg-id="{ch["tvg-id"]}"')
            if ch.get('tvg-name'):
                attrs.append(f'tvg-name="{ch["tvg-name"]}"')
            if ch.get('tvg-logo'):
                attrs.append(f'tvg-logo="{ch["tvg-logo"]}"')
            if ch.get('group-title'):
                attrs.append(f'group-title="{ch["group-title"]}"')

            attr_str = " " + " ".join(attrs) if attrs else ""

            for directive in ch.get('directives', []):
                m3u_lines.append(directive)

            m3u_lines.append(f"#EXTINF:-1{attr_str},{ch.get('name', 'Kanal')}")
            m3u_lines.append(ch['url'])

        with open(temp_m3u_path, 'w', encoding='utf-8') as f:
            f.write("\n".join(m3u_lines) + "\n")
            f.flush()
            os.fsync(f.fileno())
        os.replace(temp_m3u_path, output_m3u_path)
        print(f"[*] OLUŞTURULDU (Atomik): {output_m3u_path} ({len(channels_to_write)} içerik)")

    total_parsed = len(m3u_channels)
    total_unique = len(processed_channels)
    epg_matched_count = sum(1 for c in processed_channels if c.get('epg_matched'))
    epg_unmatched_count = total_unique - epg_matched_count
    epg_match_rate = round((epg_matched_count / total_unique * 100), 2) if total_unique > 0 else 0.0

    report = {
        'failed_sources': failed_sources,
        'removed_sources': removed_sources,
        'preserved_failed_sources': preserved_failed_sources,
        'source_success_count': len([s for s in sources_summary if s['status'] == 'success']),
        'source_failure_count': len(failed_sources),
        'total_channels_parsed': total_parsed,
        'unique_channels': total_unique,
        'duplicate_channels_removed': total_parsed - total_unique,
        'alive_channels': len(alive_channels),
        'dead_channels': len(dead_channels),
        'hls_channels_checked': hls_checked,
        'hls_channels_verified': hls_verified,
        'epg_matches_count': epg_matched_count,
        'epg_unmatched_count': epg_unmatched_count,
        'epg_match_rate_pct': epg_match_rate,
        'epg_match_methods': epg_match_stats,
        'logos_existing_valid': logo_stats['existing_valid'],
        'logos_broken_replaced': logo_stats['broken_replaced'],
        'logos_from_epg': logo_stats['from_epg'],
        'logos_from_bilo1975tr': logo_stats['from_bilo1975tr'],
        'logos_from_fallback': logo_stats['from_fallback'],
        'logos_unmatched': logo_stats['unmatched'],
        'categories': {},
        'sources_summary': sources_summary
    }

    for c in channels_to_write:
        grp = c.get('group-title', 'DİĞER')
        report['categories'][grp] = report['categories'].get(grp, 0) + 1

    report_path = os.path.join(args.outdir, 'report.json')
    temp_report_path = f"{report_path}.tmp"
    with open(temp_report_path, 'w', encoding='utf-8') as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
        f.flush()
        os.fsync(f.fileno())
    os.replace(temp_report_path, report_path)

    print(f"[*] Rapor oluşturuldu (Atomik): {report_path}")
    print("[✔] M3U ve EPG/Logo zinciri başarıyla tamamlandı!")

if __name__ == '__main__':
    main()
