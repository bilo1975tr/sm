@echo off
setlocal enabledelayedexpansion
title StreamMesh - Windows Setup ve EXE Olusturucu

:: Calisma dizinini .bat dosyasinin bulundugu klasore sabitle
cd /d "%~dp0"

echo =======================================================
echo         StreamMesh Windows Setup ve EXE Olusturucu
echo =======================================================
echo.

:: 1. Surum bilgisini oku
set "APP_VERSION=0.1.20"
if exist version.txt (
    for /f "usebackq tokens=*" %%V in ("version.txt") do set "APP_VERSION=%%V"
)
echo [*] Hedef Surum: !APP_VERSION!
echo.

:: 2. Dotnet SDK Kontrolu
set "DOTNET_CMD="
where dotnet >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    set "DOTNET_CMD=dotnet"
    goto DOTNET_OK
)
if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "DOTNET_CMD=%ProgramFiles%\dotnet\dotnet.exe"
    goto DOTNET_OK
)
if exist "%LocalAppData%\Microsoft\dotnet\dotnet.exe" (
    set "DOTNET_CMD=%LocalAppData%\Microsoft\dotnet\dotnet.exe"
    goto DOTNET_OK
)
if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" (
    set "DOTNET_CMD=%ProgramFiles(x86)%\dotnet\dotnet.exe"
    goto DOTNET_OK
)

echo [*] .NET 8 SDK bulunamadi. Indiriliyor...
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; $script = Join-Path $env:TEMP 'dotnet-install.ps1'; Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script -UserAgent 'Mozilla/5.0'; & $script -Channel 8.0 -InstallDir '$env:LocalAppData\Microsoft\dotnet' -Architecture x64; Remove-Item $script -Force -ErrorAction SilentlyContinue"

if exist "%LocalAppData%\Microsoft\dotnet\dotnet.exe" (
    set "DOTNET_CMD=%LocalAppData%\Microsoft\dotnet\dotnet.exe"
    set "PATH=%LocalAppData%\Microsoft\dotnet;%PATH%"
    goto DOTNET_OK
)

echo [HATA] .NET 8 SDK kurulamadi. Lutfen elle yukleyin: https://dotnet.microsoft.com/download/dotnet/8.0
goto END

:DOTNET_OK
echo [*] .NET SDK Aktif: !DOTNET_CMD!
echo.

:: 3. Inno Setup Compiler (ISCC.exe) Kontrolu ve Otomatik Yukleme
set "ISCC_PATH="
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"

if not "!ISCC_PATH!"=="" goto INNO_OK

where iscc >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    set "ISCC_PATH=iscc"
    goto INNO_OK
)

echo [*] Inno Setup (Kurulum Paketi Olusturucu) bulunamadi. Yukleniyor...

:: Yöntem A: Winget ile yükleme
where winget >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    echo [*] Winget ile Inno Setup kuruluyor...
    winget install --id JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements --silent >nul 2>nul
)

if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not "!ISCC_PATH!"=="" goto INNO_OK

:: Yöntem B: Direct Curl / PowerShell indirmesi
echo [*] Inno Setup resmi kurucusu indiriliyor...
if exist "%SystemRoot%\System32\curl.exe" (
    "%SystemRoot%\System32\curl.exe" -sSL "https://files.jrsoftware.org/ispack/innosetup-6.3.3.exe" -o "%TEMP%\innosetup-installer.exe"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; (New-Object System.Net.WebClient).DownloadFile('https://files.jrsoftware.org/ispack/innosetup-6.3.3.exe', '$env:TEMP\innosetup-installer.exe')"
)

if exist "%TEMP%\innosetup-installer.exe" (
    echo [*] Inno Setup sessiz kurulum yapiliyor...
    start /wait "" "%TEMP%\innosetup-installer.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
    del /f /q "%TEMP%\innosetup-installer.exe" >nul 2>nul
)

if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"

:INNO_OK
if not "!ISCC_PATH!"=="" (
    echo [*] Inno Setup Compiler Aktif: !ISCC_PATH!
) else (
    echo [UYARI] Inno Setup otomatik kurulamadi. Yalnizca Standalone EXE uretilecek.
)
echo.

:: 4. Proje Derleme ve Publish (Single-File Standalone EXE)
echo [*] Eski derleme dosyalari temizleniyor...
"!DOTNET_CMD!" clean StreamMesh.csproj -c Release

echo.
echo [*] StreamMesh Release paketi derleniyor (win-x64)...
"!DOTNET_CMD!" publish StreamMesh.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishReadyToRun=true /p:IncludeNativeLibrariesForSelfExtract=true
if %ERRORLEVEL% NEQ 0 goto BUILD_FAILED

if not exist "bin\Release\net8.0-windows\win-x64\publish\StreamMesh.exe" goto EXE_NOT_FOUND

echo [OK] Standalone Single-File EXE olusturuldu:
echo      bin\Release\net8.0-windows\win-x64\publish\StreamMesh.exe
goto BUILD_OK

:BUILD_FAILED
echo [HATA] Derleme basarisiz oldu. Lutfen koddaki hatalari inceleyin.
goto END

:EXE_NOT_FOUND
echo [HATA] StreamMesh.exe cikti klasorunde bulunamadi.
goto END

:BUILD_OK

:: 5. Inno Setup ile Windows Kurulum Paketi (StreamMesh-Setup-vX.X.X.exe) Olusturma
if not "!ISCC_PATH!"=="" (
    echo.
    echo [*] StreamMesh-Setup-v!APP_VERSION!.exe olusturuluyor...
    "!ISCC_PATH!" /DAppVersion="!APP_VERSION!" setup.iss
    if %ERRORLEVEL% EQU 0 (
        echo [OK] KURULUM PAKETI BASARIYLA OLUSTURULDU: StreamMesh-Setup-v!APP_VERSION!.exe
    ) else (
        echo [UYARI] Inno Setup derleme hatasi verdi.
    )
)

:: 6. Portable ZIP Arşivi
echo.
echo [*] Portatif ZIP paketi olusturuluyor...
copy /Y version.txt "bin\Release\net8.0-windows\win-x64\publish\" >nul 2>nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Compress-Archive -Path 'bin\Release\net8.0-windows\win-x64\publish\*' -DestinationPath 'StreamMesh-v!APP_VERSION!-Portable.zip' -Force" >nul 2>nul

echo.
echo =======================================================
echo             ISLEM BASARIYLA TAMAMLANDI
echo =======================================================
echo.
echo Uretilen Dosyalar:
if exist "StreamMesh-Setup-v!APP_VERSION!.exe" (
    echo 1) WINDOWS KURULUM EXE (INSTALLER):
    echo    StreamMesh-Setup-v!APP_VERSION!.exe
    echo.
)
echo 2) Calistirilabilir Tek EXE (Standalone):
echo    bin\Release\net8.0-windows\win-x64\publish\StreamMesh.exe
echo.
if exist "StreamMesh-v!APP_VERSION!-Portable.zip" (
    echo 3) Portatif ZIP Paketi:
    echo    StreamMesh-v!APP_VERSION!-Portable.zip
    echo.
)

:END
echo.
pause
