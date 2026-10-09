param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub-hosted runner required'}
$fixture=Join-Path $env:RUNNER_TEMP ('WintoolsArm_'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $fixture
$exe=Join-Path $fixture 'WintoolsPortable.exe'
Copy-Item -LiteralPath $Executable -Destination $exe
function Run-Portable($path,$arguments,$expected=0){
  $process=Start-Process -FilePath $path -ArgumentList $arguments -PassThru -WindowStyle Hidden
  if(-not $process.WaitForExit(300000)){throw "Portable test timed out: $arguments"}
  if($process.ExitCode -ne $expected){
    Get-ChildItem $fixture -Recurse -Filter '*error.txt' | ForEach-Object {Get-Content $_.FullName}
    Get-ChildItem $fixture -Filter 'cli-report.txt' | ForEach-Object {Get-Content $_.FullName}
    throw "Portable exit $($process.ExitCode), expected $expected"
  }
}
Run-Portable $exe '--unit-test'
$result=Get-Content (Join-Path $fixture 'portable-unit-tests.txt') -Raw
Write-Output $result
# .NET Framework 4.8.1 (release 533320) runs AnyCPU programs natively on Arm; older versions run them as x64.
$release=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release).Release
Write-Output ".NET Framework release $release"
if($release -ge 533320 -and $result -notmatch 'Process: ARM64'){throw "Not running natively on ARM64 with .NET release $release"}
# A profile dry run through the command line exercises the engine worker on this architecture.
$profileFile=Join-Path $fixture 'cli-profile.json';$report=Join-Path $fixture 'cli-report.txt'
[IO.File]::WriteAllText($profileFile,'{"Schema":"wintools/profile/1","Actions":["UI-FILEEXT"]}')
# The engine refuses system changes on Windows Server; the Arm runner is client Windows 11 and must succeed.
$server=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name InstallationType).InstallationType -ne 'Client'
Run-Portable $exe "--apply `"$profileFile`" --report `"$report`" --dry-run" $(if($server){1}else{0})
$text=Get-Content $report -Raw
Write-Output $text
if(-not $server -and $text -notmatch 'OK\s+UI-FILEEXT'){throw 'Profile dry run not reported'}
if($server -and $text -notmatch 'FAILED\s+UI-FILEEXT'){throw 'Server refusal not reported'}
Write-Output 'ARM64 unit tests and command line passed.'
exit 0
