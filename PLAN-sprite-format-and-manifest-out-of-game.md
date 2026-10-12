# Sprite Format Library, and the Manifest out of Game

(Child 1 of `EPIC-sprites-ui-engine-and-dev-tools.md`. Landed 2026-10-11, all 6 phases; durable notes in
IMPLEMENTATION-NOTES.md "Tall sprites, wall faces and tops, and the sprite manifest". Where the build differs from
the text below, each phase's "As built" says so.)

## Context

What exists today:
- `Game/Sprites` (10 files, ~610 lines) holds the manifest model (`SpriteManifestEntry`/`Part`/`Cell`,
  `SpriteLinking`), a **static** `SpriteManifest` that reads `SpriteManifest.json` next to the executable
  on first use, the load checks, `SpriteReach`, `SpriteLinks` (masks, fallbacks, weighted variant sets),
  `SpritePlacement`, `SpriteCellTransform`/`ManifestSprite` and `SpriteTint`. It uses XNA's `Rectangle`
  and `Color`.
- **Game resolves sprites to pixels.** `EntityAppearance.TryGetSprite(seed)` counts the entry's cells and
  hashes the seed into one. `TerrainRegistry` resolves a `TerrainSpriteSet` per definition, and
  `MapViewQuery` hands Presentation a resolved `SpriteView` (sheet, source rectangle, placement, transform,
  tint) for entities, terrain, wall faces and wall tops (`StructureTopView.RaisedRows` is the face's height,
  a manifest fact). `SpriteViews.TryGetFirst(name)` is how ~10 Presentation elements get icons.
- **Art decides the map.** `TerrainRegistry.CreateCell(typeId, mathUtility)` draws a variant from the
  neighborhood's layout sequence only when the manifest gives that terrain more than one variant, and the
  byte is stored per cell: `MapNeighborhood.TerrainVariants` (2 layers) and `StructureVariants` (3 layers),
  5 MiB per full neighborhood, ~60 MiB across a settled window (9 + 3 cached). `TestMapBuilder` resolves
  every terrain's variant count on the main thread at construction (`WithVariantsResolved`) so a worker can
  read it. `StructureAdminCommands` rolls a wall's or grass's variant from a `Random` seeded by position.
- **Joining reads the manifest.** `TerrainRegistry.GetLinkGroup` takes a wall's group from its sprite entry
  (its top's `LinkGroup`, else the entry name). No entry in `Content/SpriteManifest.json` sets `LinkGroup`,
  so today the group is always the sprite name. `StructureLinks.MaskFor` picks a 4-bit or 2-bit mask by the
  part's `SpriteLinking`.
- `SpriteComponent` (Core, Packed pool) is a per-instance override holding a sheet path and rectangle.
  Nothing in production writes one; `MapViewQuery` reads it, two tests write it.
- **Backgrounds are a colour wash under every tile.** `IMapViewQuery.GetBackgroundColor` picks the Blocking
  occupant's `BackgroundComponent` (Core, Packed pool, blended on merge -- nothing in production writes one
  any more), else the structure's `TerrainDefinition.BackgroundColor`, else the terrain's, else white
  (the Flying layer, which has no floor); black off the map. `MapBackgroundCache` holds one colour per
  visible tile, shifted on scroll, and `TileRenderer.DrawBackgrounds` draws a rectangle per visible tile
  into the cached terrain image, under the floors, each time it is rebuilt. Lava, Holy Ground and stone floor have no sprite: lava
  is its glyph ("~") on an OrangeRed wash under its aura's glow.
- `ResolvedBlueprint.MaximumFootprintTiles = SpriteReach.MaximumRowsAbove + 1` -- a Game rule computed from
  a sprite constant.
- `DevTools/SpriteManifestBuilder` (WinForms, `net10.0-windows`, no project references) mirrors the model
  (`ManifestEntry`/`Part`/`Cell`/`File`), `Tints`, `LinkMask`, `LinkLayout`'s fallbacks and
  `SpritePlacementRules`' reach check and message, each marked "keep in sync by hand". It also holds the
  authoring operations nothing else has: canonical turn/flip (`RotatedClockwise`, `MirroredHorizontally`...),
  `Spanning`, mask mirroring and turning for `SlotFiller`, and `EntryRules.SpanOf`.
- The manifest file holds 21 entries. The entry-level `ColumnSpan`/`RowSpan` (4 uses) are the builder's
  authoring state; Game's records don't declare them, so deserialization skips them.

## Research: authoring objects vs runtime objects

The question the epic hands this plan: should the objects a tool edits be separate types from the ones the
game loads, with an import/cook step between them? The answer sets the pattern for every IDE tab.

**Unreal.** One `UObject` class serves editor and game. Editor-only properties sit inside
`#if WITH_EDITORONLY_DATA` and cooking strips them; cooking also converts source formats (textures to
platform compression, shaders compiled). The split that matters is *source format vs platform format*,
done by the cooker. Data assets (`UDataAsset`) are not translated: the same type, minus editor data.

**Unity.** Source assets (PNG, FBX) plus a `.meta` file (GUID and import settings, checked in). The
`AssetImporter` pipeline writes imported artifacts to `Library/`, never checked in, rebuilt on demand.
Unity-native data (`ScriptableObject`, prefabs) has no import step: the editor and player load the same
serialized type. Editor-only fields under `#if UNITY_EDITOR` on a serialized type are a known trap: the
player build's serialization layout no longer matches the data ("different serialization layout" errors),
so Unity steers editor state into separate editor-only objects or the `.meta` file.

**Godot 4.** Same split: a source file plus a `.import` file (settings, checked in) and the imported
resource under `.godot/imported/` (ignored, rebuilt). Native resources (`.tres`/`.res`) load as they are,
edited and run as one type.

**Bevy.** The asset processor (0.12+) turns `assets/` into `imported_assets/` using per-asset `.meta`
files naming a loader and processor; processing is optional and the default is to load sources directly.

**What they agree on.**
- Translation exists for **foreign or heavy source formats** (images, meshes, audio, shaders) where the
  runtime form is different and costly to produce. The engine's own data assets are one type, edited and
  loaded.
- Translated output is **built, never checked in** (`Library/`, `.godot/imported/`, `imported_assets/`,
  cooked content). Only sources and their settings files are versioned.
- **Editor state that isn't content goes beside it, not in it** (`.meta`, `.import`), and per-user UI
  state goes in user settings. Conditional fields on a shared serialized type (Unity) cause mismatches.

**Weighed for this game.**
- *What the game pays for fields it never reads.* Two optional ints per entry and per part, in a 21-entry
  file read once. Nothing.
- *Where validation lives.* Today it is written twice (Game's load check, the builder's save check, the
  same message by hand). With one model it is one function in the library, run at load (the game refuses
  the file) and at save (the builder refuses the entry). A translation step would add a third place.
- *Checked in or built.* With no translation there is nothing to cook. The game's in-memory resolved form
  (XNA rectangles and colours, cells pre-indexed per entry) is the "import": done at load, microseconds,
  never written to disk -- the same thing the engines' importers do, without a cache file.
- *Content-only mods* (TODO.md, Engine). A mod ships the same JSON shape, merged by entry name at load. A
  cooked format would make every modder run a cook step, which is the friction that TODO exists to remove.
- *The other content the IDE edits* (actions and items, map layouts). Those are Game data, also written by
  our own tools, also small. The same answer holds.

**Conclusion: one model per content format, no translation file.**
- The format library's records are what the builder edits and what Presentation loads. Fields only the
  tool uses are allowed on them when they are **content** -- they must survive in the repository for the
  next person editing the entry (`ColumnSpan`/`RowSpan`, an unfinished linked set's empty slots) -- and
  are documented "authoring only; the game ignores it".
- Anything that is editor **state** rather than content (window layout, last-used sheet, zoom, the current
  selection) never goes in a content file: user settings, or a sidecar file if it must travel with the
  content.
- Loading resolves the model into whatever runtime shape the reader wants, in memory, and validation runs
  there. A translation layer is added only when a format is foreign or expensive to read at load -- none is.

Every IDE tab follows this: Game owns the actions/items and layout formats the same way the library owns
the sprite format.

## Design

### The format library: `SpriteFormat`

A new project at the root, `SpriteFormat/SpriteFormat.csproj` (`net10.0`, namespace `SpriteFormat`, no
package or project references). Referenced by Presentation, Tests and SpriteManifestBuilder; **never** by
Game or Engine. It holds everything about the file and its rules, and nothing about drawing:

| Type | From | Notes |
|---|---|---|
| `SpriteManifest` | Game `SpriteManifest` (static) | An **instance**: entries by name, in file order. `Parse(json)`, `Load(path)`, `Serialize()` (indented, file order -- the builder's save). Parse runs the checks below. |
| `SpriteManifestEntry`, `SpriteManifestPart`, `SpriteManifestCell`, `SpriteLinking` | Game + the builder's mirrors | One set of records. Entry and part gain `ColumnSpan`/`RowSpan` (authoring only). The cell gains the builder's canonical transform operations (`RotatedClockwise`, `RotatedCounterClockwise`, `MirroredHorizontally`, `MirroredVertically`, `WithoutTransform`, `Spanning`). |
| `SpriteSourceRectangle` | XNA `Rectangle` | `readonly record struct (X, Y, Width, Height)`. `SpriteManifestCell.SourceRectangle` returns it. |
| `SpriteColor` | XNA `Color` | `readonly record struct (R, G, B, A)`. |
| `SpriteTint` | Game `SpriteTint` + builder `Tints` | Parse/format "#RRGGBB(AA)", multiply, on `SpriteColor`. |
| `SpriteLinks` | Game `SpriteLinks` + builder `LinkMask`/`LinkLayout` | Mask bits, cells per set, fallbacks, `SetIndexFor`, `CellIndexFor`, plus the builder's mask mirrors and turns (what `SlotFiller` needs). The builder's slot-grid display layout stays in the builder. |
| `SpriteReach`, `SpritePlacement`, `SpriteCellTransform` | Game | Unchanged apart from the namespace. |
| `SpritePlacementTiles` | Presentation `SpritePlacementMath.DrawnTiles` + builder `SpritePlacementRules.DrawnTiles`/`HeightRows` | Where a placed cell draws, in tiles, at a footprint, including the shrink-to-fit. Presentation's `SpritePlacementMath` keeps only the pixel half (`DrawnRectangle`), on top of this. |
| `SpriteManifestValidation` | Game `SpriteManifest.Validate` + builder Save checks | One function per rule, returning the message; `Parse` throws `InvalidDataException` with it, the builder shows it. |

The JSON shape does not change. A round-trip test pins it: `Serialize(Parse(file))` equals
`Content/SpriteManifest.json` byte for byte (line endings normalised).

`LinkGroup` leaves the manifest (see "Joining" below). Parse tolerates and drops it if a file still has
one, so an old file still loads.

### The manifest is a Presentation instance

- `PresentationBootstrapper` loads `SpriteManifest.Load(Path.Combine(AppContext.BaseDirectory,
  "SpriteManifest.json"))` once and builds a **`SpriteManifestResolver`** (Presentation.Rendering) from it,
  held on `PresentationContext` beside `SpriteSheetService`. An invalid file throws at startup with the
  entry's name, as it does now (now an `InvalidDataException` rather than a `TypeInitializationException`
  on first sprite use).
- The resolver pre-resolves every entry once into XNA shapes: per entry an array of `ResolvedSprite`
  (sheet, `Rectangle`, placement, transform, `Color?` tint) for the face and one for the top, plus the
  top's raised rows. `ResolvedSprite` replaces `ManifestSprite`; `SpriteView` moves to Presentation as the
  same record (it is only ever drawn).
- Lookups:
  - `TryGetFirst(name)` -- icons, badges, chrome (what `SpriteViews.TryGetFirst` does today).
  - `TryGetForEntity(name, seed)` -- the variant an entity's seed picks. The hash moves here from
    `EntityAppearance.Variant` **unchanged, salt included**, so no entity changes look.
  - `TryGetTerrainSprites(name)` -- a `TerrainSpriteSet` (moved from Game, otherwise as today) that picks
    face and top pieces from a cell's seed and neighbour mask.
- One instance is also the place "Content-only mods" will merge mod entries by name -- not built here.

### What Game hands out instead of pixels

Game names sprites and hands out seeds and type ids; it never counts cells or picks a piece.

- `EntityVisualView(string? SpriteName, uint SpriteSeed, string Glyph, Color GlyphColor, bool IsDead)`
  replaces `SpriteView? Sprite`. From a blueprint: `appearance.SpriteName` and the spawn record's seed.
- `SpriteComponent` (per-instance override) becomes `(string SpriteName, uint Seed)`. Still Packed, still
  Core, still written by nothing in production.
- `ChargingActionView` carries `SpriteName` (an icon: always the first cell).
- `TerrainView` (the inspector's and the context menu's single-tile read) keeps name and description and
  carries the cell's type id and seed in place of a visual.
- Terrain and structures as the map draws them are read by area, not per tile (next section):
  `TryGetTerrainVisual`, `TryGetStructureVisual`, `TryGetStructureTopVisual`, `HasStructure` and
  `StructureTopView` are deleted from `IMapViewQuery`.
- `EntityAppearance` keeps `SpriteName` and loses `TryGetSprite`; `SpriteViews` is deleted.
- Occupant reads (`GetOccupants`, `GetBlockingEntityId`, `TryGetOccupant`, `TryGetVisual`, status, badges)
  stay per tile and per entity; see "Out of scope". `GetBackgroundColor` is gone (next section).

### Backgrounds: a terrain's sprite is its background

Nothing standing on a tile colours it, and terrain has no colour separate from its picture.
- `BackgroundComponent` and its pool are deleted, with the occupant rule in `GetBackgroundColor`.
- `TerrainDefinition.BackgroundColor` is deleted. A floor's sprite covers its tile; a terrain without one
  draws its glyph, if it has one, on the map's flat fill (`MapWindow.MapBackgroundColor`), under its aura's
  glow. Lava is "~" under its orange glow until a lava sprite is chosen; Holy Ground is "*" under its white
  glow; stone floor (no sprite, no glyph) is the flat fill, which shows only where a wall is taken away
  (and Admin's remove lays grass there). Transparent pixels in a floor sprite show the flat fill, not a
  colour.
- `IMapViewQuery.GetBackgroundColor`, `MapBackgroundCache` and `TileRenderer.DrawBackgrounds` are deleted,
  and with them the colour cache's upkeep on every scroll and a rectangle per visible tile in each rebuild of
  the terrain image. Off the map stays black: the terrain image fills an
  off-map tile black when it is built, from the same on-map test it already makes per tile (from phase 5,
  the buffer's on-map flag).
- The Flying layer, which has no floor, shows the flat fill where it showed white.

### The terrain image reads an area, not tiles

Today a rebuild of the cached terrain image (on every whole-tile camera move, and on terrain and structure
changes in view) makes several interface calls per visible tile -- about 1,800 tiles at Team zoom, 28,000
at Borough -- and every call finds the tile's neighborhood again. Since this plan rewrites that part of
`IMapViewQuery` anyway, it is rewritten once, as an area read:

- **`IMapViewQuery.CopyCells(MapBounds area, int mapLayer, MapCellBuffer cells)`** fills a caller-owned,
  reused buffer for a rectangle: per cell the terrain type id (0 where the layer has no floor), the
  structure type id, the terrain seed, the structure seed, and whether the cell is on the map. Row-major,
  X fastest. Inside, Game finds each neighborhood once per row segment and copies contiguous runs of its
  arrays; seeds are computed in the same pass (`CellSeeds`). Nothing is allocated per call.
- **`IMapViewQuery.Terrain`** is the `TerrainRegistry` the ids refer to (the same one `GameCatalogs.Terrain`
  hands out), so whoever implements the view -- the live session, `FakeMapView`, the map editor's authored
  layout (child 6) -- supplies ids and their definitions together.
- **Presentation decides the look.** It keeps a per-type-id table built once per definition (and rebuilt on
  `TerrainRegistry.DefinitionChanged`): the resolved `TerrainSpriteSet` from the definition's
  `SpriteName`, its glyph, and its link group. A rebuild then does no string lookup and no interface call
  per tile: it reads the buffer, picks floor and wall pieces from the seeds, works out join masks from the
  neighbouring ids already in the buffer (next section), and plans faces, tops and overhangs
  (`MapWallPlan` over the buffer instead of the view).
- **The area** is the visible grid plus the overscan (`SpriteReach`'s rows below and columns beside) plus
  one tile all round, so every cell's four neighbours are in the buffer for its mask.
- It takes any rectangle, not fixed chunks. Caching the drawn image per 32x32 chunk, so a one-tile scroll
  draws only the new strip instead of the whole image, is a renderer change for child 3; an area read is
  what it needs.
- A frame-cost line around the rebuild (`EngineHooks.FrameCost`, "Map" / "Terrain cache rebuild") is added
  before the change, so the saving is measured rather than assumed.

### Terrain variants from position and the neighborhood seed

- `TerrainCell` becomes `(ushort TypeId)`. `MapNeighborhood.TerrainVariants` and `StructureVariants` are
  deleted, and so are `TerrainRegistry.CreateCell`/`GetVariantCount`, `TestMapBuilder.WithVariantsResolved`
  and `StructureAdminCommands.VariantRollsFor`. Writers write a type id.
- `MapNeighborhood` holds its **`LayoutSeed`** (one int): `Map.CreateLayout(cellX, cellY, layoutSeed)` --
  `TestMapBuilder.Plan` passes `record.Seed` -- and `LoadNeighborhood(layout)` takes it, also when it
  copies into an already-loaded neighborhood. An empty `LoadNeighborhood(cellX, cellY)` (placeholder and
  test maps) uses 0.
- `Game.World.CellSeeds.For(layoutSeed, x, y, store)` -- a 32-bit avalanche like `NeighborhoodRecord.Mix`
  over the seed, the world coordinates and a salt for the store (terrain per `TerrainLayer`, structure).
  `Map.GetTerrainSeed`/`GetStructureSeed` compute it on read; nothing is stored. A wall's face and top read
  the same seed, so matching chances still pick matching sets.
- Presentation reduces the seed exactly as `TerrainSpriteSet` reduces a variant today: a percentile
  (`seed % 100`) when either part gives chances, else `seed % setCount` per part.
- So the same tile looks the same on every visit, after an admin edit, and in an editor given the same
  seed; adding a picture changes only how a seed is reduced, never the map.
- **One-time reseed**: the layout sequence no longer advances for variant rolls, so every seeded world's
  layout changes once. Benchmark baselines are re-recorded.

### Joining: the group is Game's, the mask is Presentation's

This refines the epic, which listed link masks under Game. Which structures belong together is content;
which picture that makes a wall draw is a display rule.
- `TerrainDefinition.LinkGroup` (`string?`): structures with the same group join. Null means its
  `SpriteName`, else its key -- exactly today's behaviour, since no manifest entry sets a group.
  `TerrainRegistry.GetLinkGroup` reads only the definition.
- Presentation computes the masks from the cell buffer: a neighbour joins when it is on the map and its
  structure's link group matches, or when it is off the map (so a wall running into an unloaded
  neighborhood keeps a straight run, as today). A top uses the four neighbours, a face its east and west.
  The rule moves from `StructureLinks`/`MapViewQuery` into a Presentation planner over the buffer, and uses
  `SpriteLinks`' mask bits directly; `Game/Terrain/StructureLinks.cs` and `INeighbourLinks` are deleted.

### Constants that crossed the line

- `ResolvedBlueprint.MaximumFootprintTiles` becomes the Game constant `3`, its remark naming the reach box.
  A test asserts it equals `SpriteReach.MaximumRowsAbove + 1`.
- `MapDrawOrder`, `MapWallPlan` and `SpritePlacementMath` read `SpriteFormat.SpriteReach` directly.

### A missing sprite is a warning, not a failure

- After the session is built, Presentation checks every sprite name Game content declares against the
  manifest and writes one line per missing name to `Console.Error` (where load and settings failures go):
  "Sprite 'X' (blueprint Goblin) is not in the sprite manifest; it draws its glyph."
- Names come from a Game view, `ContentSpriteNames` (on `GameViews`): blueprints' appearances, terrain
  definitions, actions and items -- each with what declared it. Presentation's own names (`LootBag-Red`,
  `Inventory`, `StatusEffect-*`, folder and button sprites) are pinned by a unit test against the shipped
  manifest instead.
- A miss at draw time draws the glyph, as today. `ResolvedBlueprint.IsSpawnable` is unchanged.

### SpriteManifestBuilder on the library

- References `SpriteFormat`; still no Game, Engine or FNA. Deletes `ManifestCell`/`ManifestEntry`/
  `ManifestPart`/`ManifestFile`, `Tints`, `LinkMask.Compute`, and `SpritePlacementRules`' reach check,
  message and `HeightRows`. Every "keep in sync by hand" comment goes, and the csproj comment says what it
  references and why.
- Keeps what is only UI: the slot grid's display layout, `Describe`/`SpanText` (as extension methods),
  `EntryRules.SpanOf`, dialogs and preview renderers. `System.Drawing` conversions sit at the drawing edge
  (`CellDrawing`, the preview renderers): `SpriteSourceRectangle`/`SpriteColor` to `Rectangle`/`Color`.
- The link-group combo box goes with the manifest field; the link preview joins an entry with itself.

### Out of scope

- `ItemDefinition.SpriteTint` and loot box rarity tints, and aura glow colours: child 2.
- Map drawing out of `MapWindow`: child 3. This plan only changes what `MapWindow`, `MapWallPlan` and
  `MapDrawOrder` read. Also child 3: per-chunk caching of the terrain image, an area read for the per-frame
  occupant walk (sparse and changing every frame, so its gain is less clear and is measured there).
- Choosing a lava sprite (or any floor sprite for Holy Ground and stone floor): content, whenever art is
  picked; it needs only a manifest entry and the definition's `SpriteName`.
- Content-only mods themselves: the resolver is where they will merge; nothing loads a mod's manifest yet.

## Decisions (confirmed 2026-10-11)

1. **Project name `SpriteFormat`**, at the repository root next to Engine/Game (it may move later). Not
   under `DevTools/`: the game references it, so it isn't a dev tool.
2. **`LinkGroup` leaves the manifest** for `TerrainDefinition.LinkGroup`. No content sets it; keeping it in
   the manifest as a builder-only preview hint would make the builder and the game able to disagree about
   what joins. The cost, accepted as temporary: the builder can't preview two entries joining each other
   until child 6/7.
3. **Entity variants keep today's hash**, moved into Presentation, so no entity changes look; only terrain
   reseeds.
4. **`LayoutSeed` on `MapNeighborhood`** rather than reading `NeighborhoodRecords` from the view: the map
   carries everything a cell's look depends on, so the map editor's authored layout (child 6) supplies a
   seed the same way.
5. **A terrain's sprite is its background.** `BackgroundComponent` and `TerrainDefinition.BackgroundColor`
   are both deleted; terrain without a sprite shows its glyph and its aura's glow on the flat fill (lava,
   until a lava sprite is chosen).

## Phases

Each phase builds, passes `dotnet test`, and is checked in-game before the next.

### Phase 1 -- the `SpriteFormat` library, and the builder on it
- Create the project and add it to the solution, Presentation (unused yet), Tests and the builder.
- Move the model, JSON, checks, reach, links (with the builder's mask mirrors/turns), placement in tiles,
  transform and tint into it, on `SpriteSourceRectangle`/`SpriteColor`. Game is untouched: for the phases
  between, Game's copy and the library both exist; Game's goes in phase 5.
- The builder switches to the library and deletes its mirrors (above).
- Tests: move `SpriteManifestFormatTests` to the library; the round-trip test; the canonical-transform and
  mask-mirror tests the builder's operations need; an architecture test that `SpriteFormat` references no
  solution assembly and no `Microsoft.Xna.Framework`.
- **In-game / in-tool:** open the builder, open every entry (face, top, linked sets, weights, tints,
  spans), save each unchanged -- `git diff Content/SpriteManifest.json` stays empty. Fill a linked set's
  empty slots by mirroring and turning; the placement and link previews look as before; an entry outside
  the reach box is refused with the same message.

### Phase 2 -- terrain variants from position and the neighborhood seed
- `CellSeeds`, `LayoutSeed` on `MapNeighborhood`/`NeighborhoodLayout`, `Map.GetTerrainSeed`/
  `GetStructureSeed`; `TerrainCell` loses `Variant`; the variant arrays, `CreateCell`, `GetVariantCount`,
  `WithVariantsResolved` and `VariantRollsFor` go. Game still resolves sprites this phase: `MapViewQuery`
  passes the cell's seed where it passed the variant, and `TerrainSpriteSet` takes a `uint` seed.
- Tests: the same cell gives the same seed across unload/reload and from a copied layout; different
  stores at one cell differ; a seeded plan's layout no longer depends on how many variants the
  manifest gives a terrain; `TerrainRegistryTests` variant tests move to seed reduction.
- Re-record benchmark baselines (`phase-performance-testing`, fixed seed) and record the memory drop
  (gauges: ~5 MiB per loaded neighborhood).
- **In-game:** grass and dirt still show mixed variants; wall faces and tops still join and pick matching
  sets; walk away until a neighborhood unloads and come back -- every tile looks as it did; Admin build a
  wall and remove it -- the grass left behind has a variant, and rebuilding gives the same wall look.

### Phase 3 -- backgrounds are terrain sprites
- Delete `BackgroundComponent` (its Core pool registration, `MapViewQuery`'s pool), `TerrainDefinition.
  BackgroundColor` (every definition and test fixture loses the argument), `IMapViewQuery.
  GetBackgroundColor`, `MapBackgroundCache` and `TileRenderer.DrawBackgrounds`. The terrain image fills
  off-map tiles black. `MapWindow`'s camera-follow remarks that describe background re-resolving go with it.
- Before deleting, a windowed frame-cost measurement of the map draw at Team and Borough zoom on a fixed
  seed; after, the same. (Done: no measurable change in MapWindow's draw, which fits backgrounds having been
  drawn into the cached image rather than every frame; the machine drifted during the session, so the
  comparison is MapWindow's draw against the HUD's in the same run, about 7.8x both before and after.)
- Tests: `MapViewQueryTests`' background tests go; `GameModuleIntegrationTests` stops expecting the pool;
  `MapWindowArchitectureTests`' fixture drops it; a test that an off-map tile in the terrain image is
  black.
- **In-game:** grass, dirt and walls look as before (check for transparent pixels now showing the dark
  fill); lava is a yellow "~" under its orange glow; Holy Ground a "*" under its white glow; Admin remove a
  wall in the hallway cross and the grass laid there draws normally; scroll to the map edge -- off-map is
  black; switch to the Flying layer -- the flat fill, not white; zoom to Borough and pan -- no stale
  colours anywhere.

### Phase 4 -- Presentation owns the manifest; icons through it
- `PresentationBootstrapper` loads the manifest and builds `SpriteManifestResolver` (icons and
  `TryGetFirst` only this phase); `ElementFactoryRegistry` and the elements that call
  `SpriteViews.TryGetFirst` (Button, Folder, CurrencyElement, DragGhostContent, HotbarContent,
  InventoryItemStackCell, ItemIconElement, FloatingTextRenderer, MapWindow's loot bag) take the resolver
  where they take `SpriteSheetService`. `SpriteViews` goes. `SpriteView` moves to Presentation.
- As built: the resolver is `SpriteSheetService.Sprites` (the service every one of those elements already
  holds -- names to cells, cells' sheets to textures), not a parameter beside it. `SpriteView` stays in Game
  and the resolver converts into it, and `SpriteViews` stays for `MapViewQuery`'s charging badge; both move
  or go in phase 5 with the rest of the map views.
- Tests: Presentation's own sprite names exist in the shipped manifest; resolver first-cell lookups match
  what `SpriteViews` returned.
- **In-game:** hotbar, inventory cells and tabs, folder and window buttons, currency row, drag ghost,
  floating status-effect icons, loot box results (still tinted through `ItemDefinition.SpriteTint`), shop
  and trade cells all show their sprites.

### Phase 5 -- map views hand out names, seeds and type ids; `Game/Sprites` deleted
- First, before anything else in the phase: the "Terrain cache rebuild" frame-cost line, and a windowed
  measurement of it at Team and Borough zoom on a fixed seed (scrolling, so it rebuilds), kept as the
  before figure.
- The view changes above (`EntityVisualView`, `SpriteComponent`, `ChargingActionView`, `TerrainView`, the
  per-tile terrain and structure members deleted); `CopyCells`/`MapCellBuffer` and `IMapViewQuery.Terrain`;
  `EntityAppearance.TryGetSprite` and its hash move to the resolver; `TerrainSpriteSet` moves to
  Presentation with the per-type-id table; `TerrainDefinition.LinkGroup`; the mask rule moves to
  Presentation and `StructureLinks` goes; `MaximumFootprintTiles` constant. The terrain image (its black
  off-map fill from the buffer's on-map flag) and `MapWallPlan` read the buffer; `MapDrawOrder`, `OverhangFade`, `EntityIconElement`, `ActionLockContent`
  and `InspectionWindowContent` resolve through the resolver. `FakeMapView` implements `CopyCells` over its
  own arrays.
- Delete `Game/Sprites`. Game no longer knows a manifest exists.
- Tests: `MapViewQueryTests` assert names and seeds instead of rectangles, and `CopyCells` across a
  neighborhood border, a bounded map's edge, an unloaded neighborhood (off map) and a layer with no floor;
  `TerrainSpriteSet` and entity-variant tests move to Presentation (the entity hash picks the same cell it
  did); the Presentation mask planner gives today's masks for runs, corners, lone pillars, mixed link
  groups and walls running off the map; `MapWallPlanTests` over a buffer; `MaximumFootprintTiles` equals
  reach + 1; an architecture test that `Game` doesn't reference `SpriteFormat`.
- Measure again: the rebuild line against the before figure (the change this phase exists for), and the
  entity path, which does one dictionary lookup per visible entity where it did two. Report both either way.
- As built:
  - `CopyCells(minX, minY, width, height, mapLayer, cells)` (ints, not a `MapBounds`), filling a
    `Game.World.MapCellBuffer` -- in World, beside the map it reads, not in Views.
  - Presentation's side is `MapTerrainCells` (Presentation.UI): it reads the buffer and holds the per-type-id
    looks, the floor/face/top answers and the join masks; `MapWallPlan.Plan` takes it in place of the view.
    Resolved pictures are `MapVisual` (Presentation.Rendering), which `MapOverhang` carries.
  - `TerrainView` carries a `TerrainVisualView` (sprite name, the cell's seed, glyph) rather than a type id,
    so the inspector's icon resolves it through the manifest with no registry.
  - The rebuild could not be measured in a benchmark run (the benchmark's player never moves the camera, so
    the image never rebuilds inside the range). It was measured with a temporary timing test of one rebuild's
    reads, over a seeded 256x160 grass/dirt/wall map, before and after: Release 0.62 -> 0.34 ms at 48x28 and
    3.68 -> 2.10 ms at 180x100; Debug 0.95 -> 0.59 and 10.7 -> 6.0 ms. Both did identical work (the same
    checksum of floors and wall pieces). The "Terrain cache rebuild" frame-cost line stays, for the F3 window.
- **In-game:** the player, goblins and every creature look exactly as before phase 5 on the same seed;
  multi-tile entities, tall sprites and the reach overscan; walls join, tops sit on faces, overhangs fade
  over the player and the inspected entity; corpses, Tiny and Phasing occupants; the charging badge; the
  inspection window's terrain and structure; Admin "Spawn here >" and "Apply >"; a skeleton in the Borough
  draws from its blueprint.

### Phase 6 -- missing sprites warn; docs
- `ContentSpriteNames` view and the post-build check.
- Tests: a blueprint naming an absent sprite warns once and draws its glyph; a built-in set warns nothing.
- As built: the built-in set does warn -- four actions name art that doesn't exist yet (QuickAttack,
  PowerAttack, Dodge, Wand, which already drew their glyphs). The test pins exactly those four, so a new gap
  fails it and giving one art means removing it from the list. The check runs in `GameLoop` after the
  Presentation bootstrap (`MissingSpriteCheck.Report`).
- Docs: CLAUDE.md (the layer list gains `SpriteFormat` and its rules; "Map drawing" and the Blueprints
  spawnable rule name the resolver; joining is a group on the definition and a mask in Presentation; the
  terrain image reads `CopyCells`), IMPLEMENTATION-NOTES "Tall sprites, wall faces and tops, and the sprite
  manifest" (where the manifest lives, variants as seeds, the area read and the measured rebuild cost, link
  groups on the definition and masks in Presentation, the authoring-vs-runtime rule), the epic's "Target
  layering" table (link masks move from Game to Presentation), IMPLEMENTATION-NOTES on backgrounds (a
  terrain's sprite is its background; no colour wash, no entity background), the epic's child 1 marked
  landed, and TODO.md's "Content-only mods" pointing at the resolver as the merge point.
- **In-game:** temporarily point a blueprint's sprite at a missing name -- one warning at startup, the
  creature draws its glyph, nothing else changes; revert.

## Tests (summary)

- `SpriteFormat`: format round trip, every load rule and message, transforms, mask mirrors/turns, placement
  in tiles, no solution or FNA references.
- Game: cell seeds stable and store-salted, no variant storage, `CopyCells` across borders, edges,
  unloaded neighborhoods and floorless layers, link groups from definitions, `MaximumFootprintTiles`, no
  reference to `SpriteFormat`.
- Presentation: resolver first/entity/terrain lookups, `TerrainSpriteSet` seed reduction with and without
  chances, join masks from the buffer, `MapWallPlan` over a buffer, Presentation's sprite names present in
  the manifest, off-map tiles black in the terrain image, missing-sprite warning.
- Benchmarks re-recorded after phase 2; the map draw measured before and after phase 3, and the
  terrain-image rebuild before and after phase 5.
