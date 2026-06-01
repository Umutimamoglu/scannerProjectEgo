@echo off
setlocal enabledelayedexpansion
set SCRIPT_DIR=%~dp0
set WRAPPER_DIR=%SCRIPT_DIR%\.mvn\wrapper
set MAVEN_DIR=%WRAPPER_DIR%\apache-maven
set MVN_CMD=%MAVEN_DIR%\bin\mvn.cmd
set MVN_URL=https://archive.apache.org/dist/maven/maven-3/3.9.9/binaries/apache-maven-3.9.9-bin.zip
set ZIP=%WRAPPER_DIR%\apache-maven-3.9.9-bin.zip

if not exist "%MVN_CMD%" (
  if not exist "%WRAPPER_DIR%" mkdir "%WRAPPER_DIR%"
  if not exist "%ZIP%" (
    echo Downloading Maven distribution...
    powershell -NoProfile -Command "Try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '%MVN_URL%' -OutFile '%ZIP%' -UseBasicParsing } Catch { Exit 1 }"
    if errorlevel 1 (
      echo Download failed with PowerShell. Trying certutil...
      certutil -urlcache -split -f "%MVN_URL%" "%ZIP%" >nul 2>&1
      if errorlevel 1 (
        echo Failed to download Maven distribution.
        exit /b 1
      )
    )
  )
  echo Extracting Maven distribution...
  powershell -NoProfile -Command "Expand-Archive -Path '%ZIP%' -DestinationPath '%WRAPPER_DIR%' -Force"
  if errorlevel 1 (
    echo Failed to extract Maven distribution.
    exit /b 1
  )
  if not exist "%MAVEN_DIR%" ren "%WRAPPER_DIR%\apache-maven-3.9.9" apache-maven
)
"%MVN_CMD%" %*
endlocal
