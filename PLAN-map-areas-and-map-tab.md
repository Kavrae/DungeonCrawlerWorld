# Map areas and the Map tab (read-only)

(Child 6 of `EPIC-sprites-ui-engine-and-dev-tools.md`. Planned 2026-10-18; decisions proposed, not yet confirmed --
see "Decisions" and "Open questions". **Scoped to viewing:** this plan builds the area format, its use in map
generation (fixed placements and random pools), and a read-only Map tab in Cadence Workshop. Editing areas in the
Workshop is TODO.md's "Workshop: edit map areas" (Engine, High). Until then areas are authored as text files, which
the format is designed for.)

## Goal

Named **map areas** of any size -- terrain, walls, generic entities, unique named entities, markers -- stored as
content, and used two ways:
- **Fixed:** an area always at one place in the world (today's starting area: the hallway cross, Holy Ground, the
  fixtures and shops of neighborhood (0,0)).
- **Random:** areas offered as options for generation to pick from (a pre-made boss room in the middle of an otherwise
  random neighborhood).

The Workshop's **Map tab** shows every area exactly as the game will build it, which pools and placements use it, and
what is wrong with it, and previews a generated neighborhood with its areas stamped in.

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

## Design

### The area (Game)

`AreaDefinition` (Game.Areas): `Id` (Guid), `Name`, `Description`, `Tags` (`GameplayTagSet`; declared tags, `Area.*`),
`Width`, `Height`, `Layers` (per `MapLayer`: rows, see below), `Legend` (symbol → `AreaCell`), `Palettes` (Guids,
applied in order, the area's own legend last), `Entities` (explicit placements), `Markers`, `Rules`
(`SuppressesPopulation`, default true; `AllowedTransforms`, default none), and `Uniqueness` (`None`, `OncePerWorld`).

- **Rows per layer.** `Ground` is required; `UnderGround` and `Flying` are optional and, when absent, leave what
  generation put there. Each layer's rows are `Height` strings of `Width` symbols. Coordinates are x right, y down from
  the area's top-left, as the map's.
- **`AreaCell`** (a legend entry): `Terrain` (a terrain key for that layer's floor; not allowed on `Flying`, which has
  none), `Structure` (a terrain key for a wall standing on that layer, or `Clear` to remove a generated one), and
  `Spawns` (zero or more `AreaSpawn`s). Any part left out keeps what generation put there. Two built-in symbols:
  **space keeps everything** (the area is transparent there -- a round room in a square box), **`.` is an error unless
  defined**, so nothing is implicit.
- **`AreaSpawn`**: a blueprint Guid, optional size, optional `Chance` (percent, default 100) and optional weighted
  `OneOf` (a list of blueprint Guids with weights, DCSS's `MONS` slots / CDDA's groups). A legend symbol is for the
  generic, repeated ones (`g` = a Goblin).
- **`AreaEntityPlacement`** (explicit, LDtk-style): `PlacementId` (a short string unique in the area -- the IID), x, y,
  `Layer`, blueprint Guid, optional size, `Chance`, and `Unique` (below). For anything named, large, stacked on one
  cell (11 Tiny goblins) or that later content must point at.
- **`AreaMarker`**: `MarkerId`, x, y, `Layer`, `Kind` (`PlayerSpawn` today; quest hooks and entry points later). The
  starting area's marker replaces `SpawnColumn`/`SpawnRow`.
- **Footprints** follow `SpawnRequest`: x, y is the top-left tile. Validation refuses a footprint outside the area.
- **Unique named entities.** A unique entity is a blueprint with its own explicit `Name` (a composite) placed with
  `Unique = true`. **One of it lives at a time in the session, and once killed it never comes back this session**
  (`UniqueEntityRoster` in Game, keyed by placement's blueprint Guid; saved with the save system later). If it is
  alive elsewhere or dead, the placement spawns its `Fallback` blueprint, if any, else nothing (DCSS's `KMONS`
  fallback). A unique's seed is fixed (from the area's Guid and placement id), so it is the same individual wherever it
  appears.
- **Generic entities** draw their seed from the neighborhood's **layout** seed mixed with the area's Guid and the
  placement id, so an area's residents look the same on every visit (shrines' rule: part of the place). `Chance` and
  `OneOf` roll from the same seed, so they are fixed per place too. An area with `Rules.RerollsEachVisit` rolls from the
  population seed instead (a monster den that restocks).

### Palettes, pools and fixed placements (Game)

- **`AreaPalette`**: `Id`, `Name`, `Legend`. Shared symbol meanings (`#` stone wall on stone floor, `+` a door when
  doors exist). CDDA's rule: later palettes override earlier, the area's own legend overrides all.
- **`AreaPool`**: `Id`, `Name`, `Tags`, `Members` (area Guid + `Weight`, default 10), `ChancePerNeighborhood`
  (percent), `Placement` (`Centre`, `Anywhere` -- the first fitting position from the layout seed's roll, or
  `Offset(x, y)` from the neighborhood's origin), and `Eligibility` (which neighborhoods: not the starting one, a
  distance band from it; the real generator's templates will ask by tag instead). Selection per neighborhood: filter
  members (an area already used once per world is out), roll `ChancePerNeighborhood`, pick by weight -- all from the
  layout seed, so a neighborhood gets the same area on every visit.
- **`AreaPlacement`** (fixed): `Id`, area Guid, neighborhood cell (x, y), offset in it, optional transform. The
  starting area is the first: neighborhood (0,0), offset (0,0).
- **Transforms** (CDDA `rotation`, DCSS flips): an area may allow 90° turns and mirroring; the stamp turns rows,
  explicit placements, markers and footprints together. Off by default -- a room with a door on its south wall
  shouldn't turn until there is something to join it to.

### Storage

Four new tables in `Content/Definitions/Master`, read through codecs like actions and items:
- `AreaPalettes.jsonl`, `AreaPools.jsonl`, `AreaPlacements.jsonl` -- JSON Lines, one record per line, as today.
- **`Areas/` -- one area per file** (`Areas/starting-area.json`), indented, with **one row per line**. Illustrative
  keys; Phase 1 settles them:

```
{
  "fmt": 1, "table": "Areas",
  "id": "…", "n": "Starting area", "tg": ["Area.Fixed"],
  "w": 30, "h": 24,
  "pal": ["…stone palette…"],
  "leg": { "H": { "ter": "core:holy-ground" } },
  "lay": {
    "Ground": [
      "          #     #             ",
      "          #     #             ",
      "…"
    ]
  },
  "ent": [ { "pid": "foreman", "x": 13, "y": 5, "bp": "…GoblinForeman…" } ],
  "mk":  [ { "mid": "spawn", "x": 17, "y": 13, "k": "PlayerSpawn" } ]
}
```

A one-line JSONL record holding a 64-row grid would be unreadable and every edit would be a one-line diff of the whole
grid; a file per area keeps rows readable and diffs per row. It is still one record through a codec with an enforced
schema, so the Workshop's document/inspector model and `off`/`rep` apply unchanged. The canonical writer writes rows
one per line, so a hand-written file and a saved one look the same.

### Stamping (Game)

`AreaStamp.Apply(NeighborhoodLayout layout, AreaDefinition area, int originX, int originY, transform, seeds, AreaStampResult)`:
pure, worker-safe, no pool, map or event. It resolves the legend (palettes, then the area's), writes terrain and
structures into the layout cell by cell (space and left-out parts keep what's there), rolls `Chance`/`OneOf`, and adds
`SpawnRequest`s for symbol spawns, explicit placements and uniques (checked against a **snapshot** of the unique roster
taken when planning starts; the main thread re-checks at spawn, since a unique may have been placed meanwhile). It
records the cells it covers (for population suppression and the Workshop's outlines) and its markers.

`TestMapBuilder.Plan` becomes: generate the layout → **stamp fixed placements and the pools' picks** → aura rows →
shrines and population, skipping cells an area covers when it suppresses population → **area spawns as their own
batch, after shrines and before the population** (spawn like-with-like; an area's entities are placed before the
creatures that might otherwise take their cells). The starting area replaces `IsHallwayWall`, `IsHolyGround`,
`BuildFixtureEntities` and the spawn constants.

### Validation (Game, at build)

`AreaContentValidation`, after every `Configure` beside `ContentTagValidation`: rows match `Width`/`Height`; every
symbol is defined (space aside); terrain keys and blueprint Guids exist; no floor on `Flying`; footprints inside the
area and blocking footprints not overlapping each other or a wall; placement and marker ids unique in the area; tags
declared; a pool's members exist and fit the neighborhood; a fixed placement fits its neighborhood and two fixed
placements don't overlap; at most one `PlayerSpawn` marker in the world's fixed placements. A failing area fails the
build like any content (`ContentFailure`: file, line, record id, reason -- line is the row's line in the area file).

### The Map tab (Builders, read-only)

The same three-window layout as the other tabs (`RecordBrowserTab`), over areas, plus two modes:
- **Browser:** areas grouped as Fixed (in a fixed placement), by pool, and Unused; search by name and tag; each with
  its size. Palettes and pools are listed in their own sections, so selecting one shows its record.
- **Preview: the area as the game builds it.** A **preview build** per selected area: a `GameBuildPass` over a bounded
  `Map` the area's size (one generated-terrain fill underneath, so transparent cells show something), the area stamped
  by `AreaStamp` exactly as generation does, its spawns spawned through the session's `EntityFactory`, never
  simulated. The preview window hosts a `MapRenderer` over that build's `MapViewQuery` with its own `MapCamera`:
  **layer switch** (UnderGround/Ground/Flying), zoom levels and pan, a grid overlay, and a **hover readout**: cell
  coordinates, symbol, terrain, structure, entity names, placement and marker ids. Transparent cells are hatched;
  markers are drawn as labelled badges. A **Reroll** button changes only the preview seed, to see `Chance`/`OneOf`
  outcomes. Builds are cached per area and rebuilt when the session is.
- **Pool preview:** a pool shows its members with weight and resulting odds, and each member's thumbnail.
- **Neighborhood preview:** a seed and a neighborhood cell, then `TestMapBuilder.Plan` exactly as the game runs it,
  applied to a bounded one-neighborhood preview build and drawn with the same renderer, with every stamped area
  outlined and named on hover. This is the check that "a boss room in the middle of a random neighborhood" happens.
- **Related:** an area uses terrain, blueprints and palettes; it is used by pools and fixed placements. Links open
  the other tabs (a blueprint has no tab yet, so it's a plain line). `ContentReferenceIndex` gains these edges.
- **Error List:** load and validation failures for areas, palettes, pools and placements, tied to the record and,
  for areas, the row and column.

## Decisions (proposed)

1. **Areas are rows of symbols plus a legend**, with explicit entity placements and markers beside them (DCSS/CDDA for
   cells, LDtk for entities).
2. **One area per file, rows one per line**; palettes, pools and fixed placements as JSON Lines tables.
3. **Space keeps what generation made; every other symbol must be defined.** Left-out parts of a legend entry keep too.
4. **Rows per `MapLayer`**, `Ground` required; `Flying` has no floor.
5. **Generic residents are part of the place**: seeded from the layout seed, so the same each visit, unless the area
   rerolls each visit.
6. **A unique entity is a named blueprint placed `Unique`**: one alive per session, never back once killed this
   session, a fallback otherwise. Names come from blueprints, never from a per-placement name override (an override
   would not rebuild from the spawn record).
7. **Areas can be unique** (once per world) for pools.
8. **Selection is eligibility → chance → weight, from the layout seed**; placement is `Centre`, `Anywhere` or an
   `Offset`.
9. **Transforms are opt-in per area.**
10. **Stamping is pure and runs inside `Plan`**, between the generated layout and the population; area spawns are a
    batch of their own; areas suppress random population by default.
11. **The starting area becomes the first area**, and the first fixed placement: a box around the cross's centre
    (about 30x24: every fixture, the shops, Holy Ground and the spawn). The cross's arms are trimmed to the box --
    today they run the whole 1024-tile neighborhood, which as an area would be a 1024-row file of spaces, and they are
    test scaffolding. Fixtures take their seeds by Decision 5's rule (the layout seed, the area and the placement id)
    instead of from the population sequence, which population suppression would shift anyway. So the seeded world
    changes once, and benchmark baselines are re-recorded.
12. **The Map tab previews through real builds** -- the session's own stamp, factory and renderer -- never a fake map
    view, so what it shows is what the game builds.
13. **A demo pool** ("Boss rooms": one or two rooms with a `GoblinForeman` boss) placed `Centre` in neighborhoods other
    than the starting one -- the example in the request. This changes every seeded world (benchmark baselines are
    re-recorded) and is marked TEMPORARY until real map generation decides where boss rooms go.

## Phases

Each phase ends with the game (and, from Phase 4, the Workshop) running and a stop for testing before the next.

### Phase 1 -- The area format (Game)

- `AreaDefinition`, `AreaCell`, `AreaSpawn`, `AreaEntityPlacement`, `AreaMarker`, `AreaPalette`, `AreaPool`,
  `AreaPlacement`; their codecs (`AreaContentCodecs`) with enforced schemas; the four tables in `MasterContent`
  (the area folder reader beside `ContentTable`); `Area.*` tags declared.
- `AreaContentValidation` at build.
- Tests: round trip every record to identical bytes (rows one per line), each validation failure with its row and
  column, palette override order, unknown symbol, transparent cells.
- No area is shipped yet and nothing uses them: the game is unchanged.

### Phase 2 -- Fixed placements and the starting area (Game)

- `AreaStamp`, `UniqueEntityRoster`, `AreaPlacements` in `Plan`; population suppression; the area spawn batch.
- `Areas/starting-area.json` holds the hallway cross, Holy Ground, the fixtures (explicit placements, comments kept as
  each placement's description) and the `PlayerSpawn` marker; `FloorBuilder` reads the spawn from the marker.
  `IsHallwayWall`, `IsHolyGround`, `BuildFixtureEntities` and the spawn constants go.
- Tests: the stamp writes exactly the cells it should (transparent cells untouched), a unique is placed once and its
  fallback after, footprints and markers through a stamp; `SpawnRecordRebuilderTests` still pass.
- Tests: every fixture spawns with the blueprint, cell and size it had before (compare against the old
  `BuildFixtureEntities` requests, captured once as expected values), and with the same seed on every visit.
- Verify: in game, the starting area looks and behaves as before inside the box (the arms now stop at its edge);
  benchmark A/B to see planning cost, then re-record baselines.

### Phase 3 -- Random pools (Game)

- Pool selection and placement rules, area uniqueness, transforms, `Rules.RerollsEachVisit`.
- The TEMPORARY "Boss rooms" pool with one or two areas.
- Tests: selection is deterministic per layout seed and the same each visit; chance 0 never and 100 always places;
  weights over many seeds; a once-per-world area appears once; a turned area's placements and footprints turn with
  it; the unique boss is not duplicated when two neighborhoods pick the same room.
- Verify: walk to a neighboring neighborhood's centre and find the boss room; leave and return, same room; benchmark
  A/B (expect a small cost in planning, none per frame) and re-record baselines.

### Phase 4 -- The Map tab: areas, palettes and pools (Builders)

- The tab, its browser and sections, the per-area preview build with renderer, layer switch, zoom/pan, grid, hover
  readout, transparent hatching, markers, Reroll; the pool preview; Related, Dependents and the Error List.
- `ContentReferenceIndex` edges for areas, palettes, pools and placements.
- Tests (Workshop.Tests): the preview build of every shipped area matches a stamp into a plain layout cell for cell;
  browser sections; reference edges.
- Verify: the starting area in the tab matches the game's neighborhood (0,0); the boss room's Reroll changes only
  `Chance`/`OneOf` cells; a broken area (an undefined symbol, written by hand) shows in the Error List at its row.

### Phase 5 -- Neighborhood preview (Builders)

- Seed and neighborhood fields, the one-neighborhood preview build through `Plan`, outlines and hover names for
  stamped areas.
- Verify: the preview of a seeded neighborhood matches what the game generates there; the boss room outline sits at
  the centre.

## Open questions

- **Decision 6, unique entities:** is "one alive at a time, never back once killed this session" the right meaning of
  unique, and are names only from blueprints (so a new unique NPC needs a C# composite until blueprints are data)?
- **Decision 11:** trim the hallway cross's arms to the starting area's box, or keep the full-length arms (they
  would stay C# in `TestMapBuilder` until real map generation replaces them)?
- **Decision 13:** is a TEMPORARY demo pool wanted, or should pools wait for real map generation?
- **Where the area files live** for mods: a mod's areas folder beside its tables (the data-driven plan's Phase 5).

## Out of scope / follow-ups

- **Editing** -- TODO.md "Workshop: edit map areas" (Engine, High).
- Nested areas (DCSS subvaults, CDDA nested chunks) and area parameters (CDDA): a symbol that places another area
  picked by tag.
- Entry points and joining an area to its surroundings (DCSS `@`, CDDA `joins`) -- with "Real map generation --
  neighborhood templates" (TODO.md, Game, High), which will ask pools for areas by tag instead of `Eligibility`.
- Items on the ground (TODO.md "Dropping items on the ground") and container contents per placement.
- Composite buildings as area content (TODO.md "Composite buildings").
- Blueprints and terrain as data, so a unique NPC or a new wall is authored without C#.
- Saving the unique roster with the save system.
