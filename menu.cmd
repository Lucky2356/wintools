@echo off
rem ============================================================================
rem  menu.cmd - interactive Russian catalogue UI for wintweaks.
rem
rem  Stored in CP866 (OEM Cyrillic). Everything ABOVE the "chcp 866" line is
rem  deliberately ASCII: cmd.exe decodes a batch file line by line using the
rem  code page that is active at that moment. The original code page is
rem  restored on exit.
rem
rem  "<nul" on chcp and on every for /f command mode call: those child
rem  processes inherit stdin and would swallow it when the menu is driven from
rem  a file. Interactively it changes nothing.
rem
rem  Text coming from data\descr.ru is always assigned to a variable and echoed
rem  with "echo(!VAR!" - delayed expansion is not re-parsed, so parentheses in
rem  the Russian descriptions cannot break the enclosing for/if block.
rem ============================================================================
setlocal EnableExtensions EnableDelayedExpansion
title wintweaks

set "APPROOT=%~dp0"
if "%APPROOT:~-1%"=="\" set "APPROOT=%APPROOT:~0,-1%"
set "JOURNAL=%APPROOT%\state\applied.dat"
set "ENGINE=%APPROOT%\wintweaks.cmd"
set "DESCR=%APPROOT%\data\descr.ru"
set "TWEAKDEF=%APPROOT%\data\tweaks.def"
set "PSH=powershell -NoProfile -ExecutionPolicy Bypass -File "%APPROOT%\lib\helpers.ps1""

if not exist "%ENGINE%" (
    echo [ERROR] wintweaks.cmd not found next to menu.cmd
    exit /b 1
)
if not exist "%DESCR%" (
    echo [ERROR] data\descr.ru not found
    exit /b 1
)

set "CP_ORIG="
for /f "tokens=2 delims=:" %%C in ('chcp 2^>nul ^<nul') do set "CP_ORIG=%%C"
set "CP_ORIG=%CP_ORIG: =%"
set "CP_ORIG=%CP_ORIG:.=%"
if not defined CP_ORIG set "CP_ORIG=437"
for /f "delims=0123456789" %%D in ("%CP_ORIG%") do set "CP_ORIG=437"
chcp 866 >nul <nul

fltmc >nul 2>&1
if errorlevel 1 goto elevate

set "M_DRY=0"
set "M_RP=0"
set "M_HIDE=0"
set "M_DEEP=0"
set /a EMPTYIN=0
call :inventory

rem ============================== главное меню ================================
:main
set "CAT="
call :collect
cls
echo ==================================================================
echo   wintweaks - обратимые твики Windows
echo ==================================================================
echo   Система : !OS_NAME!
echo   Права   : администратор
echo   Журнал  : применено !S_OK!, откачено !S_REV!, вручную !S_MAN!
if !S_PEND! GTR 0 echo   ВНИМАНИЕ: !S_PEND! незавершённых записей - выполните откат
echo ------------------------------------------------------------------
echo   Режим   : [!F_DRY!] пробный прогон, ничего не менять   - клавиша D
echo             [!F_RP!] точка восстановления перед изменением - клавиша T
echo             [!F_HIDE!] показывать то, чего нет на этом ПК     - клавиша H
echo             [!F_DEEP!] глубокое удаление приложений со следами - клавиша G
echo ------------------------------------------------------------------
call :catline  1 PRIV  "Приватность и телеметрия"
call :catline  2 UI    "Интерфейс и Проводник"
call :catline  3 SVC   "Службы"
call :catline  4 TASK  "Задачи планировщика"
call :catline  5 UPD   "Обновления Windows"
call :catline  6 PERF  "Производительность и игры"
call :catline  7 EDGE  "Microsoft Edge"
call :catline  8 APPS  "Встроенные приложения"
call :catline  9 SYS   "Система: питание, сеть, диск"
call :catline 10 CLEAN "Очистка диска"
echo ------------------------------------------------------------------
echo    S. Статус и журнал      V. Проверка системы     L. Логи
echo    P. Создать точку восстановления сейчас
echo    R. ОТКАТИТЬ ВСЁ         0. Выход
echo ==================================================================
set "CHOICE="
set /p "CHOICE=  Ваш выбор: "
if defined CHOICE for /f "delims=" %%X in ("!CHOICE!") do set "CHOICE=%%X"
if not defined CHOICE (
    set /a EMPTYIN+=1
    if !EMPTYIN! GEQ 3 goto quit
    goto main
)
set /a EMPTYIN=0
if /i "!CHOICE!"=="D" (call :toggle M_DRY & goto main)
if /i "!CHOICE!"=="S" (call :run "status" & goto main)
if /i "!CHOICE!"=="V" (call :run "verify" & goto main)
if /i "!CHOICE!"=="L" goto a_logs
if /i "!CHOICE!"=="T" (call :toggle M_RP & goto main)
if /i "!CHOICE!"=="H" (call :toggle M_HIDE & goto main)
if /i "!CHOICE!"=="G" (call :toggle M_DEEP & goto main)
if /i "!CHOICE!"=="P" goto a_makerp
if /i "!CHOICE!"=="R" goto a_revert_all
if "!CHOICE!"=="0"  goto quit
if "!CHOICE!"=="1"  (set "CAT=PRIV"  & set "CATNAME=Приватность и телеметрия"      & goto category)
if "!CHOICE!"=="2"  (set "CAT=UI"    & set "CATNAME=Интерфейс и Проводник"         & goto category)
if "!CHOICE!"=="3"  (set "CAT=SVC"   & set "CATNAME=Службы"                        & goto category)
if "!CHOICE!"=="4"  (set "CAT=TASK"  & set "CATNAME=Задачи планировщика"           & goto category)
if "!CHOICE!"=="5"  (set "CAT=UPD"   & set "CATNAME=Обновления Windows"            & goto category)
if "!CHOICE!"=="6"  (set "CAT=PERF"  & set "CATNAME=Производительность и игры"     & goto category)
if "!CHOICE!"=="7"  (set "CAT=EDGE"  & set "CATNAME=Microsoft Edge"                & goto category)
if "!CHOICE!"=="8"  (set "CAT=APPS"  & set "CATNAME=Встроенные приложения"         & goto category)
if "!CHOICE!"=="9"  (set "CAT=SYS"   & set "CATNAME=Система: питание, сеть, диск"  & goto category)
if "!CHOICE!"=="10" (set "CAT=CLEAN" & set "CATNAME=Очистка диска"                 & goto category)
echo   Нет такого пункта.
call :pause
goto main

rem ============================ список категории ==============================
:category
call :collect
cls
echo ==================================================================
echo   !CATNAME!
echo ==================================================================
echo   [+] сделано нами   [=] уже в нужном состоянии   [ ] не выполнено
echo   [-] нет на этом ПК  [x] недоступно в этой версии Windows
echo ------------------------------------------------------------------
set /a N=0
for /f "usebackq eol=# tokens=1-6 delims=|" %%A in ("%DESCR%") do (
    if /i "%%B"=="!CAT!" (
        call :state "%%A" ST
        set "_SHOW=1"
        if "!ST!"=="-" if not "!M_HIDE!"=="1" set "_SHOW=0"
        if "!_SHOW!"=="1" (
        set /a N+=1
        set "ID!N!=%%A"
        set "NM=%%C"
        set "NUM=  !N!"
        set "NUM=!NUM:~-3!"
        echo(  !NUM!. [!ST!] !NM!
        )
    )
)
echo ------------------------------------------------------------------
echo   Номер пункта - открыть описание и выполнить
if /i not "!CAT!"=="CLEAN" echo   A - выполнить все невыполненные     R - откатить всю категорию
if /i "!CAT!"=="CLEAN"     echo   A - выполнить все шаги очистки
echo   0 - назад
echo ==================================================================
set "CHOICE="
set /p "CHOICE=  Ваш выбор: "
if defined CHOICE for /f "delims=" %%X in ("!CHOICE!") do set "CHOICE=%%X"
if not defined CHOICE (
    set /a EMPTYIN+=1
    if !EMPTYIN! GEQ 3 goto quit
    goto category
)
set /a EMPTYIN=0
if "!CHOICE!"=="0" goto main
if /i "!CHOICE!"=="A" goto cat_apply_all
if /i "!CHOICE!"=="R" goto cat_revert_all
set "SEL="
for /f "delims=0123456789" %%D in ("!CHOICE!") do set "SEL=BAD"
if defined SEL (
    echo   Введите номер пункта.
    call :pause
    goto category
)
if !CHOICE! LSS 1 goto category
if !CHOICE! GTR !N! goto category
set "CURID=!ID%CHOICE%!"
goto card

rem ============================== карточка твика ==============================
:card
call :collect
call :state "!CURID!" ST
call :meta "!CURID!"
cls
echo ==================================================================
for /f "usebackq eol=# tokens=1-6 delims=|" %%A in ("%DESCR%") do (
    if /i "%%A"=="!CURID!" (
        set "NM=%%C" & set "D1=%%D" & set "D2=%%E" & set "D3=%%F"
    )
)
echo(  !NM!
echo ==================================================================
echo   Идентификатор : !CURID!
if "!ST!"=="+" echo   Состояние     : ВЫПОЛНЕНО этой программой
if "!ST!"=="=" echo   Состояние     : уже в нужном состоянии, менять нечего
if "!ST!"=="-" echo   Состояние     : на этом ПК отсутствует
if "!ST!"==" " echo   Состояние     : не выполнено
if "!ST!"=="x" echo   Состояние     : недоступно в этой версии Windows
set "_INF=!INFO_%CURID%!"
if defined _INF if not "!_INF!"=="-" echo   Сейчас        : !_INF!
echo   Тип           : !M_KIND!
echo   Риск          : !M_RISK!
echo   Откат         : !M_UNDO!
echo ------------------------------------------------------------------
if "!M_DEEP!"=="1" if /i "!M_TYPE!"=="APPX" echo(  РЕЖИМ ГЛУБОКОЙ ОЧИСТКИ: будут удалены также данные приложения в
if "!M_DEEP!"=="1" if /i "!M_TYPE!"=="APPX" echo(  AppData, ключ активации в реестре и заготовка для новых учётных
if "!M_DEEP!"=="1" if /i "!M_TYPE!"=="APPX" echo(  записей. Всё это сначала копируется в папку backups.
if "!M_DEEP!"=="1" if /i "!M_TYPE!"=="APPX" echo ------------------------------------------------------------------
if not "!D1!"=="-" echo(  !D1!
if not "!D2!"=="-" echo(  !D2!
if not "!D3!"=="-" echo(  !D3!
echo ------------------------------------------------------------------
if /i not "!M_UNDO:~0,4!"=="НЕТ," (
    echo    1. Выполнить          2. Откатить          0. Назад
) else (
    echo    1. Выполнить                               0. Назад
)
echo ==================================================================
set "CHOICE="
set /p "CHOICE=  Ваш выбор: "
if defined CHOICE for /f "delims=" %%X in ("!CHOICE!") do set "CHOICE=%%X"
if not defined CHOICE (
    set /a EMPTYIN+=1
    if !EMPTYIN! GEQ 3 goto quit
    goto card
)
set /a EMPTYIN=0
if "!CHOICE!"=="0" goto category
if "!CHOICE!"=="1" (call :do_apply "!CURID!" & goto card)
if "!CHOICE!"=="2" (call :do_revert "!CURID!" & goto card)
goto card

rem =============================== действия ===================================
:do_apply
set "_ID=%~1"
call :meta "!_ID!"
set "_FLAGS=/include-risky /id:!_ID!"
if "!M_DEEP!"=="1" if /i "!M_TYPE!"=="APPX" set "_FLAGS=/deep !_FLAGS!"
if "%M_DRY%"=="1" (
    call :run "!M_VERB! /dry !_FLAGS!"
    exit /b 0
)
if /i "!_ID!"=="EDGE-UNINSTALL" (
    echo(
    echo   Удаление Edge НЕОБРАТИМО: локальной копии не сохраняется.
    echo   Вернуть его можно будет только командой winget install --id Microsoft.Edge -e
    call :ask "Всё равно удалить Microsoft Edge"
    if errorlevel 1 exit /b 0
)
call :run "!M_VERB! /yes !_FLAGS!"
exit /b 0

:do_revert
set "_ID=%~1"
call :meta "!_ID!"
if /i "!M_UNDO:~0,4!"=="НЕТ," (
    echo(
    echo   Для этого пункта откат не предусмотрен: !M_UNDO!
    call :pause
    exit /b 0
)
if "%M_DRY%"=="1" (
    call :run "revert /dry /id:!_ID!"
    exit /b 0
)
call :run "revert /yes /id:!_ID!"
exit /b 0

:cat_apply_all
set "_IDS="
for /f "usebackq eol=# tokens=1,2 delims=|" %%A in ("%DESCR%") do (
    if /i "%%B"=="!CAT!" (
        call :state "%%A" ST
        if not "!ST!"=="+" set "_IDS=!_IDS! /id:%%A"
    )
)
if not defined _IDS (
    echo(
    echo   В этой категории всё уже выполнено.
    call :pause
    goto category
)
call :cat_verb
echo(
echo   Будут выполнены все невыполненные пункты категории !CATNAME!.
if "%M_DRY%"=="1" (
    call :run "!CV! /dry !_IDS!"
    goto category
)
call :ask "Продолжить"
if errorlevel 1 goto category
call :run "!CV! /yes /include-risky !_IDS!"
goto category

:cat_revert_all
set "_IDS="
for /f "usebackq eol=# tokens=1,2 delims=|" %%A in ("%DESCR%") do (
    if /i "%%B"=="!CAT!" (
        call :state "%%A" ST
        if "!ST!"=="+" set "_IDS=!_IDS! /id:%%A"
    )
)
if not defined _IDS (
    echo(
    echo   В этой категории нечего откатывать.
    call :pause
    goto category
)
if "%M_DRY%"=="1" (
    call :run "revert /dry !_IDS!"
    goto category
)
call :ask "Откатить все выполненные пункты категории"
if errorlevel 1 goto category
call :run "revert /yes !_IDS!"
goto category

:a_revert_all
if !S_OK! EQU 0 if !S_PEND! EQU 0 (
    echo(
    echo   В журнале нет выполненных записей - откатывать нечего.
    call :pause
    goto main
)
if "%M_DRY%"=="1" (
    call :run "revert /dry"
    goto main
)
echo(
echo   Будет восстановлено исходное состояние ВСЕХ записей журнала
echo   в порядке, обратном применению: !S_OK! шт.
call :ask "Откатить всё"
if errorlevel 1 goto main
call :run "revert /yes"
goto main

:a_logs
cls
echo ==================================================================
echo   Логи      : %APPROOT%\logs
echo   Резервные : %APPROOT%\backups
echo   Журнал    : %JOURNAL%
echo ==================================================================
set "LASTLOG="
for /f "delims=" %%F in ('dir /b /o-d "%APPROOT%\logs\*.log" 2^>nul ^<nul') do (
    set "LASTLOG=%%F"
    goto gotlog
)
:gotlog
if not defined LASTLOG (
    echo   Логов пока нет.
) else (
    echo   Последний лог: !LASTLOG!
    echo ------------------------------------------------------------------
    powershell -NoProfile -Command "Get-Content -LiteralPath '%APPROOT%\logs\!LASTLOG!' -Tail 25" <nul
    echo ------------------------------------------------------------------
)
call :ask "Открыть папки с логами и копиями в проводнике"
if not errorlevel 1 (
    start "" explorer "%APPROOT%\logs"
    start "" explorer "%APPROOT%\backups"
)
goto main

rem ============================ вспомогательное ===============================

rem  :meta <ID> -> M_KIND M_RISK M_UNDO M_VERB
rem  Достаём тип, риск и команду движка из data\tweaks.def. Псевдопункты
rem  разделов Система и Очистка в таблице отсутствуют - у них своя ветка.
:meta
set "M_TYPE=" & set "M_RISK=низкий" & set "M_OS=any" & set "M_PROF="
for /f "usebackq eol=# tokens=1-5 delims=|" %%A in ("%TWEAKDEF%") do (
    if /i "%%A"=="%~1" (
        set "M_PROF=%%B" & set "M_RISKRAW=%%C" & set "M_OS=%%D" & set "M_TYPE=%%E"
    )
)
set "M_VERB=apply"
set "M_UNDO=есть, полный"
if /i "%M_RISKRAW%"=="low"  set "M_RISK=низкий"
if /i "%M_RISKRAW%"=="med"  set "M_RISK=средний"
if /i "%M_RISKRAW%"=="high" set "M_RISK=ВЫСОКИЙ"
if /i "%M_TYPE%"=="REG"  set "M_KIND=параметр реестра"
if /i "%M_TYPE%"=="SVC"  set "M_KIND=служба Windows"
if /i "%M_TYPE%"=="TASK" set "M_KIND=задача планировщика"
if /i "%M_TYPE%"=="APPX" set "M_KIND=встроенное приложение"
if /i "%M_TYPE%"=="APPX" if "%M_DEEP%"=="1" set "M_KIND=приложение, глубокое удаление"
if /i "%M_TYPE%"=="APPX" if "%M_DEEP%"=="1" set "M_UNDO=есть, кроме записи для новых учёток"
if /i "%M_TYPE%"=="EDGE" set "M_KIND=удаление Microsoft Edge"
if /i "%M_TYPE%"=="EDGE" set "M_UNDO=НЕТ, только переустановка вручную"
set "_P4=%~1"
if /i "!_P4:~0,4!"=="SYS-" (
    set "M_KIND=настройка системы"
    set "M_VERB=system-change"
    set "M_RISK=низкий"
)
if /i "!_P4:~0,4!"=="CLN-" (
    set "M_KIND=шаг очистки диска"
    set "M_VERB=cleanup"
    set "M_RISK=средний"
    set "M_UNDO=НЕТ, удаление файлов необратимо"
)
exit /b 0

:cat_verb
set "CV=apply"
if /i "!CAT!"=="SYS"   set "CV=system-change"
if /i "!CAT!"=="CLEAN" set "CV=cleanup"
exit /b 0

rem  :state <ID> <outvar>  ->  "+" выполнено, " " нет, "!" недоступно на этой ОС
:state
set "_S= "
set "_M=!ABSENT: %~1 =!"
if not "!_M!"=="!ABSENT!" set "_S=-"
set "_M=!ALREADY: %~1 =!"
if not "!_M!"=="!ALREADY!" set "_S=="
set "_M=!APPLIED: %~1 =!"
if not "!_M!"=="!APPLIED!" set "_S=+"
set "_M=!SKIPOS: %~1 =!"
if not "!_M!"=="!SKIPOS!" set "_S=x"
set "%~2=!_S!"
exit /b 0

:catline
set "_C=%~2"
set "_T=  %~1"
set "_T=!_T:~-3!"
set "_CNT=!C_%~2!"
set "_APP=!A_%~2!"
if not defined _CNT set "_CNT=0"
if not defined _APP set "_APP=0"
set "_PAD=%~3                                   "
set "_PAD=!_PAD:~0,35!"
echo(  !_T!. !_PAD! готово !_APP! из !_CNT!
exit /b 0

rem  Запуск движка. На время работы возвращаем исходную кодовую страницу,
rem  чтобы reg query читал значения так же, как при обычном запуске.
:run
cls
echo ------------------------------------------------------------------
echo   wintweaks.cmd %~1 !RPFLAG!
echo ------------------------------------------------------------------
chcp %CP_ORIG% >nul <nul
call "%ENGINE%" %~1 !RPFLAG!
set "RC=!ERRORLEVEL!"
chcp 866 >nul <nul
call :inventory
echo ------------------------------------------------------------------
if "!RC!"=="0" echo   Готово.
if not "!RC!"=="0" echo   Завершено с кодом !RC! - подробности в логе, пункт L.
call :pause
exit /b 0

:ask
set "ANS="
echo(
set /p "ANS=  %~1? [Y/N]: "
if defined ANS for /f "delims=" %%X in ("!ANS!") do set "ANS=%%X"
if not defined ANS exit /b 1
if /i "!ANS!"=="Y" exit /b 0
if /i "!ANS!"=="Д" exit /b 0
exit /b 1

:pause
echo(
echo   Нажмите любую клавишу...
pause >nul
exit /b 0

:toggle
if "!%~1!"=="1" (set "%~1=0") else (set "%~1=1")
exit /b 0

rem  Собирает всё, что показывают экраны: версия ОС, счётчики журнала,
rem  строку выполненных ID и строку пунктов, недоступных на этой ОС.
:collect
set "F_DRY= " & if "!M_DRY!"=="1" set "F_DRY=X"
set "F_RP= " & if "!M_RP!"=="1" set "F_RP=X"
set "F_HIDE= " & if "!M_HIDE!"=="1" set "F_HIDE=X"
set "F_DEEP= " & if "!M_DEEP!"=="1" set "F_DEEP=X"
set "RPFLAG=/no-restore-point" & if "!M_RP!"=="1" set "RPFLAG=/restore-point"

set "OS_BUILD=" & set "OS_UBR=" & set "OS_DISPLAY=" & set "OS_EDITION="
for /f "usebackq tokens=1,2,*" %%A in (`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v CurrentBuild 2^>nul ^<nul`) do if /i "%%~B"=="REG_SZ" set "OS_BUILD=%%~C"
for /f "usebackq tokens=1,2,*" %%A in (`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v DisplayVersion 2^>nul ^<nul`) do if /i "%%~B"=="REG_SZ" set "OS_DISPLAY=%%~C"
for /f "usebackq tokens=1,2,*" %%A in (`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v EditionID 2^>nul ^<nul`) do if /i "%%~B"=="REG_SZ" set "OS_EDITION=%%~C"
for /f "usebackq tokens=1,2,*" %%A in (`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v UBR 2^>nul ^<nul`) do if /i "%%~B"=="REG_DWORD" set "OS_UBR=%%~C"
set /a _B=!OS_BUILD! 2>nul
rem  ProductName в реестре у Windows 11 до сих пор равен "Windows 10 Pro" -
rem  Microsoft не стала его менять. Поэтому имя считаем по номеру сборки.
if !_B! GEQ 22000 (set "OS_FAMILY=win11" & set "OS_NUM=11") else (set "OS_FAMILY=win10" & set "OS_NUM=10")
set "OS_ED=!OS_EDITION!"
if /i "!OS_EDITION!"=="Professional" set "OS_ED=Pro"
if /i "!OS_EDITION!"=="Core" set "OS_ED=Home"
if /i "!OS_EDITION!"=="CoreSingleLanguage" set "OS_ED=Home SL"
if /i "!OS_EDITION!"=="ProfessionalWorkstation" set "OS_ED=Pro for Workstations"
set /a _UBRN=!OS_UBR! 2>nul
set "OS_NAME=Windows !OS_NUM! !OS_ED! !OS_DISPLAY! (сборка !OS_BUILD!.!_UBRN!)"

set /a S_OK=0, S_REV=0, S_PEND=0, S_MAN=0
set "APPLIED= "
if exist "%JOURNAL%" (
    for /f "usebackq tokens=2,10 delims=|" %%a in ("%JOURNAL%") do (
        if /i "%%b"=="OK"       (set /a S_OK+=1   & set "APPLIED=!APPLIED!%%a ")
        if /i "%%b"=="REVERTED" set /a S_REV+=1
        if /i "%%b"=="PENDING"  (set /a S_PEND+=1 & set "APPLIED=!APPLIED!%%a ")
        if /i "%%b"=="MANUAL"   set /a S_MAN+=1
    )
)

set "ABSENT= "
set "ALREADY= "
if exist "%APPROOT%\state\inventory.dat" (
    for /f "usebackq tokens=1-4 delims=|" %%A in ("%APPROOT%\state\inventory.dat") do (
        if "%%B"=="0" set "ABSENT=!ABSENT!%%A "
        if "%%C"=="1" set "ALREADY=!ALREADY!%%A "
        set "INFO_%%A=%%D"
    )
)
set "SKIPOS= "
for /f "usebackq eol=# tokens=1,4 delims=|" %%A in ("%TWEAKDEF%") do (
    if /i not "%%B"=="any" if /i not "%%B"=="!OS_FAMILY!" set "SKIPOS=!SKIPOS!%%A "
)

for %%C in (PRIV UI SVC TASK UPD PERF EDGE APPS SYS CLEAN) do (
    set "C_%%C=0"
    set "A_%%C=0"
)
for /f "usebackq eol=# tokens=1,2 delims=|" %%A in ("%DESCR%") do (
    call :state "%%A" ST
    set "_CNT=1"
    if "!ST!"=="-" if not "!M_HIDE!"=="1" set "_CNT=0"
    if "!_CNT!"=="1" set /a C_%%B+=1
    if "!ST!"=="+" set /a A_%%B+=1
    if "!ST!"=="=" set /a A_%%B+=1
)
exit /b 0

rem ============================== перезапуск ==================================
:elevate
cls
echo ==================================================================
echo   wintweaks
echo ==================================================================
echo(
echo   Для выполнения и отката изменений нужны права администратора.
call :ask "Перезапустить меню от имени администратора"
if errorlevel 1 goto quit
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WorkingDirectory '%APPROOT%'" <nul
goto quit

rem  Сканирование ПК: что из каталога вообще есть на этой машине и что уже
rem  находится в нужном состоянии. Результат - state\inventory.dat.
:inventory
echo(
echo   Сканирую систему: службы, приложения, задачи, реестр...
chcp %CP_ORIG% >nul <nul
%PSH% -Action Inventory -Root "%APPROOT%" >nul 2>&1
chcp 866 >nul <nul
exit /b 0

:a_makerp
echo(
echo   Точка восстановления - снимок системных файлов и реестра силами самой
echo   Windows. Для работы отката она НЕ нужна: каждый твик и так пишет свою
echo   резервную копию. Это просто дополнительная страховка.
echo   Windows делает не больше одной точки в сутки.
call :ask "Создать точку восстановления сейчас"
if errorlevel 1 goto main
echo(
echo   Создаю, это может занять до минуты...
chcp %CP_ORIG% >nul <nul
%PSH% -Action RestorePoint -Description "wintweaks" >nul 2>&1
set "RC=!ERRORLEVEL!"
chcp 866 >nul <nul
if "!RC!"=="0" echo   Готово: точка восстановления создана.
if not "!RC!"=="0" echo   Не получилось. Скорее всего защита системы отключена,
if not "!RC!"=="0" echo   либо Windows уже создавала точку в последние 24 часа.
call :pause
goto main

:quit
chcp %CP_ORIG% >nul <nul
endlocal
exit /b 0
