@echo off
title Teeth - automatic setup and fix
rem ============================================================
rem  ONE CLICK. Double-click this file on EACH laptop.
rem  First time on a laptop = automatic setup. Later = automatic check and repair.
rem  Windows asks for administrator permission once: click Yes.
rem ============================================================
set "ARG=%~1"
net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo  Windows will now ask for administrator permission. Click Yes.
  if "%ARG%"=="" (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  ) else (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%ARG%' -Verb RunAs"
  )
  exit /b
)
if "%ARG%"=="" set "ARG=Auto"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0App Files\Tools\Teeth-Doctor.ps1" -Mode %ARG%
echo.
pause
