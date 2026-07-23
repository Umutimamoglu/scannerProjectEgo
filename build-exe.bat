@echo off
REM ============================================================
REM  Kimlik Okuma ve Kart Baski Sistemi - exe uretimi
REM
REM  jpackage ile "app-image" uretir: icinde exe + gomulu JRE olan
REM  tasinabilir bir klasor. Java kurulu olmayan makinede de calisir.
REM
REM  Cikti: dist\KimlikKartSistemi\KimlikKartSistemi.exe
REM
REM  Kullanim: build-exe.bat
REM ============================================================
setlocal

if not defined JAVA_HOME (
    if exist "C:\Program Files\Eclipse Adoptium\jdk-17.0.19.10-hotspot" (
        set "JAVA_HOME=C:\Program Files\Eclipse Adoptium\jdk-17.0.19.10-hotspot"
    )
)
if not defined JAVA_HOME (
    echo HATA: JAVA_HOME tanimli degil. JDK 17 kurulu olmali.
    exit /b 1
)

set "MVN=%~dp0.mvn\wrapper\apache-maven\bin\mvn.cmd"
set "APPNAME=KimlikKartSistemi"
set "DIST=%~dp0dist"

echo.
echo [1/4] Derleniyor ve jar'lar toplaniyor...
call "%MVN%" -q clean package -DskipTests
if errorlevel 1 (
    echo HATA: Maven derlemesi basarisiz.
    exit /b 1
)

echo [2/4] Onceki cikti temizleniyor...
if exist "%DIST%\%APPNAME%" rmdir /s /q "%DIST%\%APPNAME%"
if not exist "%DIST%" mkdir "%DIST%"

echo [3/4] jpackage ile exe uretiliyor...
"%JAVA_HOME%\bin\jpackage.exe" ^
    --type app-image ^
    --name "%APPNAME%" ^
    --app-version 1.0.0 ^
    --vendor "Mobiloby" ^
    --description "Kimlik Okuma ve Kart Baski Sistemi" ^
    --input "%~dp0target\lib" ^
    --main-jar id-scanner-1.0-SNAPSHOT.jar ^
    --main-class com.mobiloby.MainUI ^
    --dest "%DIST%" ^
    --java-options "-Dfile.encoding=UTF-8" ^
    --java-options "-Dsun.java2d.dpiaware=true"
if errorlevel 1 (
    echo HATA: jpackage basarisiz.
    exit /b 1
)

echo [4/4] Native kutuphaneler ve kaynaklar kopyalaniyor...
REM Bunlar exe'nin YANINDA olmali - AppPaths kok dizini exe konumundan cozuyor.
xcopy /e /i /y /q "%~dp0native_x64"    "%DIST%\%APPNAME%\native_x64"    >nul
xcopy /e /i /y /q "%~dp0native_evolis" "%DIST%\%APPNAME%\native_evolis" >nul
if exist "%~dp0config.ini" copy /y "%~dp0config.ini" "%DIST%\%APPNAME%\config.ini" >nul

REM Calisma klasorleri
if not exist "%DIST%\%APPNAME%\output"      mkdir "%DIST%\%APPNAME%\output"
if not exist "%DIST%\%APPNAME%\scan_output" mkdir "%DIST%\%APPNAME%\scan_output"

echo.
echo ============================================================
echo  TAMAM
echo.
echo  Uygulama: %DIST%\%APPNAME%\%APPNAME%.exe
echo.
echo  Bu klasorun TAMAMINI kopyalayarak baska makineye tasiyabilirsiniz.
echo  Java kurulumu gerekmez - JRE gomulu.
echo.
echo  Hedef makinede yine de gerekli olanlar:
echo    - Okuyucu icin libusb-win32 surucusu (Zadig)
echo    - Yazici icin Evolis Premium Suite
echo ============================================================
endlocal
