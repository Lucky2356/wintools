@echo off
rem ============================================================================
rem  wintweaks - safe, fully revertible Windows tweak tool
rem  ASCII only on purpose (console runs in an OEM codepage).
rem  Usage:  wintweaks.cmd <command> [options]     ("wintweaks.cmd help")
rem ============================================================================
setlocal EnableExtensions EnableDelayedExpansion

set "APPROOT=%~dp0"
if "%APPROOT:~-1%"=="\" set "APPROOT=%APPROOT:~0,-1%"
set "LIBDIR=%APPROOT%\lib"
set "DATADIR=%APPROOT%\data"
set "STATEDIR=%APPROOT%\state"
set "BACKUPROOT=%APPROOT%\backups"
set "LOGDIR=%APPROOT%\logs"
set "JOURNAL=%STATEDIR%\applied.dat"
set "PROTECTED=%DATADIR%\protected.def"
set "TWEAKDEF=%DATADIR%\tweaks.def"
set "PSH=powershell -NoProfile -ExecutionPolicy Bypass -File "%LIBDIR%\helpers.ps1""

set "CMDNAME=%~1"
if "%CMDNAME%"=="" set "CMDNAME=help"
shift

rem ---------------------------------------------------------------- defaults
set "OPT_DRY=0"
set "OPT_PROFILE=extended"
set "OPT_RISKY=0"
set "OPT_RECYCLE=0"
set "OPT_STRICT=0"
set "OPT_YES=0"
set "OPT_RUN="
set "OPT_IDS="
set "OPT_USERSID="
set "OPT_RP=ask"
set "OPT_DEEP=0"
set "OPT_LOGOVERRIDE="

:parseargs
if "%~1"=="" goto argsdone
set "A=%~1"
if /i "!A!"=="/dry"             (set "OPT_DRY=1"      & goto nextarg)
if /i "!A!"=="/include-risky"   (set "OPT_RISKY=1"    & goto nextarg)
if /i "!A!"=="/include-recycle" (set "OPT_RECYCLE=1"  & goto nextarg)
if /i "!A!"=="/strict"          (set "OPT_STRICT=1"   & goto nextarg)
if /i "!A!"=="/yes"             (set "OPT_YES=1"      & goto nextarg)
if /i "!A!"=="/restore-point"    (set "OPT_RP=yes"     & goto nextarg)
if /i "!A!"=="/no-restore-point" (set "OPT_RP=no"      & goto nextarg)
if /i "!A!"=="/deep"             (set "OPT_DEEP=1"     & goto nextarg)
if /i "!A:~0,9!"=="/profile:"   (set "OPT_PROFILE=!A:~9!"      & goto nextarg)
if /i "!A:~0,5!"=="/run:"       (set "OPT_RUN=!A:~5!"          & goto nextarg)
if /i "!A:~0,4!"=="/id:"        (set "OPT_IDS=!OPT_IDS!!A:~4! " & goto nextarg)
if /i "!A:~0,10!"=="/user-sid:" (set "OPT_USERSID=!A:~10!"     & goto nextarg)
if /i "!A:~0,5!"=="/log:"       (set "OPT_LOGOVERRIDE=!A:~5!"  & goto nextarg)
echo [ERROR] Unknown option: !A!
endlocal & exit /b 1
:nextarg
shift
goto parseargs
:argsdone
if defined OPT_IDS set "OPT_IDS= !OPT_IDS!"

if /i not "%OPT_PROFILE%"=="core" if /i not "%OPT_PROFILE%"=="balanced" if /i not "%OPT_PROFILE%"=="extended" (
    echo [ERROR] Unknown profile: %OPT_PROFILE%  ^(use core^|balanced^|extended^)
    endlocal & exit /b 1
)

rem ---- validate the command before doing anything privileged --------------
set "_KNOWN=0"
for %%C in (help menu status apply revert verify cleanup system-change) do if /i "%CMDNAME%"=="%%C" set "_KNOWN=1"
if "%_KNOWN%"=="0" (
    echo [ERROR] Unknown command: %CMDNAME%
    call :print_help
    endlocal ^& exit /b 1
)

if /i "%CMDNAME%"=="help" goto do_help
if /i "%CMDNAME%"=="menu" (
    endlocal
    call "%~dp0menu.cmd"
    exit /b %ERRORLEVEL%
)

call "%LIBDIR%\core.cmd" :init
if errorlevel 1 goto bail

if /i "%CMDNAME%"=="status"  goto do_status

call "%LIBDIR%\core.cmd" :preflight
if errorlevel 1 goto bail

if /i "%CMDNAME%"=="apply"         goto do_apply
if /i "%CMDNAME%"=="revert"        goto do_revert
if /i "%CMDNAME%"=="verify"        goto do_verify
if /i "%CMDNAME%"=="cleanup"       goto do_cleanup
if /i "%CMDNAME%"=="system-change" goto do_syschange

rem ------------------------------------------------------------------ apply
:do_apply
call "%LIBDIR%\core.cmd" :plan_apply
call "%LIBDIR%\core.cmd" :confirm "Apply tweaks now (profile=%OPT_PROFILE% risky=%OPT_RISKY% dry=%OPT_DRY%)"
if errorlevel 1 goto bail
call "%LIBDIR%\core.cmd" :restorepoint
call :run_tweaks
goto finish

rem ----------------------------------------------------------------- revert
:do_revert
call "%LIBDIR%\core.cmd" :confirm "Revert previously applied changes"
if errorlevel 1 goto bail
call "%LIBDIR%\core.cmd" :revert_journal
goto finish

:do_verify
call "%LIBDIR%\core.cmd" :verify_journal
goto finish

:do_cleanup
call "%LIBDIR%\cleanup.cmd" :main
goto finish

:do_syschange
call "%LIBDIR%\syschange.cmd" :main
goto finish

:do_status
call "%LIBDIR%\core.cmd" :status
goto finish

rem ------------------------------------------------- iterate over tweaks.def
:run_tweaks
set /a FAILS=0
if not exist "%TWEAKDEF%" (
    call "%LIBDIR%\core.cmd" :log ERROR "Missing tweak definition file: %TWEAKDEF%"
    exit /b 1
)
for /f "usebackq eol=# tokens=1-9 delims=|" %%A in ("%TWEAKDEF%") do (
    call :do_tweak "%%A" "%%B" "%%C" "%%D" "%%E" "%%F" "%%G" "%%H" "%%I"
    if errorlevel 6 exit /b 6
    if "%OPT_STRICT%"=="1" if !FAILS! GTR 0 (
        call "%LIBDIR%\core.cmd" :log ERROR "Strict mode: rolling back run %RUNID%"
        set "OPT_RUN=%RUNID%"
        set "OPT_IDS="
        call "%LIBDIR%\core.cmd" :revert_journal
        exit /b 4
    )
)
if !FAILS! GTR 0 (
    call "%LIBDIR%\core.cmd" :log WARN "Completed with !FAILS! failure(s). Roll this run back with: wintweaks.cmd revert /run:%RUNID%"
    exit /b 4
)
exit /b 0

:do_tweak
set "T_ID=%~1"
set "T_PROFILE=%~2"
set "T_RISK=%~3"
set "T_OS=%~4"
set "T_TYPE=%~5"
set "T_TARGET=%~6"
set "T_NAME=%~7"
set "T_VTYPE=%~8"
set "T_VALUE=%~9"
if not defined T_ID exit /b 0
call "%LIBDIR%\core.cmd" :should_run
if errorlevel 6 exit /b 6
if errorlevel 1 exit /b 0
if /i "%T_TYPE%"=="REG"  call "%LIBDIR%\reg.cmd"  :apply_one
if /i "%T_TYPE%"=="SVC"  call "%LIBDIR%\svc.cmd"  :apply_one
if /i "%T_TYPE%"=="TASK" call "%LIBDIR%\task.cmd" :apply_one
if /i "%T_TYPE%"=="APPX" call "%LIBDIR%\appx.cmd" :apply_one
if /i "%T_TYPE%"=="EDGE" call "%LIBDIR%\appx.cmd" :apply_one
if errorlevel 1 set /a FAILS+=1
exit /b 0

rem ----------------------------------------------------------------- finish
:finish
set "RC=%ERRORLEVEL%"
%PSH% -Action WriteManifest -Root "%APPROOT%" >nul 2>&1
call "%LIBDIR%\core.cmd" :log INFO "Finished: exit=%RC%  log=%LOGFILE%"
endlocal & exit /b %RC%

:bail
set "RC=%ERRORLEVEL%"
endlocal & exit /b %RC%

:do_help
call :print_help
endlocal & exit /b 0

:print_help
echo(
echo   wintweaks - safe, fully revertible Windows tweaks
echo(
echo   wintweaks.cmd ^<command^> [options]
echo(
echo   Commands:
echo     apply           apply tweaks from data\tweaks.def according to the profile
echo     revert          restore everything recorded in state\applied.dat
echo     status          show OS info and what is currently applied
echo     verify          compare the live system against the journal (read only)
echo     cleanup         safe disk cleanup (never touched by revert)
echo     system-change   reversible system configuration (power / network / fs)
echo     menu            interactive menu in Russian - the easy way in
echo     help            this text
echo(
echo   Options:
echo     /dry                  show what would change, change nothing
echo     /profile:core^|balanced^|extended     tweak set (default: extended)
echo     /id:^<TWEAK-ID^>        act on a single tweak only (repeatable)
echo     /run:^<RUNID^>          revert only that run
echo     /include-risky        allow RISK=high tweaks (SysMain, WSearch, WER)
echo     /include-recycle      allow recycle bin purge during cleanup
echo     /strict               auto-rollback the current run on the first failure
echo     /yes                  never prompt (unattended)
echo     /restore-point        always create a system restore point first
echo     /no-restore-point     never create one (default: ask once per run)
echo     /deep                 app removal also wipes user data, registry
echo                           traces and the provisioned copy (backed up first)
echo     /user-sid:^<SID^>       write HKCU tweaks into HKU\^<SID^> instead
echo     /log:^<path^>           alternative log file
echo(
echo   Exit codes: 0 ok  1 usage  2 not admin  3 unsupported OS
echo               4 partial failure  5 aborted  6 protected object
echo(
exit /b 0
