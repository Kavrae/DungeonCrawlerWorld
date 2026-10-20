# Map areas and the Map tab (read-only)

(Child 6 of `EPIC-sprites-ui-engine-and-dev-tools.md`. Planned 2026-10-18; decisions settled in review 2026-10-18.
**Scoped to fixed areas and viewing:** this plan builds the area format, places areas at fixed spots in the world
(the first being a 32x32 starting area for testing content), and adds a read-only Map tab to Cadence Workshop. Moved
to TODO.md: editing ("Workshop: edit map areas", Engine, High), area pools ("Area pools", Game, Medium), nested areas
("Nested areas", Game, Medium), entry points ("Area entry points", Game, Medium) and areas that follow the player
("Areas that follow the player", Game, Medium). Mods' area files are the data-driven plan's Phase 6. Until the
editor lands, areas are authored as text files, which the format is designed for.)

## Goal

Named **map areas** of any size -- terrain, walls, generic entities, unique named entities, markers -- stored as
content and stamped into the world at fixed places. The first is a **32x32 starting area for testing content**,
cross-shaped hallways included, in the player's spawn neighborhood; the rest of that neighborhood stays generated, for
testing random neighborhood generation later. The starting area is TEMPORARY in one sense: it is later replaced by a
pool of starting areas ("Area pools").

The Workshop's **Map tab** shows every area exactly as the game will build it, where it is placed, and what is wrong
with it, and previews the spawn neighborhood with the starting area stamped in.

The format and stamp are built so that the follow-ups need no change to them: pools pick areas by Guid and tags,
nested areas stamp an area into an area, entry points are markers, and an area that follows the player is an
**instance** of a definition with its own identity (see "Area instances").

## Context

What exists today:
- **Everything hand-placed is C# in `TestMapBuilder`**: `IsHallwayWall` (wall columns 10 and 16 and rows 10 and 16 of
  neighborhood (0,0)), `IsHolyGround` (one cell), `BuildFixtureEntities` (13 fixtures and 15 Tiny goblins at offsets
  from the neighborhood's origin, each seed drawn from the population sequence after the population rows), and the
  player spawn constants `SpawnColumn`/`SpawnRow` read by `FloorBuilder`. Nothing else in the world is authored.
- **Generation is already data-first.** `TestMapBuilder.Plan(map, record, populationSeed)` fills a detached
  `NeighborhoodLayout` (`Map.CreateLayout`; terrain per `(x, y, TerrainLayer)`, structures per `(x, y, MapLayer)`,
  both `TerrainCell` type ids) and a spawn list of `SpawnRequest`s in batches (`NeighborhoodPlan.SpawnRowEnds`) --
  touching no pool, map or event, so it runs on a worker. The layout comes from the record's seed (the same on every
  visit), the population from a per-visit seed. Shrines are rolled from the layout seed with their own salt, so they
  are "part of the place", and spawned first and together so their ids are consecutive (pool paging).
- **Terrain is named by a string key** (`TerrainDefinition.Key`, `core:stone-wall`), registered in C#
  (`BuiltInTerrain`), resolved to a session-local `ushort` by `TerrainRegistry.GetId`. **Blueprints are named by Guid**
  (`BlueprintRegistry.GetId(Guid)`), C# only. A footprint's position is its top-left tile
  (`World`: `CubeInt(position, size)`), which is what `SpawnRequest`'s x/y means.
- **Content as data exists for actions, items and auras** (`PLAN-data-driven-actions-and-items.md`, Phases 1-2):
  `Engine.Content` (`ContentSchema`/`ContentField`, `ContentCodec<T>`, `ContentWriter`, `ContentObject`,
  `ContentTable`, `ContentGuid`, `IContentReferences`), `Game.Content.MasterContent` (`GameModuleContext.Content`), JSON
  Lines tables in `Content/Definitions/Master/*.jsonl` with `off`/`rep` envelope keys, loaded per table on first ask.
- **The Workshop** (`PLAN-ide-shell-and-actions-items-tab.md`, landed view-only): `WorkshopHost`/`WorkshopShell`,
  `IIdeTab`, `RecordBrowserTab<TRecord, TPreview>`, the Related window, `TabNavigator`, `ContentReferenceIndex`/
  `ContentLinks`, the bottom panel (Error List, Dependents), `ContentSession` (a never-simulated `GameSession` over
  `GameBuildPass.CreatePlaceholderMap()`), and the UIEngine controls (`Dropdown`, `ListView`, `SplitPane`, ...).
- **The map renderer is reusable** (child 3): `MapRenderer` draws from `IMapViewQuery`, `AuraPowerView`, a `MapCamera`
  and the manifest, never the World or the store. A `Map(Vector3Int size)` builds a small bounded map, and a
  `GameBuildPass` over one gives a real `IMapViewQuery` (`MapViewQuery`), skeleton drawing from blueprint appearance
  included.

## Research: tile map builders

**Tiled** (general-purpose, editor-first). A map is a stack of **layers**: tile layers (a grid of tile ids, saved as
CSV or base64, in chunks for infinite maps) and **object layers** (free-placed objects with a type and typed **custom
properties** -- string, int, float, bool, colour, file, object reference). **Object templates** are saved objects that
instances reference and override. **World files** position many maps in one coordinate space. **Wang sets** autotile
by corner/edge, and **Automapping** rewrites a map from rule maps. What carries over: layers per kind of content;
entities as placed objects with typed fields, separate from the tile grid; templates are what blueprints already are
here; a world file is the fixed-placement table.

**LDtk** (level designer, editor-first). A **world** of levels (free, grid-vania, or linear layout). Layers are
**IntGrid** (a grid of small integer values with meaning, e.g. wall/floor), **auto-layers** (tiles painted from IntGrid
by rules), tiles and **entities**. An entity **definition** declares typed **fields** with constraints and display modes
and a **max count** (capping `PlayerStart` at one moves the existing one). Every instance has a stable string **IID**,
and **entity reference** fields point at other instances by IID (a button names its door). IntGrid values must not be
read by array position, since users reorder them. What carries over: meaning-first cells (a cell says "stone wall",
not "tile 47"); entity placements with stable ids, so later content (quests, relationships, a lever and its door) can
name a placement; uniqueness as a declared constraint; never identify by order.

**Dungeon Crawl Stone Soup vaults** (roguelike, text-first, `.des` files). A vault is an ASCII **map** with built-in
glyphs (`x` wall, `.` floor, `+` door, `@` entry point on the edge, `0`/`1-7` monster slots, `d-k` item slots).
`KFEAT`/`KMONS`/`KITEM` give a glyph a meaning (a feature, a monster with fallbacks -- e.g. when a unique has already
been generated), `MONS`/`ITEM` fill the numbered slots with weighted choices. `SUBST`/`NSUBST`/`SHUFFLE` randomise
glyphs before placement. **Selection:** `DEPTH` filters where it may appear (a vault with none is never picked at
random), `CHANCE` is rolled per level, `WEIGHT` picks among the rest (default 10). **Placement:** `ORIENT` -- `float`
(anywhere), `encompass` (the whole level), `centre` or a compass edge; none makes it a minivault. `PLACE` pins a vault
to a level. **Uniqueness tags:** `uniq_` once per game, `luniq_` once per level, `buniq_` once per branch, `allow_dup`.
**Subvaults** fill a glyph's bounding box with another vault picked by tag. Tags such as `no_monster_gen` stop random
population inside it. What carries over: text that a person (or Claude) can write and read in a diff; legend-mapped
symbols; filter → chance → weight selection; placement rules; uniqueness for areas and for monsters with a fallback;
an area suppressing random population.

**Cataclysm: Dark Days Ahead JSON mapgen** (roguelike, JSON + text rows). A mapgen record has **`rows`** (strings, one
character per tile) mapped through `terrain`/`furniture` objects, `fill_ter` for unmapped characters, and shared
**palettes** (later palettes override earlier, the record overrides all). `place_monster`/`place_monsters` spawn a
monster or a group at x/y with `chance`, `repeat`, `pack_size`. **Nested chunks** (`place_nested`, `nested`) drop
smaller maps in, conditioned on neighbours. Several mapgens for one overmap terrain are picked by **`weight`**
(default 1000, 0 disables). **`rotation`** turns the finished map in 90° steps. **Parameters** pick a value once per
special and switch other fields on it (one colour scheme for a whole building). Overmap **specials** place multi-tile
sets in the world. What carries over: rows plus a legend, in JSON; palettes shared across areas; weights; rotation;
nesting as a later step.

**Caves of Qud.** Hand-made zones are authored in Qud's own map editor and saved as map files, while most of the world
is generated; a zone's builders run in order, so a hand-made map can be the whole zone or one step among procedural
ones. What carries over: authored and generated are steps in one pipeline, not separate worlds.

**Godot, Unity, Unreal.** Godot's `TileMapLayer` (one layer per node), **terrain sets** for autotiling and **scene
tiles** (a whole scene placed as a tile); Unity's Tilemap with Rule Tiles and **prefab/GameObject brushes**; Unreal's
**Level Instances / Packed Level Actors** (an authored chunk saved once and placed many times; already cited in TODO.md's
"Real map generation"). Bevy's scenes are the same idea as data. What carries over: entities are placed by reference
to their template (a blueprint), never copied; an authored chunk is placed as a unit.

**Spelunky and Brogue.** Spelunky composes a level from room templates chosen by role (the path's rooms vs side rooms);
Brogue's "machines" are blueprints of features placed with requirements. Both are the generator's half; they confirm
the area's job is to be a well-described piece, with tags the generator can ask for.

### What the research decides

1. **Text first, editor later.** Editor-first formats (Tiled's base64 layers, LDtk's arrays) are unreadable by hand,
   and there is no editor yet. The roguelike formats (DCSS, CDDA) are **rows of symbols plus a legend**, which a person
   or Claude writes and a diff shows. The edit TODO writes the same format.
2. **Cells mean things.** A legend entry says which terrain, which structure, which entities -- by key and Guid, never
   by position in a list.
3. **Entities are placements of blueprints** with a stable id per placement, never copies.
4. **Selection is filter → chance → weight; placement is a rule.**
5. **Uniqueness is declared** -- on an area (once per world) and on a placement (one living at a time, with a
   fallback).
6. **Authored and generated are one pipeline.** An area is stamped into a neighborhood's plan between the generated
   layout and the population, on the worker, as data.

This plan takes 1-3 and 6 now, and the parts of 5 that concern entities. Selection and area uniqueness (4, and 5 for
areas) are "Area pools".

## Design

### The area (Game)

`AreaDefinition` (Game.Areas): `Id` (Guid), `Name`, `Description`, `Tags` (`GameplayTagSet`; declared tags, `Area.*`),
`Width`, `Height`, `Layers` (per `MapLayer`: rows, see below), `Legend` (symbol → `AreaCell`), `Palettes` (Guids,
applied in order, the area's own legend last), `Entities` (explicit placements) and `Markers`.

- **Rows per layer.** `Ground` is required; `UnderGround` and `Flying` are optional and, when absent, leave what
  generation put there. Each layer's rows are `Height` strings of `Width` symbols. Coordinates are x right, y down from
  the area's top-left, as the map's. Nothing in a definition names a world position: where it goes is its
  placement's, and what happened to it is its instance's.
- **`AreaCell`** (a legend entry): `Terrain` (a terrain key for that layer's floor; not allowed on `Flying`, which has
  none), `Structure` (a terrain key for a wall standing on that layer, or `Clear` to remove a generated one), and
  `Spawns` (zero or more `AreaSpawn`s). Any part left out keeps what generation put there. **Space keeps everything**
  (the area is transparent there -- a round room in a square box); **every other symbol must be defined**, so nothing
  is implicit.
- **`AreaSpawn`**: a blueprint Guid, optional size, optional `Chance` (percent, default 100) and optional weighted
  `OneOf` (a list of blueprint Guids with weights, DCSS's `MONS` slots / CDDA's groups). A legend symbol is for the
  generic, repeated ones (`g` = a Goblin).
- **`AreaEntityPlacement`** (explicit, LDtk-style): `PlacementId` (a short string unique in the area -- the IID), x, y,
  `Layer`, blueprint Guid, optional size, `Chance`, `Unique` and `Fallback` (below), and an optional `Description`
  (authoring notes, the fixtures' comments today). For anything named, large, stacked on one cell (11 Tiny goblins) or
  that later content must point at.
- **`AreaMarker`**: `MarkerId`, x, y, `Layer`, `Kind` -- `PlayerSpawn` today; entry points are markers too ("Area entry
  points"). The starting area's marker replaces `SpawnColumn`/`SpawnRow`.
- **Footprints** follow `SpawnRequest`: x, y is the top-left tile. Validation refuses a footprint outside the area.
- **Unique named entities (confirmed 2026-10-18).** A unique entity is a blueprint with its own explicit `Name` (a
  composite) placed with `Unique = true`. **One of it lives at a time in the session, and once killed it never comes
  back this session** (`UniqueEntityRoster` in Game, keyed by the blueprint Guid; saved with the save system later). If
  it is alive elsewhere or dead, the placement spawns its `Fallback` blueprint, if any, else nothing (DCSS's `KMONS`
  fallback). Names come from blueprints only: a per-placement name would not rebuild from the spawn record, so a new
  unique NPC is a C# composite until blueprints are data. A unique's seed is fixed (from the area's Guid and the
  placement id), so it is the same individual wherever it appears.
- **Generic entities** draw their seed from the **instance's** seed (for a fixed placement, the neighborhood's layout
  seed mixed with the placement's Guid) mixed with the placement id or the cell, so an area's residents look the same on
  every visit -- the shrines' rule: part of the place. `Chance` and `OneOf` roll from the same seed.
- **No area rules yet.** Rules such as a boss room refusing teleportation in or out, or a safe room punishing an
  attacker before the attack lands, come later; whether they are hard-coded behaviours keyed by tag or authored in the
  map builder is undecided. The format reserves nothing for them; tags (`Area.Boss`, `Area.Safe`) are the likely hook.
  The only behaviour this plan gives an area is that random population doesn't spawn inside the cells it covers.

### Palettes and fixed placements (Game)

- **`AreaPalette`**: `Id`, `Name`, `Legend`. Shared symbol meanings (`#` stone wall on stone floor, `+` a door when
  doors exist). CDDA's rule: later palettes override earlier, the area's own legend overrides all.
- **`AreaPlacement`** (fixed): `Id`, area Guid, neighborhood cell (x, y), offset in it. The starting area is the first:
  neighborhood (0,0), offset (0,0), covering x 0-31, y 0-31.

### Area instances

Every stamp of an area is an **instance**: `AreaInstanceKey` (what the instance is -- for a fixed placement, the
placement's Guid), the definition it came from, its seed, and where it sits. Generation and the Workshop both go
through instances, never through a definition directly. Today every instance is a fixed placement and its key is
redundant; it exists so that **areas that follow the player** need no change to the format or the stamp:
- *What it is:* the first time the player enters a Training Guild building, its interior is picked at random from the
  Training Guild pool; every Training Guild building entered after that leads to **that same guild**. To the player the
  door teleports them to one place; mechanically every entrance reuses one instance's map data, so an item dropped in
  it is there when they come in through another door.
- *What this plan fixes for it:* the instance key is separate from the definition and from any world position; the
  instance's seed (not the neighborhood's) seeds its residents; the stamp writes into any `NeighborhoodLayout`, not only
  one at the area's world position; placement and marker ids are area-local, so an instance's entities and entry
  points are addressable wherever it is stamped.
- *What it needs later* (TODO.md "Areas that follow the player"): the binding from a key like "Training Guild" to the
  instance picked (per session, then saved); where an instance lives (its own map, as floors are separate maps, or a
  reserved pocket region); doors that move the player into it and back out to the door they used (entry points); and
  persistence of what's inside across unloads, which today's streamer doesn't do (it destroys a neighborhood's
  entities and regenerates its population), tied to "Save and load Beyond neighborhoods" (TODO.md, Global).

### Storage

Three new tables in `Content/Definitions/Master`, read through codecs like actions and items:
- `AreaPalettes.jsonl` and `AreaPlacements.jsonl` -- JSON Lines, one record per line, as today.
- **`Areas/` -- one area per file** (`Areas/starting-area.json`), indented, with **one row per line**. Illustrative
  keys; Phase 1 settles them:

```
{
  "fmt": 1, "table": "Areas",
  "id": "…", "n": "Starting area", "tg": ["Area.Testing"],
  "w": 32, "h": 32,
  "pal": ["…stone palette…"],
  "leg": { "H": { "ter": "core:holy-ground" } },
  "lay": {
    "Ground": [
      "          #     #               ",
      "          #     #               ",
      "…"
    ]
  },
  "ent": [ { "pid": "foreman", "x": 13, "y": 5, "bp": "…GoblinForeman…" } ],
  "mk":  [ { "mid": "spawn", "x": 17, "y": 13, "k": "PlayerSpawn" } ]
}
```

A one-line JSONL record holding a 32-row grid would be unreadable and every edit would be a one-line diff of the whole
grid; a file per area keeps rows readable and diffs per row. It is still one record through a codec with an enforced
schema, so the Workshop's document/inspector model and `off`/`rep` apply unchanged. The canonical writer writes rows
one per line, so a hand-written file and a saved one look the same.

### The starting area

`Areas/starting-area.json`, 32x32 at the origin of neighborhood (0,0), tagged `Area.Testing`:
- **The hallway cross**, as today's `IsHallwayWall` clipped to the box: wall columns 10 and 16 where y < 10 or
  y > 16, wall rows 10 and 16 where x < 10 or x > 16, open where they meet, on stone floor. Today the arms run the whole 1024-tile neighborhood; outside the box
  they go, and that ground is generated like any other.
- **Holy Ground** at (14,13), **the fixtures** as explicit placements at today's offsets (the long-description
  goblin, the Huge engineer, the stationary and moving fairies, the multi-race and multi-class goblins, the 4 and 11
  Tiny goblins, the Phasing fairy, both shops, the Goblin Foreman, the healing shrine), each with its comment as its
  `Description`, and the **`PlayerSpawn` marker** at (17,13).
- Every other cell inside the box is defined (stone or generated-style floor, chosen when it's written), so the box
  is fully authored and no random creature spawns inside it.
- The rest of neighborhood (0,0) is generated as every other neighborhood is, for testing random neighborhood
  generation later.

### Stamping (Game)

`AreaStamp.Apply(NeighborhoodLayout layout, AreaInstance instance, AreaStampResult result)`: pure, worker-safe, no
pool, map or event. It resolves the legend (palettes, then the area's), writes terrain and structures into the layout
cell by cell (space and left-out parts keep what's there), rolls `Chance`/`OneOf` from the instance seed, and adds
`SpawnRequest`s for symbol spawns and explicit placements, uniques included (checked against a **snapshot** of the
unique roster taken when planning starts; the main thread re-checks at spawn, since a unique may have been placed
meanwhile). It records the cells it covers (for population suppression and the Workshop's outline) and its markers.

`TestMapBuilder.Plan` becomes: generate the layout → **stamp the neighborhood's fixed placements** → aura rows →
shrines and population, skipping cells an area covers → **area spawns as their own batch, after shrines and before
the population** (spawn like-with-like; an area's entities are placed before creatures that might take their cells).
The starting area replaces `IsHallwayWall`, `IsHolyGround`, `BuildFixtureEntities` and the spawn constants;
`FloorBuilder` reads the spawn from the marker.

### Validation (Game, at build)

`AreaContentValidation`, after every `Configure` beside `ContentTagValidation`: rows match `Width`/`Height`; every
symbol is defined (space aside); terrain keys and blueprint Guids exist; no floor on `Flying`; footprints inside the
area and blocking footprints not overlapping each other or a wall; placement and marker ids unique in the area; tags
declared; a fixed placement fits its neighborhood and two fixed placements don't overlap; exactly one `PlayerSpawn`
marker across the fixed placements. A failing area fails the build like any content (`ContentFailure`: file, line,
record id, reason -- line is the row's line in the area file).

### The Map tab (Builders, read-only)

The same three-window layout as the other tabs (`RecordBrowserTab`):
- **Browser:** areas grouped as Placed (in a fixed placement) and Unplaced; palettes and placements in sections of
  their own, so selecting one shows its record; search by name and tag; each area with its size.
- **Preview: the area as the game builds it.** A **preview build** per selected area: a `GameBuildPass` over a bounded
  `Map` the area's size (a plain generated fill underneath, so transparent cells show something), an instance stamped
  by `AreaStamp` exactly as generation does, its spawns spawned through the build's `EntityFactory`, never simulated.
  The preview window hosts a `MapRenderer` over that build's `MapViewQuery` with its own `MapCamera`: **layer switch**
  (UnderGround/Ground/Flying), zoom levels and pan, a grid overlay, and a **hover readout**: cell coordinates, symbol,
  terrain, structure, entity names, placement and marker ids. Transparent cells are hatched; markers are drawn as
  labelled badges. A **Reroll** button changes only the preview instance's seed, to see `Chance`/`OneOf` outcomes.
  Builds are cached per area and rebuilt when the session is.
- **Neighborhood preview:** a seed and a neighborhood cell, then `TestMapBuilder.Plan` exactly as the game runs it,
  applied to a bounded one-neighborhood preview build and drawn with the same renderer, with every stamped instance
  outlined and named on hover. For the spawn neighborhood: the starting area at its corner and generated ground around
  it.
- **Related:** an area uses terrain, blueprints and palettes; it is used by placements. Links open the other tabs (a
  blueprint has no tab yet, so it's a plain line). `ContentReferenceIndex` gains these edges.
- **Error List:** load and validation failures for areas, palettes and placements, tied to the record and, for areas,
  the row and column.

## Decisions

Settled in review 2026-10-18:
1. **Areas are rows of symbols plus a legend**, with explicit entity placements and markers beside them (DCSS/CDDA for
   cells, LDtk for entities).
2. **One area per file, rows one per line**; palettes and fixed placements as JSON Lines tables.
3. **Space keeps what generation made; every other symbol must be defined.** Left-out parts of a legend entry keep too.
4. **Rows per `MapLayer`**, `Ground` required; `Flying` has no floor.
5. **Residents are part of the place**: seeded from the instance's seed, so the same each visit.
6. **Unique entities (confirmed):** a named blueprint placed `Unique`; one alive per session, never back once killed
   this session, a fallback otherwise; names only from blueprints.
7. **Stamping is pure and runs inside `Plan`**, between the generated layout and the population; area spawns are a
   batch of their own; random population doesn't spawn inside an area.
8. **The starting area is 32x32**, for testing content, with the hallway cross clipped to it; the rest of the spawn
   neighborhood is generated, for testing random generation later. It is later replaced by a pool of starting areas.
   The seeded world changes once (the arms are shorter, the box has no random population, and fixtures take their
   seeds by Decision 5 instead of from the population sequence).
9. **Every stamp is an instance** with its own key and seed, separate from the definition and from world position, so
   areas that follow the player reuse the format and stamp unchanged.
10. **No area rules in this plan**; whether rules are hard-coded or authored is undecided.
11. **Pools, nested areas, entry points and following areas are TODO.md items**; mods' area files are the
    data-driven plan's Phase 6.
12. **The Map tab previews through real builds** -- the game's own stamp, factory and renderer -- never a fake map
    view, so what it shows is what the game builds.
13. **When the feature is complete, a new performance baseline across all metrics** is recorded.

## Phases

Each phase ends with the game (and, from Phase 3, the Workshop) running and a stop for testing before the next.

### Phase 1 -- The area format (Game)

- `AreaDefinition`, `AreaCell`, `AreaSpawn`, `AreaEntityPlacement`, `AreaMarker`, `AreaPalette`, `AreaPlacement`,
  `AreaInstance`/`AreaInstanceKey`; their codecs (`AreaContentCodecs`) with enforced schemas; the three tables in
  `MasterContent` (the area folder reader beside `ContentTable`); `Area.*` tags declared (`Area.Testing` first).
- `AreaContentValidation` at build.
- Tests: round trip every record to identical bytes (rows one per line), each validation failure with its row and
  column, palette override order, an undefined symbol, transparent cells.
- No area is shipped yet and nothing uses them: the game is unchanged.

### Phase 2 -- Fixed placements and the starting area (Game)

- `AreaStamp`, `UniqueEntityRoster`, fixed placements in `Plan`; population suppression; the area spawn batch.
- `Areas/starting-area.json` (see "The starting area") and its placement; `FloorBuilder` reads the spawn marker.
  `IsHallwayWall`, `IsHolyGround`, `BuildFixtureEntities` and the spawn constants go.
- Tests: the stamp writes exactly the cells it should (transparent cells untouched); every fixture spawns with the
  blueprint, cell and size it had before (the old `BuildFixtureEntities` requests captured once as expected values)
  and with the same seed on every visit; a unique is placed once and its fallback after; footprints and markers through
  a stamp; `SpawnRecordRebuilderTests` still pass.
- Verify: in game, the starting area looks and behaves as before inside the box; the arms stop at its edge; generated
  ground and creatures fill the rest of the neighborhood.

### Phase 3 -- The Map tab (Builders)

- The tab, its browser and sections, the per-area preview build with renderer, layer switch, zoom/pan, grid, hover
  readout, transparent hatching, markers and Reroll; Related, Dependents and the Error List.
- `ContentReferenceIndex` edges for areas, palettes and placements.
- Tests (Workshop.Tests): the preview build of every shipped area matches a stamp into a plain layout cell for cell;
  browser sections; reference edges.
- Verify: the starting area in the tab matches the game's; a broken area (an undefined symbol, written by hand) shows
  in the Error List at its row and column.

### Phase 4 -- Neighborhood preview, and the new baseline (Builders)

- Seed and neighborhood fields, the one-neighborhood preview build through `Plan`, outlines and hover names for
  stamped instances.
- Verify: the preview of the seeded spawn neighborhood matches what the game generates there.
- **New performance baseline across all metrics**, through the phase-performance-testing skill: seeded headless and
  windowed runs with `--diagnostics=all` (frame, memory, startup, leak, gauges), saved as the new baseline, with the
  change against the last baseline reported.

## Open questions

- None.

## Out of scope / follow-ups

- **Editing** -- TODO.md "Workshop: edit map areas" (Engine, High).
- **Area pools** -- TODO.md "Area pools" (Game, Medium): many areas per pool, an area in many pools; the pool of
  starting areas replaces the 32x32 starting area.
- **Nested areas** -- TODO.md "Nested areas" (Game, Medium).
- **Entry points** -- TODO.md "Area entry points" (Game, Medium).
- **Areas that follow the player** -- TODO.md "Areas that follow the player" (Game, Medium).
- **Mods' area files** -- `PLAN-data-driven-actions-and-items.md`, Phase 6.
- **Area rules** (boss rooms refusing teleportation, safe rooms punishing attackers before the attack lands) --
  hard-coded or authored, undecided.
- Items on the ground (TODO.md "Dropping items on the ground") and container contents per placement.
- Composite buildings as area content (TODO.md "Composite buildings").
- Blueprints and terrain as data, so a unique NPC or a new wall is authored without C#.
- Saving the unique roster with the save system.
