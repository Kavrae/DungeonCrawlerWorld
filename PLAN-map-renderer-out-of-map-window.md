# Map Renderer out of MapWindow

(Child 3 of `EPIC-sprites-ui-engine-and-dev-tools.md`. Started 2026-10-13; decisions confirmed 2026-10-13.
Phases 1 and 2 landed 2026-10-13, phase 3 2026-10-14.)

## Context

What exists today:
- **`MapWindow` (1354 lines) is three things at once**: a `Window` routing input (hotkeys, clicks, right-drag,
  context menus, pause), the game's overlays (targeting highlights, windup charge fills, Dodge's WASD hints, the
  selection and followed-entity glows, floating text), and the whole map renderer: the flat fill, the terrain
  image (`DrawTerrainDirectly` / `DrawStructures` into `_terrainCache`), the row walk (`DrawOccupants` over
  `MapDrawOrder`: corpses, the Tiny sub-grid, Blocking and Phasing occupants, overhangs with `OverhangFade`, layer
  badges), per-entity cues (health bar, loot bag badge, charging badge, dodge glow) and the aura glow
  (`DrawGlowOverlay` through `AuraGlowBlend` into `_glowCache`, with `FullGlowPower` / `GlowOpacityMultiplier`).
- **The planners are already out**: `MapDrawOrder`, `MapWallPlan`, `MapTerrainCells`, `OverhangFade`,
  `MapTileLayerCache`, `AuraGlowBlend` are pure or self-contained classes MapWindow drives. What is still inside
  MapWindow is the code that turns their output into draws, the caches' lifetime and their invalidation.
- **Invalidation is hand-kept at every camera call site.** Two caches (`_terrainCache`, `_glowCache`) are
  invalidated from `ChangeLayer`, `UpdateZoomLevel`, `CycleZoom`, `UpdateScrollPosition`, `CenterCameraOn`
  (through `ApplyCameraScrollToCaches`), the `_scrollLimitBounds` check in `Update`, four `EventBus` subscriptions
  (`TerrainChangedEvent`, `StructureChangedEvent`, and `TerrainLoadedEvent` / `TerrainUnloadingEvent` through
  `InvalidateTerrainCachesIfVisible`), and the aura `Version` check. A new way to move the camera has to remember.
- **`MapCamera` reads `Game.World.World`**, not `IMapViewQuery` -- only for `Map.Bounds` (scroll limits) and
  `IsOnMap` (hover hit-testing). It also carries follow-the-player bookkeeping (`FollowsPlayer`,
  `LastKnownPlayerPosition`, `ResumeFollowingPlayer`, and `ApplyDrag` switching follow off) that only MapWindow
  reads. `ActionTargetingController` (hover/click hit-testing) and `FloatingTextRenderer` (zoom, `TileToScreen`,
  viewport culling) share the one camera through `ShellServices`.
- **Tile rectangles for overlays** come from MapWindow's private `TryGetTileRectangle` (tile -> on-screen rectangle,
  false off the grid), shared by every highlight.
- Tests: `MapWindowTests` (input through a real headless MapWindow, `TestMapWindows`), `MapCameraTests` (over a
  `TestWorlds` World), the planners' own tests over `FakeMapView`, and `MapWindowArchitectureTests` (MapWindow and
  floating text name no Game component type). Nothing draws headlessly: there is no GraphicsDevice in tests, and
  MapWindow's caches stay null there.
- TODO.md "Review MapWindow for properties that belong on MapViewState instead" (Presentation, Low) is folded in.

## Design

### Three pieces, one host

| Piece | Holds | Reads |
|---|---|---|
| **`MapCamera`** | Scroll, zoom, drag offset, tile grid, scroll limits, screen <-> tile math, tile rectangles | `IMapViewQuery` (`Bounds`, `IsOnMap`) |
| **`MapRenderer`** (new) | The world as drawn: fill, terrain image, row walk with per-entity cues, glow; both caches and their invalidation | `IMapViewQuery`, `AuraPowerView`, a camera, the manifest (`SpriteSheetService`), the shared renderers and fonts |
| **`MapGameOverlay`** (new) | What the player is doing on the map: targeting and footprint highlights, windup charge fills (the player's and every telegraph), Dodge's WASD hints, the selected tile and followed entity glows | `MapViewState`, `ActionTargetingController`, `IMapViewQuery`, the camera |
| **`MapWindow`** | Input routing, pause, camera follow, context menus, layer clamping, hosting the three above plus `FloatingTextRenderer` | -- |

The map tab (child 6) builds a `MapCamera` and a `MapRenderer` over its authored layout's `IMapViewQuery` and
draws its own overlays (selection, placement ghost) with the camera's tile rectangles; it never sees
`MapGameOverlay` or `MapWindow`.

### MapCamera reads the view, and is only a viewport

- `MapCamera(IMapViewQuery mapView)` replaces `MapCamera(World world)`: scroll limits from `mapView.Bounds`,
  hit-testing through `mapView.IsOnMap`. `ShellBootstrapper` passes `gameSession.Views.MapView`.
- **It refreshes its own scroll limits**: `MapCamera.RefreshScrollLimitsIfBoundsChanged()` compares `Bounds`
  against the bounds it last computed limits from and recomputes them when they differ (the sliding window),
  returning whether it did. MapWindow's `_scrollLimitBounds` goes.
- `TryGetTileRectangle(int mapX, int mapY, out Rectangle)` moves from MapWindow to the camera -- it is camera math,
  and every overlay (the game's and the map tab's) needs it.
- **Follow-the-player leaves the camera**: `FollowsPlayer`, `LastKnownPlayerPosition` and `ResumeFollowingPlayer`
  become MapWindow's (the only reader). `ApplyDrag` no longer touches follow; MapWindow stops following in
  `OnRightDragAction` once the drag has moved (a drag "starts" on every right-press, taps included). A map tab's
  camera has no player to follow.
- `CenterCameraOn`, `UpdateScrollPosition`, `ApplyDrag` and `EndDrag` keep returning the applied delta, but no
  caller needs it for caches any more (below); MapWindow's drag still commits through `UpdateScrollPosition`.

### MapRenderer: the world, drawn from the view

- **Construction**: `MapRenderer(IMapViewQuery, AuraPowerView, MapCamera, ElementPoolService, FontService,
  SpriteSheetService, SpriteRenderer, LabelRenderer)`. It loads its five map fonts and the loot bag sprite at
  construction (today's `Initialize` work) and owns `MapTerrainCells`, `MapWallPlan`, `MapDrawOrder`,
  `AuraGlowBlend` and the watched-rectangle buffers. `ElementPoolService` supplies the `SpriteBatch`, the unit
  rectangle and, once there is one, the `GraphicsDevice` -- the same source every element draws with; child 4
  moves that type to UIEngine, which Presentation will reference.
- **Two calls a frame**, mirroring today's split:
  - `Update(int mapLayer)` -- outside any SpriteBatch pass: builds the caches the first time a device exists,
    decides whether each is stale, and re-renders them (today's `EnsureRendered` calls, unchanged).
  - `Draw(SpriteBatch, int mapLayer, IReadOnlyList<int> watchedEntityIds)` -- the fill, the terrain image (or the
    uncached fallback), the row walk, the glow image (or its fallback). Draws nothing that isn't the world.
- **Watched entities are an input**: which entities overhangs fade over is the host's statement (the game: the
  player, plus the inspected entity in Detail/Admin -- `OverhangFade.CollectWatchedEntities`, now called by
  MapWindow). The renderer only turns ids into drawn rectangles. The map tab passes none, or its selection.
- **Per-entity cues stay in the row walk** (health bar, loot bag badge, charging badge, dodge glow): they must be
  drawn in the entity's turn so a later row covers them, and they read only `IMapViewQuery`. The "no health bar
  for the player" rule keeps reading `IMapViewQuery.PlayerEntityId` (-1 in a layout with no player).
- **The glow's opacity curve moves in** (`FullGlowPower`, `GlowOpacityMultiplier`), as child 2 left it to.
- **Caches invalidate themselves from what they were drawn for.** Each cache remembers the key it was rendered
  against -- `MapImageKey(Point Scroll, int Columns, int Rows, Point TileSize, int MapLayer)` -- and `Update`
  re-renders when the camera's current key differs, whatever moved the camera. So the camera calls in MapWindow
  (`ChangeLayer`, the zoom calls, scrolling, follow) stop invalidating anything, and a new way to move the camera
  can't forget to. `MapTileLayerCache` already compares the grid size; it takes the whole key instead. A sub-tile
  drag changes no key (it is the blit offset), exactly as today. On top of the key:
  - the glow image re-renders when `AuraPowerView.Version` changes (as today);
  - **terrain changes are told, not observed**: `InvalidateTerrain()` and `InvalidateTerrainIn(MapBounds area)`
    (the overscan-widened overlap test, today's `InvalidateTerrainCachesIfVisible`, moved in as is). Both drop
    both images, as today. The renderer takes no `EventBus`: MapWindow forwards the four terrain events, and the
    map tab calls the same methods for an edit it makes -- or gets the same events, if its edits go through
    `World.SetTerrain`.
- **The overlap test is pure and on the renderer** (`MapRenderer.TouchesDrawnArea(MapBounds area, MapImageKey
  key)`, static), so it is tested without a device.
- What stays exactly as it is: the draw order, every constant's value, `MapDrawOrder`/`MapWallPlan`/
  `MapTerrainCells`/`OverhangFade` and their tests, the headless fallback (no device: the uncached path), and the
  "Map / Terrain cache rebuild" frame cost row (name unchanged -- the benchmark skill and F3 read it).

### MapGameOverlay: the player's intent, drawn over the world

- Moves out of MapWindow unchanged: `DrawTargetingHighlights`, `DrawChargeFillHighlight`,
  `DrawMaskedTileHighlight`, `DrawDodgeDirectionalHints` (and its WASD table), `DrawSelectedTileHighlight`,
  `DrawFollowedEntityHighlight`, `DrawSelectedTileGlow`, with `TargetSelectionMaskAlpha` and
  `ChargeBackdropAlphaFraction`. One `Draw(SpriteBatch)`; tile rectangles from the camera.
- It needs the badge font (Dodge's hints): loaded from `FontService` at construction, like the renderer's.
- `FloatingTextRenderer` stays its own class, drawn by MapWindow after the overlay, as today.

### MapWindow: input, state and hosting

- `DrawContent` = `_renderer.Draw(...)`, `_overlay.Draw(...)`, `_floatingTextRenderer.Draw(...)`.
- `Update` = targeting tick, floating text update, `_camera.RefreshScrollLimitsIfBoundsChanged()`, camera follow, hover tracking,
  `_renderer.Update(layer)`.
- Keeps: hotkeys (pause, Home, zoom, layers, movement, hotbar), clicks, right-drag, context menus and Inspect,
  `IsAdjacentToPlayer`, the settable delegates, `CurrentMapLayer` clamping.
- Constructor loses `LabelRenderer`/`SpriteSheetService`/`SpriteRenderer`/`AuraPowerView` (now the renderer's,
  built in `ElementFactoryRegistry`) and takes the built `MapRenderer` and `MapGameOverlay` instead. Both are built
  once per session beside `FloatingTextRenderer`, as that is today.

### The MapViewState review (TODO.md, folded in)

What MapWindow still holds once the renderer and overlay are out, and where each belongs:

| Field | Verdict |
|---|---|
| `IsPaused` | Stays. One reader (`GameLoop`), toggled only by the map's Space; IMPLEMENTATION-NOTES "Pause modality" keeps it deliberately separate from menu mode. Not map-view state. |
| Camera follow (`FollowsPlayer`, last-known player position) | Moves **in** from `MapCamera` (above); nothing else reads it. |
| `_scrollLimitBounds` | Gone: the camera's own (above). |
| `_claimedKeysThisFrame` | Stays: per-call scratch for one hotkey pass. |
| `IsTextInputFocused`, `OnCorpseClicked`, `OnShopClicked`, `OnInspectionOpened` | Stay: late wiring, not state. |
| Caches, fonts, planners, glow version, loot bag sprite, watched buffers | The renderer's / the overlay's. |

Nothing moves to `MapViewState`: everything another window reads is already there (layer, selection, armed
action, targeting mode, inspection) or on the shared camera. The TODO item is deleted with that answer recorded in
this plan.

### Where the files live

`Presentation/MapRendering/` (namespace `Presentation.MapRendering`) for the map's own types: `MapRenderer`, `MapGameOverlay`,
`MapCamera`, `MapDrawOrder` (+ `MapDrawItem`, `MapOverhang`), `MapWallPlan`, `MapTerrainCells`,
`MapTileLayerCache`, `OverhangFade`, `AuraGlowBlend`, `ZoomLevel`.
`MapWindow` and `MapViewState` stay in `Presentation.UI` (a window and shared UI state). `MapVisual` and
`TerrainSpriteSet` stay in `Presentation.Rendering`: they are what `SpriteManifestResolver` hands out (an item icon
reads `MapVisual` too), and moving them would point Rendering at MapRendering. This makes child 4's split read
off the folder: `Presentation/MapRendering` stays in Presentation, and none of it goes to UIEngine. Moved with
`mv`, never `git mv`. Not `Presentation.Map`: that namespace would hide the type `Game.World.Map` in every
`Presentation.*` and `Tests.Presentation.*` namespace (CLAUDE.md's namespace rule).

### Out of scope

- "Aura glow shouldn't tint wall tops" (TODO.md, Low): the glow is one image blitted after the row walk, and a top
  is drawn in the walk (overhangs) or in the terrain image (cached tops). Fixing it needs either a third image
  (tops split from the terrain image, glow between) plus a per-tile top coverage mask for the live tops, or a
  shader pass -- child 7, where the glow moves onto the uber effect anyway. The renderer makes either a change in
  one class.
- A `DrawsEntityCues` option (a map tab showing bare sprites): add it when the tab wants it.
- "Camera dead zone", "Minimap", "Fog of War", "Debug drawing": unchanged in substance; their text is repointed
  from MapWindow to the renderer / camera / overlay where it names a method.
- Moving `ActionTargetingController` or `PlayerMovementController`: input, MapWindow's side, unchanged.

## Decisions (confirmed 2026-10-13)

1. **Three pieces**: `MapRenderer` (the world, including per-entity cues), `MapGameOverlay` (targeting, telegraphs,
   selection), and MapWindow as host -- rather than leaving the overlays in MapWindow.
2. **Caches key themselves** on (scroll, grid, tile size, layer) and stop being invalidated by camera call sites;
   terrain changes are told through `InvalidateTerrain` / `InvalidateTerrainIn` -- the renderer takes no
   `EventBus`.
3. **`MapCamera` over `IMapViewQuery`**, owning its scroll-limit refresh and tile rectangles; follow-the-player moves
   to MapWindow.
4. **`Presentation/MapRendering/` folder** for the map's types.
5. **Nothing moves to `MapViewState`**; the TODO item is deleted with the table above as its answer.

## Phases

Each phase builds, passes `dotnet test`, and is checked in-game before the next.

### Phase 1 -- the camera reads the view; map files to Presentation/MapRendering
- `mv` the map types listed above into `Presentation/MapRendering/`, namespace `Presentation.MapRendering`; fix usings (Presentation,
  DungeonCrawlerWorld, Tests). No behaviour change in this step.
- `MapCamera(IMapViewQuery)`; `MapCamera.Update()` refreshes scroll limits on a bounds change; `TryGetTileRectangle`
  moves onto the camera (MapWindow's draw methods call it); follow state moves to MapWindow, which stops following
  in `OnRightDragStartAction`. `ShellBootstrapper` and `TestMapWindows` pass the map view.
- Tests: `MapCameraTests` build over `FakeMapView` and over a real `MapViewQuery` (the centred-map cases keep a
  World behind a view); new: limits follow a `Bounds` change through `Update()` and report it, `TryGetTileRectangle`
  inside / outside the grid and under a drag offset. `MapWindowTests`' follow cases
  (`RightMouseDrag_DecouplesCameraFromPlayer_UntilHomeRecouples`, Home, layer sync) pass unchanged.
- **In-game:** walk -- camera follows; right-drag -- it stops following, Home resumes; zoom in and out at a map
  edge; walk until the window shifts -- scrolling reaches the new neighborhoods; hover and click targeting still
  land on the tile under the cursor.
- As built:
  - The namespace is `Presentation.MapRendering` (see "Where the files live"). The planners' tests and
    `AuraGlowBlendTests` moved with them, to `Tests/Presentation/MapRendering` (`Tests.Presentation.MapRendering`).
  - The camera's method is `RefreshScrollLimitsIfBoundsChanged()`, replacing `RefreshScrollLimits()`; `Initialize`
    records the bounds, so the first frame no longer invalidates the caches for an unchanged map.
  - Follow stops in `OnRightDragAction` on a moving drag (as `ApplyDrag` did), not on drag start.
  - Tests that only need a camera build one through `TestMapCameras.Over(world)`, a real `MapViewQuery` over the
    test's world. New camera tests: the bounds refresh reports a change and no change; a camera over `FakeMapView`;
    `TryGetTileRectangle` inside, outside and mid-drag.

### Phase 2 -- MapRenderer
- `MapRenderer` as designed: construction, `Update`, `Draw`, `InvalidateTerrain`, `InvalidateTerrainIn`, the static
  `TouchesDrawnArea`; `MapImageKey`; `MapTileLayerCache` re-renders on a key change. Everything from the fill to
  the glow moves out of MapWindow unchanged in order and constants.
- MapWindow: builds nothing it draws the world with; forwards the four terrain events; drops
  `InvalidateTerrainCaches`, `ApplyCameraScrollToCaches` and the invalidation in `ChangeLayer` / zoom / scroll /
  follow; collects watched entities and passes them to `Draw`. `ElementFactoryRegistry` and `TestMapWindows` build
  the renderer.
- Tests: `MapImageKey` -- a whole-tile scroll, a zoom, a layer change each change it, a sub-tile drag doesn't;
  `TouchesDrawnArea` -- inside, the overscan margins each side (in at the edge, out one past), below the grid by
  `MaximumRowsAbove + 1`; a renderer builds and runs `Update` headlessly over `FakeMapView` (no device: no caches,
  no throw); `MapWindowArchitectureTests` scan `MapRenderer` and `MapCamera` for component types, and a new test
  holds that neither names `Game.World.World`, `EventBus` or `ComponentManager` (the renderer and camera read only
  views).
- **In-game:** every map at Team, Neighborhood and Borough zoom looks as before -- floors, walls with faces and
  tops, overhang fade behind a wall over the player and an inspected goblin, corpses with loot badges, Tiny
  grids, Phasing translucency, layer badges, health bars, charging badges above tall sprites, dodge glow, lava /
  Holy Ground / shrine / Toxic Idol glow; Page Up/Down; right-drag smoothly with no stale edge; walk across a
  window shift and watch neighborhoods stream in at the edge with no stale terrain; Admin "Regenerate" a visible
  neighborhood; Scroll of Torch's light moves with its holder. F3: "Map / Terrain cache rebuild" fires on camera
  moves only, not every frame.
- As built:
  - `TouchesDrawnArea(MapBounds area, Point scroll, int tileColumns, int tileRows)` takes the grid rather than a
    key: the overlap doesn't depend on the layer or tile size.
  - `MapTileLayerCache.Draw` also takes the key and refuses an image rendered for a different one, so a camera
    moved between `Update` and `Draw` draws the uncached path for that frame -- what an invalidation did before.
  - The image passes take the key they render (`Action<SpriteBatch, MapImageKey>`), held as delegates made once.
    The row walk reads the grid from the same key, and the camera only for the drag offset (`TileOrigin`).
  - MapWindow still drops both images when the camera's scroll limits change (`RefreshScrollLimitsIfBoundsChanged`
    returning true): a bounds change is a map change the key can't see. `ApplyCameraScrollToCaches`, the
    `CenterCameraOn` wrapper and the invalidation in `ChangeLayer`/zoom/scroll are gone. MapWindow keeps only the
    badge font (Dodge's hints) until phase 3.
  - `MapWindowArchitectureTests`' scanner takes the forbidden-type test as a parameter; a new scanner test checks it
    finds the World, the store and the bus. `MapRendererTests`: image keys (scroll, zoom, layer change it; a
    sub-tile drag doesn't), the overlap test at each edge, a headless renderer over `FakeMapView`.

### Phase 3 -- MapGameOverlay; docs
- `MapGameOverlay` as designed; MapWindow's `DrawContent` is the three draws.
- Tests: `MapWindowArchitectureTests` scan `MapGameOverlay`; `WindupTelegraphTests` / `ActionTargetingController*`
  tests unchanged (they read the controller, not the draw).
- Docs: CLAUDE.md Presentation "Map drawing" bullet (the renderer over `IMapViewQuery` + camera + manifest; the
  overlay; MapWindow hosts; caches key themselves, terrain changes are told); IMPLEMENTATION-NOTES "Tall sprites,
  wall faces and tops, and the sprite manifest" (names `Presentation.MapRendering.*`, "a pure planner MapWindow renders" ->
  the renderer; the static-versus-live paragraph's invalidation sentence); `MapTileLayerCache`'s remarks (its
  invalidation points); the epic's child 3 marked landed; TODO.md -- delete "Review MapWindow for properties that
  belong on MapViewState instead"; repoint "Aura glow shouldn't tint wall tops", "Camera dead zone", "Fog of War",
  "Debug drawing", "Red X marker over dead entities" and the corpse greyscale note from MapWindow methods to the
  renderer / overlay.
- **In-game:** arm a ranged action -- targetable tiles green, the hovered footprint dark green over them, a Burst's splash
  past range; confirm a Delayed action -- its tiles fill bottom-up; a goblin's windup telegraph red/yellow; Dodge's
  WASD hints; click a tile -- selection glow; Inspect a goblin -- the glow follows it, a Huge entity's whole
  footprint; floating damage text over all of it.
- As built:
  - `MapGameOverlay.Draw(SpriteBatch, Texture2D unitRectangle)` takes the unit rectangle from its host; it keeps its
    own player-position read for Dodge's hints. MapWindow no longer loads any font or holds a `LabelRenderer` of
    its own (it still passes one to `Window`).
  - Built in `ElementFactoryRegistry` and `TestMapWindows` beside the renderer.
  - Docs also: a "Where the map is drawn" paragraph in IMPLEMENTATION-NOTES' tall sprites section; CLAUDE.md's
    auras and occupancy bullets name `MapRenderer`. Older IMPLEMENTATION-NOTES entries that name `MapWindow`
    methods describe past work and were left as written.
  - Found in phase 2's in-game check, not fixed (user's call): at Borough zoom a white mark shows south of every
    wall -- fixed-size map fonts overflowing 9 px tiles (Stone floor's `][` under the wall). Predates this plan;
    TODO.md "Map glyphs overflow their tiles when zoomed out" (Presentation, Low).

## Tests (summary)

- Camera over a view: scroll limits follow `Bounds`, tile rectangles, follow behaviour unchanged through MapWindow.
- Renderer: image keys, the streaming overlap test, headless construction and update; architecture -- the
  renderer, camera and overlay read the world only through views.
- Everything the planners already test is untouched.
- No benchmark: the headless benchmark doesn't run Presentation. The live check is F3's "Map / Terrain cache
  rebuild" row -- it should fire no more often than before (keying on the camera rebuilds on the same events the
  call sites did).
