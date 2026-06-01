@echo off
REM Tek komutla 32-bit JDK ile Maven çalıştır.
REM Kullanım:
REM   run.bat                                → App.java (varsayılan)
REM   run.bat scan                           → ScanTest.java
REM   run.bat app --mrz A12345678,030505,310209  → manuel override
setlocal
REM Sistem Java'sını kullan (Adoptium 25 64-bit)
if not defined JAVA_HOME (
    for /f "delims=" %%i in ('where javac') do set "JAVAC_PATH=%%i"
    for %%i in ("%JAVAC_PATH%\..\..") do set "JAVA_HOME=%%~fi"
)
set "MVN=%~dp0.mvn\wrapper\apache-maven\bin\mvn.cmd"

if "%1"=="scan" (
    shift
    "%MVN%" exec:java -Dexec.mainClass=com.mobiloby.ScanTest -Dexec.args="%*"
    goto :eof
)
if "%1"=="app" shift
"%MVN%" exec:java -Dexec.mainClass=com.mobiloby.App -Dexec.args="%*"
