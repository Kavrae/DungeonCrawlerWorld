[CmdletBinding(DefaultParameterSetName = "Run")]
param(
    # Run the simulation with no window (see DungeonCrawlerWorld/HeadlessBenchmark.cs): seconds
    # instead of ~75s per run, no Draw or frame pacing in the numbers, and a determinism check.
    [Parameter(ParameterSetName = "Run")]
    [switch]$Headless,

    # Copy the current build aside as the "before" side of a later -Compare, then stop. Run it
    # after building the code you want to compare against, before making the change.
    [Parameter(ParameterSetName = "SaveBaseline", Mandatory)]
    [switch]$SaveBaseline,

    # Headless A/B: alternate runs of the saved baseline build and the current build in this one
    # session, and compare per-system medians. Machine drift hits both sides equally.
    [Parameter(ParameterSetName = "Compare", Mandatory)]
    [switch]$Compare,

    # Runs per side. 0 = default: 1 windowed, 5 headless, 3 per side for -Compare.
    [int]$Repeat = 0,

    # Which build to measure. Build it first (dotnet build DungeonCrawlerWorld.sln -c Release).
    # Debug and Release results are never compared with each other: Release inlines and optimises
    # tight loops Debug does not, so every system reads cheaper. Release is what ships.
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    # Default: DungeonCrawlerWorld/bin/<Configuration>/net10.0/DungeonCrawlerWorld.exe
    [string]$ExePath = "",
    # Default: Log/phase-benchmarks/baseline-build-<configuration>
    [string]$BaselineDirectory = "",
    [string]$DiagnosticsDirectory = "Log/diagnostics",
    [string]$OutputDir = "Log/phase-benchmarks",
    # Frame cost depends on what the world is doing, so a run is only comparable against runs of
    # the same seed AND the same frame range -- both are checked against what the game stamps
    # into its report, and both select the comparison baseline.
    [int]$Seed = 1,
    # Simulation frames measured, StartFrame inclusive to EndFrame exclusive. The frames before
    # StartFrame are warm-up (JIT, first fights); 3000 frames is 50 simulated seconds.
    [long]$StartFrame = 600,
    [long]$EndFrame = 3600,
    [int]$TimeoutSeconds = 300,
    # Default 20 for a single run against a saved one, 10 for -Compare (medians of interleaved
    # runs are much tighter).
    [double]$RegressionPercent = -1,
    [double]$RegressionMinMsPerFrame = 0.02,
    # A windowed run whose wall-clock time exceeds FrameCount/60 seconds by more than this fraction
    # fell behind, so FNA ran several Updates per Draw -- Draw costs per frame aren't comparable.
    [double]$FellBehindTolerance = 0.10
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($ExePath)) {
    $ExePath = "DungeonCrawlerWorld/bin/$Configuration/net10.0/DungeonCrawlerWorld.exe"
}
if ([string]::IsNullOrEmpty($BaselineDirectory)) {
    $BaselineDirectory = "Log/phase-benchmarks/baseline-build-$($Configuration.ToLowerInvariant())"
}

function Assert-GameNotRunning {
    # Refuse rather than kill: an already-running game may be someone's session, and two
    # instances would compete for the CPU and skew both.
    $existing = @(Get-Process -Name DungeonCrawlerWorld -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 0) {
        throw "DungeonCrawlerWorld is already running (pid $($existing.Id -join ', ')). Close it before benchmarking."
    }
}

# Flattens a benchmark report's Update/Draw sections into "Category.Group.Name" -> ms/frame.
function Get-PerFrameMap {
    param($Report)

    $perFrame = @{}
    foreach ($category in @("Update", "Draw")) {
        $section = $Report.$category
        if (-not $section) { continue }
        foreach ($groupProperty in $section.PSObject.Properties) {
            foreach ($item in $groupProperty.Value) {
                $perFrame["$category.$($groupProperty.Name).$($item.Name)"] = [double]$item.MillisecondsPerFrame
            }
        }
    }
    return $perFrame
}

function Get-Median {
    param([double[]]$Values)

    $sorted = @($Values | Sort-Object)
    $middle = [math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

# One launch of the game, start to finish. Returns the report, its per-frame map and (headless
# only) the world fingerprint.
function Invoke-BenchmarkRun {
    param([string]$Exe, [bool]$RunHeadless)

    $gameArguments = @("--seed=$Seed", "--benchmark-frames=$StartFrame-$EndFrame")
    if ($RunHeadless) {
        $gameArguments = @("--headless") + $gameArguments
    } else {
        $gameArguments = @("--diagnostics=frame") + $gameArguments
    }

    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    $process = Start-Process -FilePath $Exe -ArgumentList $gameArguments -PassThru `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

    $benchmarkFile = $null
    try {
        # The game writes benchmark-<timestamp>-<pid>.json the moment frame EndFrame begins;
        # matching on this process's pid means a stale file from an earlier run is never used.
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ($true) {
            $benchmarkFile = Get-ChildItem -Path $DiagnosticsDirectory -Filter "benchmark-*-$($process.Id).json" -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($null -ne $benchmarkFile -and (-not $RunHeadless -or $process.HasExited)) { break }

            if ($process.HasExited -and $null -eq $benchmarkFile) {
                $errorText = Get-Content -LiteralPath $stderrPath -Raw
                throw "The game exited (code $($process.ExitCode)) before writing its benchmark report. $errorText"
            }
            if ((Get-Date) -gt $deadline) {
                throw "No benchmark report after ${TimeoutSeconds}s. A windowed game may be paused or showing a blocking notification -- simulation frames only advance while unpaused."
            }
            Start-Sleep -Milliseconds 500
        }

        # Written with a single File.WriteAllText; a short pause keeps a read from racing it.
        Start-Sleep -Milliseconds 300
        $report = Get-Content -LiteralPath $benchmarkFile.FullName -Raw | ConvertFrom-Json
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(10000) | Out-Null
        }
    }

    $fingerprint = $null
    $stdout = Get-Content -LiteralPath $stdoutPath -Raw
    if ($stdout -match "\[Headless\] Fingerprint (\w+)") { $fingerprint = $Matches[1] }
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -ErrorAction SilentlyContinue

    if ([int]$report.RandomSeed -ne $Seed -or [long]$report.StartFrame -ne $StartFrame -or [long]$report.EndFrame -ne $EndFrame) {
        throw "$($benchmarkFile.Name) is seed $($report.RandomSeed), frames $($report.StartFrame)-$($report.EndFrame); expected seed $Seed, frames $StartFrame-$EndFrame."
    }
    if ($RunHeadless -and $null -eq $fingerprint) {
        throw "Headless run pid $($process.Id) printed no fingerprint."
    }

    $perFrame = Get-PerFrameMap -Report $report
    if ($perFrame.Count -eq 0) {
        throw "$($benchmarkFile.Name) recorded nothing inside frames $StartFrame-$EndFrame."
    }

    return [pscustomobject]@{
        Report      = $report
        PerFrame    = $perFrame
        Fingerprint = $fingerprint
    }
}

# Per-entry median, min and max across runs.
function Get-Summary {
    param($Runs)

    $keys = @($Runs | ForEach-Object { $_.PerFrame.Keys }) | Select-Object -Unique
    $summary = @{}
    foreach ($key in $keys) {
        $values = [double[]]@($Runs | ForEach-Object { if ($_.PerFrame.ContainsKey($key)) { $_.PerFrame[$key] } else { 0.0 } })
        $summary[$key] = [pscustomobject]@{
            Median = Get-Median -Values $values
            Min    = ($values | Measure-Object -Minimum).Minimum
            Max    = ($values | Measure-Object -Maximum).Maximum
        }
    }
    return $summary
}

# Two runs of the same build and seed must end in the same world. Different fingerprints mean the
# simulation isn't deterministic, and the runs measured different workloads.
function Test-Deterministic {
    param($Runs, [string]$Label)

    $distinct = @($Runs | ForEach-Object { $_.Fingerprint } | Select-Object -Unique)
    if ($distinct.Count -gt 1) {
        Write-Host "WARNING: $Label runs ended in different worlds (fingerprints $($distinct -join ', ')). The simulation is not deterministic for this seed -- its costs are not comparable run to run. Investigate before trusting any numbers."
        return $false
    }
    return $true
}

function Get-GitInfo {
    $commit = $null
    $branch = $null
    $dirty = $null
    try { $commit = (git rev-parse --short HEAD 2>$null) } catch {}
    try { $branch = (git branch --show-current 2>$null) } catch {}
    try { $dirty = @(git status --porcelain 2>$null).Count -gt 0 } catch {}
    return [pscustomobject]@{ Commit = $commit; Branch = $branch; HasUncommittedChanges = $dirty }
}

$exeDirectory = Split-Path -Parent $ExePath
$exeName = Split-Path -Leaf $ExePath

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Game exe not found: $ExePath. Run 'dotnet build DungeonCrawlerWorld.sln' first."
}

# ---------------------------------------------------------------------------------------------
# -SaveBaseline: copy the build aside and stop.
# ---------------------------------------------------------------------------------------------
if ($SaveBaseline) {
    if (Test-Path -LiteralPath $BaselineDirectory) {
        Remove-Item -LiteralPath $BaselineDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $BaselineDirectory | Out-Null
    Copy-Item -Path (Join-Path $exeDirectory "*") -Destination $BaselineDirectory -Recurse -Force

    $git = Get-GitInfo
    [ordered]@{
        savedUtc              = (Get-Date).ToUniversalTime().ToString("o")
        configuration         = $Configuration
        gitCommit             = $git.Commit
        hasUncommittedChanges = $git.HasUncommittedChanges
        source                = (Resolve-Path -LiteralPath $exeDirectory).Path
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $BaselineDirectory "baseline-info.json") -Encoding utf8

    Write-Host "Saved the current build as the A/B baseline: $BaselineDirectory (commit $($git.Commit)$(if ($git.HasUncommittedChanges) { ', with uncommitted changes' }))."
    Write-Host "Make the change, rebuild, then run with -Compare."
    exit 0
}

Assert-GameNotRunning

# ---------------------------------------------------------------------------------------------
# -Compare: interleaved headless A/B.
# ---------------------------------------------------------------------------------------------
if ($Compare) {
    $baselineExe = Join-Path $BaselineDirectory $exeName
    if (-not (Test-Path -LiteralPath $baselineExe)) {
        throw "No saved baseline build at $baselineExe. Build the 'before' code and run with -SaveBaseline first."
    }
    $baselineInfo = Get-Content -LiteralPath (Join-Path $BaselineDirectory "baseline-info.json") -Raw | ConvertFrom-Json

    $pairs = if ($Repeat -gt 0) { $Repeat } else { 3 }
    $threshold = if ($RegressionPercent -ge 0) { $RegressionPercent } else { 10 }

    Write-Host "A/B, headless, $Configuration, seed $Seed, frames $StartFrame-$EndFrame, $pairs runs per side, interleaved"
    Write-Host "  A (baseline): $baselineExe  (saved $($baselineInfo.savedUtc), commit $($baselineInfo.gitCommit))"
    Write-Host "  B (current):  $ExePath"

    $runsA = @()
    $runsB = @()
    for ($i = 1; $i -le $pairs; $i++) {
        # Alternate which side goes first, so a slow drift over the session doesn't always
        # penalise the same side.
        $order = if ($i % 2 -eq 1) { @("A", "B") } else { @("B", "A") }
        foreach ($side in $order) {
            Write-Host "  run $i/$pairs $side..."
            if ($side -eq "A") { $runsA += Invoke-BenchmarkRun -Exe $baselineExe -RunHeadless $true }
            else { $runsB += Invoke-BenchmarkRun -Exe $ExePath -RunHeadless $true }
        }
    }

    Write-Host ""
    $deterministicA = Test-Deterministic -Runs $runsA -Label "A (baseline)"
    $deterministicB = Test-Deterministic -Runs $runsB -Label "B (current)"
    $sameWorld = $deterministicA -and $deterministicB -and $runsA[0].Fingerprint -eq $runsB[0].Fingerprint
    if ($deterministicA -and $deterministicB) {
        if ($sameWorld) {
            Write-Host "A and B simulated the same world (fingerprint $($runsA[0].Fingerprint)): the change affected cost only, so every row compares like with like."
        } else {
            Write-Host "A and B simulated DIFFERENT worlds (fingerprints $($runsA[0].Fingerprint) vs $($runsB[0].Fingerprint)): the change altered gameplay, so some rows differ because the game did different things, not only because code got faster or slower."
        }
    }
    Write-Host ""

    $summaryA = Get-Summary -Runs $runsA
    $summaryB = Get-Summary -Runs $runsB
    $keys = @($summaryA.Keys) + @($summaryB.Keys) | Select-Object -Unique |
        Sort-Object { if ($summaryB.ContainsKey($_)) { -$summaryB[$_].Median } else { 0 } }

    $header = "{0,-53} {1,10} {2,10} {3,8} {4,9}  {5}" -f "System (ms/frame, median)", "A", "B", "Delta%", "A spread", "Flag"
    Write-Host $header
    Write-Host ("-" * $header.Length)

    $regressions = @()
    $improvements = @()
    $rows = [ordered]@{}
    foreach ($key in $keys) {
        $a = if ($summaryA.ContainsKey($key)) { $summaryA[$key] } else { [pscustomobject]@{ Median = 0.0; Min = 0.0; Max = 0.0 } }
        $b = if ($summaryB.ContainsKey($key)) { $summaryB[$key] } else { [pscustomobject]@{ Median = 0.0; Min = 0.0; Max = 0.0 } }
        $delta = $b.Median - $a.Median
        $deltaPct = if ($a.Median -gt 0) { [math]::Round(($delta / $a.Median) * 100, 1) } else { $null }
        $spreadPct = if ($a.Median -gt 0) { [math]::Round((($a.Max - $a.Min) / $a.Median) * 100, 1) } else { 0 }

        # Flag only outside A's own run-to-run range, not just past a percentage: a system whose A
        # runs spread 15% can't be called 12% slower from one comparison.
        $flag = ""
        if ($null -ne $deltaPct -and [math]::Abs($delta) -ge $RegressionMinMsPerFrame -and [math]::Abs($deltaPct) -ge $threshold) {
            if ($delta -gt 0 -and $b.Median -gt $a.Max) { $flag = "REGRESSION"; $regressions += $key }
            elseif ($delta -lt 0 -and $b.Median -lt $a.Min) { $flag = "IMPROVED"; $improvements += $key }
        }

        $rows[$key] = [ordered]@{ a = [math]::Round($a.Median, 4); b = [math]::Round($b.Median, 4); deltaPercent = $deltaPct; aSpreadPercent = $spreadPct; flag = $flag }
        $deltaText = if ($null -eq $deltaPct) { "n/a" } else { "$deltaPct%" }
        Write-Host ("{0,-53} {1,10:N4} {2,10:N4} {3,8} {4,8}%  {5}" -f $key, $a.Median, $b.Median, $deltaText, $spreadPct, $flag)
    }

    $git = Get-GitInfo
    $result = [ordered]@{
        timestampUtc   = (Get-Date).ToUniversalTime().ToString("o")
        sampling       = "ab-headless"
        configuration  = $Configuration
        unit           = "ms/frame"
        randomSeed     = $Seed
        startFrame     = $StartFrame
        endFrame       = $EndFrame
        runsPerSide    = $pairs
        baseline       = $baselineInfo
        currentCommit  = $git.Commit
        currentDirty   = $git.HasUncommittedChanges
        fingerprintA   = @($runsA | ForEach-Object { $_.Fingerprint })
        fingerprintB   = @($runsB | ForEach-Object { $_.Fingerprint })
        sameWorld      = $sameWorld
        systems        = $rows
    }
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    $outFile = Join-Path $OutputDir ("ab-{0}.json" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss"))
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outFile -Encoding utf8

    Write-Host ""
    Write-Host "Saved: $outFile"
    if ($regressions.Count -gt 0) { Write-Host "REGRESSIONS (>=$threshold%, >=${RegressionMinMsPerFrame}ms/frame, outside A's range): $($regressions -join ', ')" }
    if ($improvements.Count -gt 0) { Write-Host "Improvements (same rule): $($improvements -join ', ')" }
    if ($regressions.Count -eq 0 -and $improvements.Count -eq 0) { Write-Host "No change beyond A's own run-to-run range and the threshold." }
    exit 0
}

# ---------------------------------------------------------------------------------------------
# Default: a benchmark of the current build, compared against the last saved comparable run.
# ---------------------------------------------------------------------------------------------
$runCount = if ($Repeat -gt 0) { $Repeat } elseif ($Headless) { 5 } else { 1 }
$threshold = if ($RegressionPercent -ge 0) { $RegressionPercent } else { 20 }
$sampling = if ($Headless) { "frame-range-headless" } else { "frame-range" }

Write-Host "$(if ($Headless) { 'Headless' } else { 'Windowed' }) benchmark, $Configuration, seed $Seed, frames $StartFrame-$EndFrame, $runCount run(s)"
$runs = @()
for ($i = 1; $i -le $runCount; $i++) {
    if ($runCount -gt 1) { Write-Host "  run $i/$runCount..." }
    $runs += Invoke-BenchmarkRun -Exe $ExePath -RunHeadless $Headless.IsPresent
}

if ($Headless) {
    if (Test-Deterministic -Runs $runs -Label "Headless") {
        Write-Host "All $runCount runs ended in the same world (fingerprint $($runs[0].Fingerprint))."
    }
}

$summary = Get-Summary -Runs $runs
$medians = [ordered]@{}
foreach ($key in ($summary.Keys | Sort-Object { -$summary[$_].Median })) {
    $medians[$key] = [math]::Round($summary[$key].Median, 4)
}

$fellBehind = $false
if (-not $Headless) {
    $report = $runs[0].Report
    $expectedWallClockMs = $report.FrameCount / 60.0 * 1000.0
    $fellBehind = $report.WallClockMilliseconds -gt $expectedWallClockMs * (1 + $FellBehindTolerance)
}

$git = Get-GitInfo
$result = [ordered]@{
    timestampUtc          = (Get-Date).ToUniversalTime().ToString("o")
    gitCommit             = $git.Commit
    gitBranch             = $git.Branch
    hasUncommittedChanges = $git.HasUncommittedChanges
    sampling              = $sampling
    configuration         = $Configuration
    unit                  = "ms/frame"
    randomSeed            = $Seed
    startFrame            = $StartFrame
    endFrame              = $EndFrame
    runs                  = $runCount
    fingerprint           = $runs[0].Fingerprint
    wallClockMilliseconds = @($runs | ForEach-Object { [math]::Round([double]$_.Report.WallClockMilliseconds, 1) })
    fellBehind            = $fellBehind
    systems               = $medians
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outFile = Join-Path $OutputDir ("{0}.json" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss"))
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $outFile -Encoding utf8

Write-Host "Saved benchmark: $outFile"
if (-not $Headless) {
    Write-Host ("Wall clock {0:N1}s for {1} frames (real time would be {2:N1}s)" -f ($runs[0].Report.WallClockMilliseconds / 1000), $runs[0].Report.FrameCount, ($runs[0].Report.FrameCount / 60.0))
    if ($fellBehind) {
        Write-Host "WARNING: the run fell behind real time -- Update costs are still comparable, Draw costs per frame are not."
    }
}
Write-Host ""

# Only an earlier run of the same sampling mode, seed and range is a valid comparison. Windowed
# and headless never compare to each other: headless has no Draw between frames, so every system
# reads far cheaper.
$outFullPath = (Resolve-Path -LiteralPath $outFile).Path
$previousFile = $null
$previous = $null
$skipped = 0
foreach ($candidate in (Get-ChildItem -Path $OutputDir -Filter "2*.json" | Where-Object { $_.FullName -ne $outFullPath } | Sort-Object Name -Descending)) {
    $candidateReport = Get-Content -LiteralPath $candidate.FullName -Raw | ConvertFrom-Json
    # Files from before -Configuration existed were all Debug.
    $candidateConfiguration = if ($candidateReport.configuration) { $candidateReport.configuration } else { "Debug" }
    if ($candidateReport.sampling -eq $sampling -and
        $candidateConfiguration -eq $Configuration -and
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
    Write-Host "No previous $Configuration $sampling seed-$Seed, frames $StartFrame-$EndFrame benchmark in $OutputDir -- this is the first recorded baseline for it."
    if ($skipped -gt 0) {
        Write-Host "($skipped earlier file(s) skipped: a different configuration, seed, frame range or sampling mode, so not comparable.)"
    }
    Write-Host ""
    Write-Host ("{0,-55} {1,10} {2,10}" -f "System", "ms/frame", "spread")
    foreach ($name in $medians.Keys) {
        $entry = $summary[$name]
        $spread = if ($entry.Median -gt 0 -and $runCount -gt 1) { "{0:N1}%" -f ((($entry.Max - $entry.Min) / $entry.Median) * 100) } else { "" }
        Write-Host ("{0,-55} {1,10:N4} {2,10}" -f $name, $medians[$name], $spread)
    }
    exit 0
}

Write-Host "Comparing against previous run: $($previousFile.Name) (commit $($previous.gitCommit), $($previous.timestampUtc))"
Write-Host "Only valid if that run was made in this same sitting -- machine state alone has moved every row ~40% between sessions. For a before/after of a change, use -SaveBaseline and -Compare."
if ($skipped -gt 0) {
    Write-Host "($skipped newer file(s) skipped: a different configuration, seed, frame range or sampling mode, so not comparable.)"
}
if ($previous.fellBehind) {
    Write-Host "WARNING: the previous run fell behind real time -- its Draw costs per frame are not comparable."
}
if ($Headless -and $previous.fingerprint -and $previous.fingerprint -ne $runs[0].Fingerprint) {
    Write-Host "NOTE: the previous run ended in a different world (fingerprint $($previous.fingerprint) vs $($runs[0].Fingerprint)) -- gameplay changed between them."
}
Write-Host ""

$prevSystems = @{}
if ($previous.systems) {
    $previous.systems.PSObject.Properties | ForEach-Object { $prevSystems[$_.Name] = [double]$_.Value }
}

$allCompareNames = @($medians.Keys) + @($prevSystems.Keys) | Select-Object -Unique |
    Sort-Object { if ($medians.Contains($_)) { -$medians[$_] } else { 0 } }

$header = "{0,-53} {1,10} {2,10} {3,10} {4,8}  {5}" -f "System (ms/frame)", "Current", "Previous", "Delta", "Delta%", "Flag"
Write-Host $header
Write-Host ("-" * $header.Length)

$regressions = @()
foreach ($name in $allCompareNames) {
    $cur = if ($medians.Contains($name)) { $medians[$name] } else { 0.0 }
    $prev = if ($prevSystems.ContainsKey($name)) { $prevSystems[$name] } else { 0.0 }
    $delta = $cur - $prev
    $deltaPctStr = "n/a"
    $isRegression = $false
    if ($prev -gt 0) {
        $deltaPct = [math]::Round(($delta / $prev) * 100, 1)
        $deltaPctStr = "$deltaPct%"
        if ($delta -ge $RegressionMinMsPerFrame -and $deltaPct -ge $threshold) {
            $isRegression = $true
        }
    }
    $flag = if ($isRegression) { "REGRESSION" } else { "" }
    if ($isRegression) { $regressions += $name }
    Write-Host ("{0,-53} {1,10:N4} {2,10:N4} {3,10:N4} {4,8}  {5}" -f $name, $cur, $prev, $delta, $deltaPctStr, $flag)
}

Write-Host ""
if ($regressions.Count -gt 0) {
    Write-Host "Flagged regressions (>=$threshold% and >=${RegressionMinMsPerFrame}ms/frame increase): $($regressions -join ', ')"
} else {
    Write-Host "No regressions above threshold (>=$threshold% and >=${RegressionMinMsPerFrame}ms/frame)."
}
