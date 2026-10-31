# Gelişmiş EPG Yönetimi, Canlı Durum Kartları, Saat Ofseti ve Ölü Adres Temizleme Planı

Kullanıcının ilettiği ekran görüntüsü ve talepleri doğrultusunda; Ayarlar > EPG Rehber sekmesindeki ilkel listeyi zenginleştiren, her EPG kaynağının gerçek indirilme durumunu, kanal ve program sayısını gösteren, kaynak bazında özelleştirilebilir Saat Ofseti (+3, +2, +1, 0, -1 saat vb.) sağlayan, `.xml.gz` ve ham `.xml` formatlarını otomatik fallback ile destekleyen ve ölü/cevap vermeyen bağlantıları (AceStream ve YouTube hariç) temizleyen kapsamlı geliştirme planıdır.

---

## 1. Tespit Edilen Eksikler ve İhtiyaç Analizi

1. **İlkel Arayüz ve Bilgi Eksikliği (Görsel Analizi)**:
   - Mevcut ekranda sadece düz URL metni ve kırmızı "Kaldır" butonu yer almaktadır.
   - Kullanıcı bu kaynağın başarılı inip inmediğini, kaç kanal ve yayın akışı içerdiğini, son güncelleme vaktini görememektedir.
   - **Çözüm**: IPTV Hesapları sekmesindeki modern kart yapısına benzer, yeşil/kırmızı durum rozeti, kanal sayısı, program sayısı, son güncelleme tarihi ve ofset bilgisini barındıran zengin kart tasarımı uygulanacaktır.

2. **GZip (`.xml.gz`) ve Ham XML (`.xml`) Çift Yönlü Fallback**:
   - Kullanıcı URL olarak `.xml` girse bile sistem önce otomatik olarak hızlı `.gz` versiyonunu deneyecek; sunucu 404 verirse veya sıkıştırılmamışsa doğrudan ham `.xml` verisini okuyacaktır.
   - Tersine `.xml.gz` girilip sunucuda gzip yoksa doğrudan ham akış dekompresyonsuz çözülecektir. Magic byte (`0x1F 0x8B`) kontrolü akışın her anında şeffaf çalışacaktır.

3. **Özelleştirilebilir Saat Dilimi Ofseti (Time Shift / Offset)**:
   - Bazı XMLTV kaynakları hatalı şekilde UTC (`+0000`) vermekte veya yerel yayın saatini yanlış ofsetle iletmektedir.
   - Her EPG kaynağına ait bağımsız bir `TimeShiftHours` (-12 ile +12 saat arası) seçeneği eklenecek; kullanıcı örneğin bir kaynak için "+3 Saat (TR Düzeltmesi)" veya "-1 Saat" seçebilecek, programlar hesaplanırken bu ofset eklenerek saatler tam yerine oturacaktır.

4. **Ölü ve Yanıt Vermeyen Adreslerin Temizlenmesi**:
   - Ping ve HTTP HEAD/GET bağlantı testlerine cevap vermeyen, zaman aşımına uğrayan (Timeout), 404 veya 500 veren ölü EPG kaynakları ve çalışmayan yayın adresleri sistemden otomatik olarak temizlenecektir.
   - **Kritik İstisna**: AceStream (P2P hash/engine) ve YouTube (harici çözümleyici) bağlantıları asla ölü sayılarak silinmeyecektir.

---

## 2. Kullanıcı Deneyimi ve Arayüz Tasarımı (Settings > EPG Rehber)

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ [EPG / XMLTV Kaynağı Ekle]                                                             │
│ ┌────────────────────────────────────────────────────────────┬──────────────┬────────┐ │
│ │ https://iptv-epg.org/files/epg-tr.xml                      │ Ofset: +0 Sa │ [Ekle] │ │
│ └────────────────────────────────────────────────────────────┴──────────────┴────────┘ │
│                                                                                        │
│ Aktif EPG Kaynakları (4 Kaynak):                                                       │
│ ┌────────────────────────────────────────────────────────────────────────────────────┐ │
│ │ 🌐 https://iptv-epg.org/files/epg-tr.xml.gz           [✅ Başarılı]  [Ofset: +3 Sa] │ │
│ │ 📺 184 Kanal  │  📅 14.820 Program  │  🕒 Bugün 15:30  │  ⚡ GZip Sıkıştırılmış       │ │
│ │ [🔄 Şimdi Güncelle]   [🕒 Saat Ofseti Değiştir]   [🗑️ Kaldır]                      │ │
│ └────────────────────────────────────────────────────────────────────────────────────┘ │
│ ┌────────────────────────────────────────────────────────────────────────────────────┐ │
│ │ 🌐 http://168.205.87.198:8555/xmltv.php?username=...  [❌ Ulaşılamadı (Timeout)]   │ │
│ │ ⚠️ Sunucu yanıt vermedi (HTTP 504 / Zaman Aşımı)                                   │ │
│ │ [🗑️ Ölü Kaynağı Temizle]   [🔄 Tekrar Dene]                                        │ │
│ └────────────────────────────────────────────────────────────────────────────────────┘ │
│                                                                                        │
│ [⚡ Tüm EPG Kaynaklarını Güncelle]   [🧹 Yanıt Vermeyen Ölü Kaynakları Temizle]       │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Teknik Mimari ve Veritabanı Değişiklikleri

### A. SQLite Tablo Şeması Güncellemesi (`DatabaseEngine.cs` & `EpgRepository.cs`)
`EpgSources` tablosuna aşağıdaki sütunlar eklenecek / migrate edilecektir:
- `Status TEXT DEFAULT 'Bekliyor'` (Başarılı, Hata, İndiriliyor, Bekliyor)
- `ChannelCount INTEGER DEFAULT 0` (Bu kaynaktan çekilen kanal sayısı)
- `ProgramCount INTEGER DEFAULT 0` (Bu kaynaktan çekilen yayın programı sayısı)
- `LastUpdated INTEGER DEFAULT 0` (Son başarılı güncelleme Unix zamanı)
- `TimeShiftHours INTEGER DEFAULT 0` (Kullanıcının belirlediği saat ofseti, örn: +3, -1)
- `ErrorMessage TEXT DEFAULT ''` (Hata oluştuğunda gösterilecek açıklama)

### B. Akıllı İndirici & Fallback Motoru (`EpgEngine.cs`)
1. URL Analizi:
   - Eğer URL `.xml` ile bitiyorsa, önce arka planda hızlıca `.xml.gz` adresi denenir (HEAD/GET). Varsa çok hızlı indirilir.
   - Eğer `.xml.gz` bulunamazsa orijinal `.xml` indirilir.
2. Magic Byte & Dekompresyon:
   - Gelen dosyanın ilk 2 byte'ı `0x1F 0x8B` ise doğrudan `GZipStream` ile açılır; değilse ham `FileStream` olarak okunur.
3. Saat Ofseti Entegrasyonu:
   - `TryParseXmlTime(time, sourceUrl, timeShiftHours, out DateTime result)`
   - Hesaplanan tarihe `result = result.AddHours(timeShiftHours);` uygulanır. Böylece UTC veya hatalı ofsetli yayınlar kullanıcının seçtiği saat dilimine birebir oturtulur.
4. Kaynak İstatistiklerinin Kaydı:
   - Kaynak işlenip bittiğinde `_db.UpdateEpgSourceStats(url, "Başarılı", channelCount, programCount, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "")` çağrılarak veritabanına işlenir. Hata alırsa `"Hata: " + ex.Message` kaydedilir.

### C. Ölü Adres Temizleyici Motor (`StreamValidator.cs` & `MaintenanceEngine.cs`)
- **EPG Kaynakları İçin**: Kaynak adresine 5 saniyelik hafif `HEAD / GET` isteği atılır. Eğer DNS çözülemiyorsa, soket reddediliyorsa veya 404/500/Timeout veriyorsa ölü olarak işaretlenir ve kullanıcı onaylı veya tek tık temizleme ile listeden silinir.
- **Kanal Akış Adresleri İçin**:
  - `acestream://`, `127.0.0.1:6878`, 40 haneli hash ve `youtube.com`/`youtu.be` adresleri KESİNLİKLE muaf tutulur.
  - Normal IPTV/M3U HTTP akışları taranırken yanıt vermeyen linkler temizlenir.

---

## 4. Uygulama Adımları

1. **Model ve Veritabanı Katmanı**:
   - `Models/EpgSourceItem.cs` modeli oluşturulacak.
   - `EpgRepository.cs` içine kaynak istatistiklerini güncelleme (`UpdateEpgSourceStats`), saat ofsetini kaydetme (`SetEpgSourceTimeShift`) ve ölü kaynakları temizleme metodları eklenecek.
2. **EpgEngine Geliştirmesi**:
   - Çift yönlü GZip/Ham XML fallback sistemi ve `TimeShiftHours` saat kaydırma desteği kodlanacak.
3. **SettingsView Arayüzü (XAML & C#)**:
   - `SettingsView.xaml` dosyasındaki `EpgSourcesList` tasarımı IPTV hesapları kart stiline dönüştürülecek.
   - Her karta durum rozeti, kanal/program sayıları, son çekilme zamanı, Saat Ofseti seçim ComboBox'ı (+3, +2, +1, 0, -1, vb.) ve tekil yenileme/kaldırma butonları eklenecek.
   - "🧹 Yanıt Vermeyen Ölü Kaynakları Temizle" butonu eklenecek.
4. **Web Arayüzü Entegrasyonu**:
   - `server.js` ve `MediaServer.cs` üzerinden EPG kaynaklarının durum ve saat ofsetlerinin web API'ye yansıtılması sağlanacak.

---

## 5. Doğrulama ve Test Kriterleri

- **Kart Bilgileri Testi**: Settings > EPG sekmesine girildiğinde her EPG kaynağının altında yeşil "Başarılı", kanal sayısı (örn: 184 Kanal), program sayısı ve tarihi doğru görüntülenecek.
- **Ofset Testi**: Bir kaynağa "+3 Saat" ofset verildiğinde, o kaynağın kanallarındaki program saatlerinin tam 3 saat ileri kaydığı doğrulanacak.
- **Fallback Testi**: Hem `.xml` hem de `.xml.gz` uzantılı adresler hatasız indirilip işlenecek.
- **Ölü Link Testi**: Ekran görüntüsündeki çalışmayan `168.205...` veya `live.syri.tv` linkleri test edilip "Ulaşılamadı" olarak işaretlenecek ve AceStream/YouTube linklerine dokunulmadan temizlenebilecek.
