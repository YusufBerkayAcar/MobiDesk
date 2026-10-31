# Milestone M1 Empirical Challenger Stress Test Suite
# Tests C# compilation, execution contexts, MobiPathResolver, config edge cases, log locking, and dual-write.

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$ProjectRoot = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $ProjectRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   M1 EMPIRICAL CHALLENGER STRESS HARNESS                 " -ForegroundColor Cyan
Write-Host "   Project Root: $ProjectRoot                             " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$Passed = 0
$Failed = 0
$Tests = @()

function Assert-Condition([bool]$condition, [string]$testName, [string]$details = "") {
    if ($condition) {
        Write-Host "  [PASS] $testName $(if ($details) {"($details)"})" -ForegroundColor Green
        $script:Passed++
        $script:Tests += [PSCustomObject]@{ Test = $testName; Status = "PASS"; Details = $details }
    } else {
        Write-Host "  [FAIL] $testName $(if ($details) {"($details)"})" -ForegroundColor Red
        $script:Failed++
        $script:Tests += [PSCustomObject]@{ Test = $testName; Status = "FAIL"; Details = $details }
    }
}

# Ensure no rogue processes are running before starting
Get-Process -Name MobiDesk -ErrorAction SilentlyContinue | Stop-Process -Force

# ==============================================================================
# SECTION 1: Compilation Stress Across Working Directories
# ==============================================================================
Write-Host "`n--- Section 1: Compilation Stress Across Working Directories ---" -ForegroundColor Yellow

# 1.1 Compile from repository root
Set-Location $ProjectRoot
$p1 = Start-Process -FilePath "cmd.exe" -ArgumentList "/c desktop\build.bat" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Condition ($p1.ExitCode -eq 0 -and (Test-Path "$ProjectRoot\desktop\MobiDesk.exe")) `
    "Compile from Repository Root" "ExitCode=$($p1.ExitCode)"

# 1.2 Compile from desktop directory
Set-Location "$ProjectRoot\desktop"
$p2 = Start-Process -FilePath "cmd.exe" -ArgumentList "/c build.bat" -WorkingDirectory "$ProjectRoot\desktop" -NoNewWindow -Wait -PassThru
Assert-Condition ($p2.ExitCode -eq 0) `
    "Compile from inside desktop/ Directory" "ExitCode=$($p2.ExitCode)"

# 1.3 Compile from temp directory
Set-Location $env:TEMP
$p3 = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$ProjectRoot\desktop\build.bat`"" -WorkingDirectory $env:TEMP -NoNewWindow -Wait -PassThru
Assert-Condition ($p3.ExitCode -eq 0) `
    "Compile from Temp Directory" "ExitCode=$($p3.ExitCode)"

Set-Location $ProjectRoot

# ==============================================================================
# SECTION 2: Test Suite Execution Across Working Directories
# ==============================================================================
Write-Host "`n--- Section 2: Test Suite Execution Across Working Directories ---" -ForegroundColor Yellow

function Run-MobiTest([string]$execPath, [string]$argString, [string]$workDir = $ProjectRoot) {
    if (Test-Path "$ProjectRoot\desktop\test_results.log") {
        Remove-Item -Force "$ProjectRoot\desktop\test_results.log"
    }
    $p = Start-Process -FilePath $execPath -ArgumentList $argString -WorkingDirectory $workDir -NoNewWindow -Wait -PassThru
    $logContent = if (Test-Path "$ProjectRoot\desktop\test_results.log") {
        Get-Content "$ProjectRoot\desktop\test_results.log" -Raw
    } else { "" }
    $passed147 = ($logContent -match "RESULTS: Total=147, Passed=147, Failed=0")
    return @{
        ExitCode = $p.ExitCode
        PassedAll = $passed147
    }
}

# 2.1 Direct exe from root
$r21 = Run-MobiTest "$ProjectRoot\desktop\MobiDesk.exe" "--test" $ProjectRoot
Assert-Condition ($r21.ExitCode -eq 0 -and $r21.PassedAll) `
    "desktop/MobiDesk.exe --test executed from root" "ExitCode=$($r21.ExitCode)"

# 2.2 Direct exe from desktop/
$r22 = Run-MobiTest "$ProjectRoot\desktop\MobiDesk.exe" "--test" "$ProjectRoot\desktop"
Assert-Condition ($r22.ExitCode -eq 0 -and $r22.PassedAll) `
    "MobiDesk.exe --test executed inside desktop/" "ExitCode=$($r22.ExitCode)"

# 2.3 Direct exe from temp
$r23 = Run-MobiTest "$ProjectRoot\desktop\MobiDesk.exe" "--test" $env:TEMP
Assert-Condition ($r23.ExitCode -eq 0 -and $r23.PassedAll) `
    "desktop/MobiDesk.exe --test executed from temp dir" "ExitCode=$($r23.ExitCode)"

# 2.4 MobiDesk.bat --test from root
$r24 = Run-MobiTest "cmd.exe" "/c `"$ProjectRoot\MobiDesk.bat`" --test" $ProjectRoot
Assert-Condition ($r24.ExitCode -eq 0 -and $r24.PassedAll) `
    "MobiDesk.bat --test executed from root" "ExitCode=$($r24.ExitCode)"

# 2.5 MobiDesk.bat --test from desktop/
$r25 = Run-MobiTest "cmd.exe" "/c `"$ProjectRoot\MobiDesk.bat`" --test" "$ProjectRoot\desktop"
Assert-Condition ($r25.ExitCode -eq 0 -and $r25.PassedAll) `
    "MobiDesk.bat --test executed from inside desktop/" "ExitCode=$($r25.ExitCode)"

# 2.6 MobiDesk.bat --test from temp
$r26 = Run-MobiTest "cmd.exe" "/c `"$ProjectRoot\MobiDesk.bat`" --test" $env:TEMP
Assert-Condition ($r26.ExitCode -eq 0 -and $r26.PassedAll) `
    "MobiDesk.bat --test executed from temp dir" "ExitCode=$($r26.ExitCode)"

# ==============================================================================
# SECTION 3: MobiPathResolver Deep Validation Probe
# ==============================================================================
Write-Host "`n--- Section 3: MobiPathResolver Deep Validation Probe ---" -ForegroundColor Yellow

$probeCs = @"
using System;
using System.IO;
using ScreenMonitoringApp;

namespace ProbeTest {
    class Program {
        static int Main() {
            string root = MobiPathResolver.GetProjectRoot();
            string png = MobiPathResolver.ResolveAsset("app_icon.png");
            string ico = MobiPathResolver.ResolveAsset("app_icon.ico");
            string addDisp = MobiPathResolver.ResolveDisplayScript("Sanal_Ekran_Ekle.bat");
            string remDisp = MobiPathResolver.ResolveDisplayScript("Sanal_Ekran_Kaldir.bat");
            string addAud = MobiPathResolver.ResolveAudioScript("Sanal_Ses_Ekle.bat");
            string remAud = MobiPathResolver.ResolveAudioScript("Sanal_Ses_Kaldir.bat");
            string usb = MobiPathResolver.ResolveDirectory("usbmmidd", "drivers/display");
            string vdd = MobiPathResolver.ResolveDirectory("vdd", "drivers/display");
            string vda = MobiPathResolver.ResolveDirectory("vda", "drivers/audio");

            bool ok = !string.IsNullOrEmpty(root) && Directory.Exists(root) &&
                      File.Exists(png) && File.Exists(ico) &&
                      File.Exists(addDisp) && File.Exists(remDisp) &&
                      File.Exists(addAud) && File.Exists(remAud) &&
                      Directory.Exists(usb) && Directory.Exists(vdd) && Directory.Exists(vda);

            Console.WriteLine("ROOT: " + root);
            Console.WriteLine("PNG: " + png);
            Console.WriteLine("ICO: " + ico);
            Console.WriteLine("ADD_DISP: " + addDisp);
            Console.WriteLine("REM_DISP: " + remDisp);
            Console.WriteLine("ADD_AUD: " + addAud);
            Console.WriteLine("REM_AUD: " + remAud);
            Console.WriteLine("USB: " + usb);
            Console.WriteLine("VDD: " + vdd);
            Console.WriteLine("VDA: " + vda);
            Console.WriteLine("ALL_RESOLVED: " + ok);

            return ok ? 0 : 1;
        }
    }
}
"@

$probeSource = "$ProjectRoot\desktop\PathProbe.cs"
$probeExe = "$ProjectRoot\desktop\PathProbe.exe"
Set-Content -Path $probeSource -Value $probeCs

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$pCompile = Start-Process -FilePath $csc -ArgumentList "/nologo /target:exe /out:`"$probeExe`" /r:`"$ProjectRoot\desktop\MobiDesk.exe`" `"$probeSource`"" -Wait -PassThru -NoNewWindow
Assert-Condition ($pCompile.ExitCode -eq 0 -and (Test-Path $probeExe)) "PathProbe probe compiled against MobiDesk.exe"

if (Test-Path $probeExe) {
    # 3.1 Run probe from desktop/
    $pRunDesk = Start-Process -FilePath $probeExe -WorkingDirectory "$ProjectRoot\desktop" -Wait -PassThru -NoNewWindow
    Assert-Condition ($pRunDesk.ExitCode -eq 0) "PathProbe in desktop/ resolves all assets, scripts, and drivers" "ExitCode=$($pRunDesk.ExitCode)"

    # 3.2 Run probe from repo root
    $pRunRoot = Start-Process -FilePath $probeExe -WorkingDirectory $ProjectRoot -Wait -PassThru -NoNewWindow
    Assert-Condition ($pRunRoot.ExitCode -eq 0) "PathProbe from repo root resolves all assets, scripts, and drivers" "ExitCode=$($pRunRoot.ExitCode)"

    # 3.3 Run probe from temp dir
    $pRunTemp = Start-Process -FilePath $probeExe -WorkingDirectory $env:TEMP -Wait -PassThru -NoNewWindow
    Assert-Condition ($pRunTemp.ExitCode -eq 0) "PathProbe from temp dir resolves all assets, scripts, and drivers" "ExitCode=$($pRunTemp.ExitCode)"

    # Cleanup probe
    Remove-Item -Force $probeSource, $probeExe
}

# ==============================================================================
# SECTION 4: Edge Cases (mobidesk.cfg & Log Locking)
# ==============================================================================
Write-Host "`n--- Section 4: Edge Cases (mobidesk.cfg & Log Locking) ---" -ForegroundColor Yellow

# 4.1 Missing mobidesk.cfg
$cfgPath = "$ProjectRoot\desktop\mobidesk.cfg"
$cfgBak = "$ProjectRoot\desktop\mobidesk.cfg.edge_bak"
if (Test-Path $cfgPath) { Move-Item -Force $cfgPath $cfgBak }
try {
    $rMissingCfg = Run-MobiTest "$ProjectRoot\desktop\MobiDesk.exe" "--test" $ProjectRoot
    Assert-Condition ($rMissingCfg.ExitCode -eq 0 -and $rMissingCfg.PassedAll) `
        "Missing mobidesk.cfg handled gracefully (147/147 pass)" "ExitCode=$($rMissingCfg.ExitCode)"
    Assert-Condition (Test-Path $cfgPath) `
        "Default mobidesk.cfg automatically recreated"
} finally {
    if (Test-Path $cfgBak) { Move-Item -Force $cfgBak $cfgPath }
}

# 4.2 mobidesk.cfg located ONLY in root
$rootCfg = "$ProjectRoot\mobidesk.cfg"
if (Test-Path $cfgPath) { Move-Item -Force $cfgPath $rootCfg }
try {
    Set-Content $rootCfg "lang=en`nfps=45`nquality=85"
    $rRootCfg = Run-MobiTest "$ProjectRoot\desktop\MobiDesk.exe" "--test" $ProjectRoot
    Assert-Condition ($rRootCfg.ExitCode -eq 0 -and $rRootCfg.PassedAll) `
        "Root-only mobidesk.cfg handled gracefully (147/147 pass)" "ExitCode=$($rRootCfg.ExitCode)"
} finally {
    if (Test-Path $rootCfg) { Move-Item -Force $rootCfg $cfgPath }
}

# 4.3 Corrupt mobidesk.cfg
if (Test-Path $cfgPath) { Copy-Item -Force $cfgPath $cfgBak }
try {
    Set-Content $cfgPath "fps=-999`nquality=99999`nwindow_w=10`nwindow_h=-500`nlang=UNKNOWN`n[CORRUPT_SYNTAX]"
    $rCorruptCfg = Run-MobiTest "$ProjectRoot\desktop\MobiDesk.exe" "--test" $ProjectRoot
    Assert-Condition ($rCorruptCfg.ExitCode -eq 0 -and $rCorruptCfg.PassedAll) `
        "Corrupt mobidesk.cfg handled with safe clamped defaults (147/147 pass)" "ExitCode=$($rCorruptCfg.ExitCode)"
} finally {
    if (Test-Path $cfgBak) { Move-Item -Force $cfgBak $cfgPath }
}

# 4.4 Read-only test_results.log
$desktopLog = "$ProjectRoot\desktop\test_results.log"
$rootLog = "$ProjectRoot\test_results.log"
& attrib.exe +r $desktopLog 2>$null
& attrib.exe +r $rootLog 2>$null
try {
    $pReadOnly = Start-Process -FilePath "$ProjectRoot\desktop\MobiDesk.exe" -ArgumentList "--test" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
    Assert-Condition ($pReadOnly.ExitCode -eq 0) `
        "Read-only test_results.log safely caught without crash" "ExitCode=$($pReadOnly.ExitCode)"
} finally {
    & attrib.exe -r $desktopLog 2>$null
    & attrib.exe -r $rootLog 2>$null
}

# 4.5 Exclusively locked test_results.log
$stream = [System.IO.File]::Open($desktopLog, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
    $pLocked = Start-Process -FilePath "$ProjectRoot\desktop\MobiDesk.exe" -ArgumentList "--test" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
    Assert-Condition ($pLocked.ExitCode -eq 0) `
        "Exclusively locked test_results.log caught gracefully without crash" "ExitCode=$($pLocked.ExitCode)"
} finally {
    $stream.Close()
    $stream.Dispose()
}

# ==============================================================================
# SECTION 5: Log Dual-Write Verification
# ==============================================================================
Write-Host "`n--- Section 5: Log Dual-Write Verification ---" -ForegroundColor Yellow

if (Test-Path $desktopLog) { Remove-Item -Force $desktopLog }
if (Test-Path $rootLog) { Remove-Item -Force $rootLog }

$pDual = Start-Process -FilePath "$ProjectRoot\desktop\MobiDesk.exe" -ArgumentList "--test" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Condition ($pDual.ExitCode -eq 0) "MobiDesk.exe executed for dual-write verification"

Assert-Condition (Test-Path $desktopLog) "desktop/test_results.log was created"
Assert-Condition (Test-Path $rootLog) "Root test_results.log was created"

if ((Test-Path $desktopLog) -and (Test-Path $rootLog)) {
    $desktopLen = (Get-Item $desktopLog).Length
    $rootLen = (Get-Item $rootLog).Length
    $desktopHash = (Get-FileHash $desktopLog -Algorithm SHA256).Hash
    $rootHash = (Get-FileHash $rootLog -Algorithm SHA256).Hash

    Assert-Condition ($desktopLen -eq $rootLen -and $desktopHash -eq $rootHash) `
        "Dual-write logs have identical size and SHA-256 hash" `
        "DesktopBytes=$desktopLen, RootBytes=$rootLen, Hash=$desktopHash"
}

# ==============================================================================
# Summary & Exit
# ==============================================================================
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "   STRESS TEST RESULTS: Total=$($Passed + $Failed), Passed=$Passed, Failed=$Failed" -ForegroundColor $(if ($Failed -eq 0) {"Green"} else {"Red"})
Write-Host "==========================================================" -ForegroundColor Cyan

if ($Failed -gt 0) {
    exit 1
} else {
    exit 0
}
