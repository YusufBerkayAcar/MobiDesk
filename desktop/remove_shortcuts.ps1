# Remove Desktop shortcut (including OneDrive redirected Desktops)
$desktopPath = [Environment]::GetFolderPath('Desktop')
$deskLnk = Join-Path $desktopPath "MobiDesk Studio.lnk"
if (Test-Path $deskLnk) { Remove-Item -Force $deskLnk }

$userDesk = Join-Path $env:USERPROFILE "Desktop\MobiDesk Studio.lnk"
if (Test-Path $userDesk) { Remove-Item -Force $userDesk }

# Remove Start Menu shortcut
$startMenuPath = [Environment]::GetFolderPath('Programs')
$menuLnk = Join-Path $startMenuPath "MobiDesk Studio.lnk"
if (Test-Path $menuLnk) { Remove-Item -Force $menuLnk }

Write-Host "[OK] Shortcuts removed successfully."

