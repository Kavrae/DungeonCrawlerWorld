# Diagnostics Wired Through Named Engine Hooks

(Pre-implementation. Replaces TODO.md's "Diagnostics wired through named engine hooks" entry, which
is deleted when the last phase lands.)

## Context

`DiagnosticsEngine` is constructed in `GameLoop`'s constructor (and `HeadlessBenchmark.Run`) before
any session exists. Today each feature reaches the engine by its own hand-wired path:

- **Startup.** `StartupProfiler?` is a parameter of `ModValidation.Validate`,
  `GameBootstrapper.Build`, `GameBuildPass.Run`, `GameBuildPass.BuildModules` and `EcsBuilder.Begin`,
  carried in `EcsBuildState`, and every step wraps itself in `startupProfiler?.Phase(...)`
  (`SortedModules`, `RegisteredComponents`, `ConfiguredModules` per module;
  `WorldSessionBootstrapper` and `GameLoop.Initialize` for host steps). Mod trial builds are not
  profiled (`ModValidation` passes none into `GameBuildPass.Run`).
- **Memory / LeakDetection.** Nullable fields, created by `AttachEcsContext`, called from
  `WorldSessionBootstrapper` after the build. It only creates a tracker the first time, so a second
  session in the same process would keep measuring the first session's pools. Nothing ends a session:
  `EcsContext` has no teardown, and `HeadlessBenchmark` disposes `PlayerActivityLog` by hand.
- **Frame cost.** `SystemManager.Profiler` and `EventBus.Profiler` are settable null-means-off
  properties set by `WorldSessionBootstrapper`; `ShellContext` gets the recorder through
  `LoadContent`; `GameLoop` records `Shell.Update`/`Shell.Draw` itself.
- **Frame protocol.** `GameLoop.Update` and `HeadlessBenchmark.Run` each do
  `PlayerActivityLog.BeginFrame` → `BeginSimulationFrame(frame)` → time `EcsContext.Update` →
  `RecordSimulationTick("GameLoop", "EcsContext.Update (all systems)", elapsed)`. `GameLoop` also
  calls `DiagnosticsEngine.Tick()` every host frame; headless never does.

Other builds exist besides the session: every mod's trial build (`ModValidation`) and
`SpawnRecordRebuilder`'s staging pass. They run `EcsBuilder` but never update, so anything keyed off
"an `EcsContext` was built" would fire for them too.

Tests: `SystemOrderRecorder` (`BuiltInModulesTests`, `GameBootstrapperTests`) pins system order by
setting `SystemManager.Profiler`; `EventBusTests` sets `EventBus.Profiler`; `FrameRangeBenchmarkTests`
and `StartupProfilerTests` construct the trackers directly. Tests run parallel at method level
(`MSTestSettings.cs`).

## Design

### The rule

**Engine code emits; diagnostics listens.** An emit site reads one static channel field and does
nothing else when it is null. Nothing passes a profiler, recorder or `DiagnosticsEngine` into
engine or game code, and no host drives a frame protocol. (Unreal Insights' model: named channels,
a scope helper that is one branch when off, one engine-owned frame marker.)

### `Engine.Diagnostics.EngineHooks` -- the channels

A static class, one property per channel, each holding at most one listener:

| Channel | Listener | Emitted by |
|---|---|---|
| `Scopes` | `IDiagnosticScopeListener` -- `ScopeStarted(in DiagnosticScope)`, `ScopeEnded(in DiagnosticScope, TimeSpan)` | `EcsBuilder` stages and module phases, `GameBuildPass`, `ModValidation`, host startup steps |
| `Sessions` | `ISimulationSessionListener` -- `SessionStarted(EcsContext)`, `SessionEnding(EcsContext)` | `EcsContext.BeginSession` / `EcsContext.Dispose` |
| `SimulationFrames` | `ISimulationFrameListener` -- `FrameStarting(long frame)`, `FrameEnded(long frame, TimeSpan elapsed)` | `SystemManager.Update` |
| `FrameCosts` | `IFrameCostRecorder` (existing) | `SystemManager`, `EventBus`, `ShellContext`, `GameLoop` (shell update/draw) |

- **One listener per channel.** `EngineHooks.Subscribe(listener)` sets every channel whose interface
  the listener implements and returns an `IDisposable` that clears them; subscribing a channel that
  already has a listener throws, naming it. `DiagnosticsEngine` is the only production listener and
  fans out internally (as `CompositeFrameCostRecorder` does today). No multicast delegates on the hot
  path.
- **Cost when off:** one static field read and a null check -- what `Profiler is { } profiler` costs
  today on an instance property. `EventBus.Publish` and the per-system loop read the channel into a
  local once per call/frame, not per iteration.
- **`DiagnosticScope`** is a `readonly struct`: a constant `Name` plus an optional `IModule` (and a
  `ModulePhase` enum for module phases). `EngineHooks.Scope("Entity Population")` and
  `EngineHooks.ModulePhase(ModulePhase.Configure, module)` return a disposable `struct` that holds the
  start timestamp only when a listener exists. No string is built at an emit site: the listener
  formats `"Configure:Health"` itself, so an interpolated name costs nothing when off (today that is
  guaranteed only by `?.` short-circuiting).
- Why static rather than an injected hooks object: an instance would have to be threaded to
  `EcsBuilder`, `SystemManager` and `EventBus` -- the thing being removed. Diagnostics is one per
  process by nature (like `GlobalState`). Every build stays self-contained in everything it
  *simulates*; only what it reports is shared.

### Build hooks

- `EcsBuilder`'s stages emit a scope per stage (`"RegisterComponents"`, `"Configure"`,
  `"RegisterSystems"`) with a `ModulePhase` scope per module inside. `EcsBuildState.StartupProfiler`
  and `EcsBuilder.Begin`'s `startupProfiler` parameter are deleted.
- `GameBuildPass.Run`/`BuildModules`, `GameBootstrapper.Build` and `ModValidation.Validate` lose their
  `StartupProfiler?` parameters; their own steps become `EngineHooks.Scope(...)`.
  `WorldSessionBootstrapper` and `GameLoop.Initialize` do the same for theirs, keeping today's names
  ("Mod Validation", "Module Load", "Entity Population", ...) so startup reports stay comparable.
- **`StartupProfiler` becomes an `IDiagnosticScopeListener`.** It records nesting depth
  (`PhaseRecord` gains `Depth`, written to the report), so each module phase is shown under the step
  it ran in. It listens only until the first simulation frame starts; after that, a runtime build
  (a staging rebuild) is not startup. Its stability detection moves onto `FrameEnded`'s elapsed.
- **Mod trial builds are recorded**, nested under "DryRunValidateMods" with one scope per mod
  (`"Trial:<mod type>"`). They are real startup cost, and today they are one opaque number.

### Session hooks

- `EcsContext.BeginSession()` emits `SessionStarted` once (throws on a second call);
  `EcsContext : IDisposable`, whose `Dispose` emits `SessionEnding` if the session began. A trial or
  staging build never calls `BeginSession`, so no listener ever sees one -- the host states which
  build is the session instead of diagnostics inferring it.
- `WorldSessionBootstrapper.Build` calls `ecsContext.BeginSession()` as its last step, replacing
  `AttachEcsContext` and the two `Profiler =` lines. `WorldSessionContext : IDisposable` disposes
  `PlayerActivityLog` and the `EcsContext`; `GameLoop` disposes it on exit (`OnExiting`),
  `HeadlessBenchmark` with `using`.
- `DiagnosticsEngine` creates `ComponentMemoryTracker`, `LeakDetector` and `PoolMemoryReport` in
  `SessionStarted` against that context and drops them in `SessionEnding` (writing a pending
  benchmark memory report first, if its range had opened). A second session gets fresh trackers.
  `SessionStarted` while one is active throws -- one simulated session per process.

### Frame hooks

- `SystemManager.Update` emits `FrameStarting(time.FrameCount)` before `Clock.Advance` and
  `FrameEnded(frame, elapsed)` after the frame-scoped buffers clear, reading the timestamp only when
  a listener exists.
- `DiagnosticsEngine` on `FrameStarting`: the benchmark window (below) and the pool-memory baseline.
  On `FrameEnded`: records `FrameCostCategory.Update, "GameLoop", "EcsContext.Update (all systems)"`
  -- same group and item, so the benchmark skill and saved baselines keep matching -- feeds
  `StartupProfiler` stability, and runs today's `Tick()` (memory/leak sampling and the 5 s report).
  `BeginSimulationFrame`, `RecordSimulationTick` and the public `Tick` are deleted.
- **Benchmark closes at the end of frame `EndFrame - 1`** instead of the start of `EndFrame`, the same
  frames' Updates. The headless loop becomes "update until `IsBenchmarkComplete`", simulating exactly
  the frames it does today, so fingerprints stay identical to a pre-change baseline build. Windowed,
  the range loses the one shell Update/Draw after its last frame (1 host frame in 3000).
- `DiagnosticsEngine`'s periodic `latest.json` write is skipped headless (the engine is constructed
  with a `DiagnosticsHost.Headless` flag, or headless keeps masking features as today) -- the report
  is the benchmark file, and a headless run must not overwrite a windowed `latest.json`.
- Reports and sampling pause with the simulation (paused / menu mode). Shell frame costs recorded
  while paused still accumulate and appear in the next report.
- **`PlayerActivityLog.BeginFrame` goes too:** the log reads `SimulationClock.CurrentFrame` and
  `DateTime.Now` when it writes a line. Before the first frame the clock reads 0, which is what the
  spawn line prints today. Both hosts then call nothing per frame but `EcsContext.Update`.

### Frame costs

`SystemManager.Profiler`, `EventBus.Profiler` and `ShellContext.LoadContent`'s recorder parameter are
deleted; each reads `EngineHooks.FrameCosts`. `DiagnosticsEngine.FrameCostRecorder` becomes private
(what it subscribes). `DebugWindowContent` keeps its `DiagnosticsEngine?` -- it reads
`TopFrameCostEntry`, it doesn't feed it.

### Lifecycle of `DiagnosticsEngine`

Construction stays side-effect-free (tests build engines in parallel). `diagnostics.Start()`
subscribes it to `EngineHooks` and starts `StartupProfiler`'s clock; `Dispose` unsubscribes. `GameLoop`
calls `Start()` first thing in its constructor (so startup timing starts where it does today) and
disposes on exit; `HeadlessBenchmark` holds it in a `using`. With nothing enabled and no benchmark,
`Start()` subscribes nothing, so every channel stays null.

### Tests

- A test that subscribes a listener touches process state and is `[DoNotParallelize]` (MSTest runs
  those after the parallel set, alone). `SystemOrderRecorder` subscribes through `EngineHooks` for the
  two system-order tests; `EventBusTests`' two profiler tests the same.
- New: `EngineHooksTests` (subscribe/unsubscribe, double-subscribe throws, disposing clears only its
  own channels); `EcsBuilder` emits stage and module-phase scopes in order with correct nesting;
  `BeginSession` twice throws, `Dispose` without `BeginSession` emits nothing, trial/staging builds
  emit no session; `SystemManager.Update` emits start/end with the frame number; `DiagnosticsEngine`
  creates fresh trackers for a second session and drops the first's; headless-shaped loop driven only
  by hooks completes the benchmark after the same number of `Update` calls as today.
- `StartupProfilerTests` gains depth; `FrameRangeBenchmarkTests` drive `FrameStarting`/`FrameEnded`.

## Phases

Each phase ends with a build, the full test run, and a stop for in-game testing.

1. **Channels + frame costs.** `EngineHooks`, `DiagnosticScope`, listener interfaces,
   `DiagnosticsEngine.Start/Dispose`. Move `SystemManager`, `EventBus`, `ShellContext` and
   `GameLoop`'s shell timings to `FrameCosts`; delete the `Profiler` properties and the
   `LoadContent` parameter; convert the recorder tests. In game: `--diagnostics=frame`, `latest.json`
   and the debug window's "Top:" still populate.
2. **Frame hooks.** Emit from `SystemManager.Update`; move the benchmark window, simulation-tick
   entry, stability feed and `Tick` onto them; benchmark closes at end of `EndFrame - 1`; remove both
   hosts' frame protocol and `PlayerActivityLog.BeginFrame`; headless skips `latest.json`. Check:
   `phase-performance-testing` `-Compare` against a baseline saved before phase 1 -- same fingerprint,
   no regression (this is also the cost-when-off check for phase 1's hot-path reads).
3. **Session hooks.** `BeginSession`/`Dispose` on `EcsContext`, `WorldSessionContext : IDisposable`,
   per-session trackers, delete `AttachEcsContext`. In game: `--diagnostics=memory,leak` report as
   before; headless memory run (`-Memory`) still writes its report.
4. **Build hooks.** Scopes from `EcsBuilder` and the Game/host steps; delete every `StartupProfiler?`
   parameter and `EcsBuildState.StartupProfiler`; `StartupProfiler` becomes a scope listener with
   depth; trial builds recorded. In game: `--diagnostics=startup` writes a nested startup report with
   today's top-level names.
5. **Docs.** CLAUDE.md (Commands/Benchmarks line, a Diagnostics bullet naming `EngineHooks` and the
   "emit, don't thread" rule), `IMPLEMENTATION-NOTES.md` section, the `phase-performance-testing`
   skill if anything it reads moved, delete the TODO.md entry.

## Out of scope / follow-ups

- **Named gauges** (the TODO's Godot reference): `Gauges` per session (`EcsContext`-owned, cleared
  with it, so a closure over a session object can't outlive it), registered by engine and game code
  (entity count, streamer queue depth, gen-1 GC count), sampled on `FrameEnded` only when a `Gauges`
  feature is on, and written per benchmark range. Needs this plan's session and frame hooks; worth
  its own TODO entry to feed the "Gen-1 GC frames during a window shift" investigation.
- Runtime channel toggling (Unreal's `-trace=` at runtime): channels are chosen at launch today;
  `Subscribe`/`Dispose` already allow it later.

## Decisions to confirm

1. **Static channels** (one listener each, `[DoNotParallelize]` for tests that subscribe) rather than
   an injected hooks object.
2. **Explicit `BeginSession`** by the host rather than inferring the session from "first `Update`".
3. **Benchmark closes at the end of `EndFrame - 1`**, keeping headless fingerprints identical.
4. **Mod trial builds appear in the startup report**, nested.
5. **Gauges deferred** to a follow-up TODO entry rather than a sixth phase.
