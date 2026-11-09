@echo off
setlocal
set "WOG_SD_APP=%~1"
if not defined WOG_SD_APP set /p "WOG_SD_APP=SD app folder (example E:\switch\WogSwitchProbe): "
if not defined WOG_SD_APP exit /b 1
where dotnet.exe >nul 2>&1
if errorlevel 1 (
  echo Install Microsoft .NET 10 Runtime, then run this tool again.
  exit /b 1
)
dotnet "%~dp0runtime\WogSwitch.Managed.dll" --prepare-sd "%WOG_SD_APP%"
set "WOG_RESULT=%ERRORLEVEL%"
echo.
if not "%WOG_RESULT%"=="0" echo Preparation failed. See the message above.
if "%WOG_RESULT%"=="0" echo Preparation complete. Safely eject the SD card before using it on Switch.
pause
exit /b %WOG_RESULT%
