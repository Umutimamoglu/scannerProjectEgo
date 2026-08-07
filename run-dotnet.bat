@echo off
REM ============================================================
REM  Kimlik Okuma ve Kart Baski Sistemi - .NET surumu
REM
REM  Kullanim:
REM    run-dotnet.bat          derler ve calistirir
REM    run-dotnet.bat test     testleri calistirir
REM    run-dotnet.bat build    sadece derler
REM
REM  Java surumu icin: run.bat
REM ============================================================

setlocal
set DOTNET=C:\Program Files\dotnet\dotnet.exe
set SLN=%~dp0dotnet\IdScanner.slnx
set APP=%~dp0dotnet\src\IdScanner.App\bin\Debug\net10.0-windows\IdScanner.App.exe

if not exist "%DOTNET%" (
    echo HATA: .NET SDK bulunamadi: %DOTNET%
    echo winget install --id Microsoft.DotNet.SDK.10
    exit /b 1
)

if "%1"=="test" (
    "%DOTNET%" test "%SLN%" --nologo
    exit /b %ERRORLEVEL%
)

echo Derleniyor...
"%DOTNET%" build "%SLN%" -v q --nologo
if errorlevel 1 (
    echo.
    echo Derleme basarisiz.
    exit /b 1
)

if "%1"=="build" exit /b 0

echo.
echo Baslatiliyor...
echo   Log dosyasi : dotnet\src\IdScanner.App\bin\Debug\net10.0-windows\logs\
echo   Ham veri    : dotnet\src\IdScanner.App\bin\Debug\net10.0-windows\diag\
echo.
start "" "%APP%"

endlocal
