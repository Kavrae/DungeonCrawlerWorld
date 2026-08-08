[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $true)][int]$SkipBlocks,
    [string]$OutputDir = "Log/phase-benchmarks",
    [double]$RegressionPercent = 20,
    [double]$RegressionMinMs = 1.0
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $LogPath)) {
    throw "Log file not found: $LogPath"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Each run prints one block per real second like:
#   [PerformanceProfile] Top phases (ms spent in the last second):
#   [PerformanceProfile]   EcsContext.Update (all systems): 674.1ms
#   [PerformanceProfile]   TestCombatBehaviorSystem: 370.4ms
# Parse every block in the captured log, in order.
$lines = Get-Content -LiteralPath $LogPath
$blocks = @()
$current = $null
foreach ($line in $lines) {
    if ($line -match '^\[PerformanceProfile\] Top phases') {
        if ($null -ne $current) { $blocks += , $current }
        $current = [ordered]@{}
        continue
    }
    if ($null -ne $current -and $line -match '^\[PerformanceProfile\]\s+(.+?):\s+([\d.]+)ms\s*$') {
        $current[$matches[1]] = [double]$matches[2]
    }
}
if ($null -ne $current) { $blocks += , $current }

if ($blocks.Count -eq 0) {
    throw "No [PerformanceProfile] blocks found in $LogPath. Either the run never reached the report interval, or it was launched via 'dotnet run' (bypass it -- invoke the built .exe directly, see SKILL.md), or the game booted into a paused/blocking-notification state."
}

if ($blocks.Count -le $SkipBlocks) {
    throw "Only $($blocks.Count) block(s) captured but -SkipBlocks is $SkipBlocks. Capture a longer run -- there needs to be steady-state data left after discarding the warmup blocks."
}

# Blocks before SkipBlocks are the startup-cost outliers (JIT warmup, initial
# allocations, first-tick spikes) -- see SKILL.md for how SkipBlocks is derived
# from a real 30-second wall-clock warmup window rather than a fixed guess.
$steadyBlocks = $blocks[$SkipBlocks..($blocks.Count - 1)]

$allNames = $steadyBlocks | ForEach-Object { $_.Keys } | Select-Object -Unique

# A system missing from a block genuinely means 0ms that block (PhaseProfiler
# only records a name when Record() was called for it), so average over the
# full steady-state block count, not just the blocks where the name appears.
$averages = [ordered]@{}
foreach ($name in $allNames) {
    $sum = 0.0
    foreach ($b in $steadyBlocks) {
        if ($b.Contains($name)) { $sum += $b[$name] }
    }
    $averages[$name] = [math]::Round($sum / $steadyBlocks.Count, 2)
}

$sortedNames = $averages.Keys | Sort-Object { -$averages[$_] }

$commit = $null
$branch = $null
try { $commit = (git rev-parse --short HEAD 2>$null) } catch {}
try { $branch = (git branch --show-current 2>$null) } catch {}

$result = [ordered]@{
    timestampUtc           = (Get-Date).ToUniversalTime().ToString("o")
    gitCommit               = $commit
    gitBranch                = $branch
    sourceLog               = (Resolve-Path -LiteralPath $LogPath).Path
    totalBlocksCaptured     = $blocks.Count
    warmupBlocksSkipped     = $SkipBlocks
    steadyStateSampleCount  = $steadyBlocks.Count
    systems                 = [ordered]@{}
}
foreach ($name in $sortedNames) { $result.systems[$name] = $averages[$name] }

$outFile = Join-Path $OutputDir ("{0}.json" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss"))
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $outFile -Encoding utf8

Write-Host "Saved benchmark: $outFile"
Write-Host "($($steadyBlocks.Count) steady-state samples averaged, $SkipBlocks warmup blocks discarded, $($blocks.Count) total blocks captured)"
Write-Host ""

$previousFile = Get-ChildItem -Path $OutputDir -Filter "*.json" |
    Where-Object { $_.FullName -ne (Resolve-Path -LiteralPath $outFile).Path } |
    Sort-Object Name -Descending |
    Select-Object -First 1

if ($null -eq $previousFile) {
    Write-Host "No previous benchmark found in $OutputDir -- this is the first recorded baseline."
    Write-Host ""
    Write-Host ("{0,-40} {1,10}" -f "System", "ms/sec")
    foreach ($name in $sortedNames) {
        Write-Host ("{0,-40} {1,10}" -f $name, $averages[$name])
    }
    exit 0
}

$previous = Get-Content -LiteralPath $previousFile.FullName -Raw | ConvertFrom-Json
Write-Host "Comparing against previous: $($previousFile.Name) (commit $($previous.gitCommit), $($previous.timestampUtc))"
Write-Host ""

$prevSystems = @{}
if ($previous.systems) {
    $previous.systems.PSObject.Properties | ForEach-Object { $prevSystems[$_.Name] = [double]$_.Value }
}

$allCompareNames = @($sortedNames) + @($prevSystems.Keys) | Select-Object -Unique |
    Sort-Object { if ($averages.Contains($_)) { -$averages[$_] } else { 0 } }

$header = "{0,-38} {1,10} {2,10} {3,10} {4,8}  {5}" -f "System", "Current", "Previous", "Delta", "Delta%", "Flag"
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
        if ($delta -ge $RegressionMinMs -and $deltaPct -ge $RegressionPercent) {
            $isRegression = $true
        }
    }
    $flag = if ($isRegression) { "REGRESSION" } else { "" }
    if ($isRegression) { $regressions += $name }
    Write-Host ("{0,-38} {1,10} {2,10} {3,10} {4,8}  {5}" -f $name, $cur, $prev, [math]::Round($delta, 2), $deltaPctStr, $flag)
}

Write-Host ""
if ($regressions.Count -gt 0) {
    Write-Host "Flagged regressions (>=$RegressionPercent% and >=${RegressionMinMs}ms increase): $($regressions -join ', ')"
} else {
    Write-Host "No regressions above threshold (>=$RegressionPercent% and >=${RegressionMinMs}ms)."
}
