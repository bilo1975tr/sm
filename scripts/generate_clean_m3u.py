#!/usr/bin/env python3
"""
generate_clean_m3u.py
---------------------
- auto_update.json dosyasındaki M3U ve EPG (XML) adreslerini indirir.
- M3U listelerini parse eder (tvg-id, tvg-name, tvg-logo, group-title vb.).
- EPG (XML) kaynaklarından kanal ID'lerini, isimlerini ve ikonlarını çıkarır.
- Kanal isimlerini ve EPG ID'lerini eşleştirir (tvg-id veya isim normalizasyonu ile).
- tv-logo/tv-logos GitHub deposundan logoları tarayıp kanallara atar.
- Hızlı akış kontrolü (stream health check) ile çalışan yayın linklerini doğrular.
- Çalışmayan ölü linkleri temizler ve yayına hazır `cleaned_playlist.m3u` ve `report.json` oluşturur.
"""

import argparse
import json
import re
import os
import sys
import unicodedata
import xml.etree.ElementTree as ET
from urllib.request import Request, urlopen
from urllib.parse import quote
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

def normalize_name(s: str) -> str:
    """Metni küçük harfe çevirir, aksanları ve özel karakterleri temizler."""
    if not s:
        return ''
    s = s.strip().lower()
    # Türkçe karakterleri basitçe ascii karşılıklarına çevir
    s = s.translate(_TURKISH_CHAR_MAP)
    # Unicode ayrıştırma (ör. é -> e)
    s = unicodedata.normalize('NFKD', s)
    s = ''.join(c for c in s if not unicodedata.combining(c))
    # Sadece ascii harf/numara bırak
    s = re.sub(r'[^a-z0-9]+', ' ', s)
    s = re.sub(r'\s+', ' ', s).strip()
    return s

def sanitize_url(url: str) -> str:
    """URL içindeki özel karakterleri safe quote eder. Sadece http/https kabul eder."""
    if not url:
        return ""
    url = url.strip()
    if not (url.startswith('http://') or url.startswith('https://')):
        return ""
    try:
        # Sadece non-ASCII karakterleri quote et
        return quote(url, safe=":/%#?=@[]!$&'()*+,;")
    except Exception:
        return url

def fetch_text_sync(url: str, timeout: int = 20) -> str:
    """Standart kütüphane ile URL içeriği indirir."""
    clean_url = sanitize_url(url)
    if not clean_url:
        return ""
    req = Request(clean_url, headers={'User-Agent': DEFAULT_USER_AGENT})
    try:
        with urlopen(req, timeout=timeout) as resp:
            charset = resp.headers.get_content_charset() or 'utf-8'
            return resp.read().decode(charset, errors='ignore')
    except Exception as e:
        print(f"[x] İndirme hatası ({url}): {e}")
        return ""

def check_stream_sync(url: str, timeout: int = 5) -> tuple:
    """Standart kütüphane ile yayın bağlantısını test eder (küçük chunk okuma)."""
    clean_url = sanitize_url(url)
    if not clean_url:
        return False, "Geçersiz URL formatı"
    req = Request(clean_url, headers={'User-Agent': DEFAULT_USER_AGENT})
    try:
        with urlopen(req, timeout=timeout) as resp:
            if resp.status < 400:
                chunk = resp.read(256)
                if len(chunk) > 0:
                    return True, f"HTTP {resp.status}"
                return False, "Boş yanıt"
            return False, f"HTTP {resp.status}"
    except Exception as e:
        return False, str(e)

def parse_m3u(content: str, source_url: str, default_category: str = "TV"):
    """M3U içeriğini parse eder ve kanal listesi döner."""
    channels = []
    lines = content.splitlines()
    i = 0
    while i < len(lines):
        line = lines[i].strip()
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
                    # Sadece geçerli http veya https URL'leri kabul et
                    if nxt.startswith('http://') or nxt.startswith('https://'):
                        url = nxt
                    break
                j += 1

            if url:
                group = attrs.get('group-title') or default_category
                channel = {
                    'name': name,
                    'tvg-id': attrs.get('tvg-id') or attrs.get('tvg-name') or None,
                    'tvg-name': attrs.get('tvg-name') or name,
                    'tvg-logo': attrs.get('tvg-logo') or None,
                    'group-title': group,
                    'url': url,
                    'source': source_url,
                    'normalized_name': normalize_name(name)
                }
                channels.append(channel)
            i = j
        i += 1
    return channels

# XML parsing helper: namespace-tolerant
def _local_name(tag: str) -> str:
    return tag.split('}')[-1] if '}' in tag else tag

def _find_children_by_localname(elem, name):
    out = []
    for child in elem:
        if _local_name(child.tag) == name:
            out.append(child)
    return out

def parse_epg_xml(xml_content: str):
    """EPG XML verisini xml.etree.ElementTree kullanarak parse eder (namespace-tolerant)."""
    channels = {}
    if not xml_content or not xml_content.strip():
        return channels
    try:
        root = ET.fromstring(xml_content)
        # channel elemanları genelde root içinde veya tv root'u altındadır; kök altında herhangi bir yerde ara
        for ch in root.findall('.//'):
            if _local_name(ch.tag) != 'channel':
                continue
            ch_id = ch.get('id') or ch.get('channel')
            if not ch_id:
                # bazen <channel> içinde @id yoktur; atla
                continue

            # display-name'leri bulun
            display_names = []
            for dn in ch.findall('.//'):
                if _local_name(dn.tag) == 'display-name' and dn.text:
                    display_names.append(dn.text.strip())
            primary_name = display_names[0] if display_names else ch_id

            icon = None
            for ic in ch.findall('.//'):
                if _local_name(ic.tag) == 'icon':
                    icon = ic.get('src') or ic.get('url') or None
                    if icon:
                        break

            channels[ch_id] = {
                'id': ch_id,
                'display_name': primary_name,
                'icon': icon,
                'normalized_name': normalize_name(primary_name)
            }
    except Exception as e:
        print(f"[!] EPG XML parse uyarısı: {e}")
    return channels

def fetch_tv_logos_sync(github_token=None):
    """tv-logo reposundan logo verilerini indirir."""
    logos = {}
    endpoints = [
        "https://api.github.com/repos/tv-logo/tv-logos/contents/countries/turkey",
        "https://api.github.com/repos/tv-logo/tv-logos/contents/countries/tr",
        "https://api.github.com/repos/tv-logo/tv-logos/contents/files/countries/turkey"
    ]
    headers = {'User-Agent': DEFAULT_USER_AGENT}
    if github_token:
        headers['Authorization'] = f"token {github_token}"

    for gh_api in endpoints:
        try:
            req = Request(gh_api, headers=headers)
            with urlopen(req, timeout=10) as resp:
                data = json.loads(resp.read().decode('utf-8'))
                if isinstance(data, list):
                    for f in data:
                        name = f.get('name', '')
                        dl = f.get('download_url') or f.get('html_url', '').replace('/blob/', '/raw/')
                        if name and dl:
                            base_name = os.path.splitext(name)[0]
                            logos[normalize_name(base_name)] = dl
                    if logos:
                        break
        except Exception:
            continue
    return logos

def main():
    parser = argparse.ArgumentParser(description="M3U Otomatik Temizleme ve EPG / Logo Eşleme")
    parser.add_argument('--source', default='auto_update.json', help='auto_update.json dosya yolu veya URL')
    parser.add_argument('--outdir', default='.', help='Çıktı klasörü')
    parser.add_argument('--fetch-logos', action='store_true', default=False, help='tv-logos reposundan logoları çek')
    parser.add_argument('--github-token', default=None, help='GitHub Personal Access Token (opsiyonel)')
    parser.add_argument('--remove-dead', action='store_true', default=False, help='Çalışmayan ölü linkleri kaldır')
    parser.add_argument('--check-streams', action='store_true', default=False, help='Canlılık kontrolü yap')
    parser.add_argument('--stream-timeout', type=int, default=8, help='Akış kontrolü zaman aşımı (sn)')

    args = parser.parse_args()

    # 1. auto_update.json Dosyası Oku
    data = {}
    if args.source.startswith('http://') or args.source.startswith('https://'):
        txt = fetch_text_sync(args.source)
        if txt:
            try:
                data = json.loads(txt)
            except Exception as e:
                print(f"[x] JSON parse hatası: {e}")
                sys.exit(1)
    else:
        if os.path.exists(args.source):
            with open(args.source, 'r', encoding='utf-8') as f:
                data = json.load(f)
        else:
            print(f"[x] Kaynak dosya bulunamadı: {args.source}")
            sys.exit(1)

    m3u_urls = []
    epg_urls = []

    for category, urls in data.items():
        if category.lower() in ('epg', 'xml', 'epgs'):
            for u in urls:
                epg_urls.append(u)
        else:
            for u in urls:
                if isinstance(u, str):
                    if u.strip().lower().endswith('.xml'):
                        epg_urls.append(u)
                    else:
                        m3u_urls.append((category, u))

    print(f"[*] Toplam {len(m3u_urls)} M3U playlist adresi ve {len(epg_urls)} EPG adresi işlenecek.")

    # 2. M3U İndirme ve Parsing
    print("[*] M3U listeleri indiriliyor...")
    m3u_channels = []

    with ThreadPoolExecutor(max_workers=10) as executor:
        m3u_results = list(executor.map(lambda item: (item[0], item[1], fetch_text_sync(item[1])), m3u_urls))

    for cat, u, content in m3u_results:
        if content:
            parsed = parse_m3u(content, u, default_category=cat)
            m3u_channels.extend(parsed)
            print(f"  [+] {cat.upper()}: {u} -> {len(parsed)} kanal")
        else:
            print(f"  [-] İndirilemedi: {u}")

    print(f"[*] Toplam çekilen ham kanal sayısı: {len(m3u_channels)}")

    # 3. EPG İndirme
    print("[*] EPG verileri indiriliyor...")
    epg_channels_by_id = {}
    epg_channels_by_name = {}

    with ThreadPoolExecutor(max_workers=5) as executor:
        epg_results = list(executor.map(lambda u: (u, fetch_text_sync(u)), epg_urls))

    for u, content in epg_results:
        if content:
            parsed = parse_epg_xml(content)
            print(f"  [+] EPG ({u}): {len(parsed)} kanal bulundu.")
            for cid, ch_data in parsed.items():
                epg_channels_by_id[cid] = ch_data
                if ch_data.get('normalized_name'):
                    epg_channels_by_name[ch_data['normalized_name']] = ch_data

    # 4. Logo Verisi
    logos_db = {}
    if args.fetch_logos:
        print("[*] tv-logos reposundan logo indeksleri alınıyor...")
        logos_db = fetch_tv_logos_sync(github_token=args.github_token)
        print(f"  [+] {len(logos_db)} hazır logo eşleşmesi bulundu.")

    # 5. Kanal Eşleştirme ve Logo/EPG Atama
    processed_channels = []
    seen_urls = set()

    for ch in m3u_channels:
        url = ch.get('url')
        if not url or url in seen_urls:
            continue
        seen_urls.add(url)

        epg_match = None
        if ch.get('tvg-id') and ch['tvg-id'] in epg_channels_by_id:
            epg_match = epg_channels_by_id[ch['tvg-id']]
        elif ch.get('normalized_name') and ch['normalized_name'] in epg_channels_by_name:
            epg_match = epg_channels_by_name[ch['normalized_name']]

        if epg_match:
            if not ch.get('tvg-id'):
                ch['tvg-id'] = epg_match['id']

        assigned_logo = ch.get('tvg-logo')
        if not assigned_logo and epg_match and epg_match.get('icon'):
            assigned_logo = epg_match['icon']

        if not assigned_logo and ch.get('normalized_name') and logos_db:
            norm = ch['normalized_name']
            if norm in logos_db:
                assigned_logo = logos_db[norm]
            else:
                for lk, lurl in logos_db.items():
                    if lk and (lk in norm or norm in lk):
                        assigned_logo = lurl
                        break

        ch['tvg-logo'] = assigned_logo or ""
        ch['epg_matched'] = bool(epg_match)
        processed_channels.append(ch)

    # 6. Stream Kontrolü
    alive_channels = []
    dead_channels = []

    if args.check_streams and processed_channels:
        print(f"[*] {len(processed_channels)} kanal için canlılık testi başlatılıyor...")

        def check_task(ch):
            ok, info = check_stream_sync(ch['url'], timeout=args.stream_timeout)
            ch['alive'] = ok
            ch['check_info'] = info
            return ch

        with ThreadPoolExecutor(max_workers=20) as executor:
            results = list(executor.map(check_task, processed_channels))

        for ch in results:
            if ch.get('alive'):
                alive_channels.append(ch)
            else:
                dead_channels.append(ch)
        print(f"  [+] Canlı yayın: {len(alive_channels)}, Ölü yayın: {len(dead_channels)}")
    else:
        alive_channels = processed_channels

    # 7. Çıktı Dosyaları
    os.makedirs(args.outdir, exist_ok=True)

    output_m3u_path = os.path.join(args.outdir, 'cleaned_playlist.m3u')
    m3u_lines = ["#EXTM3U"]

    channels_to_write = alive_channels if args.remove_dead else processed_channels

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
        m3u_lines.append(f"#EXTINF:-1{attr_str},{ch.get('name', 'Kanal')}")
        m3u_lines.append(ch['url'])

    with open(output_m3u_path, 'w', encoding='utf-8') as f:
        f.write("\n".join(m3u_lines))

    print(f"[*] OLUŞTURULDU: {output_m3u_path} ({len(channels_to_write)} kanal)")

    report = {
        'total_channels_parsed': len(m3u_channels),
        'unique_channels': len(processed_channels),
        'alive_channels': len(alive_channels),
        'dead_channels': len(dead_channels),
        'epg_matches_count': sum(1 for c in processed_channels if c.get('epg_matched')),
        'logo_matches_count': sum(1 for c in processed_channels if c.get('tvg-logo')),
        'categories': {}
    }

    for c in channels_to_write:
        grp = c.get('group-title', 'DİĞER')
        report['categories'][grp] = report['categories'].get(grp, 0) + 1

    report_path = os.path.join(args.outdir, 'report.json')
    with open(report_path, 'w', encoding='utf-8') as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    print(f"[*] Rapor oluşturuldu: {report_path}")
    print("[✔] İşlem başarıyla tamamlandı!")

if __name__ == '__main__':
    main()