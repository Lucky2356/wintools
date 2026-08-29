<#
  helpers.ps1 - the only PowerShell in this project. Always invoked from CMD.

  Why it exists: sc.exe, schtasks.exe, powercfg.exe and netsh.exe all print
  LOCALIZED output, so parsing them by token position breaks on a non-English
  Windows. Every piece of *state* is therefore read through CIM / cmdlets and
  printed back as stable ASCII "KEY=VALUE" lines that CMD can parse safely.
  Mutations stay in CMD with the native tools.
#>
param(
  [Parameter(Mandatory=$true)][string]$Action,
  [string]$Name,
  [string]$Full,
  [string]$Description,
  [string]$Root
)

$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference    = 'SilentlyContinue'

function Out-KV($k, $v) { Write-Output ("{0}={1}" -f $k, $v) }

switch ($Action) {

  'GetEnvironment' {
    $cs = Get-CimInstance Win32_ComputerSystem
    Out-KV 'PARTOFDOMAIN' ([int][bool]$cs.PartOfDomain)

    $bat = @(Get-CimInstance Win32_Battery)
    Out-KV 'ISLAPTOP' ([int]($bat.Count -gt 0))

    $ssd = 0
    try {
      $letter = $env:SystemDrive.Substring(0,1)
      $media  = (Get-Partition -DriveLetter $letter | Get-Disk | Get-PhysicalDisk).MediaType
      if ($media -match 'SSD|NVMe') { $ssd = 1 }
    } catch { $ssd = 0 }
    Out-KV 'SYSTEMDISKSSD' $ssd

    $xbox = 0
    try {
      $d = @(Get-PnpDevice -PresentOnly | Where-Object { $_.FriendlyName -like '*Xbox*Controller*' })
      if ($d.Count -gt 0) { $xbox = 1 }
    } catch { $xbox = 0 }
    Out-KV 'XBOXCONTROLLER' $xbox
    break
  }

  'GetService' {
    $s = Get-CimInstance Win32_Service -Filter ("Name='{0}'" -f $Name)
    if (-not $s) { Out-KV 'EXISTS' 0; break }
    Out-KV 'EXISTS'    1
    Out-KV 'STARTMODE' $s.StartMode      # Boot|System|Auto|Manual|Disabled
    Out-KV 'STATE'     $s.State          # Running|Stopped|...
    $d = (Get-ItemProperty ("HKLM:\SYSTEM\CurrentControlSet\Services\{0}" -f $Name)).DelayedAutostart
    Out-KV 'DELAYED' ([int][bool]$d)
    break
  }

  'GetTask' {
    $i = $Full.LastIndexOf('\')
    $p = $Full.Substring(0, $i + 1)
    $n = $Full.Substring($i + 1)
    $t = Get-ScheduledTask -TaskPath $p -TaskName $n
    if (-not $t) { Out-KV 'EXISTS' 0; break }
    Out-KV 'EXISTS' 1
    Out-KV 'STATE'  $t.State             # Ready|Disabled|Running|Queued
    break
  }

  'GetAppx' {
    $p = @(Get-AppxPackage -Name $Name -ErrorAction SilentlyContinue)[0]
    if (-not $p) { Out-KV 'EXISTS' 0; break }
    Out-KV 'EXISTS'       1
    Out-KV 'FULLNAME'     $p.PackageFullName
    Out-KV 'FAMILYNAME'   $p.PackageFamilyName
    Out-KV 'INSTALLLOC'   $p.InstallLocation
    Out-KV 'NONREMOVABLE' ([int][bool]$p.NonRemovable)
    break
  }

  'RemoveAppx' {
    # Per user only: no -AllUsers, so the provisioned payload stays on disk and
    # RestoreAppx can re-register it without downloading anything.
    try { Remove-AppxPackage -Package $Full -ErrorAction Stop; exit 0 } catch { exit 1 }
  }

  'RestoreAppx' {
    # $Full = InstallLocation, $Name = PackageFamilyName
    try {
      if ($Full -and (Test-Path -LiteralPath (Join-Path $Full 'AppxManifest.xml'))) {
        Add-AppxPackage -Register (Join-Path $Full 'AppxManifest.xml') -DisableDevelopmentMode -ErrorAction Stop
        exit 0
      }
    } catch { }
    try {
      Add-AppxPackage -RegisterByFamilyName -MainPackage $Name -ErrorAction Stop
      exit 0
    } catch { exit 1 }
  }

  'GetProvisioned' {
    # Provisioned entry = the copy Windows hands to every NEW user account.
    $p = @(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue |
           Where-Object { $_.DisplayName -eq $Name })[0]
    if (-not $p) { Out-KV 'EXISTS' 0; break }
    Out-KV 'EXISTS'      1
    Out-KV 'PACKAGENAME' $p.PackageName
    break
  }

  'RemoveProvisioned' {
    try {
      Remove-AppxProvisionedPackage -Online -PackageName $Full -ErrorAction Stop | Out-Null
      exit 0
    } catch { exit 1 }
  }

  'GetEdge' {
    $base = 'C:\Program Files (x86)\Microsoft\Edge\Application'
    if (-not (Test-Path -LiteralPath $base)) { Out-KV 'EXISTS' 0; break }
    $v = Get-ChildItem -LiteralPath $base -Directory -ErrorAction SilentlyContinue |
         Where-Object { $_.Name -match '^\d+\.' } |
         Sort-Object { [version]$_.Name } | Select-Object -Last 1
    if (-not $v) { Out-KV 'EXISTS' 0; break }
    $setup = Join-Path $v.FullName 'Installer\setup.exe'
    if (-not (Test-Path -LiteralPath $setup)) { Out-KV 'EXISTS' 0; break }
    Out-KV 'EXISTS'  1
    Out-KV 'VERSION' $v.Name
    Out-KV 'SETUP'   $setup
    break
  }

  'Inventory' {
    # Reads data/tweaks.def and works out, for every catalogue entry, whether
    # the object exists on THIS machine and whether it is already in the wanted
    # state. Result goes to state/inventory.dat as  ID|PRESENT|ALREADY|INFO.
    $def = Join-Path $Root 'data\tweaks.def'
    $svc = @{}
    Get-CimInstance Win32_Service -ErrorAction SilentlyContinue |
      ForEach-Object { $svc[$_.Name] = $_.StartMode }
    $appx = @{}
    Get-AppxPackage -ErrorAction SilentlyContinue |
      ForEach-Object { $appx[$_.Name] = 1 }
    $task = @{}
    Get-ScheduledTask -ErrorAction SilentlyContinue |
      ForEach-Object { $task[($_.TaskPath + $_.TaskName)] = $_.State }
    $edge = Test-Path -LiteralPath 'C:\Program Files (x86)\Microsoft\Edge\Application'

    $out = New-Object System.Collections.Generic.List[string]
    foreach ($line in (Get-Content -LiteralPath $def)) {
      if ($line -notmatch '^[A-Za-z]') { continue }
      $p = $line -split '\|'
      if ($p.Count -lt 9) { continue }
      $id = $p[0]; $type = $p[4]; $target = $p[5]; $vname = $p[6]; $want = $p[8]
      $present = 1; $already = 0; $info = '-'
      switch ($type) {
        'SVC' {
          if ($svc.ContainsKey($target)) {
            $info = $svc[$target]
            if ($info -eq 'Disabled') { $already = 1 }
          } else { $present = 0 }
        }
        'APPX' { if (-not $appx.ContainsKey($target)) { $present = 0 } }
        'TASK' {
          if ($task.ContainsKey($target)) {
            $info = [string]$task[$target]
            if ($info -eq 'Disabled') { $already = 1 }
          } else { $present = 0 }
        }
        'EDGE' { if (-not $edge) { $present = 0 } }
        'REG' {
          # Turn the hive prefix into a PS drive with plain string ops:
          # -replace is unusable here, a trailing backslash breaks the regex.
          if     ($target.StartsWith('@HKCU@')) { $key = 'HKCU:' + $target.Substring(6) }
          elseif ($target.StartsWith('HKLM\'))  { $key = 'HKLM:' + $target.Substring(4) }
          elseif ($target.StartsWith('HKCU\'))  { $key = 'HKCU:' + $target.Substring(4) }
          else                                   { $key = $target }
          $vn = $vname
          if ($vn -eq '@DEFAULT@') { $vn = '(default)' }
          try {
            $cur = (Get-ItemProperty -LiteralPath $key -Name $vn -ErrorAction Stop).$vn
            $w = $want
            if ($w -eq '@EMPTY@') { $w = '' }
            if ("$cur" -eq "$w") {
              $already = 1
            } else {
              # Compare DWORDs numerically: 0xffffffff comes back as -1
              $cn = 0; $wn = $null
              if ([int64]::TryParse("$cur", [ref]$cn)) {
                if ($w -like '0x*') { $wn = [convert]::ToInt64($w.Substring(2), 16) }
                else { $t = 0; if ([int64]::TryParse($w, [ref]$t)) { $wn = $t } }
                if ($null -ne $wn) {
                  if (($cn -band 0xFFFFFFFF) -eq ($wn -band 0xFFFFFFFF)) { $already = 1 }
                }
              }
            }
          } catch { }
        }
      }
      $out.Add(('{0}|{1}|{2}|{3}' -f $id, $present, $already, $info))
    }
    $dir = Join-Path $Root 'state'
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $enc = New-Object System.Text.ASCIIEncoding
    [System.IO.File]::WriteAllLines((Join-Path $dir 'inventory.dat'), $out, $enc)
    Out-KV 'ITEMS' $out.Count
    break
  }

  'GetTcpAutotuning' {
    $v = (Get-NetTCPSetting -SettingName InternetCustom).AutoTuningLevelLocal
    if (-not $v) { $v = (Get-NetTCPSetting -SettingName Internet).AutoTuningLevelLocal }
    if (-not $v) { Out-KV 'AUTOTUNING' 'unknown' } else { Out-KV 'AUTOTUNING' $v }
    break
  }

  'GetLastAccess' {
    $out = (fsutil behavior query disablelastaccess) -join ' '
    if ($out -match '=\s*(\d+)') { Out-KV 'LASTACCESS' $Matches[1] } else { Out-KV 'LASTACCESS' 'unknown' }
    break
  }

  'DirStat' {
    $days = 0
    if ($Name) { [int]::TryParse($Name, [ref]$days) | Out-Null }
    $cut = (Get-Date).AddDays(-$days)
    $n = 0; $b = 0
    if (Test-Path -LiteralPath $Full) {
      Get-ChildItem -LiteralPath $Full -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $cut } |
        ForEach-Object { $n++; $b += $_.Length }
    }
    Out-KV 'FILES' $n
    Out-KV 'SIZEMB' ([int]($b / 1MB))
    break
  }

  'DiskFree' {
    $d = Get-PSDrive -Name $env:SystemDrive.Substring(0,1)
    Out-KV 'FREEMB' ([int]($d.Free / 1MB))
    break
  }

  'RestorePoint' {
    try {
      Checkpoint-Computer -Description $Description -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
      exit 0
    } catch {
      exit 1
    }
  }

  'WriteManifest' {
    $journal = Join-Path $Root 'state\applied.dat'
    $cols = @('runId','id','type','target','name','prevState','prevType','prevData',
              'keyCreated','result','timestamp')
    $rows = @()
    if (Test-Path $journal) {
      $rows = Get-Content -LiteralPath $journal | Where-Object { $_ -match '\|' } | ForEach-Object {
        $v = $_ -split '\|'
        $o = [ordered]@{}
        for ($i = 0; $i -lt $cols.Count; $i++) { $o[$cols[$i]] = $(if ($i -lt $v.Count) { $v[$i] } else { '' }) }
        [pscustomobject]$o
      }
    }
    $os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $manifest = [pscustomobject]@{
      schema  = 'wintweaks/1'
      note    = 'Human-readable mirror of state/applied.dat. The .dat file is the source of truth for revert.'
      host    = $env:COMPUTERNAME
      written = (Get-Date).ToString('s')
      os      = [pscustomobject]@{
                  build   = $os.CurrentBuild
                  ubr     = $os.UBR
                  display = $os.DisplayVersion
                  edition = $os.EditionID
                }
      applied  = @($rows | Where-Object { $_.result -eq 'OK' }).Count
      reverted = @($rows | Where-Object { $_.result -eq 'REVERTED' }).Count
      entries  = $rows
    }
    $json = $manifest | ConvertTo-Json -Depth 6
    # PS 5.1 renders an empty array as {} - keep the contract a JSON array
    if (@($rows).Count -eq 0) { $json = $json -replace '"entries":\s*\{\s*\}', '"entries":  []' }
    # no BOM, so ordinary JSON parsers are happy
    [System.IO.File]::WriteAllText((Join-Path $Root 'state\manifest.json'), $json,
                                   (New-Object System.Text.UTF8Encoding($false)))
    break
  }

  default {
    Write-Error ("Unknown action: {0}" -f $Action)
    exit 2
  }
}
exit 0
