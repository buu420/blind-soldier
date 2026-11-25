@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Blind-Soldier\Launcher2013\Manage-2013Launcher.ps1" -Action Uninstall -GameRoot "%~dp0."
if errorlevel 1 (
    echo The original 2013 launcher could not be restored. Review the message above.
    pause
    exit /b 1
)
pause
