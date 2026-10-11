# Tall Sprites and Two-Tile Walls

(Landed 2026-10-11, all 8 phases; durable notes in IMPLEMENTATION-NOTES.md "Tall sprites, wall faces and
tops, and the sprite manifest". Replaced TODO.md's "Sprites taller than one tile, and two-tile walls (front +
top)" entry and absorbed "Per-entity sprite scale" and "Multi-tile sprites"; all three are deleted.)

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

**Tile and level editors** (RPG Maker, Godot 4 TileSet, Unity Rule Tile, Tiled, LDtk), looked at during Phase 1 for
how walls, tall art and joined tiles are authored:
- *Walls are two pictures.* RPG Maker's wall sheet (A4) splits each wall into a ceiling (top) section and a side (face)
  section, from different parts of the sheet: the ceiling joins in every direction, the face joins left and right with
  end caps. LDtk derives several rule layers from one "this cell is a wall" grid. Nobody draws a joined wall as one tall
  sprite per cell. This plan does the same: one wall entry, a face and a top authored separately.
- *Joining.* Godot's "Match Sides" and Tiled's Edge sets are the 16-piece set used here; "Match Corners and Sides" is
  the 47-piece upgrade path, and Godot users report how many more pieces it needs.
- *Turns and mirrors are expected.* Godot gives every tile `flip_h`/`flip_v`/`transpose` (the same 8 orientations as
  rotation + flip here), Unity Rule Tile can try a rule's sprite rotated or mirrored, and Tiled's maintainer notes many
  tilesets leave out orientations because users are expected to flip or rotate. So the builder can fill a set's missing
  pieces by mirroring or turning the ones it has.
- *Origin is per tile.* Godot's `texture_origin` (draw offset) and advice to shift tall art so its base sits on the
  tile match the pivot here; its separate `y_sort_origin` is this plan's anchor-tile rule.

## Design

### The rule

**Draw the map by rows, top to bottom. Within a row, draw left to right. Anything that stands up
draws in the row of its lowest footprint tile, and its sprite may extend upward (and sideways)
past its footprint.** A later row's sprite overlaps an earlier row's, never the reverse. A wall
is one manifest entry with two separately authored parts: a face standing on the wall's own tile
(one or two rows tall) and a top on the tile above the face. Whatever covers a tile something can
stand on is drawn in the row walk; whatever covers only walls stays in the cache.

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
  `SpriteManifest.ToSprite` is the only runtime reader that changes. (Landed in Phase 1.)
- **Turns and flips, per cell** (landed in Phase 1). `SpriteManifestCell` gains `Rotation` (0, 90,
  180, 270, clockwise) and `FlipHorizontal`/`FlipVertical`, all omitted when unset. Flips apply
  first, then the rotation -- the order `SpriteBatch` applies `SpriteEffects` and rotation in, so
  the drawing phases pass them straight through. A quarter turn needs a square cell (the drawn
  size must not change), checked at load. Pivot and placement are measured on the turned picture.
  Both flips together are always written as a 180 turn, so equal pictures compare equal. Drawing
  them is the drawing phases' job: until then a turned cell draws unturned.
- **Unfinished linked sets** (landed in Phase 1). A linked set may be saved with empty slots, held
  as `null` cells, so a set can be finished across sessions. Every lookup treats an empty slot as
  a miss; only a linked set may hold one.
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

### Wall faces and tops

- **One entry, two parts** (data landed in Phase 1). A wall is a manifest entry whose own cells are
  its **face** and whose `Top` (`SpriteManifestPart`: cells, placement, linking, link group) is its
  **top**, authored separately -- usually from different areas of the sheet, as RPG Maker's wall
  sheet does. One name pairs them, so `TerrainDefinition` needs nothing new: `StoneWall` keeps
  `SpriteName`, pointed at the wall entry (`"StoneWall"`), and a definition whose entry has no
  `Top` behaves exactly as today (glyph-only walls, mods). `TerrainRegistry` caches top variants
  beside the face variants (`SpriteManifest.CountTopCells`/`TryGetTopCell`) and picks one by the
  same `TerrainCell.Variant`. `IMapViewQuery` gains `TryGetStructureTopVisual(x, y, layer, out
  EntityVisualView)`.
- **The face** is a 4-piece `Horizontal` set -- standalone, left end, right end, middle -- with
  variant sets for variety (see "Face end caps"). Its height comes from its placement like any
  sprite's: a 16x32 face at 16 px per tile stands two rows tall on the wall's
  tile. Stretched, it is one tile. It draws only on a wall tile whose south neighbour isn't a wall
  of the same link group, so a thick wall shows faces only along its exposed south edge.
- **The top** is a 16-slot linked set, drawn on every wall tile, on the tile directly above the
  face's highest row: `SpriteManifestPart.HeightRows()` of the face (its own tile plus every row it
  reaches above, a part row counting whole) is how many rows the top is raised. The same raise
  applies whether or not that tile draws a face, so a column of wall tiles reads as tops with the
  face rows at its bottom.
- **Face + top ≤ 3 rows.** The top is checked against the reach box raised by the face's height
  (`SpriteReach.Of(..., raisedRows)`): with a one-tile top, a face may be at most two rows tall.
  Manifest load and the builder both refuse more, naming the entry and "(top, on an N-row face)".
  A wall's picture therefore reaches up to two rows north of the tiles that block movement; the
  gain is that a tall wall hides whoever stands behind it. (RPG Maker avoids the mismatch by making
  tall walls more rows of blocking cells; our walls are one cell thick, so that would be a map
  generation change, not this plan.)
- Until joining lands, a linked top draws its fully-joined piece (all four sides), so walls read as
  one continuous mass rather than as random pieces.
- **Cached or live, per piece, by what it covers:**
  - *Every tile it covers is a structure or off the map.* Nothing can stand there (walls block
    every mover but Phasing, and Phasing already draws translucently on top of walls). It goes into
    `_terrainCache`: faces in the first pass with the floor, tops in a **second pass** after every
    floor and face, so the cache's walk order stops mattering. A one-row face covers only its own
    wall tile, so it is always cached.
  - *It covers a tile something can stand on.* A top over an open tile, or a two-row face whose
    upper row is open, must occlude whoever stands there. It goes on an **overhang list**,
    `(mapX, mapY, SpriteView)` keyed by the wall's own tile, rebuilt inside the same cache-render
    callback. The list has exactly the cache's invalidation (structures, terrain streaming, scroll,
    zoom, layer), so there is nothing new to keep in sync. The uncached fallback path rebuilds it
    every frame it runs.
- **Where the row walk draws them.** A wall in row `r` belongs to row `r`'s bucket. The walk draws
  that row's overhangs first (its faces, then its tops), then row `r`'s occupants. Its face's upper
  row and its top therefore land over everything standing in rows `r-1` and `r-2` (behind the
  wall), and a tall entity standing beside the wall in row `r` still draws over them. The overhang
  list is sorted by row once when it is built, and the walk advances an index through it. There
  are no per-tile structure reads added to the occupant pass: only boundary walls in view reach
  the list, typically a few dozen.
- Something south of a wall is in a later row and draws over the face anyway.
- Glow still draws over everything after the walk, so a wall top under an aura glows like the floor.
- The Admin "Regenerate" and neighborhood streaming paths already invalidate the cache, so they
  need nothing new.

### Fading what's in front of the player or the inspected entity

- **Who is watched.** Each frame, compute the watched rectangles: the player's drawn sprite
  rectangle and, when `InspectionMode` is Detail or Admin, `InspectedEntityId`'s. Use the drawn
  rectangle from the sprite placement, which for a 1x1 stretched sprite is exactly the footprint the
  TODO describes. It also covers a wide sprite peeking into the next column and a tall one's head.
  There are at most two rectangles, so no spatial index.
- **What fades.** An overhang (a wall top, a tall face's live upper row, or any future tall-terrain
  part) whose destination rectangle intersects a watched rectangle draws at `OverhangFadeAlpha`
  (0.7, raised from 0.4 after the in-game check: the wall stays readable), through the existing `alphaMultiplier`. Only live overhangs can fade. Cached pieces sit
  over walls, where nobody stands, and a Phasing entity inside a wall already draws over it.
- The fade is instant rather than lerped, because a lerp needs per-cell state for a cosmetic
  nicety. Revisit only if it reads as popping.

### Joined-up (linked) wall tops

RimWorld's linked-atlas idea, applied to the top face. Without it, every wall top is the same
self-contained piece, so a wall run reads as a row of separate blocks rather than one wall.

- **Declared on the art, not the terrain.**
  - `SpriteManifestEntry` and `SpriteManifestPart` (a wall's `Top`) carry `Linking` (`None` |
    `Cardinal`, default `None`) and `LinkGroup` (string?, defaulting to the entry's own `Name`).
    Both are authored in SpriteManifestBuilder. For a wall it is the top part's group that decides
    which walls join.
  - Linking is purely visual, and which pieces join depends on how the art was drawn, so it
    belongs with the art. `TerrainDefinition` needs nothing new.
  - Two structures link when their wall entries' tops share a `LinkGroup`. That lets a later
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
  - The wall entry's `Top` holds `16 × k` cells, in mask order, in `k` groups of 16.
  - `TerrainCell.Variant % k` picks the group, so random variety still works alongside linking.
  - Cells may repeat, turned or mirrored: a mask the pack has no exact piece for reuses the nearest
    one (the pack has no isolated-pillar top, so it reuses the end cap), or a piece that mirrors
    or turns into it -- the builder's "Fill empty..." does that.
  - `SpriteManifest` validates at load: a linked part whose cell count isn't a multiple of 16
    throws, naming the entry, so a broken manifest fails at startup instead of drawing wrong
    pieces. An empty slot of an unfinished set loads, and draws as the fully-joined piece.
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
- **Face end caps** (data and builder landed in Phase 1; drawn from Phase 4, joined in Phase 6). A
  face only shows where the south neighbour isn't a wall, so only its left and right neighbours
  matter, as with RPG Maker's faces. `SpriteLinking.Horizontal` is four slots per variant set, in
  mask order east 1, west 2:
  - **standalone** (0): no wall to the left or right;
  - **left end** (1): no wall to the left, a wall to the right;
  - **right end** (2): a wall to the left, no wall to the right;
  - **middle** (3): walls on both sides.

  A neighbour counts when it is a structure of the same link group (the wall's, which is its top's
  group), whether or not it shows a face itself; off-map and unloaded neighbours count, as for tops.
  **Only the standalone piece is required**: manifest load and the builder refuse a variant set
  without one, and an empty left end, right end or middle falls back to it
  (`SpriteLinks.CellIndexFor`, which also gives a Cardinal set's empty slot its fully joined piece).
  So a face with only a standalone piece behaves like a plain repeating face. Until Phase 6 joins
  them, every face draws its standalone piece.
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

`DevTools/SpriteManifestBuilder` is the only way manifest entries get authored. Its
`ManifestCell`/`ManifestEntry`/`ManifestPart` mirrors carry the same fields as the Game records,
plus two pieces of authoring state the game skips: the span each part was authored at
(`ColumnSpan`/`RowSpan` on the entry and on `Top`). Everything below landed in Phase 1.

**Kinds and selection spaces.**
- **Kind**: `Options` (the game picks one at random), `Linked set` (16 slots, one per combination
  of joined sides) or `Wall (face + top)`.
- A wall has two **separate selection spaces**, switched by an "Editing: Face | Top" toggle: the
  face is a 4-slot row (left end, middle, right end, standalone -- the standalone slot marked
  required while empty), the top a 16-slot linked set. The sheet, the span, the placement panel
  and the turn/flip buttons act only on the part being edited, and the sheet outlines only that
  part's cells, so a face cell can never land in the top or the reverse. Each part keeps its own
  span, placement, link group and variant sets. The pair saves as one entry.

**Span and gestures.**
- Each part's **span** (columns x rows of grid cells) is chosen right under Kind. A **click**
  places a span-sized option, or fills the selected slot, with the clicked cell as its top-left; a
  span that would run off the sheet is refused with a warning. A **drag** places exactly the
  dragged cells. A **click inside an existing option** removes it. A drag that would add an
  option identical to an existing one (same pixels, same turn and flip) is refused with a warning.
- A cell that doesn't fit its part's span is **marked, not refused**: a warning icon on its slot
  (bottom-right), on its row in the options list, and beside its entry in Existing entries. The
  legend counts them. Save writes unfinished work as it is -- mismatched cells and empty slots --
  so a sheet can be fixed partway through a set without losing anything; the save message says
  what is left.
- On the sheet, each option is one outlined, numbered rectangle (`#1`, `#2` ...), and the options
  list describes each: "#2 Wall_Tiles.png (6,21) 1x2 cells of 16x16, turned 90°".
- **Delete selected...** (or the Delete key) removes the selected existing entries after a confirmation that defaults to
  No; Delete in the options list removes the selected options. Every list selects with Shift+click and Ctrl+click, and
  every number box selects its value when focused, through `StandardControlBehaviors` -- applied to each window, not
  wired per control. An entry opens for editing only while it is the one selected.
  Save replaces an entry where it stands, so re-saving an unchanged entry rewrites the file byte
  for byte.

**Turns and flips.** "Turn or flip the selected one" (⟲ 90°, ⟳ 90°, Flip ↔, Flip ↕, Reset) acts
on the selected option or slot, as the picture looks now. A quarter turn of a non-square cell is
refused. Thumbnails and both previews draw cells turned and flipped.

**Placement.**
- A `Pixels per tile` numeric with a "stretch to footprint" checkbox (checked, the default, saves
  no value), and a pivot with a "Pivot at bottom-centre" default, two numerics and a "Set pivot"
  toggle: with it on, the next click on the preview sets the pivot to that pixel.
- **Preview panel**: the selected option drawn at zoom on a grid of map tiles, a crosshair at the
  pivot, the footprint tile shaded and the reach box as a boundary line. A footprint picker (1x1,
  2x2, 3x3) redraws it at that size with the draw-time clamp applied. A wall's top is drawn raised
  onto its face, with the face drawn faintly beneath.
- Save refuses anything outside the reach box at 1x1 -- a wall's top counted on top of its face --
  with the same message the game's load check gives. That is the one refusal kept on purpose: the
  game would otherwise fail at startup.

**Linked sets and link groups.**
- **Slots**: a 4×4 grid laid out as a 3x3 room, a vertical run, a horizontal run and a pillar, each
  slot labelled with a small diagram whose bars show its joined sides (hover for the words; no bit
  numbers). Select a slot, then click or drag on the sheet to fill it. A filled slot shows its
  piece as a thumbnail.
- **Copy to...** copies the selected slot's piece to any slots ticked in a dialog, "as it is",
  turned or mirrored. **Fill empty...** fills every empty slot from a filled one by mirroring
  left-right (on by default), top-bottom, or turning -- breadth first, fewest steps, never
  overwriting -- so a set can be authored from as few as six pieces. Art lit from above usually
  survives only a left-right mirror, hence the default. **Add variant set** adds another 16-slot
  page (the `k` above); **Clear slot** and **Remove this set** undo.
- **Link group** sits above the slots: a combo box of every group already in the manifest,
  editable to type a new one, defaulting to (and following) the entry's name, with an "Also in
  this group:" line listing the other entries in it. It isn't the name: the name identifies the
  entry, the group decides which other walls this one joins (a later door frame joins stone walls
  by sharing `stone-wall`).
- **Link preview**: a fixed test layout -- horizontal and vertical runs, L, T and + junctions, a
  3×3 block and a lone pillar -- drawn with the current slots by the same mask the game uses, each
  piece at its own placement, row by row top to bottom like the map's row walk. For a wall it draws
  the face on every wall tile open to the south and the top raised onto it, so a tall face's
  occlusion of the rows behind shows before anything runs in game. Stretched pieces that aren't
  square get a note: the game squeezes them into one tile too.

**Deferred until a sheet needs them:** animation entries (a frame sequence with timing, which
"Walking and action animations" will need), and grid margin/offset for sheets with padding between
cells. Not part of this plan.

The builder has no test project. Its logic that matters stays in small plain classes, which can
move under test if it grows: `EntryRules` (hit-testing, duplicates, span fit), `LinkMask` (mask
order and display layout), `SlotFiller`, `SpritePlacementRules` (reach, raise, clamp, face
height) and `ManifestCell`'s turn/flip algebra. A scratch harness checked the turn/flip algebra on
all 8 orientations and that every filled slot's piece joins exactly its slot's sides. The round
trip is covered from the Game side (see Tests).

## Decisions (approved)

1. **Wall art.** You pick the wall's face and top cells in SpriteManifestBuilder (grey stone to
   match today), as one `StoneWall` wall entry: the face as its 4 pieces (standalone required), and the top directly as its 16 linked
   slots in group `stone-wall`. Authored in Phase 1.
2. **Four-bit linking, not a 47-piece blob.** Inner (concave) corners use the plain piece. Upgrade
   only if they read wrong in game.
3. **Off-map and unloaded neighbours count as linked.**
4. **Linking and link groups live in the sprite manifest**, authored in the builder, not on
   `TerrainDefinition`.
5. **Spans are chosen up front, per part.** A click places a span-sized option or fills a slot
   from the clicked cell; a drag places exactly the dragged cells; there is no mode switch. A cell
   that doesn't fit its part's span is marked, not refused. (Replaces "click adds a 1x1 option"
   and "save refuses mixed spans".)
6. **Tall creatures in front of the player don't fade.** The TODO scopes the fade to tall terrain.
   A big creature south of the player covering them can join the fade later by adding its drawn
   rectangle to the overhang test.
7. **The fade is 0.7 alpha and instant** (0.4 read as too faint in game).
8. **Clicks stay tile-based.** A tall sprite's head isn't a click target for its entity.
9. **Corpses keep a top-row anchor** (floor-level). A corpse's own sprite placement can still
   overhang upward over the row above. That is accepted, and "Blood pool under dead entities" can
   revisit floor decals.
10. **A wall is one entry with a face and a top, authored in separate selection spaces.** Face =
    options on the wall's tile, top = a linked set on the tile above the face. Not two entries
    named from `TerrainDefinition` (no `TopSpriteName`), and not one tall linked sprite per cell.
11. **The face's height is its placement; the top is raised by it; face + top ≤ 3 rows.** A face
    may be two rows tall with a one-row top. Its upper row is a live overhang wherever it covers an
    open tile, and fades like a top.
12. **Faces have standalone, left end, right end and middle pieces** (`SpriteLinking.Horizontal`): the piece for a wall on neither, the right, the left or both sides; only the standalone piece is required, and an empty one falls back to it. Data and builder in Phase 1, drawn joined in Phase 6.
13. **Cells can be turned and flipped** (flip, then a clockwise turn; quarter turns only on square
    cells), and the builder fills empty slots by mirroring left-right by default, top-bottom and
    turns on request.
14. **Unfinished work saves.** Empty slots (as `null`) and cells that don't fit the span are saved
    as they are; the game loads them, treating an empty slot as a miss. Only a reach-box breach is
    refused, because the game would fail at startup.

## Phases

Each phase ends with a manual in-game check before the next starts.

1. **SpriteManifestBuilder and the manifest format.** This comes first so that every later phase
   has real sprites to test with.
   - Build (builder): everything in "SpriteManifestBuilder" above:
     - the Options, Linked set and Wall kinds, the wall's separate Face/Top selection spaces;
     - per-part span, click/drag, numbered outlines, misfit warnings, partial saves, Delete;
     - turn/flip, "Copy to…" with "Copy as", "Fill empty…", "Add variant set";
     - pixels per tile, pivot, "Set pivot", and the placement preview with the reach box (a top
       raised onto its face);
     - the link group above the slots with "Also in this group:", and the link preview drawn in
       row order with the wall's face.
   - Build (game, data only, no drawing changes):
     - `SpriteManifestCell` gains spans, `Rotation` and flips; `SpriteManifestEntry` gains
       placement, `Linking`, `LinkGroup` and `Top` (`SpriteManifestPart`); `ToSprite` honours spans;
     - load validation: linked parts in whole sets of 16 (empty slots allowed), quarter turns only
       on square cells, nothing outside the reach box at 1x1, a top raised by its face's height;
     - the lookups return placement beside the cell (`TryGetFirst`/`TryGetCell`, and
       `CountTopCells`/`TryGetTopCell`), read by nothing yet.

     This is so that whatever the builder writes loads, and is checked, from day one.
   - Art: author Player and Goblin placement, the `StoneWall` wall entry (face as standalone, left, right and middle pieces, top as a
     16-slot linked set in group `stone-wall`), and a `Shop-2x2` entry as one 2x2 option. The
     existing `"Wall"` entry stays until the walls phase switches over.
   - Check (builder):
     - a click places a span-sized option and a drag places the dragged cells; clicking inside an
       option removes it;
     - a cell that doesn't fit its span is marked on its slot, its option row and its entry, and
       an unfinished set saves and reopens as it was;
     - an entry outside the reach box, and a wall whose face and top pass three rows, are refused
       with a reason;
     - face and top keep separate cells, spans and placement when switching between them;
     - a face saves with only its standalone piece, is refused without one, and the link preview
       draws left ends, right ends and middles on runs, standalone pieces on pillars, and the
       standalone piece wherever another is empty;
     - "Fill empty…" from six pieces with turns fills all 16, and the link preview joins the test
       layout cleanly, faces along exposed south edges and tops raised onto them;
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
     the reach-box clamp), `DrawVisual`, cell turns and flips passed to `SpriteBatch`, and the
     health-bar and charging-badge anchoring. Delete TODO "Per-entity sprite scale".
   - Check:
     - the player is larger with feet on its tile, and the goblin is smaller;
     - 2x2 and Huge goblins draw the same art scaled up, a Huge one inside its own 3x3, and they
       match the builder's footprint preview;
     - the player's head overlapping the row above draws over a goblin there;
     - a goblin south of the player draws over the player's feet, not under them;
     - a turned or flipped option draws turned or flipped, as in the builder;
     - inventory and hotbar icons are unchanged.

     Point `Shop` at `Shop-2x2` and spawn one 2x2. If it looks right, "Multi-tile sprites" (Low)
     is done; delete that TODO.
4. **Wall faces and tops.**
   - Build: registry top variants (a linked top draws its fully-joined piece for now, an empty slot
     too), the face's height and the top's raise, `TryGetStructureTopVisual`, the cache's second
     pass, the overhang list (tops and tall faces' live rows), and the walk's per-row overhang draw.
     Point `StoneWall` at the `StoneWall` wall entry, and remove the old `"Wall"` entry.
   - Check:
     - walk behind (north of) a wall and the top -- and a two-row face's upper row -- covers you;
       walk in front and you cover the face;
     - thick walls read as tops with one face row;
     - check at zoom, during drag-scroll, across a neighborhood boundary while streaming, and after
       Admin Regenerate.
5. **Fade.**
   - Build: watched rectangles and the overhang fade.
   - Check:
     - the wall top, and a tall face's upper row, go translucent over the player and stay opaque
       elsewhere;
     - the same happens for a Detail-inspected goblin walking behind a wall;
     - in Basic mode it doesn't happen for the goblin.
6. **Joined-up wall tops and face end caps (game and builder).**
   - Build: the registry's per-type link-group int, the mask computation shared by the cache top
     pass and the overhang-list build, `16 × k` variant selection, and the widened visibility test.
     Face end caps: the 2-bit face mask (a same-group wall left or right, off-map counting) and the standalone fallback, shared by the cache and the overhang list.
   - Check:
     - the in-game walls match the builder's link preview for the same slots;
     - a straight horizontal run, a vertical run, a thick block, an isolated pillar, and T, L and +
       junctions all join cleanly;
     - a face run ends in caps, including where the wall turns north;
     - Admin-spawned or removed walls re-link their neighbours;
     - a wall crossing a neighborhood boundary has no cap before or after the neighbour streams in;
     - joined tops still fade over the player.
7. **Tile tinting (game and builder).** Added after Phase 3, at the end of the feature so the notes
   cover it.
   - **What carries a tint.** A tint is a colour multiplied into the sprite (`#RRGGBB` or
     `#RRGGBBAA`, omitted when white), the same multiply `SpriteBatch` already applies.
     - *An option* has one tint, applied equally to every cell it spans: a 2x2 option is one picture,
       so its four cells can't differ.
     - *A linked set or a wall* has one tint per variant set, applied to every slot of that set, so
       a second set can be the same pieces in another colour (moss-stained stone) without new art.
       A wall's face and top sets are tinted separately.
   - **Data.** `SpriteManifestCell` gains `Tint` (string?, omitted when unset); the builder writes a
     set's tint onto each of its cells, so the game reads one per cell and has no set-level shape to
     learn. A `Color Tint` beside the transform on `ManifestSprite` and `SpriteView` carries it
     to the renderer, which multiplies it into the tint it already applies (a corpse's grey, a
     Phasing occupant's alpha). Load validation refuses a malformed colour, naming the entry. Icons
     read it too: a tinted option looks the same in the inventory as on the map.
   - **Builder.** A "Tint..." button (colour dialog) and a "Clear tint" beside the turn/flip row:
     for options it sets every selected option's tint; for a linked set or a wall part it sets the
     current variant set's. The sheet outlines, slot thumbnails, placement preview and link preview
     all draw tinted, and the options list and set selector show a colour swatch.
   - **Relation to TODO "SpriteDefinition: tint on the sprite itself".** This is that entry's
     mechanism. Moving `ItemDefinition.SpriteTint` (loot-box rarity chests) onto tinted manifest
     entries is a follow-up that entry keeps; delete the entry only if that move is done here.
   - Check:
     - a tinted option, a tinted 2x2 option and a tinted variant set look the same in the builder's
       previews and in game;
     - two variant sets of one wall, tinted differently, both appear across a long wall;
     - Clear tint restores the art exactly, and untinted entries re-save byte-identical.
8. **Notes.**
   - Add an `IMPLEMENTATION-NOTES.md` entry covering the row-walk rule, anchor rules, placement,
     the reach box and the Huge size cap, walls as face + top (height, raise, three-row budget), the
     static/live overhang split, turns and flips, tinting, and linking (the mask is computed, not
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
  - A turn or flip reaches `SpriteBatch` as the matching rotation and `SpriteEffects`.
  - The health-bar anchor clamps to the footprint top.
- **Footprint cap:** a blueprint whose default `Size` is 4 on either axis fails `ResolveAll`, naming
  it, and a mod with one fails its dry run. A `SpawnRequest` with `Size` 4 throws. 3x3 is accepted.
- **`SpriteManifest`** (landed in Phase 1, `SpriteManifestFormatTests`):
  - Entries without the new fields deserialize unchanged: span 1x1, no placement, not linked, no top.
  - Placement, spans, turns, flips, `Linking`, `LinkGroup` and `Top` round trip, and are omitted
    when unset.
  - A 2x1-span option's source rectangle is two cells wide from its origin cell.
  - A linked part with 0, 15 or 17 cells throws at load, naming the entry; an unfinished one with
    `null` slots loads; an options entry with a `null` cell throws.
  - A rotation other than 0/90/180/270, or a quarter turn of a non-square cell, throws.
  - At a 1x1 footprint, an entry whose placement reaches 3 rows above, 3 columns to one side, or
    below the footprint throws at load, naming the entry and its reach. Exactly 2 rows and 2
    columns loads.
  - A wall's face height counts every row it reaches; a top on a face that together pass three
    rows throws naming "(top", and 1+1, 2+1 and 1+2 load.
  - An omitted `LinkGroup` (entry or top) defaults to the entry name.
  - The builder's authoring-only spans on an entry are skipped.
  - The checked-in `Content/SpriteManifest.json` loads, every entry within the reach box, which
    catches a builder that wrote a shape the game can't read.
  - `SpriteManifestTests`' Wall rows become the `StoneWall` wall entry (Phase 4, when it exists).
- **`TerrainRegistry`:** the top variant follows `Variant` like the face, and a definition whose
  entry has no top gives none. For `MapViewQuery.TryGetStructureTopVisual`: none with no structure,
  none with no top.
- **Overhang list build** (pure, from an `IMapViewQuery` fake):
  - A top over open floor goes to the list; one over another wall, or over an off-map tile, goes
    to the cached pass.
  - A two-row face whose upper row is open goes to the list; a one-row face never does.
  - A face draws only where the south neighbour isn't a same-group wall.
  - A top is raised by its face's height whether or not its tile draws a face.
  - The list is sorted by row.
- **Walk with overhangs:** a wall in row r yields its face and top after row r-1's occupants and
  before row r's.
- **Fade:**
  - The player's rectangle intersecting an overhang (top or tall face) gives the fade alpha, and
    one next to it gives full alpha.
  - The inspected entity counts only in Detail or Admin mode.
  - A 2x2 inspected entity counts on every tile.
- **Wall linking**, the pure mask function over an `IMapViewQuery` fake:
  - Each of the 16 neighbour layouts gives its mask.
  - A same-group structure links, and a different-group one doesn't.
  - An off-map or unloaded neighbour links.
  - The mask is read from the wall's own neighbours, not those of the tile its top draws on.
  - `Variant % k` picks the group, and a 16-cell part always gives group 0.
  - Face end caps: each of the 4 east/west layouts gives its piece (standalone, left end, right end, middle), a missing piece falls back to standalone, and a same-group wall to either side links whether or not it shows a face
    itself.
- **Linked `TerrainRegistry`:** two definitions whose tops share a `LinkGroup` get the same
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
