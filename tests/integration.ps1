# Real Windows objects, exclusively on disposable GitHub-hosted runners.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $env:RUNNER_TEMP) {
  throw 'This test may only run on a GitHub-hosted Windows runner.'
}
$root = Split-Path $PSScriptRoot -Parent
$id = 'WintoolsCI_' + [guid]::NewGuid().ToString('N')
$fixture = Join-Path $env:RUNNER_TEMP $id
$key = 'HKCU\Software\' + $id
$psKey = 'HKCU:\Software\' + $id
$journal = Join-Path $fixture 'applied.dat'
$service = $id
$task = $id
$null = New-Item -ItemType Directory -Path $fixture
$null = New-Item -ItemType Directory -Path (Join-Path $fixture 'data')
$null = New-Item -Path $psKey
$helper = Join-Path $root 'lib\helpers.ps1'
$checks = New-Object 'System.Collections.Generic.List[string]'
function Assert($condition,$message) { if (-not $condition) { throw $message }; $checks.Add($message) }
function Run-Module([string[]]$commands,[int]$expected=0) {
  $setup = @(
    '@echo off','setlocal EnableExtensions EnableDelayedExpansion',
    ('set "LIBDIR=' + $root + '\lib"'),('set "APPROOT=' + $fixture + '"'),
    ('set "JOURNAL=' + $journal + '"'),('set "BACKUPDIR=' + $fixture + '\backup"'),
    ('set "BACKUPROOT=' + $fixture + '"'),('set "PROTECTED=' + $root + '\data\protected.def"'),
    ('set "PSH=powershell -NoProfile -ExecutionPolicy Bypass -File "' + $helper + '""'),
    'set "RUNID=first"','set "OPT_DRY=0"','set "OPT_IDS="','set "OPT_RUN="',
    'set "LOGFILE="','set "LOGDATE=2026-09-07"','set "QUIET=0"','set "JOURNAL_FAILED=0"','set "HKCU_ROOT=HKCU"'
  )
  $batch = Join-Path $fixture 'run.cmd'
  [IO.File]::WriteAllText($batch,(($setup + $commands + 'exit /b %ERRORLEVEL%') -join "`r`n") + "`r`n",[Text.Encoding]::ASCII)
  & cmd.exe /d /c $batch
  if ($LASTEXITCODE -ne $expected) { throw "Module exit $LASTEXITCODE, expected $expected" }
}
function Apply-Reg($target,$name,$type,$value,$run='first',$dry='0') {
  [IO.File]::WriteAllText((Join-Path $fixture 'data\tweaks.def'),"CI-REG|manual|low|any|REG|$target|$name|$type|$value`r`n")
  Run-Module @('set "T_ID=CI-REG"',('set "T_TARGET=' + $target + '"'),('set "T_NAME=' + $name + '"'),('set "T_VTYPE=' + $type + '"'),('set "T_VALUE=' + $value + '"'),('set "RUNID=' + $run + '"'),('set "OPT_DRY=' + $dry + '"'),'call "%LIBDIR%\reg.cmd" :apply_one')
}
function Revert($run='') { Run-Module @(('set "OPT_RUN=' + $run + '"'),'call "%LIBDIR%\core.cmd" :revert_journal') }
function Verify($expected=0) { Run-Module @('call "%LIBDIR%\core.cmd" :verify_journal') $expected }
function Clear-Journal { [IO.File]::WriteAllText($journal,'') }
try {
  Clear-Journal
  $null = New-ItemProperty -Path $psKey -Name Value -Value 7 -PropertyType DWord
  Apply-Reg $key Value REG_DWORD 1 first 1
  Assert ((Get-ItemProperty $psKey).Value -eq 7 -and (Get-Item $journal).Length -eq 0) 'Dry run preserved registry and journal'
  Apply-Reg $key Value REG_DWORD 1
  Assert ((Get-ItemProperty $psKey).Value -eq 1) 'Registry apply changed actual value'
  Verify
  Apply-Reg $key Value REG_DWORD 1 second
  Assert (@(Get-Content $journal).Count -eq 1) 'Repeated apply was idempotent'
  Set-ItemProperty $psKey Value 9
  Verify 4
  Apply-Reg $key Value REG_DWORD 1 second
  Revert second
  Assert ((Get-ItemProperty $psKey).Value -eq 9) 'Selected revert restored external drift snapshot'
  Revert
  Assert ((Get-ItemProperty $psKey).Value -eq 7) 'Full revert restored original snapshot'
  Verify
  Revert

  Clear-Journal
  Set-ItemProperty $psKey -Name '(default)' -Value 'Original'
  & powershell -NoProfile -ExecutionPolicy Bypass -File $helper -Action GetRegistry -Full $key -Name '@DEFAULT@'
  Apply-Reg $key '@DEFAULT@' REG_SZ '@EMPTY@'
  Assert ((Get-Item $psKey).GetValue('') -ceq '') 'Default registry value became empty'
  Revert
  Assert ((Get-Item $psKey).GetValue('') -ceq 'Original') 'Default registry value restored'

  Clear-Journal
  $child = $key + '\Created\Leaf'
  Apply-Reg $child Value REG_DWORD 1
  $null = New-ItemProperty ($psKey + '\Created\Leaf') -Name Foreign -Value keep -PropertyType String
  Revert
  Assert ((Get-ItemProperty ($psKey + '\Created\Leaf')).Foreign -eq 'keep') 'Revert preserved unrelated data in a created key'
  Clear-Journal
  Apply-Reg ($key + '\Empty\Leaf') Value REG_DWORD 1
  Revert
  Assert (-not (Test-Path ($psKey + '\Empty'))) 'Revert pruned empty created ancestors'

  Clear-Journal
  $null = New-Service -Name $service -BinaryPathName "$env:SystemRoot\System32\cmd.exe /c exit" -StartupType Manual
  [IO.File]::WriteAllText((Join-Path $fixture 'data\tweaks.def'),"CI-SVC|manual|low|any|SVC|$service|-|start|disabled`r`n")
  Run-Module @('set "T_ID=CI-SVC"',('set "T_TARGET=' + $service + '"'),'set "T_VALUE=disabled"','call "%LIBDIR%\svc.cmd" :apply_one')
  Assert ((Get-CimInstance Win32_Service -Filter "Name='$service'").StartMode -eq 'Disabled') 'Service start mode changed'
  Verify
  Revert
  Assert ((Get-CimInstance Win32_Service -Filter "Name='$service'").StartMode -eq 'Manual') 'Service start mode restored'

  Clear-Journal
  $action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\cmd.exe" -Argument '/c exit'
  $null = Register-ScheduledTask -TaskName $task -Action $action
  [IO.File]::WriteAllText((Join-Path $fixture 'data\tweaks.def'),"CI-TASK|manual|low|any|TASK|\$task|-|state|disable`r`n")
  Run-Module @('set "T_ID=CI-TASK"',('set "T_TARGET=\' + $task + '"'),'call "%LIBDIR%\task.cmd" :apply_one')
  Assert ((Get-ScheduledTask -TaskName $task).State -eq 'Disabled') 'Task disabled'
  Verify
  Revert
  Assert ((Get-ScheduledTask -TaskName $task).State -ne 'Disabled') 'Task enabled on revert'
  $checks | ConvertTo-Json | Set-Content (Join-Path $env:RUNNER_TEMP 'wintools-integration.json') -Encoding UTF8
  Write-Output 'Real Windows integration checks passed.'
} finally {
  Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue
  & sc.exe delete $service | Out-Null
  # All cleanup targets are the unique objects created above, never catalogue objects.
  if ($psKey -match '^HKCU:\\Software\\WintoolsCI_[a-f0-9]{32}$') { Remove-Item -LiteralPath $psKey -Recurse -Force }
  $resolved = [IO.Path]::GetFullPath($fixture)
  $allowed = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
  if ($resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -eq $id) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
exit 0
