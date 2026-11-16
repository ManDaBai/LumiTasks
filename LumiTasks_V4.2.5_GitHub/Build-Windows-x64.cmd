@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Windows-x64.ps1"
if errorlevel 1 echo BUILD_FAILED - installed app unchanged.
pause
