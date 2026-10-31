@echo off
setlocal enabledelayedexpansion
title MobiDesk Sanal Ses Karti Kurucu (MobiDesk Audio)

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
set "LOG_FILE=%TARGET_DIR%\install.log"

if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%" >nul 2>&1
echo [%date% %time%] MobiDesk Sanal Ses Kurulumu baslatildi > "%LOG_FILE%"

if not "%~1"=="--silent" (
    echo =======================================================
    echo         MOBIDESK SANAL SES KARTI KURULUMU
    echo =======================================================
    echo.
)

if not "%~1"=="--silent" echo [1/6] Surucu dosyalari hazirlaniyor (%TARGET_DIR%)...
if exist "%VBCABLE_DIR%" (
    xcopy /y /e /q "%VBCABLE_DIR%\*" "%TARGET_DIR%\" >> "%LOG_FILE%" 2>&1
)
if exist "%VIRTUALAUDIODRIVER_DIR%" (
    xcopy /y /e /q "%VIRTUALAUDIODRIVER_DIR%\*" "%TARGET_DIR%\" >> "%LOG_FILE%" 2>&1
)

if not "%~1"=="--silent" echo [2/6] Guvenlik sertifikasi sisteme yukleniyor...
if exist "%TARGET_DIR%\vbcable.cer" (
    certutil -addstore -f TrustedPublisher "%TARGET_DIR%\vbcable.cer" >> "%LOG_FILE%" 2>&1
    certutil -addstore -f root "%TARGET_DIR%\vbcable.cer" >> "%LOG_FILE%" 2>&1
)
if exist "%TARGET_DIR%\MobiDeskAudio.cer" (
    certutil -addstore -f TrustedPublisher "%TARGET_DIR%\MobiDeskAudio.cer" >> "%LOG_FILE%" 2>&1
    certutil -addstore -f root "%TARGET_DIR%\MobiDeskAudio.cer" >> "%LOG_FILE%" 2>&1
)

if not "%~1"=="--silent" echo [3/6] Surucu paketi Windows Surucu Deposuna ekleniyor...
if exist "%TARGET_DIR%\MobiDeskAudio.inf" (
    pnputil /add-driver "%TARGET_DIR%\MobiDeskAudio.inf" /install >> "%LOG_FILE%" 2>&1
)
if exist "%TARGET_DIR%\vbMmeCable64_win10.inf" (
    pnputil /add-driver "%TARGET_DIR%\vbMmeCable64_win10.inf" /install >> "%LOG_FILE%" 2>&1
) else if exist "%TARGET_DIR%\vbMmeCable64_win7.inf" (
    pnputil /add-driver "%TARGET_DIR%\vbMmeCable64_win7.inf" /install >> "%LOG_FILE%" 2>&1
)

if not "%~1"=="--silent" echo [4/6] Sanal Ses Donanimi kuruluyor ve baglaniyor...
set "INF_PATH=%TARGET_DIR%\vbMmeCable64_win10.inf"
if not exist "%INF_PATH%" set "INF_PATH=%TARGET_DIR%\vbMmeCable64_win7.inf"

if exist "%TARGET_DIR%\DevInstaller.exe" (
    if exist "%TARGET_DIR%\MobiDeskAudio.inf" (
        "%TARGET_DIR%\DevInstaller.exe" install "%TARGET_DIR%\MobiDeskAudio.inf" Root\MobiDeskAudio >> "%LOG_FILE%" 2>&1
    )
    "%TARGET_DIR%\DevInstaller.exe" install-auto "%INF_PATH%" "%TARGET_DIR%\VBCABLE_Setup_x64.exe" >> "%LOG_FILE%" 2>&1
) else (
    if exist "%TARGET_DIR%\VBCABLE_Setup_x64.exe" (
        start "" /wait "%TARGET_DIR%\VBCABLE_Setup_x64.exe"
    )
)

if not "%~1"=="--silent" echo [5/6] Ses aygiti etiketleniyor ve 16ch filtreleniyor...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$keys = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render' -ErrorAction SilentlyContinue; foreach ($k in $keys) { $p = $k.PSPath + '\Properties'; $desc = (Get-ItemProperty $p -ErrorAction SilentlyContinue).'{a45c254e-df1c-4efd-8020-67d146a850e0},2'; if ($desc -like '*CABLE Input*') { Set-ItemProperty -Path $p -Name '{a45c254e-df1c-4efd-8020-67d146a850e0},14' -Value 'MobiDesk Audio' -Type String -ErrorAction SilentlyContinue } elseif ($desc -like '*16ch*') { Set-ItemProperty -Path $k.PSPath -Name 'DeviceState' -Value 2 -Type DWord -ErrorAction SilentlyContinue } }" >> "%LOG_FILE%" 2>&1

if not "%~1"=="--silent" echo [6/6] Windows Ses Hizmeti yenileniyor...
net stop audiosrv /y >> "%LOG_FILE%" 2>&1
net stop AudioEndpointBuilder /y >> "%LOG_FILE%" 2>&1
timeout /t 1 /nobreak >nul
net start AudioEndpointBuilder >> "%LOG_FILE%" 2>&1
net start audiosrv >> "%LOG_FILE%" 2>&1
timeout /t 2 /nobreak >nul

:: Cihazin aktifligini 15 saniye boyunca dogrula
set "INSTALLED=0"
for /L %%i in (1,1,15) do (
    if exist "%TARGET_DIR%\DevInstaller.exe" (
        "%TARGET_DIR%\DevInstaller.exe" check VBAudioVACWDM >nul 2>&1
        if !errorlevel! equ 0 (
            set "INSTALLED=1"
            goto DEV_READY
        )
    ) else (
        powershell -NoProfile -Command "$d = Get-PnpDevice -FriendlyName '*CABLE*' -ErrorAction SilentlyContinue; if ($d -and $d.Status -eq 'OK') { exit 0 } else { exit 1 }"
        if !errorlevel! equ 0 (
            set "INSTALLED=1"
            goto DEV_READY
        )
    )
    timeout /t 1 /nobreak >nul
)

:DEV_READY
if "%INSTALLED%"=="1" (
    if not "%~1"=="--silent" (
        echo.
        echo =======================================================
        echo  [BASARILI] MobiDesk Sanal Ses Karti Basariyla Kuruldu!
        echo =======================================================
        echo  * Windows Ses Ayarlarinda 'MobiDesk Audio' (veya CABLE Input) gorunuyor.
        echo =======================================================
        echo.
        timeout /t 3 >nul
    )
    exit /b 0
) else (
    if not "%~1"=="--silent" (
        echo.
        echo =======================================================
        echo  [UYARI] Otomatik baglanti henuz tamamlanamadi.
        echo  Resmi kurulum penceresi aciliyor, lutfen 'Install Driver'a tiklayin...
        echo =======================================================
        if exist "%TARGET_DIR%\VBCABLE_Setup_x64.exe" (
            start "" "%TARGET_DIR%\VBCABLE_Setup_x64.exe"
        )
        timeout /t 5 >nul
    )
    exit /b 1
)
