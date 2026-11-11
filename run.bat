@echo off
setlocal
chcp 65001 >nul 2>&1
title StreamMesh Hybrid

cd /d "%~dp0"

echo =====================================================================
echo           StreamMesh Hybrid Motoru Baslatiliyor...
echo =====================================================================
echo.

REM Internet Uzerinden Indirilen Dosyalarin Engellerini Kaldir
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File" >nul 2>&1

REM Yonetici Yetkisi ve Guvenlik Duvari Kontrolu
net session >nul 2>&1
if %errorlevel% == 0 (
    echo [GUVENLIK] Yonetici modu: Guvenlik Duvari ve Defender istisnalari tanimlaniyor...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-MpPreference -ExclusionPath '%~dp0' -ErrorAction SilentlyContinue; Add-MpPreference -ExclusionProcess 'StreamMesh.exe' -ErrorAction SilentlyContinue" >nul 2>&1
    netsh advfirewall firewall add rule name="StreamMesh Uygulamasi" dir=in action=allow program="%~dp0bin\Release\net8.0-windows\StreamMesh.exe" enable=yes profile=any >nul 2>&1
    netsh advfirewall firewall add rule name="StreamMesh Ag Portlari" dir=in action=allow protocol=TCP localport=8088,6878,8621,48931-48950 enable=yes profile=any >nul 2>&1
    echo [GUVENLIK] Guvenlik duvari ve antivirus izinleri basariyla onaylandi.
    echo.
) else (
    echo [BILGI] Standart kullanici modunda baslatiliyor.
    echo.
)

set "PATH=%LocalAppData%\Microsoft\dotnet;%ProgramFiles%\dotnet;%PATH%"
set "DOTNET_ROOT=%LocalAppData%\Microsoft\dotnet"

set "SDK_VER="
for /f "tokens=1" %%V in ('dotnet --version 2^>nul') do set "SDK_VER=%%V"

echo %SDK_VER% | findstr /r "^[0-9]" >nul 2>&1
if %errorlevel% neq 0 goto :AUTO_INSTALL
if not defined SDK_VER goto :AUTO_INSTALL

goto :DOTNET_OK

:AUTO_INSTALL
echo =====================================================================
echo [BILGI] Sisteminizde .NET 8 SDK (Derleyici) bulunamadi.
echo         Kaynak kodlarin derlenebilmesi icin .NET 8 SDK kuruluyor...
echo         (Lutfen bekleyin, bu islem yalnizca ilk seferde 1-2 dk surer)
echo =====================================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; $s = Join-Path $env:TEMP 'dotnet-install.ps1'; Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $s -UserAgent 'Mozilla/5.0'; & $s -Channel 8.0 -InstallDir '$env:LocalAppData\Microsoft\dotnet' -Architecture x64; Remove-Item $s -Force -ErrorAction SilentlyContinue"

set "PATH=%LocalAppData%\Microsoft\dotnet;%ProgramFiles%\dotnet;%PATH%"
set "DOTNET_ROOT=%LocalAppData%\Microsoft\dotnet"

set "SDK_VER="
for /f "tokens=1" %%V in ('"%LocalAppData%\Microsoft\dotnet\dotnet.exe" --version 2^>nul') do set "SDK_VER=%%V"
if not defined SDK_VER (
    for /f "tokens=1" %%V in ('dotnet --version 2^>nul') do set "SDK_VER=%%V"
)

echo %SDK_VER% | findstr /r "^[0-9]" >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo [UYARI] .NET 8 SDK otomatik indirilemedi.
    echo.
    REM Eger onceden derlenmis hazir EXE varsa dogrudan calistir
    if exist "%~dp0bin\Release\net8.0-windows\win-x64\publish\StreamMesh.exe" (
        echo [BILGI] Hazir derlenmis StreamMesh.exe bulundu, baslatiliyor...
        start "" "%~dp0bin\Release\net8.0-windows\win-x64\publish\StreamMesh.exe"
        exit /b 0
    )
    if exist "%~dp0bin\Release\net8.0-windows\StreamMesh.exe" (
        echo [BILGI] Hazir derlenmis StreamMesh.exe bulundu, baslatiliyor...
        start "" "%~dp0bin\Release\net8.0-windows\StreamMesh.exe"
        exit /b 0
    )
    echo Lutfen Microsoft resmi sitesinden .NET 8 SDK x64 kurun:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

:DOTNET_OK
echo [BILGI] .NET SDK Surumu: %SDK_VER%
echo.

REM Eski StreamMesh orneklerini temizle
taskkill /f /im StreamMesh.exe >nul 2>&1

REM Ikon klasoru kontrolu
if not exist "logos" mkdir logos

echo [DEPENDENCY] Kutuphaneler kontrol ediliyor ve yukleniyor...
dotnet restore StreamMesh.csproj --verbosity quiet
if errorlevel 1 dotnet restore --no-cache StreamMesh.csproj

echo.
echo [BUILD] Uygulama derleniyor...
dotnet build StreamMesh.csproj -c Release --nologo -v quiet
if errorlevel 1 (
    echo [BUILD TEKRAR] Ayrintili hata ciktisi ile derleniyor...
    dotnet build StreamMesh.csproj -c Release --nologo
    if errorlevel 1 goto :BUILD_FAIL
)

echo.
echo [BASLAT] StreamMesh Penceresi Baslatiliyor...
set "EXE_PATH=%~dp0bin\Release\net8.0-windows\StreamMesh.exe"
set "PUBLISH_EXE=%~dp0bin\Release\net8.0-windows\win-x64\publish\StreamMesh.exe"

if exist "%EXE_PATH%" (
    start "" /wait "%EXE_PATH%"
) else if exist "%PUBLISH_EXE%" (
    start "" /wait "%PUBLISH_EXE%"
) else (
    dotnet run --project StreamMesh.csproj -c Release --no-build
)

set "APP_EXIT_CODE=%errorlevel%"
if %APP_EXIT_CODE% neq 0 (
    echo.
    echo [UYARI] StreamMesh cikis kodu: %APP_EXIT_CODE%
    goto :SHOW_LOGS
)

REM Log dosyasinda hata olusmussa veya aninda kapandiysa kontrol et
set "LOG_FILE=%LocalAppData%\StreamMesh\app.log"
set "CRASH_FILE=%LocalAppData%\StreamMesh\crash.log"
set "LOCAL_CRASH=%~dp0crash.log"

if exist "%CRASH_FILE%" goto :SHOW_LOGS
if exist "%LOCAL_CRASH%" goto :SHOW_LOGS

if exist "%LOG_FILE%" (
    findstr /i "Exception Error Hata FATAL" "%LOG_FILE%" >nul 2>&1
    if not errorlevel 1 (
        goto :SHOW_LOGS
    )
)

echo [BASARILI] StreamMesh oturumu sonlandi.
pause
exit /b 0

:SHOW_LOGS
echo.
echo ======================= SON UYGULAMA LOGLARI =======================
if exist "%LocalAppData%\StreamMesh\crash.log" (
    echo --- KRITIK HATA LOGU: crash.log ---
    powershell -NoProfile -Command "Get-Content -Path '%LocalAppData%\StreamMesh\crash.log' -Tail 30"
    echo.
)
if exist "%~dp0crash.log" (
    echo --- YEREL CRASH DOSYASI: crash.log ---
    powershell -NoProfile -Command "Get-Content -Path '%~dp0crash.log' -Tail 30"
    echo.
)
if exist "%LocalAppData%\StreamMesh\app.log" (
    echo --- UYGULAMA LOGU: app.log ---
    powershell -NoProfile -Command "Get-Content -Path '%LocalAppData%\StreamMesh\app.log' -Tail 35"
)
echo ====================================================================
echo.
pause
exit /b 0

:BUILD_FAIL
echo.
echo [HATA] Uygulama calistirilamadi veya derleme hatasi olustu.
echo Lutfen yukaridaki ciktiyi kontrol edin.
pause
exit /b 1
