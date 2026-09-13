$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
try {
    $request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($requestData)) | ConvertFrom-Json
    $packages=@(Get-AppxPackage -PackageTypeFilter Main)
    if($request.Action -eq 'inventory') {
        $errors=@(); $starts=@()
        try {$starts=@(Get-StartApps)} catch {$errors+='Названия и команды запуска части приложений недоступны: '+$_.Exception.Message}
        $reset=$null -ne (Get-Command Reset-AppxPackage -ErrorAction SilentlyContinue)
        $rows=@(foreach($package in $packages) {
            $family=[string]$package.PackageFamilyName
            $entries=@($starts | Where-Object {([string]$_.AppID).StartsWith($family+'!',[StringComparison]::OrdinalIgnoreCase)} | ForEach-Object {@{Name=[string]$_.Name;Id=[string]$_.AppID}})
            $names=@($entries | ForEach-Object {$_.Name} | Select-Object -Unique)
            $name=if($names.Count){$names -join ' / '}else{[string]$package.Name}
            @{FullName=[string]$package.PackageFullName;FamilyName=$family;Name=$name;Version=[string]$package.Version;Publisher=[string]$package.Publisher;Location=[string]$package.InstallLocation;Protected=($package.NonRemovable -ne $false -or [string]$package.SignatureKind -eq 'System');ResetSupported=$reset;Entries=$entries}
        })
        @{Rows=$rows;Errors=$errors} | ConvertTo-Json -Depth 6 -Compress
    } else {
        if($request.Action -notin @('remove','reset','register')){throw 'Неизвестное действие пакета.'}
        $matches=@($packages | Where-Object {$_.PackageFullName -ceq $request.FullName -and $_.PackageFamilyName -ceq $request.FamilyName})
        if($matches.Count -ne 1){throw 'Пакет удалён или обновлён. Обновите список и выберите его заново.'}
        $package=$matches[0]
        if($package.NonRemovable -ne $false -or [string]$package.SignatureKind -eq 'System'){throw 'Системный или защищённый пакет недоступен для этого действия.'}
        switch($request.Action) {
            'remove' {Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop}
            'reset' {Reset-AppxPackage -Package $package.PackageFullName -ErrorAction Stop}
            'register' {
                if([string]::IsNullOrWhiteSpace($package.InstallLocation)){throw 'Папка пакета недоступна.'}
                $manifest=Join-Path $package.InstallLocation 'AppxManifest.xml'
                if(-not(Test-Path -LiteralPath $manifest -PathType Leaf)){throw 'Установленный манифест не найден.'}
                Add-AppxPackage -Path $manifest -Register -DisableDevelopmentMode -ErrorAction Stop
            }
        }
        $deadline=[DateTime]::UtcNow.AddSeconds(10)
        do {
            $after=@(Get-AppxPackage -PackageTypeFilter Main | Where-Object {$_.PackageFamilyName -ceq $request.FamilyName})
            $confirmed=if($request.Action -eq 'remove'){$after.Count -eq 0}else{$after.Count -gt 0}
            if($confirmed){break}
            Start-Sleep -Milliseconds 500
        } while([DateTime]::UtcNow -lt $deadline)
        if($request.Action -eq 'remove' -and $after.Count){throw 'Windows завершила команду, но пакет по-прежнему зарегистрирован. Обновите список.'}
        if($request.Action -ne 'remove' -and $after.Count -eq 0){throw 'После операции пакет не найден. Проверьте список приложений.'}
        Write-Output 'Команда Windows выполнена. Проверьте работу приложения.'
    }
    exit 0
} catch {[Console]::Error.WriteLine($_.Exception.Message);exit 4}
