@echo off
setlocal
title StreamMesh Runner

:: Scriptin bulundugu klasore gecis yap (Klasor isminde bosluk vs varsa korumak icin)
cd /d "%~dp0"

echo [INFO] Sistem gereksinimleri kontrol ediliyor...

:: Gecici PATH guncellemesi (Local AppData altindaki olasi yuklemeyi kontrol etmek icin)
set "PATH=%PATH%;%LocalAppData%\Microsoft\dotnet"

where dotnet >nul 2>nul
if %ERRORLEVEL% equ 0 goto :DOTNET_OK

echo [WARNING] .NET SDK sisteminizde yuklu degil!
echo [INFO] .NET 8.0 SDK otomatik olarak indiriliyor, lutfen bekleyin...

:: curl ile indirmeyi dene, basarisiz olursa powershell ile dene
curl -L -o "%temp%\dotnet-install.ps1" https://dot.net/v1/dotnet-install.ps1 >nul 2>nul
if exist "%temp%\dotnet-install.ps1" goto :INSTALL_DOTNET

powershell -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; (New-Object System.Net.WebClient).DownloadFile('https://dot.net/v1/dotnet-install.ps1', '%temp%\dotnet-install.ps1')" >nul 2>nul
if exist "%temp%\dotnet-install.ps1" goto :INSTALL_DOTNET

goto :TRY_WINGET

:INSTALL_DOTNET
echo [INFO] .NET 8.0 SDK yukleniyor (Bu islem birkac dakika surebilir, lutfen pencereyi kapatmayin)...
powershell -ExecutionPolicy Bypass -File "%temp%\dotnet-install.ps1" -Channel 8.0 -Architecture x64
del "%temp%\dotnet-install.ps1"
set "PATH=%PATH%;%LocalAppData%\Microsoft\dotnet"
goto :DOTNET_OK

:TRY_WINGET
echo [WARNING] Yukleme betigi indirilemedi. winget ile kurulmaya calisiliyor...
where winget >nul 2>nul
if %ERRORLEVEL% neq 0 goto :INSTALL_FAILED

winget install Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements
set "PATH=%PATH%;C:\Program Files\dotnet\"
goto :DOTNET_OK

:INSTALL_FAILED
echo [ERROR] .NET SDK otomatik olarak kurulamadi. Lutfen .NET 8.0 SDK'yi manuel olarak indirin:
echo https://dotnet.microsoft.com/en-us/download/dotnet/8.0
echo Kurulumdan sonra bu pencereyi kapatip tekrar run_c.bat dosyasini calistirin.
pause
exit /b 1

:DOTNET_OK
echo [INFO] .NET SDK algilandi.
echo [INFO] Gerekli kutuphaneler kontrol ediliyor ve indiriliyor...
cd /d "%~dp0csharp_version\StreamMesh"
dotnet restore

if %ERRORLEVEL% neq 0 (
    echo [ERROR] Kutuphaneler indirilemedi. 'dotnet' komutu algilanamadiysa, lutfen bu pencereyi kapatip tekrar run_c.bat dosyasini calistirin.
    pause
    exit /b 1
)

echo [INFO] Uygulama derleniyor ve baslatiliyor...
dotnet run
pause
