# InfraTown automated playtest: opens a scene (default "Assets/Scenes/1park2.unity", -Scene to change)
# in play mode (batch mode, with graphics)
# and runs the scenario in Assets/Tests/Editor/PlaytestRunner.cs.
#
#   powershell -ExecutionPolicy Bypass -File Tools/run-playtest.ps1
#
# Output: Logs/Playtest/report.md + screenshots (*.png). The scene is never saved.
# Like run-tests.ps1, it runs in a mirrored copy when the editor has the project open.
# Exit code: 0 = all checks passed, 2 = some checks failed, other = could not run.

param(
    [string]$Scene = "Assets/Scenes/1park2.unity",
    [string]$UnityPath = $env:UNITY_EDITOR,
    [int]$TimeoutMinutes = 20,
    [switch]$ForceCopy
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$version = ((Get-Content (Join-Path $root "ProjectSettings/ProjectVersion.txt") | Select-String "m_EditorVersion:") -split ":\s*")[1].Trim()
if (-not $UnityPath) { $UnityPath = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
if (-not (Test-Path $UnityPath)) { Write-Host "Unity $version not found: $UnityPath" -ForegroundColor Red; exit 1 }

function Test-EditorOpen {
    $lock = Join-Path $root "Temp/UnityLockfile"
    if (-not (Test-Path $lock)) { return $false }
    try { $fs = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None'); $fs.Close(); return $false } catch { return $true }
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

Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like "*$version*VBCSCompiler.dll*" -and -not (Get-Process -Id $_.ParentProcessId -ErrorAction SilentlyContinue) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

$outDir = Join-Path $root "Logs/Playtest"
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
New-Item -ItemType Directory -Force $outDir | Out-Null
$logPath = Join-Path $root "Logs/Playtest.log"

$unityArgs = @(
    "-batchmode",
    "-projectPath", "`"$projectPath`"",
    "-executeMethod", "InfraTown.Tests.Playtest.PlaytestRunner.Run",
    "-playtestOut", "`"$outDir`"",
    "-playtestScene", "`"$Scene`"",
    "-logFile", "`"$logPath`""
)

Write-Host "Running playtest with Unity $version (timeout $TimeoutMinutes min) ..."
$proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -PassThru -NoNewWindow
if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    Write-Host "Timed out -> stopping Unity" -ForegroundColor Red
    Stop-Process -Id $proc.Id -Force
    exit 1
}

$report = Join-Path $outDir "report.md"
if (-not (Test-Path $report)) {
    Write-Host "No report (Unity exit code $($proc.ExitCode)). See $logPath" -ForegroundColor Red
    Select-String -Path $logPath -Pattern "error CS|Exception|Scripts have compiler errors" | Select-Object -First 15 | ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
    exit 1
}

$lines = Get-Content $report -Encoding UTF8
$lines | Select-Object -First 5 | ForEach-Object { Write-Host $_ }
$lines | Where-Object { $_ -like "*❌*" } | ForEach-Object { Write-Host $_ -ForegroundColor Red }
Write-Host "Report: $report"

# Start-Process may not surface Unity's exit code reliably, so judge from the report
$summary = $lines | Where-Object { $_ -match "(\d+) 통과 / (\d+) 실패" } | Select-Object -First 1
if ($summary -and $Matches[2] -eq "0") { exit 0 }
exit 2
