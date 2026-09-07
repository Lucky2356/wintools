@echo off
rem ============================================================================
rem  task.cmd - scheduled task enable/disable with exact restore.
rem  Only the enabled flag is touched; the task definition is never rewritten.
rem  Journal mapping: PREV_TYPE = task State (Ready/Disabled/Running)
rem ============================================================================
if "%~1"=="" exit /b 1
set "_ENTRY=%~1"
shift
goto %_ENTRY%

rem =========================================================== :apply_one ====
:apply_one
set "TSK_EXISTS=0" & set "TSK_STATE="
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action GetTask -Full "%T_TARGET%" 2^>nul`) do set "TSK_%%A=%%B"
if not "%TSK_EXISTS%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED-ABSENT %T_ID% (task not present on this build)"
    exit /b 0
)
if /i "%TSK_STATE%"=="Disabled" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED-SAME %T_ID% (task is already disabled)"
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
    if "%OPT_DRY%"=="0" schtasks /Query /TN "%T_TARGET%" /XML > "%BACKUPDIR%\task_%T_ID%.xml" 2>nul
    call "%LIBDIR%\core.cmd" :journal_add "%T_ID%" TASK "%T_TARGET%" "-" "PRESENT" "%TSK_STATE%" "-" "0"
    if errorlevel 1 exit /b 4
)
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY %T_ID%: schtasks /Change /TN %T_TARGET% /DISABLE   [was %TSK_STATE%]"
    exit /b 0
)
schtasks /Change /TN "%T_TARGET%" /DISABLE >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "FAILED %T_ID%: cannot disable task %T_TARGET% (access denied?)"
    call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" FAILED
    exit /b 1
)
call "%LIBDIR%\core.cmd" :log INFO "OK %T_ID%: task disabled %T_TARGET%   [was %TSK_STATE%]"
call "%LIBDIR%\core.cmd" :journal_mark "%T_ID%" OK
exit /b 0

rem ========================================================== :revert_one ====
:revert_one
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY revert %R_ID%: schtasks /Change /TN %R_TARGET% /ENABLE  (original state %R_PTYPE%)"
    exit /b 0
)
if /i "%R_PTYPE%"=="Disabled" (
    call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: task was already disabled before us - leaving it disabled"
    call "%LIBDIR%\core.cmd" :journal_setresult "%R_RUN%" "%R_ID%" REVERTED
    exit /b 0
)
schtasks /Change /TN "%R_TARGET%" /ENABLE >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "REVERT FAILED %R_ID%: cannot enable task %R_TARGET% (backup XML: %BACKUPROOT%\%R_RUN%\task_%R_ID%.xml)"
    exit /b 1
)
call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: task enabled %R_TARGET%"
call "%LIBDIR%\core.cmd" :journal_setresult "%R_RUN%" "%R_ID%" REVERTED
exit /b 0
