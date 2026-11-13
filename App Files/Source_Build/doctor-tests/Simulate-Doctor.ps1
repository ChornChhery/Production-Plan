# Simulator for Teeth-Doctor.ps1: runs the REAL script against PRETEND Windows commands (every command that changes the PC is replaced
# by a function that only writes to a log). For developers and for Test-Doctor-Scenarios.ps1. Not needed to use Teeth.
# It still creates temp folders and a loopback listener, so it is NOT started by Run-Doctor-SelfTest.bat.
param([string]$Scenario = 'L2', [string]$Mode = 'Fix', [switch]$Silent)
$ErrorActionPreference = 'Stop'
$tmp = [System.IO.Path]::GetTempPath()
$root = Join-Path $tmp ('teethsim_' + $Scenario + '_' + $Mode)
Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
$tools = Join-Path (Join-Path $root 'App Files') 'Tools'
New-Item -ItemType Directory -Path $tools -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path (Join-Path (Join-Path $root 'App Files') 'lib') 'x64') -Force | Out-Null
foreach ($f in 'System.Data.SQLite.dll','ClosedXML.dll','DocumentFormat.OpenXml.dll') { Set-Content (Join-Path (Join-Path (Join-Path $root 'App Files') 'lib') $f) 'x' }
Set-Content (Join-Path (Join-Path (Join-Path (Join-Path $root 'App Files') 'lib') 'x64') 'SQLite.Interop.dll') 'x'
Set-Content (Join-Path $root 'Teeth.exe') 'x'; Set-Content (Join-Path $root 'TeethSender.exe') 'x'
Set-Content (Join-Path $tools 'Teeth-Doctor-Hidden.vbs') 'x'
$doctorPath = Join-Path (Join-Path (Join-Path $PSScriptRoot '..') '..') 'Tools'
$src = Get-Content (Join-Path $doctorPath 'Teeth-Doctor.ps1') -Raw
$src = $src.Replace("function Test-Admin {", "function Test-Admin { return `$true }`nfunction Test-AdminOLD {").Replace("`$target = 'C:\Teeth'", "`$target = `$Root").Replace("function Read-LiveCounts {", "function Read-LiveCounts { return `$global:SimLive }`nfunction Read-LiveCountsOLD {").Replace("function Write-LiveCounts {", "function Write-LiveCounts { param(`$Counts,[string]`$ShiftKey) `$global:M.Log += ('Write-LiveCounts IN ' + `$Counts.In + ' OUT ' + `$Counts.Out + ' key ' + `$ShiftKey); `$global:SimLive = [pscustomobject]@{ In = `$Counts.In; Out = `$Counts.Out; Frozen = 0.0; ShiftKey = `$ShiftKey }; return `$true }`nfunction Write-LiveCountsOLD {")
Set-Content (Join-Path $tools 'Teeth-Doctor.ps1') $src
$env:USERPROFILE = Join-Path $tmp 'teethsim_user'; $env:WINDIR = Join-Path $tmp 'teethsim_win'; New-Item -ItemType Directory -Path $env:USERPROFILE -Force | Out-Null
$role = if ($Scenario -like 'L1*') { '1' } else { '2' }
if ($Mode -ne 'Setup') { Set-Content (Join-Path (Join-Path $root 'App Files') 'teeth-doctor.ini') @("Role=$role") }
$port = 55055
if ($role -eq '1') { Set-Content (Join-Path $root 'sender.ini') @(('Host=' + $(if ($Scenario -like '*unreach*') { '192.0.2.1' } else { '127.0.0.1' })), "Port=$port") } else { Set-Content (Join-Path (Join-Path $root 'App Files') 'link.ini') @("Port=$port") }

$global:M = @{ Suspend = $true; Teeth = $false; Sender = $false; Rules = @{}; Ips = @(); Dhcp = $true; Powersave = $true; Tasks = @{}; Log = @(); Listener = $false }
$mac = '00-E0-4C-68-01-AB'
function New-Nic($n,$i,$m,$pnp,$st,$desc) { [pscustomobject]@{ Name=$n; ifIndex=$i; MacAddress=$m; InterfaceDescription=$desc; PnPDeviceID=$pnp; Status=$st; PhysicalMediaType='802.3'; ComponentID='x' } }
$global:Nics = @( (New-Nic 'Ethernet' 3 'AA-11-22-33-44-55' 'PCI\VEN_8086' 'Up' 'Intel I219'),
                  (New-Nic 'Ethernet 4' 9 $mac 'USB\VID_0BDA&PID_8153\000001' 'Up' 'Realtek USB GbE'),
                  (New-Nic 'Wi-Fi' 5 'AA-11-22-33-44-66' 'PCI\VEN_8086&WIFI' 'Up' 'Intel Wireless-AC') )
$global:Nics[1].Status = 'Up'
if ($Scenario -like '*unplugged*') { $global:Nics[1].Status = 'Disconnected' }
if ($Scenario -like '*noadapter') { $global:Nics = @($global:Nics[0], $global:Nics[2]) }
$global:M.Ips = @( @{ If = 3; Ip = '192.168.0.97'; Pl = 22 }, @{ If = 5; Ip = '10.98.101.89'; Pl = 24 }, @{ If = 9; Ip = '169.254.12.4'; Pl = 16 } )
if ($Scenario -like '*conflict') { $global:M.Ips += @{ If = 3; Ip = '192.168.50.77'; Pl = 24 } }

function Get-NetAdapter { [CmdletBinding()] param([switch]$Physical,[switch]$IncludeHidden) $global:Nics }
function Get-NetIPAddress { [CmdletBinding()] param($InterfaceIndex,$AddressFamily,$IPAddress)
  $r = @(); foreach ($a in $global:M.Ips) { if ($null -ne $InterfaceIndex -and $a.If -ne $InterfaceIndex) { continue }; if ($IPAddress -and $a.Ip -ne $IPAddress) { continue }
    $nic = $global:Nics | Where-Object { $_.ifIndex -eq $a.If }; $r += [pscustomobject]@{ IPAddress=$a.Ip; PrefixLength=$a.Pl; InterfaceIndex=$a.If; InterfaceAlias=$(if($nic){$nic.Name}else{'x'}) } }
  if ($r.Count -eq 0) { return } ; $r }
function Get-NetRoute { [CmdletBinding()] param($InterfaceIndex,$DestinationPrefix) if ($InterfaceIndex -eq 3 -or ($Scenario -like '*usbgateway' -and $InterfaceIndex -eq 9)) { [pscustomobject]@{x=1} } }
function Set-NetIPInterface { [CmdletBinding()] param($InterfaceIndex,$AddressFamily,$Dhcp) $global:M.Log += "Set-NetIPInterface $InterfaceIndex Dhcp=$Dhcp" }
function Remove-NetIPAddress { [CmdletBinding()] param($InterfaceIndex,$IPAddress,[switch]$Confirm) $global:M.Ips = @($global:M.Ips | Where-Object { -not ($_.If -eq $InterfaceIndex -and $_.Ip -eq $IPAddress) }); $global:M.Log += "Remove-NetIPAddress $IPAddress" }
function New-NetIPAddress { [CmdletBinding()] param($InterfaceIndex,$IPAddress,$PrefixLength) $global:M.Ips += @{ If=$InterfaceIndex; Ip=$IPAddress; Pl=$PrefixLength }; $global:M.Log += "New-NetIPAddress $IPAddress/$PrefixLength (no gateway)" }
function Set-DnsClientServerAddress { [CmdletBinding()] param($InterfaceIndex,[switch]$ResetServerAddresses) }
function Enable-NetAdapter { [CmdletBinding()] param($Name,[switch]$Confirm) }
function Get-NetAdapterAdvancedProperty { [CmdletBinding()] param($Name) [pscustomobject]@{ DisplayName='Energy Efficient Ethernet'; DisplayValue='Enabled'; ValidDisplayValues=@('Enabled','Disabled') } }
function Set-NetAdapterAdvancedProperty { [CmdletBinding()] param($Name,$DisplayName,$DisplayValue) $global:M.Log += "EEE -> $DisplayValue" }
function Get-CimInstance { [CmdletBinding()] param($ClassName,$Namespace)
  if ($ClassName -eq 'Win32_ComputerSystem') { return [pscustomobject]@{ UserName='SIM\operator' } }
  if ($ClassName -eq 'MSPower_DeviceEnable') { return [pscustomobject]@{ InstanceName='USB\VID_0BDA&PID_8153\000001_0'; Enable=$global:M.Powersave } } }
function Set-CimInstance { [CmdletBinding()] param($InputObject,$Property) $global:M.Powersave = $false; $global:M.Log += 'power saving off' }
function Get-NetFirewallRule { [CmdletBinding()] param($DisplayName) if ($global:M.Rules.ContainsKey($DisplayName)) { [pscustomobject]@{ DisplayName=$DisplayName; Enabled='True'; Action='Allow'; Direction='Inbound' } } }
function Get-NetFirewallPortFilter { [CmdletBinding()] param($AssociatedNetFirewallRule) [pscustomobject]@{ LocalPort = [string]$global:M.Rules[$AssociatedNetFirewallRule.DisplayName] } }
function New-NetFirewallRule { [CmdletBinding()] param($DisplayName,$Direction,$Action,$Protocol,$LocalPort,$IcmpType,$Profile,$RemoteAddress) $global:M.Rules[$DisplayName] = $LocalPort; $global:M.Log += "New-NetFirewallRule '$DisplayName' $Protocol $LocalPort from $RemoteAddress" }
function Remove-NetFirewallRule { [CmdletBinding()] param([Parameter(ValueFromPipeline)]$InputObject,$DisplayName) process { if ($InputObject) { $global:M.Rules.Remove($InputObject.DisplayName) } } }
function Get-NetTCPConnection { [CmdletBinding()] param($LocalPort,$RemotePort,$RemoteAddress,$State)
  if ($LocalPort -eq $port -and $State -eq 'Listen' -and ($global:M.Teeth -or $global:M.Listener)) { return [pscustomobject]@{ OwningProcess = $(if($global:M.Listener){777}else{101}) } } }
function Get-Process { [CmdletBinding()] param($Name,$Id)
  if ($Id -eq 777) { return [pscustomobject]@{ ProcessName='powershell'; Id=777 } }
  if ($Id -eq 101) { return [pscustomobject]@{ ProcessName='Teeth'; Id=101 } }
  if ($Name -eq 'Teeth' -and $global:M.Teeth) { return [pscustomobject]@{ ProcessName='Teeth' } }
  if ($Name -eq 'TeethSender' -and $global:M.Sender) { return [pscustomobject]@{ ProcessName='TeethSender' } }
  if ($Name -eq 'explorer') { return [pscustomobject]@{ ProcessName='explorer' } } }
function Stop-Process { [CmdletBinding()] param($Id,[switch]$Force) $global:M.Listener = $false; $global:M.Log += "Stop-Process $Id" }
function Start-Process { [CmdletBinding()] param($FilePath,$ArgumentList,$WorkingDirectory,[switch]$Wait,[switch]$NoNewWindow,[switch]$PassThru)
  if ($FilePath -like '*ndp48*') { $global:M.Log += 'ran .NET 4.8 installer ' + $ArgumentList; $global:NetRel = 533320; return [pscustomobject]@{ ExitCode = 0 } }
  if ($ArgumentList -like '*Teeth.exe*') { $global:M.Teeth = $true; $global:M.Log += 'started Teeth.exe' }
  if ($ArgumentList -like '*TeethSender.exe*') { $global:M.Sender = $true; $global:M.Log += 'started TeethSender.exe' } }
function Start-Sleep { [CmdletBinding()] param($Seconds,$Milliseconds) }
function Clear-Host { }
$global:SimRole = $role
function Read-Host { param($Prompt) if ($Prompt -like '*(Y/n)*') { Write-Host "   [simulated answer to '$Prompt': y]"; return 'y' }
  Write-Host "   [simulated answer to '$Prompt': $($global:SimRole)]"; return $global:SimRole }
$global:NetRel = 533320
function Get-ItemProperty { [CmdletBinding()] param($Path,$Name) [pscustomobject]@{ Release = $global:NetRel } }
function Get-AuthenticodeSignature { [CmdletBinding()] param($LiteralPath) [pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Subject = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond' } } }
function Set-TimeZone { [CmdletBinding()] param($Id) $global:Tz = $Id; $global:M.Log += "Set-TimeZone $Id" }
function Set-Date { [CmdletBinding()] param($Date) $global:M.Log += 'Set-Date (clock time kept)' }
function Unblock-File { [CmdletBinding()] param([Parameter(ValueFromPipeline)]$InputObject) process { } }
function powercfg { $a = $args -join ' '; if ($a -like '*setac*' -or $a -like '*setdc*' -or $a -like '*setactive*') { $global:M.Log += "powercfg $a"; if ($a -like '*setacvalueindex*') { $global:M.Suspend = ($a -match ' 1$') }; return }
  if ($a -like '*STANDBYIDLE*') { return @('  Minimum Possible Setting: 0x00000000','  Current AC Power Setting Index: 0x00000708','  Current DC Power Setting Index: 0x00000384') }
  $v = if ($global:M.Suspend) { '0x00000001' } else { '0x00000000' }
  return @('  Minimum Possible Setting: 0x00000000','  Maximum Possible Setting: 0xffffffff',"  Current AC Power Setting Index: $v","  Current DC Power Setting Index: $v") }
function ipconfig { 'Windows IP Configuration (simulated)' }
function netsh { 'Ok.' }
function robocopy { $global:LASTEXITCODE = 1 }
function Get-ScheduledTask { [CmdletBinding()] param($TaskName) if ($global:M.Tasks.ContainsKey($TaskName)) { $global:M.Tasks[$TaskName] } }
function New-ScheduledTaskAction { param($Execute,$Argument) [pscustomobject]@{ Execute=$Execute; Arguments=$Argument } }
function New-ScheduledTaskTrigger { param([switch]$AtLogOn,[switch]$Once,$User,$At,$RepetitionInterval,$RepetitionDuration) [pscustomobject]@{ t = $(if($AtLogOn){'logon'}else{"every $($RepetitionInterval.TotalMinutes) min"}) } }
function New-ScheduledTaskPrincipal { param($UserId,$LogonType,$RunLevel) [pscustomobject]@{ UserId=$UserId; RunLevel=$RunLevel } }
function New-ScheduledTaskSettingsSet { param([switch]$AllowStartIfOnBatteries,[switch]$DontStopIfGoingOnBatteries,[switch]$StartWhenAvailable,$MultipleInstances,$ExecutionTimeLimit) 'settings' }
function Register-ScheduledTask { [CmdletBinding()] param($TaskName,$Action,$Trigger,$Principal,$Settings,[switch]$Force)
  $global:M.Tasks[$TaskName] = [pscustomobject]@{ State='Ready'; Actions=@($Action) }; $global:M.Log += "Register-ScheduledTask '$TaskName' triggers: $((@($Trigger)|%{$_.t}) -join ', ') user=$($Principal.UserId) runlevel=$($Principal.RunLevel)" }
function Unregister-ScheduledTask { [CmdletBinding()] param($TaskName,[switch]$Confirm) $global:M.Tasks.Remove($TaskName) | Out-Null; $global:M.Log += 'task removed' }
$global:Tz = 'SE Asia Standard Time'
function Get-TimeZone { [pscustomobject]@{ Id = $global:Tz } }

# scenario preconditions
$env:ProgramData = Join-Path $tmp ('teethsim_pd_' + $Scenario + '_' + $Mode); Remove-Item $env:ProgramData -Recurse -Force -ErrorAction SilentlyContinue
$global:SimLive = $null
if ($Scenario -like 'L2-lost*' -or $Scenario -like 'L2-reset*') {
  $nowKey = $(if ((Get-Date).Hour -ge 8 -and (Get-Date).Hour -lt 20) { (Get-Date).Date.AddHours(8) } elseif ((Get-Date).Hour -lt 8) { (Get-Date).Date.AddDays(-1).AddHours(20) } else { (Get-Date).Date.AddHours(20) }).ToString('yyyy-MM-dd HH:mm', [System.Globalization.CultureInfo]::InvariantCulture)
  $global:SimLive = [pscustomobject]@{ In = 0; Out = 0; Frozen = 0.0; ShiftKey = $nowKey }
  New-Item -ItemType Directory -Path (Join-Path $env:ProgramData 'Teeth') -Force | Out-Null
  $sk = $(if ($Scenario -like 'L2-reset*') { '1999-01-01 08:00' } else { $nowKey })
  Set-Content (Join-Path (Join-Path $env:ProgramData 'Teeth') 'counts-latest.txt') @('In=1234','Out=1100','Frozen=0',('ShiftKey=' + $sk),'Saved=x')
}
if ($Scenario -like '*net47') { $global:NetRel = 460805; Set-Content (Join-Path $root 'ndp48-x86-x64-allos-enu.exe') 'x' }
if ($Scenario -like '*tz') { $global:Tz = 'China Standard Time'; Add-Content (Join-Path (Join-Path $root 'App Files') 'teeth-doctor.ini') 'TimeZone=SE Asia Standard Time' }
if ($Scenario -like '*ok') { $global:M.Suspend = $false }
if ($Scenario -like '*listener') { $global:M.Listener = $true }
if ($Scenario -like 'L1*') { $global:M.Sender = $false; if ($Scenario -notlike '*unreach*') { $global:L = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port); $global:L.Start() } }
if ($Scenario -eq 'L2-rules-wrong') { $global:M.Rules['Teeth link'] = 9999 }
if ($Scenario -like 'L2*' -and $Scenario -notlike '*ok') { }
if ($Scenario -like '*ok') { $global:M.Teeth = $true; $global:M.Sender = $true; $global:M.Rules = @{ 'Teeth link' = $port; 'Teeth ping' = 1 }
   $global:M.Ips = @($global:M.Ips | Where-Object { $_.If -ne 9 }); $global:M.Ips += @{ If = 9; Ip = $(if($role -eq '1'){'192.168.50.1'}else{'192.168.50.2'}); Pl = 24 }; $global:M.Powersave = $false; $global:M.Tasks['Teeth Doctor'] = [pscustomobject]@{ State='Ready'; Actions=@([pscustomobject]@{ Arguments = '"' + (Join-Path $tools 'Teeth-Doctor-Hidden.vbs') + '"' }) } }

$ps = Join-Path $tools 'Teeth-Doctor.ps1'
$args2 = @{ Mode = $Mode }; if ($Silent) { $args2['Silent'] = $true }
try { & $ps @args2; if ($env:SIM_THEN) { Write-Host '=============== SECOND RUN (Fix, same simulated PC) ==============='; $global:M.Log = @(); & $ps -Mode Fix } } catch { Write-Host "SCRIPT THREW: $($_.Exception.Message)" -ForegroundColor Magenta; Write-Host $_.ScriptStackTrace }
Write-Host "`n--- exit code: $LASTEXITCODE"
Write-Host '--- what the simulated Windows was asked to change:'
$global:M.Log | ForEach-Object { Write-Host "    $_" }
if (Test-Path (Join-Path (Join-Path $root 'App Files') 'Logs/LAST-STATUS.txt')) { Write-Host '--- LAST-STATUS.txt written' }
if ($global:L) { $global:L.Stop() }
