@echo off
setlocal
set "TARGET_EXE=%~dp0desktop\MobiDesk.exe"
if not exist "%TARGET_EXE%" set "TARGET_EXE=%~dp0MobiDesk.exe"

if not exist "%TARGET_EXE%" (
    echo [ERROR] MobiDesk.exe not found! Please run desktop\build.bat first.
    exit /b 1
)

if "%~1"=="--test" (
    "%TARGET_EXE%" %*
    exit /b %errorlevel%
)
if "%~1"=="--tests" (
    "%TARGET_EXE%" %*
    exit /b %errorlevel%
)
if "%~1"=="--run-tests" (
    "%TARGET_EXE%" %*
    exit /b %errorlevel%
)

start "" "%TARGET_EXE%" %*
exit /b 0
