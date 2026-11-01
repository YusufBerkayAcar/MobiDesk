# ==============================================================================
# Challenger Modular M1-1 Adversarial Stress Test Suite
# ==============================================================================
# Focus:
#   1. desktop/build.bat directory invariance (root, desktop/, relative paths)
#   2. MSBuild obj/ and bin/ collision handling and CS0579 avoidance
#   3. desktop/build.bat --test execution and exit code propagation
#   4. Executable execution & path resolution across arbitrary working directories
#   5. CLI exit codes and invalid flag handling
# ==============================================================================

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $ProjectRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   CHALLENGER M1-1 ADVERSARIAL STRESS TEST HARNESS        " -ForegroundColor Cyan
Write-Host "   Root: $ProjectRoot                                     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$Passed = 0
$Failed = 0
$Results = @()

function Assert-Check([bool]$Condition, [string]$TestName, [string]$Details = "") {
    if ($Condition) {
        Write-Host "  [PASS] $TestName $(if ($Details) {"- $Details"})" -ForegroundColor Green
        $script:Passed++
        $script:Results += [PSCustomObject]@{ Test = $TestName; Status = "PASS"; Details = $Details }
    } else {
        Write-Host "  [FAIL] $TestName $(if ($Details) {"- $Details"})" -ForegroundColor Red
        $script:Failed++
        $script:Results += [PSCustomObject]@{ Test = $TestName; Status = "FAIL"; Details = $Details }
    }
}

$desktopBat = "$ProjectRoot\desktop\build.bat"
$csprojPath = "$ProjectRoot\desktop\MobiDesk.csproj"
$exePath    = "$ProjectRoot\desktop\MobiDesk.exe"
$msbuildPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$cscPath     = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

# ==============================================================================
# 1. Stress-Test desktop/build.bat Directory Invariance
# ==============================================================================
Write-Host "`n--- TEST 1: desktop/build.bat Directory Invariance ---" -ForegroundColor Yellow

# 1a. From Root
$p1a = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$desktopBat`"" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Check ($p1a.ExitCode -eq 0) "build.bat succeeds when invoked from repo root" "ExitCode=$($p1a.ExitCode)"

# 1b. From desktop/
$p1b = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"build.bat`"" -WorkingDirectory "$ProjectRoot\desktop" -NoNewWindow -Wait -PassThru
Assert-Check ($p1b.ExitCode -eq 0) "build.bat succeeds when invoked from desktop/ directory" "ExitCode=$($p1b.ExitCode)"

# 1c. From drivers/ using relative path
$p1c = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"`..\desktop\build.bat`"" -WorkingDirectory "$ProjectRoot\drivers" -NoNewWindow -Wait -PassThru
Assert-Check ($p1c.ExitCode -eq 0) "build.bat succeeds when invoked from drivers/ via relative path" "ExitCode=$($p1c.ExitCode)"

# 1d. From assets/icons/ using double-parent relative path
$p1d = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"`..\..\desktop\build.bat`"" -WorkingDirectory "$ProjectRoot\assets\icons" -NoNewWindow -Wait -PassThru
Assert-Check ($p1d.ExitCode -eq 0) "build.bat succeeds when invoked from assets/icons/ via relative path" "ExitCode=$($p1d.ExitCode)"


# ==============================================================================
# 2. Stress-Test MSBuild obj/ and bin/ Collision & CS0579 Avoidance
# ==============================================================================
Write-Host "`n--- TEST 2: MSBuild Collision & CS0579 Auto-Clean ---" -ForegroundColor Yellow

# Clean first
if (Test-Path "$ProjectRoot\desktop\obj") { Remove-Item -Recurse -Force "$ProjectRoot\desktop\obj" }
if (Test-Path "$ProjectRoot\desktop\bin") { Remove-Item -Recurse -Force "$ProjectRoot\desktop\bin" }

# Run MSBuild
$pMsbuild = Start-Process -FilePath $msbuildPath -ArgumentList "`"$csprojPath`" /p:Configuration=Release" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Check ($pMsbuild.ExitCode -eq 0) "MSBuild MobiDesk.csproj succeeds" "ExitCode=$($pMsbuild.ExitCode)"
Assert-Check (Test-Path "$ProjectRoot\desktop\obj\Release") "MSBuild created obj/Release directory"

# Run build.bat immediately without manual cleaning
$pBuildAfterMsbuild = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$desktopBat`"" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Start-Sleep -Milliseconds 200
Assert-Check ($pBuildAfterMsbuild.ExitCode -eq 0) "build.bat succeeds immediately after MSBuild without manual clean" "ExitCode=$($pBuildAfterMsbuild.ExitCode)"
Assert-Check (-not (Test-Path "$ProjectRoot\desktop\obj")) "build.bat auto-cleaned desktop/obj intermediate directory"

# Synthetic CS0579 Collision Challenge:
# Manually create desktop/obj/Debug/ConflictingAttributes.cs to prove CS0579 hazard is real and auto-clean eliminates it
New-Item -ItemType Directory -Force -Path "$ProjectRoot\desktop\obj\Debug" | Out-Null
[System.IO.File]::WriteAllText("$ProjectRoot\desktop\obj\Debug\ConflictingAttributes.cs", "[assembly: System.Reflection.AssemblyTitle(`"CollisionTest`")]")

# Prove that raw csc.exe /recurse: would fail with CS0579
$pRawCsc = Start-Process -FilePath $cscPath -ArgumentList "/nologo /target:winexe /unsafe /out:`"$ProjectRoot\desktop\test_fail.exe`" /recurse:`"$ProjectRoot\desktop\*.cs`"" -NoNewWindow -Wait -PassThru
Assert-Check ($pRawCsc.ExitCode -ne 0) "Raw csc.exe /recurse confirms CS0579 collision when obj contains conflicting attribute" "ExitCode=$($pRawCsc.ExitCode)"

# Recreate the conflicting file (since raw csc didn't delete it)
Assert-Check (Test-Path "$ProjectRoot\desktop\obj\Debug\ConflictingAttributes.cs") "Conflicting file present in obj/Debug"

# Run build.bat with conflicting file present
$pBuildAutoClean = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$desktopBat`"" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Check ($pBuildAutoClean.ExitCode -eq 0) "build.bat auto-cleans obj/ and neutralizes CS0579 collision" "ExitCode=$($pBuildAutoClean.ExitCode)"
Assert-Check (-not (Test-Path "$ProjectRoot\desktop\obj")) "desktop/obj completely eliminated by build.bat"


# ==============================================================================
# 3. Stress-Test desktop/build.bat --test & Exit Code Propagation
# ==============================================================================
Write-Host "`n--- TEST 3: build.bat --test & Exit Code Propagation ---" -ForegroundColor Yellow

$pBuildTest = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$desktopBat --test`"" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Check ($pBuildTest.ExitCode -eq 0) "desktop/build.bat --test runs test suite and exits 0" "ExitCode=$($pBuildTest.ExitCode)"

$pMobiBat = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$ProjectRoot\MobiDesk.bat --test`"" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Check ($pMobiBat.ExitCode -eq 0) "MobiDesk.bat --test forwards to desktop binary and exits 0" "ExitCode=$($pMobiBat.ExitCode)"


# ==============================================================================
# 4. Stress-Test Executable Execution & Path Resolution from Arbitrary Dirs
# ==============================================================================
Write-Host "`n--- TEST 4: Path Resolution Across Arbitrary Working Directories ---" -ForegroundColor Yellow

$testDirs = @(
    @{ Dir = $ProjectRoot; Label = "Project Root" },
    @{ Dir = "$ProjectRoot\desktop"; Label = "desktop/" },
    @{ Dir = "$ProjectRoot\drivers"; Label = "drivers/" },
    @{ Dir = "$ProjectRoot\assets"; Label = "assets/" },
    @{ Dir = $env:TEMP; Label = "System Temp" }
)

foreach ($td in $testDirs) {
    # Delete logs prior to invocation to verify they are properly recreated
    if (Test-Path "$ProjectRoot\desktop\test_results.log") { Remove-Item -Force "$ProjectRoot\desktop\test_results.log" }
    if (Test-Path "$ProjectRoot\test_results.log") { Remove-Item -Force "$ProjectRoot\test_results.log" }

    $pRun = Start-Process -FilePath $exePath -ArgumentList "--test" -WorkingDirectory $td.Dir -NoNewWindow -Wait -PassThru
    Assert-Check ($pRun.ExitCode -eq 0) "[$($td.Label)] MobiDesk.exe --test exits 0" "ExitCode=$($pRun.ExitCode)"

    $logContent = ""
    if (Test-Path "$ProjectRoot\desktop\test_results.log") {
        $logContent = Get-Content "$ProjectRoot\desktop\test_results.log" -Raw
    }
    $has155 = $logContent -match "RESULTS: Total=155, Passed=155, Failed=0"
    Assert-Check ($has155 -eq $true) "[$($td.Label)] 155/155 tests PASS verified in test_results.log"
}


# ==============================================================================
# 5. CLI Flags & Invalid Flag Handling
# ==============================================================================
Write-Host "`n--- TEST 5: CLI Exit Codes and Flag Handling ---" -ForegroundColor Yellow

# 5a. Aliases
$pAlias1 = Start-Process -FilePath $exePath -ArgumentList "--tests" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Check ($pAlias1.ExitCode -eq 0) "--tests alias exits 0" "ExitCode=$($pAlias1.ExitCode)"

$pAlias2 = Start-Process -FilePath $exePath -ArgumentList "--run-tests" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Check ($pAlias2.ExitCode -eq 0) "--run-tests alias exits 0" "ExitCode=$($pAlias2.ExitCode)"

# 5b. Invalid flags alongside --test
$pInvalidWithTest = Start-Process -FilePath $exePath -ArgumentList "--test --unknown-flag-xyz" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Check ($pInvalidWithTest.ExitCode -eq 0) "--test with extraneous invalid flag still executes tests cleanly and exits 0" "ExitCode=$($pInvalidWithTest.ExitCode)"

$pInvalidFirst = Start-Process -FilePath $exePath -ArgumentList "--unrecognized-param-abc --test" -WorkingDirectory $ProjectRoot -NoNewWindow -Wait -PassThru
Assert-Check ($pInvalidFirst.ExitCode -eq 0) "Extraneous invalid flag preceding --test still executes tests cleanly and exits 0" "ExitCode=$($pInvalidFirst.ExitCode)"


# ==============================================================================
# SUMMARY & VERDICT
# ==============================================================================
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "   CHALLENGER M1-1 STRESS TEST SUMMARY                    " -ForegroundColor Cyan
Write-Host "   Total Checks: $($Passed + $Failed) | Passed: $Passed | Failed: $Failed" -ForegroundColor $(if ($Failed -eq 0) {"Green"} else {"Red"})
Write-Host "==========================================================" -ForegroundColor Cyan

if ($Failed -gt 0) {
    Write-Host "`nVERDICT: REQUEST_CHANGES" -ForegroundColor Red
    exit 1
} else {
    Write-Host "`nVERDICT: APPROVE" -ForegroundColor Green
    exit 0
}
