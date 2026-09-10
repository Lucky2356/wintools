param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub-hosted runner required'}
$fixture=Join-Path $env:RUNNER_TEMP ('WintoolsPortable_'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $fixture
$exe=Join-Path $fixture 'WintoolsPortable.exe'
Copy-Item -LiteralPath $Executable -Destination $exe
function Run-Portable($path,$arguments,$expected=0){
  $process=Start-Process -FilePath $path -ArgumentList $arguments -PassThru -WindowStyle Hidden
  $timeout=if($arguments -eq '--self-test'){900000}else{90000}
  $finished=$process.WaitForExit($timeout)
  $progress=Join-Path $fixture 'portable-integration-progress.txt'
  if(Test-Path $progress){Get-Content $progress -Tail 100;Copy-Item $progress (Join-Path (Split-Path $Executable -Parent) 'portable-integration-progress.txt') -Force}
  if(-not $finished){throw "Portable test timed out: $arguments"}
  if($process.ExitCode -ne $expected){
    Get-ChildItem $fixture -Recurse -Filter '*error.txt' | ForEach-Object {Get-Content $_.FullName}
    throw "Portable exit $($process.ExitCode), expected $expected"
  }
}
Run-Portable $exe '--self-test'
Run-Portable $exe '--ui-smoke'
foreach($name in @('portable-ui.png','portable-ui-light.png','portable-ui-settings-dark.png','portable-ui-settings-light.png','portable-ui-compact.png','portable-ui-updates.png','portable-ui-collections.png','portable-ui-browse.png','portable-ui-plan.png','portable-ui-health-5.png','portable-ui-health-6.png','portable-ui-health-7.png','portable-ui-health-8.png','portable-ui-service-cards.png','portable-ui-wide-2.png','portable-ui-wide-3.png','portable-ui-wide-5.png','portable-ui-monitor.png','portable-ui-applications.png','portable-ui-applications-compact.png','portable-ui-network.png','portable-ui-service-management.png','portable-ui-service-management-compact.png','portable-ui-service-watch.png','portable-ui-power.png','portable-ui-power-compact.png','portable-ui-integrity.png','portable-ui-integrity-compact.png','portable-ui-startup.png','portable-ui-startup-compact.png','portable-ui-repair.png','portable-ui-repair-compact.png','portable-ui-processes.png','portable-ui-processes-compact.png')){
  $screenshot=Join-Path $fixture $name
  if(-not(Test-Path $screenshot)){throw "UI screenshot missing: $name"}
  Copy-Item $screenshot (Join-Path (Split-Path $Executable -Parent) $name)
}
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
