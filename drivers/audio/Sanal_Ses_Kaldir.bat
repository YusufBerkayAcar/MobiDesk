@echo off
setlocal enabledelayedexpansion
title MobiDesk Sanal Ses Karti Kaldirici (MobiDesk Audio)

:: 1. Yonetici yetkisi kontrolu
net session >nul 2>&1
if %errorLevel% neq 0 (
    if not "%~1"=="--silent" echo [!] Yonetici haklari ile baslatiliyor...
    powershell -NoProfile -WindowStyle Hidden -Command "Start-Process '%~f0' -ArgumentList '%*' -Verb RunAs -WindowStyle Hidden" 2>nul
    exit /b 0
)

set "SCRIPT_DIR=%~dp0"
if exist "%SCRIPT_DIR%vda\VBCable" (
    set "VBCABLE_DIR=%SCRIPT_DIR%vda\VBCable"
) else if exist "%SCRIPT_DIR%..\..\vda\VBCable" (
    set "VBCABLE_DIR=%SCRIPT_DIR%..\..\vda\VBCable"
) else if exist "%SCRIPT_DIR%drivers\audio\vda\VBCable" (
    set "VBCABLE_DIR=%SCRIPT_DIR%drivers\audio\vda\VBCable"
) else (
    set "VBCABLE_DIR=%SCRIPT_DIR%vda\VBCable"
)

if exist "%SCRIPT_DIR%vda\VirtualAudioDriver" (
    set "VIRTUALAUDIODRIVER_DIR=%SCRIPT_DIR%vda\VirtualAudioDriver"
) else if exist "%SCRIPT_DIR%..\..\vda\VirtualAudioDriver" (
    set "VIRTUALAUDIODRIVER_DIR=%SCRIPT_DIR%..\..\vda\VirtualAudioDriver"
) else if exist "%SCRIPT_DIR%drivers\audio\vda\VirtualAudioDriver" (
    set "VIRTUALAUDIODRIVER_DIR=%SCRIPT_DIR%drivers\audio\vda\VirtualAudioDriver"
) else (
    set "VIRTUALAUDIODRIVER_DIR=%SCRIPT_DIR%vda\VirtualAudioDriver"
)
set "TARGET_DIR=C:\VirtualAudioDriver"
set "LOG_FILE=%TARGET_DIR%\uninstall.log"

if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%" >nul 2>&1
echo [%date% %time%] MobiDesk Sanal Ses Kaldirma baslatildi > "%LOG_FILE%"

if not "%~1"=="--silent" (
    echo =======================================================
    echo         MOBIDESK SANAL SES KARTI KALDIRILIYOR
    echo =======================================================
    echo.
    echo [1/4] Sanal Ses Aygiti sistemden kaldiriliyor...
)

:: Varsa arka planda kalan setup sureclerini kapat (Kullaniciya gereksiz arayuz acilmasini onler)
taskkill /F /IM VBCABLE_Setup_x64.exe >nul 2>&1
taskkill /F /IM VBCABLE_Setup.exe >nul 2>&1

:: DevInstaller ile cihazi dogrudan ve sessizce kaldir (SetupAPI)
if exist "%TARGET_DIR%\DevInstaller.exe" (
    "%TARGET_DIR%\DevInstaller.exe" remove Root\MobiDeskAudio >> "%LOG_FILE%" 2>&1
    "%TARGET_DIR%\DevInstaller.exe" remove VBAudioVACWDM >> "%LOG_FILE%" 2>&1
) else if exist "%VBCABLE_DIR%\DevInstaller.exe" (
    "%VBCABLE_DIR%\DevInstaller.exe" remove Root\MobiDeskAudio >> "%LOG_FILE%" 2>&1
    "%VBCABLE_DIR%\DevInstaller.exe" remove VBAudioVACWDM >> "%LOG_FILE%" 2>&1
)

:: PnP Aygitini devre disi birak
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-PnpDevice -FriendlyName '*CABLE*' -ErrorAction SilentlyContinue | Disable-PnpDevice -Confirm:$false; Get-PnpDevice -FriendlyName '*MobiDesk*' -ErrorAction SilentlyContinue | Disable-PnpDevice -Confirm:$false" >> "%LOG_FILE%" 2>&1

if not "%~1"=="--silent" echo [2/4] Windows Ses Hizmeti yenileniyor...
net stop audiosrv /y >> "%LOG_FILE%" 2>&1
net stop AudioEndpointBuilder /y >> "%LOG_FILE%" 2>&1
timeout /t 1 /nobreak >nul
net start AudioEndpointBuilder >> "%LOG_FILE%" 2>&1
net start audiosrv >> "%LOG_FILE%" 2>&1
timeout /t 2 /nobreak >nul

if not "%~1"=="--silent" echo [3/4] Surucu paketleri temizleniyor...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$drvs = Get-WindowsDriver -Online -ErrorAction SilentlyContinue | Where-Object { $_.OriginalFileName -like '*vbMmeCable*' -or $_.OriginalFileName -like '*MobiDeskAudio*' }; foreach ($d in $drvs) { & pnputil /delete-driver $d.Driver /uninstall /force }" >> "%LOG_FILE%" 2>&1

:: Kayit Defterindeki MobiDesk Audio veya CABLE etiketlerini temizle
powershell -NoProfile -ExecutionPolicy Bypass -Command "$keys = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render' -ErrorAction SilentlyContinue; foreach ($k in $keys) { $p = $k.PSPath + '\Properties'; $desc = (Get-ItemProperty $p -ErrorAction SilentlyContinue).'{a45c254e-df1c-4efd-8020-67d146a850e0},2'; if ($desc -like '*CABLE*' -or $desc -like '*MobiDesk*') { Remove-ItemProperty -Path $p -Name '{a45c254e-df1c-4efd-8020-67d146a850e0},14' -ErrorAction SilentlyContinue } }" >> "%LOG_FILE%" 2>&1

if not "%~1"=="--silent" echo [4/4] Gecici surucu dosyalari temizleniyor...
if exist "%TARGET_DIR%" (
    rmdir /s /q "%TARGET_DIR%" >nul 2>&1
)

if not "%~1"=="--silent" (
    echo.
    echo =======================================================
    echo  [OK] MobiDesk sanal ses karti basariyla sistemden kaldirildi.
    echo =======================================================
    timeout /t 2 >nul
)
exit /b 0
