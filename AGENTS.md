# STREAMMESH GELİŞTİRME YÖNERGESİ (OPTİMİZE EDİLMİŞ)

## 1. TEMEL DAVRANIŞ

* Her zaman Türkçe cevap ver.
* Cevaplar kısa, net ve teknik olsun.
* Gereksiz açıklama yapma.
* İstenen işi yap, gereksiz rapor üretme.

---

## 2. SİSTEMİ KORU

En önemli kural budur.

* Çalışan sistemi bozma.
* Çalışan özelliği kaldırma.
* Davranışı değiştirme (özellikle istenmedikçe).
* Sadece gerekli olan kısmı düzelt.
* Minimum müdahale ile çalış.

---

## 3. GÜVENLİ GELİŞTİRME

Kod üzerinde çalışırken:

* Hata varsa düzelt.
* Performans sorunu varsa optimize et.
* Güvenlik açığı varsa kapat.
* Gereksiz karmaşıklığı azalt.
* Modern ama stabil yöntem kullan.

Bunları yaparken mevcut davranışı koru.

---

## 4. ANALİZ KURALI

Uzun analiz hazırlama.

Yalnızca şu durumlarda analiz yap:

* Gerçek kırılma riski varsa
* Birden fazla çözüm arasında seçim gerekiyorsa
* Kullanıcı özellikle analiz isterse

Diğer durumlarda analiz yazmadan doğrudan uygula.

---

## 5. RAPOR KURALI

Her işlem sonunda yalnızca aşağıdaki kısa raporu ver:

* Değiştirilen dosyalar
* Yapılan değişiklik
* Derleme durumu
* Hata varsa hata

Uzun rapor, TDD, alternatif çözüm, performans makalesi veya tekrar eden açıklamalar yazma.

---

## 6. TEKRAR ANALİZ ETME

Bir dosya veya metot analiz edildiyse aynı analizi tekrar üretme.

Önceki sonucu kullan.

Aynı dosya için tekrar 20 sayfalık analiz hazırlama.

---

## 7. DEĞİŞİKLİK ŞEKLİ

Her seferinde yalnızca tek işi yap.

İş bittikten sonra dur.

Yeni işe geçmeden önce kullanıcı onayı bekle.

---

## 8. REFACTOR KURALI

Kullanıcı istemedikçe:

* Büyük refactor yapma.
* Dosya taşıma.
* İsim değiştirme.
* Namespace değiştirme.
* MVVM dönüşümü yapma.
* Mimari değiştirme.

Sadece gerekli satırı düzelt.

---

## 9. VERİTABANI

* Yavaş sorguları optimize et.
* Gereksiz sorguları kaldır.
* Index gerekiyorsa öner.
* Şemayı kullanıcı onayı olmadan değiştirme.

---

## 10. PERFORMANS

Performans problemi varsa:

* Önce en düşük riskli çözümü uygula.
* Gereksiz thread oluşturma.
* Gereksiz bellek kullanma.
* UI kilitlenmesine izin verme.

---

## 11. DOĞRULAMA

Derleme yapabiliyorsan yap.

Yapamıyorsan sadece şunu yaz:

"Derleme doğrulanamadı. Yerel Visual Studio/MSBuild ortamında test edilmelidir."

Hiçbir zaman doğrulanmayan bir şeyi doğrulandı gibi gösterme.

---

## 12. VERSİYON

Gerçekten kod değiştiyse `/VERSION` dosyasını güncelle.

Kod değişmediyse VERSION dosyasına dokunma.

---

## 13. ÇALIŞMA AKIŞI

Her görevde şu sırayı uygula:

1. Dosyayı oku.
2. En fazla 5 satırlık teknik özet ver.
3. Kritik risk varsa belirt.
4. Değişikliği uygula.
5. Derlemeyi doğrula (veya doğrulanamadığını belirt).
6. Kısa rapor ver.
7. Dur ve kullanıcı onayı bekle.

---

## 14. SON KURAL

Amaç rapor yazmak değildir.

Amaç çalışan projeyi daha kaliteli hale getirmektir.

Gereksiz analiz, gereksiz açıklama, gereksiz rapor ve tekrar eden teknik incelemeler üretme.

Odak noktan kodu geliştirmek olsun.
