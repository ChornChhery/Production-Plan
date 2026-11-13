@echo off
title Teeth - remove the firewall rules
net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo  Right-click this file and choose "Run as administrator", then try again.
  echo.
  pause
  exit /b 1
)
netsh advfirewall firewall delete rule name="Teeth link"
netsh advfirewall firewall delete rule name="Teeth ping"
echo.
echo  Done. The two Teeth firewall rules are removed. Nothing else was changed.
echo.
pause
