# No system mutations: all journal/catalogue files live in a temporary directory.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$helper = Join-Path $root 'lib\helpers.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('wintweaks-tests-' + [guid]::NewGuid().ToString('N') + ' space')
$null = New-Item -ItemType Directory -Path (Join-Path $fixture 'state') -Force
$null = New-Item -ItemType Directory -Path (Join-Path $fixture 'data') -Force
$journal = Join-Path $fixture 'state\applied.dat'
$encoding = [Text.Encoding]::GetEncoding(28591)
$savedRun = $env:OPT_RUN; $savedIds = $env:OPT_IDS
$savedLog = $env:LOGFILE; $env:LOGFILE = ''
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Invoke-Helper($arguments, $expected) {
  $output = & $helper @arguments
  Assert ($LASTEXITCODE -eq $expected) ("Expected exit $expected, got ${LASTEXITCODE}: $output")
  return $output
}
function Write-Journal($text) { [IO.File]::WriteAllText($journal, $text, $encoding) }
try {
  Copy-Item -LiteralPath (Join-Path $root 'data\tweaks.def'),(Join-Path $root 'data\protected.def'),(Join-Path $root 'data\descr.ru') -Destination (Join-Path $fixture 'data')
  $null = Invoke-Helper @{Action='ValidateData'; Root=$fixture} 0
  $defs = Join-Path $fixture 'data\tweaks.def'
  $original = [IO.File]::ReadAllText($defs)
  foreach ($bad in @(
    'TEST-ID|core|low|any|REG|HKCU\Software\Test||REG_DWORD|0',
    'TEST-ID|core|low|any|REG|HKCU\Software\Test|Value|REG_SZ|%TEMP%',
    'TEST-ID|core|low|any|REG|HKCU\Software\Test|Value|REG_SZ|"bad"'
  )) {
    [IO.File]::WriteAllText($defs, $original + "`r`n" + $bad)
    $null = Invoke-Helper @{Action='ValidateData'; Root=$fixture} 1
  }
  [IO.File]::WriteAllText($defs, $original)
  # A file can exist yet be unreadable. Validation must fail closed.
  $handle = [IO.File]::Open($defs, 'Open', 'ReadWrite', 'None')
  try {
    $ErrorActionPreference = 'Continue'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $helper -Action ValidateData -Root $fixture 2>$null
    $ErrorActionPreference = 'Stop'
    Assert ($LASTEXITCODE -ne 0) 'Unreadable catalogue was accepted'
  } finally { $handle.Dispose() }

  $null = Invoke-Helper @{Action='AcquireLock'; Root=$fixture; Token='owner'} 0
  $null = Invoke-Helper @{Action='AcquireLock'; Root=$fixture; Token='other'} 1
  $null = Invoke-Helper @{Action='ReleaseLock'; Root=$fixture; Token='other'} 0
  Assert (Test-Path (Join-Path $fixture 'state\run.lock')) 'Non-owner removed lock'
  $null = Invoke-Helper @{Action='ReleaseLock'; Root=$fixture; Token='owner'} 0
  Assert (-not (Test-Path (Join-Path $fixture 'state\run.lock'))) 'Owner did not release lock'
  $null = Invoke-Helper @{Action='AcquireLock'; Root=(Join-Path $fixture 'missing'); Token='owner'} 1

  $row = 'run1|UI-FILEEXT|REG|HKCU\Software\Test|HideFileExt|PRESENT|REG_SZ|original' + [char]233 + '|0|PENDING|time'
  $other = 'run2|UI-FILEEXT|REG|HKCU\Software\Test|HideFileExt|PRESENT|REG_DWORD|1|0|OK|time'
  Write-Journal ($row + "`r`n" + $other + "`r`n")
  $null = Invoke-Helper @{Action='JournalSetResult'; Full=$journal; Token='run1'; Name='UI-FILEEXT'; Description='OK'} 0
  Assert ([IO.File]::ReadAllText($journal,$encoding) -ceq ($row.Replace('|PENDING|','|OK|') + "`r`n" + $other + "`r`n")) 'Update changed original data or another run'
  $before = [IO.File]::ReadAllText($journal,$encoding)
  $handle = [IO.File]::Open($journal, 'Open', 'Read', 'Read')
  try {
    $null = Invoke-Helper @{Action='JournalSetResult'; Full=$journal; Token='run1'; Name='UI-FILEEXT'; Description='REVERTED'} 4
  } finally { $handle.Dispose() }
  Assert ([IO.File]::ReadAllText($journal,$encoding) -ceq $before) 'Failed replacement damaged journal'
  Assert (@(Get-ChildItem (Join-Path $fixture 'state') -Filter '*.tmp').Count -eq 0) 'Temporary journal leaked'
  Write-Journal 'broken|row'
  $null = Invoke-Helper @{Action='ValidateJournal'; Full=$journal} 4
  $null = Invoke-Helper @{Action='JournalSetResult'; Full=$journal; Token='run1'; Name='UI-FILEEXT'; Description='OK'} 4
  Assert ([IO.File]::ReadAllText($journal) -eq 'broken|row') 'Malformed journal was overwritten'

  # Controlled read-only providers make drift/error coverage independent of the host.
  function Get-CimInstance { [pscustomobject]@{StartMode=$global:wtTestmode} }
  function Get-ScheduledTask { if ($global:wtTesttaskError) { throw 'query denied' }; [pscustomobject]@{State=$global:wtTesttaskState} }
  function Get-Item {
    $key = New-Object psobject
    $key | Add-Member ScriptMethod GetValueKind { $global:wtTestkind }
    $key | Add-Member ScriptMethod GetValue { $global:wtTestvalue }
    $key | Add-Member ScriptMethod Close { }
    return $key
  }
  $env:OPT_RUN = ''; $env:OPT_IDS = ''
  [IO.File]::WriteAllLines($defs, @(
    'TEST-REG|core|low|any|REG|@HKCU@\Software\Test|Value|REG_DWORD|0xffffffff',
    'TEST-SVC|core|low|any|SVC|TestService|-|start|disabled',
    'TEST-TASK|core|low|any|TASK|\TestTask|-|state|disable'
  ))
  $rows = @(
    'run1|TEST-REG|REG|HKU\S-1-5-21-123\Software\Test|Value|PRESENT|REG_DWORD|0|0|OK|time',
    'run1|TEST-SVC|SVC|TestService|-|PRESENT|Auto|Running|0|OK|time',
    'run2|TEST-TASK|TASK|\TestTask|-|PRESENT|Ready|-|0|OK|time'
  )
  Write-Journal (($rows -join "`r`n") + "`r`n")
  $global:wtTestkind='DWord'; $global:wtTestvalue=-1; $global:wtTestmode='Disabled'; $global:wtTesttaskState='Disabled'; $global:wtTesttaskError=$false
  $output = Invoke-Helper @{Action='VerifyJournal'; Root=$fixture; Full=$journal} 0
  Assert ($output[-1] -like '*matched=3 drift=0 unsupported=0*') 'Matching state not recognized'
  $global:wtTestvalue=0; $global:wtTestmode='Auto'; $global:wtTesttaskState='Ready'
  $output = Invoke-Helper @{Action='VerifyJournal'; Root=$fixture; Full=$journal} 4
  Assert ($output[-1] -like '*matched=0 drift=3 unsupported=0*') 'Drift not detected'
  $global:wtTesttaskError=$true; $env:OPT_RUN='run2'
  $output = Invoke-Helper @{Action='VerifyJournal'; Root=$fixture; Full=$journal} 4
  Assert ($output[-1] -like '*matched=0 drift=0 unsupported=1*') 'Read error or run filter mishandled'
  $env:OPT_RUN=''; $env:OPT_IDS=' TEST-REG '
  $global:wtTestvalue=-1; $global:wtTestkind='String'
  $null = Invoke-Helper @{Action='VerifyJournal'; Root=$fixture; Full=$journal} 4
  $global:wtTestkind='DWord'
  $null = Invoke-Helper @{Action='VerifyJournal'; Root=$fixture; Full=$journal} 0
  $currentDefs = [IO.File]::ReadAllText($defs)
  [IO.File]::WriteAllText($defs, $currentDefs.Replace('REG_DWORD|0xffffffff','REG_SZ|@EMPTY@'))
  $global:wtTestkind='String'; $global:wtTestvalue=''
  $null = Invoke-Helper @{Action='VerifyJournal'; Root=$fixture; Full=$journal} 0
  $global:wtTestvalue='different'
  $null = Invoke-Helper @{Action='VerifyJournal'; Root=$fixture; Full=$journal} 4
  [IO.File]::WriteAllText($defs, $currentDefs)
  Assert ([IO.File]::ReadAllText($journal,$encoding) -ceq (($rows -join "`r`n") + "`r`n")) 'Verify modified journal'
  $env:OPT_IDS=''
  foreach ($status in @('PENDING','FAILED','MANUAL')) {
    Write-Journal $rows[0].Replace('|OK|',('|' + $status + '|'))
    $null = Invoke-Helper @{Action='VerifyJournal'; Root=$fixture; Full=$journal} 4
  }
  Write-Journal $rows[0].Replace('|OK|','|REVERTED|')
  $null = Invoke-Helper @{Action='VerifyJournal'; Root=$fixture; Full=$journal} 0
  Write-Journal ''
  $null = Invoke-Helper @{Action='VerifyJournal'; Root=$fixture; Full=$journal} 0

  $batch = Join-Path $fixture 'journal-test.cmd'
  $core = Join-Path $root 'lib\core.cmd'
  $commands = @(
    '@echo off', 'setlocal EnableExtensions EnableDelayedExpansion',
    ('set "PSH=powershell -NoProfile -ExecutionPolicy Bypass -File "' + $helper + '""'),
    ('set "JOURNAL=' + $journal + '"'), ('set "BACKUPDIR=' + $fixture + '\backups"'),
    'set "OPT_DRY=0"', 'set "RUNID=batch-run"', 'set "LOGFILE="', 'set "LOGDATE=2026-09-07"',
    ('call "' + $core + '" :journal_add TEST-REG REG "HKCU\Software\Test" Value PRESENT REG_DWORD 1 0'),
    'if errorlevel 1 exit /b 10',
    ('call "' + $core + '" :journal_mark TEST-REG OK'),
    'if errorlevel 1 exit /b 11',
    'set "OPT_DRY=1"',
    ('call "' + $core + '" :journal_setresult batch-run TEST-REG REVERTED'),
    'if errorlevel 1 exit /b 12',
    'set "OPT_DRY=0"', ('set "JOURNAL=' + $fixture + '\state"'),
    ('call "' + $core + '" :journal_add TEST-REG REG "HKCU\Software\Test" Value PRESENT REG_DWORD 1 0'),
    'if not errorlevel 1 exit /b 13',
    'if not "!JOURNAL_FAILED!"=="1" exit /b 14', 'exit /b 0'
  )
  [IO.File]::WriteAllText($batch, ($commands -join "`r`n") + "`r`n",[Text.Encoding]::ASCII)
  $ErrorActionPreference = 'Continue'
  $batchOutput = & cmd.exe /d /c $batch 2>&1
  $ErrorActionPreference = 'Stop'
  Assert ($LASTEXITCODE -eq 0) ("CMD journal regression exit ${LASTEXITCODE}: $batchOutput")
  $saved = [IO.File]::ReadAllText($journal)
  Assert ($saved -match '\|OK\|' -and $saved -notmatch 'REVERTED') 'CMD mark or dry mode failed'

  # Exercise the public entry point and lock cleanup on an initialization error.
  $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'lib')
  Copy-Item -LiteralPath $helper,$core -Destination (Join-Path $fixture 'lib')
  Copy-Item -LiteralPath (Join-Path $root 'wintweaks.cmd') -Destination $fixture
  Copy-Item -LiteralPath (Join-Path $root 'data\tweaks.def') -Destination $defs -Force
  Write-Journal ''
  $engine = Join-Path $fixture 'wintweaks.cmd'
  $output = & cmd.exe /d /c $engine status
  Assert ($LASTEXITCODE -eq 0) ("Public status failed: $output")
  Assert (-not (Test-Path (Join-Path $fixture 'state\run.lock'))) 'Status leaked lock'
  Write-Journal 'broken|row'
  $output = & cmd.exe /d /c $engine status
  Assert ($LASTEXITCODE -eq 4) ("Malformed journal was accepted by public CLI: $output")
  Assert (-not (Test-Path (Join-Path $fixture 'state\run.lock'))) 'Initialization failure leaked lock'
  Write-Output 'Runtime regression checks passed.'
} finally {
  $env:OPT_RUN=$savedRun; $env:OPT_IDS=$savedIds
  $env:LOGFILE=$savedLog
  $resolved = [IO.Path]::GetFullPath($fixture)
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
  if ($resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -like 'wintweaks-tests-*') {
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
# CI shells propagate LASTEXITCODE; the final negative test deliberately sets 4.
exit 0
