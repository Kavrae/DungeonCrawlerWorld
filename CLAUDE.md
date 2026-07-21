# CLAUDE.md

Guidance for Claude Code in this repo.

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
- Tests.csproj refs Mods.ExampleMod/Mods.TestFixtures with `ReferenceOutputAssembly="false"` — built to disk, loaded at runtime via ModuleLoader (real mod pipeline), not compiled against.

## Layers (one-way deps)
`Engine → Game → Presentation → DungeonCrawlerWorld(exe)`
- **Engine**: generic ECS + modding infra. No game-specific knowledge.
- **Game**: world/map, blueprints (entity templates), built-in modules (Core, Movement, Health, Energy, Race, Class).
- **Presentation**: FNA (XNA-compat) rendering + window/UI. No gameplay logic.
- **DungeonCrawlerWorld**: exe. `GameLoop.cs` = composition root. `GameShellBootstrapper.cs` = windows/layout. `Presentation/Input/GameInputController.cs` = input.
- **Content**: copies `Content/Fonts/*.ttf` to output only; not a compiled MonoGame pipeline.
- **Mods.ExampleMod**: real mod pattern — refs Engine/Game with `Private="false"` (no duplicate copies shipped). **Mods.TestFixtures**: mod DLLs for ModuleLoader failure-path tests.

## ECS (Engine/ECS)
- `EcsContext` = EntityManager + ComponentManager + SystemManager + EventBus. Built by `Bootstrapper.Build(modules,...)`: topo-sorts by `Dependencies`, registers ALL components before ANY systems (cross-module pool deps).
- Pools: `RegisterDirectPool/RegisterPackedPool/RegisterMultiPool`, each with `MergeAction<T>`. Blueprints always `Merge` not `Add` (composable, no collision throw).
- Systems: `ISystem` + `StripeCount`. `SystemManager` runs every system every frame; each processes only `Count/StripeCount` of its pop per call (entity striping, flat per-frame cost vs periodic-system spikes). `EntityStripeSet` buckets by `entityId % StripeCount`, updated incrementally via EntityAdded/EntityRemoved (no rescan). Relevant when adding/modifying a system's iteration.

## Modding (Engine.Modules)
- `IModule` = components + systems + `Dependencies` + identity `Guid Id`.
- `ModuleLoader.LoadFromDirectory`: reflects `*.dll` into collectible `AssemblyLoadContext`s, constructs public concrete `IModule` types. Per-DLL/type failures caught → `ModuleFailure`, never aborts whole load.
- `ModuleSet.Combine`: merges built-ins + mods, replaces built-in by `Id` match.
- `Game.Bootstrap.GameBootstrapper.Build` = real composition point: lists built-in modules, loads mods, dry-run validates each mod (trial-register w/ built-ins against throwaway instances, drop on throw), configures `IGameModule`s (need `IMapQuery`/`MathUtility`/`EventBus`, unavailable at plain ctor) via `GameModuleContext`.

## World & Map (Game.World)
- `World : IMapQuery` owns `Map` + placement (`PlaceEntityOnMap`/`MoveEntity`/`RemoveEntityFromMap`).
- `Map`: 2 flat arrays — creature occupancy per `(x,y,MapLayer)`, terrain per `(x,y,TerrainLayer)`. Separate so wall/floor don't collide.
- Map occupancy is governed by `IMapQuery.IsBlocking`, implemented on `World` from two `MultiComponentPool` markers — `ForceBlockingComponent` (wins if present) then `NonBlockingComponent`, else default blocking; both are many-source/count-based so overlapping sources (assumed for all effects) work correctly. `OccupancyComponent` (`IsTiny`/`IsPhasing`) is rendering-only now (`MapWindow`'s tiny-grid/phasing-alpha treatment) and no longer affects collision.
- `FloorBuilder`: `CreateMap` must run before `GameBootstrapper.Build` (MovementModule.Configure needs IMapQuery); `PopulateFloor` needs EntityManager/ComponentManager from Build. Real ordering constraint.

## Blueprints (Game.Blueprints)
- `IBlueprint.Build(ComponentManager, entityId)` = entity template.
- `CompositeBlueprint`: ordered parts (e.g. race, then class) + optional `overrides` delegate. Later parts run after earlier — order matters for adjustments.

## Presentation
- `PresentationBootstrapper`: fixed service set (FontService, SpriteBatchRenderer, GlyphRenderer, TileRenderer, WindowService) — no module system (set is static).
- UI: `Window`/`WindowService`/`MapWindow`, content panes (`SelectionWindowContent`, `DebugWindowContent`). `NotificationCenter` can block gameplay update for a frame (checked in `GameLoop.Update` before `EcsContext` advances).

## UI changes
Visually verify any Presentation/UI change by running the game and looking at it — no screen captures.

## Scale
`GameLoop.InitialEntityCapacity`/`InitialComponentCapacity` sized for TestMapBuilder's 1000×1000×3 map (~2.6M entities) to avoid reallocate-copy churn during population — not arbitrary defaults.
