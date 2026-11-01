# ==============================================================================
# Challenger Modular M1-2 Adversarial Stress Test Suite
# ==============================================================================
# Verifies:
#   1. Single-Instance Mutex & Wakeup Event Safety + Mutex Bypass on --test
#   2. Cyber Obsidian Theme Integrity & MainForm Aliases (Reflection & Instantiation)
#   3. Test Log Dual-Write Consistency (SHA256 Match across Working Dirs)
# ==============================================================================

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $ProjectRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   CHALLENGER M1-2 ADVERSARIAL STRESS TEST HARNESS        " -ForegroundColor Cyan
Write-Host "   Root: $ProjectRoot                                     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$Passed = 0
$Failed = 0
$TestResults = @()

function Assert-Test([bool]$Condition, [string]$TestName, [string]$Details = "") {
    if ($Condition) {
        Write-Host "  [PASS] $TestName $(if ($Details) {"- $Details"})" -ForegroundColor Green
        $script:Passed++
        $script:TestResults += [PSCustomObject]@{ Test = $TestName; Status = "PASS"; Details = $Details }
    } else {
        Write-Host "  [FAIL] $TestName $(if ($Details) {"- $Details"})" -ForegroundColor Red
        $script:Failed++
        $script:TestResults += [PSCustomObject]@{ Test = $TestName; Status = "FAIL"; Details = $Details }
    }
}

# Ensure no hanging MobiDesk processes before starting
Get-Process -Name MobiDesk -ErrorAction SilentlyContinue | Stop-Process -Force

$exePath = "$ProjectRoot\desktop\MobiDesk.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "Compiling MobiDesk.exe..." -ForegroundColor Yellow
    cmd.exe /c "desktop\build.bat"
}

# ==============================================================================
# SECTION 1: Single-Instance Mutex & Argument Safety
# ==============================================================================
Write-Host "`n--- SECTION 1: Single-Instance Mutex & Argument Safety ---" -ForegroundColor Yellow

$MUTEX_NAME = "Local\MobiDesk_SingleInstance_Mutex_v1"
$EVENT_NAME = "Local\MobiDesk_Wakeup_Event_v1"

# 1.1 Source Code Verification of Named Primitives
$programCs = Get-Content "$ProjectRoot\desktop\Program.cs" -Raw
$hasMutexName = $programCs -match [regex]::Escape($MUTEX_NAME)
$hasEventName = $programCs -match [regex]::Escape($EVENT_NAME)
Assert-Test ($hasMutexName -and $hasEventName) `
    "Program.cs contains exact named primitives" `
    "Mutex: $MUTEX_NAME, Event: $EVENT_NAME"

# 1.2 Verify --test early dispatch in source code
$testIndex = $programCs.IndexOf("runTests")
$mutexIndex = $programCs.IndexOf($MUTEX_NAME)
Assert-Test ($testIndex -gt 0 -and $testIndex -lt $mutexIndex) `
    "--test dispatch precedes Mutex acquisition in Program.cs" `
    "runTestsIdx=$testIndex, mutexIdx=$mutexIndex"

# 1.3 Mutex Bypass Stress: Hold Mutex externally and execute --test
Write-Host "`n[*] Simulating active mutex holding '$MUTEX_NAME'..." -ForegroundColor Cyan
$mutexCreated = $false
$foreignMutex = $null
try {
    $foreignMutex = New-Object System.Threading.Mutex($true, $MUTEX_NAME, [ref]$mutexCreated)
    Assert-Test ($mutexCreated -eq $true) "Successfully acquired $MUTEX_NAME in harness"

    # Now run MobiDesk.exe --test while the mutex is held!
    $pTestUnderMutex = Start-Process -FilePath $exePath -ArgumentList "--test" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
    Assert-Test ($pTestUnderMutex.ExitCode -eq 0) `
        "--test bypasses mutex acquisition and executes successfully when mutex is locked" `
        "ExitCode=$($pTestUnderMutex.ExitCode)"

    # Also test case variations while mutex is held
    $pUpper = Start-Process -FilePath $exePath -ArgumentList "--TEST" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
    Assert-Test ($pUpper.ExitCode -eq 0) `
        "--TEST (uppercase) bypasses mutex acquisition" "ExitCode=$($pUpper.ExitCode)"

    $pTests = Start-Process -FilePath $exePath -ArgumentList "--tests" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
    Assert-Test ($pTests.ExitCode -eq 0) `
        "--tests alias bypasses mutex acquisition" "ExitCode=$($pTests.ExitCode)"

    $pRunTests = Start-Process -FilePath $exePath -ArgumentList "--run-tests" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
    Assert-Test ($pRunTests.ExitCode -eq 0) `
        "--run-tests alias bypasses mutex acquisition" "ExitCode=$($pRunTests.ExitCode)"

} finally {
    if ($foreignMutex -ne $null) {
        $foreignMutex.ReleaseMutex()
        $foreignMutex.Close()
        $foreignMutex.Dispose()
    }
}

# 1.4 Real Two-Instance GUI Mutex & Wakeup Event Signaling
Write-Host "`n[*] Verifying two-instance GUI behavior and wakeup signaling..." -ForegroundColor Cyan
$pPrimary = $null
$pSecondary = $null
try {
    # Launch primary MobiDesk instance minimized
    $pPrimary = Start-Process -FilePath $exePath -ArgumentList "--minimized" -WorkingDirectory $ProjectRoot -PassThru
    Start-Sleep -Milliseconds 1200
    Assert-Test (-not $pPrimary.HasExited) "Primary MobiDesk instance running" "PID=$($pPrimary.Id)"

    # Launch secondary MobiDesk instance in normal GUI mode
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $pSecondary = Start-Process -FilePath $exePath -WorkingDirectory $ProjectRoot -PassThru
    $pSecondary.WaitForExit(5000)
    $sw.Stop()

    Assert-Test ($pSecondary.HasExited -eq $true) `
        "Secondary GUI instance exited immediately upon detecting running primary instance" `
        "ElapsedMs=$($sw.ElapsedMilliseconds), ExitCode=$($pSecondary.ExitCode)"

    # While primary instance is still actively running, execute --test
    $pTestWhileGuiRunning = Start-Process -FilePath $exePath -ArgumentList "--test" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
    Assert-Test ($pTestWhileGuiRunning.ExitCode -eq 0) `
        "--test runs cleanly and completes with ExitCode 0 while primary GUI instance is running" `
        "ExitCode=$($pTestWhileGuiRunning.ExitCode)"

} finally {
    if ($pSecondary -ne $null -and -not $pSecondary.HasExited) { $pSecondary.Kill() }
    if ($pPrimary -ne $null -and -not $pPrimary.HasExited) { $pPrimary.Kill() }
}

# 1.5 Concurrent --test Execution Stress
Write-Host "`n[*] Stress-testing 3 concurrent --test executions in parallel..." -ForegroundColor Cyan
$concurrentCount = 3
$procs = @()
for ($i = 0; $i -lt $concurrentCount; $i++) {
    $procs += Start-Process -FilePath $exePath -ArgumentList "--test" -WorkingDirectory $ProjectRoot -PassThru
}

$allExitedZero = $true
$exitCodes = @()
foreach ($p in $procs) {
    $p.WaitForExit()
    $exitCodes += $p.ExitCode
    if ($p.ExitCode -ne 0) { $allExitedZero = $false }
}
Assert-Test ($allExitedZero -eq $true) `
    "All $concurrentCount concurrent --test instances completed with ExitCode 0" `
    "ExitCodes=$($exitCodes -join ',')"


# ==============================================================================
# SECTION 2: Cyber Obsidian Theme Integrity & Reflection Probe
# ==============================================================================
Write-Host "`n--- SECTION 2: Cyber Obsidian Theme Integrity ---" -ForegroundColor Yellow

$probeCode = @'
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using ScreenMonitoringApp;

namespace ThemeStressProbe
{
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            int failures = 0;

            // 1. Verify all 15 CyberObsidianTheme constants against exact original RGB values
            var expectedColors = new System.Collections.Generic.Dictionary<string, Color>()
            {
                { "Bg", Color.FromArgb(11, 14, 22) },
                { "Card", Color.FromArgb(17, 22, 34) },
                { "CardBorder", Color.FromArgb(30, 39, 60) },
                { "CardHover", Color.FromArgb(23, 30, 46) },
                { "Primary", Color.FromArgb(99, 102, 241) },
                { "PrimaryDark", Color.FromArgb(79, 70, 229) },
                { "Accent", Color.FromArgb(6, 182, 212) },
                { "Success", Color.FromArgb(16, 185, 129) },
                { "SuccessDark", Color.FromArgb(5, 150, 105) },
                { "Danger", Color.FromArgb(239, 68, 68) },
                { "DangerDark", Color.FromArgb(190, 18, 60) },
                { "Warning", Color.FromArgb(245, 158, 11) },
                { "Text", Color.FromArgb(248, 250, 252) },
                { "TextMuted", Color.FromArgb(148, 163, 184) },
                { "TextSubtle", Color.FromArgb(100, 116, 139) }
            };

            Type themeType = typeof(CyberObsidianTheme);
            foreach (var kv in expectedColors)
            {
                FieldInfo fi = themeType.GetField(kv.Key, BindingFlags.Public | BindingFlags.Static);
                if (fi == null)
                {
                    Console.WriteLine("FAIL: CyberObsidianTheme missing field " + kv.Key);
                    failures++;
                    continue;
                }
                Color val = (Color)fi.GetValue(null);
                if (val.ToArgb() != kv.Value.ToArgb())
                {
                    Console.WriteLine("FAIL: CyberObsidianTheme." + kv.Key + " mismatch. Expected " + kv.Value + ", Got " + val);
                    failures++;
                }
            }

            // 2. Verify all MainForm static aliases match CyberObsidianTheme exactly
            Type mainFormType = typeof(MainForm);
            var formAliases = new System.Collections.Generic.Dictionary<string, string>()
            {
                { "ColorBg", "Bg" },
                { "ColorCard", "Card" },
                { "ColorCardBorder", "CardBorder" },
                { "ColorCardHover", "CardHover" },
                { "ColorPrimary", "Primary" },
                { "ColorPrimaryDark", "PrimaryDark" },
                { "ColorAccent", "Accent" },
                { "ColorSuccess", "Success" },
                { "Success", "Success" },
                { "ColorSuccessDark", "SuccessDark" },
                { "ColorDanger", "Danger" },
                { "ColorDangerDark", "DangerDark" },
                { "ColorWarning", "Warning" },
                { "Warning", "Warning" },
                { "ColorText", "Text" },
                { "ColorTextMuted", "TextMuted" },
                { "ColorTextSubtle", "TextSubtle" }
            };

            foreach (var kv in formAliases)
            {
                FieldInfo fiForm = mainFormType.GetField(kv.Key, BindingFlags.Public | BindingFlags.Static);
                FieldInfo fiTheme = themeType.GetField(kv.Value, BindingFlags.Public | BindingFlags.Static);
                if (fiForm == null)
                {
                    Console.WriteLine("FAIL: MainForm missing alias field " + kv.Key);
                    failures++;
                    continue;
                }
                Color cForm = (Color)fiForm.GetValue(null);
                Color cTheme = (Color)fiTheme.GetValue(null);
                if (cForm.ToArgb() != cTheme.ToArgb())
                {
                    Console.WriteLine("FAIL: MainForm." + kv.Key + " does not match CyberObsidianTheme." + kv.Value);
                    failures++;
                }
            }

            // 3. Instantiate Custom Controls, Dialogs, and Panels with Valid Constructors
            try
            {
                using (var dummyForm = new Form())
                {
                    using (var c1 = new ModernDarkScrollBar()) { c1.CreateControl(); }
                    using (var c2 = new ModernButton("Test", 0, 0, 100, 30, CyberObsidianTheme.Card, CyberObsidianTheme.Text)) { c2.CreateControl(); }
                    using (var c3 = new ModernCard("TestCard", 0, 0, 200, 150)) { c3.CreateControl(); }
                    using (var c4 = new ModernSwitch("Title", "Sub", 0, 0, 120, 40)) { c4.CreateControl(); }
                    using (var c5 = new ModernSlider(0, 100, 50, CyberObsidianTheme.Primary, CyberObsidianTheme.Accent)) { c5.CreateControl(); }
                    using (var c6 = new ModernSegmentedMode("T1", "T2", 0, 0, 150, 36)) { c6.CreateControl(); }
                    using (var c7 = new TelemetryBadge("FPS", "60", 0, 0, 80, 40)) { c7.CreateControl(); }
                    using (var c8 = new QuickChip("Chip", 0, 0, 80, 30)) { c8.CreateControl(); }
                    using (var c9 = new RoundedPanel(100, 100, 8, CyberObsidianTheme.Card, CyberObsidianTheme.CardBorder)) { c9.CreateControl(); }
                    using (var c10 = new DoubleBufferedPanel()) { c10.CreateControl(); }
                    using (var c11 = new ModernComboBox()) { c11.CreateControl(); }
                    using (var c12 = new TabletHeroBanner("Device", "Specs", "Online", CyberObsidianTheme.Success)) { c12.CreateControl(); }
                    using (var c13 = new ModernTitleBar(dummyForm, "Title", "PRO")) { c13.CreateControl(); }
                    using (var c14 = new ModernStudioTabs(0, 0, 300, 40)) { c14.CreateControl(); }
                    using (var c15 = new NotificationCardControl()) { c15.CreateControl(); }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: Control instantiation exception: " + ex);
                failures++;
            }

            Console.WriteLine("Failures: " + failures);
            return failures == 0 ? 0 : 1;
        }
    }
}
'@

$probeCsPath = "$ProjectRoot\desktop\ThemeProbe.cs"
$probeExePath = "$ProjectRoot\desktop\ThemeProbe.exe"
Set-Content -Path $probeCsPath -Value $probeCode

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$compileProbe = Start-Process -FilePath $cscPath -ArgumentList "/nologo /target:exe /out:`"$probeExePath`" /r:`"$exePath`" `"$probeCsPath`"" -Wait -PassThru -NoNewWindow
Assert-Test ($compileProbe.ExitCode -eq 0) "Theme & Control Probe compiled successfully against MobiDesk.exe"

if (Test-Path $probeExePath) {
    $runProbe = Start-Process -FilePath $probeExePath -WorkingDirectory "$ProjectRoot\desktop" -Wait -PassThru -NoNewWindow
    Assert-Test ($runProbe.ExitCode -eq 0) `
        "All 15 CyberObsidianTheme constants and 17 MainForm aliases verified via Reflection (0 mismatches)" `
        "ExitCode=$($runProbe.ExitCode)"

    # Clean up probe files
    Remove-Item -Force $probeCsPath, $probeExePath -ErrorAction SilentlyContinue
}


# ==============================================================================
# SECTION 3: Test Log Dual-Write Consistency
# ==============================================================================
Write-Host "`n--- SECTION 3: Test Log Dual-Write Consistency ---" -ForegroundColor Yellow

$desktopLog = "$ProjectRoot\desktop\test_results.log"
$rootLog = "$ProjectRoot\test_results.log"

function Verify-DualLog([string]$workingDir, [string]$label) {
    if (Test-Path $desktopLog) { Remove-Item -Force $desktopLog }
    if (Test-Path $rootLog) { Remove-Item -Force $rootLog }

    $p = Start-Process -FilePath $exePath -ArgumentList "--test" -WorkingDirectory $workingDir -NoNewWindow -Wait -PassThru
    Assert-Test ($p.ExitCode -eq 0) "[$label] MobiDesk.exe --test exited 0"

    $existsDesktop = Test-Path $desktopLog
    $existsRoot = Test-Path $rootLog
    Assert-Test ($existsDesktop -and $existsRoot) "[$label] Both desktop/test_results.log and root test_results.log created"

    if ($existsDesktop -and $existsRoot) {
        $desktopInfo = Get-Item $desktopLog
        $rootInfo = Get-Item $rootLog
        $hDesktop = (Get-FileHash $desktopLog -Algorithm SHA256).Hash
        $hRoot = (Get-FileHash $rootLog -Algorithm SHA256).Hash

        $sameLen = ($desktopInfo.Length -eq $rootInfo.Length)
        $sameHash = ($hDesktop -eq $hRoot)
        $rawDesktop = Get-Content $desktopLog -Raw
        $has155 = $rawDesktop -match "RESULTS: Total=155, Passed=155, Failed=0"

        Assert-Test ($sameLen -and $sameHash -and $has155) `
            "[$label] Dual-write SHA256 & byte size match exactly with 155 PASS" `
            "SHA256=$hDesktop, Bytes=$($desktopInfo.Length)"
    }
}

# 3.1 Run from Project Root
Verify-DualLog $ProjectRoot "Run from Project Root"

# 3.2 Run from desktop/ directory
Verify-DualLog "$ProjectRoot\desktop" "Run from desktop/ directory"

# 3.3 Run from Temp directory
Verify-DualLog $env:TEMP "Run from Temp directory"


# ==============================================================================
# SUMMARY & VERDICT
# ==============================================================================
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "   ADVERSARIAL STRESS TEST SUMMARY                        " -ForegroundColor Cyan
Write-Host "   Total Tests: $($Passed + $Failed) | Passed: $Passed | Failed: $Failed" -ForegroundColor $(if ($Failed -eq 0) {"Green"} else {"Red"})
Write-Host "==========================================================" -ForegroundColor Cyan

foreach ($t in $TestResults) {
    $color = if ($t.Status -eq "PASS") { "Green" } else { "Red" }
    Write-Host "  [$($t.Status)] $($t.Test) $(if ($t.Details) {"- $($t.Details)"})" -ForegroundColor $color
}

if ($Failed -gt 0) {
    Write-Host "`nVERDICT: REQUEST_CHANGES" -ForegroundColor Red
    exit 1
} else {
    Write-Host "`nVERDICT: APPROVE" -ForegroundColor Green
    exit 0
}
