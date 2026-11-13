# Teeth-Doctor.ps1  -  automatic setup and automatic troubleshooting for the Teeth two-laptop link.
# Start it with TEETH-AUTO.bat (main folder). You do not type anything in cmd or PowerShell.
#
#   -Mode Auto   (default) first time = Setup, afterwards = Fix
#   -Mode Setup  one-time setup of THIS laptop, then the same checks as Fix
#   -Mode Fix    check everything and repair what a program can repair
#   -Mode Check  only look and report, change nothing
#   -Mode Undo   remove what Setup added (scheduled tasks, firewall rules, adapter IP back to automatic)
#   -Silent      no questions, no screen output (used by the scheduled task); result goes to Logs\
#
# ASCII only on purpose (Windows PowerShell 5.1 reads files without a BOM as ANSI).

param(
    [ValidateSet('Auto', 'Setup', 'Fix', 'Check', 'Undo', 'LoadOnly')]
    [string]$Mode = 'Auto',
    [switch]$Silent
)

$ErrorActionPreference = 'Stop'
$script:DoctorVersion = 'Doctor 2 (2026-11-12)'

$ToolsDir = $PSScriptRoot
$AppFiles = Split-Path $ToolsDir -Parent
$Root     = Split-Path $AppFiles -Parent
$LogDir   = Join-Path $AppFiles 'Logs'
$CfgPath  = Join-Path $AppFiles 'teeth-doctor.ini'
$TaskName = 'Teeth Doctor'

# ---------------------------------------------------------------------------
# Small helpers (pure functions: these are unit-tested on a non-Windows PC)
# ---------------------------------------------------------------------------

function ConvertFrom-IniLines {
    param([string[]]$Lines)
    $h = @{}
    foreach ($l in $Lines) {
        if ($null -eq $l) { continue }
        $t = $l.Trim()
        if ($t -eq '' -or $t.StartsWith(';') -or $t.StartsWith('#')) { continue }
        $i = $t.IndexOf('=')
        if ($i -lt 1) { continue }
        $h[$t.Substring(0, $i).Trim()] = $t.Substring($i + 1).Trim()
    }
    return $h
}

function Read-IniFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @{} }
    return (ConvertFrom-IniLines -Lines @(Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue))
}

function ConvertTo-NormalMac {
    param([string]$Mac)
    if (-not $Mac) { return '' }
    return (($Mac -replace '[^0-9A-Fa-f]', '').ToUpper())
}

function Get-RangePrefix {
    # '192.168.50.2' -> '192.168.50'
    param([string]$Ip)
    $p = $Ip.Split('.')
    if ($p.Count -lt 4) { return '' }
    return ($p[0] + '.' + $p[1] + '.' + $p[2])
}

function Test-IpInPrefix {
    param([string]$Ip, [string]$Prefix)
    return ($Ip.StartsWith($Prefix + '.'))
}

function Test-AdapterSafe {
    # An adapter is safe to configure only if it is a USB adapter, has no default gateway
    # and has no address from some other network (the print network must never be touched).
    param($Adapter, [string]$Prefix)
    if (-not $Adapter.IsUsb) {
        return [pscustomobject]@{ Safe = $false; Why = ('"' + $Adapter.Name + '" is not a USB adapter (it is a built-in port: never changed).') }
    }
    if ($Adapter.HasGateway) {
        return [pscustomobject]@{ Safe = $false; Why = ('"' + $Adapter.Name + '" has a gateway: it looks like a normal network, not the Teeth cable.') }
    }
    foreach ($ip in @($Adapter.Ipv4)) {
        if ($ip -like '169.254.*') { continue }
        if (Test-IpInPrefix -Ip $ip -Prefix $Prefix) { continue }
        return [pscustomobject]@{ Safe = $false; Why = ('"' + $Adapter.Name + '" already has the address ' + $ip + ' from another network.') }
    }
    return [pscustomobject]@{ Safe = $true; Why = '' }
}

function Select-TeethAdapter {
    # Decides which adapter is the Teeth USB adapter. Returns Status = Found / None / Ambiguous / Unsafe
    param($Adapters, [string]$SavedMac, [string]$ExpectedIp, [string]$Prefix)
    $all = @($Adapters)
    $saved = ConvertTo-NormalMac $SavedMac
    if ($saved -ne '') {
        $m = @($all | Where-Object { (ConvertTo-NormalMac $_.Mac) -eq $saved })
        if ($m.Count -ge 1) {
            return [pscustomobject]@{ Status = 'Found'; Adapter = $m[0]; Candidates = $m; Message = 'saved adapter found by its MAC address' }
        }
    }
    $cands = @($all | Where-Object { $_.IsUsb -and (-not $_.IsWifi) })
    if ($cands.Count -eq 0) {
        return [pscustomobject]@{ Status = 'None'; Adapter = $null; Candidates = @(); Message = 'no USB-Ethernet adapter found' }
    }
    $safe = @($cands | Where-Object { (Test-AdapterSafe -Adapter $_ -Prefix $Prefix).Safe })
    if ($safe.Count -eq 0) {
        $why = (Test-AdapterSafe -Adapter $cands[0] -Prefix $Prefix).Why
        return [pscustomobject]@{ Status = 'Unsafe'; Adapter = $null; Candidates = $cands; Message = $why }
    }
    if ($safe.Count -eq 1) {
        return [pscustomobject]@{ Status = 'Found'; Adapter = $safe[0]; Candidates = $safe; Message = 'the only USB-Ethernet adapter' }
    }
    $has = @($safe | Where-Object { @($_.Ipv4) -contains $ExpectedIp })
    if ($has.Count -eq 1) {
        return [pscustomobject]@{ Status = 'Found'; Adapter = $has[0]; Candidates = $safe; Message = 'the USB adapter that already has the Teeth address' }
    }
    $up = @($safe | Where-Object { $_.Status -eq 'Up' })
    if ($up.Count -eq 1) {
        return [pscustomobject]@{ Status = 'Found'; Adapter = $up[0]; Candidates = $safe; Message = 'the only USB adapter with a cable connected' }
    }
    return [pscustomobject]@{ Status = 'Ambiguous'; Adapter = $null; Candidates = $safe; Message = 'more than one USB-Ethernet adapter' }
}

function Get-PowercfgIndexes {
    # powercfg /query prints "... Index: 0x..." for AC then DC (labels are translated, so only the hex is used).
    param([string[]]$Lines)
    $hex = @()
    foreach ($l in $Lines) {
        $mm = [regex]::Matches([string]$l, '0x[0-9a-fA-F]+')
        foreach ($m in $mm) { $hex += $m.Value }
    }
    if ($hex.Count -lt 2) { return $null }
    $ac = [Convert]::ToInt64($hex[$hex.Count - 2].Substring(2), 16)
    $dc = [Convert]::ToInt64($hex[$hex.Count - 1].Substring(2), 16)
    return [pscustomobject]@{ AC = $ac; DC = $dc }
}

function Get-OverallResult {
    param($Results)
    $r = @($Results)
    $fail = @($r | Where-Object { $_.Status -eq 'FAIL' }).Count
    $warn = @($r | Where-Object { $_.Status -eq 'WARN' }).Count
    if ($fail -gt 0) { return 'RED' }
    if ($warn -gt 0) { return 'YELLOW' }
    return 'GREEN'
}

function Get-ShiftKeyNow {
    # Same rule as the app (ShiftCalculator.GetShiftStart + ShiftKeys): day shift starts 08:00, night shift 20:00.
    param([datetime]$Now)
    $d = $Now.Date
    if ($Now.Hour -ge 8 -and $Now.Hour -lt 20) { $start = $d.AddHours(8) }
    elseif ($Now.Hour -lt 8) { $start = $d.AddDays(-1).AddHours(20) }
    else { $start = $d.AddHours(20) }
    return $start.ToString('yyyy-MM-dd HH:mm', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Test-ShouldRestoreCounts {
    # $true only when the database shows 0 / 0 but the last saved counts of THIS SAME shift were above 0.
    param($Live, $Saved, [string]$KeyNow)
    if ($null -eq $Live -or $null -eq $Saved) { return $false }
    if (([int]$Live.In -ne 0) -or ([int]$Live.Out -ne 0)) { return $false }
    if (([int]$Saved.In -le 0) -and ([int]$Saved.Out -le 0)) { return $false }
    if ([string]$Saved.ShiftKey -ne $KeyNow) { return $false }
    return $true
}

if ($Mode -eq 'LoadOnly') { return }

# ---------------------------------------------------------------------------
# Output, log, results
# ---------------------------------------------------------------------------

$script:Results = New-Object System.Collections.ArrayList
$script:Changed = $false

function Say {
    param([string]$Text, [string]$Color = 'Gray')
    if (-not $Silent) { Write-Host $Text -ForegroundColor $Color }
}

function Write-Log {
    param([string]$Text)
    try {
        if (-not (Test-Path -LiteralPath $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
        $f = Join-Path $LogDir 'Teeth-Doctor-log.txt'
        if ((Test-Path -LiteralPath $f) -and ((Get-Item -LiteralPath $f).Length -gt 524288)) {
            Move-Item -LiteralPath $f -Destination (Join-Path $LogDir 'Teeth-Doctor-log.old.txt') -Force
        }
        $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $Text
        [System.IO.File]::AppendAllText($f, $line + [Environment]::NewLine)
    } catch { }
}

function Add-Result {
    param([string]$Status, [string]$Name, [string]$Detail = '', [string]$Todo = '')
    [void]$script:Results.Add([pscustomobject]@{ Status = $Status; Name = $Name; Detail = $Detail; Todo = $Todo })
    $color = 'Gray'; $tag = $Status
    switch ($Status) {
        'OK'    { $color = 'Green';  $tag = '[OK]   ' }
        'FIXED' { $color = 'Cyan';   $tag = '[FIXED]' }
        'WARN'  { $color = 'Yellow'; $tag = '[NOTE] ' }
        'FAIL'  { $color = 'Red';    $tag = '[PROBLEM]' }
        'INFO'  { $color = 'Gray';   $tag = '[info] ' }
    }
    $line = $tag + ' ' + $Name
    if ($Detail) { $line = $line + ' - ' + $Detail }
    Say ('  ' + $line) $color
    if ($Todo -and ($Status -eq 'FAIL' -or $Status -eq 'WARN')) { Say ('            -> ' + $Todo) 'Yellow' }
    if ($Status -eq 'FIXED') { $script:Changed = $true }
    if ($Status -ne 'OK' -and $Status -ne 'INFO') {
        Write-Log ($Status + ': ' + $Name + $(if ($Detail) { ' - ' + $Detail } else { '' }))
    }
}

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ask-YesNo {
    param([string]$Question, [bool]$Default = $true)
    if ($Silent) { return $Default }
    $hint = if ($Default) { 'Y/n' } else { 'y/N' }
    $a = Read-Host ($Question + ' (' + $hint + ')')
    if ($a -eq $null -or $a.Trim() -eq '') { return $Default }
    return ($a.Trim().ToLower().StartsWith('y'))
}

# ---------------------------------------------------------------------------
# Windows data gathering
# ---------------------------------------------------------------------------

function Get-AdapterInfo {
    $list = @()
    $nics = @(Get-NetAdapter -Physical -IncludeHidden:$false -ErrorAction SilentlyContinue)
    foreach ($n in $nics) {
        $pnp = [string]$n.PnPDeviceID
        $comp = ''
        try { $comp = [string]$n.ComponentID } catch { }
        $isUsb = ($pnp -like 'USB\*') -or ($comp -like 'usb\*')
        $desc = [string]$n.InterfaceDescription
        $isWifi = ([string]$n.PhysicalMediaType -eq 'Native 802.11') -or ($desc -match 'Wireless|Wi-?Fi|802\.11')
        $ips = @(Get-NetIPAddress -InterfaceIndex $n.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | ForEach-Object { $_.IPAddress })
        $gw = @(Get-NetRoute -InterfaceIndex $n.ifIndex -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue)
        $list += [pscustomobject]@{
            Name = [string]$n.Name; Index = [int]$n.ifIndex; Mac = [string]$n.MacAddress
            Description = $desc; PnpId = $pnp; Status = [string]$n.Status
            IsUsb = $isUsb; IsWifi = $isWifi; HasGateway = ($gw.Count -gt 0); Ipv4 = $ips
        }
    }
    return $list
}

function Get-OtherNetworkIps {
    param([int]$ExceptIndex)
    $r = @()
    foreach ($a in @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue)) {
        if ($a.InterfaceIndex -eq $ExceptIndex) { continue }
        if ($a.IPAddress -like '127.*') { continue }
        $r += [pscustomobject]@{ Name = [string]$a.InterfaceAlias; Ip = [string]$a.IPAddress }
    }
    return $r
}

function Test-TcpPort {
    param([string]$Ip, [int]$Port, [int]$Ms = 1500)
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $iar = $c.BeginConnect($Ip, $Port, $null, $null)
        if ($iar.AsyncWaitHandle.WaitOne($Ms)) {
            try { $c.EndConnect($iar); return $c.Connected } catch { return $false }
        }
        return $false
    } catch { return $false } finally { $c.Close() }
}

function Test-Ping {
    param([string]$Ip, [int]$Ms = 1000)
    try {
        $p = New-Object System.Net.NetworkInformation.Ping
        $r = $p.Send($Ip, $Ms)
        return ($r.Status -eq 'Success')
    } catch { return $false }
}

function Get-ProcsByName {
    param([string]$Name)
    return @(Get-Process -Name $Name -ErrorAction SilentlyContinue)
}

function Start-AppNormal {
    # Starts a program the way a double-click does (not as administrator), even from an elevated script.
    param([string]$Exe, [string]$ProcName)
    $dir = Split-Path $Exe -Parent
    if ($Silent) {
        for ($i = 0; $i -lt 45; $i++) { if (Get-ProcsByName 'explorer') { break }; Start-Sleep -Seconds 2 }
        Start-Sleep -Seconds 3
    }
    try { Start-Process -FilePath (Join-Path $env:WINDIR 'explorer.exe') -ArgumentList ('"' + $Exe + '"') } catch { }
    for ($i = 0; $i -lt 12; $i++) {
        Start-Sleep -Seconds 1
        if (Get-ProcsByName $ProcName) { return $true }
    }
    try { Start-Process -FilePath $Exe -WorkingDirectory $dir } catch { return $false }
    for ($i = 0; $i -lt 8; $i++) {
        Start-Sleep -Seconds 1
        if (Get-ProcsByName $ProcName) { return $true }
    }
    return $false
}

# ---------------------------------------------------------------------------
# Steps
# ---------------------------------------------------------------------------

function Get-DotNetRelease {
    $rel = 0
    try { $rel = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction Stop).Release } catch { }
    return $rel
}

function Find-DotNetInstaller {
    $name = 'ndp48-x86-x64-allos-enu.exe'
    $dirs = @($Root, $AppFiles, $ToolsDir, (Join-Path $Root 'Prerequisites'), (Join-Path $AppFiles 'Prerequisites'))
    try { foreach ($d in @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) { if ($d.Root) { $dirs += $d.Root } } } catch { }
    foreach ($d in $dirs) {
        if (-not $d) { continue }
        $f = Join-Path $d $name
        if (Test-Path -LiteralPath $f) { return $f }
    }
    return $null
}

function Step-DotNet {
    # Returns $true when .NET 4.8 is fine (or was installed), $false when it is a problem.
    $todoManual = 'Copy ndp48-x86-x64-allos-enu.exe into the Teeth folder (get it from microsoft.com on a PC with internet, 111 MB), then run TEETH-AUTO.bat again.'
    $rel = Get-DotNetRelease
    if ($rel -ge 528040) { Add-Result 'OK' '.NET Framework 4.8'; return $true }
    $have = if ($rel -ge 461808) { '4.7.2' } elseif ($rel -ge 460798) { '4.7' } elseif ($rel -gt 0) { 'older than 4.7' } else { 'none' }
    if ($Silent -or $Mode -eq 'Check' -or -not $script:IsAdmin) {
        Add-Result 'FAIL' '.NET Framework 4.8' ('not found (this PC has: ' + $have + ')') 'Double-click TEETH-AUTO.bat and click Yes: it installs .NET 4.8 by itself. Otherwise install ndp48-x86-x64-allos-enu.exe once.'
        return $false
    }
    $exe = Find-DotNetInstaller
    if ($exe) {
        Say ('  .NET Framework 4.8 is missing (this PC has: ' + $have + '). Installer found: ' + $exe) 'Yellow'
    } else {
        Say ('  .NET Framework 4.8 is missing (this PC has: ' + $have + '). The installer file is not in the Teeth folder.') 'Yellow'
        if (-not (Ask-YesNo 'Download it from Microsoft now? (needs internet, 111 MB)' $true)) {
            Add-Result 'FAIL' '.NET Framework 4.8' 'not found' $todoManual
            return $false
        }
        $exe = Join-Path $env:TEMP 'ndp48-x86-x64-allos-enu.exe'
        try {
            try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072 } catch { }
            Say '  Downloading... please wait (a few minutes on a slow line).' 'Gray'
            $wc = New-Object System.Net.WebClient
            $wc.DownloadFile('https://go.microsoft.com/fwlink/?linkid=2088631', $exe)
        } catch {
            Add-Result 'FAIL' '.NET Framework 4.8' ('download failed: ' + $_.Exception.Message) $todoManual
            return $false
        }
    }
    # Only run the file when Windows says it is signed by Microsoft and not damaged.
    $sigOk = $false
    try {
        $sig = Get-AuthenticodeSignature -LiteralPath $exe
        if ($sig.Status -eq 'Valid' -and $sig.SignerCertificate.Subject -match 'O=Microsoft Corporation') { $sigOk = $true }
    } catch { }
    if (-not $sigOk) {
        Add-Result 'FAIL' '.NET Framework 4.8' 'the installer file is not a valid Microsoft file (damaged or incomplete download)' ('Delete ' + $exe + ' and get a fresh copy. ' + $todoManual)
        return $false
    }
    if (-not (Ask-YesNo '  Install .NET Framework 4.8 now? It takes 5 to 15 minutes. Do not switch the PC off.' $true)) {
        Add-Result 'FAIL' '.NET Framework 4.8' 'not installed (you answered no)' 'Run TEETH-AUTO.bat again and answer Y.'
        return $false
    }
    Say '  Installing .NET Framework 4.8 ... please wait. Nothing to click.' 'Cyan'
    $code = -1
    try {
        $pr = Start-Process -FilePath $exe -ArgumentList '/q /norestart' -Wait -PassThru
        $code = [int]$pr.ExitCode
    } catch {
        Add-Result 'FAIL' '.NET Framework 4.8' ('could not start the installer: ' + $_.Exception.Message) $todoManual
        return $false
    }
    Write-Log ('.NET 4.8 installer exit code ' + $code)
    if ($code -eq 0 -or $code -eq 3010 -or $code -eq 1641) {
        if ((Get-DotNetRelease) -ge 528040) {
            Add-Result 'FIXED' '.NET Framework 4.8' 'installed'
            return $true
        }
        Add-Result 'WARN' '.NET Framework 4.8' 'installed, but Windows needs a RESTART to finish' 'Restart this PC, then double-click TEETH-AUTO.bat again.'
        return $true
    }
    if ($code -eq 5100) {
        Add-Result 'FAIL' '.NET Framework 4.8' 'this Windows is too old or not updated enough for .NET 4.8 (installer code 5100)' 'Run Windows Update (needs internet), restart, then run TEETH-AUTO.bat again. Windows 10 older than 1607 / Windows 7 without SP1 cannot use .NET 4.8.'
        return $false
    }
    if ($code -eq 1603 -or $code -eq 1618) {
        Add-Result 'FAIL' '.NET Framework 4.8' ('installer stopped (code ' + $code + '): another install or a pending restart is blocking it') 'Restart this PC, wait 5 minutes, then run TEETH-AUTO.bat again.'
        return $false
    }
    Add-Result 'FAIL' '.NET Framework 4.8' ('installer failed (code ' + $code + ')') 'Restart this PC and run TEETH-AUTO.bat again. If it fails again send App Files\Logs\LAST-STATUS.txt.'
    return $false
}

function Step-Folder {
    $problems = 0
    $x64 = [Environment]::Is64BitOperatingSystem
    if ($x64) { Add-Result 'OK' '64-bit Windows' } else { Add-Result 'FAIL' '64-bit Windows' 'this is 32-bit Windows; Teeth.exe needs 64-bit' 'Use a 64-bit Windows 10/11 PC.'; $problems++ }

    if (-not (Step-DotNet)) { $problems++ }

    $lib = Join-Path $AppFiles 'lib'
    $missing = @()
    foreach ($f in @('System.Data.SQLite.dll', 'ClosedXML.dll', 'DocumentFormat.OpenXml.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $lib $f))) { $missing += $f }
    }
    if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $lib 'x64') 'SQLite.Interop.dll'))) { $missing += 'x64\SQLite.Interop.dll' }
    if ($script:Role -eq 2 -and -not (Test-Path -LiteralPath (Join-Path $Root 'Teeth.exe'))) { $missing += 'Teeth.exe' }
    if ($script:Role -eq 1 -and -not (Test-Path -LiteralPath (Join-Path $Root 'TeethSender.exe'))) { $missing += 'TeethSender.exe' }
    if ($missing.Count -eq 0) { Add-Result 'OK' 'Program files present' } else {
        Add-Result 'FAIL' 'Program files present' ('missing: ' + ($missing -join ', ')) 'Copy the WHOLE Teeth folder from the zip (not only the exe).'; $problems++
    }

    $tmp = Join-Path $Root '__doctor_write_test.tmp'
    try { Set-Content -LiteralPath $tmp -Value 'test' -ErrorAction Stop; Remove-Item -LiteralPath $tmp -Force; Add-Result 'OK' 'Teeth folder is writable' }
    catch { Add-Result 'FAIL' 'Teeth folder is writable' 'the folder is read-only or protected' 'Move the Teeth folder to C:\Teeth.'; $problems++ }
    return $problems
}

function Step-InstallCopy {
    # Setup only: make sure the program lives in C:\Teeth. Returns $true when it started a new run from C:\Teeth.
    $target = 'C:\Teeth'
    if ($Root.TrimEnd('\').ToLower() -eq $target.ToLower()) { return $false }
    if ($Root.ToLower().StartsWith('c:\program files')) {
        Add-Result 'WARN' 'Folder location' ('running from ' + $Root) 'Program Files is protected. Copy the folder to C:\Teeth.'
    }
    $exists = Test-Path -LiteralPath $target
    if ($exists) {
        Say ''
        Say ('  C:\Teeth already exists (an older copy). This folder is: ' + $Root) 'Yellow'
        $upd = Ask-YesNo '  Update C:\Teeth with the new program files (database, Log Data and settings are kept)? Teeth and TeethSender must be closed.' $false
        if (-not $upd) {
            Add-Result 'WARN' 'Folder location' ('working in ' + $Root + ' (C:\Teeth not updated)') 'It is better to use one folder only: C:\Teeth.'
            return $false
        }
        foreach ($n in @('Teeth', 'TeethSender')) { foreach ($p in (Get-ProcsByName $n)) { try { $p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $p.HasExited) { $p.Kill() } } catch { } } }
    } else {
        Say ('  Copying the Teeth folder to ' + $target + ' ...') 'Cyan'
    }
    $robo = @($Root, $target, '/E', '/R:1', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP',
              '/XF', 'Teeth.sqlite', 'Teeth.sqlite-journal', 'Teeth.sqlite-wal', 'link.ini', 'sender.ini', 'pending.txt', 'teeth-doctor.ini',
              '/XD', 'Log Data', 'Logs')
    & robocopy @robo | Out-Null
    $code = $LASTEXITCODE
    if ($code -ge 8) { Add-Result 'FAIL' 'Copy to C:\Teeth' ('robocopy error code ' + $code) 'Close Teeth and TeethSender and run this again.'; return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $target 'Log Data'))) {
        if (Test-Path -LiteralPath (Join-Path $Root 'Log Data')) { Copy-Item -LiteralPath (Join-Path $Root 'Log Data') -Destination $target -Recurse -Force }
    }
    Add-Result 'FIXED' 'Copy to C:\Teeth' 'program files copied (data and settings kept)'
    $newScript = Join-Path (Join-Path (Join-Path $target 'App Files') 'Tools') 'Teeth-Doctor.ps1'
    if (Test-Path -LiteralPath $newScript) {
        Say '  Continuing from C:\Teeth ...' 'Cyan'
        Save-Cfg -Path (Join-Path (Join-Path $target 'App Files') 'teeth-doctor.ini')
        # let the new run take the lock
        if ($script:haveMutex -and $script:mutex) { try { $script:mutex.ReleaseMutex() } catch { }; $script:haveMutex = $false }
        $ps = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $newScript + '"'), '-Mode', 'Setup')
        Start-Process -FilePath 'powershell.exe' -ArgumentList $ps -Wait -NoNewWindow
        return $true
    }
    return $false
}

function Step-RestorePoints {
    $a = Join-Path $env:USERPROFILE 'network-before-teeth.txt'
    if (-not (Test-Path -LiteralPath $a)) {
        try { & ipconfig /all | Out-File -FilePath $a -Encoding ASCII; Add-Result 'FIXED' 'Restore point (network)' ('saved ' + $a) } catch { Add-Result 'WARN' 'Restore point (network)' 'could not save ipconfig' }
    } else { Add-Result 'OK' 'Restore point (network)' 'already saved' }
    if ($script:Role -eq 2) {
        $b = Join-Path $env:USERPROFILE 'firewall-before-teeth.wfw'
        if (-not (Test-Path -LiteralPath $b)) {
            try { & netsh advfirewall export $b | Out-Null; if (Test-Path -LiteralPath $b) { Add-Result 'FIXED' 'Restore point (firewall)' ('saved ' + $b) } else { Add-Result 'WARN' 'Restore point (firewall)' 'could not save' } } catch { Add-Result 'WARN' 'Restore point (firewall)' 'could not save' }
        } else { Add-Result 'OK' 'Restore point (firewall)' 'already saved' }
    }
}

function Step-Unblock {
    try {
        Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
        Add-Result 'OK' 'Files unblocked' 'downloaded-file block removed from the folder'
    } catch { Add-Result 'WARN' 'Files unblocked' 'could not unblock (not a problem if the programs start)' }
}

function Step-Role {
    $cfg = $script:Cfg
    if ($cfg.ContainsKey('Role') -and (($cfg['Role'] -eq '1') -or ($cfg['Role'] -eq '2'))) {
        $script:Role = [int]$cfg['Role']
        return $true
    }
    if ($Silent -or $Mode -eq 'Check') {
        Add-Result 'FAIL' 'Which laptop is this' 'not set up yet' 'Double-click TEETH-AUTO.bat on this laptop once.'
        return $false
    }
    $hint = ''
    $ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | ForEach-Object { $_.IPAddress })
    if ($ips -contains $script:L2Ip) { $hint = '2' }
    elseif ($ips -contains $script:L1Ip) { $hint = '1' }
    elseif (Test-Path -LiteralPath (Join-Path $AppFiles 'Teeth.sqlite')) { $hint = '2' }
    elseif (Test-Path -LiteralPath (Join-Path $Root 'sender.ini')) { $hint = '1' }
    Say ''
    Say '  Which laptop is this?' 'White'
    Say '     1 = LAPTOP 1  (key button, TeethSender)' 'White'
    Say '     2 = LAPTOP 2  (scanner, Teeth screen)' 'White'
    if ($hint) { Say ('  (I think it is ' + $hint + '. Just press Enter to accept.)') 'Cyan' }
    for ($i = 0; $i -lt 3; $i++) {
        $a = (Read-Host '  Type 1 or 2').Trim()
        if ($a -eq '' -and $hint) { $a = $hint }
        if ($a -eq '1' -or $a -eq '2') { $script:Role = [int]$a; $cfg['Role'] = $a; return $true }
    }
    Add-Result 'FAIL' 'Which laptop is this' 'no answer' 'Run TEETH-AUTO.bat again and type 1 or 2.'
    return $false
}

function Save-Cfg {
    param([string]$Path = $CfgPath)
    $order = @('Role', 'AdapterMac', 'TimeZone', 'Laptop1Ip', 'Laptop2Ip', 'PrefixLength', 'RestartPrograms', 'WatchEveryMinutes')
    $lines = @('; Teeth-Doctor settings (made by Teeth-Doctor.ps1). Safe to edit with Notepad.',
               '; Role: 1 = Laptop 1 (TeethSender), 2 = Laptop 2 (Teeth). AdapterMac = the USB adapter used for the Teeth cable.',
               '; RestartPrograms: true = the Doctor starts Teeth / TeethSender again if they are closed.')
    foreach ($k in $order) { if ($script:Cfg.ContainsKey($k)) { $lines += ($k + '=' + $script:Cfg[$k]) } }
    foreach ($k in $script:Cfg.Keys) { if ($order -notcontains $k) { $lines += ($k + '=' + $script:Cfg[$k]) } }
    try { Set-Content -LiteralPath $Path -Value $lines -Encoding ASCII } catch { Add-Result 'WARN' 'Settings file' 'could not save teeth-doctor.ini' }
}

function Step-Adapter {
    # Returns the adapter object (fresh) or $null
    $expected = if ($script:Role -eq 1) { $script:L1Ip } else { $script:L2Ip }
    $adapters = @(Get-AdapterInfo)
    $savedMac = if ($script:Cfg.ContainsKey('AdapterMac')) { $script:Cfg['AdapterMac'] } else { '' }
    $sel = Select-TeethAdapter -Adapters $adapters -SavedMac $savedMac -ExpectedIp $expected -Prefix $script:Prefix

    if ($sel.Status -eq 'None') {
        Add-Result 'FAIL' 'USB adapter' 'no USB-Ethernet adapter found' 'Plug the USB-Ethernet adapter into this laptop (same USB port every time), then run again.'
        return $null
    }
    if ($sel.Status -eq 'Unsafe') {
        Add-Result 'FAIL' 'USB adapter' $sel.Message 'Nothing was changed. The Doctor only touches a USB adapter that has no gateway and no address from another network.'
        return $null
    }
    if ($sel.Status -eq 'Ambiguous') {
        if ($Silent -or $Mode -eq 'Check') {
            Add-Result 'FAIL' 'USB adapter' 'more than one USB-Ethernet adapter' 'Run TEETH-AUTO.bat by hand once and choose the adapter.'
            return $null
        }
        Say ''
        Say '  More than one USB-Ethernet adapter found. Which one has the Teeth cable?' 'White'
        $i = 0
        foreach ($c in $sel.Candidates) { $i++; Say ('     ' + $i + ' = ' + $c.Name + '   (' + $c.Description + ', cable: ' + $c.Status + ')') 'White' }
        $pick = $null
        for ($t = 0; $t -lt 3; $t++) {
            $a = (Read-Host '  Type the number').Trim()
            $n = 0
            if ([int]::TryParse($a, [ref]$n) -and $n -ge 1 -and $n -le $sel.Candidates.Count) { $pick = $sel.Candidates[$n - 1]; break }
        }
        if (-not $pick) { Add-Result 'FAIL' 'USB adapter' 'no choice made' 'Run TEETH-AUTO.bat again.'; return $null }
        $sel = [pscustomobject]@{ Status = 'Found'; Adapter = $pick; Candidates = @($pick); Message = 'chosen by you' }
    }

    $ad = $sel.Adapter
    # Safety check, also for an adapter found by its saved MAC address
    $safe = Test-AdapterSafe -Adapter $ad -Prefix $script:Prefix
    if (-not $safe.Safe) {
        $override = $false
        if ($Mode -eq 'Setup' -and -not $Silent -and $ad.IsUsb -and -not $ad.HasGateway) {
            $override = Ask-YesNo ('  ' + $safe.Why + ' Use it for the Teeth cable anyway?') $false
        }
        if (-not $override) {
            Add-Result 'FAIL' 'USB adapter' $safe.Why 'Nothing was changed.'
            return $null
        }
    }

    # remember the adapter by MAC (survives a move to another USB port)
    $mac = ConvertTo-NormalMac $ad.Mac
    if ($mac -ne '' -and ((ConvertTo-NormalMac $savedMac) -ne $mac) -and $Mode -ne 'Check') {
        $script:Cfg['AdapterMac'] = $ad.Mac
        Save-Cfg
        Add-Result 'FIXED' 'USB adapter remembered' ('"' + $ad.Name + '" (' + $ad.Mac + ')')
    }

    # enable if disabled
    if ($ad.Status -eq 'Disabled') {
        if ($script:Fixing) {
            try { Enable-NetAdapter -Name $ad.Name -Confirm:$false -ErrorAction Stop; Add-Result 'FIXED' 'USB adapter enabled' $ad.Name; Start-Sleep -Seconds 3 } catch { Add-Result 'FAIL' 'USB adapter enabled' ('could not enable ' + $ad.Name) 'Enable it in Network Connections (ncpa.cpl).' }
        } else { Add-Result 'FAIL' 'USB adapter enabled' ($ad.Name + ' is disabled') 'Run TEETH-AUTO.bat to enable it.' }
    }

    # IP address
    $have = @(Get-NetIPAddress -InterfaceIndex $ad.Index -AddressFamily IPv4 -ErrorAction SilentlyContinue)
    $okIp = @($have | Where-Object { $_.IPAddress -eq $expected -and $_.PrefixLength -eq $script:PrefixLen }).Count -gt 0
    $others = @($have | Where-Object { $_.IPAddress -ne $expected })
    if ($okIp -and $others.Count -eq 0) {
        Add-Result 'OK' ('IP address ' + $expected) ('on "' + $ad.Name + '"')
    } elseif (-not $script:Fixing) {
        Add-Result 'FAIL' ('IP address ' + $expected) ('"' + $ad.Name + '" has: ' + (($have | ForEach-Object { $_.IPAddress }) -join ', ')) 'Run TEETH-AUTO.bat to set it.'
    } elseif (-not $script:IsAdmin) {
        Add-Result 'FAIL' ('IP address ' + $expected) 'administrator rights needed to set it' 'Double-click TEETH-AUTO.bat (it asks for administrator permission).'
    } else {
        $conf = @(Get-OtherNetworkIps -ExceptIndex $ad.Index | Where-Object { Test-IpInPrefix -Ip $_.Ip -Prefix $script:Prefix })
        if ($conf.Count -gt 0) {
            Add-Result 'FAIL' ('IP address ' + $expected) ('another network on this laptop already uses ' + $script:Prefix + '.x: "' + $conf[0].Name + '" ' + $conf[0].Ip) 'Not changed. Tell the helper: the cable network needs another address range.'
        } else {
            try {
                Set-NetIPInterface -InterfaceIndex $ad.Index -AddressFamily IPv4 -Dhcp Disabled -ErrorAction Stop
                foreach ($h in $others) { Remove-NetIPAddress -InterfaceIndex $ad.Index -IPAddress $h.IPAddress -Confirm:$false -ErrorAction SilentlyContinue }
                $still = @(Get-NetIPAddress -InterfaceIndex $ad.Index -IPAddress $expected -ErrorAction SilentlyContinue)
                if ($still.Count -eq 0) {
                    New-NetIPAddress -InterfaceIndex $ad.Index -IPAddress $expected -PrefixLength $script:PrefixLen -ErrorAction Stop | Out-Null
                }
                Set-DnsClientServerAddress -InterfaceIndex $ad.Index -ResetServerAddresses -ErrorAction SilentlyContinue
                Add-Result 'FIXED' ('IP address ' + $expected) ('set on "' + $ad.Name + '" (no gateway, no DNS)')
            } catch {
                Add-Result 'FAIL' ('IP address ' + $expected) ('could not set it: ' + $_.Exception.Message) 'Set it by hand: Win+R, ncpa.cpl (see the guide, step 4 or 5).'
            }
        }
    }

    # power saving of the USB adapter (a classic reason for "link drops after a while")
    if ($script:Fixing -and $script:IsAdmin -and $ad.PnpId) {
        try {
            $items = @(Get-CimInstance -Namespace 'root\wmi' -ClassName MSPower_DeviceEnable -ErrorAction Stop | Where-Object { $_.InstanceName -like ($ad.PnpId + '*') })
            $on = @($items | Where-Object { $_.Enable })
            if ($on.Count -gt 0) {
                foreach ($i in $on) { Set-CimInstance -InputObject $i -Property @{ Enable = $false } -ErrorAction Stop }
                Add-Result 'FIXED' 'Adapter power saving' 'Windows may no longer switch the USB adapter off to save power'
            } else { Add-Result 'OK' 'Adapter power saving' 'off' }
        } catch { Add-Result 'INFO' 'Adapter power saving' 'could not read/change (not important)' }
    }
    if ($Mode -eq 'Setup' -and $script:IsAdmin) {
        try {
            $names = @('Energy Efficient Ethernet', 'Green Ethernet', 'Advanced EEE', 'Power Saving Mode', 'Ultra Deep Power Down')
            $done = @()
            foreach ($p in @(Get-NetAdapterAdvancedProperty -Name $ad.Name -ErrorAction SilentlyContinue)) {
                if ($names -contains $p.DisplayName) {
                    $vals = @($p.ValidDisplayValues)
                    $off = $vals | Where-Object { $_ -match '^(Disabled|Off)$' } | Select-Object -First 1
                    if ($off -and ([string]$p.DisplayValue -ne [string]$off)) {
                        Set-NetAdapterAdvancedProperty -Name $ad.Name -DisplayName $p.DisplayName -DisplayValue $off -ErrorAction Stop
                        $done += $p.DisplayName
                    }
                }
            }
            if ($done.Count -gt 0) { Add-Result 'FIXED' 'Adapter energy saving' ('turned off: ' + ($done -join ', ')); Start-Sleep -Seconds 4 }
        } catch { Add-Result 'INFO' 'Adapter energy saving' 'driver has no such setting or refused (not important)' }
    }

    # fresh copy
    return (@(Get-AdapterInfo) | Where-Object { (ConvertTo-NormalMac $_.Mac) -eq $mac } | Select-Object -First 1)
}

function Step-UsbSuspend {
    # Windows "USB selective suspend" can switch a USB device off. Turn it off for the active power plan.
    if (-not $script:IsAdmin) { return }
    $sub = '2a737441-1930-4402-8d77-b2bebba308a3'
    $set = '48e6b7a6-50f5-4782-a5d4-53bb8f07e226'
    try {
        $idx = Get-PowercfgIndexes -Lines @(& powercfg /query SCHEME_CURRENT $sub $set 2>$null)
        if ($null -eq $idx) { Add-Result 'INFO' 'USB selective suspend' 'could not read the power plan (not important)'; return }
        if ($idx.AC -eq 0 -and $idx.DC -eq 0) { Add-Result 'OK' 'USB selective suspend' 'off'; return }
        if (-not $script:Fixing) { Add-Result 'WARN' 'USB selective suspend' 'on: Windows may switch USB devices off' 'Run TEETH-AUTO.bat to turn it off.'; return }
        & powercfg /setacvalueindex SCHEME_CURRENT $sub $set 0 | Out-Null
        & powercfg /setdcvalueindex SCHEME_CURRENT $sub $set 0 | Out-Null
        & powercfg /setactive SCHEME_CURRENT | Out-Null
        Add-Result 'FIXED' 'USB selective suspend' 'turned off for the active power plan'
    } catch { Add-Result 'INFO' 'USB selective suspend' 'could not change (company policy?) - not important' }
}

function Step-Sleep {
    try {
        $idx = Get-PowercfgIndexes -Lines @(& powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 2>$null)
        if ($null -eq $idx) { return }
        if ($idx.AC -gt 0) {
            Add-Result 'WARN' 'Sleep' ('this PC goes to sleep after ' + [int]($idx.AC / 60) + ' min on mains power') 'A sleeping laptop counts nothing. Set Sleep to Never (Settings > System > Power). Not changed by the Doctor.'
        } else { Add-Result 'OK' 'Sleep' 'never on mains power' }
    } catch { }
}

function Step-Firewall {
    # Laptop 2 only
    $port = $script:Port
    $remote = $script:Prefix + '.0/24'
    $linkOk = $false
    $pingOk = $false
    $r = @(Get-NetFirewallRule -DisplayName 'Teeth link' -ErrorAction SilentlyContinue)
    if ($r.Count -ge 1) {
        $pf = @(Get-NetFirewallPortFilter -AssociatedNetFirewallRule $r[0] -ErrorAction SilentlyContinue)
        $linkOk = ([string]$r[0].Enabled -eq 'True') -and ([string]$r[0].Action -eq 'Allow') -and ([string]$r[0].Direction -eq 'Inbound') -and ($pf.Count -ge 1) -and ([string]$pf[0].LocalPort -eq [string]$port)
    }
    $p = @(Get-NetFirewallRule -DisplayName 'Teeth ping' -ErrorAction SilentlyContinue)
    if ($p.Count -ge 1) { $pingOk = ([string]$p[0].Enabled -eq 'True') -and ([string]$p[0].Action -eq 'Allow') }

    if ($linkOk -and $pingOk) { Add-Result 'OK' 'Firewall rules' ('"Teeth link" (port ' + $port + ') and "Teeth ping" are present'); return }
    if (-not $script:Fixing) { Add-Result 'FAIL' 'Firewall rules' 'missing or wrong' 'Run TEETH-AUTO.bat on this laptop.'; return }
    if (-not $script:IsAdmin) { Add-Result 'FAIL' 'Firewall rules' 'administrator rights needed' 'Double-click TEETH-AUTO.bat.'; return }
    try {
        if (-not $linkOk) {
            Get-NetFirewallRule -DisplayName 'Teeth link' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
            New-NetFirewallRule -DisplayName 'Teeth link' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $port -Profile Any -RemoteAddress $remote -ErrorAction Stop | Out-Null
        }
        if (-not $pingOk) {
            Get-NetFirewallRule -DisplayName 'Teeth ping' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
            New-NetFirewallRule -DisplayName 'Teeth ping' -Direction Inbound -Action Allow -Protocol ICMPv4 -IcmpType 8 -Profile Any -RemoteAddress $remote -ErrorAction Stop | Out-Null
        }
        Add-Result 'FIXED' 'Firewall rules' ('added (only from ' + $remote + ', incoming only)')
    } catch {
        Add-Result 'FAIL' 'Firewall rules' ('could not add: ' + $_.Exception.Message) 'Company IT policy may block firewall changes. Send the Logs folder to the helper.'
    }
}

function Step-PortOwner {
    # Laptop 2 only: who listens on the port?
    $port = $script:Port
    $l = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
    if ($l.Count -eq 0) { return $null }
    $procId = [int]$l[0].OwningProcess
    $pr = Get-Process -Id $procId -ErrorAction SilentlyContinue
    $name = if ($pr) { [string]$pr.ProcessName } else { 'unknown' }
    return [pscustomobject]@{ Id = $procId; Name = $name }
}

# ---------------------------------------------------------------------------
# Counts safety net (Laptop 2): the live counts are copied to a file outside the Teeth folder while Teeth runs.
# If Teeth is closed and the database then shows 0 / 0 in the SAME shift, the counts are put back before Teeth starts.
# ---------------------------------------------------------------------------

function Get-CountsStorePath {
    $base = $env:ProgramData
    if (-not $base) { $base = $env:ALLUSERSPROFILE }
    if (-not $base) { $base = $AppFiles }
    return (Join-Path (Join-Path $base 'Teeth') 'counts-latest.txt')
}

function Open-LiveDb {
    param([bool]$ReadOnly)
    $dll = Join-Path (Join-Path $AppFiles 'lib') 'System.Data.SQLite.dll'
    $db = Join-Path $AppFiles 'Teeth.sqlite'
    if (-not (Test-Path -LiteralPath $dll) -or -not (Test-Path -LiteralPath $db)) { return $null }
    [Environment]::SetEnvironmentVariable('PreLoadSQLite_BaseDirectory', (Join-Path $AppFiles 'lib'))
    [void][System.Reflection.Assembly]::LoadFrom($dll)
    $cs = 'Data Source=' + $db + ';Version=3;Journal Mode=WAL;Synchronous=Normal;'
    if ($ReadOnly) { $cs = $cs + 'Read Only=True;' }
    $c = New-Object System.Data.SQLite.SQLiteConnection($cs)
    $c.Open()
    return $c
}

function Read-LiveCounts {
    $c = $null
    try {
        $c = Open-LiveDb -ReadOnly $true
        if ($null -eq $c) { return $null }
        $cmd = $c.CreateCommand()
        $cmd.CommandText = 'SELECT CountIn, CountOut, FrozenSeconds, ShiftKey FROM LiveCounts WHERE Id = 1;'
        $r = $cmd.ExecuteReader()
        try {
            if ($r.Read()) {
                $k = ''
                if (-not $r.IsDBNull(3)) { $k = [string]$r.GetString(3) }
                return [pscustomobject]@{ In = [int]$r.GetInt32(0); Out = [int]$r.GetInt32(1); Frozen = [double]$r.GetDouble(2); ShiftKey = $k }
            }
        } finally { $r.Close() }
        return $null
    } catch { Write-Log ('Read-LiveCounts: ' + $_.Exception.Message); return $null }
    finally { if ($c) { $c.Close() } }
}

function Write-LiveCounts {
    param($Counts, [string]$ShiftKey)
    $c = $null
    try {
        $c = Open-LiveDb -ReadOnly $false
        if ($null -eq $c) { return $false }
        $cmd = $c.CreateCommand()
        $cmd.CommandText = 'UPDATE LiveCounts SET CountIn = @i, CountOut = @o, FrozenSeconds = @f, ShiftKey = @k WHERE Id = 1;'
        [void]$cmd.Parameters.AddWithValue('@i', [int]$Counts.In)
        [void]$cmd.Parameters.AddWithValue('@o', [int]$Counts.Out)
        [void]$cmd.Parameters.AddWithValue('@f', [double]$Counts.Frozen)
        [void]$cmd.Parameters.AddWithValue('@k', $ShiftKey)
        [void]$cmd.ExecuteNonQuery()
        return $true
    } catch { Write-Log ('Write-LiveCounts: ' + $_.Exception.Message); return $false }
    finally { if ($c) { $c.Close() } }
}

function Save-CountsFile {
    param($Counts)
    try {
        $f = Get-CountsStorePath
        $d = Split-Path $f -Parent
        if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
        $lines = @(('In=' + $Counts.In), ('Out=' + $Counts.Out), ('Frozen=' + ([string]$Counts.Frozen)), ('ShiftKey=' + $Counts.ShiftKey), ('Saved=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
        Set-Content -LiteralPath $f -Value $lines -Encoding ASCII
    } catch { Write-Log ('Save-CountsFile: ' + $_.Exception.Message) }
}

function Read-CountsFile {
    try {
        $f = Get-CountsStorePath
        if (-not (Test-Path -LiteralPath $f)) { return $null }
        $h = Read-IniFile -Path $f
        if (-not ($h.ContainsKey('In') -and $h.ContainsKey('Out') -and $h.ContainsKey('ShiftKey'))) { return $null }
        $fz = 0.0
        if ($h.ContainsKey('Frozen')) { [void][double]::TryParse([string]$h['Frozen'], [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$fz) }
        return [pscustomobject]@{ In = [int]$h['In']; Out = [int]$h['Out']; Frozen = $fz; ShiftKey = [string]$h['ShiftKey'] }
    } catch { return $null }
}

function Add-CountsHistory {
    param($Live, [string]$Note)
    try {
        $f = Join-Path $LogDir 'counts-history.txt'
        if (-not (Test-Path -LiteralPath $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
        if ((Test-Path -LiteralPath $f) -and ((Get-Item -LiteralPath $f).Length -gt 200000)) { Move-Item -LiteralPath $f -Destination (Join-Path $LogDir 'counts-history.old.txt') -Force }
        $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  IN=' + $Live.In + ' OUT=' + $Live.Out + '  shift ' + $Live.ShiftKey + $(if ($Note) { '  ' + $Note } else { '' })
        [System.IO.File]::AppendAllText($f, $line + [Environment]::NewLine)
    } catch { }
}

function Step-CountsGuard {
    # Laptop 2 only. Runs BEFORE Teeth is started.
    if ($script:Role -ne 2) { return }
    $running = @(Get-ProcsByName 'Teeth').Count -gt 0
    $live = Read-LiveCounts
    if ($null -eq $live) { return }
    $saved = Read-CountsFile
    if ($running) {
        $same = ($null -ne $saved) -and ($saved.In -eq $live.In) -and ($saved.Out -eq $live.Out) -and ($saved.ShiftKey -eq $live.ShiftKey)
        if (-not $same) {
            Save-CountsFile -Counts $live
            $note = ''
            if ($null -ne $saved -and $saved.ShiftKey -eq $live.ShiftKey -and ($live.In -lt $saved.In -or $live.Out -lt $saved.Out)) { $note = 'went DOWN (reset by the operator or a correction?)' }
            Add-CountsHistory -Live $live -Note $note
        }
        return
    }
    $keyNow = Get-ShiftKeyNow -Now (Get-Date)
    if (-not (Test-ShouldRestoreCounts -Live $live -Saved $saved -KeyNow $keyNow)) { return }
    if ($Mode -eq 'Check' -or -not $script:Fixing) {
        Add-Result 'WARN' 'Counts' ('the database shows 0 / 0 but this shift had IN ' + $saved.In + ' / OUT ' + $saved.Out) 'Run TEETH-AUTO.bat: it puts the counts back before it starts Teeth.'
        return
    }
    if (Write-LiveCounts -Counts $saved -ShiftKey $keyNow) {
        Add-Result 'FIXED' 'Counts' ('the database was 0 / 0 in the same shift: restored IN ' + $saved.In + ' / OUT ' + $saved.Out)
        Add-CountsHistory -Live $saved -Note 'RESTORED by the Doctor'
        Write-Log ('Counts restored: IN ' + $saved.In + ' OUT ' + $saved.Out)
    } else {
        Add-Result 'WARN' 'Counts' ('the database shows 0 / 0 but this shift had IN ' + $saved.In + ' / OUT ' + $saved.Out + ' and they could not be restored') 'Write down these numbers and enter them with Settings > Admin > correction.'
    }
}

function Step-Programs {
    $restart = $true
    if ($script:Cfg.ContainsKey('RestartPrograms') -and ($script:Cfg['RestartPrograms'] -eq 'false')) { $restart = $false }

    if ($script:Role -eq 2) {
        # a leftover stand-in listener (guide step 10/12) blocks the port: stop it
        $own = Step-PortOwner
        if ($own -and ($own.Name -ne 'Teeth')) {
            if (($own.Name -match '^(powershell|pwsh)$') -and $script:Fixing) {
                try { Stop-Process -Id $own.Id -Force -ErrorAction Stop; Add-Result 'FIXED' ('Port ' + $script:Port) 'a leftover test listener (PowerShell) was blocking it: stopped'; Start-Sleep -Seconds 1 }
                catch { Add-Result 'FAIL' ('Port ' + $script:Port) ('used by ' + $own.Name + ' (id ' + $own.Id + ')') 'Close that window, then run this again.' }
            } else {
                Add-Result 'FAIL' ('Port ' + $script:Port) ('used by "' + $own.Name + '" (id ' + $own.Id + '), not by Teeth') 'Close that program, then run this again. Teeth shows "port busy" until then.'
            }
        }
        $t = Get-ProcsByName 'Teeth'
        if ($t.Count -gt 1) { Add-Result 'WARN' 'Teeth.exe running' ('' + $t.Count + ' copies are open') 'Close the extra copy: only one Teeth.exe may run.' }
        elseif ($t.Count -eq 1) { Add-Result 'OK' 'Teeth.exe running' }
        elseif (-not $script:Fixing -or -not $restart) { Add-Result 'FAIL' 'Teeth.exe running' 'not running' 'Double-click Teeth.exe (or run TEETH-AUTO.bat).' }
        else {
            $exe = Join-Path $Root 'Teeth.exe'
            if (Start-AppNormal -Exe $exe -ProcName 'Teeth') { Add-Result 'FIXED' 'Teeth.exe running' 'it was closed: started again' }
            else { Add-Result 'FAIL' 'Teeth.exe running' 'could not start it' 'Double-click Teeth.exe by hand and read the message it shows.' }
        }
        if (Get-ProcsByName 'TeethSender') { Add-Result 'WARN' 'Wrong program' 'TeethSender.exe is running on Laptop 2' 'TeethSender belongs on Laptop 1 only. Close it here.' }
    } else {
        $s = Get-ProcsByName 'TeethSender'
        if ($s.Count -gt 1) { Add-Result 'WARN' 'TeethSender.exe running' ('' + $s.Count + ' copies are open') 'Close the extra copy: only one TeethSender may run.' }
        elseif ($s.Count -eq 1) { Add-Result 'OK' 'TeethSender.exe running' }
        elseif (-not $script:Fixing -or -not $restart) { Add-Result 'FAIL' 'TeethSender.exe running' 'not running: key presses are NOT counted' 'Double-click TeethSender.exe (or run TEETH-AUTO.bat).' }
        else {
            $exe = Join-Path $Root 'TeethSender.exe'
            if (Start-AppNormal -Exe $exe -ProcName 'TeethSender') { Add-Result 'FIXED' 'TeethSender.exe running' 'it was closed: started again' }
            else { Add-Result 'FAIL' 'TeethSender.exe running' 'could not start it' 'Double-click TeethSender.exe by hand and read the message it shows.' }
        }
        if (Get-ProcsByName 'Teeth') { Add-Result 'WARN' 'Wrong program' 'Teeth.exe is running on Laptop 1' 'Teeth.exe belongs on Laptop 2 only. Close it here.' }
    }
}

function Step-Link {
    param($Adapter)
    $port = $script:Port
    if ($script:Role -eq 2) {
        $l = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
        if ($l.Count -gt 0) { Add-Result 'OK' ('Teeth listening on port ' + $port) } else { Add-Result 'FAIL' ('Teeth listening on port ' + $port) 'nothing is listening' 'Teeth.exe must be open. Read the red text at the top of Teeth.' }
        $e = @(Get-NetTCPConnection -LocalPort $port -State Established -ErrorAction SilentlyContinue | Where-Object { $_.RemoteAddress -eq $script:L1Ip })
        if ($e.Count -gt 0) { Add-Result 'OK' 'Laptop 1 connected' ('TeethSender is connected from ' + $script:L1Ip) }
        else { Add-Result 'WARN' 'Laptop 1 connected' 'Laptop 1 is not connected right now' 'On Laptop 1: is it on, is TeethSender open, is the cable in both USB adapters? (TEETH-AUTO.bat on Laptop 1 checks all that.)' }
        return
    }
    # Laptop 1
    $target = $script:Target
    $est = @(Get-NetTCPConnection -RemoteAddress $target -RemotePort $port -State Established -ErrorAction SilentlyContinue)
    if ($est.Count -gt 0) { Add-Result 'OK' ('Link to Laptop 2 (' + $target + ':' + $port + ')') 'TeethSender is connected'; return }
    $wait = if ($Silent) { 6 } else { 25 }
    $okPort = $false
    for ($i = 0; $i -lt $wait; $i += 2) {
        $okPort = Test-TcpPort -Ip $target -Port $port -Ms 1500
        if ($okPort) { break }
        Start-Sleep -Seconds 1
    }
    if ($okPort) {
        Add-Result 'OK' ('Link to Laptop 2 (' + $target + ':' + $port + ')') 'port is open'
        return
    }
    $adNow = $null
    if ($Adapter) { $adNow = @(Get-AdapterInfo | Where-Object { $_.Index -eq $Adapter.Index }) | Select-Object -First 1 }
    if ($adNow -and $adNow.Status -ne 'Up' -and $script:Fixing -and $script:IsAdmin -and -not $Silent) {
        # "Disconnected" is usually the cable, but a stuck USB adapter driver looks the same: switch the adapter off and on once.
        Say '  No cable signal. Trying to switch the USB adapter off and on once ...' 'Gray'
        try { Restart-NetAdapter -Name $adNow.Name -Confirm:$false -ErrorAction Stop } catch { Write-Log ('Restart-NetAdapter failed: ' + $_.Exception.Message) }
        for ($k = 0; $k -lt 12; $k++) {
            Start-Sleep -Seconds 1
            $adNow = @(Get-AdapterInfo | Where-Object { $_.Index -eq $Adapter.Index }) | Select-Object -First 1
            if ($adNow -and $adNow.Status -eq 'Up') { break }
        }
        if ($adNow -and $adNow.Status -eq 'Up') {
            Start-Sleep -Seconds 3
            if (Test-TcpPort -Ip $target -Port $port -Ms 2500) {
                Add-Result 'FIXED' ('Link to Laptop 2 (' + $target + ':' + $port + ')') 'USB adapter switched off and on: the cable signal is back'
                return
            }
        }
    }
    $ping = Test-Ping -Ip $target
    if ($adNow -and $adNow.Status -ne 'Up') {
        Add-Result 'FAIL' 'Link to Laptop 2' ('USB adapter is "' + $adNow.Status + '": no cable signal') 'Is Laptop 2 switched on? Look at the lights on BOTH USB adapters: no light = no link. Plug the cable into the two USB adapters (not the built-in ports). Try another cable, then another USB port on this PC. Test: put this PC cable + adapter on a PC that works.'
    } elseif ($ping) {
        Add-Result 'FAIL' 'Link to Laptop 2' ('Laptop 2 answers ping but Teeth is not listening on port ' + $port) 'On Laptop 2 double-click TEETH-AUTO.bat: it starts Teeth and opens the firewall.'
    } else {
        Add-Result 'FAIL' 'Link to Laptop 2' ('no contact with ' + $target) 'On Laptop 2 double-click TEETH-AUTO.bat (it checks the address, firewall and Teeth). Then check the cable.'
    }
}

function Step-Counts {
    if ($script:Role -ne 1) { return }
    $f = Join-Path $Root 'pending.txt'
    if (Test-Path -LiteralPath $f) {
        $n = @(Get-Content -LiteralPath $f -ErrorAction SilentlyContinue | Where-Object { $_ -and $_.Trim() -ne '' }).Count
        if ($n -gt 0) { Add-Result 'INFO' 'IN counts waiting' ($n + ' count(s) wait in pending.txt; they are sent when the link is up') }
        else { Add-Result 'OK' 'IN counts waiting' 'none' }
    }
}

function Step-Clock {
    $tz = ''
    try { $tz = (Get-TimeZone).Id } catch { }
    Add-Result 'INFO' 'Clock' ((Get-Date -Format 'yyyy-MM-dd HH:mm') + ', time zone ' + $tz + $(if ($script:Role -eq 2) { ' (the shifts follow THIS clock: check it weekly)' } else { '' }))
    if (-not $script:Cfg.ContainsKey('TimeZone') -and -not $Silent -and $Mode -eq 'Fix') { $script:Cfg['TimeZone'] = 'SE Asia Standard Time'; Save-Cfg }
    $want = ''
    if ($script:Cfg.ContainsKey('TimeZone')) { $want = [string]$script:Cfg['TimeZone'] }
    if (-not $want -or -not $tz -or $tz -eq $want) { return }
    $done = $false
    if (-not $Silent -and $script:Fixing -and $script:IsAdmin) {
        $q = '  This PC time zone is "' + $tz + '" but Teeth expects "' + $want + '". Change the time zone and KEEP the time on the clock as it is now (' + (Get-Date -Format 'HH:mm') + ')?'
        if (Ask-YesNo $q $true) {
            try {
                $keep = Get-Date
                Set-TimeZone -Id $want -ErrorAction Stop
                Set-Date -Date $keep | Out-Null
                $done = $true
            } catch { Write-Log ('Set-TimeZone failed: ' + $_.Exception.Message) }
        }
    }
    if ($done) { Add-Result 'FIXED' 'Time zone' ('now "' + $want + '", clock kept at ' + (Get-Date -Format 'HH:mm')) }
    else { Add-Result 'WARN' 'Time zone' ('"' + $tz + '" but Teeth expects "' + $want + '"') 'Run TEETH-AUTO.bat and answer Y, or fix it in Windows: Settings > Time and language. Check the clock shows the right local time.' }
}

function Step-Tasks {
    $vbs = Join-Path $ToolsDir 'Teeth-Doctor-Hidden.vbs'
    $existing = @(Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)
    $good = $false
    if ($existing.Count -ge 1) {
        $arg = ''
        try { $arg = [string]$existing[0].Actions[0].Arguments } catch { }
        $good = ($arg -like ('*' + $vbs + '*')) -and ([string]$existing[0].State -ne 'Disabled')
    }
    if ($good) { Add-Result 'OK' 'Automatic checking' ('scheduled task "' + $TaskName + '" is present'); return }
    if (-not $script:Fixing) { Add-Result 'WARN' 'Automatic checking' 'scheduled task is not set up' 'Run TEETH-AUTO.bat.'; return }
    if (-not $script:IsAdmin) { Add-Result 'WARN' 'Automatic checking' 'administrator rights needed to create the scheduled task' 'Double-click TEETH-AUTO.bat.'; return }
    if (-not (Test-Path -LiteralPath $vbs)) { Add-Result 'FAIL' 'Automatic checking' 'Teeth-Doctor-Hidden.vbs is missing' 'Copy the WHOLE Teeth folder from the zip.'; return }
    try {
        $user = ''
        try { $user = [string](Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).UserName } catch { }
        if (-not $user) { $user = $env:USERDOMAIN + '\' + $env:USERNAME }
        $every = 5
        if ($script:Cfg.ContainsKey('WatchEveryMinutes')) { $tmp = 0; if ([int]::TryParse($script:Cfg['WatchEveryMinutes'], [ref]$tmp) -and $tmp -ge 1) { $every = $tmp } }
        $action = New-ScheduledTaskAction -Execute 'wscript.exe' -Argument ('"' + $vbs + '"')
        $t1 = New-ScheduledTaskTrigger -AtLogOn -User $user
        $t2 = New-ScheduledTaskTrigger -Once -At ((Get-Date).Date) -RepetitionInterval (New-TimeSpan -Minutes $every) -RepetitionDuration (New-TimeSpan -Days 3650)
        $pr = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
        $se = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
        Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger @($t1, $t2) -Principal $pr -Settings $se -Force -ErrorAction Stop | Out-Null
        Add-Result 'FIXED' 'Automatic checking' ('scheduled task "' + $TaskName + '": at every log-on and every ' + $every + ' minutes, no window, no question')
    } catch {
        Add-Result 'WARN' 'Automatic checking' ('could not create the scheduled task: ' + $_.Exception.Message) 'The manual TEETH-AUTO.bat click still works.'
    }
}

function Step-Undo {
    Say ''
    Say '  Removing what Teeth-Doctor added ...' 'Cyan'
    if (-not $script:IsAdmin) { Add-Result 'FAIL' 'Undo' 'administrator rights needed' 'Double-click TEETH-UNDO.bat.'; return }
    try {
        if (@(Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue).Count -gt 0) {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
            Add-Result 'FIXED' 'Scheduled task' 'removed'
        } else { Add-Result 'OK' 'Scheduled task' 'was not there' }
    } catch { Add-Result 'WARN' 'Scheduled task' $_.Exception.Message }
    foreach ($n in @('Teeth link', 'Teeth ping')) {
        try {
            if (@(Get-NetFirewallRule -DisplayName $n -ErrorAction SilentlyContinue).Count -gt 0) {
                Get-NetFirewallRule -DisplayName $n | Remove-NetFirewallRule -ErrorAction Stop
                Add-Result 'FIXED' ('Firewall rule ' + $n) 'removed'
            }
        } catch { Add-Result 'WARN' ('Firewall rule ' + $n) $_.Exception.Message }
    }
    try {
        $ad = @(Get-AdapterInfo)
        $mac = if ($script:Cfg.ContainsKey('AdapterMac')) { ConvertTo-NormalMac $script:Cfg['AdapterMac'] } else { '' }
        $mine = @($ad | Where-Object { $mac -ne '' -and (ConvertTo-NormalMac $_.Mac) -eq $mac })
        foreach ($m in $mine) {
            $safe = Test-AdapterSafe -Adapter $m -Prefix $script:Prefix
            if ($safe.Safe) {
                foreach ($ip in @($m.Ipv4)) { if (Test-IpInPrefix -Ip $ip -Prefix $script:Prefix) { Remove-NetIPAddress -InterfaceIndex $m.Index -IPAddress $ip -Confirm:$false -ErrorAction SilentlyContinue } }
                Set-NetIPInterface -InterfaceIndex $m.Index -AddressFamily IPv4 -Dhcp Enabled -ErrorAction SilentlyContinue
                Add-Result 'FIXED' 'USB adapter' ('"' + $m.Name + '" back to automatic address (DHCP)')
            }
        }
    } catch { Add-Result 'WARN' 'USB adapter' $_.Exception.Message }
    try {
        $sub = '2a737441-1930-4402-8d77-b2bebba308a3'; $set = '48e6b7a6-50f5-4782-a5d4-53bb8f07e226'
        & powercfg /setacvalueindex SCHEME_CURRENT $sub $set 1 | Out-Null
        & powercfg /setdcvalueindex SCHEME_CURRENT $sub $set 1 | Out-Null
        & powercfg /setactive SCHEME_CURRENT | Out-Null
        Add-Result 'FIXED' 'USB selective suspend' 'back to the Windows default (on)'
    } catch { }
    if (Test-Path -LiteralPath $CfgPath) { Remove-Item -LiteralPath $CfgPath -Force -ErrorAction SilentlyContinue; Add-Result 'FIXED' 'Settings file' 'teeth-doctor.ini removed' }
    Say ''
    Say '  Not touched: Teeth.exe, TeethSender.exe, Teeth.sqlite, Log Data, sender.ini, pending.txt, the built-in Ethernet port, the print software.' 'Gray'
}

function Write-Status {
    param([string]$Overall)
    try {
        if (-not (Test-Path -LiteralPath $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
        $lines = @('Teeth Doctor - last run ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  mode=' + $Mode + $(if ($Silent) { ' (automatic)' } else { ' (by hand)' }) + '  ' + $script:DoctorVersion,
                   'Laptop role: ' + $script:Role + '   Overall: ' + $Overall, '')
        foreach ($r in $script:Results) {
            $lines += ('[' + $r.Status + '] ' + $r.Name + $(if ($r.Detail) { ' - ' + $r.Detail } else { '' }))
            if ($r.Todo -and ($r.Status -eq 'FAIL' -or $r.Status -eq 'WARN')) { $lines += ('      -> ' + $r.Todo) }
        }
        Set-Content -LiteralPath (Join-Path $LogDir 'LAST-STATUS.txt') -Value $lines -Encoding ASCII
    } catch { }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

$script:mutex = $null
$script:haveMutex = $false
try {
    $script:mutex = New-Object System.Threading.Mutex($false, 'Global\TeethDoctorMutex')
    try { $script:haveMutex = $script:mutex.WaitOne($(if ($Silent) { 0 } else { 30000 })) } catch [System.Threading.AbandonedMutexException] { $script:haveMutex = $true }
} catch { $script:haveMutex = $true }
if (-not $script:haveMutex) { if (-not $Silent) { Write-Host 'Another Teeth Doctor run is busy. Try again in a minute.' -ForegroundColor Yellow }; exit 0 }

$exitCode = 0
try {
    $script:IsAdmin = Test-Admin
    $script:Cfg = Read-IniFile -Path $CfgPath
    $script:L1Ip = if ($script:Cfg.ContainsKey('Laptop1Ip')) { $script:Cfg['Laptop1Ip'] } else { '192.168.50.1' }
    $script:L2Ip = if ($script:Cfg.ContainsKey('Laptop2Ip')) { $script:Cfg['Laptop2Ip'] } else { '192.168.50.2' }
    $script:PrefixLen = 24
    if ($script:Cfg.ContainsKey('PrefixLength')) { $tmpP = 0; if ([int]::TryParse($script:Cfg['PrefixLength'], [ref]$tmpP) -and $tmpP -ge 8 -and $tmpP -le 30) { $script:PrefixLen = $tmpP } }
    $script:Prefix = Get-RangePrefix -Ip $script:L2Ip
    $script:Role = 0

    # Mode resolution
    $effective = $Mode
    if ($Mode -eq 'Auto') {
        $hasRole = $script:Cfg.ContainsKey('Role')
        $hasTask = (@(Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)).Count -gt 0
        $effective = if ($hasRole -and $hasTask) { 'Fix' } else { 'Setup' }
    }
    $Mode = $effective
    $script:Fixing = ($Mode -eq 'Setup' -or $Mode -eq 'Fix')

    if (-not $Silent) {
        Clear-Host
        Write-Host ''
        Write-Host '  ==================================================' -ForegroundColor DarkCyan
        Write-Host '   TEETH - automatic setup and fix' -ForegroundColor White
        Write-Host ('   ' + $script:DoctorVersion + '   mode: ' + $Mode) -ForegroundColor Gray
        Write-Host '  ==================================================' -ForegroundColor DarkCyan
        Write-Host ''
        if ($Mode -eq 'Setup') { Write-Host '  First time on this laptop. This takes about a minute. Nothing to type except maybe 1 or 2.' -ForegroundColor Gray }
        if ($Mode -eq 'Check') { Write-Host '  Check only: nothing will be changed.' -ForegroundColor Gray }
        Write-Host ''
    }

    if ($Mode -eq 'Undo') {
        [void](Step-Role)
        if ($script:Role -eq 0) { $script:Role = 1 }
        Step-Undo
    } else {
        if ($script:Fixing -and -not $script:IsAdmin -and -not $Silent) {
            Say '  Note: this window has no administrator rights, so some repairs are not possible.' 'Yellow'
        }
        if (-not (Step-Role)) { throw 'norole' }
        if ($Mode -eq 'Setup' -and $script:IsAdmin) {
            $moved = Step-InstallCopy
            if ($moved) { exit 0 }
        }
        if ($Mode -eq 'Setup') {
            Step-RestorePoints
            Step-Unblock
        }
        $problems = Step-Folder
        $ad = Step-Adapter
        Step-UsbSuspend
        if ($script:Role -eq 2) {
            $script:Port = 5055
            $lp = Read-IniFile -Path (Join-Path $AppFiles 'link.ini')
            if ($lp.ContainsKey('Port')) { $pp = 0; if ([int]::TryParse($lp['Port'], [ref]$pp) -and $pp -gt 0) { $script:Port = $pp } }
            Step-Firewall
        } else {
            $script:Port = 5055
            $script:Target = $script:L2Ip
            $sp = Read-IniFile -Path (Join-Path $Root 'sender.ini')
            if ($sp.ContainsKey('Port')) { $pp = 0; if ([int]::TryParse($sp['Port'], [ref]$pp) -and $pp -gt 0) { $script:Port = $pp } }
            if ($sp.ContainsKey('Host') -and $sp['Host']) { $script:Target = $sp['Host'] }
        }
        Step-CountsGuard
        Step-Programs
        if ($script:Fixing -and $script:Changed) { Start-Sleep -Seconds 2 }
        Step-Link -Adapter $ad
        Step-Counts
        Step-Sleep
        Step-Clock
        if ($Mode -eq 'Setup' -or $Mode -eq 'Fix') { Step-Tasks }
        if ($Mode -eq 'Setup') {
            if (-not $script:Cfg.ContainsKey('Laptop1Ip')) { $script:Cfg['Laptop1Ip'] = $script:L1Ip }
            if (-not $script:Cfg.ContainsKey('Laptop2Ip')) { $script:Cfg['Laptop2Ip'] = $script:L2Ip }
            if (-not $script:Cfg.ContainsKey('PrefixLength')) { $script:Cfg['PrefixLength'] = [string]$script:PrefixLen }
            if (-not $script:Cfg.ContainsKey('RestartPrograms')) { $script:Cfg['RestartPrograms'] = 'true' }
            if (-not $script:Cfg.ContainsKey('TimeZone')) { $script:Cfg['TimeZone'] = 'SE Asia Standard Time' }
            if (-not $script:Cfg.ContainsKey('WatchEveryMinutes')) { $script:Cfg['WatchEveryMinutes'] = '5' }
            Save-Cfg
        }
    }

    $overall = Get-OverallResult -Results $script:Results
    Write-Status -Overall $overall
    if ($overall -eq 'RED') { $exitCode = 1 }

    if (-not $Silent) {
        Write-Host ''
        Write-Host '  ==================================================' -ForegroundColor DarkCyan
        $laptop = if ($script:Role -eq 1) { 'LAPTOP 1' } elseif ($script:Role -eq 2) { 'LAPTOP 2' } else { 'this laptop' }
        if ($Mode -eq 'Undo') {
            Write-Host '   UNDO DONE.' -ForegroundColor Green
        } elseif ($overall -eq 'GREEN') {
            Write-Host ('   GREEN - ' + $laptop + ' is ready. Nothing more to do.') -ForegroundColor Green
            if ($script:Role -eq 1) { Write-Host '   Press the key button: Result IN goes up on Laptop 2.' -ForegroundColor Green }
            else { Write-Host '   Scan an item: Result OUT goes up. IN comes from Laptop 1.' -ForegroundColor Green }
        } elseif ($overall -eq 'YELLOW') {
            Write-Host ('   YELLOW - ' + $laptop + ' works, but read the [NOTE] lines above.') -ForegroundColor Yellow
        } else {
            $n = @($script:Results | Where-Object { $_.Status -eq 'FAIL' }).Count
            Write-Host ('   RED - ' + $n + ' problem(s) the program could not fix by itself:') -ForegroundColor Red
            foreach ($r in $script:Results) { if ($r.Status -eq 'FAIL') { Write-Host ('     * ' + $r.Name + ': ' + $r.Todo) -ForegroundColor Red } }
            Write-Host ''
            Write-Host '   Send the helper the file: App Files\Logs\LAST-STATUS.txt' -ForegroundColor Yellow
        }
        Write-Host '  ==================================================' -ForegroundColor DarkCyan
        Write-Host ''
    }
}
catch {
    if ($_.Exception.Message -ne 'norole') {
        Write-Log ('Doctor error: ' + $_.Exception.Message)
        Say ('  The Doctor stopped with an error: ' + $_.Exception.Message) 'Red'
        Say '  Send the helper the file: App Files\Logs\Teeth-Doctor-log.txt' 'Yellow'
    }
    $exitCode = 1
}
finally {
    if ($script:mutex -and $script:haveMutex) { try { $script:mutex.ReleaseMutex() } catch { } }
}
exit $exitCode
