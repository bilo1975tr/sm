# Arama ve Toplu Kütüphaneye Ekleme İyileştirmeleri

## Yapılan İyileştirmeler

1. **Katı Köşeli Etiket Araması (`[DE]`, `[TR]`, vb.):**
   - `ChannelUtils.MatchesQueryFilter` ve `MatchesSingleTerm` fonksiyonu doğrudan netleştirildi:
     - Kullanıcı `[DE]` gibi 4 karakterlik köşeli parantezli bir arama yaptığında, sistem **sadece ve sadece adında, grubunda veya kategorisinde birebir `[DE]` (veya `[de]`) karakterleri geçen kanalları** kabul eder.
     - İspanyolca/Fransızca bağlaç olan `" de "` (`"M. Liga de Campeones"` vb.) veya kelime içi `"de"` geçişleri kesin olarak elenir.
     - Aynı şekilde `[TR]`, `[FR]`, `[4K]` gibi aramalar da birebir köşeli etiket kontrolüyle çalışır. Loose bağlaç eşleşmeleri ve heuristic dil atlamaları kaldırıldı.

2. **Eksik Sonuçlar ve 50 Kanal Limiti / Sayfalama:**
   - `AceEngine.cs` içerisindeki `page_size=50` kısıtı `200`'e yükseltildi.
   - Hem yerel AceEngine API'si hem de harici web dizinleri (`api.acestream.me`, `search.acestream.net`) için çoklu sayfa döngüsü (sayfalama) eklendi. 50'de kesilmek yerine mevcut tüm sayfalar taranarak 129+ kanalın tamamı getirilir.
   - Diğer katalog arama sınırları 500'e çıkarıldı.

3. **Toplu Olarak Kütüphaneye Ekleme Butonu:**
   - `SearchAceStreamView.xaml` arayüzüne sonuç sayısının yanına `➕ Tümünü Kütüphaneye Ekle` butonu eklendi.
   - Arama tamamlandığında kütüphanede zaten ekli olan kanallar otomatik `✔ Eklendi` olarak işaretlenir.
   - Kullanıcı `➕ Tümünü Kütüphaneye Ekle` butonuna bastığında listedeki tüm yeni kanallar tek bir işlemle (`SaveChannelsBatchAsync`) SQLite veritabanına eklenir ve buton `✔ Tümü Eklendi` durumuna geçer.

4. **Derleme Hatası Giderildi (CS0117):**
   - `SearchAceStreamView.xaml.cs` dosyasında `Channel` modelinde bulunmayan `AddedDate` ataması kaldırıldı; veritabanı kayıt tarihi SQLite katmanında (`ChannelRepository`) otomatik yönetilmektedir. Proje hatasız derlenmeye hazır.
