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
  [string]$Root,
  [string]$Token
)

$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference    = 'SilentlyContinue'

function Out-KV($k, $v) { Write-Output ("{0}={1}" -f $k, $v) }

function Read-Journal($path) {
  $ErrorActionPreference = 'Stop'
  # A one-byte encoding preserves existing OEM bytes during status updates.
  $encoding = [System.Text.Encoding]::GetEncoding(28591)
  $lines = [System.IO.File]::ReadAllLines($path, $encoding)
  foreach ($line in $lines) {
    $p = $line -split '\|'
    if ($p.Count -ne 11 -or @($p | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0 -or
        $p[2] -notin @('REG','SVC','TASK','APPX','APPXDEEP','EDGE','PWR','NET','FS') -or
        $p[9] -notin @('PENDING','OK','FAILED','REVERTED','MANUAL')) {
      throw 'Malformed journal entry; journal was not changed.'
    }
  }
  return ,$lines
}

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
    if ($LASTEXITCODE -eq 0 -and $out -match '=\s*([0-3])\b') { Out-KV 'LASTACCESS' $Matches[1] } else { Out-KV 'LASTACCESS' 'unknown' }
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

  'ValidateData' {
    $ErrorActionPreference = 'Stop'
    $errors = New-Object System.Collections.Generic.List[string]
    $defs = Join-Path $Root 'data\tweaks.def'
    $protected = Join-Path $Root 'data\protected.def'
    $descriptions = Join-Path $Root 'data\descr.ru'
    foreach ($required in @($defs, $protected, $descriptions)) {
      if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        $errors.Add("missing required file: $required")
      }
    }
    if ($errors.Count -eq 0) {
      $ids = @{}
      $lineNo = 0
      foreach ($line in (Get-Content -LiteralPath $defs)) {
        $lineNo++
        if (-not $line -or $line.StartsWith('#')) { continue }
        $p = $line -split '\|'
        if ($p.Count -ne 9) { $errors.Add("tweaks.def:${lineNo}: expected 9 fields"); continue }
        if (@($p | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
          $errors.Add("tweaks.def:${lineNo}: empty field (use a placeholder)")
        }
        $id, $profile, $risk, $os, $type = $p[0..4]
        if ($id -notmatch '^[A-Z][A-Z0-9-]{1,63}$') { $errors.Add("tweaks.def:${lineNo}: invalid id '$id'") }
        elseif ($ids.ContainsKey($id)) { $errors.Add("tweaks.def:${lineNo}: duplicate id '$id'") }
        else { $ids[$id] = $true }
        if ($profile -notin @('core','balanced','extended','manual')) { $errors.Add("tweaks.def:${lineNo}: invalid profile '$profile'") }
        if ($risk -notin @('low','med','high')) { $errors.Add("tweaks.def:${lineNo}: invalid risk '$risk'") }
        if ($os -notin @('any','win10','win11')) { $errors.Add("tweaks.def:${lineNo}: invalid os '$os'") }
        if ($type -notin @('REG','SVC','TASK','APPX','EDGE')) { $errors.Add("tweaks.def:${lineNo}: invalid type '$type'") }
        if ($line -match '[!&"%<>^\x00-\x1f]') { $errors.Add("tweaks.def:${lineNo}: unsafe CMD metacharacter") }
      }
      $lineNo = 0
      foreach ($line in (Get-Content -LiteralPath $protected)) {
        $lineNo++
        if (-not $line -or $line.StartsWith('#')) { continue }
        if ($line -notmatch '^(SVC|APPX|KEY):[^|!&"%<>^\x00-\x1f]+$') { $errors.Add("protected.def:${lineNo}: invalid entry") }
      }
      $cp866 = [System.Text.Encoding]::GetEncoding(866)
      $descIds = @{}
      $lineNo = 0
      foreach ($line in [System.IO.File]::ReadAllLines($descriptions, $cp866)) {
        $lineNo++
        if (-not $line -or $line.StartsWith('#')) { continue }
        $p = $line -split '\|'
        if ($p.Count -ne 6) { $errors.Add("descr.ru:${lineNo}: expected 6 fields"); continue }
        if (@($p | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
          $errors.Add("descr.ru:${lineNo}: empty field (use -)")
        }
        if ($descIds.ContainsKey($p[0])) { $errors.Add("descr.ru:${lineNo}: duplicate id '$($p[0])'") }
        else { $descIds[$p[0]] = $true }
      }
      foreach ($id in $ids.Keys) { if (-not $descIds.ContainsKey($id)) { $errors.Add("descr.ru: missing id '$id'") } }
    }
    foreach ($message in $errors) { Write-Output "ERROR=$message" }
    if ($errors.Count -gt 0) { exit 1 }
    break
  }

  'AcquireLock' {
    $ErrorActionPreference = 'Stop'
    $path = Join-Path $Root 'state\run.lock'
    $record = "{0}|{1}" -f (Get-Date).ToString('s'), $Token
    try {
      $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
      $bytes = [System.Text.Encoding]::ASCII.GetBytes($record)
      $stream.Write($bytes, 0, $bytes.Length)
      exit 0
    } catch {
      exit 1
    } finally {
      if ($stream) { $stream.Dispose() }
    }
  }

  'ValidateJournal' {
    try { $null = Read-Journal $Full } catch { Write-Output "ERROR=$($_.Exception.Message)"; exit 4 }
    break
  }

  'CheckRegistryAbsent' {
    $ErrorActionPreference = 'Stop'
    try {
      $keyPath = $Full -replace '^HKCU\\','HKEY_CURRENT_USER\' -replace '^HKLM\\','HKEY_LOCAL_MACHINE\' -replace '^HKU\\','HKEY_USERS\'
      $key = Get-Item -LiteralPath ('Registry::' + $keyPath)
      try {
        if (-not $Name) { exit 4 }
        if ($Name -eq '@DEFAULT@') { $Name = '' }
        if ($Name -in $key.GetValueNames()) { exit 4 }
      } finally { $key.Close() }
    } catch [System.Management.Automation.ItemNotFoundException] { exit 0 }
      catch { exit 4 }
    break
  }

  'ValidateRevertOrder' {
    try {
      $lines = Read-Journal $Full
      $blocked = @{}
      for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        $r = $lines[$i] -split '\|'
        if ($r[9] -in @('REVERTED','MANUAL')) { continue }
        $type = $r[2] -replace '^APPXDEEP$', 'APPX'
        $object = "$type|$($r[3])|$($r[4])"
        $selected = (-not $env:OPT_RUN -or $r[0] -eq $env:OPT_RUN) -and
                    (-not $env:OPT_IDS -or $r[1] -in ($env:OPT_IDS -split '\s+'))
        if ($selected -and $blocked.ContainsKey($object)) {
          throw "Revert newer run $($blocked[$object]) for $($r[1]) first."
        }
        if ($selected -and $type -eq 'REG' -and $r[5] -eq 'ABSENT' -and $r[8] -ne '0') {
          foreach ($later in $blocked.Keys) {
            $parts = $later -split '\|'
            if ($parts[0] -eq 'REG' -and ($parts[1] -eq $r[8] -or $parts[1].StartsWith($r[8] + '\',[StringComparison]::OrdinalIgnoreCase))) {
              throw "Revert newer run $($blocked[$later]) before removing its parent key."
            }
          }
        }
        if (-not $selected) { $blocked[$object] = $r[0] }
      }
    } catch { Write-Output "ERROR=$($_.Exception.Message)"; exit 4 }
    break
  }

  'VerifyJournal' {
    $ErrorActionPreference = 'Stop'
    function Write-Verification($message) {
      Write-Output $message
      if ($env:LOGFILE) {
        [IO.File]::AppendAllText($env:LOGFILE, $message + [Environment]::NewLine, [Console]::OutputEncoding)
      }
    }
    try {
      $lines = Read-Journal $Full
      $catalogue = @{}
      foreach ($line in Get-Content -LiteralPath (Join-Path $Root 'data\tweaks.def')) {
        if (-not $line -or $line.StartsWith('#')) { continue }
        $p = $line -split '\|'
        $catalogue[$p[0]] = $p
      }
      $matched = 0; $drift = 0; $unknown = 0
      foreach ($line in $lines) {
        $r = $line -split '\|'
        if ($env:OPT_RUN -and $r[0] -ne $env:OPT_RUN) { continue }
        if ($env:OPT_IDS -and $r[1] -notin ($env:OPT_IDS -split '\s+')) { continue }
        if ($r[9] -eq 'REVERTED') { continue }
        $result = 'UNSUPPORTED'
        $detail = 'no verifier for this entry'
        if ($r[9] -ne 'OK') {
          $detail = 'journal status ' + $r[9]
        } else {
          $def = $catalogue[$r[1]]
          # System-change entries are built into the CMD engine, not tweaks.def.
          $system = @{
            'SYS-FASTSTARTUP-OFF'='REG|HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power|HiberbootEnabled|REG_DWORD|0'
            'SYS-LONGPATHS'='REG|HKLM\SYSTEM\CurrentControlSet\Control\FileSystem|LongPathsEnabled|REG_DWORD|1'
            'SYS-HIBERNATE-OFF'='PWR|HIBERNATE|-|REG_DWORD|0'
            'SYS-TCP-AUTOTUNING'='NET|autotuninglevel|-|NETSH|Normal'
            'SYS-LASTACCESS'='FS|disablelastaccess|-|FSUTIL|1'
          }
          if ($system.ContainsKey($r[1])) { $def = ("$($r[1])|manual|low|any|" + $system[$r[1]]) -split '\|' }
          if ($r[1] -eq 'SYS-POWER-SCHEME' -and $r[3] -match '^[0-9a-f-]{36}$') {
            $def = @($r[1],'manual','low','any','PWR',$r[3],'SCHEME','SCHEME','-')
          }
          if ($def -and ($def[4] -eq $r[2] -or ($def[4] -eq 'APPX' -and $r[2] -eq 'APPXDEEP'))) {
            # A renamed target must not silently verify a different object.
            $target = $def[5]
            if ($target.StartsWith('@HKCU@')) {
              $suffix = $target.Substring(6)
              if ($r[3] -eq ('HKCU' + $suffix) -or $r[3] -match ('^HKU\\S-1-[0-9-]+' + [regex]::Escape($suffix) + '$')) {
                $target = $r[3]
              }
            }
            if ($target -eq $r[3] -and ($def[6] -eq $r[4] -or $def[4] -eq 'APPX')) {
              try {
                $same = $false
                $detail = 'compared with current catalogue'
                switch ($r[2]) {
                  'REG' {
                    $keyPath = $r[3] -replace '^HKCU\\','HKEY_CURRENT_USER\' -replace '^HKLM\\','HKEY_LOCAL_MACHINE\' -replace '^HKU\\','HKEY_USERS\'
                    $key = Get-Item -LiteralPath ('Registry::' + $keyPath)
                    try {
                      $valueName = $r[4]
                      if ($valueName -eq '@DEFAULT@') { $valueName = '' }
                      if ($valueName -notin $key.GetValueNames()) { $detail = 'registry value absent'; break }
                      $kind = $key.GetValueKind($valueName).ToString()
                      $value = $key.GetValue($valueName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                      if ($def[7] -eq 'REG_DWORD' -and $kind -eq 'DWord') {
                        $want = if ($def[8] -like '0x*') { [Convert]::ToInt64($def[8].Substring(2),16) } else { [int64]$def[8] }
                        $same = (([int64]$value -band 4294967295) -eq $want)
                      } elseif ($def[7] -eq 'REG_SZ' -and $kind -eq 'String') {
                        $want = $def[8]
                        if ($want -eq '@EMPTY@') { $want = '' }
                        $same = ([string]$value -ceq $want)
                      }
                    } finally { if ($key) { $key.Close() } }
                  }
                  'SVC' {
                    $service = Get-CimInstance Win32_Service -Filter ("Name='{0}'" -f $r[3])
                    $modes = @{disabled='Disabled'; auto='Auto'; demand='Manual'; boot='Boot'; system='System'}
                    if (-not $modes.ContainsKey($def[8])) { throw 'Unsupported service start mode' }
                    $same = $service -and $service.StartMode -eq $modes[$def[8]]
                    if (-not $service) { $detail = 'service absent' }
                  }
                  'TASK' {
                    $split = $r[3].LastIndexOf('\')
                    $task = Get-ScheduledTask -TaskPath $r[3].Substring(0,$split+1) -TaskName $r[3].Substring($split+1)
                    $same = $task -and $task.State -eq 'Disabled'
                    if (-not $task) { $detail = 'task absent' }
                  }
                  { $_ -in @('APPX','APPXDEEP') } {
                    $same = @(Get-AppxPackage -Name $r[3] -ErrorAction Stop).Count -eq 0
                    if ($r[2] -eq 'APPXDEEP') {
                      $provisioned = @(Get-AppxProvisionedPackage -Online -ErrorAction Stop | Where-Object { $_.DisplayName -eq $r[3] })
                      $dataPath = Join-Path $env:LOCALAPPDATA ('Packages\' + $r[4])
                      $activation = 'Registry::HKEY_CURRENT_USER\Software\Classes\ActivatableClasses\Package\' + $r[6]
                      $same = $same -and $provisioned.Count -eq 0 -and -not (Test-Path -LiteralPath $dataPath) -and -not (Test-Path -LiteralPath $activation)
                    }
                  }
                  'NET' {
                    $tcp = Get-NetTCPSetting -SettingName InternetCustom -ErrorAction Stop
                    if (-not $tcp.AutoTuningLevelLocal) { $tcp = Get-NetTCPSetting -SettingName Internet -ErrorAction Stop }
                    if (-not $tcp.AutoTuningLevelLocal) { throw 'TCP state unavailable' }
                    $same = $tcp.AutoTuningLevelLocal -eq 'Normal'
                  }
                  'FS' {
                    $output = (fsutil behavior query disablelastaccess) -join ' '
                    if ($LASTEXITCODE -ne 0 -or $output -notmatch '=\s*([0-3])\b') { throw 'NTFS state unavailable' }
                    $same = $Matches[1] -eq '1'
                  }
                  'PWR' {
                    if ($r[3] -eq 'HIBERNATE') {
                      $power = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' -Name HibernateEnabled
                      $same = $power.HibernateEnabled -eq 0
                    } else {
                      $active = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes' -Name ActivePowerScheme
                      $indexes = @(Get-CimInstance -Namespace root\cimv2\power -ClassName Win32_PowerSettingDataIndex)
                      $wanted = @{
                        '3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e'=1200 # monitor, seconds
                        '29f6c1db-86da-48c5-9fdb-f2b67b1f44da'=0    # standby
                        '6738e2c4-e8a5-4a42-b16a-e040e769756e'=0    # disk
                      }
                      $same = $active.ActivePowerScheme -eq $r[3]
                      foreach ($setting in $wanted.Keys) {
                        $instance = 'Microsoft:PowerSettingDataIndex\{' + $r[3] + '}\AC\{' + $setting + '}'
                        $index = @($indexes | Where-Object { $_.InstanceID -eq $instance })
                        if ($index.Count -ne 1) { throw 'Power setting unavailable' }
                        $same = $same -and $index[0].SettingIndexValue -eq $wanted[$setting]
                      }
                    }
                  }
                  default { throw [NotSupportedException]::new('Unsupported entry type') }
                }
                $result = if ($same) { 'MATCH' } else { 'DRIFT' }
              } catch [System.Management.Automation.ItemNotFoundException] {
                $result = 'DRIFT'; $detail = 'object absent'
              } catch [NotSupportedException] {
                $result = 'UNSUPPORTED'; $detail = $_.Exception.Message
              } catch {
                $result = 'ERROR'
                $detail = 'state could not be checked: ' + $_.Exception.Message
              }
            } else { $detail = 'catalogue target or name changed' }
          }
        }
        switch ($result) { 'MATCH' { $matched++ }; 'DRIFT' { $drift++ }; default { $unknown++ } }
        Write-Verification ("VERIFY {0} {1}: {2}" -f $r[1], $result, $detail)
      }
      Write-Verification "Verify done: matched=$matched drift=$drift unchecked=$unknown. System settings were not modified."
      if ($drift -gt 0 -or $unknown -gt 0) { exit 4 }
    } catch { Write-Output "ERROR=$($_.Exception.Message)"; exit 4 }
    break
  }

  'JournalSetResult' {
    $ErrorActionPreference = 'Stop'
    $temporary = $null
    try {
      if ($Description -notin @('OK','FAILED','REVERTED','MANUAL')) { throw 'Invalid journal result.' }
      $lines = Read-Journal $Full
      $matched = 0
      for ($i = 0; $i -lt $lines.Count; $i++) {
        $p = $lines[$i] -split '\|'
        if ($p[0] -eq $Token -and $p[1] -eq $Name) {
          $p[9] = $Description
          $lines[$i] = $p -join '|'
          $matched++
        }
      }
      if ($matched -eq 0) { throw 'Journal entry not found.' }
      $temporary = $Full + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
      [System.IO.File]::WriteAllLines($temporary, $lines, [System.Text.Encoding]::GetEncoding(28591))
      # Same-directory atomic replacement: failure leaves the original intact.
      [System.IO.File]::Replace($temporary, $Full, [System.Management.Automation.Language.NullString]::Value)
    } catch {
      Write-Output "ERROR=$($_.Exception.Message)"
      exit 4
    } finally {
      if ($temporary -and [System.IO.File]::Exists($temporary)) { [System.IO.File]::Delete($temporary) }
    }
    break
  }

  'ReleaseLock' {
    $path = Join-Path $Root 'state\run.lock'
    if (Test-Path -LiteralPath $path) {
      $owner = (Get-Content -LiteralPath $path -ErrorAction SilentlyContinue) -split '\|', 2
      if ($owner.Count -eq 2 -and $owner[1] -eq $Token) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
      }
    }
    break
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
