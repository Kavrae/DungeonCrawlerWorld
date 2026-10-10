# Tall Sprites and Two-Tile Walls

(Pre-implementation. Replaces TODO.md's "Sprites taller than one tile, and two-tile walls (front +
top)" entry, and absorbs "Per-entity sprite scale" (Low). Both are deleted when the phase that lands
them is done.)

## Context

What exists today:
- `MapWindow.DrawContent` draws, in order: the flat background, `_terrainCache` (backgrounds,
  terrain, structures), `DrawOccupants`, `_glowCache`, targeting highlights, the selection/followed
  highlight. There is no depth buffer: `SpriteSortMode.Deferred` draws in call order.
- `DrawOccupants` walks column-outer, row-inner. For each tile it draws the underlay (corpses, Tiny
  sub-grid, collecting Phasing), the Blocking occupant (only from its top-left origin tile, across
  its `Size` footprint), the Phasing overlay, then the up/down layer badges.
- A sprite is always stretched to exactly its footprint (`SpriteRenderer.Draw`). The Player cell is
  64x64 with the character small in the middle, the Goblin cell 32x32, terrain 16x16. That is the
  "player needs to render larger, goblins smaller" problem in "Per-entity sprite scale".
- Walls are structure cells (`Map`'s per-`MapLayer` store, `TerrainCell` flyweights from
  `TerrainRegistry`), drawn into `_terrainCache` after the floor, one 1x1 sprite (`"Wall"`).
  `StructureChangedEvent`, `TerrainLoadedEvent`/`TerrainUnloadingEvent` and every camera commit
  invalidate the cache.
- `MapCamera` sizes its grid to the visible area + 2 tiles on each axis, extending right and down
  from `CurrentScrollPosition`.
- `SpriteManifest` entries are a name and a list of cells (sheet, column, row, cell size), mirrored
  by hand in `DevTools/SpriteManifestBuilder/ManifestCell.cs`. `SpriteComponent` is a per-instance
  override; everything else reaches the map through `IMapViewQuery.TryGetVisual` as a `SpriteView`.
- `Wall_Tiles.png` (Pixel Crawler pack) already has separate art for wall tops (the rounded blobs,
  three palettes) and brick fronts.

## Research: how other tile games do it

**RimWorld.**
- *Two draw paths.* Things that never move (walls, floors, plants, furniture) are "printed" into
  chunked section meshes rebuilt only when a section is marked dirty (`DrawerType.MapMeshOnly`).
  Pawns and anything animated draw every frame (`RealtimeOnly`). That is the same split as
  `MapTileLayerCache` vs `DrawOccupants`. The lesson this plan takes: a tall static thing can stay
  in the baked image wherever it only overlaps other static things. Only its overlap with things
  that move needs per-frame work.
- *Draw size is not footprint.* `graphicData.drawSize` is independent of a building's `size` (the
  wiki's example: a 2x2 building drawn 3x3 on a 384 px canvas). Trees draw well above their cell.
  So sprite size belongs to the art, not the footprint. This plan does that with pixels-per-tile
  plus a pivot.
- *Fixed altitude layers.* Things sort by an `AltitudeLayer` (terrain < floor < building < pawn <
  ...), with tiny increments inside a layer. Vanilla walls don't overhang at all: they are flat
  top-down tiles, drawn from a 16-variant linked atlas (`LinkDrawerType.CornerFiller`) keyed by
  which neighbours are also walls, and edge shadows fake the height. Mods such as "Sense of Depth
  for Tall Buildings" add a second, higher-altitude layer holding a building's upper part so pawns
  pass behind it. That is exactly the front/top split here.
- *Linked atlas.* This is where wall tops that join cleanly come from: a 4-bit neighbour mask
  selects the variant, and out-of-bounds cells can count as linked (`MapEdge`). "Joined-up wall
  tops" takes both.

**Stardew Valley.** The map has Back, Buildings, Paths, Front and AlwaysFront layers. Front tiles
(tree canopies, roof edges) are depth-sorted by row against characters: they draw over the player
when the player is north of them and behind the player when the player is south. Vanilla can't make
Front tiles translucent, and the community wrote "Show Players Behind Map Tiles" to show the player
through them anyway. That is the fade this plan builds in from the start.

**3/4-view tile engines generally** (Zelda: ALttP, Godot's Y-sort, gamedev.net's standard answer).
Draw back to front, row by row: each row's tiles, then its objects. An object spanning several rows
draws with its **last (lowest) row**, not its first. Godot sorts by the sprite's visual bottom, and
tall sprites are shifted up so their base sits on the tile origin, which is a pivot. Cliffs and
walls are a stack: base, side face, then a top tile on the row above.

**GPU depth sorting (considered and rejected).** Stardew draws with `layerDepth = y/10000` and lets
the batch sort. It doesn't fit this renderer:
- Every outlined glyph is nine same-depth draws. XNA's depth sort isn't stable, so outline and fill
  would flicker in and out of order.
- A per-sprite sort breaks texture batching across sheets.
- The whole UI shares one `SpriteBatch` pass that `ElementPoolService`'s render-state stack owns.

An explicit row walk gets the same order deterministically, and the loop already has that shape.

## Design

### The rule

**Draw the map by rows, top to bottom. Within a row, draw left to right. Anything that stands up
draws in the row of its lowest footprint tile, and its sprite may extend upward (and sideways)
past its footprint.** A later row's sprite overlaps an earlier row's, never the reverse. Walls are
split into a front (their own tile, still drawn from the cache) and a top (the tile above, drawn in
the row walk when something could stand under it).

### The row walk

`DrawOccupants` becomes row-outer, column-inner:
- **Anchor tile.** The Blocking occupant draws from the bottom-left tile of its footprint
  (`Position.X`, `Position.Y + Size.Y - 1`), not its top-left, with the footprint rectangle
  unchanged. Phasing overlays use the same anchor. Today they draw at every tile the entity occupies,
  which a multi-tile Phasing entity would show as duplicates.
- **Corpses keep their top-left anchor.** A corpse lies on the floor: anything standing on its
  lower tiles must draw over it. Drawing it in its top row, ahead of that row's standing occupants,
  does that. The Tiny sub-grid is single-tile and unchanged.
- **Overscan.** Walk rows `[0, TileRows + MaxReachRowsAbove)` and columns
  `[-MaxReachColumnsBeside, TileColumns + MaxReachColumnsBeside)`, both constants 2. That way an
  entity whose anchor is just below or to either side of the grid still draws the part that reaches
  into view. See "Keeping overhang small" for why the constants are 2 and how content is held to
  them. The camera's existing +2 already covers the bottom and right edges, and `IsOnMap` keeps
  rejecting unloaded cells.
- **Layer badges move out of the walk.** They describe the tile, and a later row's tall sprite
  would paint over them. The walk appends `(tileOrigin, hasHigher, hasLower)` to a reused buffer,
  only for tiles with a non-zero mask, and a short pass draws them after the walk. There are no
  extra `GetOccupiedLayerMask` reads.
- `DrawOccupants`' remark about row-major making no measured performance difference becomes the
  reason for the change, not a reason to leave it.

### Sprite placement (pixels per tile + pivot)

Art decides how big a sprite draws, the way RimWorld's `drawSize` and Unity/Godot's
pixels-per-unit and pivot do.
- `SpriteManifestEntry` gains optional `PixelsPerTile` (int) and `PivotX`/`PivotY` (source
  pixels). A missing `PixelsPerTile` keeps today's behaviour, stretching to the footprint, so no
  existing entry changes meaning. The pivot defaults to bottom-centre of the cell. The fields live
  on the entry, not the cell, because every variant or frame of an entry shares one canvas layout.
  They are mirrored in `DevTools/SpriteManifestBuilder` (see "SpriteManifestBuilder" below).
- **Multi-cell options.** `SpriteManifestCell` gains `ColumnSpan`/`RowSpan`. An absent or 0 value
  means 1, so every existing entry reads unchanged. One option's source rectangle is then
  `CellWidth × ColumnSpan` by `CellHeight × RowSpan`, starting at its (Column, Row) grid cell. An
  option is still a single contiguous rectangle, so it is still one `SpriteView` and one draw.
  `SpriteManifest.ToSprite` is the only runtime reader that changes.
- **Drawn rectangle.**
  - **Size** is `sourceSize / PixelsPerTile * CurrentTileSize`, scaled per axis by the footprint
    (`Size.X`, `Size.Y`). Placement is authored for a 1x1 footprint, and a 2x2 or Huge (3x3)
    goblin draws the same art scaled up. Scaling per axis is what stretching already does, so a
    1x2 ghost keeps its shape.
  - **Position:** the pivot lands on the bottom-centre of the footprint.
  - **Example:** for the Player (64 px canvas, feet about 12 px above the bottom), something like
    `PixelsPerTile = 32`, `PivotY = 52`. Actual values are picked in game.
  - **Clamped to the reach box** (see "Keeping overhang small"). A scaled-up sprite that would leave
    the box is shrunk uniformly about its pivot until it fits, so it is never clipped. In practice a
    Huge entity's art fits its own 3x3 footprint.
- **Carried as data.** `SpriteView` gains a `SpritePlacement? Placement`. `SpriteManifest` lookups
  return it alongside the cell, `MapViewQuery` fills it for blueprint-appearance and terrain
  sprites, and a per-instance `SpriteComponent` override has none (stretch). `SpriteComponent` and
  the pools don't change size.
- **Computed once.** A `SpritePlacementMath` helper in Presentation (pure, testable) does the
  calculation, and `DrawVisual` calls it. `SpriteOrGlyphRenderer` stays a stretch-to-rect
  primitive. Inventory, hotbar and Folder icons never read `Placement`, so icons keep filling their
  cells.
- **Cues follow the drawn sprite, not the footprint.** The health bar sits at the top of the drawn
  rectangle (clamped to no lower than the footprint top), and the charging badge sits above the
  drawn top. Otherwise a tall sprite's head covers its own health bar. Dodging glow, loot bag,
  selection and targeting highlights stay on the footprint: they are about tiles, not the picture.
- Clicks and hover stay tile-based. Clicking a tall sprite's head selects the tile under the
  cursor, never "the sprite you probably meant", which follows the core principle of removing
  ambiguity. This matches RimWorld and Stardew.

### Wall fronts and tops

- `TerrainDefinition` gains `TopSpriteName` (string?, the face drawn on the tile directly above).
  `StoneWall` becomes `SpriteName: "Wall-Front", TopSpriteName: "Wall-Top"`. `TerrainRegistry`
  caches top variants beside the base variants and picks one by the same `TerrainCell.Variant`.
  `IMapViewQuery` gains `TryGetStructureTopVisual(x, y, layer, out EntityVisualView)`. A definition
  with no top behaves exactly as today, which covers glyph-only walls and mods.
- `Wall-Top` is already a linked set when this lands (Phase 1 authors it that way). Until joining
  lands, a linked top draws its fully-joined piece (all four sides), so walls read as one
  continuous mass rather than as random pieces.
- **Where each top draws depends on what is under it:**
  - *The tile above is also a structure, or is off the map.* Nothing can stand there (walls block
    every mover but Phasing, and Phasing already draws translucently on top of walls). The top goes
    into `_terrainCache`, in a **second pass** after every floor and front. A thick wall reads as
    all tops with one row of fronts at the bottom, and the cache's walk order stops mattering.
  - *The tile above is open.* Something can stand behind the wall, so the top must occlude it. It
    goes on an **overhang list**, `(mapX, mapY, SpriteView)`, rebuilt inside the same cache-render
    callback. The list has exactly the cache's invalidation (structures, terrain streaming, scroll,
    zoom, layer), so there is nothing new to keep in sync. The uncached fallback path rebuilds it
    every frame it runs.
- **Where the row walk draws them.** A wall in row `r+1` belongs to row `r+1`'s bucket. The walk
  draws that row's overhangs first, then row `r+1`'s occupants. The top therefore lands over
  everything standing in row `r` (behind the wall), and a tall entity standing beside the wall in
  row `r+1` still draws over it. The overhang list is sorted by row once when it is built, and the
  walk advances an index through it. There are no per-tile structure reads added to the occupant
  pass: only boundary walls in view reach the list, typically a few dozen.
- Fronts stay in the cache. Something south of a wall is in a later row and draws over the front
  anyway. Something north never reaches the front, because walls are only ever one tile tall.
- Glow still draws over everything after the walk, so a wall top under an aura glows like the floor.
- The Admin "Regenerate" and neighborhood streaming paths already invalidate the cache, so they
  need nothing new.

### Fading what's in front of the player or the inspected entity

- **Who is watched.** Each frame, compute the watched rectangles: the player's drawn sprite
  rectangle and, when `InspectionMode` is Detail or Admin, `InspectedEntityId`'s. Use the drawn
  rectangle from the sprite placement, which for a 1x1 stretched sprite is exactly the footprint the
  TODO describes. It also covers a wide sprite peeking into the next column and a tall one's head.
  There are at most two rectangles, so no spatial index.
- **What fades.** An overhang (wall top, or any future tall-terrain part) whose destination
  rectangle intersects a watched rectangle draws at `OverhangFadeAlpha` (0.4), through the existing
  `alphaMultiplier`. Only live overhangs can fade. Cached tops sit over walls, where nobody stands,
  and a Phasing entity inside a wall already draws over it.
- The fade is instant rather than lerped, because a lerp needs per-cell state for a cosmetic
  nicety. Revisit only if it reads as popping.

### Joined-up (linked) wall tops

RimWorld's linked-atlas idea, applied to the top face. Without it, every wall top is the same
self-contained piece, so a wall run reads as a row of separate blocks rather than one wall.

- **Declared on the art, not the terrain.**
  - `SpriteManifestEntry` gains `Linking` (`None` | `Cardinal`, default `None`) and `LinkGroup`
    (string?, defaulting to the entry's own `Name`). Both are authored in SpriteManifestBuilder.
  - Linking is purely visual, and which pieces join depends on how the art was drawn, so it
    belongs with the art. `TerrainDefinition` needs nothing beyond `TopSpriteName`.
  - Two structures link when their top sprites' entries share a `LinkGroup`. That lets a later
    door frame or pillar join a stone wall by sharing its group, while unrelated structures stay
    separate.
  - `TerrainRegistry` flattens each type id's link group to an int, alongside the cached variants,
    so the neighbour test compares ints and never looks up strings.
- **The mask.** Four bits, one per cardinal neighbour of the *wall* cell (not the tile its top draws
  on) holding a structure in the same group: N = 1, E = 2, S = 4, W = 8. That gives 16 variants.
- **Map edge and unloaded cells count as linked.** RimWorld has the same rule (its `MapEdge` link
  flag). Without it, a wall running off the map or into an unloaded neighborhood would get a
  rounded end cap, which would then pop to a straight run when the neighbour streams in.
- **The art.**
  - The `Wall-Top` entry holds `16 × k` cells, in mask order, in `k` groups of 16.
  - `TerrainCell.Variant % k` picks the group, so random variety still works alongside linking.
  - Cells may repeat: a mask the pack has no exact piece for reuses the nearest one. For example,
    the pack has no isolated-pillar top, so it reuses the end cap.
  - `SpriteManifest` validates at load: a linked entry whose cell count isn't a multiple of 16
    throws, naming the entry, so a broken manifest fails at startup instead of drawing wrong
    pieces.
  - Authored as slots in SpriteManifestBuilder (see below), never by hand-counting JSON.
- **Computed, not stored.**
  - The mask is worked out in the cache's top pass and in the overhang-list build. Both run only on
    invalidation, and cost four extra structure reads per top in view.
  - It isn't stored in `TerrainCell`. `Variant` is rolled once, and neighbours change after that:
    `SetStructure`, streaming, Admin Regenerate. A stored mask would need fix-up at every one of
    those writes.
  - `StructureChangedEvent` already invalidates the whole cache, so a wall added or removed at
    runtime re-links its neighbours with nothing new.
  - `InvalidateTerrainCachesIfVisible` widens its test by one tile. A neighborhood loading just
    outside the grid can change the mask of a wall on the grid's edge.
- **Fronts are not linked.** A front only shows where the south neighbour isn't a wall. Its left
  and right ends are covered well enough by the tops above them, and the brick front art in the
  pack tiles seamlessly. If end caps turn out to read badly, that is a separate, 2-bit (E/W)
  version of the same mechanism, not part of this plan.
- **Inner corners are out of scope.** Four bits can't see diagonals. An L-shaped wall's concave
  corner uses the plain piece for its mask, not a notched one, the way RimWorld's walls do. A
  47-piece "blob" tileset (8 neighbours) is the upgrade path if that corner reads wrong at 36 px.
  The top pass would compute an 8-bit mask and remap it, and nothing else would change.

### Keeping overhang small

The row walk's overscan (`MaxReachRowsAbove`, `MaxReachColumnsBeside`) is extra rows and columns
walked every frame. It has to cover the farthest anything draws from its anchor tile (the
bottom-left of its footprint). If one big sprite were allowed to reach five rows up, every frame
would pay for five extra rows for its sake. So the limit is a rule the content follows, not a number
that grows to fit the content.

- **The reach box: 2 rows above and 2 columns beside the anchor tile.** Everything an entity draws
  stays within rows `anchor − 2 … anchor` and columns `anchor − 2 … anchor + 2`. Nothing draws
  below its footprint's bottom edge, since the next row would paint over it anyway.
- **Huge (3x3) is the size cap.** The box is sized to fit a Huge entity exactly: its footprint
  reaches 2 rows up and 2 columns right of its anchor. It is used for bosses, gods and small
  buildings.
  - The same box gives a 1x1 creature up to 3 tiles of height and 5 of width, and a 2x2 one a row
    of headroom.
  - A Huge entity's art must fit its own footprint, which the clamp in "Sprite placement"
    guarantees.
  - Footprints are capped too: a blueprint's default `Size`, or a `SpawnRequest.Size`, past 3 on
    either axis is refused, naming the blueprint (at `ResolveAll` and at `EntityFactory.Spawn`).
    Anything bigger is built from pieces (below).
- **Enforced in three places:**
  - *Manifest load.* `SpriteManifest` works out each placed entry's reach at a 1x1 footprint, and
    an entry outside the box throws, naming the entry and its reach. That catches authoring
    mistakes.
  - *Draw time.* `SpritePlacementMath` clamps, because a sprite within the box at 1x1 can leave it
    when scaled up to 2x2 or 3x3. That guarantees the overscan holds whatever size something is
    spawned at.
  - *Builder.* The preview draws the box as a boundary line, so it's seen while authoring, not at
    startup.
- **Anything bigger than Huge is made of small pieces, not drawn as one big sprite.** A large
  building, a castle or a gatehouse is composed of parts, each within the limit:
  - walls and wall tops (structure cells, joined by a link group);
  - doors and windows (entities);
  - roof and awning pieces (overhang pieces like wall tops).
  
  This is how RimWorld and Stardew build interiors too: walls are tiles, and a "building" is a
  room the player sees, not one sprite.
- **A building as one thing is a parent entity owning its pieces, through an entity
  relationship** (landed: CLAUDE.md, IMPLEMENTATION-NOTES "Entity relationships"). Some things
  belong to the building rather than to any one piece: its name and owner, "this is the potion
  shop", that clicking any of its doors opens its trade, and that destroying it destroys its
  parts. What the relationship already gives, and what's left:
  - given: a `BuildingPartLink` (piece → building) registered `Acyclic` with `DestroySources`
    holds the parent by `EntityKey` and destroys the pieces with it, before the building's own
    `EntityDestroying` handlers run; `RootOf` finds a piece's building and
    `CopyDescendantEntityIds` lists a building's pieces; links to skeletons are allowed, and a
    relationship changes no tiers;
  - left: interaction on a piece resolving to its root, wherever an entity is chosen for
    interaction (context menu, shop/trade opening, inspection);
  - left: a blueprint facet listing parts with offsets, spawned parent first in one
    `EntityFactory` call, within one neighborhood's plan so the building unloads and regenerates
    whole, and like-with-like for pool paging;
  - left: the parent is an ordinary non-blocking, invisible entity at the building's origin tile
    (the aura-anchor precedent), so tiers, streaming and inspection treat it like anything else.
  
  None of that is in this plan. The Notes phase adds a TODO entry for it, so the next large
  building isn't solved with a big sprite and a raised overscan instead.
- Until then, a shop or small building is a single entity up to Huge. A 2x2 or 3x3 shop sprite is
  fine within the box. What the limit forbids is anything bigger than that.

### SpriteManifestBuilder

`DevTools/SpriteManifestBuilder` is the only way manifest entries get authored. Today an entry is a
flat list of cells: you click a grid cell to add it, and every cell is an alternative the game
picks from at random. That has no way to say "these four cells are *one* 2x2 picture" or "these
16 cells are one joined-up set". The builder gains both, plus the placement fields. Its
`ManifestCell`/`ManifestEntry` mirrors gain the same fields as the Game records.

**Options versus multi-tile: one gesture each, no mode switch.**
- **Click** a grid cell to add a one-cell *option*, exactly as today.
- **Drag** across a rectangle of grid cells to add *one* multi-cell option spanning all of them,
  saved as a single cell with `ColumnSpan`/`RowSpan`.
- **Click inside an existing option** (single or multi-cell) to remove it. A drag that starts
  inside an existing option still adds, because a drag is never a remove.
- A drag that would add a rectangle identical to an existing option is refused with a warning,
  rather than adding a duplicate that would silently double its odds. Partly overlapping
  rectangles are allowed; the sheet decides what is sensible.
- **On the sheet**, each option is drawn as one outlined rectangle with its number (`#1`, `#2` …),
  so a 2x2 option reads as one box, not four. The options list shows each option's span: "#2
  Wall_Tiles (6,21) 1x2 cells".
- **The rule the tool enforces:** every option of an entry has the same span. Save refuses a
  mix ("#3 is 2x2, the others are 1x1") because placement is per entry. Two options of different
  shapes would draw at different sizes, and that almost always means a cell was clicked when a drag
  was meant. If a real need for mixed shapes shows up, relax this deliberately.
- **Legend.** A one-line legend under the entry fields reads "Options: 3, each 2x2 cells". That
  makes "several choices" versus "one big picture" explicit at a glance.

**Placement fields.**
- A `Pixels per tile` numeric has a "stretch to footprint" checkbox. Checked, the default, means
  no value is saved.
- **Pivot** has two numerics, plus a "Set pivot" toggle. With the toggle on, the next click on the
  preview sets the pivot to that pixel, instead of adding or removing on the sheet.
- **Preview panel.** The selected option is drawn at zoom, with tile lines every `PixelsPerTile`
  source pixels and a crosshair at the pivot. The footprint tile is shaded, and the reach box
  is a boundary line. That shows how much of the sprite overhangs its tile, and whether that is
  allowed, before anything is run in game. Save refuses an entry outside the box at 1x1, with the same
  message the game's load check gives. A footprint picker (1x1, 2x2, 3x3) redraws the preview at
  that size with the draw-time clamp applied, so you can see how a Huge variant of the creature
  will look.

**Linked sets and link groups.**
- **Kind** selector: `Options` (default) or `Linked set`.
- **Linked set layout.** The option list is replaced by a 4×4 grid of 16 **slots**. Each slot is
  labelled with a small tile diagram whose bars show its connected sides, and none mentions bit
  numbers. Select a slot, then click (or drag, for multi-cell) on the sheet to fill it. A filled
  slot shows its piece as a thumbnail.
  - **Filling slots:** "Copy to…" copies the selected slot's piece to other slots, for pieces the
    pack doesn't have. "Add variant set" adds another 16-slot page (the `k` above).
  - **Saving:** save writes the slots in mask order. It is refused while any slot is empty, and
    the refusal names the empty slots.
- **Link group** field: a combo box of every `LinkGroup` already in the manifest, editable to type
  a new one. It defaults to the entry's name. Beside it, a read-only "Also in this group:" line
  lists the other entries in the group, so joining two structures' art is a visible choice.
- **Link preview.** A small canvas draws a fixed test layout: a horizontal run, a vertical run,
  L, T and + junctions, a 3×3 block, and a lone pillar. It uses the current slots, so a wrong
  piece shows up before saving. It uses the same mask function the game uses. The builder doesn't
  reference Game, so the few lines are duplicated beside the other mirrored types, with the same
  "keep in sync" note.

The builder has no test project. Its logic that matters stays in small plain classes, which can
move under test if it grows: option hit-testing and span validation, slot-to-mask ordering, and
the mask function. The round trip is covered from the Game side (see Tests).

## Decisions (approved)

1. **Wall art.** You pick the `Wall-Front` and `Wall-Top` cells in SpriteManifestBuilder (grey
   stone to match today). Both are authored in Phase 1, `Wall-Top` directly as its 16 linked
   slots.
2. **Four-bit linking, not a 47-piece blob.** Inner (concave) corners use the plain piece. Upgrade
   only if they read wrong in game.
3. **Off-map and unloaded neighbours count as linked.**
4. **Linking and link groups live in the sprite manifest**, authored in the builder, not on
   `TerrainDefinition`.
5. **Click adds an option, drag adds one multi-cell option.** There is no mode switch, and every
   option in an entry must share one span.
6. **Tall creatures in front of the player don't fade.** The TODO scopes the fade to tall terrain.
   A big creature south of the player covering them can join the fade later by adding its drawn
   rectangle to the overhang test.
7. **The fade is 0.4 alpha and instant.**
8. **Clicks stay tile-based.** A tall sprite's head isn't a click target for its entity.
9. **Corpses keep a top-row anchor** (floor-level). A corpse's own sprite placement can still
   overhang upward over the row above. That is accepted, and "Blood pool under dead entities" can
   revisit floor decals.

## Phases

Each phase ends with a manual in-game check before the next starts.

1. **SpriteManifestBuilder and the manifest format.** This comes first so that every later phase
   has real sprites to test with.
   - Build (builder): everything in "SpriteManifestBuilder" above:
     - click/drag options with spans, numbered outlines, the same-span rule and legend;
     - the pixels-per-tile and pivot fields, "Set pivot", and the preview with the reach box;
     - the Kind selector, the 16-slot linked set with side diagrams, "Copy to…" and "Add variant
       set";
     - the link-group combo box with "Also in this group:", and the link preview.
   - Build (game, data only, no drawing changes):
     - `SpriteManifestEntry`/`SpriteManifestCell` gain the placement, span, `Linking` and
       `LinkGroup` fields, and `SpriteManifest.ToSprite` honours spans;
     - load validation: a linked entry must be a multiple of 16, and no entry may pass the
       reach box at a 1x1 footprint;
     - the lookups return placement beside the cell, read by nothing yet.

     This is so that whatever the builder writes loads, and is checked, from day one.
   - Art: author Player and Goblin placement, `Wall-Front`, and `Wall-Top` as a 16-slot linked set
     in group `stone-wall`, plus a `Shop-2x2` entry as one 2x2 option. The existing `"Wall"` entry
     stays until the walls phase switches over.
   - Check (builder):
     - a drag makes one 2x2 option and a click makes a 1x1; clicking inside the 2x2 removes it;
     - a mixed-span save, an incomplete linked set, and an entry outside the reach box are each refused
       with a reason;
     - the link preview joins the test layout cleanly;
     - existing entries open and re-save byte-identical.
   - Check (game): it starts, and every map sprite looks exactly as before.
2. **Row walk.**
   - Build: row-outer walk, bottom-row anchoring for Blocking and Phasing, corpse top-row
     anchoring, the 2-row/2-column overscan, the layer-badge post-pass, and the Huge footprint cap
     at `ResolveAll` and `EntityFactory.Spawn`. Update `DrawOccupants`' remarks.
   - Check:
     - nothing looks different at 1x1;
     - a Huge (3x3) goblin is drawn whole beside its neighbours, including when its anchor is just
       off the left, right or bottom edge;
     - an Admin spawn of anything bigger than 3x3 is refused with its reason;
     - a corpse with a live occupant on it, the Tiny grid, Phasing and badges all look right at all
       three zoom levels;
     - compare MapWindow draw time with the diagnostics engine before and after.
3. **Sprite placement (game).**
   - Build: `SpritePlacement`/`SpriteView.Placement`, `SpritePlacementMath` (footprint scaling and
     the reach-box clamp), `DrawVisual`, and the health-bar and charging-badge anchoring. Delete
     TODO "Per-entity sprite scale".
   - Check:
     - the player is larger with feet on its tile, and the goblin is smaller;
     - 2x2 and Huge goblins draw the same art scaled up, a Huge one inside its own 3x3, and they
       match the builder's footprint preview;
     - the player's head overlapping the row above draws over a goblin there;
     - a goblin south of the player draws over the player's feet, not under them;
     - inventory and hotbar icons are unchanged.

     Point `Shop` at `Shop-2x2` and spawn one 2x2. If it looks right, "Multi-tile sprites" (Low)
     is done; delete that TODO.
4. **Wall fronts and tops.**
   - Build: `TopSpriteName`, registry top variants (a linked top draws its fully-joined piece for
     now), `TryGetStructureTopVisual`, the cache's second pass, the overhang list, and the walk's
     per-row overhang draw. Switch `StoneWall` to `Wall-Front`/`Wall-Top`, and remove the old
     `"Wall"` entry.
   - Check:
     - walk behind (north of) a wall and the top covers you; walk in front and you cover the front;
     - thick walls read as tops with one front row;
     - check at zoom, during drag-scroll, across a neighborhood boundary while streaming, and after
       Admin Regenerate.
5. **Fade.**
   - Build: watched rectangles and the overhang fade.
   - Check:
     - the wall top goes translucent over the player and stays opaque elsewhere;
     - the same happens for a Detail-inspected goblin walking behind a wall;
     - in Basic mode it doesn't happen for the goblin.
6. **Joined-up wall tops (game).**
   - Build: the registry's per-type link-group int, the mask computation shared by the cache top
     pass and the overhang-list build, `16 × k` variant selection, and the widened visibility test.
   - Check:
     - the in-game walls match the builder's link preview for the same slots;
     - a straight horizontal run, a vertical run, a thick block, an isolated pillar, and T, L and +
       junctions all join cleanly;
     - Admin-spawned or removed walls re-link their neighbours;
     - a wall crossing a neighborhood boundary has no cap before or after the neighbour streams in;
     - joined tops still fade over the player.
7. **Notes.**
   - Add an `IMPLEMENTATION-NOTES.md` entry covering the row-walk rule, anchor rules, placement,
     the reach box and the Huge size cap, the static/live overhang split, and linking (the mask is computed, not
     stored, and off-map counts as linked).
   - Delete the TODO entry.
   - Update "Walking and action animations": a moving entity draws in the bucket of its lower
     visual row (its destination when moving south, its origin when moving north), or it will pop
     between rows mid-step.
   - Add a TODO entry, "Composite buildings", from "Keeping overhang small": a `BuildingPartLink`
     relationship (`Acyclic`, `DestroySources`), interaction resolving to the root (`RootOf`), the blueprint parts facet spawned within one neighborhood, and the
     parent as an invisible non-blocking entity at the origin tile.

## Tests

- **Row-walk order**, via a recording draw sink around `DrawOccupants`. That means extracting the
  walk's "what draws where, in what order" into a pure planner (`MapDrawOrder`) that yields draw
  items MapWindow then renders, so it's testable headless:
  - A south neighbour's item comes after a north neighbour's, including across columns: row r+1,
    column c comes after row r, column c+1.
  - A 2x2 Blocking entity yields once, at its bottom-left tile, with its full footprint rectangle.
  - An entity anchored two rows below the grid, or two columns left or right of it, is still
    yielded. Three away is not.
  - A Huge entity anchored two columns left of the grid yields with its footprint reaching into
    view.
  - A corpse yields in its top row, ahead of a live occupant standing on its lower tile.
  - A multi-tile Phasing entity yields once.
  - Layer badges come after every occupant.
- **`SpritePlacementMath`:**
  - No `PixelsPerTile` gives the footprint exactly.
  - A 64 px cell at 32 px per tile gives 2x2 tiles at each zoom tile size.
  - The pivot lands on the footprint's bottom-centre.
  - Placement scales per axis with the footprint: 2x2 doubles it, and 1x2 doubles only its height.
  - A sprite inside the reach box at 1x1 that leaves it at 3x3 is shrunk uniformly about its pivot
    to fit exactly. One that already fits is untouched.
  - No result ever leaves rows `anchor − 2 … anchor` or columns `anchor − 2 … anchor + 2`. This is
    checked over every footprint from 1x1 to 3x3 against the checked-in manifest.
  - The health-bar anchor clamps to the footprint top.
- **Footprint cap:** a blueprint whose default `Size` is 4 on either axis fails `ResolveAll`, naming
  it, and a mod with one fails its dry run. A `SpawnRequest` with `Size` 4 throws. 3x3 is accepted.
- **`SpriteManifest`:**
  - Entries without the new fields deserialize unchanged: span 1x1, no placement, not linked.
  - Placement, spans, `Linking` and `LinkGroup` round trip.
  - A 2x1-span option's source rectangle is two cells wide from its origin cell.
  - A linked entry with 15 or 17 cells throws at load, naming the entry.
  - At a 1x1 footprint, an entry whose placement reaches 3 rows above, 3 columns to one side, or
    below the footprint throws at load, naming the entry and its reach. Exactly 2 rows and 2
    columns loads.
  - Every entry in the checked-in manifest is within the reach box at 1x1.
  - An omitted `LinkGroup` defaults to the entry name.
  - `SpriteManifestTests`' Wall rows become Wall-Front and Wall-Top.
  - The checked-in `Content/SpriteManifest.json` loads, which catches a builder that wrote a shape
    the game can't read.
- **`TerrainRegistry`:** the top variant follows `Variant` like the base, and a definition with no
  top gives none. For `MapViewQuery.TryGetStructureTopVisual`: none with no structure, none with no
  top.
- **Overhang list build** (pure, from an `IMapViewQuery` fake):
  - A wall under open floor goes to the list.
  - A wall under another wall, or under an off-map tile, goes to the cached pass.
  - The list is sorted by row.
- **Walk with overhangs:** a wall in row r+1 yields its top after row r's occupants and before row
  r+1's.
- **Fade:**
  - The player's rectangle intersecting an overhang gives the fade alpha, and one next to it gives
    full alpha.
  - The inspected entity counts only in Detail or Admin mode.
  - A 2x2 inspected entity counts on every tile.
- **Wall linking**, the pure mask function over an `IMapViewQuery` fake:
  - Each of the 16 neighbour layouts gives its mask.
  - A same-group structure links, and a different-group one doesn't.
  - An off-map or unloaded neighbour links.
  - The mask is read from the wall's own neighbours, not those of the tile its top draws on.
  - `Variant % k` picks the group, and a 16-cell entry always gives group 0.
- **Linked `TerrainRegistry`:** two definitions whose top entries share a `LinkGroup` get the same
  link-group int, and different groups get different ints. An unlinked top ignores the mask.
- **Overhang list with linking:** a live overhang and a cached top of the same wall run get masks
  that agree at the boundary between them.
- `MapWindowArchitectureTests` still passes: nothing new reads a pool.

Sources: [RimWorld Wiki: Mod Textures](https://rimworldwiki.com/wiki/Modding_Tutorials/Textures),
[RimWorld Wiki: ThingComp (SectionLayer/Print)](https://rimworldwiki.com/wiki/Modding_Tutorials/ThingComp),
[RimWorld altitudeLayer](https://rimworldwiki.com/wiki/User:Alistaire/Type:altitudeLayer),
[Sense Of Depth For Tall Buildings](https://steamcommunity.com/sharedfiles/filedetails/?id=3246956515),
[Stardew Valley Wiki: Modding:Maps](https://stardewvalleywiki.com/Modding:Maps),
[Show Players Behind Map Tiles](https://www.curseforge.com/stardewvalley/mods/show-players-behind-map-tiles),
[GameDev.net: faux-3D in 3/4 view](https://www.gamedev.net/forums/topic/616344-robust-faux-3d-in-34-top-down-perspective/),
[Godot Forum: Y-sort with 3/4 view](https://forum.godotengine.org/t/how-to-y-sort-player-with-3-4-view-tilemaplayer/83392).
