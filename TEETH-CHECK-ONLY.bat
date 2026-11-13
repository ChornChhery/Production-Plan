@echo off
title Teeth - check only (changes nothing)
rem Looks at everything and tells you what is wrong. Changes nothing. No administrator rights needed.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0App Files\Tools\Teeth-Doctor.ps1" -Mode Check
echo.
pause
