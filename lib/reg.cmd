@echo off
rem ============================================================================
rem  reg.cmd - registry value backup / apply / restore.
rem  Previous state model: (STATE, TYPE, DATA) where STATE is ABSENT or PRESENT.
rem  Placeholders: @HKCU@ root, @DEFAULT@ value name, @EMPTY@ empty string data.
rem ============================================================================
if "%~1"=="" exit /b 1
set "_ENTRY=%~1"
shift
goto %_ENTRY%

rem ============================================================= :capture ====
rem  args: <key> <valuename>
rem  out : RC_KEYEXISTS RC_STATE RC_TYPE RC_DATA
:capture
set "RC_KEYEXISTS=0"
set "RC_STATE=ABSENT"
set "RC_TYPE=-"
set "RC_DATA=-"
reg query "%~1" >nul 2>&1
if not errorlevel 1 set "RC_KEYEXISTS=1"
if "%RC_KEYEXISTS%"=="0" exit /b 0
set "_QOPT=/v "%~2""
if /i "%~2"=="@DEFAULT@" set "_QOPT=/ve"
for /f "usebackq tokens=1,2,*" %%A in (`reg query "%~1" %_QOPT% 2^>nul`) do (
    set "_TT=%%~B"
    if /i "!_TT:~0,4!"=="REG_" (
        set "RC_STATE=PRESENT"
        set "RC_TYPE=%%~B"
        if "%%~C"=="" (set "RC_DATA=@EMPTY@") else (set "RC_DATA=%%~C")
    )
)
exit /b 0

rem =========================================================== :apply_one ====
rem  Reads T_ID T_TARGET T_NAME T_VTYPE T_VALUE from the caller.
:apply_one
set "KEY=%T_TARGET%"
set "KEY=!KEY:@HKCU@=%HKCU_ROOT%!"
call "%LIBDIR%\core.cmd" :check_protected REG "!KEY!"
if errorlevel 1 exit /b 6

call :capture "!KEY!" "%T_NAME%"

rem ---- idempotency: already at the wanted value? -----------------------------
set "_SAME=0"
if /i "%RC_STATE%"=="PRESENT" (
    if /i "%T_VTYPE%"=="REG_DWORD" (
        set "_C=#" & set "_W=##"
        set /a _C=%RC_DATA% >nul 2>&1
        set /a _W=%T_VALUE% >nul 2>&1
        if "!_C!"=="!_W!" set "_SAME=1"
    ) else (
        if /i "%RC_DATA%"=="%T_VALUE%" set "_SAME=1"
    )
)
if "!_SAME!"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED-SAME %T_ID% (!KEY!\%T_NAME% is already %T_VALUE%)"
    exit /b 0
)

rem ---- backup + journal, but never overwrite an original we already hold -----
set "_HAVE=0"
for /f "usebackq tokens=2,10 delims=|" %%a in ("%JOURNAL%") do (
    if /i "%%a"=="%T_ID%" if /i "%%b"=="OK" set "_HAVE=1"
)
if "!_HAVE!"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "REAPPLY %T_ID% (original already captured in an earlier run)"
) else (
    set "_DELKEY=0"
    if "%RC_KEYEXISTS%"=="0" (
        call "%LIBDIR%\core.cmd" :topmost_missing "!KEY!"
        set "_DELKEY=!RC_DELKEY!"
    ) else (
        call :safename "!KEY!" _SAFE
        if "%OPT_DRY%"=="0" call "%LIBDIR%\core.cmd" :ensure_backupdir
        if "%OPT_DRY%"=="0" reg export "!KEY!" "%BACKUPDIR%\!_SAFE!.reg" /y >nul 2>&1
    )
    call "%LIBDIR%\core.cmd" :journal_add "%T_ID%" REG "!KEY!" "%T_NAME%" "%RC_STATE%" "%RC_TYPE%" "%RC_DATA%" "!_DELKEY!"
    if errorlevel 1 exit /b 4
)

rem ---- mutate ---------------------------------------------------------------
set "_VOPT=/v "%T_NAME%""
if /i "%T_NAME%"=="@DEFAULT@" set "_VOPT=/ve"
set "_DATA=%T_VALUE%"
if /i "%_DATA%"=="@EMPTY@" set "_DATA="
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY %T_ID%: reg add !KEY! %_VOPT% /t %T_VTYPE% /d %T_VALUE%   [was %RC_STATE%:%RC_TYPE%:%RC_DATA%]"
    exit /b 0
)
reg add "!KEY!" %_VOPT% /t %T_VTYPE% /d "%_DATA%" /f >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "FAILED %T_ID%: cannot write !KEY!\%T_NAME%"
    call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" FAILED
    exit /b 1
)
call "%LIBDIR%\core.cmd" :log INFO "OK %T_ID%: !KEY!\%T_NAME% = %T_VALUE%   [was %RC_STATE%:%RC_DATA%]"
call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" OK
exit /b 0

rem ========================================================== :revert_one ====
rem  Reads R_* set by core.cmd :revert_journal
:revert_one
set "_VOPT=/v "%R_NAME%""
if /i "%R_NAME%"=="@DEFAULT@" set "_VOPT=/ve"
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY revert %R_ID%: %R_TARGET%\%R_NAME% -> %R_PSTATE%:%R_PTYPE%:%R_PDATA% delkey=%R_KEYNEW%"
    exit /b 0
)
if /i "%R_PSTATE%"=="ABSENT" (
    if not "%R_KEYNEW%"=="0" (
        reg query "%R_KEYNEW%" >nul 2>&1
        if not errorlevel 1 reg delete "%R_KEYNEW%" /f >nul 2>&1
        call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: removed key %R_KEYNEW% (it did not exist before)"
    ) else (
        reg delete "%R_TARGET%" %_VOPT% /f >nul 2>&1
        call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: removed value %R_TARGET%\%R_NAME%"
    )
) else (
    set "_PD=%R_PDATA%"
    if /i "!_PD!"=="@EMPTY@" set "_PD="
    reg add "%R_TARGET%" %_VOPT% /t %R_PTYPE% /d "!_PD!" /f >nul 2>&1
    if errorlevel 1 (
        call "%LIBDIR%\core.cmd" :log ERROR "REVERT FAILED %R_ID%: %R_TARGET%\%R_NAME%"
        exit /b 1
    )
    call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: %R_TARGET%\%R_NAME% = %R_PDATA% (%R_PTYPE%)"
)
call "%LIBDIR%\core.cmd" :journal_setresult "%R_RUN%" "%R_ID%" REVERTED
exit /b 0

rem ============================================================ :safename ====
:safename
set "_S=%~1"
set "_S=%_S:\=_%"
set "_S=%_S: =_%"
set "_S=%_S:{=%"
set "_S=%_S:}=%"
set "%~2=%_S%"
exit /b 0
