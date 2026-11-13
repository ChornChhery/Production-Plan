# Teeth link test. Run it through Test-Link.bat in the main folder.
# Laptop 1: tests the cable link to laptop 2.  Laptop 2: tells you if Teeth is listening.
$ErrorActionPreference = "SilentlyContinue"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$hostName = "192.168.50.2"
$port = 5055
$sender = Join-Path $root "sender.ini"
$link = Join-Path (Join-Path $root "App Files") "link.ini"
if (Test-Path $sender) {
    foreach ($l in Get-Content $sender) {
        if ($l -match '^\s*Host\s*=\s*(\S+)') { $hostName = $Matches[1] }
        if ($l -match '^\s*Port\s*=\s*(\d+)') { $port = [int]$Matches[1] }
    }
} elseif (Test-Path $link) {
    foreach ($l in Get-Content $link) {
        if ($l -match '^\s*Port\s*=\s*(\d+)') { $port = [int]$Matches[1] }
    }
}
function Say($text, $color) { Write-Host $text -ForegroundColor $color }

Write-Host ""
Write-Host "Teeth link test  (target $hostName, port $port)"
Write-Host ""
$ips = @(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" })
Write-Host "This laptop has these IPv4 addresses:"
foreach ($i in $ips) { Write-Host ("   {0,-24} {1}" -f $i.InterfaceAlias, $i.IPAddress) }
Write-Host ""
$prefix = (($hostName -split '\.')[0..2]) -join '.'
$mine = @($ips | Where-Object { $_.IPAddress -like "$prefix.*" })
$self = @($ips | Where-Object { $_.IPAddress -eq $hostName })

if ($self.Count -gt 0) {
    Say "THIS IS LAPTOP 2 ($hostName). The cable test must be run on LAPTOP 1." Cyan
    Write-Host "Checking that Teeth is listening on this laptop..."
    $listen = Get-NetTCPConnection -LocalPort $port -State Listen
    if ($listen) {
        Say "RESULT: OK - Teeth is listening on port $port on this laptop." Green
        Write-Host "Next: run Test-Link.bat on LAPTOP 1."
    } else {
        Say "RESULT: NOT OK - nothing is listening on port $port." Red
        Write-Host "  1. Start Teeth.exe on THIS laptop and keep it open."
        Write-Host "  2. Read the red text at the top of Teeth. 'Link error: port $port busy' means another"
        Write-Host "     program or a second copy of Teeth uses the port: close it and start Teeth again."
        Write-Host "  3. Run this test again."
    }
    $rule = Get-NetFirewallRule -DisplayName "Teeth link"
    if (-not $rule) {
        Say "Firewall rule 'Teeth link' NOT found: right-click App Files\Tools\Allow-Laptop1-Firewall.bat and choose Run as administrator." Yellow
    } else {
        Write-Host "Firewall rule 'Teeth link' is present."
    }
} else {
    Say "THIS IS LAPTOP 1. Testing the cable link to laptop 2..." Cyan
    if ($mine.Count -eq 0) {
        Say "RESULT: NOT OK - this laptop has NO address in $prefix.x." Red
        Write-Host "  Set the IP on the USB adapter (guide step 3): laptop 1 = 192.168.50.1, mask 255.255.255.0."
        Write-Host "  Do not use the built-in Ethernet port."
        return
    }
    $pingOk = Test-Connection -ComputerName $hostName -Count 2 -Quiet
    $r = Test-NetConnection -ComputerName $hostName -Port $port -WarningAction SilentlyContinue
    if ($pingOk) { Say "Ping:  replies from $hostName" Green } else { Say "Ping:  no reply (Windows on laptop 2 may block ping: not always a problem)" Yellow }
    if ($r.TcpTestSucceeded) { Say "Port $port : open  (via adapter $($r.InterfaceAlias))" Green } else { Say "Port $port : CLOSED" Red }
    Write-Host ""
    if ($r.TcpTestSucceeded) {
        Say "RESULT: OK - Teeth is listening on port $port. Now start TeethSender.exe." Green
    } elseif ($pingOk) {
        Say "RESULT: NOT OK - laptop 2 is reachable but Teeth is not answering on port $port." Red
        Write-Host "  1. On laptop 2: is Teeth.exe open? Run Test-Link.bat there: it must say Teeth is listening."
        Write-Host "  2. On laptop 2: run App Files\Tools\Allow-Laptop1-Firewall.bat as administrator."
    } else {
        Say "RESULT: NOT OK - no contact with laptop 2 at all." Red
        Write-Host "  1. Port lights on both USB adapters? Cable plugged into the adapters (not the built-in ports)?"
        Write-Host "  2. On laptop 2: run Test-Link.bat. It must say THIS IS LAPTOP 2 and show $hostName."
        Write-Host "  3. On laptop 2: Teeth.exe open, and the firewall .bat run as administrator."
        Write-Host "  4. Same mask 255.255.255.0 on both laptops, gateway empty, set on the USB adapter."
    }
}
