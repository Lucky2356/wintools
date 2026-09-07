@echo off
rem ============================================================================
rem  svc.cmd - service start-type backup / apply / restore.
rem  State is read with CIM (helpers.ps1) because sc.exe output is localized.
rem  Journal mapping: PREV_TYPE = StartMode, PREV_DATA = State, field9 = Delayed
rem ============================================================================
if "%~1"=="" exit /b 1
set "_ENTRY=%~1"
shift
goto %_ENTRY%

rem =========================================================== :apply_one ====
:apply_one
call "%LIBDIR%\core.cmd" :check_protected SVC "%T_TARGET%"
if errorlevel 1 exit /b 6
set "SVC_EXISTS=0" & set "SVC_STARTMODE=" & set "SVC_STATE=" & set "SVC_DELAYED=0"
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action GetService -Name "%T_TARGET%" 2^>nul`) do set "SVC_%%A=%%B"
if not "%SVC_EXISTS%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED-ABSENT %T_ID% (service %T_TARGET% is not installed)"
    exit /b 0
)
call :sc_word "%T_VALUE%" _WANTMODE
if /i "%SVC_STARTMODE%"=="!_WANTMODE!" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED-SAME %T_ID% (%T_TARGET% start type is already %SVC_STARTMODE%)"
    exit /b 0
)
set "_HAVE=0"
for /f "usebackq tokens=2,10 delims=|" %%a in ("%JOURNAL%") do (
    if /i "%%a"=="%T_ID%" if /i "%%b"=="OK" set "_HAVE=1"
)
if "!_HAVE!"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "REAPPLY %T_ID% (original already captured in an earlier run)"
) else (
    if "%OPT_DRY%"=="0" call "%LIBDIR%\core.cmd" :ensure_backupdir
    if "%OPT_DRY%"=="0" reg export "HKLM\SYSTEM\CurrentControlSet\Services\%T_TARGET%" "%BACKUPDIR%\svc_%T_TARGET%.reg" /y >nul 2>&1
    call "%LIBDIR%\core.cmd" :journal_add "%T_ID%" SVC "%T_TARGET%" "-" "PRESENT" "%SVC_STARTMODE%" "%SVC_STATE%" "%SVC_DELAYED%"
    if errorlevel 1 exit /b 4
)
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY %T_ID%: sc config %T_TARGET% start= %T_VALUE%   [was %SVC_STARTMODE%/%SVC_STATE%]"
    exit /b 0
)
sc config "%T_TARGET%" start= %T_VALUE% >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "FAILED %T_ID%: sc config %T_TARGET% start= %T_VALUE%"
    call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" FAILED
    exit /b 1
)
if /i "%T_VALUE%"=="disabled" if /i "%SVC_STATE%"=="Running" (
    sc stop "%T_TARGET%" >nul 2>&1
    if errorlevel 1 call "%LIBDIR%\core.cmd" :log WARN "%T_ID%: %T_TARGET% could not be stopped now - it stays disabled and will not start after reboot"
)
call "%LIBDIR%\core.cmd" :log INFO "OK %T_ID%: service %T_TARGET% start= %T_VALUE%   [was %SVC_STARTMODE%/%SVC_STATE%]"
call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" OK
exit /b 0

rem ========================================================== :revert_one ====
:revert_one
set "_START=demand"
if /i "%R_PTYPE%"=="Auto"     set "_START=auto"
if /i "%R_PTYPE%"=="Manual"   set "_START=demand"
if /i "%R_PTYPE%"=="Disabled" set "_START=disabled"
if /i "%R_PTYPE%"=="Boot"     set "_START=boot"
if /i "%R_PTYPE%"=="System"   set "_START=system"
if /i "%R_PTYPE%"=="Auto" if "%R_KEYNEW%"=="1" set "_START=delayed-auto"
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY revert %R_ID%: sc config %R_TARGET% start= !_START!  (and start it if it was Running)"
    exit /b 0
)
sc config "%R_TARGET%" start= !_START! >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "REVERT FAILED %R_ID%: sc config %R_TARGET% start= !_START!"
    exit /b 1
)
if /i "%R_PDATA%"=="Running" (
    sc start "%R_TARGET%" >nul 2>&1
    if errorlevel 1 call "%LIBDIR%\core.cmd" :log WARN "REVERT %R_ID%: %R_TARGET% restored to !_START! but did not start now - it will start after reboot"
)
call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: service %R_TARGET% start= !_START! (was %R_PTYPE%/%R_PDATA% originally)"
call "%LIBDIR%\core.cmd" :journal_setresult "%R_RUN%" "%R_ID%" REVERTED
exit /b 0

rem  sc.exe keyword -> CIM StartMode word, so we can compare them
:sc_word
set "%~2=Manual"
if /i "%~1"=="auto"         set "%~2=Auto"
if /i "%~1"=="delayed-auto" set "%~2=Auto"
if /i "%~1"=="demand"       set "%~2=Manual"
if /i "%~1"=="disabled"     set "%~2=Disabled"
if /i "%~1"=="boot"         set "%~2=Boot"
if /i "%~1"=="system"       set "%~2=System"
exit /b 0
