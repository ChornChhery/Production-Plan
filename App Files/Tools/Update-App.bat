@echo off
setlocal enabledelayedexpansion
title Teeth - update the app after a new build

rem ============================================================
rem  DEVELOPER USE ONLY - not needed on the shop-floor PCs.
rem
rem  After you change the C# code:
rem    1. Open  App Files\Source\Teeth.sln  in Visual Studio
rem    2. Configuration = Release, Platform = x64
rem       -> Build -> Rebuild Solution
rem    3. CLOSE Teeth.exe if it is running
rem    4. Double-click this file
rem
rem  It puts the new Teeth.exe in the main Teeth folder and the
rem  new .dll files in App Files\lib, keeping the clean layout.
rem  Your database and Log Data are never touched.
rem ============================================================

for %%I in ("%~dp0..") do set "AF=%%~fI\"
for %%I in ("%~dp0..\..") do set "ROOT=%%~fI\"
set "PROJECT_DIR=%AF%Source\Teeth"
set "BUILD_DIR="

for %%D in (
    "%PROJECT_DIR%\bin\x64\Release\net48"
    "%PROJECT_DIR%\bin\x64\Release"
    "%PROJECT_DIR%\bin\Release\net48"
    "%PROJECT_DIR%\bin\Release"
) do (
    if exist "%%~D\Teeth.exe" (
        set "BUILD_DIR=%%~D"
        goto :found
    )
)

echo.
echo  ERROR: No Release build found.
echo  In Visual Studio choose Release + x64, then Build ^> Rebuild Solution,
echo  and run this file again.
echo.
pause
exit /b 1

:found
echo.
echo  Using build output:
echo    %BUILD_DIR%
echo.

copy /y "%BUILD_DIR%\Teeth.exe" "%ROOT%Teeth.exe" >nul
if errorlevel 1 (
    echo  ERROR: Could not replace Teeth.exe. Is it still running? Close it and try again.
    echo.
    pause
    exit /b 1
)

if not exist "%AF%lib" mkdir "%AF%lib"
robocopy "%BUILD_DIR%" "%AF%lib" *.dll >nul
if %ERRORLEVEL% GEQ 8 goto :copyfail
if exist "%BUILD_DIR%\x64" robocopy "%BUILD_DIR%\x64" "%AF%lib\x64" /E >nul
if %ERRORLEVEL% GEQ 8 goto :copyfail
if exist "%BUILD_DIR%\x86" robocopy "%BUILD_DIR%\x86" "%AF%lib\x86" /E >nul
if %ERRORLEVEL% GEQ 8 goto :copyfail

echo  Done.
echo    Teeth.exe          -^> %ROOT%
echo    .dll files         -^> %AF%lib
echo.
pause
endlocal
exit /b 0

:copyfail
echo  ERROR: Copying the .dll files failed. Close Teeth.exe and try again.
echo.
pause
exit /b 1
