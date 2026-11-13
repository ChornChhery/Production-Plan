@echo off
title Teeth - test port listener (stand-in for Teeth)
echo.
echo  LAPTOP 2 only. Close Teeth.exe first (it uses the same port).
echo  This starts a small listener on port 5055 so laptop 1 can test the cable and firewall.
echo  On laptop 1 run:  Test-NetConnection 192.168.50.2 -Port 5055   (PowerShell)
echo  Press Ctrl+C to stop it.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Any, 5055); $l.Start(); Write-Host \x27Listening on 5055. Press Ctrl+C to stop.\x27; try { while ($true) { if ($l.Pending()) { $c = $l.AcceptTcpClient(); Write-Host (\x27Connection from \x27 + $c.Client.RemoteEndPoint); $c.Close() }; Start-Sleep -Milliseconds 200 } } finally { $l.Stop() }"
echo.
pause
