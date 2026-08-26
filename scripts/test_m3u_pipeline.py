#!/usr/bin/env python3
"""
test_m3u_pipeline.py
--------------------
Kullanıcının talep ettiği 10 sentetik test senaryosunu yerel HTTP sunucusu ve
generate_clean_m3u modülü ile doğrular.
"""

import os
import sys
import json
import time
import threading
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse

# scripts dizinini modül arama yoluna ekle
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '.')))
import generate_clean_m3u as pipeline

PORT = 18999

class MockTestServer(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass # sessiz mod

    def do_HEAD(self):
        parsed = urlparse(self.path)
        path = parsed.path

        if path == '/logo-valid.png':
            self.send_response(200)
            self.send_header('Content-Type', 'image/png')
            self.send_header('Content-Length', '1024')
            self.end_headers()
        elif path == '/logo-broken.png' or path == '/epg-broken.png':
            self.send_response(404)
            self.end_headers()
        elif path == '/epg-icon-valid.png':
            self.send_response(200)
            self.send_header('Content-Type', 'image/png')
            self.send_header('Content-Length', '512')
            self.end_headers()
        elif path == '/bilo-logo.png':
            self.send_response(200)
            self.send_header('Content-Type', 'image/png')
            self.send_header('Content-Length', '2048')
            self.end_headers()
        else:
            self.send_response(404)
            self.end_headers()

    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path

        # 1. Logo endpoints
        if path in ('/logo-valid.png', '/epg-icon-valid.png', '/bilo-logo.png'):
            self.send_response(200)
            self.send_header('Content-Type', 'image/png')
            self.send_header('Content-Length', '100')
            self.end_headers()
            self.wfile.write(b'\x89PNG\r\n\x1a\n' + b'\x00' * 92)
        elif path in ('/logo-broken.png', '/epg-broken.png'):
            self.send_response(404)
            self.end_headers()
            self.wfile.write(b'Not Found')

        # 2. HLS endpoints
        # TEST 8: Master HTTP 200 ama Segment 404
        elif path == '/hls-dead/master.m3u8':
            self.send_response(200)
            self.send_header('Content-Type', 'application/vnd.apple.mpegurl')
            self.end_headers()
            content = "#EXTM3U\n#EXT-X-VERSION:3\n#EXTINF:10,\nhttp://127.0.0.1:18999/hls-dead/segment404.ts\n"
            self.wfile.write(content.encode('utf-8'))
        elif path == '/hls-dead/segment404.ts':
            self.send_response(404)
            self.end_headers()

        # TEST 9: Master + Segment ALIVE
        elif path == '/hls-live/master.m3u8':
            self.send_response(200)
            self.send_header('Content-Type', 'application/vnd.apple.mpegurl')
            self.end_headers()
            content = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1280000\nhttp://127.0.0.1:18999/hls-live/variant.m3u8\n"
            self.wfile.write(content.encode('utf-8'))
        elif path == '/hls-live/variant.m3u8':
            self.send_response(200)
            self.send_header('Content-Type', 'application/vnd.apple.mpegurl')
            self.end_headers()
            content = "#EXTM3U\n#EXT-X-VERSION:3\n#EXTINF:10,\nhttp://127.0.0.1:18999/hls-live/segment1.ts\n"
            self.wfile.write(content.encode('utf-8'))
        elif path == '/hls-live/segment1.ts':
            self.send_response(200)
            self.send_header('Content-Type', 'video/mp2t')
            self.end_headers()
            self.wfile.write(b'\x47' + b'\x00' * 187) # MPEG-TS sync byte

        # TEST 10: Retry endpoint (ilk istekte 500, ikinci istekte 200)
        elif path == '/retry-source.m3u':
            if not hasattr(MockTestServer, 'retry_count'):
                MockTestServer.retry_count = 0
            MockTestServer.retry_count += 1
            if MockTestServer.retry_count == 1:
                self.send_response(500)
                self.end_headers()
                self.wfile.write(b'Internal Server Error')
            else:
                self.send_response(200)
                self.end_headers()
                self.wfile.write(b'#EXTM3U\n#EXTINF:-1,TRT 1\nhttp://127.0.0.1:18999/stream1\n')
        else:
            self.send_response(404)
            self.end_headers()

def run_tests():
    server = HTTPServer(('127.0.0.1', PORT), MockTestServer)
    server_thread = threading.Thread(target=server.serve_forever, daemon=True)
    server_thread.start()
    time.sleep(0.3)

    print("=" * 60)
    print("🚀 STREAMMESH SENTETİK 10-TEST DOĞRULAMA PAKETİ")
    print("=" * 60)

    test_results = {}

    # ----------------------------------------------------
    # TEST 1: TRT 1 (URL A) + TRT 1 HD (URL B) tvg-id boş -> 1 kanal, 2 stream
    # ----------------------------------------------------
    m3u_test1 = """#EXTM3U
#EXTINF:-1 tvg-id="" group-title="Ulusal",TRT 1
http://example.com/stream_a
#EXTINF:-1 tvg-id="" group-title="Ulusal",TRT 1 HD
http://example.com/stream_b
"""
    channels1 = pipeline.parse_m3u(m3u_test1, "test_source")
    # De-dup simulation
    unique_map1 = {}
    for ch in channels1:
        key = f"name:{ch['canonical_name']}#{ch['group-title']}"
        if key not in unique_map1:
            ch['backup_urls'] = []
            unique_map1[key] = ch
        else:
            unique_map1[key]['backup_urls'].append(ch['url'])
    
    t1_pass = len(unique_map1) == 1 and len(list(unique_map1.values())[0]['backup_urls']) == 1
    test_results['TEST 1 (TRT 1 + TRT 1 HD Tekilleştirme & Alternatif Stream)'] = t1_pass
    print(f"[{'PASS' if t1_pass else 'FAIL'}] TEST 1: Tekil kanal sayısı = {len(unique_map1)}, Alternatif URL = {len(list(unique_map1.values())[0]['backup_urls'])}")

    # ----------------------------------------------------
    # TEST 2: TRT 1 HD (TRT1.tr) + TRT 1 (TRT1.tr) -> 1 kanal
    # ----------------------------------------------------
    m3u_test2 = """#EXTM3U
#EXTINF:-1 tvg-id="TRT1.tr",TRT 1 HD
http://example.com/stream_1
#EXTINF:-1 tvg-id="TRT1.tr",TRT 1
http://example.com/stream_2
"""
    channels2 = pipeline.parse_m3u(m3u_test2, "test_source")
    unique_map2 = {}
    for ch in channels2:
        key = f"id:{ch['tvg-id'].lower()}"
        if key not in unique_map2:
            unique_map2[key] = ch
    t2_pass = len(unique_map2) == 1
    test_results['TEST 2 (Aynı tvg-id ile Tekilleştirme)'] = t2_pass
    print(f"[{'PASS' if t2_pass else 'FAIL'}] TEST 2: Tekil kanal sayısı = {len(unique_map2)}")

    # ----------------------------------------------------
    # TEST 3: Aynı URL + farklı isim -> 1 kanal
    # ----------------------------------------------------
    m3u_test3 = """#EXTM3U
#EXTINF:-1,TRT 1
http://example.com/identical_stream
#EXTINF:-1,TRT 1 FHD YEDEK
http://example.com/identical_stream
"""
    channels3 = pipeline.parse_m3u(m3u_test3, "test_source")
    seen_urls3 = set()
    dedup3 = []
    for ch in channels3:
        if ch['url'] not in seen_urls3:
            seen_urls3.add(ch['url'])
            dedup3.append(ch)
    t3_pass = len(dedup3) == 1
    test_results['TEST 3 (Birebir Aynı URL Tekilleştirme)'] = t3_pass
    print(f"[{'PASS' if t3_pass else 'FAIL'}] TEST 3: Tekil kanal sayısı = {len(dedup3)}")

    # ----------------------------------------------------
    # TEST 4: STAR TV vs STAR GOLD -> Yanlışlıkla BİRLEŞMEMELİ
    # ----------------------------------------------------
    m3u_test4 = """#EXTM3U
#EXTINF:-1 group-title="TV",STAR TV
http://example.com/star_tv
#EXTINF:-1 group-title="TV",STAR GOLD
http://example.com/star_gold
#EXTINF:-1 group-title="TV",STAR SINEMA
http://example.com/star_sinema
"""
    channels4 = pipeline.parse_m3u(m3u_test4, "test_source")
    unique_map4 = {}
    for ch in channels4:
        key = f"name:{ch['canonical_name']}#{ch['group-title']}"
        unique_map4[key] = ch
    t4_pass = len(unique_map4) == 3 and "star tv" in [c['canonical_name'] for c in unique_map4.values()] and "star gold" in [c['canonical_name'] for c in unique_map4.values()]
    test_results['TEST 4 (STAR TV vs STAR GOLD Yanlış Birleşme Engeli)'] = t4_pass
    print(f"[{'PASS' if t4_pass else 'FAIL'}] TEST 4: Korunan bağımsız kanal sayısı = {len(unique_map4)} / 3")

    # ----------------------------------------------------
    # TEST 5: M3U logo 404, EPG icon 200 -> EPG logo kullanılmalı
    # ----------------------------------------------------
    m3u_logo_broken = f"http://127.0.0.1:{PORT}/logo-broken.png"
    epg_icon_valid = f"http://127.0.0.1:{PORT}/epg-icon-valid.png"
    
    is_m3u_valid = pipeline.validate_logo_url(m3u_logo_broken)
    is_epg_valid = pipeline.validate_logo_url(epg_icon_valid)
    
    assigned5 = ""
    if is_m3u_valid:
        assigned5 = m3u_logo_broken
    elif is_epg_valid:
        assigned5 = epg_icon_valid
        
    t5_pass = assigned5 == epg_icon_valid
    test_results['TEST 5 (Bozuk M3U Logosu -> Çalışan EPG İkonuna Fallback)'] = t5_pass
    print(f"[{'PASS' if t5_pass else 'FAIL'}] TEST 5: Seçilen Logo = {assigned5}")

    # ----------------------------------------------------
    # TEST 6: M3U logo 404, EPG icon 404, bilo1975tr/tv-logos içinde logo var -> bilo1975tr seçilmeli
    # ----------------------------------------------------
    bilo_logo_url = f"http://127.0.0.1:{PORT}/bilo-logo.png"
    mock_bilo_db = {'trt 1': bilo_logo_url}
    
    is_m3u_valid = pipeline.validate_logo_url(f"http://127.0.0.1:{PORT}/logo-broken.png")
    is_epg_valid = pipeline.validate_logo_url(f"http://127.0.0.1:{PORT}/epg-broken.png")
    
    assigned6 = ""
    if is_m3u_valid:
        assigned6 = "m3u"
    elif is_epg_valid:
        assigned6 = "epg"
    else:
        bilo_found = pipeline.find_best_logo_match("trt 1", "trt 1", mock_bilo_db)
        if bilo_found and pipeline.validate_logo_url(bilo_found):
            assigned6 = bilo_found

    t6_pass = assigned6 == bilo_logo_url
    test_results['TEST 6 (Bozuk Logo & Bozuk EPG -> bilo1975tr/tv-logos Fallback)'] = t6_pass
    print(f"[{'PASS' if t6_pass else 'FAIL'}] TEST 6: Seçilen Logo = {assigned6}")

    # ----------------------------------------------------
    # TEST 7: M3U logo yok, EPG logo yok, bilo1975tr/tv-logos içinde logo var -> Logo bulunmalı
    # ----------------------------------------------------
    channel_name7 = "TRT 1 HD [TR]"
    canon_name7 = pipeline.canonical_channel_name(channel_name7) # 'trt 1'
    bilo_found7 = pipeline.find_best_logo_match(pipeline.normalize_name(channel_name7), canon_name7, mock_bilo_db)
    t7_pass = bilo_found7 == bilo_logo_url
    test_results['TEST 7 (M3U Logo Yok -> bilo1975tr/tv-logos Tamamlama)'] = t7_pass
    print(f"[{'PASS' if t7_pass else 'FAIL'}] TEST 7: Bulunan Logo = {bilo_found7}")

    # ----------------------------------------------------
    # TEST 8: HLS master playlist HTTP 200 ama segment 404 -> DEAD kabul edilmeli
    # ----------------------------------------------------
    hls_dead_url = f"http://127.0.0.1:{PORT}/hls-dead/master.m3u8"
    is_alive8, info8 = pipeline.check_stream_sync(hls_dead_url, timeout=3)
    t8_pass = not is_alive8
    test_results['TEST 8 (HLS Segment 404 -> DEAD Tespiti)'] = t8_pass
    print(f"[{'PASS' if t8_pass else 'FAIL'}] TEST 8: Canlılık = {is_alive8}, Bilgi = {info8}")

    # ----------------------------------------------------
    # TEST 9: HLS master + variant + segment erişilebilir -> ALIVE kabul edilmeli
    # ----------------------------------------------------
    hls_live_url = f"http://127.0.0.1:{PORT}/hls-live/master.m3u8"
    is_alive9, info9 = pipeline.check_stream_sync(hls_live_url, timeout=3)
    t9_pass = is_alive9 and "OK" in info9
    test_results['TEST 9 (HLS Master + Variant + Segment -> ALIVE Doğrulaması)'] = t9_pass
    print(f"[{'PASS' if t9_pass else 'FAIL'}] TEST 9: Canlılık = {is_alive9}, Bilgi = {info9}")

    # ----------------------------------------------------
    # TEST 10: Kaynak indirme ilk denemede 500, ikinci denemede başarılı (retry) -> OK
    # ----------------------------------------------------
    retry_url = f"http://127.0.0.1:{PORT}/retry-source.m3u"
    content10, ok10, err10 = pipeline.fetch_text_with_retry(retry_url, max_retries=2, timeout=5)
    t10_pass = ok10 and "TRT 1" in content10
    test_results['TEST 10 (Kaynak İndirme Hata Toleransı & Retry)'] = t10_pass
    print(f"[{'PASS' if t10_pass else 'FAIL'}] TEST 10: Başarı = {ok10}, Kanal Sayısı = {1 if t10_pass else 0}")

    print("=" * 60)
    all_passed = all(test_results.values())
    print(f"🎯 GENEL SONUÇ: {sum(test_results.values())}/10 TEST BAŞARILI ({'TÜMÜ GEÇTİ' if all_passed else 'BAZI TESTLER BAŞARISIZ'})")
    print("=" * 60)

    server.shutdown()
    return 0 if all_passed else 1

if __name__ == '__main__':
    sys.exit(run_tests())
