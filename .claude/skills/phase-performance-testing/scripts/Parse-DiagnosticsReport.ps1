[CmdletBinding()]
param(
    [string]$DiagnosticsDirectory = "Log/diagnostics",
    [int]$SampleCount = 8,
    [int]$SampleIntervalSeconds = 5,
    [string]$OutputDir = "Log/phase-benchmarks",
    [double]$RegressionPercent = 20,
    [double]$RegressionMinMs = 1.0
)

$ErrorActionPreference = "Stop"

$latestPath = Join-Path $DiagnosticsDirectory "latest.json"
if (-not (Test-Path -LiteralPath $latestPath)) {
    throw "Diagnostics file not found: $latestPath. Launch the game with --diagnostics=frame (or =all) and wait for it to write at least once -- see SKILL.md."
}

# Flattens a report's FrameBudget.Update/Draw sections (Category -> Group -> [{Name,
# MillisecondsPerSecond}]) into a single "Category.Group.Name" -> ms/sec map, so systems/windows
# across both categories can be summed/averaged/diffed uniformly.
function Get-FlattenedFrameBudget {
    param($Report)

    $flattened = @{}
    if (-not $Report.FrameBudget) {
        return $flattened
    }

    foreach ($category in @("Update", "Draw")) {
        $section = $Report.FrameBudget.$category
        if (-not $section) { continue }

        foreach ($groupProperty in $section.PSObject.Properties) {
            $groupName = $groupProperty.Name
            foreach ($item in $groupProperty.Value) {
                $key = "$category.$groupName.$($item.Name)"
                $flattened[$key] = [double]$item.MillisecondsPerSecond
            }
        }
    }

    return $flattened
}

Write-Host "Sampling $latestPath : $SampleCount samples, ${SampleIntervalSeconds}s apart..."

$sums = @{}
for ($i = 0; $i -lt $SampleCount; $i++) {
    $report = Get-Content -LiteralPath $latestPath -Raw | ConvertFrom-Json
    $flattened = Get-FlattenedFrameBudget -Report $report

    foreach ($key in $flattened.Keys) {
        if (-not $sums.Contains($key)) { $sums[$key] = 0.0 }
        $sums[$key] += $flattened[$key]
    }

    if ($i -lt $SampleCount - 1) {
        Start-Sleep -Seconds $SampleIntervalSeconds
    }
}

if ($sums.Count -eq 0) {
    throw "No frame-budget entries found across $SampleCount samples of $latestPath. Was the game launched with --diagnostics=frame (or =all)?"
}

$averages = [ordered]@{}
foreach ($key in ($sums.Keys | Sort-Object { -$sums[$_] })) {
    $averages[$key] = [math]::Round($sums[$key] / $SampleCount, 2)
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
    sourceFile            = (Resolve-Path -LiteralPath $latestPath).Path
    sampleCount           = $SampleCount
    sampleIntervalSeconds = $SampleIntervalSeconds
    systems               = $averages
}

$outFile = Join-Path $OutputDir ("{0}.json" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss"))
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $outFile -Encoding utf8

Write-Host "Saved benchmark: $outFile"
Write-Host "($SampleCount samples averaged, ${SampleIntervalSeconds}s apart)"
Write-Host ""

$previousFile = Get-ChildItem -Path $OutputDir -Filter "*.json" |
    Where-Object { $_.FullName -ne (Resolve-Path -LiteralPath $outFile).Path } |
    Sort-Object Name -Descending |
    Select-Object -First 1

if ($null -eq $previousFile) {
    Write-Host "No previous benchmark found in $OutputDir -- this is the first recorded baseline."
    Write-Host ""
    Write-Host ("{0,-50} {1,10}" -f "System", "ms/sec")
    foreach ($name in $averages.Keys) {
        Write-Host ("{0,-50} {1,10}" -f $name, $averages[$name])
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

$allCompareNames = @($averages.Keys) + @($prevSystems.Keys) | Select-Object -Unique |
    Sort-Object { if ($averages.Contains($_)) { -$averages[$_] } else { 0 } }

$header = "{0,-48} {1,10} {2,10} {3,10} {4,8}  {5}" -f "System", "Current", "Previous", "Delta", "Delta%", "Flag"
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
    Write-Host ("{0,-48} {1,10} {2,10} {3,10} {4,8}  {5}" -f $name, $cur, $prev, [math]::Round($delta, 2), $deltaPctStr, $flag)
}

Write-Host ""
if ($regressions.Count -gt 0) {
    Write-Host "Flagged regressions (>=$RegressionPercent% and >=${RegressionMinMs}ms increase): $($regressions -join ', ')"
} else {
    Write-Host "No regressions above threshold (>=$RegressionPercent% and >=${RegressionMinMs}ms)."
}
