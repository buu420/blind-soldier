@echo off
setlocal EnableExtensions
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Remove-AmethystRegistryEntries.ps1"
exit /b %ERRORLEVEL%
