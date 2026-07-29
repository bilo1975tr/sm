# LibVLC DLL Sorunu ve Oynatıcı Onarım Planı

Uygulamanın kanal açamamasının temel nedeni olan LibVLC kütüphane eksikliğini gidermek ve oynatıcıyı ayağa kaldırmak için hazırlanan plandır.

## Kullanıcı İncelemesi Gereken Noktalar

> [!IMPORTANT]
> **VLC Player Tespiti:** Bilgisayarınızda `C:\Program Files\VideoLAN\VLC` yolunda kurulu bir VLC Player olduğunu tespit ettim. Uygulamayı, eksik DLL'leri bu klasörden otomatik olarak alacak şekilde güncelleyeceğim. Bu, dosyaları kopyalamaya gerek kalmadan sorunu çözecektir.

## Açık Sorular
- VLC Player'ı 64-bit olarak mı kurdunuz? (Genellikle öyledir, uygulama x64 olduğu için bu kritiktir).

## Önerilen Değişiklikler

### [Oynatıcı Başlatma]

#### [MODIFY] [PlayerView.xaml.cs](file:///C:/Users/bilo75/Downloads/streammesh/UI/Views/PlayerView.xaml.cs)
- `InitializePlayer` metodu güncellenecek.
- Uygulama klasöründe DLL bulamazsa, sırasıyla şu yolları tarayacak:
    1. `C:\Program Files\VideoLAN\VLC` (Standart VLC yolu)
    2. `AppData\Local\Programs\StreamMesh\libvlc\win-x64` (Kurulum yolu)
- DLL yolu bulunduğunda `LibVLCSharp.Shared.Core.Initialize(libPath)` şeklinde açık yol ile başlatılacak.

### [Bakım ve Tanılama]

#### [MODIFY] [MaintenanceEngine.cs](file:///C:/Users/bilo75/Downloads/streammesh/Core/MaintenanceEngine.cs)
- Başlangıçta DLL kontrolü yapan bir fonksiyon eklenecek.
- Eğer DLL bulunamazsa, loglara kullanıcıya yardımcı olacak "VLC yüklü değil veya yolu farklı" gibi spesifik uyarılar yazacak.

## Doğrulama Planı

### Manuel Doğrulama
- Uygulama başlatıldığında loglardaki "Failed to load required native libraries" hatasının gidip gitmediği kontrol edilecek.
- Herhangi bir kanal açıldığında VLC motorunun başarıyla yüklendiği doğrulanacak.
