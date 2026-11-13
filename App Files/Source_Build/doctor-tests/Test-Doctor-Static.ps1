# Static checks for the Teeth Doctor files. Reads files only, changes nothing.
# Run on the real PC with the built-in Windows PowerShell 5.1 (Run-Doctor-SelfTest.bat): the parser check then proves
# that the script is valid for THAT PowerShell, which is the most common reason for "it works here but not on the PC".
$ErrorActionPreference = 'Stop'
$root = Join-Path (Join-Path (Join-Path $PSScriptRoot '..') '..') '..'
$root = [System.IO.Path]::GetFullPath($root)
$tools = Join-Path (Join-Path $root 'App Files') 'Tools'
$doctor = Join-Path $tools 'Teeth-Doctor.ps1'
$files = @($doctor, (Join-Path $tools 'Teeth-Doctor-Hidden.vbs'), (Join-Path $root 'TEETH-AUTO.bat'), (Join-Path $root 'TEETH-CHECK-ONLY.bat'), (Join-Path $root 'TEETH-UNDO.bat'))
$script:pass = 0; $script:fail = 0; $script:lines = @()
function Check([string]$name, $cond) {
    if ($cond) { $script:pass++; $script:lines += ('PASS  ' + $name) } else { $script:fail++; $script:lines += ('FAIL  ' + $name) }
}

$script:lines += ('PowerShell running these checks: ' + $PSVersionTable.PSVersion.ToString())
foreach ($f in $files) { Check ('file exists: ' + (Split-Path $f -Leaf)) (Test-Path -LiteralPath $f) }

foreach ($f in $files) {
    if (-not (Test-Path -LiteralPath $f)) { continue }
    $bytes = [System.IO.File]::ReadAllBytes($f)
    $name = Split-Path $f -Leaf
    $nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
    Check ($name + ': ASCII only (no bytes above 127)') ($nonAscii -eq 0)
    $bad = 0
    for ($i = 0; $i -lt $bytes.Length; $i++) { if ($bytes[$i] -eq 10 -and ($i -eq 0 -or $bytes[$i - 1] -ne 13)) { $bad++ } }
    Check ($name + ': Windows line endings (CRLF)') ($bad -eq 0)
}

$tokens = $null; $errs = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($doctor, [ref]$tokens, [ref]$errs)
Check ('Teeth-Doctor.ps1 parses with no errors on this PowerShell (' + @($errs).Count + ' errors)') (@($errs).Count -eq 0)
if (@($errs).Count -gt 0) { foreach ($e in $errs) { $script:lines += ('      line ' + $e.Extent.StartLineNumber + ': ' + $e.Message) } }

$bannedKinds = @('QuestionQuestion', 'QuestionQuestionEquals', 'QuestionDot', 'QuestionLBracket', 'AndAnd', 'OrOr')
$hits = @($tokens | Where-Object { $bannedKinds -contains $_.Kind.ToString() })
Check 'no PowerShell-7-only operators (??, ?., &&, ||)' ($hits.Count -eq 0)
$bannedAst = @('TernaryExpressionAst', 'PipelineChainAst', 'NullConditionalMemberAccessAst')
$hits = @($ast.FindAll({ param($n) $bannedAst -contains $n.GetType().Name }, $true))
Check 'no PowerShell-7-only syntax (ternary, pipeline chain)' ($hits.Count -eq 0)

$cmds = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
$badParam = @('Parallel', 'AsHashtable', 'AsByteStream', 'AdditionalChildPath', 'ThrottleLimit')
$usedBad = @()
$joinBad = 0
foreach ($c in $cmds) {
    $nm = $c.GetCommandName()
    foreach ($el in $c.CommandElements) {
        if ($el -is [System.Management.Automation.Language.CommandParameterAst] -and ($badParam -contains $el.ParameterName)) { $usedBad += ($nm + ' -' + $el.ParameterName) }
    }
    if ($nm -eq 'Join-Path') {
        $pos = 0; $skip = $false
        foreach ($el in @($c.CommandElements | Select-Object -Skip 1)) {
            if ($skip) { $skip = $false; continue }
            if ($el -is [System.Management.Automation.Language.CommandParameterAst]) { if ($el.Argument -eq $null) { $skip = $true }; continue }
            $pos++
        }
        if ($pos -gt 2) { $joinBad++ }
    }
}
Check 'no parameters that Windows PowerShell 5.1 does not have' ($usedBad.Count -eq 0)
Check 'Join-Path always has 2 arguments (5.1 allows no more)' ($joinBad -eq 0)

$reserved = @('Host', 'Args', 'Input', 'Error', 'PID', 'Home', 'Pwd', 'Profile', 'PSScriptRoot', 'PSCommandPath', 'MyInvocation', 'PSItem', 'Foreach', 'This', 'LASTEXITCODE', 'Event', 'Sender')
$asg = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))
$clash = @()
foreach ($a in $asg) {
    $left = $a.Left
    if ($left -is [System.Management.Automation.Language.VariableExpressionAst]) {
        $vn = $left.VariablePath.UserPath -replace '^(script|global|local):', ''
        if ($reserved -contains $vn) { $clash += $vn }
    }
}
Check 'no assignment to a PowerShell automatic variable ($Host, $Args, $Input, ...)' ($clash.Count -eq 0)

$defs = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
$stepCalls = @($cmds | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ -and $_ -like 'Step-*' } | Select-Object -Unique)
$missing = @($stepCalls | Where-Object { $defs -notcontains $_ })
Check 'every Step-... function that is called is defined' ($missing.Count -eq 0)
Check 'function names are unique' (@($defs).Count -eq @($defs | Select-Object -Unique).Count)

$known = @('Add-Result','Ask-YesNo','Clear-Host','ConvertFrom-IniLines','ConvertTo-NormalMac','Copy-Item','Disable-NetAdapterPowerManagement','Enable-NetAdapter','Get-AdapterInfo','Get-ChildItem','Get-CimInstance','Get-Content','Get-Date','Get-Item','Get-ItemProperty','Get-NetAdapter','Get-NetAdapterAdvancedProperty','Get-NetFirewallPortFilter','Get-NetFirewallRule','Get-NetIPAddress','Get-NetRoute','Get-NetTCPConnection','Get-OtherNetworkIps','Get-OverallResult','Get-PowercfgIndexes','Get-Process','Get-ProcsByName','Get-RangePrefix','Get-ScheduledTask','Get-TimeZone','Join-Path','Move-Item','New-Item','New-NetFirewallRule','New-NetIPAddress','New-Object','New-ScheduledTaskAction','New-ScheduledTaskPrincipal','New-ScheduledTaskSettingsSet','New-ScheduledTaskTrigger','New-TimeSpan','Out-File','Out-Null','Read-Host','Read-IniFile','Register-ScheduledTask','Remove-Item','Remove-NetFirewallRule','Remove-NetIPAddress','Save-Cfg','Say','Select-Object','Select-TeethAdapter','Set-Content','Set-CimInstance','Set-DnsClientServerAddress','Set-NetAdapterAdvancedProperty','Set-NetIPInterface','Split-Path','Start-AppNormal','Start-Process','Start-Sleep','Stop-Process','Test-AdapterSafe','Test-Admin','Test-IpInPrefix','Test-Path','Test-Ping','Test-TcpPort','Unblock-File','Unregister-ScheduledTask','Where-Object','Write-Host','Write-Log','Write-Status','ForEach-Object','Step-Adapter','Step-Clock','Step-Counts','Step-Firewall','Step-Folder','Step-InstallCopy','Step-Link','Step-PortOwner','Step-Programs','Step-RestorePoints','Step-Role','Step-Sleep','Step-Tasks','Step-Undo','Step-Unblock','Step-UsbSuspend','Get-Variable','Test-NetConnection','Get-ShiftKeyNow','Test-ShouldRestoreCounts','Get-CountsStorePath','Open-LiveDb','Read-LiveCounts','Write-LiveCounts','Save-CountsFile','Read-CountsFile','Add-CountsHistory','Step-CountsGuard','Get-PSDrive','Get-DotNetRelease','Find-DotNetInstaller','Get-AuthenticodeSignature','Step-DotNet','Restart-NetAdapter','Set-TimeZone','Set-Date')
$names = @($cmds | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ } | Select-Object -Unique)
$unknown = @($names | Where-Object { ($known -notcontains $_) -and ($_ -notmatch '^(\.|&|ipconfig|netsh|powercfg|robocopy|powershell\.exe)$') })
Check 'every command used is a known one (a typo in a command name shows up here)' ($unknown.Count -eq 0)
if ($unknown.Count -gt 0) { $script:lines += ('      unknown: ' + ($unknown -join ', ')) }

$setMatch = [regex]::Match((Get-Content -LiteralPath $doctor -Raw), "ValidateSet\(([^)]*)\)")
$modes = @([regex]::Matches($setMatch.Groups[1].Value, "'([A-Za-z]+)'") | ForEach-Object { $_.Groups[1].Value })
foreach ($b in @('TEETH-AUTO.bat', 'TEETH-CHECK-ONLY.bat', 'TEETH-UNDO.bat')) {
    $t = Get-Content -LiteralPath (Join-Path $root $b) -Raw
    Check ($b + ' starts App Files\Tools\Teeth-Doctor.ps1') ($t -match [regex]::Escape('App Files\Tools\Teeth-Doctor.ps1'))
    $m = [regex]::Match($t, '-Mode\s+(\S+)')
    $val = $m.Groups[1].Value
    Check ($b + ' uses a valid -Mode (' + $val + ')') (($val -eq '%ARG%') -or ($modes -contains $val))
}
$vbs = Get-Content -LiteralPath (Join-Path $tools 'Teeth-Doctor-Hidden.vbs') -Raw
Check 'Teeth-Doctor-Hidden.vbs starts Teeth-Doctor.ps1 with -Mode Fix -Silent' (($vbs -match 'Teeth-Doctor\.ps1') -and ($vbs -match '-Mode Fix -Silent'))

$docs = @((Join-Path $root 'SETUP-TWO-LAPTOPS.txt'), (Join-Path (Join-Path $root 'App Files') 'README.md'), (Join-Path (Join-Path $root 'docs') 'Teeth_Two_Laptop_Plan.md'), (Join-Path (Join-Path $root 'docs') 'AI_CONTEXT.md'))
foreach ($d in $docs) {
    $dn = Split-Path $d -Leaf
    if (-not (Test-Path -LiteralPath $d)) { Check ($dn + ': document exists') $false; continue }
    $t = Get-Content -LiteralPath $d -Raw
    Check ($dn + ': tells the same procedure (self-test step, laptop 2 first, no IP by hand, unzip both zips, daily-use pointer, meaning of the sender number)') (($t -match 'Run-Doctor-SelfTest') -and ($t -match '(?i)laptop 2 first') -and ($t -match 'NOT set the IP') -and ($t -match '(?i)both zips') -and ($t -match 'page 15') -and ($t -match 'since start'))
}

$script:lines += ''
$script:lines += ('RESULT: ' + $script:pass + ' of ' + ($script:pass + $script:fail) + ' static checks passed')
$script:lines | ForEach-Object { Write-Host $_ }
if ($script:fail -gt 0) { exit 1 }
