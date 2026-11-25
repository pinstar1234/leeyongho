# InfraTown 빌드 테스트: 실제 Windows 플레이어를 빌드한 뒤, 빌드된 게임 안에서 자동 플레이 테스트
# (Assets/Script/Testing/BuildPlaytestRunner.cs)를 여러 번(회차마다 다른 seed) 돌리고 모든 회차의 로그를 남긴다.
#
#   powershell -ExecutionPolicy Bypass -File Tools/run-build-test.ps1 -Change feature   # 새 기능 추가: 10회
#   powershell -ExecutionPolicy Bypass -File Tools/run-build-test.ps1 -Change fix       # 기존 기능 수정: 3회
#   powershell -ExecutionPolicy Bypass -File Tools/run-build-test.ps1 -SkipBuild -Repeat 1 -Seed 71499   # 한 회차 재현
#
# 결과 (실행할 때마다 새 폴더, 사용자 요청 2026-11-24):
#   Builds/BuildTestResults/<yyyyMMdd_HHmmss>/
#     summary.txt      전체 요약 (PASS/FAIL, 회차 통과 수, 실패한 검사·seed, 재현 명령, 회차별 상세)
#     build.log        Unity 빌드 로그 (-SkipBuild면 없음)
#     run01/ run02/ …  회차별: run.txt(이 회차 O/X 상세), result.txt(게임이 쓴 원본), player.log(플레이어 로그)
#   가장 최근 요약은 Logs/BuildTest/report.txt에도 복사한다.
# 빌드: Logs/BuildTest/Player/InfraTown.exe (테스트 전용 빌드 — INFRATOWN_BUILD_TEST 정의, Build Settings는 바꾸지 않음)
# 에디터가 프로젝트를 열고 있으면 run-tests.ps1처럼 %LOCALAPPDATA%\InfraTownTestProject 미러에서 빌드한다.
# 종료 코드: 0 = 모든 회차 통과, 2 = 실패한 회차 있음, 1 = 빌드·실행 불가

param(
    [ValidateSet("feature", "fix")]
    [string]$Change = "feature",      # feature = 새 기능 추가(10회), fix = 기존 기능 수정(3회)
    [int]$Repeat = 0,                 # 0이면 -Change에 따라 10 또는 3
    [int]$Seed = 0,                   # 0이면 무작위 시작 seed, 회차마다 +1
    [string]$Scenes = "Assets/Scenes/1park2.unity",   # 빌드에 넣을 씬 (; 로 여러 개, 첫 씬에서 테스트)
    [switch]$SkipBuild,               # 이미 만든 빌드로 테스트만
    [switch]$ForceCopy,
    [int]$BuildTimeoutMinutes = 40,
    [int]$RunTimeoutMinutes = 10,
    [string]$UnityPath = $env:UNITY_EDITOR
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "Logs/BuildTest"
$playerDir = Join-Path $outDir "Player"
$exe = Join-Path $playerDir "InfraTown.exe"
$modeName = if ($Change -eq "feature") { "새 기능 모드" } else { "기존 기능 수정 모드" }
if ($Repeat -le 0) { $Repeat = if ($Change -eq "feature") { 10 } else { 3 } }
if ($Seed -le 0) { $Seed = Get-Random -Minimum 10000 -Maximum 90000 }
$group = "gameplay/" + [System.IO.Path]::GetFileNameWithoutExtension(($Scenes -split ";")[0].Trim())

# 실행마다 새 결과 폴더: Builds/BuildTestResults/<실행 시각>/run01, run02, ..., summary.txt
$startedAt = Get-Date
$stamp = $startedAt.ToString("yyyyMMdd_HHmmss")
$resultDir = Join-Path $root "Builds/BuildTestResults/$stamp"

New-Item -ItemType Directory -Force $outDir | Out-Null
New-Item -ItemType Directory -Force $resultDir | Out-Null

# 어떤 코드로 돌렸는지 요약에 남김 (git이 없으면 생략)
$gitInfo = ""
try {
    $branch = (& git -C $root rev-parse --abbrev-ref HEAD 2>$null)
    $commit = (& git -C $root rev-parse --short HEAD 2>$null)   # 커밋 제목은 콘솔 인코딩에 따라 한글이 깨질 수 있어 해시만
    if ($branch) { $gitInfo = "$branch / $commit" }
} catch { $gitInfo = "" }

$utf8Bom = New-Object System.Text.UTF8Encoding($true)

# ───────────────────────── 1. 빌드 ─────────────────────────
if (-not $SkipBuild) {
    $version = ((Get-Content (Join-Path $root "ProjectSettings/ProjectVersion.txt") | Select-String "m_EditorVersion:") -split ":\s*")[1].Trim()
    if (-not $UnityPath) { $UnityPath = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
    if (-not (Test-Path $UnityPath)) { Write-Host "Unity $version 없음: $UnityPath" -ForegroundColor Red; exit 1 }

    $lock = Join-Path $root "Temp/UnityLockfile"
    $editorOpen = $false
    if (Test-Path $lock) { try { $fs = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None'); $fs.Close() } catch { $editorOpen = $true } }

    $projectPath = $root
    if ($ForceCopy -or $editorOpen) {
        $projectPath = Join-Path $env:LOCALAPPDATA "InfraTownTestProject"
        Write-Host "에디터가 프로젝트를 사용 중이거나 -ForceCopy -> 미러에서 빌드: $projectPath" -ForegroundColor Yellow
        foreach ($dir in "Assets", "Packages", "ProjectSettings") {
            robocopy (Join-Path $root $dir) (Join-Path $projectPath $dir) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { Write-Host "robocopy 실패: $dir ($LASTEXITCODE)" -ForegroundColor Red; exit 1 }
        }
    }

    # 이전 배치 실행이 남긴 컴파일러 서버 정리 (run-tests.ps1과 같음)
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object { $_.CommandLine -like "*$version*VBCSCompiler.dll*" -and -not (Get-Process -Id $_.ParentProcessId -ErrorAction SilentlyContinue) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    $buildLog = Join-Path $resultDir "build.log"
    $buildArgs = @(
        "-batchmode", "-quit",
        "-projectPath", "`"$projectPath`"",
        "-buildTarget", "Win64",
        "-executeMethod", "InfraTown.Tests.Build.BuildTestBuilder.Build",
        "-buildTestOut", "`"$playerDir`"",
        "-buildTestScenes", "`"$Scenes`"",
        "-logFile", "`"$buildLog`""
    )
    Write-Host "빌드 중 (Unity $version, 최대 $BuildTimeoutMinutes 분) ..."
    $buildStart = Get-Date
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        if (Test-Path $exe) { Remove-Item $exe -Force }
        $proc = Start-Process -FilePath $UnityPath -ArgumentList $buildArgs -PassThru -NoNewWindow
        if (-not $proc.WaitForExit($BuildTimeoutMinutes * 60 * 1000)) {
            Stop-Process -Id $proc.Id -Force
            Write-Host "빌드 시간 초과" -ForegroundColor Red; exit 1
        }
        $licenseFail = (-not (Test-Path $exe)) -and (Test-Path $buildLog) -and (Select-String -Path $buildLog -Pattern "LicensingClient doesn't exist" -Quiet)
        if (-not $licenseFail) { break }
        Write-Host "Unity 라이선스 클라이언트 연결 실패 -> 한 번 더 시도" -ForegroundColor DarkYellow
    }
    if (-not (Test-Path $exe)) {
        Write-Host "빌드 실패. 로그: $buildLog" -ForegroundColor Red
        Select-String -Path $buildLog -Pattern "error CS|BuildFailedException|\[BuildTest\]|Build Finished, Result" |
            Select-Object -First 15 | ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
        exit 1
    }
    Write-Host ("빌드 완료 ({0:F0}초): $exe" -f ((Get-Date) - $buildStart).TotalSeconds) -ForegroundColor Green
}
elseif (-not (Test-Path $exe)) {
    Write-Host "빌드가 없음: $exe (-SkipBuild 없이 먼저 빌드하세요)" -ForegroundColor Red; exit 1
}

# ───────────────────────── 2. 반복 실행 ─────────────────────────
function Format-RunDetail($r) {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add(("== {0} 회차 (seed {1}) {2} ==" -f $r.Index, $r.Seed, $(if ($r.Ok) { "PASS" } else { "FAIL" })))
    $lines.Add(("[{0}] {1}  {2}/{3} 통과  (seed {4}, {5:F1}초)" -f $(if ($r.Ok) { "PASS" } else { "FAIL" }), $group, $r.Passed, $r.Total, $r.Seed, $r.Seconds))
    if ($r.Problem) { $lines.Add("  ! $($r.Problem)") }
    foreach ($t in $r.Tests) {
        if ($t.Pass) { $lines.Add(("  O {0} ({1}초)" -f $t.Name, $t.Seconds)) }
        else { $lines.Add(("  X {0} - {1} ({2}초)" -f $t.Name, $t.Message, $t.Seconds)) }
    }
    return $lines
}

$runs = @()
for ($i = 1; $i -le $Repeat; $i++) {
    $s = $Seed + $i - 1
    $runName = "run{0:D2}" -f $i
    $runDir = Join-Path $resultDir $runName
    New-Item -ItemType Directory -Force $runDir | Out-Null
    $resultFile = Join-Path $runDir "result.txt"
    $playerLog = Join-Path $runDir "player.log"
    if (Test-Path $resultFile) { Remove-Item $resultFile -Force }

    Write-Host ("[{0}/{1}] seed {2} 실행 중 ..." -f $i, $Repeat, $s)
    $runArgs = @("-batchmode", "-nographics", "-infratownTest", "-infratownSeed", "$s",
                 "-infratownTestOut", "`"$resultFile`"", "-logFile", "`"$playerLog`"")
    $start = Get-Date
    $proc = Start-Process -FilePath $exe -ArgumentList $runArgs -PassThru -NoNewWindow
    $timedOut = -not $proc.WaitForExit($RunTimeoutMinutes * 60 * 1000)
    if ($timedOut) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    $elapsed = ((Get-Date) - $start).TotalSeconds

    $tests = @(); $ended = $false; $runSeconds = $elapsed
    if (Test-Path $resultFile) {
        foreach ($line in (Get-Content $resultFile -Encoding UTF8)) {
            $f = $line -split "`t"
            if ($f[0] -eq "TEST") { $tests += [pscustomobject]@{ Name = $f[1]; Pass = ($f[2] -eq "O"); Seconds = $f[3]; Message = $f[4] } }
            elseif ($f[0] -eq "END") { $ended = $true; $runSeconds = [double]$f[3] }
        }
    }

    $problem = $null
    if ($timedOut) { $problem = "시간 초과 ($RunTimeoutMinutes 분) - 마지막 검사 다음에서 멈춤" }
    elseif (-not $ended) { $problem = "게임이 테스트를 끝내지 못하고 종료됨 (exit code $($proc.ExitCode)) - $runName/player.log 확인" }

    $passed = @($tests | Where-Object { $_.Pass }).Count
    $run = [pscustomobject]@{
        Index = $i; Seed = $s; Tests = $tests; Passed = $passed; Total = $tests.Count
        Ok = ($null -eq $problem -and $passed -eq $tests.Count -and $tests.Count -gt 0)
        Problem = $problem; Seconds = $runSeconds; Name = $runName
    }
    $runs += $run

    # 회차 폴더에 이 회차 상세 (회차가 끝날 때마다 바로 써서, 중간에 멈춰도 앞 회차 결과가 남음)
    $runLines = @(Format-RunDetail $run)
    $runLines += ""
    $runLines += "재현: powershell -ExecutionPolicy Bypass -File `"Tools/run-build-test.ps1`" -SkipBuild -Repeat 1 -Seed $s"
    [System.IO.File]::WriteAllLines((Join-Path $runDir "run.txt"), [string[]]$runLines, $utf8Bom)
    $color = if ($runs[-1].Ok) { "Green" } else { "Red" }
    Write-Host ("        {0}/{1} 통과 ({2:F1}초){3}" -f $passed, $tests.Count, $runSeconds, $(if ($problem) { " - $problem" } else { "" })) -ForegroundColor $color
}

# ───────────────────────── 3. 로그 ─────────────────────────
$okRuns = @($runs | Where-Object { $_.Ok }).Count
$allOk = $okRuns -eq $Repeat
$sb = New-Object System.Collections.Generic.List[string]
$sb.Add(("== 빌드 플레이 테스트 {0} ({1}, {2}): {3}/{4} 회 통과 ==" -f $(if ($allOk) { "PASS" } else { "FAIL" }), $group, $modeName, $okRuns, $Repeat))
$sb.Add(("실행: {0} ~ {1}   결과 폴더: Builds/BuildTestResults/{2}" -f $startedAt.ToString("yyyy-MM-dd HH:mm:ss"), (Get-Date).ToString("HH:mm:ss"), $stamp))
if ($gitInfo) { $sb.Add("코드: $gitInfo") }
$sb.Add(("빌드: {0}" -f $(if ($SkipBuild) { "생략 (-SkipBuild, 이전 빌드 사용)" } else { "새로 빌드 (build.log)" })))
$sb.Add("")
$sb.Add(("-- 회차별 결과 --"))
foreach ($r in $runs) {
    $sb.Add(("  {0}  {1}  seed {2}  {3}/{4} 통과{5}" -f $r.Name, $(if ($r.Ok) { "PASS" } else { "FAIL" }), $r.Seed, $r.Passed, $r.Total, $(if ($r.Problem) { " - " + $r.Problem } else { "" })))
}
$sb.Add("")
$sb.Add(("-- 실패한 검사 (실패 횟수/{0}) --" -f $Repeat))

$failures = @{}
$order = New-Object System.Collections.Generic.List[string]
foreach ($r in $runs) {
    foreach ($t in ($r.Tests | Where-Object { -not $_.Pass })) {
        if (-not $failures.ContainsKey($t.Name)) { $failures[$t.Name] = New-Object System.Collections.Generic.List[int]; $order.Add($t.Name) }
        $failures[$t.Name].Add($r.Seed)
    }
    if ($r.Problem) {
        $key = "(실행 중단) " + $r.Problem
        if (-not $failures.ContainsKey($key)) { $failures[$key] = New-Object System.Collections.Generic.List[int]; $order.Add($key) }
        $failures[$key].Add($r.Seed)
    }
}
if ($order.Count -eq 0) { $sb.Add("  (없음)") }
foreach ($name in $order) {
    $seeds = $failures[$name]
    $sb.Add(("  {0}/{1}  {2}: {3}   (seed {4})" -f $seeds.Count, $Repeat, ($group -split "/")[1], $name, ($seeds -join ", ")))
}
$sb.Add("")
$sb.Add("재현: powershell -ExecutionPolicy Bypass -File `"Tools/run-build-test.ps1`" -SkipBuild -Repeat 1 -Seed <seed>")

foreach ($r in $runs) {
    $sb.Add("")
    $sb.Add(("-- {0} 회차 상세{1} ({2}/) --" -f $r.Index, $(if ($r.Ok) { "" } else { " (실패)" }), $r.Name))
    foreach ($line in (Format-RunDetail $r)) { $sb.Add($line) }
}

$summary = Join-Path $resultDir "summary.txt"
[System.IO.File]::WriteAllLines($summary, $sb, $utf8Bom)
Copy-Item $summary (Join-Path $outDir "report.txt") -Force   # 가장 최근 요약 (예전 경로)
Write-Host ""
$sb | Select-Object -First (10 + $Repeat + $order.Count) | ForEach-Object { Write-Host $_ }
Write-Host "결과 폴더: $resultDir"
Write-Host "요약: $summary"
if ($allOk) { exit 0 } else { exit 2 }
