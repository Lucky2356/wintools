@echo off
rem ============================================================================
rem  syschange.cmd - reversible system configuration (power / network / fs).
rem  Everything here is journalled exactly like a tweak, so "revert" undoes it.
rem  Journal types: PWR (power scheme, hibernate), NET (tcp), FS (fsutil)
rem ============================================================================
if "%~1"=="" exit /b 1
set "_ENTRY=%~1"
shift
goto %_ENTRY%

rem ================================================================ :main ====
:main
if defined OPT_IDS goto sc_go
echo(
echo   system-change will adjust, all of it reversible:
echo     SYS-POWER-SCHEME     monitor timeout 20 min, no sleep, no disk timeout (AC)
echo     SYS-HIBERNATE-OFF    powercfg /h off       (desktops only, skipped on laptops)
echo     SYS-FASTSTARTUP-OFF  HiberbootEnabled = 0  (clean shutdown instead of hybrid)
echo     SYS-LONGPATHS        LongPathsEnabled = 1  (paths longer than 260 chars)
echo     SYS-TCP-AUTOTUNING   autotuninglevel = normal (the Windows default)
echo     SYS-LASTACCESS       NTFS last access updates off
call "%LIBDIR%\core.cmd" :confirm "Apply these system configuration changes"
if errorlevel 1 exit /b 5
call "%LIBDIR%\core.cmd" :restorepoint
:sc_go
set /a _SCFAIL=0

rem ---------------------------------------------------- SYS-POWER-SCHEME ----
call "%LIBDIR%\core.cmd" :want_step SYS-POWER-SCHEME
if errorlevel 1 goto sc_hib
set "PWR_GUID="
for /f "usebackq tokens=3" %%G in (`reg query "HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes" /v ActivePowerScheme 2^>nul ^| findstr /i /c:"REG_"`) do set "PWR_GUID=%%G"
if not defined PWR_GUID (
    call "%LIBDIR%\core.cmd" :log WARN "SKIPPED SYS-POWER-SCHEME (cannot read the active power scheme GUID)"
    goto sc_hib
)
set "_POW=%BACKUPDIR%\scheme_%PWR_GUID%.pow"
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY SYS-POWER-SCHEME: export %PWR_GUID% then powercfg /change monitor-timeout-ac 20 / standby 0 / disk 0"
    goto sc_hib
)
call "%LIBDIR%\core.cmd" :ensure_backupdir
powercfg /export "%_POW%" %PWR_GUID% >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "SKIPPED SYS-POWER-SCHEME (export failed - refusing to change a scheme we could not back up)"
    set /a _SCFAIL+=1
    goto sc_hib
)
call "%LIBDIR%\core.cmd" :journal_add "SYS-POWER-SCHEME" PWR "%PWR_GUID%" "SCHEME" "PRESENT" "SCHEME" "%_POW%" "0"
powercfg /change monitor-timeout-ac 20 >nul 2>&1
powercfg /change standby-timeout-ac 0  >nul 2>&1
powercfg /change disk-timeout-ac 0     >nul 2>&1
call "%LIBDIR%\core.cmd" :log INFO "OK SYS-POWER-SCHEME: timeouts adjusted, exact scheme backup at %_POW%"
call "%LIBDIR%\core.cmd" :journal_mark "SYS-POWER-SCHEME" OK

rem --------------------------------------------------- SYS-HIBERNATE-OFF ----
:sc_hib
call "%LIBDIR%\core.cmd" :want_step SYS-HIBERNATE-OFF
if errorlevel 1 goto sc_reg
if "%ENV_ISLAPTOP%"=="1" (
    call "%LIBDIR%\core.cmd" :log WARN "SKIPPED-GUARD SYS-HIBERNATE-OFF (battery detected - hibernation stays available)"
    goto sc_reg
)
call "%LIBDIR%\reg.cmd" :capture "HKLM\SYSTEM\CurrentControlSet\Control\Power" "HibernateEnabled"
if /i "%RC_DATA%"=="0x0" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED-SAME SYS-HIBERNATE-OFF (hibernation is already off)"
    goto sc_reg
)
call "%LIBDIR%\core.cmd" :journal_add "SYS-HIBERNATE-OFF" PWR "HIBERNATE" "-" "%RC_STATE%" "%RC_TYPE%" "%RC_DATA%" "0"
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY SYS-HIBERNATE-OFF: powercfg /h off   [was %RC_DATA%]"
    goto sc_reg
)
powercfg /h off >nul 2>&1
if errorlevel 1 (
    call "%LIBDIR%\core.cmd" :log ERROR "FAILED SYS-HIBERNATE-OFF"
    call "%LIBDIR%\core.cmd" :journal_mark "SYS-HIBERNATE-OFF" FAILED
    set /a _SCFAIL+=1
) else (
    call "%LIBDIR%\core.cmd" :log INFO "OK SYS-HIBERNATE-OFF: hiberfil.sys released"
    call "%LIBDIR%\core.cmd" :journal_mark "SYS-HIBERNATE-OFF" OK
)

rem ------------------------------------ SYS-FASTSTARTUP-OFF / SYS-LONGPATHS --
:sc_reg
call "%LIBDIR%\core.cmd" :want_step SYS-FASTSTARTUP-OFF
if errorlevel 1 goto sc_longpaths
set "T_ID=SYS-FASTSTARTUP-OFF" & set "T_TYPE=REG"
set "T_TARGET=HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power"
set "T_NAME=HiberbootEnabled" & set "T_VTYPE=REG_DWORD" & set "T_VALUE=0"
call "%LIBDIR%\reg.cmd" :apply_one
if errorlevel 1 set /a _SCFAIL+=1

:sc_longpaths
call "%LIBDIR%\core.cmd" :want_step SYS-LONGPATHS
if errorlevel 1 goto sc_tcp
set "T_ID=SYS-LONGPATHS" & set "T_TYPE=REG"
set "T_TARGET=HKLM\SYSTEM\CurrentControlSet\Control\FileSystem"
set "T_NAME=LongPathsEnabled" & set "T_VTYPE=REG_DWORD" & set "T_VALUE=1"
call "%LIBDIR%\reg.cmd" :apply_one
if errorlevel 1 set /a _SCFAIL+=1

:sc_tcp
rem --------------------------------------------------- SYS-TCP-AUTOTUNING ---
call "%LIBDIR%\core.cmd" :want_step SYS-TCP-AUTOTUNING
if errorlevel 1 goto sc_lastaccess
set "TCP_AUTOTUNING="
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action GetTcpAutotuning 2^>nul`) do set "TCP_%%A=%%B"
if not defined TCP_AUTOTUNING set "TCP_AUTOTUNING=unknown"
if /i "%TCP_AUTOTUNING%"=="unknown" (
    call "%LIBDIR%\core.cmd" :log WARN "SKIPPED SYS-TCP-AUTOTUNING (value unreadable - refusing a change we could not undo)"
    goto sc_lastaccess
)
if /i "%TCP_AUTOTUNING%"=="Normal" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED-SAME SYS-TCP-AUTOTUNING (already Normal)"
    goto sc_lastaccess
)
call "%LIBDIR%\core.cmd" :journal_add "SYS-TCP-AUTOTUNING" NET "autotuninglevel" "-" "PRESENT" "NETSH" "%TCP_AUTOTUNING%" "0"
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY SYS-TCP-AUTOTUNING: netsh int tcp set global autotuninglevel=normal   [was %TCP_AUTOTUNING%]"
    goto sc_lastaccess
)
netsh int tcp set global autotuninglevel=normal >nul 2>&1
call "%LIBDIR%\core.cmd" :log INFO "OK SYS-TCP-AUTOTUNING: normal   [was %TCP_AUTOTUNING%]"
call "%LIBDIR%\core.cmd" :journal_mark "SYS-TCP-AUTOTUNING" OK

rem ------------------------------------------------------- SYS-LASTACCESS ---
:sc_lastaccess
call "%LIBDIR%\core.cmd" :want_step SYS-LASTACCESS
if errorlevel 1 goto sc_end
set "LASTACCESS="
for /f "usebackq tokens=1,2 delims==" %%A in (`%PSH% -Action GetLastAccess 2^>nul`) do set "%%A=%%B"
if not defined LASTACCESS set "LASTACCESS=unknown"
if /i "%LASTACCESS%"=="unknown" (
    call "%LIBDIR%\core.cmd" :log WARN "SKIPPED SYS-LASTACCESS (current value unreadable)"
    goto sc_end
)
if "%LASTACCESS%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "SKIPPED-SAME SYS-LASTACCESS (already 1)"
    goto sc_end
)
call "%LIBDIR%\core.cmd" :journal_add "SYS-LASTACCESS" FS "disablelastaccess" "-" "PRESENT" "FSUTIL" "%LASTACCESS%" "0"
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY SYS-LASTACCESS: fsutil behavior set disablelastaccess 1   [was %LASTACCESS%]"
    goto sc_end
)
fsutil behavior set disablelastaccess 1 >nul 2>&1
call "%LIBDIR%\core.cmd" :log INFO "OK SYS-LASTACCESS: 1   [was %LASTACCESS%]"
call "%LIBDIR%\core.cmd" :journal_mark "SYS-LASTACCESS" OK

:sc_end
call "%LIBDIR%\core.cmd" :log INFO "system-change finished with %_SCFAIL% failure(s). Reboot for hibernate/fast-startup to settle."
if %_SCFAIL% GTR 0 exit /b 4
exit /b 0

rem ========================================================== :revert_one ====
:revert_one
if "%OPT_DRY%"=="1" (
    call "%LIBDIR%\core.cmd" :log INFO "DRY revert %R_ID%: %R_TYPE% %R_TARGET% -> %R_PDATA%"
    exit /b 0
)
if /i "%R_TARGET%"=="HIBERNATE" (
    if /i "%R_PDATA%"=="0x0" (powercfg /h off >nul 2>&1) else (powercfg /h on >nul 2>&1)
    call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: hibernation restored to %R_PDATA%"
    goto rv_done
)
if /i "%R_PTYPE%"=="SCHEME" (
    if not exist "%R_PDATA%" (
        call "%LIBDIR%\core.cmd" :log ERROR "REVERT FAILED %R_ID%: scheme backup missing at %R_PDATA%"
        exit /b 1
    )
    powercfg /import "%R_PDATA%" %R_TARGET% >nul 2>&1
    if errorlevel 1 (
        call "%LIBDIR%\core.cmd" :log ERROR "REVERT FAILED %R_ID%: powercfg /import %R_PDATA%"
        exit /b 1
    )
    powercfg /setactive %R_TARGET% >nul 2>&1
    call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: power scheme %R_TARGET% restored from its .pow backup"
    goto rv_done
)
if /i "%R_PTYPE%"=="NETSH" (
    netsh int tcp set global autotuninglevel=%R_PDATA% >nul 2>&1
    call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: autotuninglevel restored to %R_PDATA%"
    goto rv_done
)
if /i "%R_PTYPE%"=="FSUTIL" (
    fsutil behavior set disablelastaccess %R_PDATA% >nul 2>&1
    call "%LIBDIR%\core.cmd" :log INFO "REVERT %R_ID%: disablelastaccess restored to %R_PDATA%"
    goto rv_done
)
call "%LIBDIR%\core.cmd" :log WARN "REVERT %R_ID%: unknown entry shape, nothing done"
:rv_done
call "%LIBDIR%\core.cmd" :journal_setresult "%R_RUN%" "%R_ID%" REVERTED
exit /b 0
