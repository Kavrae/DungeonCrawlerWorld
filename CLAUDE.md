# CLAUDE.md

Guidance for Claude Code in this repo.

## Git
Local, read-only inspection/comparison commands (`git status`, `git diff`, `git log`, and similar) are allowed. Do not run anything that writes to the repo or touches a remote — `add`, `commit`, `push`, `fetch`, `pull`, `stash`, `reset`, `checkout`/`restore`, branch/tag creation or deletion, etc. Leave those to the user.

## Commands
```
dotnet build DungeonCrawlerWorld.sln
dotnet test Tests/Tests.csproj
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~ClassName.MethodName"
dotnet run --project DungeonCrawlerWorld/DungeonCrawlerWorld.csproj
```
- net10.0, Nullable enabled, LangVersion latest, all projects.
- Tests: MSTest (`Microsoft.VisualStudio.TestTools.UnitTesting` global using).
- DungeonCrawlerWorld.csproj auto-runs `dotnet tool restore` (mgcb tools) before Restore.
- Benchmarks: `--seed=N` (deterministic world), `--benchmark-frames=START-END` (one-shot per-system report), `--headless` (simulation only, no window, exits when done). Drive them through the `phase-performance-testing` skill, which also does before/after A/B runs.
- Tests.csproj refs Mods.ExampleMod/Mods.TestFixtures with `ReferenceOutputAssembly="false"` — built to disk, loaded at runtime via ModuleLoader (real mod pipeline), not compiled against.

## Layers (one-way deps)
`Engine → Game → Presentation → DungeonCrawlerWorld(exe)`
- **Engine**: generic ECS + modding infra. No game-specific knowledge.
- **Game**: world/map, blueprints (entity templates), built-in modules (Core, Movement, Health, Energy, Race, Class).
- **Presentation**: FNA (XNA-compat) rendering + window/UI. No gameplay logic.
- **DungeonCrawlerWorld**: exe. `GameLoop.cs` = composition root. `GameShellBootstrapper.cs` = windows/layout. `Presentation/Input/UiInputController.cs` = input.
- **Content**: copies `Content/Fonts/*.ttf` to output only; not a compiled MonoGame pipeline.
- **Mods.ExampleMod**: real mod pattern — refs Engine/Game with `Private="false"` (no duplicate copies shipped). **Mods.TestFixtures**: mod DLLs for ModuleLoader failure-path tests.

## ECS (Engine/ECS)
- `EcsContext` = EntityManager + ComponentManager + SystemManager + EventBus. Built by `Bootstrapper.Build(modules,...)`: topo-sorts by `Dependencies`, registers ALL components before ANY systems (cross-module pool deps).
- Pools: `RegisterDirectPool/RegisterPackedPool/RegisterMultiPool`, each with `MergeAction<T>`. Blueprints always `Merge` not `Add` (composable, no collision throw).
- Systems: `ISystem` + `StripeCount`. `SystemManager` runs every system every frame; each processes only `Count/StripeCount` of its pop per call (entity striping, flat per-frame cost vs periodic-system spikes). `EntityStripeSet` buckets by `entityId % StripeCount`, updated incrementally via EntityAdded/EntityRemoved (no rescan).
- Tiered systems: `TieredEntityStripeSet` = one `EntityStripeSet` per processing tier (Local/Neighborhood/Borough/Beyond), bucket stripe count = base `StripeCount` × tier divisor (`ProcessingTierDivisors`, `ushort` — `byte` silently truncated before). A tiered system implements `ITieredSystem`: expose `Tiers`, per-entity work in `UpdateBucket(time, entityIds, framesPerVisit)`, per-frame work in `BeginFrame`, and `Update` must be exactly `=> TieredSystemRunner.Run(this, time)`. `SystemManager` owns the tier loop and never calls a tiered system's `Update` — anything else put in `Update` is silently skipped. `SystemManager.SimulatedTierCount` = which tiers run at all (game-injected policy; P2 hook).
- **Scale every time-based field by `framesPerVisit`, never a constant.** A coarse-tier entity is visited every `StripeCount × divisor` frames, so a value advanced by 1 (or by base `StripeCount`) per visit moves at a fraction of real time. This defect recurred across ~6 systems before the loop moved into `SystemManager`. It no longer applies to timers at all — those are deadlines now (see the Timers bullet), which is what made the whole class of bug go away; it still applies to anything else a tiered system accumulates per visit, e.g. `ComplexHealthRegenSystem`'s regen amount.
- Timers: **nothing decrements a countdown any more.** Store `FrameDeadline.After(now, frames)` as a `uint` frame and compare against `EngineTime.FrameCount` (in systems) or `SimulationClock.CurrentFrame` (elsewhere; `GameModuleContext.SimulationClock`, advanced by `SystemManager.Update`). Two shapes, by what reaching the deadline means:
  - **Something happens** (a damage tick, an expiry, a windup resolving) → the component implements `IScheduledTimer` (or `IKeyedScheduledTimer` for a Multi pool, keyed by a stable instance id) and its owning system drives a `PackedTimerWheel`/`MultiTimerWheel`. Scheduling is automatic: the wheel observes the pool's `ComponentChanged`, so any normal write of `NextTickFrame` schedules it and no caller has to remember. Re-arm a periodic timer with `timer.RepeatEvery(period)` on the `ref` a pool updater hands you (`ScheduledTimerExtensions`) -- it counts from the deadline that fired, so the cadence stays exact if a firing was ever late, and there is no `now` to pass wrongly. `FrameDeadline.Never` parks a timer (permanent modifiers/immunities are never scheduled).
  - **Nothing happens, it just stops gating** (action lock, movement backoff, regen lockout) → a plain `uint` field and a comparison at the reader. No system, no wheel, no per-entity cost at all.
  - Anything that writes a timer takes `now` as a required parameter rather than reading an ambient clock, so a test can't silently disagree with the frame the system is running on. See `PLAN-timer-wheel.md`.
- Choosing a shape: no per-entity population (event queues) → plain `ISystem`, no stripe set. Must react the frame something was queued (player input, deaths, dodge windows) → plain `EntityStripeSet`, don't tier. Otherwise → `ITieredSystem`. Two different tiered passes (`StatusEffectAuraSystem`) or event-driven (`ProcessingTierSystem`) → plain `ISystem` running its own loops.
- Tiers are decided by `ProcessingTierResolver` (Game), event-driven by `ProcessingTierSystem` — no periodic scan. Every positioned entity is tiered, not just movers. Entities are born tiered: `ProcessingTierResolver.CreateEntityAt` writes the tier as the first component, before the blueprint adds pooled components (stripe sets read it in `OnMemberAdded`); `World.EntityPlaced` → `EnsureTiered` is the catch-all for other placements. An untiered entity fails open to Beyond in every stripe set. Player pinned Local once at spawn. See `PLAN-processing-tier-rework.md`.

## Modding (Engine.Modules)
- `IModule` = components + systems + `Dependencies` + identity `Guid Id`.
- `ModuleLoader.LoadFromDirectory`: reflects `*.dll` into collectible `AssemblyLoadContext`s, constructs public concrete `IModule` types. Per-DLL/type failures caught → `ModuleFailure`, never aborts whole load.
- `ModuleSet.Combine`: merges built-ins + mods, replaces built-in by `Id` match.
- `Game.Bootstrap.GameBootstrapper.Build` = real composition point: lists built-in modules, loads mods, dry-run validates each mod (trial-register w/ built-ins against throwaway instances, drop on throw), configures `IGameModule`s (need `IMapQuery`/`MathUtility`/`EventBus`, unavailable at plain ctor) via `GameModuleContext`.

## World & Map (Game.World)
- `World : IMapQuery` owns `Map` + placement (`PlaceEntityOnMap`/`MoveEntity`/`RemoveEntityFromMap`).
- `Map`: 2 flat arrays — creature occupancy per `(x,y,MapLayer)`, terrain per `(x,y,TerrainLayer)`. Separate so wall/floor don't collide.
- Map occupancy is governed by `IMapQuery.IsBlocking`, implemented on `World` from two `MultiComponentPool` markers — `ForceBlockingComponent` (wins if present) then `NonBlockingComponent`, else default blocking; both are many-source/count-based so overlapping sources (assumed for all effects) work correctly. `NonBlockingComponent.Kind` (`NonBlockingKind` flags: `Tiny`/`Phasing`) is how `MapWindow` renders the entity while exempt (tiny-grid/phasing-alpha) — folded into the same component that grants the exemption itself (not a separate component), so the two can't drift apart the way they once did. Non-Blocking entities are indexed by position in `Map`'s own `GetNonBlockingEntityIdsAt` (kept in sync by `World`'s placement/move/removal, mirroring the Blocking array) — `MapWindow`, `SelectionWindowContent`, and `AbilityEffectResolver` all query it instead of scanning every non-Blocking entity.
- `FloorBuilder`: `CreateMap` must run before `GameBootstrapper.Build` (MovementModule.Configure needs IMapQuery); `PopulateFloor` needs EntityManager/ComponentManager from Build. Real ordering constraint.

## Blueprints (Game.Blueprints)
- `IBlueprint.Build(ComponentManager, entityId)` = entity template.
- `CompositeBlueprint`: ordered parts (e.g. race, then class) + optional `overrides` delegate. Later parts run after earlier — order matters for adjustments.

## Presentation
- `PresentationBootstrapper`: fixed service set (FontService, SpriteBatchRenderer, LabelRenderer, TileRenderer, WindowService) — no module system (set is static).
- UI: `Window`/`WindowService`/`MapWindow`, content panes (`InspectionWindowContent`, `DebugWindowContent`). `NotificationCenter` can block gameplay update for a frame (checked in `GameLoop.Update` before `EcsContext` advances).
- Core design principles: remove ambiguity, remove unexpected actions. When an interaction could mean more than one thing (e.g. a drag with no single underlying identity to act on), don't guess — either resolve it to something concrete first, or refuse it outright with clear feedback (a disabled cursor, a no-op) rather than doing something the player didn't ask for.

## UI changes
Visually verify any Presentation/UI change by running the game and looking at it — no screen captures.

## Scale
`GameLoop.InitialEntityCapacity`/`InitialComponentCapacity` sized for TestMapBuilder's 1000×1000×3 map (~2.6M entities) to avoid reallocate-copy churn during population — not arbitrary defaults.
