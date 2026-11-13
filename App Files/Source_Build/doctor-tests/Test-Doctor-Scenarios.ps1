# Scenario tests for Teeth-Doctor.ps1: runs the real script through Simulate-Doctor.ps1 (pretend Windows) and checks the outcome.
# Developer test. Run:  pwsh Test-Doctor-Scenarios.ps1   (or powershell.exe -File Test-Doctor-Scenarios.ps1)
$ErrorActionPreference = 'Stop'
$sim = Join-Path $PSScriptRoot 'Simulate-Doctor.ps1'
$me = (Get-Process -Id $PID).Path
$script:pass = 0; $script:fail = 0; $script:lines = @()

function Run-Sim {
    param([string]$Scenario, [string]$Mode = 'Fix', [switch]$Silent, [switch]$Then)
    $a = @('-NoProfile', '-File', $sim, '-Scenario', $Scenario, '-Mode', $Mode)
    if ($Silent) { $a += '-Silent' }
    if ($Then) { $env:SIM_THEN = '1' } else { Remove-Item Env:\SIM_THEN -ErrorAction SilentlyContinue }
    $out = & $me @a 2>&1 | ForEach-Object { [string]$_ }
    Remove-Item Env:\SIM_THEN -ErrorAction SilentlyContinue
    $text = ($out -join "`n")
    $exit = -1
    foreach ($l in $out) { if ($l -match '--- exit code:\s*(-?\d+)') { $exit = [int]$Matches[1] } }
    $log = @(); $on = $false
    foreach ($l in $out) { if ($on -and $l -match '^\s{4}\S') { $log += $l.Trim() } if ($l -match 'what the simulated Windows was asked to change') { $on = $true } if ($l -match '^--- LAST-STATUS') { $on = $false } }
    [pscustomobject]@{ Text = $text; Exit = $exit; Log = $log; Threw = ($text -match 'SCRIPT THREW') }
}
function Check([string]$name, $cond) {
    if ($cond) { $script:pass++; $script:lines += ('PASS  ' + $name) } else { $script:fail++; $script:lines += ('FAIL  ' + $name) }
}
function Has($r, [string]$pattern) { return (@($r.Log | Where-Object { $_ -like $pattern }).Count -gt 0) }

# Laptop 2, fresh PC
$r = Run-Sim 'L2'
Check 'L2 fresh: script did not throw, exit 0' ((-not $r.Threw) -and $r.Exit -eq 0)
Check 'L2 fresh: IP 192.168.50.2/24 set, without gateway' (Has $r 'New-NetIPAddress 192.168.50.2/24 (no gateway)')
Check 'L2 fresh: leftover APIPA address removed' (Has $r 'Remove-NetIPAddress 169.254.*')
Check 'L2 fresh: both firewall rules created, only from 192.168.50.0/24' ((Has $r "New-NetFirewallRule 'Teeth link' TCP 55055 from 192.168.50.0/24") -and (Has $r "New-NetFirewallRule 'Teeth ping' ICMPv4*from 192.168.50.0/24"))
Check 'L2 fresh: Teeth.exe started' (Has $r 'started Teeth.exe')
Check 'L2 fresh: scheduled task at log-on and every 5 min, highest' (Has $r "Register-ScheduledTask 'Teeth Doctor' triggers: logon, every 5 min*runlevel=Highest")
Check 'L2 fresh: built-in port (index 3) and Wi-Fi (index 5) never changed' (@($r.Log | Where-Object { $_ -match 'Interface(Index)? (3|5)\b' -or $_ -match '192\.168\.0\.97|10\.98\.101\.89' }).Count -eq 0)
Check 'L2 fresh: TeethSender never started on laptop 2' (-not (Has $r 'started TeethSender.exe'))

# Laptop 1, fresh PC
$r = Run-Sim 'L1'
Check 'L1 fresh: no throw, exit 0' ((-not $r.Threw) -and $r.Exit -eq 0)
Check 'L1 fresh: IP 192.168.50.1/24 set, TeethSender started' ((Has $r 'New-NetIPAddress 192.168.50.1/24 (no gateway)') -and (Has $r 'started TeethSender.exe'))
Check 'L1 fresh: no firewall rule, Teeth.exe not started' ((-not (Has $r 'New-NetFirewallRule*')) -and (-not (Has $r 'started Teeth.exe')))

# Setup then Fix again: second run must change nothing
$r = Run-Sim 'L2' 'Setup' -Then
$second = $r.Text.Substring($r.Text.IndexOf('SECOND RUN'))
Check 'Setup: no throw' (-not $r.Threw)
Check 'Setup: asks "Which laptop" once' (([regex]::Matches($r.Text, 'Which laptop is this')).Count -eq 1)
Check 'Setup then Fix: second run reports no [FIXED] and no [PROBLEM]' (($second -notmatch '\[FIXED\]') -and ($second -notmatch '\[PROBLEM\]'))
Check 'Setup then Fix: second run changes nothing (log empty)' (($r.Log.Count -eq 0) -or (@($r.Log | Where-Object { $_ -match 'New-|Set-|Remove-|Register|started|Stop-' }).Count -eq 0))

# Already healthy: nothing changed
$r = Run-Sim 'L2-ok'
Check 'Healthy laptop 2: no change at all' ((-not $r.Threw) -and ($r.Log.Count -eq 0))
$r = Run-Sim 'L2-ok' 'Fix' -Silent
Check 'Silent run prints nothing to the screen' (($r.Text -notmatch '\[OK\]') -and ($r.Text -notmatch 'TEETH - automatic'))

# .NET 4.7 PC: the Doctor installs 4.8 from the installer next to the program (signed by Microsoft), only when not silent
$r = Run-Sim 'L2-net47'
Check '.NET 4.7: installer started with /q /norestart, result [FIXED], no problem left' ((-not $r.Threw) -and (Has $r 'ran .NET 4.8 installer /q /norestart') -and ($r.Text -match '\[FIXED\] \.NET Framework 4\.8') -and ($r.Text -notmatch '\[PROBLEM\]'))
$r = Run-Sim 'L2-net47' 'Fix' -Silent
Check '.NET 4.7: silent (scheduled) run never installs anything' ((-not $r.Threw) -and (-not (Has $r 'ran .NET 4.8 installer*')))
$r = Run-Sim 'L2-net47' 'Check'
Check '.NET 4.7: check-only mode reports the problem and installs nothing' ((-not (Has $r 'ran .NET 4.8 installer*')) -and ($r.Text -match '\[PROBLEM\] \.NET Framework 4\.8'))

# Wrong time zone: fixed only with an answer, clock time kept; silent only warns
$r = Run-Sim 'L2-tz'
Check 'Wrong time zone: set to SE Asia and the clock time is kept' ((-not $r.Threw) -and (Has $r 'Set-TimeZone SE Asia Standard Time') -and (Has $r 'Set-Date*'))
$r = Run-Sim 'L2-tz' 'Fix' -Silent
Check 'Wrong time zone, silent run: no change, only reported' ((-not $r.Threw) -and (-not (Has $r 'Set-TimeZone*')))

# Counts safety net: Teeth closed, database 0/0 in the same shift -> counts come back BEFORE Teeth starts
$r = Run-Sim 'L2-lost'
$iR = [array]::IndexOf(@($r.Log), (@($r.Log | Where-Object { $_ -like 'Write-LiveCounts IN 1234 OUT 1100*' }) | Select-Object -First 1))
$iS = [array]::IndexOf(@($r.Log), (@($r.Log | Where-Object { $_ -eq 'started Teeth.exe' }) | Select-Object -First 1))
Check 'Counts lost in the same shift: restored IN 1234 / OUT 1100, and BEFORE Teeth is started' ((-not $r.Threw) -and ($iR -ge 0) -and ($iS -gt $iR) -and ($r.Text -match '\[FIXED\] Counts'))
$r = Run-Sim 'L2-reset'
Check 'Saved counts from an OLD shift are never restored' ((-not $r.Threw) -and (-not (Has $r 'Write-LiveCounts*')))
$r = Run-Sim 'L2-lost' 'Check'
Check 'Check mode: counts only reported, nothing written' ((-not $r.Threw) -and (-not (Has $r 'Write-LiveCounts*')))
$r = Run-Sim 'L2-lost' 'Fix' -Silent
Check 'Silent run also restores (the 5-minute task)' ((-not $r.Threw) -and (Has $r 'Write-LiveCounts IN 1234 OUT 1100*'))

# Check-only
$r = Run-Sim 'L2' 'Check'
Check 'Check mode: changes nothing' ((-not $r.Threw) -and ($r.Log.Count -eq 0))
Check 'Check mode: reports the problems (RED, exit 1)' (($r.Exit -eq 1) -and ($r.Text -match 'RED'))

# Repairs
$r = Run-Sim 'L2-listener'
Check 'Leftover PowerShell listener stopped before Teeth starts' ((Has $r 'Stop-Process 777') -and (Has $r 'started Teeth.exe'))
$r = Run-Sim 'L2-rules-wrong'
Check 'Firewall rule with wrong port is recreated' (Has $r "New-NetFirewallRule 'Teeth link' TCP 55055*")

# Safety: must refuse and change nothing about the adapter
$r = Run-Sim 'L2-conflict'
Check 'Print network already uses 192.168.50.x: refuses, RED, no IP set' (($r.Exit -eq 1) -and (-not (Has $r 'New-NetIPAddress*')) -and ($r.Text -match 'another network on this laptop already uses'))
$r = Run-Sim 'L2-usbgateway'
Check 'USB adapter that has a gateway: refuses, RED, no IP set, no adapter change' (($r.Exit -eq 1) -and (-not (Has $r 'New-NetIPAddress*')) -and (-not (Has $r 'Set-NetIPInterface*')))
$r = Run-Sim 'L2-noadapter'
Check 'No USB adapter: RED, no adapter change, built-in port untouched' (($r.Exit -eq 1) -and (-not (Has $r 'New-NetIPAddress*')) -and (-not (Has $r 'Set-NetIPInterface*')) -and ($r.Text -match 'no USB-Ethernet adapter found'))

# Link problems on laptop 1
$r = Run-Sim 'L1-unplugged-unreach' 'Fix' -Silent
Check 'Laptop 1, cable out: RED with the cable text' (($r.Exit -eq 1) -and (-not $r.Threw))
$r = Run-Sim 'L1-unplugged-unreach'
Check 'Laptop 1, cable out: message names the adapter lights' ($r.Text -match 'no cable signal')

# Undo
$r = Run-Sim 'L2' 'Undo'
Check 'Undo: no throw, exit 0, programs and data listed as untouched' ((-not $r.Threw) -and $r.Exit -eq 0 -and ($r.Text -match 'Not touched'))

$script:lines += ''
$script:lines += ('RESULT: ' + $script:pass + ' of ' + ($script:pass + $script:fail) + ' scenario checks passed')
$script:lines | ForEach-Object { Write-Host $_ }
if ($script:fail -gt 0) { exit 1 }
