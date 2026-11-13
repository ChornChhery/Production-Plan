@echo off
setlocal enabledelayedexpansion
title Teeth - PC check

rem ============================================================
rem  Teeth - run this ONCE on a new / offline PC (double-click).
rem  It only LOOKS at the PC - it changes nothing.
rem  Checks: 64-bit Windows, .NET Framework 4.8, that all app
rem  files are present, and that the folder is writable (the app
rem  saves its database and Log Data inside the Teeth folder).
rem ============================================================

for %%I in ("%~dp0..\..") do set "ROOT=%%~fI\"
set "LIB=%ROOT%App Files\lib\"
set "PROBLEMS=0"

echo.
echo  ==== Teeth - checking this PC ====
echo.

rem --- 1. 64-bit Windows ---
set "X64=0"
if /i "%PROCESSOR_ARCHITECTURE%"=="AMD64" set "X64=1"
if /i "%PROCESSOR_ARCHITEW6432%"=="AMD64" set "X64=1"
if "%X64%"=="1" (
    echo   [OK]       64-bit Windows
) else (
    echo   [PROBLEM]  This is 32-bit Windows. Teeth.exe is built for 64-bit.
    set "PROBLEMS=1"
)

rem --- 2. .NET Framework 4.8 (registry value Release >= 528040) ---
set "REL=0"
for /f "tokens=3" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul') do set /a REL=%%A
if !REL! GEQ 528040 (
    echo   [OK]       .NET Framework 4.8 or newer is installed
) else (
    echo   [PROBLEM]  .NET Framework 4.8 was NOT found on this PC.
    echo              Install it once from the offline installer
    echo              ndp48-x86-x64-allos-enu.exe ^(Microsoft download, about 115 MB^).
    set "PROBLEMS=1"
)

rem --- 3. app files present ---
if exist "%ROOT%Teeth.exe" (
    echo   [OK]       Teeth.exe
) else (
    echo   [MISSING]  Teeth.exe
    set "PROBLEMS=1"
)
for %%F in (System.Data.SQLite.dll ClosedXML.dll DocumentFormat.OpenXml.dll) do (
    if exist "%LIB%%%F" (
        echo   [OK]       App Files\lib\%%F
    ) else (
        echo   [MISSING]  App Files\lib\%%F  ^(copy the WHOLE Teeth folder, not just Teeth.exe^)
        set "PROBLEMS=1"
    )
)
if exist "%LIB%x64\SQLite.Interop.dll" (
    echo   [OK]       App Files\lib\x64\SQLite.Interop.dll
) else (
    echo   [MISSING]  App Files\lib\x64\SQLite.Interop.dll
    set "PROBLEMS=1"
)

rem --- 4. folder is writable ---
> "%ROOT%__write_test.tmp" echo test 2>nul
if exist "%ROOT%__write_test.tmp" (
    del "%ROOT%__write_test.tmp" >nul 2>&1
    echo   [OK]       The Teeth folder is writable
) else (
    echo   [PROBLEM]  The Teeth folder is read-only or protected ^(e.g. Program Files^).
    echo              Move the Teeth folder to the Desktop or C:\Teeth and try again.
    set "PROBLEMS=1"
)

echo.
if "%PROBLEMS%"=="0" (
    echo  RESULT: This PC is ready. Double-click Teeth.exe to start.
) else (
    echo  RESULT: Fix the [PROBLEM] / [MISSING] lines above, then run this check again.
)
echo.
pause
endlocal
