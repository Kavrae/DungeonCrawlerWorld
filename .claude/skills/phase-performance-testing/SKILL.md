---
name: phase-performance-testing
description: Run a live benchmark of DungeonCrawlerWorld's ECS systems using the built-in Diagnostics engine, measured over a fixed range of simulation frames on a fixed seed, save the per-system ms/frame results to a timestamped file, and diff them against the most recent comparable run to surface regressions. Use this whenever the user asks to benchmark, profile, or performance-test the game (or specific systems), asks "how fast is X now", wants per-system or per-phase timing, mentions Log/diagnostics / the diagnostics engine / PerformanceProfile console output, or wants to check whether a recent change regressed frame cost -- even if they just say "run a benchmark" without naming the engine explicitly. Do not use this for the separate MSTest performance suite (`dotnet test --filter "TestCategory=Performance"`, e.g. AbilityScorePerformanceTests) -- that's a narrow unit-level check asserting how two code paths scale with entity count; this skill is for the live, whole-game, per-ECS-system profile.
---

# Phase performance testing

The game's `--benchmark-frames=START-END` flag (see `Program.cs`, `Engine/Diagnostics/BenchmarkFrameRange.cs`) makes `FrameRangeBenchmark` total every instrumented cost -- each system in `SystemManager`, each `EventBus` event, each window's Update/Draw -- across simulation frames `[START, END)`, then write one `Log/diagnostics/benchmark-<timestamp>-<pid>.json` the moment frame END begins. This is the only way to see real per-system cost at the game's actual scale (`FloorBuilder.PopulateFloor` populates the same ~2.6M-entity TestMapBuilder map `GameLoop.InitialEntityCapacity` is sized for) -- the checked-in `AbilityScorePerformanceTests` only measures two isolated code paths, not the whole system graph under load.

## Why frames and a seed, not wall-clock samples

Frame cost depends on what the world is doing -- map layout, which NPCs meet, how fights go. Two things used to make runs incomparable:

- **No seed.** Without `--seed=N`, `RandomSeed.Parse` picks a random one, so every run was a different world. Every benchmark before 2026-09-10 had this problem.
- **Wall-clock sampling.** The old workflow polled `latest.json` (a rolling one-second window) every 5 real seconds. Startup time varies run to run, so those samples landed on different simulation frames. Two same-seed runs of the same code still differed by up to 57% per system (2026-09-11).

A fixed seed makes the simulation repeat frame for frame; a fixed frame range makes the measurement cover exactly those frames. What's left is machine noise (GC, CPU clocks, other processes). **Update** costs are the comparable part. **Draw** costs per frame also depend on frame pacing -- a game that falls behind real time runs several Updates per Draw -- so the report records wall-clock time for the range and the script warns when the run fell behind.

`latest.json` (from `--diagnostics=frame`) still works for watching live costs while playing; it just isn't the benchmark.

## Step 1 — build

```bash
dotnet build DungeonCrawlerWorld.sln
```

## Step 2 — run the benchmark

```powershell
powershell -NoProfile -File .claude/skills/phase-performance-testing/scripts/Invoke-FrameBenchmark.ps1
```

The script does the whole run:
1. Refuses to start if a `DungeonCrawlerWorld` is already running (it may be someone's session, and two instances skew each other).
2. Launches `DungeonCrawlerWorld/bin/Debug/net10.0/DungeonCrawlerWorld.exe --diagnostics=frame --seed=1 --benchmark-frames=600-3600`.
3. Waits for the benchmark file carrying *that process's* pid (so a stale file can't be picked up), then closes the game. Expect about 70-80s: population, then 3600 simulation frames at 60/s.
4. Checks the report's seed and frame range match what it asked for, saves a timestamped JSON under `Log/phase-benchmarks/` (gitignored), and diffs against the most recent saved run with the **same seed, same frame range and frame-range sampling**. Older wall-clock files are skipped.

Defaults: `-Seed 1`, `-StartFrame 600` (frames before it are JIT warm-up and the opening moves), `-EndFrame 3600` (3000 frames = 50 simulated seconds). Change them only deliberately -- a different seed or range starts a new baseline. Anything that grew by both ≥20% and ≥0.02 ms/frame is flagged `REGRESSION` (tune with `-RegressionPercent`/`-RegressionMinMsPerFrame`). Units are **ms per simulation frame**; the frame budget at 60fps is 16.67ms.

`-NoProfile` matters: without it the user's PowerShell profile can change the starting directory, and the script's relative paths then aren't found.

If it times out with the window open, the game is probably paused or showing a blocking notification -- simulation frames only advance while `GameLoop.Update`'s pause/menu gate is open, so the range never completes.

## Step 3 — confirm the game is gone

The script closes the game in a `finally`, including on failure. Check anyway:

```bash
tasklist | grep -i DungeonCrawler   # should print nothing
```

## Reporting results

Show the user the printed comparison table (or the baseline table on a first run), call out any flagged regressions by name with their current/previous ms/frame and %, and note the saved file path. If the script warned that a run fell behind real time, say Draw numbers aren't comparable for it. If nothing regressed, say so plainly rather than just dumping the table -- "no regressions above threshold" is itself the useful answer most of the time.
