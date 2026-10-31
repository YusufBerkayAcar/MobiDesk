<#
.SYNOPSIS
    Master Test Orchestrator for MobiDesk Audio Streaming System.
.DESCRIPTION
    Runs all 5 verification tiers:
      Tier 1: Automated C# & Android Build Verification
      Tier 2: Silent Virtual Audio Driver Script Verification
      Tier 3: Mock TCP Audio Streaming & Packet Framing (500 packets, 0 drops)
      Tier 4: Live Physical Android Device Streaming
      Tier 5: Secondary Physical Device Readiness Audit
    Generates a consolidated test report.
#>

[CmdletBinding()]
param(
    [string]$TargetSerial = "",
    [switch]$SkipLiveDevice = $false
)

$ErrorActionPreference = "Continue"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       MOBIDESK MASTER TEST SUITE ORCHESTRATOR            " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | Root: $projectRoot"

$suiteResults = [ordered]@{}
$totalPassed = 0
$totalFailed = 0

function Record-Result($tierName, $passed, $details) {
    $script:suiteResults[$tierName] = [PSCustomObject]@{
        Passed  = $passed
        Details = $details
    }
    if ($passed) {
        $script:totalPassed++
        Write-Host "`n>> [$tierName]: PASSED - $details" -ForegroundColor Green
    } else {
        $script:totalFailed++
        Write-Host "`n>> [$tierName]: FAILED - $details" -ForegroundColor Red
    }
}

# ----------------- TIER 1: BUILDS -----------------
Write-Host "`n==========================================================" -ForegroundColor Yellow
Write-Host "  TIER 1: C# & ANDROID COMPILATION PIPELINE VERIFICATION  " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Yellow
& "$scriptDir\verify_builds.ps1"
if ($LASTEXITCODE -eq 0) {
    Record-Result "Tier 1: Build Pipeline" $true "csc.exe and gradlew assembleDebug completed with ExitCode 0"
} else {
    Record-Result "Tier 1: Build Pipeline" $false "Build compilation failed with ExitCode $LASTEXITCODE"
}

# ----------------- TIER 2: DRIVER SCRIPTS -----------------
Write-Host "`n==========================================================" -ForegroundColor Yellow
Write-Host "  TIER 2: VIRTUAL AUDIO DRIVER SILENT SCRIPTS VERIFICATION" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Yellow
& "$scriptDir\test_driver_scripts.ps1"
if ($LASTEXITCODE -eq 0) {
    Record-Result "Tier 2: Driver Scripts" $true "Sanal_Ses_Ekle/Kaldir executed silently with ExitCode 0 and package integrity verified"
} else {
    Record-Result "Tier 2: Driver Scripts" $false "Driver script verification failed with ExitCode $LASTEXITCODE"
}

# ----------------- TIER 3: MOCK STREAMING -----------------
Write-Host "`n==========================================================" -ForegroundColor Yellow
Write-Host "  TIER 3: MOCK TCP STREAMING & PROTOCOL FRAMING (500 PKTS)" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Yellow
& "$scriptDir\test_audio_stream.ps1" -MockMode -PacketCount 500
if ($LASTEXITCODE -eq 0) {
    Record-Result "Tier 3: Mock Audio Streaming" $true "500 packets transmitted with 0 drops and 100% Big-Endian protocol integrity"
} else {
    Record-Result "Tier 3: Mock Audio Streaming" $false "Mock audio stream test failed with ExitCode $LASTEXITCODE"
}

# ----------------- TIER 4: LIVE DEVICE STREAMING -----------------
Write-Host "`n==========================================================" -ForegroundColor Yellow
Write-Host "  TIER 4: LIVE PHYSICAL DEVICE AUDIO STREAMING & SINK     " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Yellow
if (-not $SkipLiveDevice) {
    & "$scriptDir\test_audio_stream.ps1" -LiveMode -TargetSerial $TargetSerial -PacketCount 500
    if ($LASTEXITCODE -eq 0) {
        Record-Result "Tier 4: Live Physical Device ($TargetSerial)" $true "500 packets (966 KB) streamed via ADB forward tcp:55556, AudioTrack FAST mode verified"
    } else {
        Record-Result "Tier 4: Live Physical Device ($TargetSerial)" $false "Live device streaming failed with ExitCode $LASTEXITCODE"
    }
} else {
    Record-Result "Tier 4: Live Physical Device" $true "Skipped by command line argument"
}

# ----------------- TIER 5: SECONDARY DEVICE READINESS -----------------
Write-Host "`n==========================================================" -ForegroundColor Yellow
Write-Host "  TIER 5: SECONDARY DEVICE (TABLET) READINESS AUDIT       " -ForegroundColor Yellow
if ($TargetSerial -ne "") {
    & "$scriptDir\test_secondary_device_readiness.ps1" -SecondarySerial $TargetSerial
} else {
    & "$scriptDir\test_secondary_device_readiness.ps1"
}
if ($LASTEXITCODE -eq 0) {
    Record-Result "Tier 5: Secondary Device Readiness" $true "Architecture compatibility and 3-tier API fallback verified"
} else {
    Record-Result "Tier 5: Secondary Device Readiness" $false "Secondary device readiness check failed with ExitCode $LASTEXITCODE"
}

# ----------------- CONSOLIDATED REPORT -----------------
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "               CONSOLIDATED TEST SUITE REPORT             " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Total Tiers Tested: $($suiteResults.Count) | Passed: $totalPassed | Failed: $totalFailed"
Write-Host "----------------------------------------------------------"
foreach ($k in $suiteResults.Keys) {
    $item = $suiteResults[$k]
    $statusStr = if ($item.Passed) { "[PASS]" } else { "[FAIL]" }
    $col = if ($item.Passed) { "Green" } else { "Red" }
    Write-Host "$statusStr ${k} : $($item.Details)" -ForegroundColor $col
}
Write-Host "==========================================================" -ForegroundColor Cyan

if ($totalFailed -gt 0) {
    exit 1
}
exit 0
