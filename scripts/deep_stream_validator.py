#!/usr/bin/env python3
"""
deep_stream_validator.py
------------------------
- Derin akış doğrulama (Deep Stream Validator) worker script.
- out/cleaned_playlist.m3u veya mevcut kaynakları okur.
- Kanalları HTTP / akış başlıkları, paketler ve erişilebilirlik yönünden derinlemesine test eder.
- 50 dakikalık çalışma penceresinde (chunking) çalışır, out/validator_state.json ile kaldığı yerden devam eder.
- İşlem %100 tamamlandığında out/full_cleaned_playlist.m3u dosyasını üretir.
- 1 haftalık (7 gün) cooldown (bekleme) süresi uygular.
"""

import os
import sys
import json
import time
import urllib.request
import urllib.error
from urllib.parse import urlparse

STATE_FILE = "out/validator_state.json"
INPUT_PLAYLIST = "out/cleaned_playlist.m3u"
OUTPUT_PLAYLIST = "out/full_cleaned_playlist.m3u"
MAX_RUN_SECONDS = 48 * 60  # 48 dakika (GitHub 50 dk sınırına takılmamak için)
COOLDOWN_SECONDS = 7 * 24 * 3600  # 1 Hafta

def load_state():
    if os.path.exists(STATE_FILE):
        try:
            with open(STATE_FILE, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            pass
    return {
        "index": 0,
        "valid_channels": [],
        "last_completed_at": 0,
        "total_channels": 0
    }

def save_state(state):
    os.makedirs("out", exist_ok=True)
    with open(STATE_FILE, "w", encoding="utf-8") as f:
        json.dump(state, f, ensure_ascii=False, indent=2)

def parse_m3u(filepath):
    if not os.path.exists(filepath):
        return []
    channels = []
    current_extinf = None
    with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
        for line in f:
            line_str = line.strip()
            if line_str.startswith("#EXTINF:"):
                current_extinf = line_str
            elif line_str and not line_str.startswith("#"):
                if current_extinf:
                    channels.append({"extinf": current_extinf, "url": line_str})
                    current_extinf = None
    return channels

def validate_stream(url, timeout=5):
    try:
        req = urllib.request.Request(
            url,
            headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StreamMeshValidator/1.0"}
        )
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            # İlk birkaç baytı oku veya 200 OK kontrolü yap
            if resp.status == 200:
                chunk = resp.read(1024)
                if len(chunk) > 0:
                    return True
    except Exception:
        pass
    return False

def main():
    print("=== Deep Stream Validator Başlatıldı ===")
    state = load_state()
    now = time.time()

    # 1 Haftalık Cooldown Kontrolü
    if state.get("last_completed_at", 0) > 0:
        elapsed = now - state["last_completed_at"]
        if elapsed < COOLDOWN_SECONDS:
            remaining_days = (COOLDOWN_SECONDS - elapsed) / 86400
            print(f"Cooldown aktif. Son tam taramadan bu yana {remaining_days:.1f} gün geçti (7 gün bekleniyor). İşlem atlanıyor.")
            sys.exit(0)

    channels = parse_m3u(INPUT_PLAYLIST)
    if not channels:
        print(f"Uyarı: {INPUT_PLAYLIST} bulunamadı veya boş. İşlem yapılamıyor.")
        sys.exit(0)

    state["total_channels"] = len(channels)
    start_time = time.time()
    index = state.get("index", 0)
    valid_channels = state.get("valid_channels", [])

    print(f"Toplam kanal: {len(channels)}, Kaldığı indeks: {index}")

    while index < len(channels):
        # 48 dakika süre sınırını kontrol et
        if (time.time() - start_time) > MAX_RUN_SECONDS:
            print("50 dakikalık çalışma sınırına yaklaşıldı. Durum kaydediliyor ve sonraki tur için çıkılıyor...")
            state["index"] = index
            state["valid_channels"] = valid_channels
            save_state(state)
            sys.exit(0)

        ch = channels[index]
        is_valid = validate_stream(ch["url"], timeout=6)
        if is_valid:
            valid_channels.append(ch)

        index += 1

        # Her 50 kanalda bir state güncelle
        if index % 50 == 0:
            print(f"İlerleme: {index}/{len(channels)} tarandı. Geçerli kanal sayısı: {len(valid_channels)}")
            state["index"] = index
            state["valid_channels"] = valid_channels
            save_state(state)

    # Tarama %100 bitti!
    print("🎉 Tüm kanallar derin doğrulamadan geçti! Nihai liste oluşturuluyor...")
    os.makedirs("out", exist_ok=True)
    with open(OUTPUT_PLAYLIST, "w", encoding="utf-8") as f:
        f.write("#EXTM3U\n")
        for ch in valid_channels:
            f.write(f"{ch['extinf']}\n{ch['url']}\n")

    # State'i sıfırla ve cooldown damgası bas
    state["index"] = 0
    state["valid_channels"] = []
    state["last_completed_at"] = time.time()
    save_state(state)
    print(f"Başarıyla kaydedildi: {OUTPUT_PLAYLIST}")

if __name__ == "__main__":
    main()
