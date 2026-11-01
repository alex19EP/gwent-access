@echo off
setlocal
title Gwent Access uninstaller
set "DEST=C:\Program Files (x86)\Steam\steamapps\common\GWENT The Witcher Card Game"
if exist "%~dp0Gwent.exe" set "DEST=%~dp0"
if "%DEST:~-1%"=="\" set "DEST=%DEST:~0,-1%"
if exist "%DEST%\version.dll" (
  ren "%DEST%\version.dll" version.dll.off
  if errorlevel 1 (
    echo Could not turn the mod off. Close Gwent, or right-click and Run as administrator.
    pause
    exit /b 1
  )
  echo Gwent Access is turned off. Run Install.bat to turn it back on.
) else (
  rem Quoted: the block is parsed as a whole, and a bare ")" from "Program Files (x86)" would end it and kill the script.
  echo Gwent Access is not active in "%DEST%".
)
pause
