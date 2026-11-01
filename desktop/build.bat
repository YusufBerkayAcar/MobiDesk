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

set "OUT=%SCRIPT_DIR%MobiDesk.exe"

echo [*] Compiler: %CSC%
echo [*] Sources:  %SCRIPT_DIR%*.cs (Recursive)
echo [*] Output:   %OUT%
echo [*] Icon:     %ICON%
echo [*] Manifest: %MANIFEST%
echo.

set "ICON_ARG="
if exist "%ICON%" set "ICON_ARG=/win32icon:"%ICON%""

set "MANIFEST_ARG="
if exist "%MANIFEST%" set "MANIFEST_ARG=/win32manifest:"%MANIFEST%""

:: Clean MSBuild intermediate/output directories to avoid duplicate CS0579 attribute collisions
if exist "%SCRIPT_DIR%obj" rd /s /q "%SCRIPT_DIR%obj" >nul 2>&1
if exist "%SCRIPT_DIR%bin" rd /s /q "%SCRIPT_DIR%bin" >nul 2>&1

:: Compile all modular source files recursively (/recurse must come after all flags)
"%CSC%" /nologo /target:winexe /unsafe %ICON_ARG% %MANIFEST_ARG% /out:"%OUT%" /recurse:"%SCRIPT_DIR%*.cs"

if %errorlevel% equ 0 (
    echo.
    echo ========================================================
    echo  [PASS] MobiDesk.exe compiled successfully!
    echo ========================================================
    if "%~1"=="--test" (
        echo [*] Running MobiDesk internal test suite...
        "%OUT%" --test
        if errorlevel 1 (
            echo.
            echo ========================================================
            echo  [FAIL] MobiDesk internal test suite failed with ExitCode: !errorlevel!
            echo ========================================================
            exit /b !errorlevel!
        )
    )
    exit /b 0
) else (
    echo.
    echo ========================================================
    echo  [FAIL] Compilation failed with ExitCode: %errorlevel%
    echo ========================================================
    exit /b %errorlevel%
)
