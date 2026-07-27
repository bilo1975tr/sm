@echo off
setlocal enabledelayedexpansion
echo StreamMesh Hybrid (Rust+WPF) Motoru Baslatiliyor...
echo.

:: .NET Yolu Tanimlari
set "DOTNET_LOCAL_PATH=%LocalAppData%\Microsoft\dotnet"
set "DOTNET_GLOBAL_PATH=C:\Program Files\dotnet"

:: 1. .NET SDK Kontrolu
if exist "!DOTNET_LOCAL_PATH!\dotnet.exe" (
    echo [TAMAM] .NET SDK yerel dizinde bulundu.
    set "PATH=!DOTNET_LOCAL_PATH!;%PATH%"
) else if exist "!DOTNET_GLOBAL_PATH!\dotnet.exe" (
    echo [BILGI] .NET SDK global dizinde bulundu.
    set "PATH=!DOTNET_GLOBAL_PATH!;%PATH%"
)

:: 2. Bagimliliklari kontrol et ve yukle
echo [DEPENDENCY] Kutuphaneler kontrol ediliyor...
dotnet restore

:: 3. Uygulamayi derle ve calistir
echo [BUILD] Uygulama derleniyor...
dotnet run --project StreamMesh.csproj

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [HATA] Uygulama baslatilamadi. Lutfen hata mesajlarini kontrol edin.
    pause
)
