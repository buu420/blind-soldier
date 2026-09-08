@echo off
setlocal
set "JAVA_HOME=C:\Program Files\Microsoft\jdk-21.0.11.10-hotspot"
set "XDG_CONFIG_HOME=%CD%\.artifacts\ghidra-xdg"
set "LOCALAPPDATA=%CD%\.artifacts\ghidra-localappdata"
set "PROJECT_ROOT=%TEMP%\BlindSoldierJunonMovies"
set "PROJECT_NAME=JunonMovies"
set "BINARY=C:\Games\Final Fantasy VII\workingdir\ff7_en.exe"

if not exist "%PROJECT_ROOT%" mkdir "%PROJECT_ROOT%"
if errorlevel 1 exit /b 1

if not exist "%PROJECT_ROOT%\%PROJECT_NAME%.gpr" (
  call "%CD%\.tools\ghidra_12.1.2_PUBLIC\support\analyzeHeadless.bat" ^
    "%PROJECT_ROOT%" "%PROJECT_NAME%" ^
    -import "%BINARY%"
  if errorlevel 1 exit /b 1
)

call "%CD%\.tools\ghidra_12.1.2_PUBLIC\support\analyzeHeadless.bat" ^
  "%PROJECT_ROOT%" "%PROJECT_NAME%" ^
  -process ff7_en.exe -noanalysis ^
  -scriptPath "%CD%\analysis\ghidra" ^
  -postScript DumpJunonMovieTableEvidence.java
if errorlevel 1 exit /b 1
exit /b 0
