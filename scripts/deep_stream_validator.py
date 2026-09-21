#!/usr/bin/env python3
"""
deep_stream_validator.py
------------------------
- Derin akış doğrulama (Deep Stream Validator) worker script.
- out/cleaned_playlist.m3u veya cleaned_playlist.m3u kaynaklarını okur.
- Kanalları HTTP / akış başlıkları ve paket erişilebilirliği yönünden derinlemesine test eder.
- ÇOKLU İŞ PARÇACIĞI (Multi-Threading) & HOST SINIRLAMASI:
  Her ana sunucudan (host / domain) aynı anda KESİNLİKLE EN FAZLA 1 KANAL taranır.
  Farklı hostlara ait kanallar paralel (ör. 35 eşzamanlı worker) taranarak muazzam hız kazanılır.
- 48 dakikalık çalışma penceresinde (chunking) çalışır, out/validator_state.json ile kaldığı yerden devam eder.
- İşlem %100 tamamlandığında out/full_cleaned_playlist.m3u dosyasını atomik olarak üretir.
- 1 haftalık (7 gün) cooldown (bekleme) süresi uygular (--ignore-cooldown ile zorlanabilir).
"""

import os
import sys
import json
import time
import ssl
import argparse
import threading
from collections import deque
import urllib.request
import urllib.error
from urllib.parse import urlparse, quote

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, ".."))

DEFAULT_STATE_FILE = "out/validator_state.json"
DEFAULT_OUTPUT_PLAYLIST = "out/full_cleaned_playlist.m3u"
DEFAULT_WORKERS = 35
DEFAULT_TIMEOUT = 5
MAX_RUN_SECONDS = 48 * 60  # 48 dakika (GitHub Actions 50 dk sınırına takılmamak için)
COOLDOWN_SECONDS = 7 * 24 * 3600  # 1 Hafta
MIN_HOST_INTERVAL = 0.25  # Aynı hosta yapılacak sonraki istek için nezaket aralığı (sn)

# Kendinden imzalı (self-signed) veya süresi geçmiş IPTV sertifikalarında SSL hatasını önler
SSL_CONTEXT = ssl.create_default_context()
SSL_CONTEXT.check_hostname = False
SSL_CONTEXT.verify_mode = ssl.CERT_NONE

def resolve_path(rel_path):
    if os.path.isabs(rel_path):
        return rel_path
    if os.path.exists(rel_path):
        return os.path.abspath(rel_path)
    cand = os.path.join(REPO_ROOT, rel_path)
    if os.path.exists(cand):
        return cand
    return os.path.abspath(rel_path)

def find_input_playlist(custom_path=None):
    if custom_path:
        resolved = resolve_path(custom_path)
        if os.path.exists(resolved):
            return resolved

    candidates = [
        "out/cleaned_playlist.m3u",
        "cleaned_playlist.m3u",
        os.path.join(REPO_ROOT, "out", "cleaned_playlist.m3u"),
        os.path.join(REPO_ROOT, "cleaned_playlist.m3u"),
    ]
    for c in candidates:
        if os.path.exists(c) and os.path.getsize(c) > 0:
            return os.path.abspath(c)
    return resolve_path(candidates[0])

def load_state(state_file_path):
    resolved = resolve_path(state_file_path)
    if os.path.exists(resolved):
        try:
            with open(resolved, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception as e:
            print(f"[!] State dosyası okunurken hata: {e}, yeni state oluşturulacak.")
    return {
        "index": 0,
        "valid_channels": [],
        "tested_urls": [],
        "last_completed_at": 0,
        "total_channels": 0
    }

def save_state(state, state_file_path):
    resolved = resolve_path(state_file_path)
    os.makedirs(os.path.dirname(resolved), exist_ok=True)
    temp_path = f"{resolved}.tmp"
    with open(temp_path, "w", encoding="utf-8") as f:
        json.dump(state, f, ensure_ascii=False, indent=2)
        f.flush()
        os.fsync(f.fileno())
    os.replace(temp_path, resolved)

def parse_m3u(filepath):
    resolved = resolve_path(filepath)
    if not os.path.exists(resolved):
        return []
    channels = []
    current_extinf = None
    current_directives = []
    with open(resolved, "r", encoding="utf-8", errors="ignore") as f:
        for line in f:
            line_str = line.strip()
            if line_str.startswith("#EXTINF:"):
                current_extinf = line_str
            elif line_str.startswith("#EXTVLCOPT") or line_str.startswith("#EXTHTTP") or line_str.startswith("#KODIPROP"):
                current_directives.append(line_str)
            elif line_str and not line_str.startswith("#"):
                if current_extinf:
                    channels.append({
                        "extinf": current_extinf,
                        "url": line_str,
                        "directives": list(current_directives)
                    })
                    current_extinf = None
                    current_directives.clear()
    return channels

def quote_safe_url(url: str) -> str:
    if not url:
        return ""
    url = url.strip()
    try:
        return quote(url, safe=":/%#?=@[]!$&'()*+,;")
    except Exception:
        return url

def extract_host(url: str) -> str:
    try:
        parsed = urlparse(url)
        host = parsed.hostname
        if host:
            return host.lower()
    except Exception:
        pass
    return "unknown_host"

def validate_stream(url, timeout=5, custom_directives=None):
    clean_url = quote_safe_url(url)
    if not clean_url or not (clean_url.startswith("http://") or clean_url.startswith("https://")):
        # Yerel dosya veya doğrudan RTMP/RTSP değilse standart dışı
        return False

    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Accept": "*/*",
        "Range": "bytes=0-4096"  # Tüm akışın indirilmesini önler, ilk 4KB yeterlidir
    }

    if custom_directives:
        for d in custom_directives:
            if "http-user-agent=" in d:
                headers["User-Agent"] = d.split("http-user-agent=", 1)[1].strip()
            elif "http-referrer=" in d:
                headers["Referer"] = d.split("http-referrer=", 1)[1].strip()

    req = urllib.request.Request(clean_url, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=timeout, context=SSL_CONTEXT) as resp:
            status = getattr(resp, "status", 200)
            if status in (200, 206):
                chunk = resp.read(1024)
                if len(chunk) > 0:
                    return True
                cl = resp.headers.get("Content-Length")
                if cl and int(cl) > 0:
                    return True
                ct = resp.headers.get("Content-Type", "").lower()
                if any(t in ct for t in ("mpegurl", "video", "audio", "octet-stream")):
                    return True
    except urllib.error.HTTPError as e:
        # Range başlığını kabul etmeyen bazı sunucular 416 döner, başlıksız kısa GET dene
        if e.code == 416:
            try:
                simple_req = urllib.request.Request(clean_url, headers={"User-Agent": headers["User-Agent"]})
                with urllib.request.urlopen(simple_req, timeout=timeout, context=SSL_CONTEXT) as resp:
                    if resp.status == 200:
                        chunk = resp.read(512)
                        return len(chunk) > 0
            except Exception:
                pass
    except Exception:
        pass
    return False

class HostThrottledDispatcher:
    """
    Kritik Kural: Her ana sunucudan (host) aynı anda SADECE VE SADECE 1 KANAL taranabilir.
    Farklı hostlara ait kanallar ise eşzamanlı (multi-threaded) taranır.
    """
    def __init__(self, channels):
        self.lock = threading.Lock()
        self.condition = threading.Condition(self.lock)
        self.active_hosts = set()
        self.host_last_req = {}
        self.host_queues = {}
        self.available_hosts = deque()

        for ch in channels:
            h = extract_host(ch["url"])
            if h not in self.host_queues:
                self.host_queues[h] = deque()
                self.available_hosts.append(h)
            self.host_queues[h].append(ch)

        self.remaining_count = len(channels)
        self.stop_requested = False

    def get_next(self):
        with self.condition:
            while not self.stop_requested and self.remaining_count > 0:
                now = time.time()
                # Kullanılabilir hostlar arasından bekleme süresini tamamlamış bir host seç
                candidates_checked = 0
                total_available = len(self.available_hosts)
                chosen_host = None

                while candidates_checked < total_available:
                    host = self.available_hosts.popleft()
                    last_time = self.host_last_req.get(host, 0)
                    if (now - last_time) >= MIN_HOST_INTERVAL:
                        chosen_host = host
                        break
                    else:
                        # Henüz aralığı dolmadıysa kuyruğun sonuna al
                        self.available_hosts.append(host)
                        candidates_checked += 1

                if chosen_host:
                    ch = self.host_queues[chosen_host].popleft()
                    self.active_hosts.add(chosen_host)
                    return chosen_host, ch

                # Müsait host kalmadıysa veya hepsi aktifse bekle
                self.condition.wait(timeout=0.1)

            return None, None

    def mark_done(self, host):
        with self.condition:
            self.active_hosts.discard(host)
            self.host_last_req[host] = time.time()
            self.remaining_count -= 1

            if host in self.host_queues and len(self.host_queues[host]) > 0:
                self.available_hosts.append(host)
            elif host in self.host_queues and len(self.host_queues[host]) == 0:
                del self.host_queues[host]

            self.condition.notify_all()

    def request_stop(self):
        with self.condition:
            self.stop_requested = True
            self.condition.notify_all()

def main():
    parser = argparse.ArgumentParser(description="Deep Stream Validator (Host Başına Tek Kanal Eşzamanlı Tarama)")
    parser.add_argument("--input", default=None, help="Giriş playlisti (varsayılan: out/cleaned_playlist.m3u veya cleaned_playlist.m3u)")
    parser.add_argument("--output", default=DEFAULT_OUTPUT_PLAYLIST, help="Nihai çıktı playlisti")
    parser.add_argument("--state-file", default=DEFAULT_STATE_FILE, help="Durum (checkpoint) dosyası")
    parser.add_argument("--workers", type=int, default=DEFAULT_WORKERS, help="Farklı hostlar için eşzamanlı iş parçacığı sayısı")
    parser.add_argument("--timeout", type=int, default=DEFAULT_TIMEOUT, help="Akış kontrolü zaman aşımı (sn)")
    parser.add_argument("--max-run-minutes", type=float, default=48.0, help="Maksimum çalışma süresi (dakika)")
    parser.add_argument("--ignore-cooldown", action="store_true", default=False, help="7 günlük bekleme süresini yoksay")

    args = parser.parse_args()

    print("=== Deep Stream Validator (Akıllı Host Dağıtımlı Paralel Mod) Başlatıldı ===")
    print(f"[*] Eşzamanlı Worker Sayısı: {args.workers} (Kural: Her hosttan aynı anda en fazla 1 kanal)")
    print(f"[*] Akış Timeout Süresi: {args.timeout} sn")

    state_file = resolve_path(args.state_file)
    state = load_state(state_file)
    now = time.time()

    # 1 Haftalık Cooldown Kontrolü
    if not args.ignore_cooldown and state.get("last_completed_at", 0) > 0:
        elapsed = now - state["last_completed_at"]
        if elapsed < COOLDOWN_SECONDS:
            remaining_days = (COOLDOWN_SECONDS - elapsed) / 86400
            print(f"Cooldown aktif. Son tam taramadan bu yana {remaining_days:.1f} gün geçti (7 gün bekleniyor). İşlem atlanıyor.")
            sys.exit(0)

    input_file = find_input_playlist(args.input)
    channels = parse_m3u(input_file)
    if not channels:
        print(f"[!] Uyarı: Giriş listesi bulunamadı veya boş ({input_file}). İşlem yapılamıyor.")
        sys.exit(0)

    total_channels = len(channels)
    state["total_channels"] = total_channels

    # Geriye dönük uyumluluk: index veya tested_urls üzerinden daha önce test edilenleri belirle
    tested_urls = set(state.get("tested_urls", []))
    prev_index = state.get("index", 0)
    if prev_index > 0 and len(tested_urls) < prev_index:
        for ch in channels[:prev_index]:
            tested_urls.add(ch["url"])

    valid_channels = state.get("valid_channels", [])
    valid_urls_set = set(ch["url"] for ch in valid_channels)

    # Henüz test edilmemiş kanalları filtrele
    pending_channels = [ch for ch in channels if ch["url"] not in tested_urls]

    print(f"[*] Toplam Kanal: {total_channels}")
    print(f"[*] Daha Önce Taranmış: {len(tested_urls)}, Geçerli Bulunmuş: {len(valid_channels)}")
    print(f"[*] Şimdi Taranacak Kalan Kanal: {len(pending_channels)}")

    if not pending_channels:
        print("[*] Taranacak yeni kanal bulunmuyor. Tüm liste zaten doğrulanmış.")
        state["index"] = total_channels
        save_state(state, state_file)
        sys.exit(0)

    dispatcher = HostThrottledDispatcher(pending_channels)
    stop_event = threading.Event()
    max_run_seconds = args.max_run_minutes * 60
    start_time = time.time()

    results_lock = threading.Lock()
    tested_in_this_session = 0
    last_saved_time = time.time()

    def worker_loop():
        nonlocal tested_in_this_session, last_saved_time
        while not stop_event.is_set():
            if (time.time() - start_time) > max_run_seconds:
                stop_event.set()
                dispatcher.request_stop()
                break

            host, ch = dispatcher.get_next()
            if host is None:
                break

            is_valid = validate_stream(
                ch["url"],
                timeout=args.timeout,
                custom_directives=ch.get("directives")
            )

            dispatcher.mark_done(host)

            should_save = False
            with results_lock:
                tested_urls.add(ch["url"])
                tested_in_this_session += 1
                if is_valid and ch["url"] not in valid_urls_set:
                    valid_channels.append(ch)
                    valid_urls_set.add(ch["url"])

                current_tested = len(tested_urls)
                if tested_in_this_session % 50 == 0 or (time.time() - last_saved_time) >= 15:
                    should_save = True
                    last_saved_time = time.time()

            if should_save:
                with results_lock:
                    elapsed = time.time() - start_time
                    speed = tested_in_this_session / max(elapsed, 0.1)
                    pct = (len(tested_urls) / total_channels) * 100
                    active_host_count = len(dispatcher.active_hosts)
                    print(
                        f"İlerleme: {len(tested_urls)}/{total_channels} (%{pct:.1f}) | "
                        f"Geçerli: {len(valid_channels)} | "
                        f"Aktif Farklı Host: {active_host_count} | "
                        f"Hız: {speed:.1f} kanal/sn"
                    )
                    state["index"] = len(tested_urls)
                    state["valid_channels"] = valid_channels
                    state["tested_urls"] = list(tested_urls)
                    save_state(state, state_file)

    # Worker iş parçacıklarını başlat
    workers = []
    worker_count = min(args.workers, len(dispatcher.host_queues))
    if worker_count < 1:
        worker_count = 1

    print(f"[*] {worker_count} aktif iş parçacığı başlatılıyor...")
    for _ in range(worker_count):
        t = threading.Thread(target=worker_loop)
        t.daemon = True
        t.start()
        workers.append(t)

    try:
        while any(t.is_alive() for t in workers):
            if (time.time() - start_time) > max_run_seconds:
                print("\n[!] Belirtilen süre sınırına yaklaşıldı. Durum kaydediliyor...")
                stop_event.set()
                dispatcher.request_stop()
                break
            time.sleep(0.5)

        for t in workers:
            t.join(timeout=2.0)
    except KeyboardInterrupt:
        print("\n[!] Kullanıcı tarafından durduruldu (Ctrl+C). Mevcut ilerleme kaydediliyor...")
        stop_event.set()
        dispatcher.request_stop()
        for t in workers:
            t.join(timeout=2.0)

    # Son durumu diske kaydet
    state["index"] = len(tested_urls)
    state["valid_channels"] = valid_channels
    state["tested_urls"] = list(tested_urls)
    save_state(state, state_file)

    # Tarama tamamlandı mı kontrol et
    if len(tested_urls) >= total_channels:
        print("\n🎉 Tüm kanallar derin doğrulamadan başarıyla geçti!")
        output_file = resolve_path(args.output)
        os.makedirs(os.path.dirname(output_file), exist_ok=True)
        temp_out = f"{output_file}.tmp"
        with open(temp_out, "w", encoding="utf-8") as f:
            f.write("#EXTM3U\n")
            for ch in valid_channels:
                for d in ch.get("directives", []):
                    f.write(f"{d}\n")
                f.write(f"{ch['extinf']}\n{ch['url']}\n")
            f.flush()
            os.fsync(f.fileno())
        os.replace(temp_out, output_file)

        # State'i sıfırla ve cooldown damgası bas
        state["index"] = 0
        state["valid_channels"] = []
        state["tested_urls"] = []
        state["last_completed_at"] = time.time()
        save_state(state, state_file)
        print(f"[✔] Nihai temizlenmiş tam liste oluşturuldu: {output_file} ({len(valid_channels)} çalışan yayın)")
    else:
        print(f"[*] Durum başarıyla kaydedildi: {len(tested_urls)}/{total_channels} kanal tamamlandı. Bir sonraki çalıştırmada kaldığı yerden devam edecek.")

if __name__ == "__main__":
    main()

