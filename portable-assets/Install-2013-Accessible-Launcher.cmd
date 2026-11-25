@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Blind-Soldier\Launcher2013\Manage-2013Launcher.ps1" -Action Install -GameRoot "%~dp0."
if errorlevel 1 (
    echo The 2013 launcher could not be installed. Review the message above.
    pause
    exit /b 1
)
pause
