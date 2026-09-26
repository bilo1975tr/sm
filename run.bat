@echo off
setlocal enabledelayedexpansion
title StreamMesh Hybrid (Rust+WPF) Motoru
:: Calisma dizinini .bat dosyasinin bulundugu asil klasore sabitle
cd /d "%~dp0"
echo =====================================================================
echo           StreamMesh Hybrid (Rust+WPF) Motoru Baslatiliyor...
echo =====================================================================
echo.

:: .NET Yolu Tanimlari
set "DOTNET_LOCAL_PATH=%LocalAppData%\Microsoft\dotnet"
set "DOTNET_GLOBAL_PATH=C:\Program Files\dotnet"

:: Proje Dosyasi Kontrolu
set "PROJECT_FILE=%~dp0StreamMesh.csproj"
if not exist "!PROJECT_FILE!" (
    if exist "StreamMesh.csproj" (
        set "PROJECT_FILE=StreamMesh.csproj"
    ) else (
        echo [HATA] StreamMesh.csproj proje dosyasi bulunamadi!
        echo Calisilan Dizin: %cd%
        pause
        exit /b 1
    )
)
:: 1. Mevcut .NET SDK Kontrolu
where dotnet >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo [TAMAM] .NET SDK sistem PATH'inde bulundu.
    goto :DOTNET_READY
)

if exist "!DOTNET_LOCAL_PATH!\dotnet.exe" (
    echo [TAMAM] .NET SDK yerel dizinde bulundu.
    set "PATH=!DOTNET_LOCAL_PATH!;%PATH%"
    goto :DOTNET_READY
)

if exist "!DOTNET_GLOBAL_PATH!\dotnet.exe" (
    echo [TAMAM] .NET SDK global dizinde bulundu.
    set "PATH=!DOTNET_GLOBAL_PATH!;%PATH%"
    goto :DOTNET_READY
)

:: 2. .NET Bulunamadiysa Otomatik Olarak İndir ve Kur
echo [UYARI] Sisteminizde .NET 8.0 SDK bulunamadi.
echo [OTOMATIK KURULUM] .NET 8.0 SDK Microsoft resmi sunucularindan indiriliyor...
echo Lutfen bekleyin, bu islem ilk seferde internet baglantiniza bagli olarak 1-2 dakika surebilir.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "& { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; $installer = Join-Path $env:TEMP 'dotnet-install.ps1'; Write-Host '[1/2] Microsoft yukleme araci indiriliyor...'; (New-Object System.Net.WebClient).DownloadFile('https://dot.net/v1/dotnet-install.ps1', $installer); Write-Host '[2/2] .NET 8.0 SDK kuruluyor (Yerel Dizin: %LocalAppData%\Microsoft\dotnet)...'; & $installer -Channel 8.0 -InstallDir '%LocalAppData%\Microsoft\dotnet' -Architecture x64; Remove-Item $installer -Force -ErrorAction SilentlyContinue }"

if exist "!DOTNET_LOCAL_PATH!\dotnet.exe" (
    echo.
    echo [BASARILI] .NET 8.0 SDK basariyla kuruldu!
    set "PATH=!DOTNET_LOCAL_PATH!;%PATH%"
    setx PATH "!DOTNET_LOCAL_PATH!;%PATH%" >nul 2>&1
) else (
    echo.
    echo [HATA] .NET 8.0 SDK otomatik kurulamadi.
    echo Lutfen .NET 8.0 SDK x64 surumunu manuel olarak indirip kurun:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

:DOTNET_READY
echo.
:: 3. Bagimliliklari kontrol et ve yukle
echo [DEPENDENCY] Kutuphaneler kontrol ediliyor ve yukleniyor...
dotnet restore "!PROJECT_FILE!"
if %ERRORLEVEL% NEQ 0 (
    echo [UYARI] Paket yuklemede sorun olustu, yeniden deneniyor...
    dotnet restore --no-cache "!PROJECT_FILE!"
)

:: 4. Uygulamayi derle ve calistir
echo.
echo [BUILD] Uygulama derleniyor ve calistiriliyor...
dotnet run --project "!PROJECT_FILE!"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [HATA] Uygulama baslatilamadi. Lutfen yukaridaki hata mesajlarini kontrol edin.
    pause
)
