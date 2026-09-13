$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'Disposable GitHub-hosted runner required'}
$root=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $env:RUNNER_TEMP ('WintoolsStore_'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $fixture
$packageRoot=Join-Path $fixture 'package'
$null=New-Item -ItemType Directory -Path $packageRoot
$name='WintoolsFixture.'+[guid]::NewGuid().ToString('N')
$publisher='CN='+$name
$certificate=$null;$trust=$null;$fullName=$null;$started=Get-Date
function Invoke-Package($action,$package,$family,$expected=0) {
    $request=@{Action=$action;FullName=$package;FamilyName=$family}|ConvertTo-Json -Compress
    $data=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($request))
    $script='$requestData='''+$data+"'`n"+[IO.File]::ReadAllText((Join-Path $root 'ui\StoreApps.ps1'))
    $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    $info=[Diagnostics.ProcessStartInfo]::new("$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe",'-NoProfile -NonInteractive -EncodedCommand '+$encoded)
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[Text.Encoding]::UTF8;$info.StandardErrorEncoding=[Text.Encoding]::UTF8
    $process=[Diagnostics.Process]::Start($info)
    try {
        $output=$process.StandardOutput.ReadToEndAsync();$errorOutput=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(120000)){throw "Package fixture timed out: $action"}
        if($process.ExitCode -ne $expected){throw "Package $action exit $($process.ExitCode): $($errorOutput.GetAwaiter().GetResult())"}
        return $output.GetAwaiter().GetResult()
    } finally {$process.Dispose()}
}
try {
    $sdk=Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory | Where-Object {Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')} | Sort-Object Name -Descending | Select-Object -First 1
    if(-not $sdk){throw 'Windows SDK MakeAppx is required for the package integration fixture'}
    $makeappx=Join-Path $sdk.FullName 'x64\makeappx.exe';$signtool=Join-Path $sdk.FullName 'x64\signtool.exe'
    $source=Join-Path $fixture 'Fixture.cs';[IO.File]::WriteAllText($source,'internal static class Fixture { static void Main() {} }')
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$packageRoot\Fixture.exe" $source
    if($LASTEXITCODE -ne 0){throw 'Fixture compilation failed'}
    Add-Type -AssemblyName System.Drawing
    foreach($size in @(44,150)){$bitmap=[Drawing.Bitmap]::new($size,$size);try{$bitmap.Save((Join-Path $packageRoot "logo$size.png"),[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}}
    $manifest=@"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap rescap">
 <Identity Name="$name" Publisher="$publisher" Version="1.0.0.0" ProcessorArchitecture="x64" />
 <Properties><DisplayName>Wintools disposable fixture</DisplayName><PublisherDisplayName>Wintools tests</PublisherDisplayName><Logo>logo44.png</Logo></Properties>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.14393.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
 <Resources><Resource Language="en-us" /></Resources>
 <Applications><Application Id="App" Executable="Fixture.exe" EntryPoint="Windows.FullTrustApplication"><uap:VisualElements DisplayName="Wintools disposable fixture" Description="Temporary CI test application" BackgroundColor="transparent" Square150x150Logo="logo150.png" Square44x44Logo="logo44.png" /></Application></Applications>
 <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@
    [IO.File]::WriteAllText((Join-Path $packageRoot 'AppxManifest.xml'),$manifest,[Text.UTF8Encoding]::new($false))
    $appx=Join-Path $fixture 'Fixture.msix'
    & $makeappx pack /d $packageRoot /p $appx /o
    if($LASTEXITCODE -ne 0){throw 'Fixture packaging failed'}
    $certificate=New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature -FriendlyName $name -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
    $trust=[Security.Cryptography.X509Certificates.X509Store]::new('TrustedPeople','LocalMachine');$trust.Open('ReadWrite');$trust.Add($certificate)
    & $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $appx
    if($LASTEXITCODE -ne 0){throw 'Fixture signing failed'}
    Add-AppxPackage -Path $appx
    $package=Get-AppxPackage -Name $name;$fullName=$package.PackageFullName;$family=$package.PackageFamilyName
    if(-not $fullName){throw 'Fixture installation missing'}
    $inventory=Invoke-Package 'inventory' '' '' | ConvertFrom-Json
    $row=@($inventory.Rows | Where-Object {$_.FullName -eq $fullName})
    if($row.Count -ne 1 -or $row[0].Protected){throw 'Fixture not available in inventory'}
    $null=Invoke-Package 'register' $fullName $family
    if($row[0].ResetSupported){
        $localState=Join-Path $env:LOCALAPPDATA "Packages\$family\LocalState"
        $null=New-Item -ItemType Directory -Path $localState -Force
        $marker=Join-Path $localState 'wintools-fixture.txt';[IO.File]::WriteAllText($marker,'disposable fixture data')
        $null=Invoke-Package 'reset' $fullName $family
        if(Test-Path $marker){throw 'Reset did not remove fixture data'}
    }else{Write-Output 'Reset command not available on this Windows; capability is reported as unavailable.'}
    $null=Invoke-Package 'remove' $fullName $family
    if(Get-AppxPackage -Name $name){throw 'Fixture removal did not take effect'}
    $null=Invoke-Package 'remove' $fullName $family 4
    Write-Output 'Store inventory, registration, supported reset, removal and stale identity checks passed.'
} catch {
    Write-Output 'Package deployment diagnostics:'
    Get-AppxPackage -Name $name | Format-List Name,PackageFullName,Status,SignatureKind,InstallLocation
    Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-AppXDeploymentServer/Operational';StartTime=$started} -MaxEvents 100 -ErrorAction SilentlyContinue | Where-Object {$_.Message -like "*$name*"} | Select-Object -First 15 TimeCreated,Id,Message | Format-List
    throw
} finally {
    Get-AppxPackage -Name $name | ForEach-Object {Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Continue}
    if($certificate){if($trust){$trust.Remove($certificate)};Remove-Item -LiteralPath ('Cert:\CurrentUser\My\'+$certificate.Thumbprint) -ErrorAction Continue}
    if($trust){$trust.Close()}
}
