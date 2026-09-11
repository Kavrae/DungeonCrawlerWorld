[CmdletBinding()]
param(
    [string]$ExePath = "DungeonCrawlerWorld/bin/Debug/net10.0/DungeonCrawlerWorld.exe",
    [string]$DiagnosticsDirectory = "Log/diagnostics",
    [string]$OutputDir = "Log/phase-benchmarks",
    # Frame cost depends on what the world is doing, so a run is only comparable against earlier
    # runs of the same seed AND the same frame range -- both are checked against what the game
    # itself stamps into the report, and both select the comparison baseline.
    [int]$Seed = 1,
    # Simulation frames measured, StartFrame inclusive to EndFrame exclusive. The frames before
    # StartFrame are warm-up (JIT, first fights); 3000 frames is 50 simulated seconds.
    [long]$StartFrame = 600,
    [long]$EndFrame = 3600,
    [int]$TimeoutSeconds = 300,
    [double]$RegressionPercent = 20,
    [double]$RegressionMinMsPerFrame = 0.02,
    # A run whose wall-clock time exceeds FrameCount/60 seconds by more than this fraction fell
    # behind, so FNA ran several Updates per Draw -- Draw costs per frame aren't comparable then.
    [double]$FellBehindTolerance = 0.10
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Game exe not found: $ExePath. Run 'dotnet build DungeonCrawlerWorld.sln' first."
}

# Refuse rather than kill: an already-running game may be someone's session, and two instances
# would compete for the CPU and skew both.
$existing = @(Get-Process -Name DungeonCrawlerWorld -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
    throw "DungeonCrawlerWorld is already running (pid $($existing.Id -join ', ')). Close it before benchmarking."
}

$gameArguments = @("--diagnostics=frame", "--seed=$Seed", "--benchmark-frames=$StartFrame-$EndFrame")
Write-Host "Launching $ExePath $($gameArguments -join ' ')"
$process = Start-Process -FilePath $ExePath -ArgumentList $gameArguments -PassThru

$benchmarkFile = $null
try {
    # The game writes benchmark-<timestamp>-<pid>.json the moment frame EndFrame begins; matching
    # on this process's pid means a stale file from an earlier run can never be picked up.
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($null -eq $benchmarkFile) {
        $benchmarkFile = Get-ChildItem -Path $DiagnosticsDirectory -Filter "benchmark-*-$($process.Id).json" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $benchmarkFile) { break }

        if ($process.HasExited) {
            throw "The game exited (code $($process.ExitCode)) before writing its benchmark report."
        }
        if ((Get-Date) -gt $deadline) {
            throw "No benchmark report after ${TimeoutSeconds}s. If the window is open, the game may be paused or showing a blocking notification -- simulation frames only advance while unpaused."
        }
        Start-Sleep -Seconds 2
    }

    # Written with a single File.WriteAllText; a short pause keeps a read from racing the write.
    Start-Sleep -Milliseconds 500
    $report = Get-Content -LiteralPath $benchmarkFile.FullName -Raw | ConvertFrom-Json
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(10000) | Out-Null
    }
}

if ([int]$report.RandomSeed -ne $Seed -or [long]$report.StartFrame -ne $StartFrame -or [long]$report.EndFrame -ne $EndFrame) {
    throw "$($benchmarkFile.Name) is seed $($report.RandomSeed), frames $($report.StartFrame)-$($report.EndFrame); expected seed $Seed, frames $StartFrame-$EndFrame."
}

$expectedWallClockMs = $report.FrameCount / 60.0 * 1000.0
$fellBehind = $report.WallClockMilliseconds -gt $expectedWallClockMs * (1 + $FellBehindTolerance)

$perFrame = @{}
foreach ($category in @("Update", "Draw")) {
    $section = $report.$category
    if (-not $section) { continue }
    foreach ($groupProperty in $section.PSObject.Properties) {
        foreach ($item in $groupProperty.Value) {
            $perFrame["$category.$($groupProperty.Name).$($item.Name)"] = [double]$item.MillisecondsPerFrame
        }
    }
}

if ($perFrame.Count -eq 0) {
    throw "$($benchmarkFile.Name) recorded nothing inside frames $StartFrame-$EndFrame."
}

$averages = [ordered]@{}
foreach ($key in ($perFrame.Keys | Sort-Object { -$perFrame[$_] })) {
    $averages[$key] = [math]::Round($perFrame[$key], 4)
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$commit = $null
$branch = $null
try { $commit = (git rev-parse --short HEAD 2>$null) } catch {}
try { $branch = (git branch --show-current 2>$null) } catch {}

$result = [ordered]@{
    timestampUtc          = (Get-Date).ToUniversalTime().ToString("o")
    gitCommit             = $commit
    gitBranch             = $branch
    sampling              = "frame-range"
    unit                  = "ms/frame"
    randomSeed            = $Seed
    startFrame            = $StartFrame
    endFrame              = $EndFrame
    wallClockMilliseconds = [math]::Round([double]$report.WallClockMilliseconds, 1)
    fellBehind            = $fellBehind
    sourceFile            = $benchmarkFile.FullName
    systems               = $averages
}

$outFile = Join-Path $OutputDir ("{0}.json" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss"))
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $outFile -Encoding utf8

Write-Host "Saved benchmark: $outFile"
Write-Host ("Seed {0}, simulation frames {1}-{2}, {3:N1}s wall clock (real time would be {4:N1}s)" -f $Seed, $StartFrame, $EndFrame, ($report.WallClockMilliseconds / 1000), ($expectedWallClockMs / 1000))
if ($fellBehind) {
    Write-Host "WARNING: the run fell behind real time -- Update costs are still comparable, Draw costs per frame are not."
}
Write-Host ""

# Only an earlier frame-range run of the same seed and range is a valid comparison. Wall-clock
# runs (no "sampling" field) sampled a different, unaligned workload and never match.
$outFullPath = (Resolve-Path -LiteralPath $outFile).Path
$previousFile = $null
$previous = $null
$skipped = 0
foreach ($candidate in (Get-ChildItem -Path $OutputDir -Filter "*.json" | Where-Object { $_.FullName -ne $outFullPath } | Sort-Object Name -Descending)) {
    $candidateReport = Get-Content -LiteralPath $candidate.FullName -Raw | ConvertFrom-Json
    if ($candidateReport.sampling -eq "frame-range" -and
        [int]$candidateReport.randomSeed -eq $Seed -and
        [long]$candidateReport.startFrame -eq $StartFrame -and
        [long]$candidateReport.endFrame -eq $EndFrame) {
        $previousFile = $candidate
        $previous = $candidateReport
        break
    }
    $skipped++
}

if ($null -eq $previousFile) {
    Write-Host "No previous seed-$Seed, frames $StartFrame-$EndFrame benchmark in $OutputDir -- this is the first recorded baseline for it."
    if ($skipped -gt 0) {
        Write-Host "($skipped earlier file(s) skipped: a different seed, frame range or sampling method, so not comparable.)"
    }
    Write-Host ""
    Write-Host ("{0,-55} {1,10}" -f "System", "ms/frame")
    foreach ($name in $averages.Keys) {
        Write-Host ("{0,-55} {1,10:N4}" -f $name, $averages[$name])
    }
    exit 0
}

Write-Host "Comparing against previous run: $($previousFile.Name) (commit $($previous.gitCommit), $($previous.timestampUtc))"
if ($skipped -gt 0) {
    Write-Host "($skipped newer file(s) skipped: a different seed, frame range or sampling method, so not comparable.)"
}
if ($previous.fellBehind) {
    Write-Host "WARNING: the previous run fell behind real time -- its Draw costs per frame are not comparable."
}
Write-Host ""

$prevSystems = @{}
if ($previous.systems) {
    $previous.systems.PSObject.Properties | ForEach-Object { $prevSystems[$_.Name] = [double]$_.Value }
}

$allCompareNames = @($averages.Keys) + @($prevSystems.Keys) | Select-Object -Unique |
    Sort-Object { if ($averages.Contains($_)) { -$averages[$_] } else { 0 } }

$header = "{0,-53} {1,10} {2,10} {3,10} {4,8}  {5}" -f "System (ms/frame)", "Current", "Previous", "Delta", "Delta%", "Flag"
Write-Host $header
Write-Host ("-" * $header.Length)

$regressions = @()
foreach ($name in $allCompareNames) {
    $cur = if ($averages.Contains($name)) { $averages[$name] } else { 0.0 }
    $prev = if ($prevSystems.ContainsKey($name)) { $prevSystems[$name] } else { 0.0 }
    $delta = $cur - $prev
    $deltaPctStr = "n/a"
    $isRegression = $false
    if ($prev -gt 0) {
        $deltaPct = [math]::Round(($delta / $prev) * 100, 1)
        $deltaPctStr = "$deltaPct%"
        if ($delta -ge $RegressionMinMsPerFrame -and $deltaPct -ge $RegressionPercent) {
            $isRegression = $true
        }
    }
    $flag = if ($isRegression) { "REGRESSION" } else { "" }
    if ($isRegression) { $regressions += $name }
    Write-Host ("{0,-53} {1,10:N4} {2,10:N4} {3,10:N4} {4,8}  {5}" -f $name, $cur, $prev, $delta, $deltaPctStr, $flag)
}

Write-Host ""
if ($regressions.Count -gt 0) {
    Write-Host "Flagged regressions (>=$RegressionPercent% and >=${RegressionMinMsPerFrame}ms/frame increase): $($regressions -join ', ')"
} else {
    Write-Host "No regressions above threshold (>=$RegressionPercent% and >=${RegressionMinMsPerFrame}ms/frame)."
}
