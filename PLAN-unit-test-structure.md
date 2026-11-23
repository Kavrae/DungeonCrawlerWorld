# Unit tests: one stable structure, safe for worker threads

Plans the user's request of 2026-11-22: review the test projects, give them a consistent structure that stays stable
when work moves onto worker threads (`PLAN-multithreading.md`, first expected in `NpcDecisionSystem`), and remove
redundant tests. It also finishes TODO.md's "TOP PRIORITY: Investigate the tests that fail on some runs".

**Status:** written 2026-11-22 from a read of the code, a syntax-tree scan of every test and repeated runs. Decisions
1-4 accepted the same day, as recommended. Phase 0 done 2026-11-22; it found three failing `Tests` methods (see its
results). Phase 1 built and checked 2026-11-22, with one cause still open (its step 8). Phases 2 to 5 built and
checked the same day, Phase 6 on 2026-11-23. **Complete**, with that one cause open in TODO.md.

## What the review found

### The suites today

| Project | Test methods | Notes |
|---|---|---|
| `Tests` | 4,378 | 483 files, 89k lines. Engine, UIEngine, Game and Presentation in one assembly |
| `Workshop.Tests` | 357 | 38 files, flat |
| `Analyzers.Tests` | 8 | 2 files |

Both large projects use `[assembly: Parallelize(Scope = MethodLevel)]`. `[DoNotParallelize]` is on 97 classes and 23
methods, so about 1,000 of `Tests`' methods run one at a time. That is the UI: its one font service isn't thread-safe.

### Run stability

| Run | Result |
|---|---|
| `Workshop.Tests`, idle, 12 runs | All passed |
| `Workshop.Tests`, 36 busy processes on 12 cores, 4 runs | All passed |
| `Tests`, 6 runs | All passed |
| `Workshop.Tests` with the launcher and without the thread-pool setting from before 2026-11-15, 10 runs | None of the five named tests failed |

**The five failures of 2026-11-14 have not been reproduced, so their cause is not established.** One of the five is
explained and fixed: `DiagnosticsLinkListener` marked a session ended before freeing its connection slot, proven by
widening the gap. The other four are launcher tests. The commit after the failures both stopped the launcher blocking
in its exit handler and raised the thread-pool minimum to 64 (`Workshop.Tests/AssemblySetup.cs`), whose comment
describes tests starving the pool. That is the likeliest cause, but removing both did not bring the failures back here.

### Hazards, most serious first

1. **Parallel tests share a catalog that writes on read.** `BlueprintTestContext` holds one built session for the whole
   run. `BlueprintTests` and `BlueprintCompositionTests` (30 methods, parallel) build races through it, and a race's
   derived action goes through `ActionCatalog.GetOrCreateDerived`, an unlocked `Dictionary` write. Found by reading; no
   failure has been observed. It is the same hazard `PLAN-multithreading.md` lists for forked sections.
2. **A class breaks the font rule.** `EffectiveActionPresentationTests` uses `TestFonts.Shared` without
   `[DoNotParallelize]`. `TestFonts` records that this corrupted glyph measurements. Nothing enforces the rule.
   `PointerStateTests` reaches `TestUiInputController` without the attribute and needs the same check.
3. **`Workshop.Tests` has five `FontService` instances** (`ContentInspectorTests`, `ControlGalleryTabTests`,
   `DebugTabHarness`, `RecordBrowserKeysTests`, `RecordRowTests`). `TestFonts` records that more than one caused an
   intermittent access violation at test-host shutdown.
4. **Tests block the pool threads they depend on.** Process and socket tests sleep in a loop on a pool thread while the
   work they wait for needs one too. The 64-thread minimum hides this in `Workshop.Tests`; `Tests` has no such setting.
5. **Wall-clock assertions.** `StartupProfilerTests.WarmupThenSteady_...` asserts an upper bound of 200 ms, which a
   preempted thread can miss. `GameLauncherTests.Stop_DuringTheBuild_...` relies on fixed 300 ms and 500 ms sleeps.
6. **Tests read this machine's state.** `DiagnosticsReportFolderTests` opens whatever is in `Log/`, so a benchmark
   writing a report during a test run can fail it, and another machine tests something else.
7. **Leftovers.** `WhileTheGameRuns_PlayStartsNothing` leaves a process running for about five seconds.
   `InventoryManagementWindowTests` sleeps to order stacks that are ordered by a counter, so those sleeps do nothing.
   The repository-root search is written five times.

### What worker threads will need from the tests

Already right: `NpcDecisionSystem` chooses for a whole bucket and then acts, its rolls are derived per entity, and a
behavior that writes while choosing throws in Debug, with a test.

Missing:
- **Nothing tests that one creature's choice is independent of another's**, which is what makes a fork safe.
- **The world fingerprint is too coarse for this.** `HeadlessBenchmark.Fingerprint` hashes positions, health and pool
  counts. A different decision that hasn't yet moved or hurt anything is invisible to it.
- **Process-wide counters and handles depend on order** (`NextStackInstanceId`, `NextAcquiredSequence`, `ItemIds`,
  `EntityIdentities`). They are thread-safe, but their values differ between sessions in one process, so a determinism
  check must never compare them.

### Redundant tests

- **Exact duplicates: 3 to remove.**
  - `FormatModifierRow_FlatMagnitudeIsWholeNumber_NoTrailingDecimal` (same as `..._AdditiveOutgoingDamageBuff_ReadsAsPlusDamage`)
  - `ClosingTheOnlyFocusedWindow_WithNoDefaultFocusWindowConfigured_LeavesFocusNull` (same as `ClosingTheFocusedWindow_ClearsTheControllersFocusedWindow`)
  - `IsImmune_NoEventBusOrPlayerQuery_DoesNotThrow` (same as `IsImmune_MatchingEffectType_ReturnsTrue`; it now passes both)
- **Wholly contained in a longer test: 3 to remove.** Same setup, action and assertion, and the longer test continues.
  - `PressingOverATitleButton_SetsPressedPressable` (in `ReleasingTheMouse_ClearsPressedPressable`)
  - `PressingOverATitleButton_SetsIsPressedOnTheButton` (in `ReleasingTheMouse_ClearsIsPressedOnTheButton`)
  - `FirstUpdate_NoPresetReference_OutsideLocalButSameNeighborhood_Neighborhood` (in `PlayerLeavesTheMap_...`)
- **Same test with different values: 63 groups, 141 methods, up to 78 fewer.** These become `[DataRow]`s and keep every
  case. Largest: `StringUtilityTests`, `HealthWindowTests`, `ItemComparisonHighlightingTests`, `TargetShapeResolverTests`.
- **No assertion: 13.** Most are "does not throw" tests, where not throwing is the behavior. Each is reviewed; none is
  removed for this reason alone.
- **Duplicates in meaning but not in text** can't be found by a scan. Phase 4 uses coverage to nominate them, and each
  is read before it goes.

## The structure

### Every test is one of three kinds

| Kind | What it may touch | How it runs |
|---|---|---|
| **Isolated** (the default) | Only what it builds itself, its own temp folder, immutable data | In parallel |
| **Shared read-only** | A frozen fixture from `Tests.Support`, which nothing can write through | In parallel |
| **Serial** | A named process-wide resource: fonts, `EngineHooks` channels | `[DoNotParallelize]`, enforced |

### Rules

1. **A fixture shared between tests is never written, including by a read that caches.** A test that spawns or builds
   entities makes its own session.
2. **A serial resource says so on its type** (`[SerialTestResource("Fonts")]`), and the build fails for a test class
   that reaches it without `[DoNotParallelize]` (a new analyzer rule, decision 2).
3. **No `Thread.Sleep` to wait for something.** A test awaits the event or task. Where there is none, it uses the one
   `TestWait.UntilAsync(condition, description)`, whose timeout fails the test by name. A timeout is only ever a
   failure path, never what makes a test pass.
4. **No wall-clock upper bounds** outside `TestCategory=Performance`.
5. **A test reads only checked-in files and its own temp folder**, never `Log/`, `%LocalAppData%` or another test's
   output.
6. **A test that starts a process or thread ends it**, on failure too.
7. **Determinism checks compare stable values** (Guids, entity keys, positions), never session handles or counters.

### Layout

```
Tests.Support/    new: TestWait, TestDirectory, TestRepository, TestFonts, the serial resource attributes
Tests/
  EngineTests/    ECS, Events, Math, Tags, Diagnostics, Persistence, ...
  UIEngine/
  GameTests/      Modules, Blueprints, Effects, World, Floors, Spawning, ...
  Presentation/
  Architecture/   every *ArchitectureTests file
  Determinism/    seeded-world and worker-count checks (Phase 6)
  Fixtures/       the helpers that were at the project root
Workshop.Tests/   Shell/, DebugTab/, Editing/, Browsing/, MapAreas/, Fixtures/
Analyzers.Tests/
```

`Tests.Support` references no IDE project, so `WorkshopArchitectureTests` still holds. A file is named for the class it
tests, and test names stay `Subject_Situation_Result`, which 99% already follow.

## Decisions for the user

1. **Add a `Tests.Support` project?** Recommended. `Workshop.Tests` needs the same single font service and wait helper
   as `Tests`, and neither may reference the other. The alternative is linked source files.
2. **Enforce the serial rule with an analyzer?** Recommended, as a new `CAD` rule in `Cadence/Analyzers`: it follows real
   symbols and fails at build. A source-scanning architecture test is cheaper but misses use through a helper.
3. **Keep real time in the frame-window tests?** Recommended. `FrameBudgetTracker` and `StartupProfiler` would need an
   injected clock on a per-frame path only to save about 9 seconds of sleeping, and tests shouldn't drive that design.
   Their upper-bound assertions still go.
4. **Move the folders?** Recommended, last and as one mechanical commit, because it touches nearly every file and
   conflicts with any work in progress. Skipping Phase 5 loses none of the stability.

## Phases

Each phase ends with its checks; stop after each for review.

### Phase 0 -- Baseline (no repository changes)

1. Record failure rates: 50 idle and 20 loaded runs of `Workshop.Tests`, 20 idle and 5 loaded of `Tests`.
2. Record line coverage per project (`--collect "Code Coverage"`) and each suite's duration.

**Check:** this plan holds a dated table of all three, and every failure's message and stack trace is saved.

**Results, 2026-11-22** (commit `44a190c9` plus the uncommitted listener fix and launcher test wait; 12 cores; "loaded"
is 36 busy processes). Run data, failure logs and coverage are in `Log/test-baseline-20261122` (not in git).

| Suite | Condition | Runs failing | Average | Range |
|---|---|---|---|---|
| `Workshop.Tests` (393 cases) | Idle | 0 of 50 | 6.8 s | 6-8 s |
| `Workshop.Tests` | Loaded | 0 of 20 | 54.0 s | 18-122 s |
| `Tests` (4,713 cases) | Idle | **1 of 20** | 40.2 s | 39-41 s |
| `Tests` | Loaded | **5 of 5** | 192.6 s | 113-298 s |

| Failing test | When | Message |
|---|---|---|
| `DiagnosticsLinkTests.EveryMessageType_ArrivesAsSent` | 5 of 5 loaded | No Hello message arrived within 10 s |
| `DiagnosticsLinkTests.TheIdeClosingMidRun_ClosesTheLinkWithOneErrorLine` | 5 of 5 loaded | No Hello message arrived within 10 s |
| `DiagnosticsLinkTests.WithTheLinkOn_ASimulationFrameAllocatesNothing` | 1 of 20 idle | Expected 0 bytes allocated, was 4,992 |

**Causes not yet known.** Two explanations were tested outside the suite and neither reproduced a failure: a link
connecting while 300 work items held the thread pool, and 40 rounds of 5,000 frames under constant forced collections.

| Line coverage | `Tests` | `Workshop.Tests` | `Analyzers.Tests` |
|---|---|---|---|
| Engine | 91.4% | 39.2% | 0.7% |
| Game | 87.9% | 35.1% | |
| UIEngine | 71.6% | 52.1% | |
| Presentation | 67.3% | 0% | |
| SpriteFormat | 88.6% | 56.4% | |
| Workshop | | 71.3% | |
| Builders | | 51.9% | |
| Analyzers | | | 93.4% |

### Phase 1 -- Fix the known hazards

1. Give `BlueprintTestContext`'s entity-building tests their own session; keep the shared one for reads only.
2. Mark `EffectiveActionPresentationTests` serial, and `PointerStateTests` if it reaches fonts.
3. One `FontService` in `Workshop.Tests`.
4. Stop the process `WhileTheGameRuns_PlayStartsNothing` leaves behind.
5. Remove the inventory sleeps and the 200 ms upper bound.
6. Replace `DiagnosticsReportFolderTests`' machine state with small checked-in reports of each kind.
7. Find and fix why the link's Hello doesn't arrive under load: record the link's own error line, whether the receiver
   accepted, and the thread-pool state at the timeout. If the game's link is at fault, fix it there.
8. Find what allocated the 4,992 bytes, and fix it if a frame really allocates. If it's the runtime's one-off work, make
   the test measure so that this can't count, without loosening its zero.

**Check:**
- Both suites pass 20 times idle and 5 times loaded.
- `Workshop.Tests` passes with an empty `Log/` folder.
- No test process is left running after a run.

**Results, 2026-11-22:**

| Suite | Condition | Before (Phase 0) | After |
|---|---|---|---|
| `Workshop.Tests` (394 cases) | Idle | 0 of 50 failing | 0 of 20 failing, 6.3 s average |
| `Workshop.Tests` | Loaded | 0 of 20 failing | 0 of 5 failing, 25.8 s average |
| `Tests` (4,713 cases) | Idle | 1 of 20 failing | 0 of 20 failing, 41.5 s average |
| `Tests` | Loaded | **5 of 5 failing** | **0 of 5 failing**, 190.6 s average |

- **Step 7, cause found and fixed in the engine.** The receiver had accepted the connection, but `DiagnosticsLink` waited
  on `ConnectAsync(...).Wait(5 s)`, whose completion comes through the busy thread pool, then closed a working link.
  Its sender thread now connects synchronously, using no pool thread; disposing the link still ends a connect in progress.
- **Step 8, cause not found.** The 4,992-byte allocation did not recur in 45 instrumented idle runs, the 20 idle runs
  above or 220 runs of the test alone, under four JIT configurations. A 1-in-20 failure can still pass 20 runs, so it is
  not shown fixed. The test stays at exactly zero, with no retry, and a failure now names each allocating frame, its
  bytes, and the methods compiled and gen 0 collections during the measurement.
- The report tests read seven checked-in reports, one of each kind, and pass from a copy with no repository or `Log/`
  above it. A new test fails if a kind has no fixture.
- `PointerStateTests` reaches no font service, so it stays parallel.

### Phase 2 -- `Tests.Support` and enforcement

1. The project, with `TestRepository` (replacing five copies), `TestDirectory`, `TestWait` and `TestFonts`.
2. The serial resource attribute and the analyzer rule, with `Analyzers.Tests` cases.
3. An architecture test for rules 3 and 5: no `Thread.Sleep` or `Log/` path outside a short, named allowance.

**Check:**
- Removing `[DoNotParallelize]` from a font-using class fails the build, naming the class and resource.
- A class that reaches the fonts only through a helper fails the same way.
- A new `Thread.Sleep` in a test fails the architecture test.

**Results, 2026-11-22:**

| Check | Result |
|---|---|
| `[DoNotParallelize]` removed from a class naming the fonts (`EffectiveActionPresentationTests`) | Build fails, 3 `CAD0004` errors naming each test and `Fonts` |
| Removed from a class reaching them only through a helper (`DropdownTests`) | Build fails, 20 errors, each with the path through `ControlTestHarness` |
| Removed from a class subscribing to `EngineHooks` (`FrameCostNestingTests`) | Build fails, naming `EngineHooks` |
| Removed from a Workshop class (`RecordRowTests`) | Build fails, 4 errors |
| A new `Thread.Sleep`, and a new `"Log"` path | The architecture test fails, naming the file |
| Whole solution | Builds with no errors |
| `Analyzers.Tests` (47 cases) | Passes |
| `Workshop.Tests` (394 cases) | 0 of 10 idle and 0 of 3 loaded runs failing |
| `Tests` (4,720 cases) | 0 of 10 idle and 0 of 3 loaded runs failing |

- **No existing test broke the rule.** With Phase 1's fix in, `CAD0004` reports nothing across both suites.
- **`TestDirectory` holds only `TryDelete`.** Every temp folder was already cleaned up, so it replaces the two copies of
  the "tolerate a locked mod DLL" helper and adds no wrapper nothing needs.
- **The helper is `TestRepository`, not `RepositoryPaths`,** because the Workshop App already has an internal type of
  that name.
- **`CAD0004`'s limit:** it doesn't follow virtual or interface calls, or calls into another assembly.
- Nine files keep an exact `Thread.Sleep` allowance: two for real-time frame windows (decision 3), seven until Phase 3.

### Phase 3 -- Waits that don't block

1. Move process, socket and listener tests to `async Task` over `TestWait` or the real completion.
2. Replace `Stop_DuringTheBuild`'s fixed sleeps with waits on the launcher's state.
3. Remove `AssemblySetup`'s thread-pool minimum.

**Check:**
- `Workshop.Tests` passes 50 idle and 20 loaded runs with the default thread pool.
- The same under `DOTNET_ThreadPool_ForceMaxWorkerThreads=4`, which the blocking versions would struggle with.

**Results, 2026-11-22** (no thread-pool setting in either suite):

| Suite | Condition | Runs failing | Average |
|---|---|---|---|
| `Workshop.Tests` (394 cases) | Idle | 0 of 50 | 6.0 s |
| `Workshop.Tests` | Idle, 4 pool threads | 0 of 50 | 6.0 s |
| `Workshop.Tests` | Loaded | 0 of 20 | 24.1 s |
| `Workshop.Tests` | Loaded, 4 pool threads | 0 of 20 | 23.6 s |
| `Tests` (4,720 cases) | Idle | 0 of 10 | 42.7 s |
| `Tests` | Idle, 4 pool threads | 0 of 3 | 41.0 s |
| `Tests` | Loaded | 0 of 3 | 153.0 s |
| `Analyzers.Tests` (47 cases) | Idle | Passed | |

- Every wait on a process, socket, listener or rebuild is now `async` over `TestWait`, and `AssemblySetup`'s 64-thread
  minimum is gone. The only `Thread.Sleep`s left are the eight in the real-time frame-window tests (decision 3).
- `Stop_DuringTheBuild` waits for the build's own output line instead of sleeping. With the launcher's generation check
  removed it fails 10 of 10 runs, and on the real launcher it passes 10 of 10.
- The first Workshop loop was discarded: restoring that deliberately broken launcher kept its old timestamp, so the
  build didn't recompile it. The table is from the rerun after a forced rebuild.
- The blocking versions were not run under 4 pool threads, so this doesn't show how much they would have struggled.
- The 2026-11-14 launcher failures were never reproduced, so their exact cause stays unproven.

### Phase 4 -- Remove redundant tests

1. Remove the six listed above.
2. Fold the same-shape groups into `[DataRow]`s where the names differ only by input. A group whose names state
   different rules stays separate.
3. Review the 13 tests with no assertion.
4. Nominate further duplicates from per-class coverage, read each, and list them for the user before removing any.

**Check:**
- Line coverage per project is unchanged from Phase 0, or each drop is explained.
- The number of executed cases falls only by the tests listed as removed.

**Results, 2026-11-22:**

| Check | Result |
|---|---|
| Duplicates removed | 6, the ones listed under "Redundant tests" |
| Methods folded into `[DataRow]`s | 23 fewer methods, every case kept under its old name as its display name |
| Executed cases | `Tests` 4,720 to 4,714; the six gone are exactly the six removed, and no case is new. `Workshop.Tests` 394 and `Analyzers.Tests` 47 unchanged |
| Product lines covered, against a capture taken just before this phase | 52,761 across the suites: 0 lost, 0 gained |
| `Workshop.Tests` | 0 of 10 idle and 0 of 3 loaded runs failing |
| `Tests` | 0 of 10 idle and 0 of 3 loaded runs failing |

- **Same-shape groups.** The scan's 78 was an upper bound. Reading all 63 groups, most state different rules in the same
  code shape ("reduces" and "clamps", "before its frame" and "on its frame"), so they stay separate tests.
- **No-assertion tests.** All 13 stay. Each states that something builds, or does nothing and doesn't throw, and a throw
  fails it.
- **Per-class coverage was not run.** Nearly every class's lines overlap another's, so it wouldn't single out a
  duplicate. Reading the same-shape groups nominated these three instead.

**Nominated, then removed at the user's go-ahead (2026-11-22):** `Tests` now runs 4,711 cases, and the three gone are
exactly these. One coverage capture missed `SpriteManifestResolver.cs:160-161`, the early return when a name is already
remembered, which none of these tests reach. The same build covered both lines in 6 of 6 repeat runs, so nothing was
lost; those two lines' coverage depends on timing between threads.

| Test | Why it looks redundant |
|---|---|
| `EventBusTests.Publish_NonBufferedEvent_StillDispatchesImmediately` | The same test as `Publish_InvokesSubscriber` with a different number |
| `HealthHealTests.Apply_NoEventBusOrPlayerQuery_DoesNotThrow` | The same call and result as `Apply_RaisesCurrentHealthByFractionOfMaximum`; `Heal` no longer takes either argument |
| `ParalysisSystemTests.NoHealthComponentInvolvedAnywhere_ExpiresWithoutError` | The same as `OnItsExpiryFrame_TimerIsRemoved` with an earlier frame; neither test has a health component |

### Phase 5 -- Folders and names (decision 4)

One mechanical commit: files moved with plain `mv`, namespaces following folders, solution folders mirroring them.

**Check:** the same test count passes before and after, and the diff holds only moves and namespace lines.

**Results, 2026-11-22:**

| Check | Result |
|---|---|
| Files moved | 367, each identical to its committed version apart from namespace references, except the one test below |
| Other files changed | The two test `.csproj` files (a global using each), the isolation test's allowed paths, and two paths in IMPLEMENTATION-NOTES |
| Cases | `Tests` 4,711, `Workshop.Tests` 394, `Analyzers.Tests` 47, as before. Two display names changed because they print a helper type's namespace |
| Product lines covered | 52,761: 0 lost, 0 gained |
| Whole solution | Builds with no errors |
| `Workshop.Tests` | 0 of 10 idle and 0 of 3 loaded runs failing |
| `Tests` | 0 of 10 idle and 0 of 3 loaded runs failing |

- **Folder names differ from the first layout.** `Engine` and `Game` would have made namespaces hiding the real ones
  across 416 files and about 3,600 references, and `Map` would hide the `Map` type. They are `EngineTests`, `GameTests`
  and `MapAreas`, and `Debug` is `DebugTab`.
- **One test changed beyond its namespace.** `ARecordDecodedFromManyThreadsAtOnce_IsOneInstance` used `Parallel.For`,
  which overlapped only by luck: the new test order stopped it reaching the lock's contended path (10 of 10 runs before,
  0 of 8 after). It now releases 32 dedicated threads from a barrier and reached that path in 10 of 10 runs.
- `SpriteManifestResolver.cs:160-161` and `ContentTableLines.cs:170` still vary between runs with timing and random Guids.

### Phase 6 -- Ready for workers

Buildable today, before any fork exists:

1. A `SimulationTrace` test helper: per frame and entity, the decision chosen and each `ActivationUsedEvent`, in stable
   identifiers (rule 7).
2. **Same seed, same trace**: two sessions in one process, over a few hundred frames of a mixed NPC scenario.
3. **Choice independence**: two identical sessions, one bucket forward and one reversed. Each creature must choose the
   same thing. This is the property a fork relies on.
4. A test that every `IBehavior` and consideration, run over that scenario, writes and draws nothing while choosing.

When `PLAN-multithreading.md` Phase 3 lands, the same scenario takes a worker count (0, 1, 2 and 6) and the trace must
match at each. Tests stay at 0 workers unless they ask, as that plan's decision 6 says, and worker tests live in
`Tests/Determinism` under `TestCategory=Determinism`, in the unfiltered run.

**Check:**
- Making one behavior read another creature's just-written state fails the choice-independence test.
- Making a behavior draw from a shared stream fails the trace test.
- The whole folder passes 20 times idle and 5 times loaded.

**Results, 2026-11-23** (`Tests/Determinism`, six tests, no production code changed). A test-only module wraps each
built-in behavior by its Guid, as a mod can, so the trace records the real system. The scenario reaches all seven
behaviors and a consideration.

| Bug injected into production code, then removed | Caught by |
|---|---|
| A scratch buffer leaks from one creature's choice to the next | `EachCreaturesChoice_IsTheSameWhicheverOrderTheyChooseIn` |
| `Evaluate` draws from the shared effects stream | `ChoosingAnExtraTime_ChangesNothingThatFollows` |
| `Evaluate` uses an unseeded random | Those two and `TheSameSeed_GivesTheSameTraceAndWorld` |
| `Evaluate` writes a component | `Choosing_WritesNoComponent` |

| Suite | Idle runs failing | Loaded runs failing |
|---|---|---|
| `Tests/Determinism` (6 cases) | 0 of 20 | 0 of 5 |
| `Tests` (4,717 cases) | 0 of 10 | 0 of 3 |
| `Workshop.Tests` (394 cases) | 0 of 10 | 0 of 3 |

- Choice independence compares both orders in one session at each sample, since choosing is shown to write nothing.
  That needs no second session and compares every factor of each choice, not only the winner.
- **These tests run on one thread.** They prove the choose pass is order-independent and writes no component or stream.
  They don't prove it is safe to run concurrently.
- **What a fork must still solve (found reading the choose path):**
  - `RangedAttack.Evaluate` reaches `NpcDecisionServices.BlueprintActions`, which fills a cache on first ask and can call
    `ActionCatalog.GetOrCreateDerived`, an unlocked dictionary write. This answers the first-ask question in
    `PLAN-multithreading.md`'s table: it can happen while choosing.
  - `NpcDecisionServices`' `Random`, `CandidateActions`, `ActionIds`, `AdjacentTiles` and `CountedEntityIds` are one
    instance each and need one per worker, as that plan expects.

## Not doing

- **Retries or longer timeouts**, which hide a failure rather than fix it.
- **Splitting `Tests` into one project per layer.** Folders give the same order; more assemblies add build time.
- **Making production catalogs thread-safe for tests.** Tests stop sharing them instead; what forks need is decided in
  `PLAN-multithreading.md` Phase 3.
- **Removing a test because a scan flagged it.** Every removal is read first.

## Bookkeeping

- Done 2026-11-22: Phase 3's check passed, so TODO.md's "TOP PRIORITY" entry is replaced by one for what is still open,
  the unexplained allocation in `WithTheLinkOn_ASimulationFrameAllocatesNothing`.
- CLAUDE.md's test bullets and IMPLEMENTATION-NOTES gain the three kinds and the rules once Phase 2 lands.
- Correct the "Locked because tests run in parallel" remarks on `ItemIds` and `EntityIdentities` if the reason changes.
