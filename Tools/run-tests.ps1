# InfraTown test runner (Unity Test Framework, batch mode)
#
#   powershell -ExecutionPolicy Bypass -File Tools/run-tests.ps1
#   powershell -ExecutionPolicy Bypass -File Tools/run-tests.ps1 -Filter EconomyTests
#
# If the Unity editor already has this project open, batch mode cannot open the same folder,
# so the script mirrors Assets/Packages/ProjectSettings into a cache folder and runs there.
# The first run in that cache imports everything (a few minutes); later runs are much faster.
#
# Exit code: 0 = all passed, 2 = some tests failed, other = could not run.

param(
    [string]$Platform = "EditMode",
    [string]$Filter = "",
    [string]$UnityPath = $env:UNITY_EDITOR,
    [switch]$ForceCopy
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$version = ((Get-Content (Join-Path $root "ProjectSettings/ProjectVersion.txt") | Select-String "m_EditorVersion:") -split ":\s*")[1].Trim()
if (-not $UnityPath) {
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
}
if (-not (Test-Path $UnityPath)) {
    Write-Host "Unity $version not found: $UnityPath (set UNITY_EDITOR or pass -UnityPath)" -ForegroundColor Red
    exit 1
}

function Test-EditorOpen {
    $lock = Join-Path $root "Temp/UnityLockfile"
    if (-not (Test-Path $lock)) { return $false }
    try {
        $fs = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $fs.Close()
        return $false
    } catch {
        return $true
    }
}

$projectPath = $root
if ($ForceCopy -or (Test-EditorOpen)) {
    $projectPath = Join-Path $env:LOCALAPPDATA "InfraTownTestProject"
    Write-Host "Unity editor is using the project -> running in a mirrored copy: $projectPath" -ForegroundColor Yellow
    foreach ($dir in "Assets", "Packages", "ProjectSettings") {
        robocopy (Join-Path $root $dir) (Join-Path $projectPath $dir) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { Write-Host "robocopy failed for $dir ($LASTEXITCODE)" -ForegroundColor Red; exit 1 }
    }
}

# A Roslyn compiler server left behind by a previous batch run (its parent process is gone)
# makes the next batch compile fail with "error CS2011 ... .rsp". Stop only those orphans.
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like "*$version*VBCSCompiler.dll*" -and -not (Get-Process -Id $_.ParentProcessId -ErrorAction SilentlyContinue) } |
    ForEach-Object {
        Write-Host "Stopping orphaned compiler server (pid $($_.ProcessId))" -ForegroundColor DarkYellow
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

$logDir = Join-Path $root "Logs"
New-Item -ItemType Directory -Force $logDir | Out-Null
$resultsPath = Join-Path $logDir "TestResults-$Platform.xml"
$logPath = Join-Path $logDir "TestRun-$Platform.log"
Remove-Item $resultsPath, $logPath -ErrorAction SilentlyContinue

$unityArgs = @(
    "-batchmode", "-nographics",
    "-projectPath", "`"$projectPath`"",
    "-runTests", "-testPlatform", $Platform,
    "-testResults", "`"$resultsPath`"",
    "-logFile", "`"$logPath`""
)
if ($Filter) { $unityArgs += @("-testFilter", "`"$Filter`"") }

Write-Host "Running $Platform tests with Unity $version ..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
$sw.Stop()

if (-not (Test-Path $resultsPath)) {
    Write-Host "No test results (Unity exit code $($proc.ExitCode)). Compile errors?" -ForegroundColor Red
    if (Test-Path $logPath) {
        Select-String -Path $logPath -Pattern "error CS|Scripts have compiler errors|another Unity instance" |
            Select-Object -First 20 | ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
    }
    Write-Host "Full log: $logPath"
    exit 1
}

[xml]$xml = Get-Content $resultsPath -Encoding UTF8
$run = $xml.'test-run'
Write-Host ("Total {0} | Passed {1} | Failed {2} | Skipped {3}  ({4:N0}s)" -f $run.total, $run.passed, $run.failed, $run.skipped, $sw.Elapsed.TotalSeconds)

$failed = $xml.SelectNodes("//test-case[@result='Failed']")
foreach ($case in $failed) {
    Write-Host "FAIL $($case.fullname)" -ForegroundColor Red
    $msg = $case.failure.message.InnerText
    if ($msg) { Write-Host "     $($msg.Trim())" -ForegroundColor DarkRed }
}

if ([int]$run.failed -gt 0) { exit 2 }
Write-Host "All tests passed." -ForegroundColor Green
exit 0
