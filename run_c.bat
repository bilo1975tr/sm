@echo off
title StreamMesh Runner

IF NOT EXIST "csharp_version\StreamMesh\ffmpeg.exe" (
    echo [INFO] FFmpeg bulunamadi, indiriliyor... Lutfen bekleyin...
    powershell -Command "Invoke-WebRequest -Uri 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip' -OutFile 'ffmpeg.zip'"
    echo [INFO] FFmpeg Cikariliyor...
    powershell -Command "Expand-Archive -Path 'ffmpeg.zip' -DestinationPath 'ffmpeg_temp' -Force"
    copy "ffmpeg_temp\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe" "csharp_version\StreamMesh\ffmpeg.exe"
    rmdir /S /Q "ffmpeg_temp"
    del "ffmpeg.zip"
    echo [INFO] FFmpeg basariyla kuruldu!
)

echo [INFO] Uygulama baslatiliyor...
cd csharp_version\StreamMesh
dotnet run
pause
