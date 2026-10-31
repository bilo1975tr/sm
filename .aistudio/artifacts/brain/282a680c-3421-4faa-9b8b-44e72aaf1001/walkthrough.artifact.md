# StreamMesh AceStream Yalın & Kesin `infohash=` Yapılandırması

## Yapılan Güncelleme:
HlsProxyEngine (durdurma, geri sarma, kaldığı yerden devam etme) motoru **%100 muhafaza edilerek**, AceStream kaynak adresleri tahrif edilmeden doğrudan tek ve kesin doğru formata sabitlendi:

```text
http://127.0.0.1:6878/ace/getstream?infohash={40_haneli_hash}
```

### Değişiklik Özeti:
1. **Gereksiz Alternatif Üretimi Kaldırıldı:**
   - `?id=`, `?content_id=` veya parametresiz `?{hash}` gibi adres üretip sırayla deneme döngüsü tamamen silindi.
2. **`HlsProxyEngine` Motoru Birebir Korundu:**
   - Duraklatma, geri sarma ve canlı yayına dönme (DVR/Timeshift) işlevselliği kayıpsız devam etmektedir.
3. **`AceEngine.cs` & `PlayerViewModel.cs` Güncellendi:**
   - AceStream yayınları için üretilen tek ve doğrudan adres `http://127.0.0.1:6878/ace/getstream?infohash={hash}` olarak belirlendi.
