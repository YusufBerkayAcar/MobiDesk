@echo off
title Harici Sanal Monitor Kaldirici (MobiDesk VDD)
color 0c

:: Yonetici yetkisi kontrolu
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -NoProfile -WindowStyle Hidden -Command "Start-Process '%~f0' -ArgumentList '%*' -Verb RunAs -WindowStyle Hidden" 2>nul
    exit /b 0
)

set SCRIPT_DIR=%~dp0

if exist "%SCRIPT_DIR%usbmmidd\usbmmidd_v2\deviceinstaller64.exe" (
    set "DEV_INSTALLER=%SCRIPT_DIR%usbmmidd\usbmmidd_v2\deviceinstaller64.exe"
) else if exist "%SCRIPT_DIR%..\..\usbmmidd\usbmmidd_v2\deviceinstaller64.exe" (
    set "DEV_INSTALLER=%SCRIPT_DIR%..\..\usbmmidd\usbmmidd_v2\deviceinstaller64.exe"
) else if exist "%SCRIPT_DIR%drivers\display\usbmmidd\usbmmidd_v2\deviceinstaller64.exe" (
    set "DEV_INSTALLER=%SCRIPT_DIR%drivers\display\usbmmidd\usbmmidd_v2\deviceinstaller64.exe"
) else (
    set "DEV_INSTALLER=%SCRIPT_DIR%usbmmidd\usbmmidd_v2\deviceinstaller64.exe"
)

if not "%~1"=="--silent" (
    echo =======================================================
    echo          Harici Sanal Monitor Devre Disi Birakiliyor
    echo =======================================================
    echo.
)

if exist "%DEV_INSTALLER%" (
    "%DEV_INSTALLER%" remove Root\MttVDD >nul 2>&1
    "%DEV_INSTALLER%" enableidd 0 >nul 2>&1
)

powershell -NoProfile -Command "Get-PnpDevice -FriendlyName '*Virtual Display Driver*' -ErrorAction SilentlyContinue | Disable-PnpDevice -Confirm:$false" >nul 2>&1

if not "%~1"=="--silent" (
    echo [OK] Harici sanal ekran basariyla kaldirildi.
    timeout /t 3
)
