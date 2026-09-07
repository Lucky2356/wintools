@echo off
rem ============================================================================
rem  cleanup.cmd - safe disk cleanup.
rem  Deleting temp files is irreversible by nature, so NOTHING here is written
rem  to the journal and "revert" never touches any of it. Every irreversible
rem  step asks for its own confirmation and honours /dry.
rem  Hard rules: no DISM /ResetBase, no Windows.old, no Windows Update Cleanup,
rem  no user data (Downloads, browser profiles), recycle bin only on request.
rem ============================================================================
if "%~1"=="" exit /b 1
set "_ENTRY=%~1"
shift
goto %_ENTRY%

:main
set "CLEANFAIL=0"
if not defined OPT_IDS set "OPT_IDS= CLN-USERTEMP CLN-WINTEMP "
set "FREE_BEFORE=0"
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action DiskFree 2^>nul`) do set "%%A=%%B"
set "FREE_BEFORE=%FREEMB%"
call "%LIBDIR%\core.cmd" :log INFO "Disk cleanup starting. Free on %SystemDrive% now: %FREE_BEFORE% MB (dry=%OPT_DRY%)"

call :step_temp CLN-USERTEMP
if errorlevel 1 set "CLEANFAIL=1"
call :step_temp CLN-WINTEMP
if errorlevel 1 set "CLEANFAIL=1"
call :step_temp CLN-CRASHDUMPS
if errorlevel 1 set "CLEANFAIL=1"
call :step_do_cache
if errorlevel 1 set "CLEANFAIL=1"
call :step_wu_download
if errorlevel 1 set "CLEANFAIL=1"
call :step_cleanmgr
if errorlevel 1 set "CLEANFAIL=1"
call :step_dism
if errorlevel 1 set "CLEANFAIL=1"
call :step_recycle
if errorlevel 1 set "CLEANFAIL=1"

set "FREEMB=0"
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action DiskFree 2^>nul`) do set "%%A=%%B"
set /a _FREED=%FREEMB%-%FREE_BEFORE%
call "%LIBDIR%\core.cmd" :log INFO "Cleanup finished. Free now %FREEMB% MB (delta %_FREED% MB; includes other system activity)."
if "%CLEANFAIL%"=="1" exit /b 4
exit /b 0

rem ---------------------------------------------------------- :step_temp ----
rem  args: <cleanup ID>; the helper resolves an allowlisted directory.
:step_temp
call "%LIBDIR%\core.cmd" :want_step "%~1"
if errorlevel 1 exit /b 0
%PSH% -Action CleanupFiles -Name "%~1" >> "%LOGFILE%" 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log WARN "%~1: incomplete cleanup; see the log for counts."
    exit /b 4
)
call "%LIBDIR%\core.cmd" :log INFO "%~1: completed (dry=%OPT_DRY%); counts in log."
exit /b 0

rem ------------------------------------------------------ :step_do_cache ----
:step_do_cache
call "%LIBDIR%\core.cmd" :want_step CLN-DO-CACHE
if errorlevel 1 exit /b 0
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY CLN-DO-CACHE: would run Delete-DeliveryOptimizationCache"
    exit /b 0
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Delete-DeliveryOptimizationCache -Force -ErrorAction Stop } catch { exit 1 }" >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log WARN "CLN-DO-CACHE: cache not cleared"
    exit /b 4
) else (
    call "%LIBDIR%\core.cmd" :log INFO "CLN-DO-CACHE: Delivery Optimization cache cleared"
)
exit /b 0

rem --------------------------------------------------- :step_wu_download ----
rem  Only SoftwareDistribution\Download is emptied. DataStore (update history
rem  and the ability to uninstall updates) is never touched.
:step_wu_download
call "%LIBDIR%\core.cmd" :want_step CLN-WU-DOWNLOAD
if errorlevel 1 exit /b 0
set "_WUD=%SystemRoot%\SoftwareDistribution\Download"
if not exist "%_WUD%" exit /b 0
set "FILES=0" & set "SIZEMB=0"
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action DirStat -Full "%_WUD%" -Name 0 2^>nul`) do set "%%A=%%B"
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY CLN-WU-DOWNLOAD: would free about %SIZEMB% MB of downloaded update packages (they redownload on demand)"
    exit /b 0
)
call "%LIBDIR%\core.cmd" :confirm "CLN-WU-DOWNLOAD: delete %SIZEMB% MB of cached update downloads? Update history is kept, packages redownload when needed."
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED CLN-WU-DOWNLOAD (declined)"
    exit /b 0
)
%PSH% -Action CleanupUpdateCache >> "%LOGFILE%" 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "CLN-WU-DOWNLOAD: incomplete cleanup or service restoration; see log."
    exit /b 4
)
call "%LIBDIR%\core.cmd" :log INFO "CLN-WU-DOWNLOAD: download files cleared; original service running states restored."
exit /b 0

rem ------------------------------------------------------ :step_cleanmgr ----
rem  A conservative, explicit handler whitelist under our own sageset id 64.
:step_cleanmgr
call "%LIBDIR%\core.cmd" :want_step CLN-CLEANMGR
if errorlevel 1 exit /b 0
where cleanmgr >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log WARN "SKIPPED CLN-CLEANMGR (cleanmgr.exe is not present on this system)"
    exit /b 0
)
set "_VC=HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches"
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY CLN-CLEANMGR: would select Temporary Files, Thumbnail Cache, Delivery Optimization Files, Windows Error Reporting, Downloaded Program Files and run cleanmgr /sagerun:64"
    exit /b 0
)
call "%LIBDIR%\core.cmd" :confirm "CLN-CLEANMGR: run Disk Cleanup with a conservative handler set? Windows.old and Update Cleanup stay OFF."
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED CLN-CLEANMGR (declined)"
    exit /b 0
)
call "%LIBDIR%\core.cmd" :ensure_backupdir
if errorlevel 1 exit /b 4
reg export "%_VC%" "%BACKUPDIR%\volumecaches.reg" /y >nul 2>&1
if errorlevel 1 exit /b 4
call "%LIBDIR%\core.cmd" :log INFO "CLN-CLEANMGR: VolumeCaches backed up to %BACKUPDIR%\volumecaches.reg"
%PSH% -Action CleanupManager >> "%LOGFILE%" 2>&1
if errorlevel 1 exit /b 4
call "%LIBDIR%\core.cmd" :log INFO "CLN-CLEANMGR: finished"
exit /b 0

rem ---------------------------------------------------------- :step_dism ----
rem  StartComponentCleanup only. /ResetBase is banned: it removes the ability
rem  to uninstall installed updates.
:step_dism
call "%LIBDIR%\core.cmd" :want_step CLN-DISM
if errorlevel 1 exit /b 0
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY CLN-DISM: running DISM /Online /Cleanup-Image /AnalyzeComponentStore (read only)"
    DISM /Online /Cleanup-Image /AnalyzeComponentStore >> "%LOGFILE%" 2>&1
    exit /b 0
)
call "%LIBDIR%\core.cmd" :confirm "CLN-DISM: run component store cleanup? This can take several minutes. Update uninstall stays possible (no /ResetBase)."
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED CLN-DISM (declined)"
    exit /b 0
)
DISM /Online /Cleanup-Image /StartComponentCleanup >> "%LOGFILE%" 2>&1
if errorlevel 1 exit /b 4
call "%LIBDIR%\core.cmd" :log INFO "CLN-DISM: component store cleanup finished"
exit /b 0

rem ------------------------------------------------------- :step_recycle ----
:step_recycle
call "%LIBDIR%\core.cmd" :want_step CLN-RECYCLE
if errorlevel 1 exit /b 0
if not "%OPT_RECYCLE%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED CLN-RECYCLE (recycle bin holds user data - pass /include-recycle to empty it)"
    exit /b 0
)
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY CLN-RECYCLE: would empty the recycle bin"
    exit /b 0
)
call "%LIBDIR%\core.cmd" :confirm "CLN-RECYCLE: permanently empty the recycle bin? This cannot be undone."
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED CLN-RECYCLE (declined)"
    exit /b 0
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Clear-RecycleBin -Force -ErrorAction Stop } catch { exit 4 }" >nul 2>&1
if errorlevel 1 exit /b 4
call "%LIBDIR%\core.cmd" :log INFO "CLN-RECYCLE: recycle bin emptied"
exit /b 0
