@echo off
title StreamMesh Safe Launcher
echo [DEBUG] StreamMesh Baslatiliyor...
echo.

echo [1] Mevcut islemler temizleniyor...
taskkill /f /im StreamMesh.exe 2>nul

echo.
echo [2] Kutuphaneler kontrol ediliyor (NuGet)...
dotnet restore
if %ERRORLEVEL% NEQ 0 (
    echo [HATA] NuGet geri yukleme basarisiz! Internet baglantinizi kontrol edin.
    pause
    exit /b
)

echo.
echo [3] Uygulama derleniyor...
dotnet build -c Debug
if %ERRORLEVEL% NEQ 0 (
    echo [HATA] Derleme sirasinda hata olustu. Kodlari kontrol edin.
    pause
    exit /b
)

echo.
echo [4] Uygulama calistiriliyor...
cd bin\Debug\net8.0-windows
StreamMesh.exe
if %ERRORLEVEL% NEQ 0 (
    echo [HATA] Uygulama beklenmedik bir sekilde kapandi.
    pause
)
