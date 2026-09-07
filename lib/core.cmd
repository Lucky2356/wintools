@echo off
rem ============================================================================
rem  core.cmd - logging, guardrails, journal, revert engine.
rem  Called as:  call core.cmd :label [args]
rem  NOTE: no SETLOCAL here on purpose - callers rely on the variables we set.
rem ============================================================================
if "%~1"=="" exit /b 1
set "_ENTRY=%~1"
shift
goto %_ENTRY%

rem ================================================================ :init ====
:init
for %%D in ("%STATEDIR%" "%BACKUPROOT%" "%LOGDIR%") do if not exist "%%~D" md "%%~D" >nul 2>&1
set "QUIET=0"
set "RUNID="
for /f "usebackq delims=" %%T in (`powershell -NoProfile -Command "'{0}_{1}' -f (Get-Date).ToString('yyyyMMdd_HHmmss_fff'), ([guid]::NewGuid().ToString('N').Substring(0,8))" 2^>nul`) do set "RUNID=%%T"
if not defined RUNID (
    set "RUNID=%DATE%_%TIME%"
    set "RUNID=!RUNID:/=-!"
    set "RUNID=!RUNID::=-!"
    set "RUNID=!RUNID:.=-!"
    set "RUNID=!RUNID: =0!"
)
set "LOGDATE="
for /f "usebackq delims=" %%T in (`powershell -NoProfile -Command "(Get-Date).ToString('yyyy-MM-dd')" 2^>nul`) do set "LOGDATE=%%T"
if not defined LOGDATE set "LOGDATE=%DATE%"
set "BACKUPDIR=%BACKUPROOT%\%RUNID%"
if defined OPT_LOGOVERRIDE (set "LOGFILE=%OPT_LOGOVERRIDE%") else (set "LOGFILE=%LOGDIR%\wintweaks_%RUNID%.log")
set "LOCK_HELD=0"
%PSH% -Action AcquireLock -Root "%APPROOT%" -Token "%RUNID%" >nul 2>&1
if errorlevel 1 (
    echo   [ERROR] Another wintweaks process is active, or state\run.lock is stale.
    echo           If no process is running, remove state\run.lock and retry.
    exit /b 4
)
set "LOCK_HELD=1"
%PSH% -Action ValidateSelection -Root "%APPROOT%" -Name "%CMDNAME%"
if errorlevel 1 exit /b 1
%PSH% -Action ValidateData -Root "%APPROOT%"
if errorlevel 1 (
    echo   [ERROR] Data validation failed. No system changes were made.
    call :shutdown
    exit /b 1
)
set "JOURNAL_FAILED=0"
if not exist "%JOURNAL%" type nul > "%JOURNAL%"
%PSH% -Action ValidateJournal -Full "%JOURNAL%"
if errorlevel 1 exit /b 4
call :log INFO "=== wintweaks run %RUNID% cmd=%CMDNAME% profile=%OPT_PROFILE% dry=%OPT_DRY% risky=%OPT_RISKY% strict=%OPT_STRICT% ==="
call :log INFO "backups=%BACKUPDIR%"
exit /b 0

rem ============================================================ :shutdown ====
:shutdown
if "%LOCK_HELD%"=="1" %PSH% -Action ReleaseLock -Root "%APPROOT%" -Token "%RUNID%" >nul 2>&1
set "LOCK_HELD=0"
exit /b 0

rem ================================================================= :log ====
:log
set "_LVL=%~1"
set "_MSG=%~2"
if "%QUIET%"=="1" if /i not "%_LVL%"=="ERROR" if /i not "%_LVL%"=="FATAL" exit /b 0
echo   [%_LVL%] %_MSG%
if defined LOGFILE >>"%LOGFILE%" echo %LOGDATE% %TIME% [%_LVL%] %_MSG%
exit /b 0

rem =========================================================== :preflight ====
:preflight
fltmc >nul 2>&1
if errorlevel 1 (
    call :log ERROR "Administrator rights are required. Re-run this script from an elevated prompt."
    exit /b 2
)
set "OS_BUILD=" & set "OS_DISPLAY=" & set "OS_EDITION=" & set "OS_TYPE=" & set "OS_UBR="
for /f "usebackq tokens=3" %%V in (`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v CurrentBuild 2^>nul ^| findstr /i /c:"REG_"`) do set "OS_BUILD=%%V"
for /f "usebackq tokens=3" %%V in (`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v DisplayVersion 2^>nul ^| findstr /i /c:"REG_"`) do set "OS_DISPLAY=%%V"
for /f "usebackq tokens=3" %%V in (`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v EditionID 2^>nul ^| findstr /i /c:"REG_"`) do set "OS_EDITION=%%V"
for /f "usebackq tokens=3" %%V in (`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v InstallationType 2^>nul ^| findstr /i /c:"REG_"`) do set "OS_TYPE=%%V"
for /f "usebackq tokens=3" %%V in (`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v UBR 2^>nul ^| findstr /i /c:"REG_"`) do set "OS_UBR=%%V"
if not defined OS_BUILD (
    call :log ERROR "Cannot determine the Windows build - refusing to continue."
    exit /b 3
)
set /a BUILDNUM=%OS_BUILD% 2>nul
if %BUILDNUM% LSS 17763 (
    call :log ERROR "Unsupported build %OS_BUILD%. Windows 10 1809 (17763) or newer is required."
    exit /b 3
)
if /i not "%OS_TYPE%"=="Client" (
    call :log ERROR "Unsupported installation type %OS_TYPE%. This tool targets client Windows only."
    exit /b 3
)
if %BUILDNUM% GEQ 22000 (set "OS_FAMILY=win11") else (set "OS_FAMILY=win10")
call :log INFO "OS: %OS_EDITION% build=%OS_BUILD% ubr=%OS_UBR% display=%OS_DISPLAY% family=%OS_FAMILY%"
set "ENV_PARTOFDOMAIN=0" & set "ENV_ISLAPTOP=0" & set "ENV_SYSTEMDISKSSD=0" & set "ENV_XBOXCONTROLLER=0"
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action GetEnvironment 2^>nul`) do set "ENV_%%A=%%B"
call :log INFO "Environment: domain=%ENV_PARTOFDOMAIN% laptop=%ENV_ISLAPTOP% systemdisk_ssd=%ENV_SYSTEMDISKSSD% xbox=%ENV_XBOXCONTROLLER%"
if "%ENV_PARTOFDOMAIN%"=="1" (
    call :log WARN "Domain-joined machine: Group Policy may re-apply or overwrite policy tweaks after a revert."
    call :confirm "Continue on a domain-joined machine"
    if errorlevel 1 exit /b 5
)
set "CUR_SID="
for /f "usebackq tokens=2" %%S in (`whoami /user /nh 2^>nul`) do set "CUR_SID=%%S"
if defined OPT_USERSID (set "HKCU_ROOT=HKU\%OPT_USERSID%") else (set "HKCU_ROOT=HKCU")
call :log INFO "HKCU tweaks target: %HKCU_ROOT% (running as %USERDOMAIN%\%USERNAME% SID %CUR_SID%)"
if /i not "%HKCU_ROOT%"=="HKCU" (
    reg query "%HKCU_ROOT%" >nul 2>&1
    if errorlevel 1 (
        call :log ERROR "User hive %HKCU_ROOT% is not loaded - that user must be signed in."
        exit /b 3
    )
)
exit /b 0

rem ============================================================= :confirm ====
:confirm
if "%OPT_YES%"=="1" exit /b 0
if "%OPT_DRY%"=="1" exit /b 0
echo(
echo   %~1
set "ANS="
set /p "ANS=  Type YES to continue: "
if /i "%ANS%"=="YES" exit /b 0
call :log WARN "Aborted by user."
exit /b 5

rem ======================================================== :restorepoint ====
:restorepoint
if "%OPT_DRY%"=="1" (
    call :log INFO "DRY: restore point step skipped"
    exit /b 0
)
if /i "%OPT_RP%"=="no" (
    call :log INFO "System restore point: skipped by /no-restore-point. Per-item backups are still made in %BACKUPDIR%"
    exit /b 0
)
if /i "%OPT_RP%"=="ask" (
    if "%OPT_YES%"=="1" (
        call :log INFO "System restore point: not requested (/yes without /restore-point)"
        exit /b 0
    )
    echo(
    echo   A system restore point takes 10-60 seconds. It is optional:
    echo   every change is backed up individually and revert works without it.
    set "_RPANS="
    set /p "_RPANS=  Create a restore point first? [Y/N]: "
    if defined _RPANS for /f "delims=" %%X in ("!_RPANS!") do set "_RPANS=%%X"
    if /i not "!_RPANS!"=="Y" (
        call :log INFO "System restore point: declined by user"
        exit /b 0
    )
)
call :log INFO "Creating a system restore point..."
%PSH% -Action RestorePoint -Description "wintweaks %RUNID%" >nul 2>&1
if errorlevel 1 (
    call :log WARN "Restore point NOT created. System Protection is probably off, or Windows already made one in the last 24h."
) else (
    call :log INFO "Restore point created."
)
exit /b 0

rem ========================================================== :should_run ====
rem  Reads T_* set by the caller. exit 0 = run, 1 = skip, 6 = protected object.
:should_run
set "_EXPLICIT=0"
if defined OPT_IDS (
    set "_M=!OPT_IDS: %T_ID% =!"
    if "!_M!"=="!OPT_IDS!" exit /b 1
    set "_EXPLICIT=1"
)
call :profile_rank "%OPT_PROFILE%" WANT
call :profile_rank "%T_PROFILE%" HAS
if not defined HAS (
    call :log WARN "SKIPPED-BADPROFILE %T_ID% (unknown profile %T_PROFILE%)"
    exit /b 1
)
if "!_EXPLICIT!"=="0" if %HAS% GTR %WANT% exit /b 1
if /i not "%T_OS%"=="any" if /i not "%T_OS%"=="%OS_FAMILY%" (
    call :log INFO "SKIPPED-OS %T_ID% (needs %T_OS%, this is %OS_FAMILY%)"
    exit /b 1
)
if /i "%T_RISK%"=="high" if not "%OPT_RISKY%"=="1" (
    call :log WARN "SKIPPED-RISKY %T_ID% (pass /include-risky to enable)"
    exit /b 1
)
if /i "%T_ID%"=="PERF-SYSMAIN" if not "%ENV_SYSTEMDISKSSD%"=="1" (
    call :log WARN "SKIPPED-GUARD PERF-SYSMAIN (system disk is not an SSD, SysMain stays enabled)"
    exit /b 1
)
if /i "%T_ID%"=="SVC-XBOX-GIP" if "%ENV_XBOXCONTROLLER%"=="1" (
    call :log WARN "SKIPPED-GUARD SVC-XBOX-GIP (an Xbox controller is present)"
    exit /b 1
)
call :check_protected "%T_TYPE%" "%T_TARGET%"
if errorlevel 1 exit /b 6
exit /b 0

:profile_rank
set "%~2="
if /i "%~1"=="core"     set "%~2=1"
if /i "%~1"=="balanced" set "%~2=2"
if /i "%~1"=="extended" set "%~2=3"
if /i "%~1"=="manual"   set "%~2=9"
exit /b 0

rem ===================================================== :check_protected ====
:check_protected
if not exist "%PROTECTED%" exit /b 0
if /i "%~1"=="SVC" (
    findstr /i /x /c:"SVC:%~2" "%PROTECTED%" >nul
    if not errorlevel 1 (
        call :log FATAL "%T_ID% targets protected service %~2 - refusing to continue."
        exit /b 1
    )
    exit /b 0
)
if /i "%~1"=="APPX" (
    findstr /i /x /c:"APPX:%~2" "%PROTECTED%" >nul
    if not errorlevel 1 (
        call :log FATAL "%T_ID% targets protected package %~2 - refusing to continue."
        exit /b 1
    )
    exit /b 0
)
if /i "%~1"=="REG" (
    for /f "usebackq eol=# tokens=1,* delims=:" %%P in ("%PROTECTED%") do (
        if /i "%%P"=="KEY" (
            echo %~2| findstr /i /b /c:"%%Q" >nul
            if not errorlevel 1 (
                call :log FATAL "%T_ID% targets protected registry key %%Q - refusing to continue."
                exit /b 1
            )
        )
    )
)
exit /b 0

rem ========================================================== :plan_apply ====
:plan_apply
echo(
echo   Planned changes (profile=%OPT_PROFILE% risky=%OPT_RISKY%):
set "QUIET=1"
set /a _PLANNED=0
for /f "usebackq eol=# tokens=1-9 delims=|" %%A in ("%TWEAKDEF%") do (
    set "T_ID=%%A" & set "T_PROFILE=%%B" & set "T_RISK=%%C" & set "T_OS=%%D"
    set "T_TYPE=%%E" & set "T_TARGET=%%F" & set "T_NAME=%%G" & set "T_VTYPE=%%H" & set "T_VALUE=%%I"
    call :should_run
    if not errorlevel 1 (
        set /a _PLANNED+=1
        echo     %%E   %%A
    )
)
set "QUIET=0"
echo(
echo   %_PLANNED% tweak(s) selected. Backups go to %BACKUPDIR%
if %_PLANNED% EQU 0 (
    call :log WARN "No applicable tweaks selected. Check IDs, profile, OS and risk flags."
    exit /b 1
)
exit /b 0

rem ========================================================= :journal_add ====
rem  args: ID TYPE TARGET NAME PREVSTATE PREVTYPE PREVDATA KEYCREATED
:journal_add
if "%OPT_DRY%"=="1" exit /b 0
if "%JOURNAL_FAILED%"=="1" exit /b 4
call :ensure_backupdir
if errorlevel 1 exit /b 4
>>"%JOURNAL%" echo %RUNID%^|%~1^|%~2^|%~3^|%~4^|%~5^|%~6^|%~7^|%~8^|PENDING^|%LOGDATE% %TIME%|| goto journal_add_failed
exit /b 0
:journal_add_failed
set "JOURNAL_FAILED=1"
call :log ERROR "Cannot append journal entry; change aborted."
exit /b 4

rem =========================================================== :want_step ====
rem  exit 0 = run this step. With /id: only the named steps run.
:want_step
if not defined OPT_IDS exit /b 0
set "_M=!OPT_IDS: %~1 =!"
if "!_M!"=="!OPT_IDS!" exit /b 1
exit /b 0

rem ==================================================== :ensure_backupdir ====
:ensure_backupdir
if not exist "%BACKUPDIR%" md "%BACKUPDIR%" >nul 2>&1
if not exist "%BACKUPDIR%\" (
    set "JOURNAL_FAILED=1"
    call :log ERROR "Cannot create backup directory."
    exit /b 4
)
exit /b 0

rem ======================================================== :journal_mark ====
:journal_mark
call :journal_setresult "%RUNID%" "%~1" "%~2"
exit /b %ERRORLEVEL%

rem =================================================== :journal_setresult ====
:journal_setresult
if "%OPT_DRY%"=="1" exit /b 0
%PSH% -Action JournalSetResult -Full "%JOURNAL%" -Token "%~1" -Name "%~2" -Description "%~3"
if errorlevel 1 (
    set "JOURNAL_FAILED=1"
    call :log ERROR "Journal update failed; original journal retained."
    exit /b 4
)
exit /b 0

rem ====================================================== :revert_journal ====
:revert_journal
%PSH% -Action ValidateRevertOrder -Full "%JOURNAL%"
if errorlevel 1 exit /b 4
set /a _N=0
for /f "usebackq delims=" %%L in ("%JOURNAL%") do (
    set /a _N+=1
    set "_J!_N!=%%L"
)
if %_N%==0 (
    call :log INFO "Journal is empty - nothing to revert."
    exit /b 0
)
set /a _DONE=0, _SKIP=0
for /l %%i in (%_N%,-1,1) do (
    for /f "tokens=1-11 delims=|" %%a in ("!_J%%i!") do (
        set "R_RUN=%%a"    & set "R_ID=%%b"    & set "R_TYPE=%%c"
        set "R_TARGET=%%d" & set "R_NAME=%%e"  & set "R_PSTATE=%%f"
        set "R_PTYPE=%%g"  & set "R_PDATA=%%h" & set "R_KEYNEW=%%i"
        set "R_RESULT=%%j"
        set "_DOIT=1"
        if /i "!R_RESULT!"=="REVERTED" set "_DOIT=0"
        if /i "!R_RESULT!"=="MANUAL" set "_DOIT=0"
        if defined OPT_RUN if /i not "!R_RUN!"=="%OPT_RUN%" set "_DOIT=0"
        if defined OPT_IDS (
            echo %OPT_IDS% | findstr /i /c:" !R_ID! " >nul
            if errorlevel 1 set "_DOIT=0"
        )
        if "!_DOIT!"=="1" (
            set /a _DONE+=1
            if /i "!R_TYPE!"=="REG"  call "%LIBDIR%\reg.cmd"       :revert_one
            if /i "!R_TYPE!"=="SVC"  call "%LIBDIR%\svc.cmd"       :revert_one
            if /i "!R_TYPE!"=="TASK" call "%LIBDIR%\task.cmd"      :revert_one
            if /i "!R_TYPE!"=="PWR"  call "%LIBDIR%\syschange.cmd" :revert_one
            if /i "!R_TYPE!"=="NET"  call "%LIBDIR%\syschange.cmd" :revert_one
            if /i "!R_TYPE!"=="FS"   call "%LIBDIR%\syschange.cmd" :revert_one
            if /i "!R_TYPE!"=="APPX" call "%LIBDIR%\appx.cmd"      :revert_one
            if /i "!R_TYPE!"=="APPXDEEP" call "%LIBDIR%\appx.cmd"  :revert_one
            if /i "!R_TYPE!"=="EDGE" call "%LIBDIR%\appx.cmd"      :revert_one
            rem Do not restore older snapshots after a newer restore failed.
            if errorlevel 1 exit /b 4
            if "!JOURNAL_FAILED!"=="1" exit /b 4
        ) else (
            set /a _SKIP+=1
        )
    )
)
call :log INFO "Revert finished: %_DONE% processed, %_SKIP% skipped."
if "%OPT_DRY%"=="0" if %_DONE% GTR 0 call :log INFO "UI and service changes fully take effect after sign-out or reboot."
exit /b 0

rem ============================================================== :status ====
:status
echo(
echo   Root:     %APPROOT%
echo   Journal:  %JOURNAL%
echo   Backups:  %BACKUPROOT%
echo   Logs:     %LOGDIR%
echo(
if not exist "%JOURNAL%" (
    echo   No journal yet - nothing has been applied.
    exit /b 0
)
set /a _A=0, _R=0, _P=0, _F=0, _M=0
for /f "usebackq tokens=1,2,3,10 delims=|" %%a in ("%JOURNAL%") do (
    if /i "%%d"=="OK"       (set /a _A+=1 & echo     [APPLIED ] %%c %%b   run %%a)
    if /i "%%d"=="REVERTED" (set /a _R+=1 & echo     [REVERTED] %%c %%b   run %%a)
    if /i "%%d"=="PENDING"  (set /a _P+=1 & echo     [PENDING!] %%c %%b   run %%a - interrupted, run revert)
    if /i "%%d"=="FAILED"   (set /a _F+=1 & echo     [FAILED  ] %%c %%b   run %%a)
    if /i "%%d"=="MANUAL"   (set /a _M+=1 & echo     [MANUAL  ] %%c %%b   run %%a - reinstall by hand, see the log)
)
echo(
echo   applied=%_A%  reverted=%_R%  pending=%_P%  failed=%_F%  manual=%_M%
echo(
exit /b 0

rem ====================================================== :verify_journal ====
:verify_journal
%PSH% -Action VerifyJournal -Root "%APPROOT%" -Full "%JOURNAL%"
exit /b %ERRORLEVEL%

rem =========================================================== :parentkey ====
:parentkey
set "_S=%~1"
set "_ACC="
set "_PAR="
:pk_scan
if "!_S!"=="" goto pk_done
set "_C=!_S:~0,1!"
set "_S=!_S:~1!"
if "!_C!"=="\" set "_PAR=!_ACC!"
set "_ACC=!_ACC!!_C!"
goto pk_scan
:pk_done
set "%~2=!_PAR!"
exit /b 0

rem ===================================================== :topmost_missing ====
rem  RC_DELKEY = highest ancestor of <key> that does not exist yet
:topmost_missing
set "RC_DELKEY=%~1"
:tm_loop
call :parentkey "!RC_DELKEY!" _PP
if not defined _PP goto tm_done
if "!_PP:\=!"=="!_PP!" goto tm_done
reg query "!_PP!" >nul 2>&1
if not errorlevel 1 goto tm_done
set "RC_DELKEY=!_PP!"
goto tm_loop
:tm_done
exit /b 0
