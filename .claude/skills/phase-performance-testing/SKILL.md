---
name: phase-performance-testing
description: Run a live wall-clock benchmark of DungeonCrawlerWorld's ECS systems using the built-in Diagnostics engine, save the per-system ms/sec results to a timestamped file, and diff them against the most recent prior run to surface regressions. Use this whenever the user asks to benchmark, profile, or performance-test the game (or specific systems), asks "how fast is X now", wants per-system or per-phase timing, mentions Log/diagnostics / the diagnostics engine / PerformanceProfile console output, or wants to check whether a recent change regressed frame cost -- even if they just say "run a benchmark" without naming the engine explicitly. Do not use this for the separate MSTest performance suite (`dotnet test --filter "TestCategory=Performance"`, e.g. AbilityScorePerformanceTests) -- that's a narrow unit-level check with its own hand-recorded baseline constants; this skill is for the live, whole-game, per-ECS-system profile.
---

# Phase performance testing

`Engine/Diagnostics/DiagnosticsEngine.cs` is opt-in via a `--diagnostics=` flag on the built exe (see `Program.cs`/`DiagnosticsFeaturesParser`). Once enabled, it writes a live per-system/per-window ms-per-second breakdown straight to `Log/diagnostics/latest.json` (plus a human-readable `latest.txt`), refreshed roughly every 5 real seconds while the game runs. This is the only way to see real per-system cost at the game's actual scale (`FloorBuilder.PopulateFloor` populates the same ~2.6M-entity TestMapBuilder map `GameLoop.InitialEntityCapacity` is sized for) -- the checked-in `AbilityScorePerformanceTests` only measures two isolated code paths, not the whole system graph under load.

This replaces an older console-scraping workflow (recoverable from git history if ever needed) -- the engine now writes structured JSON directly, so there's no `dotnet run` redirection workaround and no manual "wait ~60s then count console blocks" step to reason about.

## Step 1 — build and launch with diagnostics enabled

```bash
dotnet build DungeonCrawlerWorld.sln
```

Then launch the built exe via the Bash tool with `run_in_background: true`:

```
DungeonCrawlerWorld/bin/Debug/net10.0/DungeonCrawlerWorld.exe --diagnostics=all
```

`--diagnostics=frame,startup` is the actual minimum this workflow needs -- `frame` for the benchmark itself, `startup` for Step 2's stability signal below. `all` additionally captures memory (per-component-type bytes) and leak-indicator data in the same run at negligible extra cost, which is usually worth having anyway -- see `latest.json`'s `memory`/`leaks` sections if the user's asking about either.

## Step 2 — wait for real steady state, not a fixed sleep

Population of a 2.6M-entity map takes real time before the game starts ticking, and JIT/GC warmup takes a bit longer after that before per-system costs settle. Rather than guessing a sleep duration, wait for the engine's own stability signal: `Log/diagnostics/startup-*.json` is written exactly once, the moment `StartupProfiler` detects steady state (real measured `EcsContext.Update` cost holding steady across several rolling windows -- see its own doc comment; deliberately not a raw gap between frames, which a fixed-timestep loop smooths out misleadingly).

```bash
until ls Log/diagnostics/startup-*.json 2>/dev/null; do sleep 2; done
```

Give this a generous timeout (2-3 minutes covers slow builds/population). As of a 2026-08-16 measurement (confirmed twice, same session) this settles in roughly 5 real seconds after the game starts responding -- much faster than an earlier ~50-55s figure measured before a round of perf/cleanup work landed. Don't trust either number indefinitely; if it looks off, re-derive it empirically (this step's own `startup-*.json` output tells you directly, via `TimeToStableMilliseconds`) rather than assuming.

If the file never appears within the timeout, check `Get-Process -Id <pid> | select Responding, MainWindowTitle` -- if the window is open and responding but nothing's showing up, the game likely booted into a paused/blocking-notification state (`GameLoop.Update`'s `IsPaused || HasBlockingNotification || IsAnyWindowOpen` gate, which also gates `StartupProfiler.Tick`) rather than a tooling problem.

## Step 3 — sample and average

A single read of `latest.json` reflects only the last full second's snapshot, so it's still worth smoothing across a few samples the same way the old console-block averaging did. Run the bundled script, which polls `latest.json` several times a few seconds apart, averages each system/window's ms/sec across those samples, saves a timestamped JSON under `Log/phase-benchmarks/` (already gitignored), and diffs against the most recently saved file there:

```powershell
powershell -File .claude/skills/phase-performance-testing/scripts/Parse-DiagnosticsReport.ps1
```

Defaults to 8 samples, 5 seconds apart (~40s total, matching `latest.json`'s own ~5s refresh cadence so each poll sees fresh data) -- override with `-SampleCount`/`-SampleIntervalSeconds` for a longer/shorter measurement window. The printed table shows current vs. previous ms/sec, delta, delta%, and flags anything that grew by both ≥20% and ≥1ms as `REGRESSION` (tune with `-RegressionPercent`/`-RegressionMinMs`). If there's no prior file yet, it just prints the current results as the new baseline -- expected on the first run. Entries are named `Category.Group.Item` (e.g. `Update.SystemManager.MovementSystem`, `Draw.BaseWindows.MapWindow`), grouped first by Update vs Draw the same way `latest.json` itself is.

## Step 4 — clean up

The game doesn't exit on its own. Kill it and confirm it's actually gone before wrapping up:

```powershell
Get-CimInstance Win32_Process -Filter "Name='DungeonCrawlerWorld.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

```bash
tasklist | grep -i DungeonCrawler   # should print nothing
```

## Reporting results

Show the user the printed comparison table (or the baseline table on a first run), call out any flagged regressions by name with their current/previous ms/sec and %, and note the saved file path. If nothing regressed, say so plainly rather than just dumping the table -- "no regressions above threshold" is itself the useful answer most of the time.
