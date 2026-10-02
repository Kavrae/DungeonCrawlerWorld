[CmdletBinding()]
param(
    # Also run the saved baseline build (Invoke-FrameBenchmark.ps1 -SaveBaseline) and print the
    # per-pool difference. The baseline must itself include PoolMemoryReport, or it writes none.
    [switch]$Compare,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    # Default: DungeonCrawlerWorld/bin/<Configuration>/net10.0/DungeonCrawlerWorld.exe
    [string]$ExePath = "",
    # Default: Log/phase-benchmarks/baseline-build-<configuration>, or
    # baseline-build-<configuration>-<BaselineName> when a name is given.
    [string]$BaselineDirectory = "",
    # The named baseline Invoke-FrameBenchmark.ps1 -SaveBaseline -BaselineName saved.
    [ValidatePattern("^[A-Za-z0-9_-]*$")]
    [string]$BaselineName = "",
    [string]$DiagnosticsDirectory = "Log/diagnostics",
    [string]$OutputDir = "Log/phase-benchmarks",
    [int]$Seed = 1,
    [long]$StartFrame = 600,
    [long]$EndFrame = 3600,
    [int]$MapSize = 3072,
    [int]$TimeoutSeconds = 600,
    # Pools whose estimate moved by less than this many MB are left out of the comparison table.
    [double]$MinimumDeltaMegabytes = 0.5
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($ExePath)) {
    $ExePath = "DungeonCrawlerWorld/bin/$Configuration/net10.0/DungeonCrawlerWorld.exe"
}
if ([string]::IsNullOrEmpty($BaselineDirectory)) {
    $BaselineDirectory = "Log/phase-benchmarks/baseline-build-$($Configuration.ToLowerInvariant())"
    if (-not [string]::IsNullOrEmpty($BaselineName)) { $BaselineDirectory += "-$BaselineName" }
}

if (@(Get-Process -Name DungeonCrawlerWorld -ErrorAction SilentlyContinue).Count -gt 0) {
    throw "DungeonCrawlerWorld is already running. Close it before measuring."
}

# One headless run with --diagnostics=memory. Returns the parsed memory report.
function Invoke-MemoryRun {
    param([string]$Exe)

    if (-not (Test-Path -LiteralPath $Exe)) { throw "No build at $Exe." }

    $gameArguments = @("--headless", "--diagnostics=memory", "--seed=$Seed", "--benchmark-frames=$StartFrame-$EndFrame", "--map-size=$MapSize")
    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    # Process ids are reused, so an older report can carry this run's pid; only a file written
    # after this run started is its own.
    $startedAt = Get-Date
    $process = Start-Process -FilePath $Exe -ArgumentList $gameArguments -PassThru `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    # Windows PowerShell only reports ExitCode for a process whose handle was opened while it ran.
    $null = $process.Handle

    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            throw "Headless memory run pid $($process.Id) did not finish within ${TimeoutSeconds}s."
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Headless memory run exited with code $($process.ExitCode). $(Get-Content -LiteralPath $stderrPath -Raw)"
        }

        $file = Get-ChildItem -Path $DiagnosticsDirectory -Filter "memory-*-$($process.Id).json" -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -ge $startedAt } | Select-Object -First 1
        if ($null -eq $file) {
            throw "Run pid $($process.Id) wrote no memory report -- does $Exe include PoolMemoryReport?"
        }

        $report = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ([int]$report.RandomSeed -ne $Seed -or [long]$report.StartFrame -ne $StartFrame -or [long]$report.EndFrame -ne $EndFrame) {
            throw "$($file.Name) is seed $($report.RandomSeed), frames $($report.StartFrame)-$($report.EndFrame); expected seed $Seed, frames $StartFrame-$EndFrame."
        }

        Write-Host "  $Exe -> $($file.Name)"
        return [pscustomobject]@{ Report = $report; TextPath = [System.IO.Path]::ChangeExtension($file.FullName, ".txt") }
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(10000) | Out-Null
        }
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -ErrorAction SilentlyContinue
    }
}

function Format-Megabytes {
    param([double]$Bytes)
    return "{0:N1}" -f ($Bytes / 1MB)
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$exeName = Split-Path -Leaf $ExePath

if (-not $Compare) {
    Write-Host "Memory report, seed $Seed, frames $StartFrame-$EndFrame, map $MapSize, $Configuration"
    $run = Invoke-MemoryRun -Exe $ExePath
    Get-Content -LiteralPath $run.TextPath | Write-Host
    return
}

$baselineExe = Join-Path $BaselineDirectory $exeName
Write-Host "Memory A/B, seed $Seed, frames $StartFrame-$EndFrame, map $MapSize, $Configuration"
$a = Invoke-MemoryRun -Exe $baselineExe
$b = Invoke-MemoryRun -Exe $ExePath

$summaryRows = @(
    @{ Name = "Pools (estimate)"; A = [double]$a.Report.TotalPoolBytes; B = [double]$b.Report.TotalPoolBytes },
    @{ Name = "Live heap at end"; A = [double]$a.Report.LiveHeapBytesAtEnd; B = [double]$b.Report.LiveHeapBytesAtEnd },
    @{ Name = "Peak working set"; A = [double]$a.Report.PeakWorkingSetBytes; B = [double]$b.Report.PeakWorkingSetBytes },
    @{ Name = "Allocated before range"; A = [double]$a.Report.AllocatedBytesBeforeRange; B = [double]$b.Report.AllocatedBytesBeforeRange }
)

Write-Host ""
Write-Host ("{0,-24} {1,10} {2,10} {3,10}" -f "", "A MB", "B MB", "Delta MB")
foreach ($row in $summaryRows) {
    Write-Host ("{0,-24} {1,10} {2,10} {3,10}" -f $row.Name, (Format-Megabytes $row.A), (Format-Megabytes $row.B), (Format-Megabytes ($row.B - $row.A)))
}
Write-Host ("{0,-24} {1,10} {2,10}" -f "Collections g0/g1/g2", "$($a.Report.Gen0CollectionsBeforeRange)/$($a.Report.Gen1CollectionsBeforeRange)/$($a.Report.Gen2CollectionsBeforeRange)", "$($b.Report.Gen0CollectionsBeforeRange)/$($b.Report.Gen1CollectionsBeforeRange)/$($b.Report.Gen2CollectionsBeforeRange)")
Write-Host ("{0,-24} {1,10:N0} {2,10:N0}" -f "Living entities", $a.Report.LivingEntities, $b.Report.LivingEntities)

$poolsA = @{}
foreach ($pool in $a.Report.Pools) { $poolsA[$pool.ComponentType] = $pool }
$poolsB = @{}
foreach ($pool in $b.Report.Pools) { $poolsB[$pool.ComponentType] = $pool }

$rows = foreach ($name in ($poolsA.Keys + $poolsB.Keys | Select-Object -Unique)) {
    $bytesA = if ($poolsA.ContainsKey($name)) { [double]$poolsA[$name].EstimatedBytes } else { 0 }
    $bytesB = if ($poolsB.ContainsKey($name)) { [double]$poolsB[$name].EstimatedBytes } else { 0 }
    [pscustomobject]@{
        Pool   = $name
        CountA = if ($poolsA.ContainsKey($name)) { $poolsA[$name].Count } else { 0 }
        CountB = if ($poolsB.ContainsKey($name)) { $poolsB[$name].Count } else { 0 }
        MbA    = $bytesA / 1MB
        MbB    = $bytesB / 1MB
        Delta  = ($bytesB - $bytesA) / 1MB
    }
}

Write-Host ""
Write-Host ("{0,-38} {1,10} {2,10} {3,9} {4,9} {5,9}" -f "Pool", "Count A", "Count B", "MB A", "MB B", "Delta")
foreach ($row in ($rows | Where-Object { [math]::Abs($_.Delta) -ge $MinimumDeltaMegabytes } | Sort-Object Delta)) {
    Write-Host ("{0,-38} {1,10:N0} {2,10:N0} {3,9:N1} {4,9:N1} {5,9:N1}" -f $row.Pool, $row.CountA, $row.CountB, $row.MbA, $row.MbB, $row.Delta)
}

$countChanges = @($rows | Where-Object { $_.CountA -ne $_.CountB })
if ($countChanges.Count -gt 0) {
    Write-Host ""
    Write-Host "NOTE: $($countChanges.Count) pool(s) ended with different counts -- A and B simulated different worlds, so some memory differences come from gameplay, not layout."
}

$savedPath = Join-Path $OutputDir ("memory-ab-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date).ToUniversalTime())
[pscustomobject]@{
    timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
    configuration = $Configuration
    seed = $Seed
    startFrame = $StartFrame
    endFrame = $EndFrame
    mapSize = $MapSize
    a = $a.Report
    b = $b.Report
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $savedPath -Encoding utf8
Write-Host ""
Write-Host "Saved $savedPath"
