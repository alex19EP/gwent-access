@echo off
setlocal
title Gwent Access updater
rem Downloads the newest test build (the "dev" release) and installs it over this one.
set "URL=https://github.com/alex19EP/gwent-access/releases/download/dev/GwentAccess-dev.zip"
set "DEST=C:\Program Files (x86)\Steam\steamapps\common\GWENT The Witcher Card Game"
if exist "%~dp0Gwent.exe" set "DEST=%~dp0"
if "%DEST:~-1%"=="\" set "DEST=%DEST:~0,-1%"

if not exist "%DEST%\Gwent.exe" (
  echo Could not find Gwent in:
  echo   %DEST%
  echo Copy Update.bat into your Gwent folder, the folder that contains Gwent.exe, and run it from there.
  pause
  exit /b 1
)

tasklist /FI "IMAGENAME eq Gwent.exe" 2>NUL | find /I "Gwent.exe" >NUL
if not errorlevel 1 (
  echo Gwent is running. Close the game, then run this updater again.
  pause
  exit /b 1
)

set "TMPDIR=%TEMP%\GwentAccess-update"
if exist "%TMPDIR%" rmdir /S /Q "%TMPDIR%"
mkdir "%TMPDIR%"

echo Downloading the newest Gwent Access test build...
powershell -NoProfile -Command "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -UseBasicParsing -Uri '%URL%' -OutFile '%TMPDIR%\update.zip'; Expand-Archive -Path '%TMPDIR%\update.zip' -DestinationPath '%TMPDIR%\files' -Force"
if errorlevel 1 (
  echo.
  echo The download failed. Check your internet connection and try again.
  rmdir /S /Q "%TMPDIR%" >NUL 2>&1
  pause
  exit /b 1
)

echo Installing into:
echo   %DEST%
rem Update.bat is skipped: Windows reads a running batch file as it goes, so it can't replace itself here.
robocopy "%TMPDIR%\files" "%DEST%" /E /NFL /NDL /NJH /NJS /NP /XF Install.bat Uninstall.bat Update.bat README.txt >NUL
if errorlevel 8 (
  echo.
  echo Copying failed. Right-click Update.bat and choose Run as administrator.
  rmdir /S /Q "%TMPDIR%" >NUL 2>&1
  pause
  exit /b 1
)
if exist "%DEST%\version.dll.off" del "%DEST%\version.dll.off" >NUL 2>&1
if exist "%DEST%\winhttp.dll" (
  ren "%DEST%\winhttp.dll" winhttp.dll.off
  echo Renamed an old winhttp.dll to winhttp.dll.off, it breaks the Gwent login.
)
copy /Y "%TMPDIR%\files\README.txt" "%DEST%\GwentAccess-README.txt" >NUL 2>&1
rmdir /S /Q "%TMPDIR%" >NUL 2>&1

echo.
echo Done. You now have:
type "%DEST%\BepInEx\plugins\GwentAccess\build.txt"
echo.
echo Start Gwent as usual. If something sounds wrong, send BepInEx\LogOutput.log and the lines above.
pause
