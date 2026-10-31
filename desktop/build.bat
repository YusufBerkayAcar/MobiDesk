@echo off
setlocal enabledelayedexpansion
title MobiDesk Windows Client Compiler (desktop/build.bat)
color 0b

echo ========================================================
echo         MOBIDESK STANDALONE CLIENT BUILD SYSTEM
echo ========================================================
echo.

set "SCRIPT_DIR=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo [ERROR] csc.exe compiler not found in Microsoft.NET Framework!
    exit /b 1
)

set "ICON=%SCRIPT_DIR%app_icon.ico"
if not exist "%ICON%" set "ICON=%SCRIPT_DIR%..\assets\icons\app_icon.ico"
if not exist "%ICON%" set "ICON=%SCRIPT_DIR%..\assets\app_icon.ico"
if not exist "%ICON%" set "ICON=%SCRIPT_DIR%..\app_icon.ico"

set "MANIFEST=%SCRIPT_DIR%app.manifest"
if not exist "%MANIFEST%" set "MANIFEST=%SCRIPT_DIR%..\app.manifest"

set "SOURCE=%SCRIPT_DIR%ScreenMonitorGUI.cs"
set "OUT=%SCRIPT_DIR%MobiDesk.exe"

echo [*] Compiler: %CSC%
echo [*] Source:   %SOURCE%
echo [*] Output:   %OUT%
echo [*] Icon:     %ICON%
echo [*] Manifest: %MANIFEST%
echo.

set "ICON_ARG="
if exist "%ICON%" set "ICON_ARG=/win32icon:"%ICON%""

set "MANIFEST_ARG="
if exist "%MANIFEST%" set "MANIFEST_ARG=/win32manifest:"%MANIFEST%""

"%CSC%" /nologo /target:winexe /unsafe %ICON_ARG% %MANIFEST_ARG% /out:"%OUT%" "%SOURCE%"

if %errorlevel% equ 0 (
    echo.
    echo ========================================================
    echo  [PASS] MobiDesk.exe compiled successfully!
    echo ========================================================
    if "%~1"=="--test" (
        echo [*] Running MobiDesk internal test suite...
        "%OUT%" --test
    )
    exit /b 0
) else (
    echo.
    echo ========================================================
    echo  [FAIL] Compilation failed with ExitCode: %errorlevel%
    echo ========================================================
    exit /b %errorlevel%
)
