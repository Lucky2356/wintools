param([Parameter(Mandatory=$true)][string]$EngineZip,[string]$Output='dist\portable')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version=(Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+(-rc\.\d+)?$'){throw 'Invalid version'}
$null=New-Item -ItemType Directory -Path $Output -Force
$outputPath=[IO.Path]::GetFullPath($Output)
$info=Join-Path $outputPath 'AssemblyInfo.cs'
$numeric=($version -split '-')[0]+'.0'
$text=@"
using System.Reflection;
[assembly: AssemblyTitle("Wintools Portable")]
[assembly: AssemblyProduct("Wintools")]
[assembly: AssemblyCompany("Lucky2356")]
[assembly: AssemblyVersion("$numeric")]
[assembly: AssemblyFileVersion("$numeric")]
[assembly: AssemblyInformationalVersion("$version")]
"@
[IO.File]::WriteAllText($info,$text,[Text.UTF8Encoding]::new($false))
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@(Get-ChildItem $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)+$info
$exe=Join-Path $outputPath 'WintoolsPortable.exe'
$icon=Join-Path $outputPath 'Wintools.ico'
& (Join-Path $PSScriptRoot 'build-icon.ps1') -Output $icon
$framework=Split-Path $compiler -Parent
& $compiler /nologo /target:winexe "/win32icon:$icon" "/resource:$icon,Wintools.Icon.ico" /platform:x64 /optimize+ /codepage:65001 "/out:$exe" "/win32manifest:$PSScriptRoot\app.manifest" "/resource:$EngineZip,Wintools.Engine.zip" "/resource:$PSScriptRoot\Shell.xaml,Wintools.Shell.xaml" /reference:System.dll /reference:System.Core.dll /reference:System.Security.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Xaml.dll "/reference:$framework\WPF\UIAutomationProvider.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" "/reference:$framework\WPF\WindowsBase.dll" "/reference:$framework\WPF\PresentationCore.dll" "/reference:$framework\WPF\PresentationFramework.dll" $sources
if($LASTEXITCODE -ne 0){throw 'Portable compilation failed'}
$hash=(Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($exe+'.sha256'),"$hash  WintoolsPortable.exe`n",[Text.Encoding]::ASCII)
Write-Output "Portable executable: $exe"
