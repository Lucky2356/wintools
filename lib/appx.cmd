@echo off
rem ============================================================================
rem  appx.cmd - built-in app removal with a real way back.
rem
rem  APPX: we remove the PER-USER REGISTRATION only (Remove-AppxPackage without
rem  -AllUsers). The provisioned payload stays under Program Files\WindowsApps,
rem  so revert re-registers it with Add-AppxPackage -Register - no download, no
rem  Store, no internet. That is what makes this reversible.
rem
rem  EDGE: uninstalling Edge runs Microsoft's own setup.exe --uninstall. There
rem  is NO local backup that can bring it back, so this one is marked
rem  IRREVERSIBLE and revert only prints the reinstall instructions.
rem ============================================================================
if "%~1"=="" exit /b 1
set "_ENTRY=%~1"
shift
goto %_ENTRY%

rem =========================================================== :apply_one ====
:apply_one
if /i "%T_TYPE%"=="EDGE" goto edge_apply

call "%LIBDIR%\core.cmd" :check_protected APPX "%T_TARGET%"
if errorlevel 1 exit /b 6

set "AX_EXISTS=0" & set "AX_FULLNAME=" & set "AX_FAMILYNAME=" & set "AX_INSTALLLOC=" & set "AX_NONREMOVABLE=0"
for /f "usebackq tokens=1,* delims==" %%A in (`%PSH% -Action GetAppx -Name "%T_TARGET%" 2^>nul`) do set "AX_%%A=%%B"
if not "%AX_EXISTS%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED-ABSENT %T_ID% (package %T_TARGET% is not installed for this user)"
    exit /b 0
)
if "%AX_NONREMOVABLE%"=="1" (
    call "%LIBDIR%\core.cmd" :log WARN "SKIPPED-GUARD %T_ID% (%T_TARGET% is marked non-removable by Windows)"
    exit /b 0
)
if not exist "%AX_INSTALLLOC%\AppxManifest.xml" (
    call "%LIBDIR%\core.cmd" :log WARN "SKIPPED-GUARD %T_ID% (no manifest at %AX_INSTALLLOC% - could not guarantee a restore)"
    exit /b 0
)
if "%OPT_DEEP%"=="1" goto appx_deep

rem ---------------- normal removal: unregister for this user only ------------
call "%LIBDIR%\core.cmd" :journal_add "%T_ID%" APPX "%T_TARGET%" "%AX_FAMILYNAME%" "PRESENT" "%AX_FULLNAME%" "%AX_INSTALLLOC%" "0"
if errorlevel 1 exit /b 4
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY %T_ID%: would unregister %AX_FULLNAME% for the current user, payload kept at %AX_INSTALLLOC%"
    exit /b 0
)
%PSH% -Action RemoveAppx -Full "%AX_FULLNAME%" >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "FAILED %T_ID%: Remove-AppxPackage %AX_FULLNAME%"
    call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" FAILED
    exit /b 1
)
call "%LIBDIR%\core.cmd" :log INFO "OK %T_ID%: %T_TARGET% removed for this user, payload kept for restore"
call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" OK
exit /b 0

rem ---------------- deep removal: registration + data + traces ---------------
rem  Everything is copied out BEFORE anything is deleted. The provisioned entry
rem  is the one part that cannot be recreated from a local backup - that limit
rem  is stated in the log and in the menu card.
:appx_deep
set "_BK=%BACKUPDIR%\appx\%T_TARGET%"
set "_LD=%LOCALAPPDATA%\Packages\%AX_FAMILYNAME%"
set "_RK=HKCU\Software\Classes\ActivatableClasses\Package\%AX_FULLNAME%"
set "PV_EXISTS=0" & set "PV_PACKAGENAME="
for /f "usebackq tokens=1,* delims==" %%A in (`%PSH% -Action GetProvisioned -Name "%T_TARGET%" 2^>nul`) do set "PV_%%A=%%B"

if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY %T_ID% [DEEP]: back up payload, %LOCALAPPDATA%\Packages\%AX_FAMILYNAME% and the ActivatableClasses key, then remove all three"
    if "%PV_EXISTS%"=="1" call "%LIBDIR%\core.cmd" :log INFO "DRY %T_ID% [DEEP]: would also drop provisioned entry %PV_PACKAGENAME% - that part CANNOT be restored from a local backup"
    exit /b 0
)

call "%LIBDIR%\core.cmd" :ensure_backupdir
if errorlevel 1 exit /b 4
if not exist "%_BK%" md "%_BK%" >nul 2>&1
> "%_BK%\meta.txt" echo ORIGLOC=%AX_INSTALLLOC%|| goto deep_backup_failed
>>"%_BK%\meta.txt" echo FULLNAME=%AX_FULLNAME%|| goto deep_backup_failed
>>"%_BK%\meta.txt" echo FAMILY=%AX_FAMILYNAME%|| goto deep_backup_failed
>>"%_BK%\meta.txt" echo PROVISIONED=%PV_PACKAGENAME%|| goto deep_backup_failed

call "%LIBDIR%\core.cmd" :log INFO "%T_ID% [DEEP]: backing up payload to %_BK%\payload ..."
robocopy "%AX_INSTALLLOC%" "%_BK%\payload" /E /B /R:0 /W:0 /NFL /NDL /NJH /NJS /NP >nul 2>&1
if errorlevel 8 (
    call "%LIBDIR%\core.cmd" :log ERROR "ABORTED %T_ID% [DEEP]: could not copy the payload - refusing to delete what we cannot restore"
    exit /b 1
)
set "_HADLD=0"
if exist "%_LD%" (
    set "_HADLD=1"
    robocopy "%_LD%" "%_BK%\localdata" /E /R:0 /W:0 /NFL /NDL /NJH /NJS /NP >nul 2>&1
    if errorlevel 8 (
        call "%LIBDIR%\core.cmd" :log ERROR "ABORTED %T_ID% [DEEP]: could not copy %_LD%"
        exit /b 1
    )
)
reg query "%_RK%" >nul 2>&1
set "_HADRK=0"
if not errorlevel 1 (
    reg export "%_RK%" "%_BK%\activatable.reg" /y >nul 2>&1
    if errorlevel 1 goto deep_backup_failed
    set "_HADRK=1"
) else (
    %PSH% -Action CheckRegistryAbsent -Full "%_RK%"
    if errorlevel 1 goto deep_backup_failed
)
>>"%_BK%\meta.txt" echo LOCALDATA=%_HADLD%|| goto deep_backup_failed
>>"%_BK%\meta.txt" echo ACTIVATABLE=%_HADRK%|| goto deep_backup_failed

call "%LIBDIR%\core.cmd" :journal_add "%T_ID%" APPXDEEP "%T_TARGET%" "%AX_FAMILYNAME%" "PRESENT" "%AX_FULLNAME%" "%_BK%" "%PV_EXISTS%"
if errorlevel 1 exit /b 4

%PSH% -Action RemoveAppx -Full "%AX_FULLNAME%" >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "FAILED %T_ID% [DEEP]: Remove-AppxPackage %AX_FULLNAME%"
    call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" FAILED
    exit /b 1
)
set "_DEEPFAIL=0"
if "%PV_EXISTS%"=="1" (
    %PSH% -Action RemoveProvisioned -Full "%PV_PACKAGENAME%" >nul 2>&1
    if errorlevel 1 (
        set "_DEEPFAIL=1"
        call "%LIBDIR%\core.cmd" :log WARN "%T_ID% [DEEP]: provisioned entry %PV_PACKAGENAME% was not removed - the app may return for new user accounts"
    ) else (
        call "%LIBDIR%\core.cmd" :log INFO "%T_ID% [DEEP]: provisioned entry removed - the app will not be handed to new accounts"
    )
)
if exist "%_LD%" rd /s /q "%_LD%" >nul 2>&1
if exist "%_LD%" set "_DEEPFAIL=1"
reg query "%_RK%" >nul 2>&1
if not errorlevel 1 reg delete "%_RK%" /f >nul 2>&1
%PSH% -Action CheckRegistryAbsent -Full "%_RK%"
if errorlevel 1 set "_DEEPFAIL=1"
if "%_DEEPFAIL%"=="1" (
    call "%LIBDIR%\core.cmd" :log ERROR "FAILED %T_ID% [DEEP]: removal was incomplete; backup retained for revert."
    call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" FAILED
    exit /b 1
)
call "%LIBDIR%\core.cmd" :log INFO "OK %T_ID% [DEEP]: package, user data and activation key removed. Full backup in %_BK%"
call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" OK
exit /b 0

:deep_backup_failed
call "%LIBDIR%\core.cmd" :log ERROR "ABORTED %T_ID% [DEEP]: backup or metadata write failed; nothing was removed."
exit /b 1

rem ------------------------------------------------------------ Edge apply ---
:edge_apply
set "ED_EXISTS=0" & set "ED_VERSION=" & set "ED_SETUP="
for /f "usebackq tokens=1,* delims==" %%A in (`%PSH% -Action GetEdge 2^>nul`) do set "ED_%%A=%%B"
if not "%ED_EXISTS%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED-ABSENT %T_ID% (no Edge installer found - Edge is probably already gone)"
    exit /b 0
)
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY %T_ID%: would run Edge %ED_VERSION% setup.exe --uninstall --system-level --force-uninstall  [IRREVERSIBLE]"
    exit /b 0
)
call "%LIBDIR%\core.cmd" :log WARN "%T_ID% is IRREVERSIBLE: there is no local backup of Edge. Reinstall means downloading it from Microsoft."
call "%LIBDIR%\core.cmd" :confirm "Uninstall Microsoft Edge %ED_VERSION% - this cannot be rolled back by this tool"
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED %T_ID% (declined)"
    exit /b 0
)
call "%LIBDIR%\core.cmd" :journal_add "%T_ID%" EDGE "MicrosoftEdge" "-" "PRESENT" "%ED_VERSION%" "%ED_SETUP%" "0"
if errorlevel 1 exit /b 4
"%ED_SETUP%" --uninstall --system-level --verbose-logging --force-uninstall
set "_RC=%ERRORLEVEL%"
if not "%_RC%"=="0" (
    call "%LIBDIR%\core.cmd" :log ERROR "FAILED %T_ID%: Edge uninstaller returned %_RC%. Microsoft blocks removal outside the EEA - Edge is still installed."
    call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" FAILED
    exit /b 1
)
call "%LIBDIR%\core.cmd" :log INFO "OK %T_ID%: Edge %ED_VERSION% uninstalled"
call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" OK
exit /b 0

rem ========================================================== :revert_one ====
:revert_one
if /i "%R_TYPE%"=="EDGE" goto edge_revert
if /i "%R_TYPE%"=="APPXDEEP" goto deep_revert
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY revert %R_ID%: re-register %R_NAME% from %R_PDATA%"
    exit /b 0
)
%PSH% -Action RestoreAppx -Full "%R_PDATA%" -Name "%R_NAME%" >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "REVERT FAILED %R_ID%: could not re-register %R_NAME%. Payload expected at %R_PDATA%. Reinstall from the Store if it is gone."
    exit /b 1
)
call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: %R_TARGET% registered again for this user"
call "%LIBDIR%\core.cmd" :journal_setresult "%R_RUN%" "%R_ID%" REVERTED
exit /b 0

rem ---------------- deep revert: data, registry, then registration -----------
:deep_revert
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY revert %R_ID% [DEEP]: restore user data and activation key from %R_PDATA%, then re-register %R_NAME%"
    exit /b 0
)
if not exist "%R_PDATA%" (
    call "%LIBDIR%\core.cmd" :log ERROR "REVERT FAILED %R_ID%: backup folder is gone: %R_PDATA%"
    exit /b 1
)
if not exist "%R_PDATA%\meta.txt" (
    call "%LIBDIR%\core.cmd" :log ERROR "REVERT FAILED %R_ID%: backup metadata missing."
    exit /b 1
)
set "_PARTIAL=0"
set "_HADLD=0" & set "_HADRK=0"
set "_ORIG="
for /f "usebackq tokens=1,* delims==" %%A in ("%R_PDATA%\meta.txt") do (
    if /i "%%A"=="ORIGLOC" set "_ORIG=%%B"
    if /i "%%A"=="LOCALDATA" set "_HADLD=%%B"
    if /i "%%A"=="ACTIVATABLE" set "_HADRK=%%B"
)
if not defined _ORIG set "_PARTIAL=1"
if "%_HADLD%"=="1" if not exist "%R_PDATA%\localdata" set "_PARTIAL=1"
if "%_HADRK%"=="1" if not exist "%R_PDATA%\activatable.reg" set "_PARTIAL=1"
if exist "%R_PDATA%\localdata" (
    robocopy "%R_PDATA%\localdata" "%LOCALAPPDATA%\Packages\%R_NAME%" /E /R:0 /W:0 /NFL /NDL /NJH /NJS /NP >nul 2>&1
    if errorlevel 8 set "_PARTIAL=1"
)
if exist "%R_PDATA%\activatable.reg" (
    reg import "%R_PDATA%\activatable.reg" >nul 2>&1
    if errorlevel 1 set "_PARTIAL=1"
)

rem  Try the original location first, then the family name, then our own copy.
set "_OK=0"
if defined _ORIG (
    %PSH% -Action RestoreAppx -Full "!_ORIG!" -Name "%R_NAME%" >nul 2>&1
    if not errorlevel 1 set "_OK=1"
)
if "!_OK!"=="0" (
    %PSH% -Action RestoreAppx -Full "%R_PDATA%\payload" -Name "%R_NAME%" >nul 2>&1
    if not errorlevel 1 set "_OK=1"
)
if "!_OK!"=="0" (
    call "%LIBDIR%\core.cmd" :log ERROR "REVERT FAILED %R_ID% [DEEP]: Windows refused to register the package again."
    call "%LIBDIR%\core.cmd" :log ERROR "  Restore is incomplete. Reinstall the app from the Store; backups are in %R_PDATA%"
    exit /b 1
)
if "%_PARTIAL%"=="1" (
    call "%LIBDIR%\core.cmd" :log ERROR "REVERT FAILED %R_ID% [DEEP]: package registered, but data or registry restore was incomplete; retry with the backup intact."
    exit /b 1
)
call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID% [DEEP]: %R_TARGET% is back, user data and activation key restored"
if "%R_KEYNEW%"=="1" call "%LIBDIR%\core.cmd" :log WARN "REVERT %R_ID%: the provisioned entry was NOT restored - brand new user accounts will not get this app"
call "%LIBDIR%\core.cmd" :journal_setresult "%R_RUN%" "%R_ID%" REVERTED
exit /b 0

:edge_revert
call "%LIBDIR%\core.cmd" :log WARN "REVERT %R_ID%: Edge %R_PTYPE% cannot be restored from a local backup."
call "%LIBDIR%\core.cmd" :log WARN "  Reinstall manually: winget install --id Microsoft.Edge -e   (or download it from microsoft.com/edge)"
call "%LIBDIR%\core.cmd" :journal_setresult "%R_RUN%" "%R_ID%" MANUAL
exit /b 0
