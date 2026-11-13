@echo off
title Teeth - self test of the automatic tool (changes nothing)
rem Runs the static checks and the logic checks of Teeth-Doctor with the Windows PowerShell of THIS PC.
rem It only reads files. It changes nothing on the PC. Takes a few seconds. No administrator rights needed.
set "T=%~dp0..\Source_Build\doctor-tests"
set "BAD=0"
echo.
echo  ==== 1 of 2: static checks (is the script valid for this PC's PowerShell?) ====
powershell -NoProfile -ExecutionPolicy Bypass -File "%T%\Test-Doctor-Static.ps1"
if errorlevel 1 set "BAD=1"
echo.
echo  ==== 2 of 2: logic checks (which adapter is safe to use, how settings are read) ====
powershell -NoProfile -ExecutionPolicy Bypass -File "%T%\Test-Doctor-Logic.ps1"
if errorlevel 1 set "BAD=1"
echo.
if "%BAD%"=="0" (
  echo  RESULT: ALL CHECKS PASSED. TEETH-AUTO.bat can be used on this PC.
) else (
  echo  RESULT: SOME CHECKS FAILED. Do not use TEETH-AUTO.bat. Send a photo of this window to the helper.
)
echo.
pause
