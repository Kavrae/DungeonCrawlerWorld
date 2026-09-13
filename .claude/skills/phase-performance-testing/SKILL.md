---
name: phase-performance-testing
description: Run a live benchmark of DungeonCrawlerWorld's ECS systems using the built-in Diagnostics engine, measured over a fixed range of simulation frames on a fixed seed -- windowed, headless, or as a headless A/B of a saved baseline build against the current one -- save the per-system ms/frame results to a timestamped file, and diff them to surface regressions. Use this whenever the user asks to benchmark, profile, or performance-test the game (or specific systems), asks "how fast is X now", wants per-system or per-phase timing, mentions Log/diagnostics / the diagnostics engine / PerformanceProfile console output, or wants to check whether a recent change regressed frame cost -- even if they just say "run a benchmark" without naming the engine explicitly. Do not use this for the separate MSTest performance suite (`dotnet test --filter "TestCategory=Performance"`, e.g. AbilityScorePerformanceTests) -- that's a narrow unit-level check asserting how two code paths scale with entity count; this skill is for the live, whole-game, per-ECS-system profile.
---

# Phase performance testing

The game's `--benchmark-frames=START-END` flag (see `Program.cs`, `Engine/Diagnostics/BenchmarkFrameRange.cs`) makes `FrameRangeBenchmark` total every instrumented cost -- each system in `SystemManager`, each `EventBus` event, each window's Update/Draw -- across simulation frames `[START, END)`, then write one `Log/diagnostics/benchmark-<timestamp>-<pid>.json` the moment frame END begins. This is the only way to see real per-system cost at the game's actual scale (`FloorBuilder.PopulateFloor` populates the same ~2.6M-entity TestMapBuilder map `GameLoop.InitialEntityCapacity` is sized for) -- the checked-in `AbilityScorePerformanceTests` only measures two isolated code paths, not the whole system graph under load.

## Why frames and a seed, not wall-clock samples

Frame cost depends on what the world is doing -- map layout, which NPCs meet, how fights go. Two things used to make runs incomparable:

- **No seed.** Without `--seed=N`, `RandomSeed.Parse` picks a random one, so every run was a different world. Every benchmark before 2026-09-10 had this problem.
- **Wall-clock sampling.** The old workflow polled `latest.json` (a rolling one-second window) every 5 real seconds. Startup time varies run to run, so those samples landed on different simulation frames. Two same-seed runs of the same code still differed by up to 57% per system (2026-09-11).

A fixed seed makes the simulation repeat frame for frame; a fixed frame range makes the measurement cover exactly those frames. What's left is machine noise (GC, CPU clocks, other processes). **Update** costs are the comparable part. **Draw** costs per frame also depend on frame pacing -- a game that falls behind real time runs several Updates per Draw -- so the report records wall-clock time for the range and the script warns when the run fell behind.

**Compare within one session, not across days.** Same code, same seed, same frames measured 4.05 ms/frame for `EcsContext.Update` on the evening of 2026-09-10 and 2.3-2.5 the next morning -- the machine itself (power plan, thermals, background load) moved everything ~40%, all systems together. Back-to-back runs agree within ~4% on the total and ~10% on individual mid-sized systems. So the saved "previous" run is only a valid baseline if it was recorded in the same sitting; to measure a change, record a fresh baseline immediately before it. A whole-table shift where every row moves by the same percentage is the machine, not the code. (`--diagnostics=frame` itself costs ~4%, measured the same morning.)

`latest.json` (from `--diagnostics=frame`) still works for watching live costs while playing; it just isn't the benchmark.

## Headless vs windowed

`--headless` (see `DungeonCrawlerWorld/HeadlessBenchmark.cs`) builds the same world with the same seed and runs the simulation back to back with no window, no Presentation and no 60fps pacing, then exits.

| | Windowed | Headless |
|---|---|---|
| Time per run (population + frames 600-3600) | ~65-75s | **~8s** |
| Measures Draw / Presentation | yes | no |
| Run-to-run spread, same session | ~4% total, up to ~12% | **~0.2-2.5% total, ~1-2% per system** |
| Determinism check | no | **yes** (world fingerprint) |

Headless numbers read **about half** the windowed ones for the same code (`EcsContext.Update` 1.12 vs 2.2-2.5 ms/frame, 2026-09-11) and some systems far more (ActionLock 0.046 vs 0.21). Likely causes: Draw between frames evicts simulation data from cache, and a paced game idles long enough for the CPU to downclock -- the ECS here is memory-latency-bound, so both hit it hard. Neither is proven. So: **headless for comparing simulation code, windowed for what the player actually pays.** Never compare a headless number with a windowed one; the script keeps them apart.

Each headless run prints a fingerprint of the final world (entity count, pool sizes, every position, every health value). Same build + same seed must give the same fingerprint; the script warns if not, because then the runs measured different workloads.

## Step 1 — build

```bash
dotnet build DungeonCrawlerWorld.sln
```

## Step 2 — pick the mode

All modes run through one script (always with `-NoProfile`: without it the user's PowerShell profile can change the starting directory, and the script's relative paths then aren't found):

```powershell
powershell -NoProfile -File .claude/skills/phase-performance-testing/scripts/Invoke-FrameBenchmark.ps1 [mode]
```

**Before/after a change -- A/B (preferred).** Machine drift hits both sides equally, so this is the one comparison that holds up across a session:
1. Build the *before* code, then `-SaveBaseline`. Copies the built `bin` folder to `Log/phase-benchmarks/baseline-build/` (gitignored), with its commit noted. No git commands involved.
2. Make the change, rebuild, then `-Compare`. Runs baseline and current builds headless, interleaved (A B, B A, A B), 3 per side by default (~50s total), and prints per-system medians, delta %, and A's own run-to-run spread. `REGRESSION`/`IMPROVED` needs ≥10%, ≥0.02 ms/frame, **and** B's median outside A's min-max range. It also says whether A and B simulated the same world: if the change altered gameplay, the fingerprints differ and some rows differ because the game did different things. Saved as `Log/phase-benchmarks/ab-<timestamp>.json`.

**"How fast is the simulation now" -- `-Headless`.** 5 runs (~40s), medians with per-row spread, diffed against the last saved headless run of the same seed and range.

**"What does the player pay" -- no flag (windowed).** One paced run with Draw included (~70s), diffed against the last saved windowed run. That saved run is only a valid baseline if made in the same sitting (see above) -- the script says so every time.

**Debug or Release -- `-Configuration`.** Defaults to `Debug`; `-Configuration Release` measures `bin/Release` (build it first with `dotnet build DungeonCrawlerWorld.sln -c Release`). Release reads roughly half of Debug (headless `EcsContext.Update` 0.68 vs 1.23 ms/frame, 2026-09-11) and weights costs differently -- a tight sequential loop gains far more than scattered lookups -- so use Release for any decision about what ships. Results, previous-run lookups and saved baselines (`baseline-build-debug`/`baseline-build-release`) are kept per configuration and never compared across.

Shared defaults: `-Seed 1`, `-StartFrame 600` (frames before it are JIT warm-up and the opening moves), `-EndFrame 3600` (3000 frames = 50 simulated seconds). Change them only deliberately -- a different seed or range starts a new baseline. `-Repeat N` sets runs per side. Units are **ms per simulation frame**; the frame budget at 60fps is 16.67ms.

The script refuses to start if a `DungeonCrawlerWorld` is already running (it may be someone's session, and two instances skew each other), matches each report to *its own* process id so a stale file can't be picked up, and checks the report's seed and range match what it asked for. If a windowed run times out with the window open, the game is probably paused or showing a blocking notification -- simulation frames only advance while `GameLoop.Update`'s pause/menu gate is open.

## Step 3 — confirm the game is gone

The script closes the game in a `finally`, including on failure. Check anyway:

```bash
tasklist | grep -i DungeonCrawler   # should print nothing
```

## Reporting results

Show the user the printed comparison table (or the baseline table on a first run), call out any flagged regressions by name with their current/previous ms/frame and %, and note the saved file path. Say which mode it was (headless or windowed, A/B or against a saved run), and pass on any determinism or different-world warning -- those change what the numbers mean. If the script warned that a run fell behind real time, say Draw numbers aren't comparable for it. If nothing regressed, say so plainly rather than just dumping the table -- "no regressions above threshold" is itself the useful answer most of the time.
