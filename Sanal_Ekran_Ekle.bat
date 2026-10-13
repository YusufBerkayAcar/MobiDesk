@echo off
title Harici Sanal Monitor Kurucu (MobiDesk VDD)
color 0b

:: Yonetici yetkisi kontrolu
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -NoProfile -WindowStyle Hidden -Command "Start-Process '%~f0' -ArgumentList '%*' -Verb RunAs -WindowStyle Hidden"
    exit /b
)

set SCRIPT_DIR=%~dp0
set VDD_SRC=%SCRIPT_DIR%vdd\VirtualDisplayDriver
set DEV_INSTALLER=%SCRIPT_DIR%usbmmidd\usbmmidd_v2\deviceinstaller64.exe

if not "%~1"=="--silent" (
    echo =======================================================
    echo    GERCEK HARICI MONITOR VE OLCEKLENDIRME SURUCUSU
    echo =======================================================
    echo.
    echo [1/5] Varsa eski uyumsuz sanal ekranlar kapatiliyor...
)

if exist "%DEV_INSTALLER%" (
    "%DEV_INSTALLER%" enableidd 0 >nul 2>&1
)

if not "%~1"=="--silent" echo [2/5] Surucu dosyalari hazirlaniyor (C:\VirtualDisplayDriver)...
if not exist "C:\VirtualDisplayDriver" mkdir "C:\VirtualDisplayDriver"
if not exist "C:\IddSampleDriver" mkdir "C:\IddSampleDriver"

copy /y "%VDD_SRC%\*" "C:\VirtualDisplayDriver\" >nul
copy /y "%VDD_SRC%\vdd_settings.xml" "C:\IddSampleDriver\" >nul

if not "%~1"=="--silent" echo [3/5] Surucu Guvenlik Sertifikasi yukleniyor...
certutil -addstore -f root "C:\VirtualDisplayDriver\Virtual_Display_Driver.cer" >nul 2>&1
certutil -addstore -f TrustedPublisher "C:\VirtualDisplayDriver\Virtual_Display_Driver.cer" >nul 2>&1

if not "%~1"=="--silent" echo [4/5] Harici Ekran Surucusu Windows'a tanitiliyor...
pnputil /add-driver "C:\VirtualDisplayDriver\MttVDD.inf" /install >nul 2>&1

if not "%~1"=="--silent" echo [5/5] Sanal Harici Monitor olusturuluyor...
"%DEV_INSTALLER%" install "C:\VirtualDisplayDriver\MttVDD.inf" Root\MttVDD >nul 2>&1

if not "%~1"=="--silent" (
    echo.
    echo =======================================================
    echo  [BASARILI] Harici Sanal Monitor Sistemi Kuruldu!
    echo =======================================================
    echo  * Sanal ekran surucusu basariyla aktif edildi.
    echo  * MobiDesk baglanan cihaziniza gore cozunurlugu
    echo    otomatik olarak optimize edecektir.
    echo =======================================================
    echo.
    timeout /t 3
)
