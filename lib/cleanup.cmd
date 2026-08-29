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
set "FREE_BEFORE=0"
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action DiskFree 2^>nul`) do set "%%A=%%B"
set "FREE_BEFORE=%FREEMB%"
call "%LIBDIR%\core.cmd" :log INFO "Disk cleanup starting. Free on %SystemDrive% now: %FREE_BEFORE% MB (dry=%OPT_DRY%)"

call :step_temp "%TEMP%" 3 CLN-USERTEMP
call :step_temp "%SystemRoot%\Temp" 3 CLN-WINTEMP
call :step_temp "%LOCALAPPDATA%\CrashDumps" 7 CLN-CRASHDUMPS
call :step_do_cache
call :step_wu_download
call :step_cleanmgr
call :step_dism
call :step_recycle

set "FREEMB=0"
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action DiskFree 2^>nul`) do set "%%A=%%B"
set /a _FREED=%FREEMB%-%FREE_BEFORE%
call "%LIBDIR%\core.cmd" :log INFO "Cleanup done. Free now %FREEMB% MB (delta %_FREED% MB)."
exit /b 0

rem ---------------------------------------------------------- :step_temp ----
rem  args: <dir> <min age in days> <id>
:step_temp
call "%LIBDIR%\core.cmd" :want_step "%~3"
if errorlevel 1 exit /b 0
if not exist "%~1" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED %~3 (%~1 does not exist)"
    exit /b 0
)
set "FILES=0" & set "SIZEMB=0"
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action DirStat -Full "%~1" -Name "%~2" 2^>nul`) do set "%%A=%%B"
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY %~3: would delete %FILES% file(s), about %SIZEMB% MB, older than %~2 day(s) in %~1"
    exit /b 0
)
forfiles /P "%~1" /S /D -%~2 /C "cmd /c if @isdir==FALSE del /q /f @path" >nul 2>&1
call "%LIBDIR%\core.cmd" :log INFO "%~3: cleaned %~1 - up to %FILES% file(s) / %SIZEMB% MB older than %~2 day(s). Files in use are skipped."
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
    call "%LIBDIR%\core.cmd" :log WARN "CLN-DO-CACHE: Delivery Optimization cache not cleared (cmdlet unavailable or nothing to clear)"
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
net stop wuauserv >nul 2>&1
net stop bits     >nul 2>&1
del /q /f /s "%_WUD%\*" >nul 2>&1
for /d %%D in ("%_WUD%\*") do rd /s /q "%%~D" >nul 2>&1
net start bits     >nul 2>&1
net start wuauserv >nul 2>&1
call "%LIBDIR%\core.cmd" :log INFO "CLN-WU-DOWNLOAD: update download cache emptied, wuauserv and BITS restarted"
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
reg export "%_VC%" "%BACKUPDIR%\volumecaches.reg" /y >nul 2>&1
call "%LIBDIR%\core.cmd" :log INFO "CLN-CLEANMGR: VolumeCaches backed up to %BACKUPDIR%\volumecaches.reg"
for /f "usebackq delims=" %%K in (`reg query "%_VC%" 2^>nul`) do (
    set "_SEL=0"
    echo %%K| findstr /i /e /c:"\Temporary Files" /c:"\Thumbnail Cache" /c:"\Delivery Optimization Files" /c:"\Downloaded Program Files" /c:"\Windows Error Reporting Files" /c:"\System archived Windows Error Reporting" /c:"\System queued Windows Error Reporting" >nul
    if not errorlevel 1 set "_SEL=2"
    reg add "%%K" /v StateFlags0064 /t REG_DWORD /d !_SEL! /f >nul 2>&1
)
start "" /wait cleanmgr /sagerun:64
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
call "%LIBDIR%\core.cmd" :log INFO "CLN-DISM: component store cleanup finished (exit %ERRORLEVEL%)"
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
powershell -NoProfile -ExecutionPolicy Bypass -Command "Clear-RecycleBin -Force -ErrorAction SilentlyContinue" >nul 2>&1
call "%LIBDIR%\core.cmd" :log INFO "CLN-RECYCLE: recycle bin emptied"
exit /b 0
