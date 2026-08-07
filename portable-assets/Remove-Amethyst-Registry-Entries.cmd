@echo off
setlocal EnableExtensions
title Blind Soldier - Remove old mod-manager registry entries
echo Blind Soldier legacy registry cleanup
echo.
echo This removes only automatic-launch entries created by the old
echo Amethyst Accessibility Mod Manager package for Blind Soldier.
echo It does not remove game files or settings.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Blind-Soldier\Tools\Remove-AmethystRegistryEntries.ps1"
set "cleanupExit=%ERRORLEVEL%"
echo.
if "%BLIND_SOLDIER_CLEANUP_NO_PAUSE%"=="1" goto finish
pause
:finish
exit /b %cleanupExit%
