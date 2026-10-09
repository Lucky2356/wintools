param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub-hosted runner required'}
$fixture=Join-Path $env:RUNNER_TEMP ('WintoolsPortable_'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $fixture
$exe=Join-Path $fixture 'WintoolsPortable.exe'
Copy-Item -LiteralPath $Executable -Destination $exe
function Run-Portable($path,$arguments,$expected=0){
  $process=Start-Process -FilePath $path -ArgumentList $arguments -PassThru -WindowStyle Hidden
  $timeout=if($arguments -eq '--self-test'){3600000}elseif($arguments -like '--ui-smoke*'){300000}else{90000}
  $finished=$process.WaitForExit($timeout)
  $progress=Join-Path $fixture 'portable-integration-progress.txt'
  if(Test-Path $progress){Get-Content $progress -Tail 100;Copy-Item $progress (Join-Path (Split-Path $Executable -Parent) 'portable-integration-progress.txt') -Force}
  if(-not $finished){throw "Portable test timed out: $arguments"}
  if($process.ExitCode -ne $expected){
    Get-ChildItem $fixture -Recurse -Filter '*error.txt' | ForEach-Object {Get-Content $_.FullName}
    Get-ChildItem $fixture -Filter 'cli-report.txt' | ForEach-Object {Get-Content $_.FullName}
    # Screenshots taken before the failure show the broken layout in the uploaded artifact.
    Get-ChildItem $fixture -Filter 'portable-ui*.png' | Copy-Item -Destination (Split-Path $Executable -Parent) -Force
    throw "Portable exit $($process.ExitCode), expected $expected"
  }
}
# The command line applies a profile without the window. The engine refuses system changes on Windows Server,
# so on the hosted Server runners the dry run must end with code 1 and report that refusal; on client Windows it succeeds.
function Test-ProfileCommandLine($exe,$fixture){
  $profileFile=Join-Path $fixture 'cli-profile.json';$report=Join-Path $fixture 'cli-report.txt'
  $server=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name InstallationType).InstallationType -ne 'Client'
  [IO.File]::WriteAllText($profileFile,'{"Schema":"wintools/profile/1","Actions":["UI-FILEEXT"]}')
  Run-Portable $exe "--apply `"$profileFile`" --report `"$report`" --dry-run" $(if($server){1}else{0})
  $text=Get-Content $report -Raw
  if($server -and ($text -notmatch 'FAILED\s+UI-FILEEXT' -or $text -notmatch 'Unsupported installation type')){throw "Server refusal not reported: $text"}
  if(-not $server -and $text -notmatch 'OK\s+UI-FILEEXT'){throw "Profile dry run not reported: $text"}
  [IO.File]::WriteAllText($profileFile,'{"Schema":"other","Actions":["UI-FILEEXT"]}')
  Run-Portable $exe "--apply `"$profileFile`" --report `"$report`"" 2
  if((Get-Content $report -Raw) -notmatch ': 2'){throw 'Invalid profile not reported'}
}
Run-Portable $exe '--unit-test'
Run-Portable $exe '--self-test'
Run-Portable $exe '--ui-smoke'
Test-ProfileCommandLine $exe $fixture
Run-Portable $exe '--ui-smoke-english'
foreach($name in @('portable-ui.png','portable-ui-light.png','portable-ui-settings-dark.png','portable-ui-settings-light.png','portable-ui-compact.png','portable-ui-updates.png','portable-ui-collections.png','portable-ui-browse.png','portable-ui-plan.png','portable-ui-health-5.png','portable-ui-health-6.png','portable-ui-health-7.png','portable-ui-health-8.png','portable-ui-service-cards.png','portable-ui-wide-2.png','portable-ui-wide-3.png','portable-ui-wide-5.png','portable-ui-monitor.png','portable-ui-applications.png','portable-ui-applications-compact.png','portable-ui-network.png','portable-ui-service-management.png','portable-ui-service-management-compact.png','portable-ui-service-watch.png','portable-ui-power.png','portable-ui-power-compact.png','portable-ui-integrity.png','portable-ui-integrity-compact.png','portable-ui-startup.png','portable-ui-startup-compact.png','portable-ui-repair.png','portable-ui-repair-compact.png','portable-ui-processes.png','portable-ui-processes-compact.png','portable-ui-hardware.png','portable-ui-hardware-compact.png','portable-ui-hardware-native.png','portable-ui-store.png','portable-ui-store-compact.png','portable-ui-gpu.png','portable-ui-gpu-compact.png','portable-ui-gpu-native.png','portable-ui-measurements.png','portable-ui-measurements-compact.png','portable-ui-dependencies.png','portable-ui-dependencies-compact.png','portable-ui-service-paused.png','portable-ui-collection-assistant.png','portable-ui-collection-assistant-compact.png','portable-ui-history-partial.png','portable-ui-temperature-native.png','portable-ui-temperature-compact.png','portable-ui-cleanup.png','portable-ui-cleanup-compact.png','portable-ui-desktop-launch.png','portable-ui-desktop-launch-compact.png','portable-ui-logon-tasks.png','portable-ui-logon-tasks-compact.png','portable-ui-dns.png','portable-ui-dns-compact.png','portable-ui-hosts.png','portable-ui-hosts-compact.png','portable-ui-packages.png','portable-ui-packages-compact.png','portable-ui-boot.png','portable-ui-windows-update.png','portable-ui-updates-compact.png','portable-ui-backups.png','portable-ui-disk-usage.png','portable-ui-search.png','portable-ui-home.png','portable-ui-large-text.png','portable-ui-at-2k-home.png','portable-ui-at-fhd-text200-15.png','portable-ui-welcome-1.png','portable-ui-welcome-3.png','portable-ui-simple-home.png','portable-ui-shortcuts.png','portable-ui-whats-new.png','portable-ui-problem.png','portable-ui-en-15.png','portable-ui-en-welcome.png')){
  $screenshot=Join-Path $fixture $name
  if(-not(Test-Path $screenshot)){throw "UI screenshot missing: $name"}
  Copy-Item $screenshot (Join-Path (Split-Path $Executable -Parent) $name)
}
# Layouts drawn at 2K, Full HD and HD monitor sizes.
Get-ChildItem $fixture -Filter 'portable-ui-at-*.png' | Copy-Item -Destination (Split-Path $Executable -Parent) -Force
Get-ChildItem $fixture -Filter 'portable-ui-*.png' | Copy-Item -Destination (Split-Path $Executable -Parent) -Force
# Exercise the real updater in a disposable portable directory. Do not launch the result.
$stage=Join-Path $fixture ('WintoolsData\updates\'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $stage
$updater=Join-Path $stage 'updater.exe';$next=Join-Path $stage 'next.exe'
Copy-Item $exe $updater;Copy-Item $exe $next
$hash=(Get-FileHash $next -Algorithm SHA256).Hash
$badHash='0'*64
Run-Portable $updater "--replace WintoolsPortable.exe 2147483647 $badHash --ci-no-launch" 4
if((Get-FileHash $exe -Algorithm SHA256).Hash -ne $hash -or -not(Test-Path $next)){throw 'Rejected update changed original executable'}
$lock=Join-Path $fixture 'WintoolsData\state\run.lock'
[IO.File]::WriteAllText($lock,'busy')
Run-Portable $updater "--replace WintoolsPortable.exe 2147483647 $hash --ci-no-launch" 4
Remove-Item -LiteralPath $lock
Run-Portable $updater "--replace WintoolsPortable.exe 2147483647 $hash --no-relaunch"
if(-not(Test-Path ($exe+'.previous')) -or (Test-Path $next)){throw 'Atomic update or previous-version backup missing'}
if((Get-FileHash ($exe+'.previous') -Algorithm SHA256).Hash -ne $hash){throw 'Previous version backup corrupted'}
if(-not(Test-Path (Join-Path $fixture 'WintoolsData\preferences.json'))){throw 'Update lost preferences'}
Write-Output 'Portable UI and atomic updater tests passed.'
exit 0
