@echo off
setlocal
title Gwent Access installer
set "SRC=%~dp0"
set "DEST=C:\Program Files (x86)\Steam\steamapps\common\GWENT The Witcher Card Game"

rem If this package was unzipped straight into the Gwent folder, install in place.
if exist "%SRC%Gwent.exe" set "DEST=%SRC:~0,-1%"

if not exist "%DEST%\Gwent.exe" (
  echo Could not find Gwent in:
  rem Quoted: the block is parsed as a whole, and a bare ")" from "Program Files (x86)" would end it and kill the script.
  echo   "%DEST%"
  echo.
  echo Copy all files and folders from this package into your Gwent folder by hand,
  echo the folder that contains Gwent.exe. See README.txt.
  pause
  exit /b 1
)

tasklist /FI "IMAGENAME eq Gwent.exe" 2>NUL | find /I "Gwent.exe" >NUL
if not errorlevel 1 (
  echo Gwent is running. Close the game, then run this installer again.
  pause
  exit /b 1
)

echo Installing Gwent Access into:
echo   %DEST%
if /I not "%SRC:~0,-1%"=="%DEST%" (
  robocopy "%SRC%." "%DEST%" /E /NFL /NDL /NJH /NJS /NP /XF Install.bat Uninstall.bat README.txt >NUL
  if errorlevel 8 (
    echo.
    echo Copying failed. Right-click Install.bat and choose Run as administrator.
    pause
    exit /b 1
  )
)
if exist "%DEST%\version.dll.off" del "%DEST%\version.dll.off" >NUL 2>&1
if exist "%DEST%\winhttp.dll" (
  ren "%DEST%\winhttp.dll" winhttp.dll.off
  echo Renamed an old winhttp.dll to winhttp.dll.off, it breaks the Gwent login.
)
copy /Y "%SRC%README.txt" "%DEST%\GwentAccess-README.txt" >NUL 2>&1
rem Desktop shortcut that starts Gwent without the launcher.
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop')+'\Gwent Accessible.lnk'); $s.TargetPath='%DEST%\Gwent-NoLauncher.bat'; $s.WorkingDirectory='%DEST%'; $s.Save()" >NUL 2>&1
if not errorlevel 1 echo Added a desktop shortcut called Gwent Accessible. It starts Gwent without the launcher.
echo.
echo Done. Start Gwent with the Gwent Accessible shortcut on your desktop. The first start takes several minutes with no speech.
echo You'll hear "Gwent Access loaded" when it's ready.
pause
