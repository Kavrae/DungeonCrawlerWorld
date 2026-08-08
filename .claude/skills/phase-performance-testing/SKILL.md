---
name: phase-performance-testing
description: Run a live wall-clock benchmark of DungeonCrawlerWorld's ECS systems using the built-in PhaseProfiler, save the per-system ms/sec results to a timestamped file, and diff them against the most recent prior run to surface regressions. Use this whenever the user asks to benchmark, profile, or performance-test the game (or specific systems), asks "how fast is X now", wants per-system or per-phase timing, mentions the phase profiler / PerformanceProfile console output, or wants to check whether a recent change regressed frame cost -- even if they just say "run a benchmark" without naming the profiler explicitly. Do not use this for the separate MSTest performance suite (`dotnet test --filter "TestCategory=Performance"`, e.g. AbilityScorePerformanceTests) -- that's a narrow unit-level check with its own hand-recorded baseline constants; this skill is for the live, whole-game, per-ECS-system profile.
---

# Phase performance testing

`Engine/Diagnostics/PhaseProfiler.cs` is a rolling once-per-second cost tracker already wired into the real game (`DungeonCrawlerWorld/GameLoop.cs` sets it as both `SystemManager.Profiler` and `EventBus.Profiler`). While the game runs, it dumps a ranked list of every ECS system's ms-per-second cost to the console, roughly every 5 seconds:

```
[PerformanceProfile] Top phases (ms spent in the last second):
[PerformanceProfile]   EcsContext.Update (all systems): 674.1ms
[PerformanceProfile]   TestCombatBehaviorSystem: 370.4ms
[PerformanceProfile]   StatModifierExpirySystem: 81.6ms
...
```

This is the only way to see real per-system cost at the game's actual scale (`FloorBuilder.PopulateFloor` populates the same ~2.6M-entity TestMapBuilder map `GameLoop.InitialEntityCapacity` is sized for) — the checked-in `AbilityScorePerformanceTests` only measures two isolated code paths, not the whole system graph under load. Capturing this data well requires a few things that aren't obvious from the code, covered below.

## Step 1 — bypass `dotnet run`

`DungeonCrawlerWorld.csproj` sets `<OutputType>WinExe</OutputType>` (so the FNA window doesn't have a console lurking behind it). That's fine when a person double-clicks the exe, but it means `dotnet run`'s extra process hop does **not** faithfully forward the child's console output through any redirection — not shell `>`, not `nohup ... &`, not even the Bash tool's own `run_in_background` output capture. The window opens, renders, and stays fully responsive (`Get-Process | select Responding, CPU, MainWindowTitle` shows CPU climbing normally), but the redirected file stays at 0 bytes forever. This is a real dead end, not a "wait longer" problem — don't spend time waiting it out.

The fix: build once, then invoke the **built exe directly**, skipping `dotnet run` entirely.

```bash
dotnet build DungeonCrawlerWorld.sln
```

Then launch the exe itself (no `dotnet` prefix, no `dotnet run`) with the Bash tool's `run_in_background: true`. Its own managed output capture works fine for a direct exe invocation — this is the one method that actually delivers console output:

```
DungeonCrawlerWorld/bin/Debug/net10.0/DungeonCrawlerWorld.exe
```

Note the output file path the tool reports back (e.g. `...\tasks\<id>.output`) — that's the log you'll capture from and eventually feed to the parser script.

## Step 2 — wait for the game to actually start ticking

Population of a 2.6M-entity map takes real time before the update loop (and therefore any profiler output) begins. Don't assume a fixed delay — wait for the condition instead. Use Monitor (or a Bash `until` loop) to watch for the first report line, with a generous timeout (2-3 minutes covers slow builds/population):

```
until grep -q "PerformanceProfile" "<output file path>"; do sleep 1; done
```

If this never fires within a few minutes, don't just keep waiting — check `Get-Process -Id <pid> | select Responding, MainWindowTitle` first. If the window is open and responding but truly silent, the game likely booted into a paused or blocking-notification state (see `GameLoop.Update`'s `IsPaused || HasBlockingNotification || IsAnyWindowOpen` gate — `ReportTopPhases` only fires when none of those are true). That's a real "the game is waiting on player input" state, not a redirection problem this time — flag it to the user rather than guessing at a fix.

## Step 3 — separate the startup spikes from steady state

The first several reports after the game starts ticking are inflated by real warmup costs (JIT tiering up from Tier0 to optimized code, GC settling, first-touch allocation/cache effects) — not a one-time spike but a genuine gradual decline. A clean instrumented run (PowerShell polling `EcsContext.Update (all systems)` and `TestCombatBehaviorSystem` every 3 real seconds, timestamped from the first report) measured this directly: `EcsContext.Update` started around 662-670ms/sec and `TestCombatBehaviorSystem` around 296-302ms/sec in the first ~10 seconds, declined fairly steadily through roughly the 50-55 second mark (to ~597-613ms and ~267-279ms respectively), and from there stayed flat within normal noise (±10ms) through at least the 120-second mark. So **the settle point is around 50-55 real seconds, not a smaller number** — averaging in anything captured earlier than that measurably inflates every system's reported cost, and inconsistently so depending on exactly when you stopped waiting.

Because block cadence is frame-count-based (not wall-clock — see `ProfileReportIntervalFrames`), it isn't fixed across runs or machines, so don't hardcode a block count either. Tie the skip to real elapsed time instead:

1. Wait for the first report (Step 2).
2. `sleep 60` (a single bounded wait for a real timed measurement — this isn't a poll loop, so a plain background `sleep` is the right tool here; 60s gives comfortable margin over the measured ~55s settle point).
3. At that mark, count how many report blocks exist in the log so far — this count becomes `-SkipBlocks` for the parser script:
   ```powershell
   (Select-String -Path "<output file path>" -Pattern 'Top phases \(ms spent').Count
   ```
4. `sleep 45` or longer to accumulate steady-state samples on top of the warmup blocks — cadence in the measured run was roughly one report per 4 real seconds, so 45s comfortably yields 10+ steady-state samples.
5. Stop the process (Step 5) and run the parser (Step 4 below) against the full log with that `-SkipBlocks` count.

If you want to re-verify this on a different machine or after a change that plausibly shifts warmup cost (e.g. touching startup/JIT-heavy paths), rerun the timeline-polling approach above rather than trusting the 60s figure blindly — it was measured once, on one machine, and machine-to-machine JIT/GC warmup time can vary.

## Step 4 — parse, save, and diff

Run the bundled script from the repo root, passing the captured log and the `-SkipBlocks` count from Step 3:

```powershell
powershell -File .claude/skills/phase-performance-testing/scripts/Parse-PhaseProfile.ps1 -LogPath "<output file path>" -SkipBlocks <count>
```

It averages every system's ms/sec across the steady-state blocks (a system missing from a block is a true 0ms that block, not missing data — `PhaseProfiler` only records a name when `Record()` was actually called for it), saves a timestamped JSON under `Log/phase-benchmarks/` (already gitignored — this is local, machine-specific data, not something to commit), and automatically finds and diffs against the most recently saved file in that directory. The printed table shows current vs. previous ms/sec, delta, delta%, and flags anything that grew by both ≥20% and ≥1ms as `REGRESSION` (tune with `-RegressionPercent` / `-RegressionMinMs` if the defaults are too noisy or too lax for what you're chasing). If there's no prior file yet, it just prints the current results as the new baseline — that's expected on the first run.

## Step 5 — clean up

The game doesn't exit on its own. Kill it and confirm it's actually gone before wrapping up:

```powershell
Get-CimInstance Win32_Process -Filter "Name='DungeonCrawlerWorld.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

```bash
tasklist | grep -i DungeonCrawler   # should print nothing
```

## Reporting results

Show the user the printed comparison table (or the baseline table on a first run), call out any flagged regressions by name with their current/previous ms/sec and %, and note the saved file path. If nothing regressed, say so plainly rather than just dumping the table — "no regressions above threshold" is itself the useful answer most of the time.
