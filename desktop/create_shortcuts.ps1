param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\MobiDesk"
)

$ws = New-Object -ComObject WScript.Shell

# 1. Desktop Shortcut
$desktopPath = [Environment]::GetFolderPath('Desktop')
$deskLnk = $ws.CreateShortcut((Join-Path $desktopPath "MobiDesk Studio.lnk"))
$deskLnk.TargetPath = Join-Path $InstallDir "MobiDesk.exe"
$deskLnk.WorkingDirectory = $InstallDir
$deskLnk.IconLocation = (Join-Path $InstallDir "assets\icons\app_icon.ico") + ",0"
$deskLnk.Description = "MobiDesk Studio - 60 FPS Ultra-Low Latency Second Screen"
$deskLnk.Save()

# 2. Start Menu Shortcut
$startMenuPath = [Environment]::GetFolderPath('Programs')
$menuLnk = $ws.CreateShortcut((Join-Path $startMenuPath "MobiDesk Studio.lnk"))
$menuLnk.TargetPath = Join-Path $InstallDir "MobiDesk.exe"
$menuLnk.WorkingDirectory = $InstallDir
$menuLnk.IconLocation = (Join-Path $InstallDir "assets\icons\app_icon.ico") + ",0"
$menuLnk.Description = "MobiDesk Studio"
$menuLnk.Save()

Write-Host "[OK] Shortcuts created successfully on Desktop and Start Menu."

