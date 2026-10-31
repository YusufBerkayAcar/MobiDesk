@echo off
setlocal
set "UNINSTALLER=%LOCALAPPDATA%\Programs\MobiDesk\Uninstall.bat"

if exist "%UNINSTALLER%" (
    call "%UNINSTALLER%" %*
    exit /b %errorlevel%
)

:: Eger %LOCALAPPDATA% altinda yukleyici bulunamazsa proje icindeki surucu kaldiricilari cagir
echo [i] Kurulu uninstaller bulunamadi, suruculer ve kayitlar temizleniyor...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "MobiDesk" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MobiDesk" /f >nul 2>&1
if exist "%~dp0desktop\remove_shortcuts.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop\remove_shortcuts.ps1" >nul 2>&1
) else (
    if exist "%USERPROFILE%\Desktop\MobiDesk Studio.lnk" del /f /q "%USERPROFILE%\Desktop\MobiDesk Studio.lnk" >nul 2>&1
    if exist "%APPDATA%\Microsoft\Windows\Start Menu\Programs\MobiDesk Studio.lnk" del /f /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\MobiDesk Studio.lnk" >nul 2>&1
)
if exist "%~dp0drivers\display\Sanal_Ekran_Kaldir.bat" call "%~dp0drivers\display\Sanal_Ekran_Kaldir.bat" --silent >nul 2>&1
if exist "%~dp0drivers\audio\Sanal_Ses_Kaldir.bat" call "%~dp0drivers\audio\Sanal_Ses_Kaldir.bat" --silent >nul 2>&1
echo [OK] Temizlik tamamlandi.
exit /b 0
