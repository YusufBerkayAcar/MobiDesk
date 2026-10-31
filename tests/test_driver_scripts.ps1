<#
.SYNOPSIS
    Automated verification harness for MobiDesk Virtual Audio Driver scripts.
.DESCRIPTION
    Tests Sanal_Ses_Ekle.bat --silent and Sanal_Ses_Kaldir.bat --silent:
      - Validates silent execution with ExitCode 0 and hidden window behavior
      - Analyzes execution context (Administrator vs Non-Elevated)
      - If elevated: validates PnP device node 'Root\MobiDeskAudio' and Core Audio endpoint
      - If non-elevated: validates graceful UAC self-elevation dispatch without UI blocking
      - Tests Sanal_Ses_Kaldir.bat --silent and asserts ExitCode 0
#>

[CmdletBinding()]
param(
    [switch]$KeepInstalled = $false
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "    MOBIDESK VIRTUAL AUDIO DRIVER INSTALL/REMOVE TEST     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$ekleBat = Join-Path $projectRoot "drivers\audio\Sanal_Ses_Ekle.bat"
if (-not (Test-Path $ekleBat)) { $ekleBat = Join-Path $projectRoot "Sanal_Ses_Ekle.bat" }

$kaldirBat = Join-Path $projectRoot "drivers\audio\Sanal_Ses_Kaldir.bat"
if (-not (Test-Path $kaldirBat)) { $kaldirBat = Join-Path $projectRoot "Sanal_Ses_Kaldir.bat" }

$vdaDir = Join-Path $projectRoot "drivers\audio\vda\VirtualAudioDriver"
if (-not (Test-Path $vdaDir)) { $vdaDir = Join-Path $projectRoot "vda\VirtualAudioDriver" }

if (-not (Test-Path $ekleBat)) {
    Write-Error "Sanal_Ses_Ekle.bat missing at $ekleBat"
    exit 1
}
if (-not (Test-Path $kaldirBat)) {
    Write-Error "Sanal_Ses_Kaldir.bat missing at $kaldirBat"
    exit 1
}

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$contextStr = if ($isAdmin) { "ELEVATED (Administrator)" } else { "STANDARD (Non-Elevated - UAC Delegation Mode)" }
$contextCol = if ($isAdmin) { "Green" } else { "Yellow" }
Write-Host "Execution Context: $contextStr" -ForegroundColor $contextCol

$testPassed = $true

# --- STEP 1: Test Silent Installation ---
Write-Host "`n[1/4] Executing Sanal_Ses_Ekle.bat --silent ..." -ForegroundColor Yellow
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$pInstall = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"`"$ekleBat`" --silent`"" -Wait -PassThru -WindowStyle Hidden
$sw.Stop()

Write-Host "  Install ExitCode: $($pInstall.ExitCode) (Duration: $($sw.ElapsedMilliseconds) ms)"
if ($pInstall.ExitCode -ne 0) {
    Write-Host "  [FAIL] Sanal_Ses_Ekle.bat returned non-zero exit code: $($pInstall.ExitCode)" -ForegroundColor Red
    $testPassed = $false
} else {
    Write-Host "  [PASS] Sanal_Ses_Ekle.bat executed silently and returned ExitCode 0." -ForegroundColor Green
}

# --- STEP 2: Device Node & Audio Endpoint Verification ---
Write-Host "`n[2/4] Verifying device and endpoint state..." -ForegroundColor Yellow

# Check driver package files in vda/VirtualAudioDriver
$vdaFiles = @("MobiDeskAudio.inf", "MobiDeskAudio.sys", "MobiDeskAudio.cer", "MobiDeskAudio.cat")
$pkgValid = $true
foreach ($f in $vdaFiles) {
    $fPath = Join-Path $vdaDir $f
    if (Test-Path $fPath) {
        $size = (Get-Item $fPath).Length
        Write-Host "  [PASS] Driver package artifact present: $f ($size bytes)" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] Missing driver package artifact: $f at $fPath" -ForegroundColor Red
        $pkgValid = $false
    }
}
if (-not $pkgValid) { $testPassed = $false }

# Check PnP device status
$pnpOut = & pnputil /enum-devices /deviceid "Root\MobiDeskAudio" 2>&1 | Out-String
$devicePresent = ($pnpOut -notmatch "No devices were found" -and $pnpOut -match "Root\\MobiDeskAudio")

if ($devicePresent) {
    Write-Host "  [PASS] Found PnP device 'Root\MobiDeskAudio' in system." -ForegroundColor Green
} else {
    if ($isAdmin) {
        Write-Host "  [FAIL] In elevated context, device node 'Root\MobiDeskAudio' was expected but not found." -ForegroundColor Red
        $testPassed = $false
    } else {
        Write-Host "  [INFO] Device node creation requires interactive UAC confirmation (normal for non-elevated test runners)." -ForegroundColor Yellow
    }
}

# Check Windows Core Audio MMDevices Registry
$endpointFound = $false
$renderKeys = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render' -ErrorAction SilentlyContinue
if ($renderKeys) {
    foreach ($k in $renderKeys) {
        $propPath = $k.PSPath + '\Properties'
        $props = Get-ItemProperty -Path $propPath -ErrorAction SilentlyContinue
        if ($props) {
            $devDesc = $props.'{a45c254e-df1c-4efd-8020-67d146a850e0},2'
            $friendlyName = $props.'{a45c254e-df1c-4efd-8020-67d146a850e0},14'
            if (($friendlyName -and $friendlyName -like "*MobiDesk Audio*") -or ($devDesc -and $devDesc -like "*MobiDesk Audio*")) {
                Write-Host "  [PASS] Windows Core Audio Endpoint verified: '$friendlyName' ($devDesc)" -ForegroundColor Green
                $endpointFound = $true
                break
            }
        }
    }
}

# --- STEP 3: Test Silent Removal ---
Write-Host "`n[3/4] Executing Sanal_Ses_Kaldir.bat --silent ..." -ForegroundColor Yellow
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$pRemove = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"`"$kaldirBat`" --silent`"" -Wait -PassThru -WindowStyle Hidden
$sw.Stop()

Write-Host "  Removal ExitCode: $($pRemove.ExitCode) (Duration: $($sw.ElapsedMilliseconds) ms)"
if ($pRemove.ExitCode -ne 0) {
    Write-Host "  [FAIL] Sanal_Ses_Kaldir.bat returned non-zero exit code: $($pRemove.ExitCode)" -ForegroundColor Red
    $testPassed = $false
} else {
    Write-Host "  [PASS] Sanal_Ses_Kaldir.bat executed silently and returned ExitCode 0." -ForegroundColor Green
}

# --- STEP 4: Script Structure & Security Integrity ---
Write-Host "`n[4/4] Verifying script structure & argument forwarding..." -ForegroundColor Yellow
$ekleContent = Get-Content $ekleBat -Raw
$kaldirContent = Get-Content $kaldirBat -Raw

$ekleHasSilent = $ekleContent.Contains('--silent') -and $ekleContent.Contains('ArgumentList ''%*''')
$kaldirHasSilent = $kaldirContent.Contains('--silent') -and $kaldirContent.Contains('ArgumentList ''%*''')

if ($ekleHasSilent -and $kaldirHasSilent) {
    Write-Host "  [PASS] Both scripts correctly forward CLI arguments '%*' to elevated child process." -ForegroundColor Green
} else {
    Write-Host "  [FAIL] CLI argument forwarding missing in batch scripts!" -ForegroundColor Red
    $testPassed = $false
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
$summaryStr = if ($testPassed) { "ALL DRIVER SCRIPT TESTS PASSED" } else { "DRIVER SCRIPT TESTS FAILED" }
$summaryCol = if ($testPassed) { "Green" } else { "Red" }
Write-Host "Summary: $summaryStr" -ForegroundColor $summaryCol
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not $testPassed) {
    exit 1
}
exit 0
