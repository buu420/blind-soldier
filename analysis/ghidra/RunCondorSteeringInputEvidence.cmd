@echo off
setlocal
set "JAVA_HOME=C:\Program Files\Microsoft\jdk-21.0.11.10-hotspot"
set "XDG_CONFIG_HOME=%CD%\.artifacts\ghidra-xdg"
set "LOCALAPPDATA=%CD%\.artifacts\ghidra-localappdata"
call "%CD%\.tools\ghidra_12.1.2_PUBLIC\support\analyzeHeadless.bat" ^
  "%TEMP%\BlindSoldierCondorGhidra" CondorPlacement ^
  -process ff7_en.exe -noanalysis ^
  -scriptPath "%CD%\analysis\ghidra" ^
  -postScript DumpCondorSteeringInputEvidence.java
exit /b %ERRORLEVEL%
