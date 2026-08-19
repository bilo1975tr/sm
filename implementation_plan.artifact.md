# Sürüm Yönetimi ve Otomatik Güncelleme Senkronizasyonu

Bu plan, `version.txt` dosyasını "tek gerçek kaynak" (Single Source of Truth) haline getirerek, uygulamanın, GitHub yayınlarının ve otomatik güncelleme mekanizmasının birbiriyle tam uyumlu çalışmasını sağlar.

## Kullanıcı İncelemesi Gerekenler

- **VERSION dosyasının kaldırılması:** Artık sadece `version.txt` kullanılacaktır.
- **GitHub Action Güncellemesi:** GitHub üzerinde otomatik artan sürüm numaraları artık `version.txt` dosyasına yazılacak ve Releases sekmesinde bu numaralar görünecek.

## Önerilen Değişiklikler

### Sürüm Dosyaları Standartlaştırma

#### [MODIFY] [version.txt](file:///C:/Users/Administrator/Downloads/streammesh/version.txt)
- Mevcut `0.0.1` değerini, GitHub Releases ile uyumlu olması için `0.1.0` olarak güncelliyoruz.

#### [MODIFY] [StreamMesh.csproj](file:///C:/Users/Administrator/Downloads/streammesh/StreamMesh.csproj)
- `<Version>`, `<AssemblyVersion>` ve `<FileVersion>` değerlerini `0.1.0` olarak güncelliyoruz.

### GitHub İş Akışı (CI/CD)

#### [MODIFY] [build-release.yml](file:///C:/Users/Administrator/Downloads/streammesh/.github/workflows/build-release.yml)
- `VERSION` dosyasını kullanan tüm adımları `version.txt` kullanacak şekilde güncelliyoruz.
- Versiyon formatını `X.Y.Z` (Major.Minor.Build) şeklinde daha standart hale getiriyoruz.

### Uygulama Mantığı (Update Service)

#### [MODIFY] [UpdateService.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Utils/UpdateService.cs)
- `VERSION` dosyasına olan geri dönük (fallback) kontrolü kaldırıyoruz (sadece `version.txt`).
- Sürüm karşılaştırma mantığını güçlendiriyoruz (GitHub Release etiketlerindeki 'v' önekini daha iyi işlemek için).

## Doğrulama Planı

### Otomatik Testler
- `UpdateService.cs` içindeki sürüm karşılaştırma mantığının `0.1.0` ve `v0.1.1` gibi değerleri doğru algıladığını kontrol edeceğiz.

### Manuel Doğrulama
1. Uygulama içindeki "Hakkında" veya sürüm gösteren kısmın `0.1.0` olduğunu teyit edin.
2. GitHub'a push yapıldığında, `version.txt` dosyasının otomatik arttığını ve Release başlığının doğru oluştuğunu gözlemleyin.
