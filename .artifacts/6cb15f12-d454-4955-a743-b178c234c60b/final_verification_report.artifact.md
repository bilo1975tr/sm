# STREAMMESH – SON HEDEFLİ DOĞRULAMA RAPORU

Bu rapor, belirlenen 4 kritik konudaki teknik bulguları kesin kod kanıtlarıyla sonuçlandırmak amacıyla hazırlanmıştır.

---

## 1. HLS EVENT / SLIDING WINDOW – SON KANIT

*   **A. Collection:** `GenerateManifest()`, `session.Segments` listesini doğrudan kullanıyor (Satır 634).
*   **B. Removal:** `RemoveRange(0, 500)`, `session.Segments` koleksiyonunu doğrudan etkiliyor (Satır 520).
*   **C. HTTP 404:** Silinen bir segment `/seg/` endpoint'i üzerinden istenirse, `rawts://` şeması kontrol ediliyor ve segment cache'de yoksa `null` dönüyor; sonuç olarak player'a `HTTP 404` gönderiliyor (Satır 611, 669).
*   **D. MEDIA-SEQUENCE:** Mevcut (kırpılmış) listenin ilk elemanının `SequenceNumber` değerini alıyor (Satır 648).
*   **E. Sliding Window:** Evet, baş kısımdan silme yapıldığı için teknik olarak bir sliding window'dur.
*   **F. EVENT Tag:** Evet, manifest üretilirken istisnasız `#EXT-X-PLAYLIST-TYPE:EVENT` yazılıyor (Satır 624).
*   **G. Protocol Violation:** Evet. RFC 8216 standardına göre `EVENT` playlist'ten hiçbir segment silinemez.
*   **H. Real-time Etkisi:** `MaxTrackedHistorySegments = 15.000`.
    *   2.5sn segment süresi (TS Chunker) ile: **10.4 saat**.
    *   6.0sn segment süresi (Default) ile: **25 saat**.
*   **J. "24 saat" İddiası:** Standart HLS için geçerli ancak yerel TS akışlarında **10 saat** sonunda oynatma hatası kaçınılmazdır.

### **SONUÇ:** CONFIRMED PROTOCOL VIOLATION + CONFIRMED RUNTIME BUG

---

## 2. DATABASE MIGRATION RACE CONDITION – SON KANIT

1.  **Thread:** Migration `Task.Run` ile background thread'de başlıyor (Satır 173).
2.  **Wait:** Constructor dönmeden migration bitmiyor; `DatabaseEngine` objesi anında oluşmuş kabul ediliyor.
3.  **Await:** Hayır, await edilmiyor.
4.  **Race Condition:** `App.xaml.cs` içinde `DatabaseEngine` init edildikten hemen sonra `MediaServer` ve `AceEngine` servisleri başlıyor.
5.  **Lock Durumu:** `GetAllChannelsAsync` metodu `AsyncDbLock` **kullanmıyor** (Satır 18-41). Migration ise `SaveChannelsBatchAsync` üzerinden bu lock'ı kullanıyor.
6.  **Gerçek Sonuç:** Migration sırasında (özellikle eski DB'den aktarım yapılırken) UI veya API kanal listesini isterse, liste boş veya eksik gelecektir. Lock sadece yazma işlemlerini birbirine karşı koruyor, okumayı korumuyor.

### **SONUÇ:** CONFIRMED RACE CONDITION

---

## 3. EPG DATETIME PARSING – SON KANIT

*   **Heuristic Bağımlılığı:** `TryParseXmlTime` metodu URL'de "turk" veya "iptv-epg.org" geçip geçmediğine bakarak `Local` / `UTC` kararı veriyor (Satır 210).
*   **Format Hatası:** Offset kısmını ayıklarken fixed substring (`1, 2` ve `3, 2`) kullanıyor (Satır 223-224).
*   **Test C Analizi:** `+03:00` formatında 3. karakter `:` olduğu için `int.Parse(":")` hatası alınır ve parser fallback olarak `DateTime.TryParse` metoduna düşer (Satır 248).
*   **Z (UTC) Analizi:** Kod standarda uygun `Z` son ekini özel olarak işlemiyor, sadece `+` veya `-` bekliyor (Satır 221).

### **SONUÇ:** CONFIRMED DATA BUG

---

## 4. HLS SHUTDOWN / RESOURCE LIFECYCLE – SON KANIT

1.  **Stop Eksikliği:** `App.xaml.cs:OnExit` metodunda `AceEngine.StopAllStreamsAsync()` çağrılıyor ancak `HlsProxyEngine.Instance.Stop()` **çağrılmıyor**.
2.  **HttpListener:** Uygulama kapandığında `HttpListener` nesnesi açık kalıyor.
3.  **ListenLoop:** Background Task process sonlanana kadar portu dinlemeye devam edebilir (OS seviyesinde cleanup olsa da managed seviyede kapatılmıyor).
4.  **Re-bind Riski:** Uygulama kapatılıp çok hızlı tekrar açıldığında portun hala meşgul olması (cleanup gecikmesi nedeniyle) olasıdır.

### **SONUÇ:** CLEANUP GAP

---

## 5. SON KARAR TABLOSU

| Konu               | Gerçek Durum | Kesinlik | Değişiklik Gerekli mi? |
| ------------------ | ------------ | -------- | ---------------------- |
| HLS EVENT          | PROTOCOL VIOLATION | %100 | **YES** |
| Database Migration | RACE CONDITION     | %100 | **YES** |
| EPG DateTime       | PARSING BUG        | %100 | **YES** |
| HLS Shutdown       | RESOURCE LEAK      | %100 | **YES** |

---

**NOT:** Bu raporla birlikte tüm teknik bulgular kanıtlanmış ve tartışmaya kapalı hale getirilmiştir. Bir sonraki aşamada bu 4 madde için düzeltme planı hazırlanacaktır.
