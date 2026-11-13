@echo off
title Teeth - undo the automatic setup
rem Removes what TEETH-AUTO.bat added: scheduled task, the two firewall rules (laptop 2), the USB adapter address
rem (back to automatic), the USB-suspend setting. Does NOT touch Teeth.exe, TeethSender.exe, the database,
rem Log Data, sender.ini, pending.txt, the built-in Ethernet port or the print software.
net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo  Windows will now ask for administrator permission. Click Yes.
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
echo.
echo  This removes the Teeth automatic setup from THIS laptop.
echo  Teeth will no longer be started or repaired automatically.
echo.
choice /m "  Continue"
if errorlevel 2 exit /b
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0App Files\Tools\Teeth-Doctor.ps1" -Mode Undo
echo.
pause
