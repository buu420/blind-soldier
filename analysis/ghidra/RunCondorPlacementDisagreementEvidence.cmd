@echo off
setlocal
set "JAVA_HOME=C:\Program Files\Microsoft\jdk-21.0.11.10-hotspot"
set "XDG_CONFIG_HOME=%CD%\.artifacts\ghidra-xdg"
if exist "%TEMP%\BlindSoldierCondorGhidra\CondorPlacement.gpr" goto process
call "%CD%\.tools\ghidra_12.1.2_PUBLIC\support\analyzeHeadless.bat" ^
  "%TEMP%\BlindSoldierCondorGhidra" CondorPlacement ^
  -import "C:\Games\Final Fantasy VII\workingdir\ff7_en.exe" ^
  -scriptPath "%CD%\analysis\ghidra" ^
  -postScript DumpCondorPlacementDisagreementEvidence.java
goto done

:process
call "%CD%\.tools\ghidra_12.1.2_PUBLIC\support\analyzeHeadless.bat" ^
  "%TEMP%\BlindSoldierCondorGhidra" CondorPlacement ^
  -process ff7_en.exe -noanalysis ^
  -scriptPath "%CD%\analysis\ghidra" ^
  -postScript DumpCondorPlacementDisagreementEvidence.java

:done
exit /b %ERRORLEVEL%
