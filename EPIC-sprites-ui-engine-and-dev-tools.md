# Epic: Sprites out of Game, a UIEngine, and the Engine IDE

(Epic -- a plan of plans. Child 1 landed 2026-10-11, child 2 2026-10-13, child 3 2026-10-14; the rest are not started. Each child below gets its own
`PLAN-*.md` when it starts, and is phased and tested in-game like any other plan. Decided 2026-10-11.)

## Why

Three problems with one cause: sprite and drawing code sits in the wrong layer, and the only tool that
shares it copies it. The tools this epic sets up are the start of the engine's IDE: one executable where
content is authored and a running game is inspected, while code is still written in Visual Studio + Claude.
- **Art decides simulation.** `TerrainRegistry` rolls a terrain cell's sprite variant from the map's
  `MathUtility` only when the manifest gives it more than one variant, and `EntityAppearance.TryGetSprite`
  picks a cell by counting the manifest's cells. Adding a second grass picture changes every seeded world.
- **Game owns rendering.** `Game/Sprites` holds the manifest model and loader (a static class reading the
  file on first use), cell transforms, placement, the reach box, link pieces and tint parsing.
  `ItemDefinition.SpriteTint` is set by `LootboxCatalog` from `LootboxRarityColors`, and `AuraField.TryGetGlow`
  blends aura colours inside Game. Only the decisions content makes and the facts the map knows belong there.
- **The dev tool copies by hand.** `DevTools/SpriteManifestBuilder` (WinForms, zero project references by
  design) mirrors `SpriteManifestEntry`/`Cell`/`Part`, `SpriteReach`, `SpriteTint`, `SpriteLinks` and
  `SpritePlacementMath`, each marked "keep in sync by hand" (~400 lines). Two more tools are coming that need
  far more than that: an Actions & Items editor that shows targeting the way Item Details does, and a map
  editor that must draw a map exactly the way the game does.

## Target layering

| Layer | Holds |
|---|---|
| **Game** | Rules and content: sprite *names* on definitions, rarity, link groups (`TerrainDefinition.LinkGroup`), entity seeds and neighborhood seeds, aura power per cell, targeting math (`TargetShapeResolver`), the footprint cap (`ResolvedBlueprint.MaximumFootprintTiles`, a Game constant), the hand-placed layout format |
| **Sprite format library** (no FNA, no Engine) | Manifest model and JSON -- authoring-only fields included -- reach box, link layouts and masks, placement math, tint text format. Referenced by UIEngine/Presentation and by the IDE, and by SpriteManifestBuilder while it is still WinForms |
| **UIEngine** (FNA; references only Engine and the format library) | Element/window framework, input, layers, tooltips, context menus, sprite and cell drawing, shaders |
| **Presentation** | Game windows (Item Details, target shape preview, inventory...), the map renderer over `IMapViewQuery`, wall link masks (`MapTerrainCells`, from the cells it reads), the rarity palette for text |
| **Executables** | The game, and the IDE: one FNA executable on UIEngine + Presentation + a Game build, each tool a tab (Actions & Items, Map, Sprites, Diagnostics). SpriteManifestBuilder stays a separate WinForms app on the format library until it becomes the Sprites tab (child 7) |

Dependency rules: Game never references the format library (it only names sprites); the format library
references nothing in the solution; UIEngine never references Game.

## Decisions

- **Variants are Presentation's.** Game hands out a seed and Presentation reduces it by its own variant
  count. Entities: the spawn record's seed, already there. Terrain: derived from the cell's position and
  its neighborhood's seed (`NeighborhoodRecord`), so it is deterministic, nothing is stored per cell and
  nothing is drawn from the map's `MathUtility`. Map generation no longer depends on the art. One-time
  reseed of every seeded world; benchmark baselines are re-recorded.
- **Loot box rarity tints move into the manifest**, as "SpriteDefinition: tint on the sprite itself"
  (TODO.md, Presentation) already describes: a tinted variant is its own entry ("Chest-Bronze"), and
  `LootboxCatalog` names it. `ItemDefinition.SpriteTint` and its `AreEquivalentOverrides` term go. Rarity
  colours used for text (loot box results) are a Presentation palette, not manifest entries.
- **Aura glow colour is Presentation's.** Game reports per-cell power per aura; Presentation picks and
  blends colours (and, with shaders, draws them). `AuraDefinition`'s glow colour stays content, read by
  Presentation through a view.
- **A missing sprite is not a build failure.** With the manifest out of Game, a mod's trial build can't
  check sprite names. A name the manifest doesn't have draws the glyph and logs one warning at load
  (Presentation-side validation after the build). `ResolvedBlueprint.IsSpawnable` keeps requiring that a
  sprite is *declared* (name or `NoSprite`), not that it exists.
- **The format library carries the manifest's authoring-only fields** (`ColumnSpan`/`RowSpan`, unfinished
  linked sets saved with null cells); the game's loader skips or tolerates them. Child 1 researches whether
  authoring objects should instead be separate types with a translation layer to the runtime manifest
  objects, and switches only if the research says so.
- **Map drawing comes out of `MapWindow`** into a renderer that reads only `IMapViewQuery` (plus a camera
  and the manifest), so the game feeds it a live session and the map editor an authored layout.
  `Tests/Presentation/FakeMapView.cs` already shows the interface can be stood in for.
- **The dev tools are one executable, the engine IDE**, each tool a tab. It is an FNA app on the real UI,
  not WinForms copies: tabs reuse the game's own windows and renderer, so what they show is what the game
  shows. Authoring tools (actions and items, maps, sprites) and debugging tools (the gauges and the rest of
  the F3 Diagnostics window, later inspection and the AI debugger) live there; code does not -- it stays in
  Visual Studio + Claude, and the IDE never edits C#.
- **Hand-placed content is Game data**: a layout file of blueprint Guids, offsets and compositions
  (`Compose`/`Apply`), merged into a `NeighborhoodPlan` by the generator, deterministic from the plan.
  The Map tab edits it; Game owns its format.
- **The IDE gets blueprints and catalogs from a real build** -- `ModValidation`'s trial build or a
  `GameBuildPass` over `CreatePlaceholderMap()` -- so mods' content appears in it with no new mechanism.

## Child plans, in order

Each is its own plan; numbering is the order. Arrows are hard dependencies.

1. **Sprite format library + manifest out of Game.** Landed 2026-10-11 (`PLAN-sprite-format-and-manifest-out-of-game.md`; notes in IMPLEMENTATION-NOTES "Tall sprites, wall faces and tops, and the sprite manifest"). New dependency-free project holding the manifest
   model (authoring-only fields included), JSON, reach, links, placement and tint format. The manifest
   becomes an instance loaded by Presentation (not a static class reading a file), which also gives
   "Content-only mods" (TODO.md, Engine) one place to merge mod entries. Game keeps sprite names;
   `SpriteComponent` as a per-instance override becomes a name (+ seed), not a resolved cell. Variant rolls
   become seeds; terrain variants derive from position and the neighborhood seed (reseed, baselines).
   `SpriteManifestBuilder` references the library and deletes its mirrors and the "keep in sync" comments.
   **Research first: authoring model vs runtime model.** Should the objects a tool edits be separate from the
   objects the game loads, with a translation (import/cook) step between them? Compare Unreal's editor-only
   data (`WITH_EDITORONLY_DATA`, stripped when cooking), Unity's source assets vs imported artifacts (the
   `AssetImporter` pipeline and `.meta` files), Godot's `.import` step and Bevy's asset processor (source
   assets processed into runtime ones, with meta files). Weigh: what the game pays for fields it never reads,
   where validation lives (on translation, or at load), whether a translated file is checked in or built,
   how "Content-only mods" (TODO.md) merge authoring vs runtime files, and whether the same split applies to
   the other content the IDE will edit (actions and items, map layouts). The result decides the pattern for
   every IDE tab, not only sprites.
2. **Rarity tints and aura glow out of Game** (→1). Landed 2026-10-13 (`PLAN-rarity-tints-and-aura-glow-out-of-game.md`; notes in IMPLEMENTATION-NOTES "Loot boxes" and "Auras, terrain contact, Healing Shrine, Holy Ground"). A loot box rarity is a tinted manifest entry ("Lootbox-Bronze");
   `AuraPowerView` reports power, Presentation blends (`AuraGlowBlend`).
3. **Map renderer out of `MapWindow`** (→1). Landed 2026-10-14 (`PLAN-map-renderer-out-of-map-window.md`; notes in IMPLEMENTATION-NOTES "Tall sprites, wall faces and tops, and the sprite manifest", "Where the map is drawn"): `MapRenderer`, `MapGameOverlay` and `MapCamera` in `Presentation.MapRendering`. Row walk, wall plan, overhang fade, glow, caches, drawn from
   `IMapViewQuery` + camera; `MapWindow` becomes input, state and hosting. Fold in "Review MapWindow for
   properties that belong on MapViewState instead" (TODO.md, Presentation, Low).
4. **Presentation / UIEngine split** (→1). "Split Presentation into Presentation + UIEngine projects"
   (TODO.md, Presentation, Low) is the scoping note: `UiInputController`'s game-specific branches behind a
   UIEngine hook, `ColorPalettes`/`Chrome` judged. Started when child 5 starts, so the split is shaped by a
   real second consumer rather than guessed.
5. **The IDE shell and its Actions & Items tab** (→4). The IDE executable: a tab host on UIEngine +
   Presentation + a Game build, the place every later tool goes. Its first tab edits actions and items and
   reuses Item Details and `TargetShapePreviewElement`/`TargetShapePreviewGeometry` as they are. Pairs with
   the data-driven actions and items plan (`PLAN-data-driven-actions-and-items.md`), which supplies the
   content format it edits. The smaller tool, so it proves the split first.
6. **Map tab** (→3, →5). Draws with child 3's renderer over an authored layout's `IMapViewQuery`. Needs
   the hand-placed layout format in Game and its merge into `NeighborhoodPlan`, which belongs with "Real map
   generation -- neighborhood templates" (TODO.md, Game, High): the tab authors layout templates and fixed
   pieces; "Composite buildings" (TODO.md, Game) are its first multi-entity placements.
7. **Shaders, then the Sprites tab** (→4, →5). "Sprite shaders and tinting" (TODO.md, Presentation) in
   UIEngine: the packed vertex-colour "uber" effect keeps the row walk's order; adds an `fxc` build step to
   Content. Tint multiply, phasing alpha, overhang fade and glow move onto it, and "Aura glow shouldn't tint
   wall tops" (TODO.md, Presentation, Low) is easier there. After this, SpriteManifestBuilder's GDI+ preview
   can no longer match: it becomes the IDE's Sprites tab, and the WinForms app is retired.
8. **Diagnostics tab** (→5). The debugging half of the IDE: gauges, frame costs, memory and leak reports --
   what the F3 Diagnostics window and `Log/diagnostics/` show today -- for a running game. Research first
   whether the IDE hosts the session itself (in-process, so `EngineHooks`' static channels just work, with a
   game view tab) or attaches to a separate game process (a transport for gauge samples and reports, and a
   game that runs without the IDE). Later debugging tools (entity inspection, "Debug drawing and an AI
   debugger", TODO.md) follow whichever it picks.

1 and 2 can land long before any tool exists and fix the determinism leak on their own. 3 is worth doing
for `MapWindow`'s sake alone. 4 waits for 5. 6, 7 and 8 are independent of each other once 5 exists.

## Open questions (for the child plans, not this one)

- Authoring objects vs runtime objects: answered by child 1 -- one model per content format, no translation file; editor state never goes in a content file (IMPLEMENTATION-NOTES, "Where the manifest lives").
- The IDE hosting a game session vs attaching to a game process (child 8's research).
- The IDE's name and where its project lives (`DevTools/` alongside the sprite builder, or its own root).
