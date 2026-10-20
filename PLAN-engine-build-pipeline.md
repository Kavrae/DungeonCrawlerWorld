# The engine build pipeline

(Planned 2026-10-18 from the Cadence/Engine public-surface review. Scope: how a game turns a module set into a
running `EcsContext` -- `Engine.Bootstrap`, `Engine.Modules`, the three declaration registries, `SystemManager`'s
build-time setters, `EcsContext.BeginSession` -- and the Game/GameShell/Workshop code that drives it. Pools, events
and utilities from the same review are not part of this plan.)

## Goal

A game builds a session with **one engine call**. The engine runs every step, in the only order that works; the game
supplies data (modules, settings sources, capacities, simulation policy) and a few callbacks the engine calls at
fixed points. Anything a build may leave out is a named option on that call, never a step the game remembers to run
or skips by not calling it. Nothing a build depends on is swapped in afterwards, and nothing that shapes the
simulation can change once the session begins.

Performance comes first: nothing here adds work to a frame, and builds that are never simulated (mod trials, the
spawn-record staging world, the Workshop's content session) do less than today, not more.

## What is wrong today

`GameBuildPass` is 200 lines because the engine leaves it to assemble the build by hand. A second game would copy it.

**Steps the engine documents but doesn't run**
- `IModule` lists `DeclareSettings`, `DeclareTags` and `DeclareContentTypes` as its first phases, but `EcsBuilder`
  never calls them. Game calls three differently shaped registries itself (`SettingsCatalog.Declare(...).Resolve(...)`,
  `GameplayTagRegistry.Declare`, `ContentTypeRegistry.Declare`; `GameBuildPass.cs:53`, `:113-114`).
- They run **before** `ModuleOrder.Sort` validates the set, so a module with a missing `Requires`, a duplicate type
  or a duplicate Id runs its declarations first and may fail with a confusing error from a registry instead of the
  named one. They also run in input order, not sorted order.
- Settings, tags and content types reach modules only because Game copies them onto `GameModuleContext`;
  `ComponentRegistration` and `BehaviorRegistration` carry settings only.
- `ModuleSet.Combine` replaces a built-in by Id, but the replacement contract (same component types, same pool kinds)
  is checked in Game (`ModValidation.ThrowIfBreaksReplacementContract`), on a scratch `ComponentManager`, with
  settings resolved for that one module alone.

**Things swapped in after the build "completes"** (the `SimulationClock` class of trap)
- `SystemManager.Clock` is replaced after `Complete()` with the clock modules were configured against
  (`GameBuildPass.cs:75`). Forget it and the build runs on two clocks: systems see `EngineTime.FrameCount`, every
  deadline reader outside a system sees a clock stuck at 0.
- **It is already forgotten on one path.** `GameBuildPass.BuildModules` -- the "few modules" build tests use through
  `BuiltInTestModules.BuildModules` -- returns straight from `Complete()`: its `GameModuleContext.SimulationClock`
  never advances, `SimulatedTierCount` stays `int.MaxValue` (every tier simulated, unlike the game), and `SpawnMoves`
  is never registered. Tests of a few modules run under different rules from the game without saying so.
- `SystemManager.SimulatedTierCount` is set the same way (`GameBuildPass.cs:70`), and stays writable all session.
- `EventBus` is the caller's (`new EventBus()` passed to `Begin`), while the builder makes every other manager.

**Order that depends on call order**
- `SystemManager.RegisterFirst` prepends, so the *last* caller runs first. Game registers `SpawnMoves` first in
  `GameBuildPass.Run`; GameShell registers `NeighborhoodStreamer` first later, in `WorldSessionBootstrapper`, and only
  that call order makes the streamer run before `SpawnMoves`. Reorder two lines in two projects and spawns land a
  frame late.
- Systems, frame-scoped buffers, `SimulatedTierCount` and the clock can all be changed after `BeginSession`; only
  gauges and `EntityPopulations` freeze there.
- `EntityManager.CreateEntity` works from the moment `RegisterComponents` returns. CLAUDE.md says "spawn only once
  the build is complete" (blueprints resolve after every `Configure`, handlers and systems don't exist yet), but
  nothing enforces it -- a module that spawns in `Configure` gets an entity no system or handler has seen.
- `GameTiming` must be set before anything converts a duration; `GameBuildPass.Run` calls `GameFrameRate.Apply()`
  itself, and `BuildModules` relies on the test assembly having done it.

**Builds that do more than they need**
- Every mod trial (`ModValidation`), the spawn-record staging world (`GameBootstrapper`) and the content-manifest
  exporter run the whole `GameBuildPass.Run`: diagnostics policy, gauges, `LocalTierRoster`, destruction wiring.
- The Workshop's `ContentSession` builds a full session *plus* a staging world it never uses, then instantiates every
  built-in module a third time just to get a `ContentTypeRegistry` (`ContentSession.cs:52`).

## Design

### One call

```csharp
var build = EcsBuilder.Build(new EcsBuildRequest<GameModuleContext>
{
    Modules = GameBootstrapper.BuiltInModules(),
    Mods = validatedMods.Mods,
    SettingsSources = settingsSources,
    FramesPerSecond = GameFrameRate.FramesPerSecond,
    InitialEntityCapacity = ...,
    InitialComponentCapacity = ...,
    RequiredModuleIds = GameModuleContext.FoundationModuleIds,
    SimulatedTierCount = ProcessingTierDivisors.SimulatedTierCount,
    CreateContext = services => new GameModuleContext(services, map, mathUtility, ...),
    AfterConfigure = GameBuildChecks.Run,          // blueprints resolved, content validated
    AfterBehavior = (context, finish) => ...,      // game-level systems, destroy handlers, diagnostics policy
});
```

`EcsBuilder.Build` runs, in this order, each step for every module before the next:

1. **Apply the frame rate** (`GameTiming.SetFramesPerSecond`; idempotent, so the exes' own early call for the UI is
   unaffected).
2. **Instantiate** every factory -- fresh modules per build is now the engine's guarantee, not the caller's habit.
3. **Combine** built-ins and mods by Id (`ModuleSet.Combine`), then **validate and sort** (`ModuleOrder.Sort`, plus
   `RequiredModuleIds` checked with the same named error as `Requires`). Nothing module-defined has run yet.
4. **Declare**, in sorted order: settings, tags, content types. Settings resolve against `SettingsSources`; rejected
   overrides go to the result, as today.
5. **Register components**, with a registration object carrying every declaration.
6. **Check the replacement contract** for every mod that replaced a module: it must have registered every component
   type the replaced module did, as the same pool kind. The engine registers the replaced module's components on a
   scratch `ComponentManager` with the build's own settings. (A build with no replacing mods pays nothing.)
7. **Create the context**: `CreateContext(EcsBuildServices)`. Services carry everything the engine owns: component
   and entity managers, keys, `EventBus`, `SimulationClock`, and the module declarations.
8. **Configure**, then `AfterConfigure(context)`.
9. **Register behavior**, then `AfterBehavior(context, finish)`; `finish` lets the game register systems with an
   explicit place in the frame (below), subscribe destruction handlers and set the diagnostics policy.
10. **Complete**: the `EcsContext` is returned, and `CreateEntity` is allowed from here on.

The result is `EcsBuild<TContext>`: `EcsContext`, `Context`, the sorted `Modules`, the `Declarations` (settings
values, tags, content types) and `SettingsFailures`.

The staged types (`SortedModules`, `RegisteredComponents`, ...) become internal: the order is no longer something a
caller can get wrong because it is no longer something a caller does.

**`EcsBuilder.Declare(request)`** is steps 1-4 alone, returning the same `Declarations` -- for a caller that needs only
the content types or settings of a module set (the Workshop's error list) without building pools.

### The engine owns what every build shares

- `SimulationClock` is created by the engine in step 7, handed to the context in `EcsBuildServices`, and advanced by
  `SystemManager`. `SystemManager.Clock` becomes get-only; `EcsContext.Clock` exposes it to hosts.
  `GameModuleContext.SimulationClock` reads the one from services.
- `EventBus` is created by the engine; `Begin`'s parameter goes.
- `SimulatedTierCount` is a request field, passed to `SystemManager`'s constructor; the property becomes get-only.
- `ComponentRegistration` and `BehaviorRegistration` both carry `Declarations` (settings, tags, content types).
  `BehaviorRegistration` also carries `EventBus`, `Clock` and `EntityKeys`, so a game context no longer has to copy
  engine services just to make them reachable. (`GameModuleContext` may keep its own properties for them; they then
  read from services rather than being separate parameters.)
- Phase argument types are named one way: `SettingsDeclarations`, `GameplayTagDeclarations` and
  `ContentTypeDeclarations` (renamed from `ContentTypeRegistration`).

### The frame's order is stated, not implied

`SystemManager` gets a fixed order of lanes:

- **`FrameStart`** -- systems that feed the frame rather than react to it. Each is registered with an explicit
  `int` order; lower runs first; equal orders are refused. The game names its orders as constants in one place
  (e.g. `GameFrameStartOrder.NeighborhoodStreaming = 0`, `GameFrameStartOrder.SpawnMoves = 100`).
- **`Modules`** -- every module's systems, in module sort order, as today (`BuiltInModulesTests` keeps pinning it).

`RegisterFirst` goes. Host systems (`NeighborhoodStreamer`) register in `FrameStart` with their constant, from
wherever they are built, and land in the same place whatever the call order.

The lanes are flattened into one array **once**, at `BeginSession` (and on first `Update` for a context that never
begins one -- tests), so `Update`'s loop is unchanged: one array, no per-frame lane logic.

### Begin freezes the simulation's shape

`BeginSession` already freezes gauges and `EntityPopulations`. It also freezes system registration (both lanes),
frame-scoped buffer registration and the simulation policy. Registering after it throws, naming what was registered
and that the session already began.

### Entities wait for the build

`EntityManager.CreateEntity` throws until step 10: "entities are created once the build is complete; blueprints
resolve after every Configure". One predictable branch per creation -- below noise even for the ~660k startup spawns,
and confirmed with the benchmark in Phase 3. Staging and trial builds are complete when they return, so
`SpawnRecordRebuilder` and population are unaffected.

### Builds that are never simulated do less

A request field, **`Purpose`** (`Session`, `Trial`, `Staging`), handed to the callbacks so the game can leave out what
only a simulated session needs (gauges, `LocalTierRoster`, the diagnostics policy) without a second code path. The
engine itself uses it for one thing: a `Trial` or `Staging` build never accepts `BeginSession`.

Every purpose runs every phase, `RegisterBehavior` included: a mod's trial and the staging world are checks that the
whole module set builds, and the observers registered there are part of what a rebuilt creature is (a lit item's held
toggle effects come from `ToggleItemHolderSync`). `Purpose` trims only the game's own session-only wiring.

### What Game keeps

- `GameBuildPass` shrinks to building the request: `CreateContext` (World over the map, `GameModuleContext`),
  `AfterConfigure` (`ResolveAll`, `AuraContentRegistration`, the three content validations) and `AfterBehavior`
  (`SpawnMoves` in `FrameStart`, the skeleton access guard, destruction wiring, simulation resume, and for `Session`
  the roster and diagnostics). `Run` and `BuildModules` share one build path; a few-modules build is the same request
  with fewer factories, so it gets the same clock, tier policy and `SpawnMoves` as the game. `Run` adds only what a
  game session reads and a few-modules build can't hold (the roster needs Movement's pool).
- `ModValidation` keeps loading mods and trial-building each one; the contract check moves to the engine.
- `ContentSession` reads content types from its own session's `Declarations`, and builds without the staging world
  (a `GameBootstrapper.Build` parameter -- the staging world is Game's own concept).

## Phases

Each phase ends with a full test run and an in-game check; the next starts after your go-ahead.

### Phase 1 -- One build call, engine-owned clock and bus

(Built 2026-10-18; awaiting in-game testing. As built: the stage types were deleted rather than made internal, since
`Build` no longer needs them; `GameBuildPass.Run` and `BuildModules` share one build path and `Run` adds only the
roster and diagnostics; the aura-source cleanup on destroy moved into `AurasModule.RegisterBehavior`, so the shared
path needs no Auras pool and still clears sources before the footprint; startup diagnostics gain a `DeclareModules`
scope.)

- Add `EcsBuildRequest<TContext>`, `EcsBuildServices`, `ModuleDeclarations`, `EcsBuild<TContext>` and
  `EcsBuilder.Build`/`Declare`. Make the stage types internal. Declarations run after sort, in sorted order.
- Engine-created `SimulationClock` and `EventBus`; `SystemManager.Clock` and `SimulatedTierCount` get-only;
  `EcsContext.Clock`.
- `RequiredModuleIds` replaces `GameBuildPass.ThrowIfMissingFoundation`. The frame rate is applied in step 1;
  `GameFrameRate.Apply()` leaves `GameBuildPass`.
- Registration objects carry `Declarations`; rename `ContentTypeRegistration` to `ContentTypeDeclarations`.
- Migrate `GameBuildPass` (`Run` + `BuildModules` merged), `GameModuleContext`'s constructor, `GameBootstrapper`,
  `ModValidation`, `ContentManifestExporter`, `BuiltInTestModules`, `TestModuleRegistration` and the tests that build
  by hand. `SpawnMoves` stays `RegisterFirst` until Phase 3.

Test after this phase:
- Start a game; walk across a neighborhood edge. Expect streaming, spawning and the window shift as before.
- Cast a Delayed action and turn on a toggle with upkeep. Expect the windup bar and upkeep ticks on time (the clock
  every deadline reader uses is now the engine's).
- Start with `--setting=Bogus.Name=1`. Expect the same one `[Settings]` line on the console.
- Drop a mod with a missing `Requires` into Mods. Expect it reported with the named "requires" error, not a settings
  or tag error.
- Benchmark A/B on a fixed seed: no system row moves beyond noise; startup unchanged.

### Phase 2 -- Replacement contract and declarations-only builds in the engine

(Built 2026-10-18; awaiting testing. As built: the check is `Engine.Bootstrap.ReplacementContract`, comparing each
replacing mod against the game's own module it displaced, and emits a `CheckReplacements` startup scope only when
there is a replacement. The session's content types reach the Workshop as `GameSession.Catalogs.ContentTypes`;
`TestContent` reads them through `EcsBuilder.Declare`.)

- Step 6 in `EcsBuilder.Build`; delete `ModValidation.ThrowIfBreaksReplacementContract`/`RegisteredPools`.
- `ContentSession.LoadProblems` uses its session's `Declarations.ContentTypes`; `EcsBuilder.Declare` for anything
  else that needs declarations alone.

Test after this phase:
- `Mods.TestFixtures`' replacement mods: a contract-breaking one is dropped with the same message listing the missing
  pools; `ReplacementHealthModule` still builds.
- Open the Workshop: the Error List shows the same problems as before.

### Phase 3 -- Stated frame order, frozen session, entities after the build

(Built 2026-10-19; awaiting in-game testing. As built: `SystemManager.RegisterAtFrameStart(system, order)` beside
`Register`, refusing a taken order; the run order is rebuilt only when registration changes (checked by one bool at
the top of `Update`) rather than frozen at the first `Update`, so a test or tool may still register between updates
until `BeginSession`, which closes it; `EcsBuilder` holds `CreateEntity` until `Build` returns, and no built-in module
spawned during the build. Headless Release A/B against HEAD (cf5f7f0, all three phases): same world fingerprint, no row
beyond threshold, `EcsContext.Update` -2.4%; process start to frame 2, 3 runs each, medians 2758 vs 2774 ms.)

- `FrameStart` lane with explicit orders; `RegisterFirst` removed; `GameFrameStartOrder` constants;
  `SpawnMoves` and `NeighborhoodStreamer` registered by order.
- Lanes flattened once at `BeginSession`/first `Update`; registration after `BeginSession` throws.
- `CreateEntity` refused before the build completes.

Test after this phase:
- `BuiltInModulesTests`' pinned order passes with `SpawnMoves` at the front, and a new test registers a streamer
  stand-in after `SpawnMoves` and finds it running first.
- Walk until a neighborhood streams in; spawn a creature from Admin Mode's "Spawn here >". Expect both to appear the
  same frame they did before (no one-frame lag on newly streamed creatures).
- Benchmark A/B: startup population and `EntityFactory` rows within noise (the `CreateEntity` branch).

### Phase 4 -- Builds that are never simulated

(Built 2026-10-19; awaiting in-game testing. As built: `EcsBuildPurpose` on the request, in `EcsBuildServices` and
on `EcsContext`, whose `BeginSession` refuses a Trial or Staging build. No callback needed it: trials and the staging
world simply build through `GameBuildPass.BuildModules`, which never had the roster or diagnostics, and only a
Session goes through `Run`. "Skip the staging world" became "build it on the first rebuild"
(`buildsStagingWorldOnFirstRebuild`), so `SpawnRecordRebuilder` is never null and a host that never rebuilds -- the
Workshop -- never builds it; the game still builds it at startup, now after the session rather than before it.
Headless Release against the `pre-build-pipeline` baseline: same world fingerprint; process start to frame 2, 7 runs
each interleaved, medians 2653 vs 2652 ms.)

- `Purpose` on the request; Game's `AfterBehavior` skips roster and diagnostics for `Trial`/`Staging`;
  `BeginSession` refused for them.
- `GameBootstrapper.Build` parameter to skip the staging world; `ContentSession` uses it.

Test after this phase:
- `--diagnostics=startup`: compare "Mod Validation" and "Module Load" against a pre-Phase-4 run. Expect both no
  slower, with the staging build cheaper.
- Inspect an unsimulated creature (labelled "Unsimulated"): same contents as before.
- Open the Workshop: same tabs, same content, faster to open.

## Decisions

1. **Staging and trial builds keep `RegisterBehavior`.** It is part of what they check; no build purpose skips a
   module phase.
2. **Modules are passed as factories only.** The engine instantiates them, so fresh modules per build is guaranteed.
   Tests that build a few hand-made modules wrap each in a factory; there is no instance overload.
3. Making the mod-facing surface hard to misuse is a separate item: TODO.md, Engine, Low, "Mod surface: hard to
   misuse".
