<#
.SYNOPSIS
    Automated build verification harness for MobiDesk (C# WinForms + Android APK).
.DESCRIPTION
    Compiles ScreenMonitorGUI.cs with csc.exe and runs gradle assembleDebug.
    Validates zero exit codes, binary outputs, and reports execution timings.
#>

[CmdletBinding()]
param(
    [switch]$SkipAndroid = $false,
    [switch]$SkipCSharp = $false
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       MOBIDESK AUTOMATED BUILD VERIFICATION HARNESS      " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Project Root: $projectRoot"

$overallSuccess = $true
$buildResults = @{}

# 1. C# Compilation Check
if (-not $SkipCSharp) {
    Write-Host "`n[1/2] Verifying C# WinForms compilation..." -ForegroundColor Yellow
    $cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $cscPath)) {
        Write-Error "csc.exe not found at $cscPath"
        $overallSuccess = $false
        $buildResults["CSharp"] = "FAILED (csc.exe missing)"
    } else {
        $desktopDir = Join-Path $projectRoot "desktop"
        $sourceFile = Join-Path $desktopDir "ScreenMonitorGUI.cs"
        if (-not (Test-Path $sourceFile)) { $sourceFile = Join-Path $projectRoot "ScreenMonitorGUI.cs" }
        $sourceArg = if (Test-Path $sourceFile) { $sourceFile } else { "/recurse:$desktopDir\*.cs" }
        $iconFile = Join-Path $projectRoot "assets\icons\app_icon.ico"
        if (-not (Test-Path $iconFile)) { $iconFile = Join-Path $projectRoot "app_icon.ico" }
        $manifestFile = Join-Path $projectRoot "desktop\app.manifest"
        if (-not (Test-Path $manifestFile)) { $manifestFile = Join-Path $projectRoot "app.manifest" }
        $testExe = Join-Path $projectRoot "desktop\MobiDesk_BuildTest.exe"

        if (Test-Path $testExe) { Remove-Item -Force $testExe }

        $cscArgs = @(
            "/nologo",
            "/target:winexe",
            "/unsafe",
            "/win32icon:$iconFile",
            "/win32manifest:$manifestFile",
            "/out:$testExe",
            $sourceArg
        )

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process -FilePath $cscPath -ArgumentList $cscArgs -Wait -PassThru -NoNewWindow
        $sw.Stop()

        if ($p.ExitCode -eq 0 -and (Test-Path $testExe)) {
            $exeSize = (Get-Item $testExe).Length
            Write-Host "  [PASS] C# Compilation succeeded in $($sw.ElapsedMilliseconds) ms. Binary Size: $exeSize bytes" -ForegroundColor Green
            $buildResults["CSharp"] = "SUCCESS ($($sw.ElapsedMilliseconds) ms, $exeSize bytes)"
            Remove-Item -Force $testExe
        } else {
            Write-Host "  [FAIL] C# Compilation failed with ExitCode: $($p.ExitCode)" -ForegroundColor Red
            $overallSuccess = $false
            $buildResults["CSharp"] = "FAILED (ExitCode: $($p.ExitCode))"
        }
    }
}

# 2. Android Gradle Build Check
if (-not $SkipAndroid) {
    Write-Host "`n[2/2] Verifying Android Gradle assembleDebug..." -ForegroundColor Yellow
    $gradlew = Join-Path $projectRoot "gradlew.bat"
    if (-not (Test-Path $gradlew)) {
        Write-Error "gradlew.bat not found at $gradlew"
        $overallSuccess = $false
        $buildResults["Android"] = "FAILED (gradlew.bat missing)"
    } else {
        $apkPath = Join-Path $projectRoot "android\app\build\outputs\apk\debug\app-debug.apk"
        if (-not (Test-Path $apkPath)) { $apkPath = Join-Path $projectRoot "app\build\outputs\apk\debug\app-debug.apk" }
        
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Push-Location $projectRoot
        try {
            Write-Host "  Executing: gradlew assembleDebug ..."
            & cmd.exe /c "`"$gradlew`" assembleDebug"
            $exitCode = $LASTEXITCODE
            $sw.Stop()

            if ($exitCode -eq 0 -and (Test-Path $apkPath)) {
                $apkSize = (Get-Item $apkPath).Length
                Write-Host "  [PASS] Android assembleDebug succeeded in $($sw.Elapsed.TotalSeconds.ToString('0.0')) s. APK Size: $apkSize bytes" -ForegroundColor Green
                $buildResults["Android"] = "SUCCESS ($($sw.Elapsed.TotalSeconds.ToString('0.0')) s, $apkSize bytes)"
            } else {
                Write-Host "  [FAIL] Android assembleDebug failed with ExitCode: $exitCode" -ForegroundColor Red
                $overallSuccess = $false
                $buildResults["Android"] = "FAILED (ExitCode: $exitCode)"
            }
        } finally {
            Pop-Location
        }
    }
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "                 BUILD VERIFICATION SUMMARY               " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
foreach ($k in $buildResults.Keys) {
    Write-Host "  ${k}: $($buildResults[$k])"
}
$statusStr = if ($overallSuccess) { "ALL BUILDS PASSED" } else { "BUILDS FAILED" }
$statusColor = if ($overallSuccess) { "Green" } else { "Red" }
Write-Host "Overall Status: $statusStr" -ForegroundColor $statusColor

if (-not $overallSuccess) {
    exit 1
}
exit 0
