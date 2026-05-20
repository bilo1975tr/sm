@echo off
title StreamMesh EXE Builder
echo [BUILD] Uygulama x64 Single-File olarak derleniyor...

cd csharp_version\StreamMesh

:: Temizle
dotnet clean -c Release

:: Publish komutu (SingleFile, x64, ReadyToRun)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishReadyToRun=true /p:IncludeNativeLibrariesForSelfExtract=true

echo.
echo [OK] Islem tamamlandi!
echo [YOL] csharp_version\StreamMesh\bin\Release\net8.0-windows\win-x64\publish\
echo.
pause
