@echo off
title Teeth - allow laptop 1 to connect
net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo  Right-click this file and choose "Run as administrator", then try again.
  echo.
  pause
  exit /b 1
)
set "PORT=5055"
set "REMOTE=192.168.50.0/24"
netsh advfirewall firewall delete rule name="Teeth link" >nul 2>&1
netsh advfirewall firewall add rule name="Teeth link" dir=in action=allow protocol=TCP localport=%PORT% profile=any remoteip=%REMOTE%
netsh advfirewall firewall delete rule name="Teeth ping" >nul 2>&1
netsh advfirewall firewall add rule name="Teeth ping" dir=in action=allow protocol=icmpv4:8,any profile=any remoteip=%REMOTE%
echo.
echo  Done. Windows Firewall now lets laptop 1 reach Teeth on port %PORT%
echo  (and answers ping), only from the Teeth cable network %REMOTE%.
echo  (If you use another address range, change REMOTE in this file too.)
echo  (If you change Port in App Files\link.ini, change PORT in this file too.)
echo.
pause
