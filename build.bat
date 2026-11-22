@echo off
rem Radiant Quests for Meitou: double-click to build and install the mod.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
echo.
pause
