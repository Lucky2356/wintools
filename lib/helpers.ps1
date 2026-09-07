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
          if ($def -and $def[4] -eq $r[2]) {
            # A renamed target must not silently verify a different object.
            $target = $def[5]
            if ($target.StartsWith('@HKCU@')) {
              $suffix = $target.Substring(6)
              if ($r[3] -eq ('HKCU' + $suffix) -or $r[3] -match ('^HKU\\S-1-[0-9-]+' + [regex]::Escape($suffix) + '$')) {
                $target = $r[3]
              }
            }
            if ($target -eq $r[3] -and $def[6] -eq $r[4]) {
              try {
                $same = $false
                switch ($r[2]) {
                  'REG' {
                    $keyPath = $r[3] -replace '^HKCU\\','HKEY_CURRENT_USER\' -replace '^HKLM\\','HKEY_LOCAL_MACHINE\' -replace '^HKU\\','HKEY_USERS\'
                    $key = Get-Item -LiteralPath ('Registry::' + $keyPath)
                    try {
                      $valueName = $r[4]
                      if ($valueName -eq '@DEFAULT@') { $valueName = '' }
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
                  }
                  'TASK' {
                    $split = $r[3].LastIndexOf('\')
                    $task = Get-ScheduledTask -TaskPath $r[3].Substring(0,$split+1) -TaskName $r[3].Substring($split+1)
                    $same = $task -and $task.State -eq 'Disabled'
                  }
                  default { throw 'Unsupported entry type' }
                }
                $result = if ($same) { 'MATCH' } else { 'DRIFT' }
                $detail = 'compared with current catalogue'
              } catch {
                $result = 'UNSUPPORTED'
                $detail = 'state could not be checked: ' + $_.Exception.Message
              }
            } else { $detail = 'catalogue target or name changed' }
          }
        }
        switch ($result) { 'MATCH' { $matched++ }; 'DRIFT' { $drift++ }; default { $unknown++ } }
        Write-Verification ("VERIFY {0} {1}: {2}" -f $r[1], $result, $detail)
      }
      Write-Verification "Verify done: matched=$matched drift=$drift unsupported=$unknown. System settings were not modified."
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
      # Reapply keeps an earlier run's original; it has no new row to mark.
      if ($matched -eq 0) {
        if ($Description -in @('OK','FAILED') -and @($lines | Where-Object {
          $p = $_ -split '\|'; $p[1] -eq $Name -and $p[9] -eq 'OK'
        }).Count -gt 0) { exit 0 }
        throw 'Journal entry not found.'
      }
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
