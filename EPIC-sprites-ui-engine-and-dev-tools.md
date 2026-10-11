# Epic: Sprites out of Game, a UIEngine, and FNA Dev Tools

(Epic -- a plan of plans. Pre-implementation; no child plan is written yet. Each child below gets its own
`PLAN-*.md` when it starts, and is phased and tested in-game like any other plan. Decided 2026-10-11.)

## Why

Three problems with one cause: sprite and drawing code sits in the wrong layer, and the only tool that
shares it copies it.
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
  far more than that: an ActionAndItemBuilder that shows targeting the way Item Details does, and a map
  builder that must draw a map exactly the way the game does.

## Target layering

| Layer | Holds |
|---|---|
| **Game** | Rules and content: sprite *names* on definitions, rarity, link masks (`StructureLinks`, read from the map), raw variant rolls, aura power per cell, targeting math (`TargetShapeResolver`), the footprint cap (`ResolvedBlueprint.MaximumFootprintTiles`, a Game constant), the hand-placed layout format |
| **Sprite format library** (no FNA, no Engine) | Manifest model and JSON, reach box, link layouts and masks, placement math, tint text format. Referenced by UIEngine/Presentation and by every dev tool, including the WinForms one |
| **UIEngine** (FNA; references only Engine and the format library) | Element/window framework, input, layers, tooltips, context menus, sprite and cell drawing, shaders |
| **Presentation** | Game windows (Item Details, target shape preview, inventory...), the map renderer over `IMapViewQuery`, the rarity palette for text |
| **Executables** | The game, ActionAndItemBuilder and MapBuilder, all three on UIEngine + Presentation. SpriteManifestBuilder stays WinForms on the format library until shaders make its preview wrong (child 7) |

Dependency rules: Game never references the format library (it only names sprites); the format library
references nothing in the solution; UIEngine never references Game.

## Decisions

- **Variants are Presentation's.** Game hands out a seed (entities: the spawn record's, already there;
  terrain: a raw roll stored in the cell, drawn unconditionally), and Presentation reduces it by its own
  variant count. Map generation no longer depends on the art. One-time reseed of every seeded world;
  benchmark baselines are re-recorded.
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
- **Map drawing comes out of `MapWindow`** into a renderer that reads only `IMapViewQuery` (plus a camera
  and the manifest), so the game feeds it a live session and the map builder an authored layout.
  `Tests/Presentation/FakeMapView.cs` already shows the interface can be stood in for.
- **Dev tools are FNA apps on the real UI**, not WinForms copies: they reuse the game's own windows and
  renderer, so what they show is what the game shows.
- **Hand-placed content is Game data**: a layout file of blueprint Guids, offsets and compositions
  (`Compose`/`Apply`), merged into a `NeighborhoodPlan` by the generator, deterministic from the plan.
  The map builder edits it; Game owns its format.
- **Dev tools get blueprints and catalogs from a real build** -- `ModValidation`'s trial build or a
  `GameBuildPass` over `CreatePlaceholderMap()` -- so mods' content appears in them with no new mechanism.

## Child plans, in order

Each is its own plan; numbering is the order. Arrows are hard dependencies.

1. **Sprite format library + manifest out of Game.** New dependency-free project holding the manifest
   model, JSON, reach, links, placement and tint format. The manifest becomes an instance loaded by
   Presentation (not a static class reading a file), which also gives "Content-only mods" (TODO.md, Engine)
   one place to merge mod entries. Game keeps sprite names; `SpriteComponent` as a per-instance override
   becomes a name (+ seed), not a resolved cell. Variant rolls become seeds (reseed, baselines).
   `SpriteManifestBuilder` references the library and deletes its mirrors and the "keep in sync" comments.
2. **Rarity tints and aura glow out of Game** (→1). "SpriteDefinition: tint on the sprite itself";
   `AuraGlowView` reports power, Presentation blends.
3. **Map renderer out of `MapWindow`** (→1). Row walk, wall plan, overhang fade, glow, caches, drawn from
   `IMapViewQuery` + camera; `MapWindow` becomes input, state and hosting. Fold in "Review MapWindow for
   properties that belong on MapViewState instead" (TODO.md, Presentation, Low).
4. **Presentation / UIEngine split** (→1). "Split Presentation into Presentation + UIEngine projects"
   (TODO.md, Presentation, Low) is the scoping note: `UiInputController`'s game-specific branches behind a
   UIEngine hook, `ColorPalettes`/`Chrome` judged. Started when child 5 starts, so the split is shaped by a
   real second consumer rather than guessed.
5. **ActionAndItemBuilder** (→4). FNA executable on UIEngine + Presentation + a Game build. Reuses Item
   Details and `TargetShapePreviewElement`/`TargetShapePreviewGeometry` as they are. Pairs with the
   data-driven actions and items plan (`PLAN-data-driven-actions-and-items.md`), which supplies the content
   format it edits. The smaller tool, so it proves the split first.
6. **MapBuilder** (→3, →4). FNA executable drawing with child 3's renderer over an authored layout's
   `IMapViewQuery`. Needs the hand-placed layout format in Game and its merge into `NeighborhoodPlan`,
   which belongs with "Real map generation -- neighborhood templates" (TODO.md, Game, High): the builder
   authors layout templates and fixed pieces; "Composite buildings" (TODO.md, Game) are its first
   multi-entity placements.
7. **Shaders** (→4). "Sprite shaders and tinting" (TODO.md, Presentation) in UIEngine: the packed
   vertex-colour "uber" effect keeps the row walk's order; adds an `fxc` build step to Content. Tint
   multiply, phasing alpha, overhang fade and glow move onto it, and "Aura glow shouldn't tint wall tops"
   (TODO.md, Presentation, Low) is easier there. After this, SpriteManifestBuilder's GDI+ preview can no
   longer match: port it to FNA on UIEngine (its WinForms authoring kept or replaced, decided then).

1 and 2 can land long before any tool exists and fix the determinism leak on their own. 3 is worth doing
for `MapWindow`'s sake alone. 4 waits for 5.

## Open questions (for the child plans, not this one)

- Whether the format library also carries the manifest's *authoring-only* fields (`ColumnSpan`/`RowSpan`,
  unfinished linked sets saved with null cells) or the builder wraps the shared records.
- The terrain variant roll's width (a byte per cell, or derived from position and the neighborhood seed so
  nothing is stored at all).
- Whether dev tools share one executable with modes or ship as separate ones.
