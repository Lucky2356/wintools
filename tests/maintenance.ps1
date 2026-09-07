# Hosted CI only. Files and junctions are created under a unique runner fixture.
$ErrorActionPreference='Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'GitHub-hosted runner required.' }
$root=Split-Path $PSScriptRoot -Parent
$helper=Join-Path $root 'lib\helpers.ps1'
$fixture=Join-Path $env:RUNNER_TEMP ('WintoolsMaintenance_' + [guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $fixture
$savedTemp=$env:TEMP; $savedDry=$env:OPT_DRY; $savedIds=$env:OPT_IDS; $savedRun=$env:OPT_RUN
function Assert($ok,$message){if(-not $ok){throw $message}}
function Invoke-Helper($arguments,$expected){
  $output=& $helper @arguments
  Assert ($LASTEXITCODE -eq $expected) "Expected $expected, got ${LASTEXITCODE}: $output"
  $output
}
try {
  $null=New-Item -ItemType Directory -Path (Join-Path $fixture 'data')
  Copy-Item (Join-Path $root 'data\tweaks.def') (Join-Path $fixture 'data\tweaks.def')
  $env:OPT_IDS='TYPO-ID'; $env:OPT_RUN=''
  $null=Invoke-Helper @{Action='ValidateSelection';Root=$fixture;Name='apply'} 1
  $env:OPT_IDS='UI-FILEEXT'
  $null=Invoke-Helper @{Action='ValidateSelection';Root=$fixture;Name='apply'} 0
  $null=Invoke-Helper @{Action='ValidateSelection';Root=$fixture;Name='cleanup'} 1
  $env:OPT_IDS=''
  $null=Invoke-Helper @{Action='ValidateSelection';Root=$fixture;Name='system-change'} 1
  $env:OPT_IDS='SYS-LONGPATHS'
  $null=Invoke-Helper @{Action='ValidateSelection';Root=$fixture;Name='system-change'} 0

  $env:TEMP=Join-Path $fixture 'temp'
  $outside=Join-Path $fixture 'outside'
  $null=New-Item -ItemType Directory -Path $env:TEMP,$outside
  $old=Join-Path $env:TEMP 'old.tmp'; $new=Join-Path $env:TEMP 'new.tmp'
  $keep=Join-Path $outside 'keep.txt'
  foreach($file in @($old,$new,$keep)){[IO.File]::WriteAllText($file,'fixture')}
  foreach($file in @($old,$keep)){(Get-Item $file).LastWriteTime=(Get-Date).AddDays(-10)}
  $link=Join-Path $env:TEMP 'linked'
  $null=New-Item -ItemType Junction -Path $link -Target $outside
  $env:OPT_DRY='1'
  $null=Invoke-Helper @{Action='CleanupFiles';Name='CLN-USERTEMP'} 0
  Assert (Test-Path $old) 'Dry cleanup deleted a file'
  $env:OPT_DRY='0'
  $null=Invoke-Helper @{Action='CleanupFiles';Name='CLN-USERTEMP'} 0
  Assert (-not(Test-Path $old)) 'Old temp file not removed'
  Assert ((Test-Path $new) -and (Test-Path $keep)) 'Cleanup touched new or linked files'
  $env:TEMP=$link
  $null=Invoke-Helper @{Action='CleanupFiles';Name='CLN-USERTEMP'} 4
  $env:TEMP=Join-Path $fixture 'temp'
  [IO.File]::WriteAllText($old,'locked')
  (Get-Item $old).LastWriteTime=(Get-Date).AddDays(-10)
  $handle=[IO.File]::Open($old,'Open','ReadWrite','None')
  try {$null=Invoke-Helper @{Action='CleanupFiles';Name='CLN-USERTEMP'} 4} finally {$handle.Dispose()}

  # Stop failures must prevent deletion and still restore originally running services.
  $global:wtServiceTrace=New-Object 'System.Collections.Generic.List[string]'
  function Get-Service($Name){[pscustomobject]@{Status='Running'}}
  function Stop-Service($Name){$global:wtServiceTrace.Add('stop '+$Name); throw 'Stop denied'}
  function Start-Service($Name){$global:wtServiceTrace.Add('start '+$Name)}
  $null=Invoke-Helper @{Action='CleanupUpdateCache'} 4
  Assert ($global:wtServiceTrace -contains 'start wuauserv') 'Originally running update service was not restarted after failure'
  Remove-Item Function:\Get-Service,Function:\Stop-Service,Function:\Start-Service

  # Disk Cleanup must restore an existing flag after a failed native process.
  $global:wtCleanupTrace=New-Object 'System.Collections.Generic.List[string]'
  $global:wtCleanupWriteFail=$false
  function Get-ChildItem {
    $key=[pscustomobject]@{PSPath='HKLM:\Fixture\Temporary Files'}
    $key | Add-Member ScriptMethod GetValueNames { @('StateFlags0064') }
    $key | Add-Member ScriptMethod GetValue { 9 }
    $key | Add-Member ScriptMethod GetValueKind { 'DWord' }
    $key | Add-Member ScriptMethod Close { }
    $key
  }
  function New-ItemProperty($LiteralPath,$Name,$Value,$PropertyType,[switch]$Force) {
    $global:wtCleanupTrace.Add('write '+$Value)
    if($global:wtCleanupWriteFail -and $Value -eq 2){throw 'Registry write denied'}
  }
  function Start-Process {
    $global:wtCleanupTrace.Add('launch')
    [pscustomobject]@{ExitCode=7}
  }
  $null=Invoke-Helper @{Action='CleanupManager'} 4
  Assert ($global:wtCleanupTrace -contains 'launch' -and $global:wtCleanupTrace[-1] -eq 'write 9') 'Cleanup failure did not restore original flags'
  $global:wtCleanupTrace.Clear(); $global:wtCleanupWriteFail=$true
  $null=Invoke-Helper @{Action='CleanupManager'} 4
  Assert ($global:wtCleanupTrace -notcontains 'launch') 'Disk Cleanup launched after flag preparation failed'
  Remove-Item Function:\Get-ChildItem,Function:\New-ItemProperty,Function:\Start-Process

  # Public read-only entry point must work without initializing a journal or lock.
  $app=Join-Path $fixture 'app'
  $null=New-Item -ItemType Directory -Path (Join-Path $app 'lib')
  Copy-Item (Join-Path $root 'wintweaks.cmd') $app
  Copy-Item $helper (Join-Path $app 'lib\helpers.ps1')
  & cmd.exe /d /c (Join-Path $app 'wintweaks.cmd') diagnose
  Assert ($LASTEXITCODE -eq 0) 'Public diagnose failed'
  Assert (-not(Test-Path (Join-Path $app 'state'))) 'Diagnose initialized mutation state'
  $reports=@(Get-ChildItem (Join-Path $app 'reports') -Filter '*.json')
  Assert ($reports.Count -eq 1) 'Diagnostic report missing'
  $report=Get-Content $reports[0].FullName -Raw | ConvertFrom-Json
  Assert ($report.schema -eq 'wintweaks/diagnostic/1' -and $report.disks.Count -gt 0) 'Diagnostic schema or disks missing'
  Assert ($report.startupEntries.Count -eq 0 -or -not($report.startupEntries[0].PSObject.Properties.Name -contains 'Command')) 'Report included startup commands'
  Write-Output 'Maintenance regression checks passed.'
} finally {
  $env:TEMP=$savedTemp; $env:OPT_DRY=$savedDry; $env:OPT_IDS=$savedIds; $env:OPT_RUN=$savedRun
  if($link -and (Test-Path -LiteralPath $link)){[IO.Directory]::Delete($link)}
  $resolved=[IO.Path]::GetFullPath($fixture)
  $allowed=[IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\')+'\'
  if($resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -match '^WintoolsMaintenance_[a-f0-9]{32}$'){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
exit 0
