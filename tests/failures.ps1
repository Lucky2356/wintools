# Critical mutation paths run against disposable command stubs, never Windows settings.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('wintweaks-failures-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
$journal = Join-Path $fixture 'applied.dat'
$helper = Join-Path $fixture 'helper.ps1'
$trace = Join-Path $fixture 'calls.txt'
function Assert($ok,$message) { if (-not $ok) { throw $message } }
try {
  Add-Type -OutputAssembly (Join-Path $fixture 'native.exe') -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.IO;
public class NativeStub {
  public static int Main(string[] args) {
    string command = String.Join(" ", args);
    File.AppendAllText(Environment.GetEnvironmentVariable("WT_TRACE"), command + "\n");
    string fail = Environment.GetEnvironmentVariable("WT_FAIL");
    if (!String.IsNullOrEmpty(fail) && command.Contains(fail)) return 1;
    if (args.Length > 1 && args[0] == "/export") File.WriteAllText(args[1], "backup");
    if (args.Length > 0 && args[0] == "query") {
      string name = args.Length > 3 ? args[3] : "Value";
      Console.WriteLine("    " + name + (name == "ActivePowerScheme"
        ? "    REG_SZ    381b4222-f694-41f0-9685-ff5bb260df2e"
        : "    REG_DWORD    0x2"));
    }
    return 0;
  }
}
'@
  foreach ($name in @('powercfg','netsh','fsutil','reg','robocopy')) {
    Copy-Item -LiteralPath (Join-Path $fixture 'native.exe') -Destination (Join-Path $fixture ($name + '.exe'))
  }
  $helperText = @'
param($Action,$Full,$Name,$Root,$Token,$Description)
switch ($Action) {
  GetTcpAutotuning { 'AUTOTUNING=Disabled'; exit 0 }
  GetLastAccess { 'LASTACCESS=0'; exit 0 }
  CheckRegistryAbsent { exit 4 }
  RestoreAppx { exit 0 }
  GetAppx { 'EXISTS=1'; 'FULLNAME=Test.App_full'; 'FAMILYNAME=Test.App_family'; ('INSTALLLOC=' + $env:WT_PAYLOAD); 'NONREMOVABLE=0'; exit 0 }
  GetProvisioned { 'EXISTS=0'; exit 0 }
  RemoveAppx { [IO.File]::AppendAllText($env:WT_TRACE, 'REMOVEAPPX'); exit 0 }
}
& '__HELPER__' @PSBoundParameters
exit $LASTEXITCODE
'@
  [IO.File]::WriteAllText($helper,$helperText.Replace('__HELPER__',(Join-Path $root 'lib\helpers.ps1')))
  function Run-Batch($body,$fail='') {
    $preamble = @(
      '@echo off','setlocal EnableExtensions EnableDelayedExpansion',
      ('set "PATH=' + $fixture + ';%PATH%"'), ('set "LIBDIR=' + $root + '\lib"'),
      ('set "PSH=powershell -NoProfile -ExecutionPolicy Bypass -File "' + $helper + '""'),
      ('set "JOURNAL=' + $journal + '"'), ('set "BACKUPDIR=' + $fixture + '\backup"'),
      ('set "WT_TRACE=' + $trace + '"'), ('set "WT_FAIL=' + $fail + '"'),
      ('set "WT_PAYLOAD=' + $fixture + '\payload"'), ('set "LOCALAPPDATA=' + $fixture + '\local"'),
      'set "RUNID=test-run"','set "LOGDATE=2026-09-07"','set "LOGFILE="','set "OPT_DRY=0"',
      'set "JOURNAL_FAILED=0"','set "OPT_RUN="','set "OPT_IDS="'
    )
    $batch = Join-Path $fixture 'case.cmd'
    [IO.File]::WriteAllText($batch,(($preamble + $body + 'exit /b %ERRORLEVEL%') -join "`r`n") + "`r`n",[Text.Encoding]::ASCII)
    & cmd.exe /d /c $batch | Out-Null
    return $LASTEXITCODE
  }
  Push-Location $fixture
  try {
    foreach ($case in @(@('SYS-TCP-AUTOTUNING','autotuninglevel=normal'),@('SYS-LASTACCESS','set disablelastaccess'),@('SYS-POWER-SCHEME','standby-timeout-ac'))) {
      [IO.File]::WriteAllText($journal,'')
      $rc = Run-Batch @(('set "OPT_IDS= ' + $case[0] + ' "'),'call "%LIBDIR%\syschange.cmd" :main') $case[1]
      Assert ($rc -eq 4) ('System command failure hidden: ' + $case[0] + ', exit=' + $rc)
      Assert ((Get-Content $journal -Raw) -match '\|FAILED\|') 'Failed command recorded as OK'
    }
    foreach ($case in @(@('PWR','HIBERNATE','REG_DWORD','0x1','/h on'),@('NET','autotuninglevel','NETSH','Disabled','autotuninglevel'),@('FS','disablelastaccess','FSUTIL','0','set disablelastaccess'))) {
      $row = "test-run|TEST-ID|$($case[0])|$($case[1])|-|PRESENT|$($case[2])|$($case[3])|0|OK|time"
      [IO.File]::WriteAllText($journal,$row + "`r`n")
      $rc = Run-Batch @('call "%LIBDIR%\core.cmd" :revert_journal') $case[4]
      Assert ($rc -eq 4) 'Restore failure hidden'
      Assert ((Get-Content $journal -Raw) -notmatch 'REVERTED') 'Failed restore marked REVERTED'
    }
    [IO.File]::WriteAllText($journal,'')
    $rc = Run-Batch @(
      'set "T_ID=TEST-REG"','set "T_TARGET=HKU\S-1-5-21-111\Software\Test"','set "T_NAME=Value"',
      'set "T_VTYPE=REG_DWORD"','set "T_VALUE=1"','set "PROTECTED=NUL"',
      'call "%LIBDIR%\reg.cmd" :apply_one','if errorlevel 1 exit /b 9',
      'set "RUNID=second-run"','set "T_TARGET=HKU\S-1-5-21-222\Software\Test"',
      'call "%LIBDIR%\reg.cmd" :apply_one'
    )
    Assert ($rc -eq 0) 'Registry apply failed against stubs'
    Assert (@(Get-Content $journal).Count -eq 2) 'Second SID original was not journalled'
    Assert (@(Select-String -Path $journal -Pattern '\|OK\|').Count -eq 2) 'Per-run results not recorded'
    $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'payload')
    [IO.File]::WriteAllText((Join-Path $fixture 'payload\AppxManifest.xml'),'test')
    $deep = @('set "T_ID=TEST-APP"','set "T_TYPE=APPX"','set "T_TARGET=Test.App"','set "OPT_DEEP=1"','set "PROTECTED=NUL"','call "%LIBDIR%\appx.cmd" :apply_one')
    [IO.File]::WriteAllText($journal,'')
    [IO.File]::WriteAllText($trace,'')
    $rc = Run-Batch $deep 'export HKCU'
    Assert ($rc -ne 0) 'Failed AppX registry backup was ignored'
    Assert ((Get-Content $trace -Raw) -notmatch 'REMOVEAPPX') 'AppX removed after failed backup'
    Assert ((Get-Item $journal).Length -eq 0) 'Failed backup was journalled as a mutation'
    $backup = Join-Path $fixture 'backup\appx\Test.App'
    [IO.File]::WriteAllText((Join-Path $backup 'meta.txt'),"ORIGLOC=$fixture\payload`r`nLOCALDATA=1`r`nACTIVATABLE=0`r`n")
    [IO.File]::WriteAllText($journal,"test-run|TEST-APP|APPXDEEP|Test.App|Test.App_family|PRESENT|Test.App_full|$backup|0|OK|time`r`n")
    $rc = Run-Batch @('call "%LIBDIR%\core.cmd" :revert_journal')
    Assert ($rc -eq 4) 'Missing AppX user-data backup was ignored'
    Assert ((Get-Content $journal -Raw) -notmatch 'REVERTED') 'Partial AppX restore marked complete'
  } finally { Pop-Location }
  Write-Output 'Critical failure checks passed.'
} finally {
  $resolved = [IO.Path]::GetFullPath($fixture)
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
  if ($resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -like 'wintweaks-failures-*') {
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
exit 0
