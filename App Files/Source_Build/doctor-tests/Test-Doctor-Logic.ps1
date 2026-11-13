# Offline logic tests for Teeth-Doctor.ps1 (run with PowerShell 7 on any PC: pwsh Test-Doctor-Logic.ps1).
# They test only the decision logic (ini parsing, adapter choice and safety, powercfg parsing, overall result)
# with FAKE adapters. They do NOT test Windows cmdlets, the real USB adapters, the firewall or the scheduled task.
$ErrorActionPreference = 'Stop'
$doctor = Join-Path (Join-Path (Join-Path (Join-Path $PSScriptRoot '..') '..') 'Tools') 'Teeth-Doctor.ps1'
. $doctor -Mode LoadOnly

$script:pass = 0; $script:fail = 0; $script:lines = @()
function Check([string]$name, $cond) {
    if ($cond) { $script:pass++; $script:lines += ('PASS  ' + $name) } else { $script:fail++; $script:lines += ('FAIL  ' + $name) }
}
function Nic($name, $mac, $usb, $wifi, $gw, $ips, $status) {
    [pscustomobject]@{ Name = $name; Index = 1; Mac = $mac; Description = 'x'; PnpId = ''; Status = $status; IsUsb = $usb; IsWifi = $wifi; HasGateway = $gw; Ipv4 = @($ips) }
}
$P = '192.168.50'

# ini
$h = ConvertFrom-IniLines -Lines @('; c', '', 'Role = 2', 'AdapterMac=AA-BB', '  Port=5055  ', 'bad line')
Check 'ini: Role' ($h['Role'] -eq '2')
Check 'ini: trims spaces' ($h['Port'] -eq '5055')
Check 'ini: ignores comment and bad lines' ($h.Count -eq 3)
Check 'mac: normalised' ((ConvertTo-NormalMac '00-e0-4c-68-01-ab') -eq '00E04C6801AB')
Check 'mac: colon form equals dash form' ((ConvertTo-NormalMac '00:E0:4C:68:01:AB') -eq (ConvertTo-NormalMac '00-e0-4c-68-01-ab'))
Check 'prefix' ((Get-RangePrefix '192.168.50.2') -eq '192.168.50')
Check 'ip in prefix: yes' (Test-IpInPrefix '192.168.50.1' $P)
Check 'ip in prefix: 192.168.5.1 is not 192.168.50.x' (-not (Test-IpInPrefix '192.168.5.1' $P))

# safety
$builtin = Nic 'Ethernet' 'AA-00' $false $false $true @('192.168.0.97') 'Up'
$usbBlank = Nic 'Ethernet 4' 'BB-01' $true $false $false @() 'Up'
$usbApipa = Nic 'Ethernet 4' 'BB-01' $true $false $false @('169.254.12.4') 'Up'
$usbOk = Nic 'Ethernet 4' 'BB-01' $true $false $false @('192.168.50.1') 'Up'
$usbGw = Nic 'Ethernet 5' 'CC-02' $true $false $true @('192.168.1.20') 'Up'
$usbForeign = Nic 'Ethernet 6' 'DD-03' $true $false $false @('192.168.7.9') 'Up'
$wifi = Nic 'Wi-Fi' 'EE-04' $true $true $true @('10.98.101.89') 'Up'
Check 'safe: built-in port is never safe' (-not (Test-AdapterSafe $builtin $P).Safe)
Check 'safe: blank USB adapter' ((Test-AdapterSafe $usbBlank $P).Safe)
Check 'safe: APIPA USB adapter' ((Test-AdapterSafe $usbApipa $P).Safe)
Check 'safe: USB adapter with the Teeth address' ((Test-AdapterSafe $usbOk $P).Safe)
Check 'safe: USB adapter with gateway is refused' (-not (Test-AdapterSafe $usbGw $P).Safe)
Check 'safe: USB adapter with foreign address is refused' (-not (Test-AdapterSafe $usbForeign $P).Safe)

$usbGwOnly = Nic 'Ethernet 10' 'DE-08' $true $false $true @() 'Up'
Check 'safe: USB adapter with a gateway but no address is refused (gateway rule alone)' (-not (Test-AdapterSafe $usbGwOnly $P).Safe)
$usbDhcp = Nic 'Ethernet 11' 'DF-09' $true $false $false @('192.168.1.44') 'Up'
Check 'safe: USB adapter with a DHCP address from a router is refused' (-not (Test-AdapterSafe $usbDhcp $P).Safe)
Check 'safe: USB adapter with 192.168.5.1 is refused (not 192.168.50.x)' (-not (Test-AdapterSafe (Nic 'E' 'X' $true $false $false @('192.168.5.1') 'Up') $P).Safe)
$sOnlyGw = Select-TeethAdapter -Adapters @($builtin, $usbGwOnly) -SavedMac '' -ExpectedIp '192.168.50.1' -Prefix $P
Check 'select: the only USB adapter has a gateway (no address) -> nothing chosen' ($null -eq $sOnlyGw.Adapter)

# selection
$s = Select-TeethAdapter -Adapters @($builtin, $usbBlank, $wifi) -SavedMac '' -ExpectedIp '192.168.50.1' -Prefix $P
Check 'select: only USB adapter chosen, built-in and Wi-Fi ignored' ($s.Status -eq 'Found' -and $s.Adapter.Name -eq 'Ethernet 4')
$s = Select-TeethAdapter -Adapters @($builtin, $wifi) -SavedMac '' -ExpectedIp '192.168.50.1' -Prefix $P
Check 'select: no USB adapter -> None' ($s.Status -eq 'None')
$s = Select-TeethAdapter -Adapters @($builtin, $usbGw) -SavedMac '' -ExpectedIp '192.168.50.1' -Prefix $P
Check 'select: only USB adapter has a gateway -> Unsafe, nothing chosen' ($s.Status -eq 'Unsafe' -and $null -eq $s.Adapter)
$a2 = Nic 'Ethernet 7' 'FF-05' $true $false $false @() 'Disconnected'
$s = Select-TeethAdapter -Adapters @($usbOk, $a2) -SavedMac '' -ExpectedIp '192.168.50.1' -Prefix $P
Check 'select: two USB, one has the Teeth address -> that one' ($s.Status -eq 'Found' -and $s.Adapter.Mac -eq 'BB-01')
$a3 = Nic 'Ethernet 8' 'AB-06' $true $false $false @() 'Disconnected'
$s = Select-TeethAdapter -Adapters @($usbBlank, $a3) -SavedMac '' -ExpectedIp '192.168.50.1' -Prefix $P
Check 'select: two USB, only one has cable (Up) -> that one' ($s.Status -eq 'Found' -and $s.Adapter.Mac -eq 'BB-01')
$a4 = Nic 'Ethernet 9' 'AC-07' $true $false $false @() 'Up'
$s = Select-TeethAdapter -Adapters @($usbBlank, $a4) -SavedMac '' -ExpectedIp '192.168.50.1' -Prefix $P
Check 'select: two USB both Up, none configured -> Ambiguous (asks)' ($s.Status -eq 'Ambiguous' -and $s.Candidates.Count -eq 2)
$moved = Nic 'Ethernet 12' 'bb:01' $true $false $false @('169.254.9.9') 'Up'
$s = Select-TeethAdapter -Adapters @($builtin, $a4, $moved) -SavedMac 'BB-01' -ExpectedIp '192.168.50.1' -Prefix $P
Check 'select: adapter moved to another USB port is found again by MAC' ($s.Status -eq 'Found' -and $s.Adapter.Name -eq 'Ethernet 12')

# powercfg
$en = @('Power Setting GUID: x', '  Minimum Possible Setting: 0x00000000', '  Maximum Possible Setting: 0xffffffff', '  Current AC Power Setting Index: 0x00000708', '  Current DC Power Setting Index: 0x00000384')
$ix = Get-PowercfgIndexes -Lines $en
Check 'powercfg: AC 1800 s' ($ix.AC -eq 1800)
Check 'powercfg: DC 900 s' ($ix.DC -eq 900)
$de = @('  Minimum: 0x00000000', '  Maximum: 0xffffffff', '  Aktuelle Netzbetrieb-Einstellung: 0x00000000', '  Aktuelle Akkubetrieb-Einstellung: 0x00000000')
$ix = Get-PowercfgIndexes -Lines $de
Check 'powercfg: translated labels still parse (never = 0)' ($ix.AC -eq 0 -and $ix.DC -eq 0)
Check 'powercfg: garbage -> null' ($null -eq (Get-PowercfgIndexes -Lines @('nothing here')))

# overall
$rOk = @([pscustomobject]@{ Status = 'OK' }, [pscustomobject]@{ Status = 'FIXED' }, [pscustomobject]@{ Status = 'INFO' })
$rWarn = @([pscustomobject]@{ Status = 'OK' }, [pscustomobject]@{ Status = 'WARN' })
$rFail = @([pscustomobject]@{ Status = 'WARN' }, [pscustomobject]@{ Status = 'FAIL' })
Check 'overall: OK/FIXED/INFO = GREEN' ((Get-OverallResult $rOk) -eq 'GREEN')
Check 'overall: a note = YELLOW' ((Get-OverallResult $rWarn) -eq 'YELLOW')
Check 'overall: a problem = RED' ((Get-OverallResult $rFail) -eq 'RED')


# counts safety net (pure decision)
$k = '2026-11-12 08:00'
$live0 = [pscustomobject]@{ In = 0; Out = 0; Frozen = 0.0; ShiftKey = $k }
$liveN = [pscustomobject]@{ In = 5; Out = 0; Frozen = 0.0; ShiftKey = $k }
$sv = [pscustomobject]@{ In = 1234; Out = 1100; Frozen = 0.0; ShiftKey = $k }
Check 'counts: 0/0 in the database, saved counts of the same shift -> restore' (Test-ShouldRestoreCounts -Live $live0 -Saved $sv -KeyNow $k)
Check 'counts: database has counts -> never restore' (-not (Test-ShouldRestoreCounts -Live $liveN -Saved $sv -KeyNow $k))
Check 'counts: saved counts are from the OLD shift -> never restore' (-not (Test-ShouldRestoreCounts -Live $live0 -Saved $sv -KeyNow '2026-11-12 20:00'))
Check 'counts: saved counts were 0/0 -> nothing to restore' (-not (Test-ShouldRestoreCounts -Live $live0 -Saved ([pscustomobject]@{ In = 0; Out = 0; Frozen = 0.0; ShiftKey = $k }) -KeyNow $k))
Check 'counts: no saved file -> nothing to restore' (-not (Test-ShouldRestoreCounts -Live $live0 -Saved $null -KeyNow $k))
Check 'shift key: 10:00 = same day 08:00' ((Get-ShiftKeyNow -Now ([datetime]'2026-11-12 10:00')) -eq '2026-11-12 08:00')
Check 'shift key: 19:59 = same day 08:00' ((Get-ShiftKeyNow -Now ([datetime]'2026-11-12 19:59')) -eq '2026-11-12 08:00')
Check 'shift key: 20:00 = same day 20:00' ((Get-ShiftKeyNow -Now ([datetime]'2026-11-12 20:00')) -eq '2026-11-12 20:00')
Check 'shift key: 03:00 = YESTERDAY 20:00' ((Get-ShiftKeyNow -Now ([datetime]'2026-11-13 03:00')) -eq '2026-11-12 20:00')
Check 'shift key: 07:59 = YESTERDAY 20:00' ((Get-ShiftKeyNow -Now ([datetime]'2026-11-13 07:59')) -eq '2026-11-12 20:00')

$script:lines += ''
$script:lines += ('RESULT: ' + $script:pass + ' of ' + ($script:pass + $script:fail) + ' checks passed')
$script:lines | ForEach-Object { Write-Host $_ }
if ($script:fail -gt 0) { exit 1 }
