@echo off
setlocal enabledelayedexpansion
title MobiDesk Studio Kurulum Sihirbazi (Setup Wizard)
color 0b

:: 1. Yonetici Haklari Kontrolu (Sanal suruculer icin gereklidir)
net session >nul 2>&1
if %errorLevel% neq 0 (
    if "%~1"=="--no-uac" (
        goto :MAIN_INSTALL
    )
    echo =======================================================
    echo   MOBIDESK STUDIO - YONETICI HAKLARI GEREKIYOR
    echo =======================================================
    echo.
    echo [i] Surucu yuklemeleri ve sistem kayitlari icin yonetici onayi isteniyor...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd.exe -ArgumentList '/c \"\"%~f0\"\" %*' -Verb RunAs"
    exit /b 0
)

:MAIN_INSTALL

set "SCRIPT_DIR=%~dp0"
set "SRC_DIR=%SCRIPT_DIR%"
set "INSTALL_DIR=%LOCALAPPDATA%\Programs\MobiDesk"

:: Eger desktop/MobiDesk.exe yoksa once derle
if not exist "%SRC_DIR%desktop\MobiDesk.exe" (
    if exist "%SRC_DIR%desktop\build.bat" (
        echo [*] MobiDesk.exe bulunamadi, otomatik derleniyor...
        call "%SRC_DIR%desktop\build.bat"
    )
)

cls
echo =======================================================
echo          MOBIDESK STUDIO KURULUM SIHIRBAZI
echo =======================================================
echo  Hedef Dizin : %INSTALL_DIR%
echo  Surum       : 2.0.0 (Beta)
echo =======================================================
echo.

:: 1. Calisan surecleri guvenle sonlandir
echo [1/5] Calisan MobiDesk surecleri kontrol ediliyor...
taskkill /F /IM MobiDesk.exe >nul 2>&1

:: 2. Hedef dizini olustur ve dosyalari kopyala
echo [2/5] Program dosyalari yukleniyor...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%" >nul 2>&1
if not exist "%INSTALL_DIR%\assets" mkdir "%INSTALL_DIR%\assets" >nul 2>&1
if not exist "%INSTALL_DIR%\drivers" mkdir "%INSTALL_DIR%\drivers" >nul 2>&1

copy /y "%SRC_DIR%desktop\MobiDesk.exe" "%INSTALL_DIR%\MobiDesk.exe" >nul
if exist "%SRC_DIR%desktop\app.manifest" copy /y "%SRC_DIR%desktop\app.manifest" "%INSTALL_DIR%\app.manifest" >nul
if exist "%SRC_DIR%desktop\create_shortcuts.ps1" copy /y "%SRC_DIR%desktop\create_shortcuts.ps1" "%INSTALL_DIR%\create_shortcuts.ps1" >nul
if exist "%SRC_DIR%desktop\remove_shortcuts.ps1" copy /y "%SRC_DIR%desktop\remove_shortcuts.ps1" "%INSTALL_DIR%\remove_shortcuts.ps1" >nul

if exist "%SRC_DIR%assets" (
    xcopy /y /e /q "%SRC_DIR%assets\*" "%INSTALL_DIR%\assets\" >nul 2>&1
)
if exist "%SRC_DIR%drivers" (
    xcopy /y /e /q "%SRC_DIR%drivers\*" "%INSTALL_DIR%\drivers\" >nul 2>&1
)

:: 3. Kaldirici (Uninstall.bat) dosyasini olustur
(
echo @echo off
echo setlocal enabledelayedexpansion
echo title MobiDesk Studio Kaldirici ^(Uninstaller^)
echo color 0c
echo net session ^>nul 2^>^&1
echo if %%errorLevel%% neq 0 ^(
echo     if "%%~1"=="--no-uac" goto :MAIN_UNINSTALL
echo     powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd.exe -ArgumentList '/c \"\"%%~f0\"\" %%*' -Verb RunAs"
echo     exit /b 0
echo ^)
echo :MAIN_UNINSTALL
echo echo =======================================================
echo echo         MOBIDESK STUDIO SISTEMDEN KALDIRILIYOR
echo echo =======================================================
echo echo.
echo echo [1/5] Surecler sonlandiriliyor...
echo taskkill /F /IM MobiDesk.exe ^>nul 2^>^&1
echo echo [2/5] Kisayollar temizleniyor...
echo if exist "%%~dp0remove_shortcuts.ps1" ^(
echo     powershell -NoProfile -ExecutionPolicy Bypass -File "%%~dp0remove_shortcuts.ps1" ^>nul 2^>^&1
echo ^) else ^(
echo     if exist "%%USERPROFILE%%\Desktop\MobiDesk Studio.lnk" del /f /q "%%USERPROFILE%%\Desktop\MobiDesk Studio.lnk" ^>nul 2^>^&1
echo     if exist "%%APPDATA%%\Microsoft\Windows\Start Menu\Programs\MobiDesk Studio.lnk" del /f /q "%%APPDATA%%\Microsoft\Windows\Start Menu\Programs\MobiDesk Studio.lnk" ^>nul 2^>^&1
echo ^)
echo echo [3/5] Windows kayitlari temizleniyor...
echo reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "MobiDesk" /f ^>nul 2^>^&1
echo reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MobiDesk" /f ^>nul 2^>^&1
echo echo [4/5] Sanal suruculer kaldiriliyor...
echo if exist "%%~dp0drivers\display\Sanal_Ekran_Kaldir.bat" call "%%~dp0drivers\display\Sanal_Ekran_Kaldir.bat" --silent ^>nul 2^>^&1
echo if exist "%%~dp0drivers\audio\Sanal_Ses_Kaldir.bat" call "%%~dp0drivers\audio\Sanal_Ses_Kaldir.bat" --silent ^>nul 2^>^&1
echo echo [5/5] Kurulum dizini temizleniyor...
echo set "TDIR=%%~dp0"
echo cd \
echo start "" cmd /c "timeout /t 1 /nobreak >nul & rmdir /s /q \"!TDIR!\""
echo echo.
echo echo =======================================================
echo echo  [TAMAMLANDI] MobiDesk Studio basariyla kaldirildi.
echo echo =======================================================
echo timeout /t 3 ^>nul
) > "%INSTALL_DIR%\Uninstall.bat"

:: 4. Masaustu ve Baslat Menusu Kisayollarini Olustur
echo [3/5] Masaustu ve Baslat Menusu simgeleri olusturuluyor...
if exist "%INSTALL_DIR%\create_shortcuts.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_DIR%\create_shortcuts.ps1" -InstallDir "%INSTALL_DIR%" >nul 2>&1
)

:: 5. Windows Program Ekle / Kaldir (Ayarlar > Uygulamalar) Kaydini Olustur
echo [4/5] Windows 'Yuklu Uygulamalar' kaydi olusturuluyor...
set "UNINSTALL_REG=HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MobiDesk"
reg add "%UNINSTALL_REG%" /v "DisplayName" /t REG_SZ /d "MobiDesk Studio" /f >nul
reg add "%UNINSTALL_REG%" /v "DisplayVersion" /t REG_SZ /d "2.0.0" /f >nul
reg add "%UNINSTALL_REG%" /v "Publisher" /t REG_SZ /d "MobiDesk Team" /f >nul
reg add "%UNINSTALL_REG%" /v "DisplayIcon" /t REG_SZ /d "%INSTALL_DIR%\assets\icons\app_icon.ico" /f >nul
reg add "%UNINSTALL_REG%" /v "InstallLocation" /t REG_SZ /d "%INSTALL_DIR%" /f >nul
reg add "%UNINSTALL_REG%" /v "UninstallString" /t REG_SZ /d "\"%INSTALL_DIR%\Uninstall.bat\"" /f >nul
reg add "%UNINSTALL_REG%" /v "NoModify" /t REG_DWORD /d 1 /f >nul
reg add "%UNINSTALL_REG%" /v "NoRepair" /t REG_DWORD /d 1 /f >nul
reg add "%UNINSTALL_REG%" /v "EstimatedSize" /t REG_DWORD /d 35000 /f >nul

:: 6. Sanal Ekran ve Sanal Ses Suruculerini Sisteme Tanit
echo [5/5] Sanal Ekran (VDD) ve Sanal Ses (MobiDesk Audio) suruculeri kuruluyor...
if exist "%INSTALL_DIR%\drivers\display\Sanal_Ekran_Ekle.bat" (
    call "%INSTALL_DIR%\drivers\display\Sanal_Ekran_Ekle.bat" --silent
)
if exist "%INSTALL_DIR%\drivers\audio\Sanal_Ses_Ekle.bat" (
    call "%INSTALL_DIR%\drivers\audio\Sanal_Ses_Ekle.bat" --silent
)

echo.
echo =======================================================
echo     [BASARILI] MOBIDESK STUDIO KURULUMU TAMAMLANDI!
echo =======================================================
echo.
echo  * Masaustu Simgesi  : Olusturuldu (Masaustunuzde gorunur)
echo  * Baslat Menusu     : Eklendi (Windows Arama'da 'MobiDesk' yazinca cikar)
echo  * Program Ekle/Kaldir: Windows Ayarlar / Denetim Masasi'na kaydedildi
echo  * Sanal Suruculer   : Sanal Ekran ve Ses suruculeri sisteme tanitildi
echo.
if "%~1"=="--silent" exit /b 0
if "%~2"=="--silent" exit /b 0
if "%~3"=="--silent" exit /b 0

set /p START_NOW="MobiDesk Studio simdi baslatilsin mi? (E/H) [Varsayilan: E]: "
if /i "!START_NOW!"=="H" (
    echo Kurulum sihirbazi kapatiliyor...
    timeout /t 2 >nul
    exit /b 0
)

start "" "%INSTALL_DIR%\MobiDesk.exe"
exit /b 0
