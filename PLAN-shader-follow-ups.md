# 7.3: Shader follow-ups -- glow rings, clipping, a per-tile targeting overlay, tilemap terrain

(Epic child 7, after 7.2 (`PLAN-ui-on-the-sprite-effect.md`, 2026-10-23). Begun 2026-10-23 from TODO.md "Shader
follow-ups"; Decision 9 was set by the user and Decisions 1-8 confirmed, 2026-10-23. Phases 1-3 landed and were checked in
game 2026-10-23/24; Phase 4 measured and decided (not built) 2026-10-24. Only the GPU frame captures (Decision 7, Phases 1
and 3) are open; once they are done the plan can be deleted, its decisions and measurements being in IMPLEMENTATION-NOTES
"The sprite effect". SDF glyphs, which the epic listed under 7.3, stay their own TODO.md item ("Replace the text outline
with SDF glyphs") -- see Decision 8.)

## Why

The game still draws by hand, one rectangle at a time, what other engines draw with a shader: a glow is about twenty 1 px
rectangles, every scrollable element ends and restarts the UI's one pass twice to change the viewport and the scissor, and
the targeting overlay is one or more translucent quads per tile per frame. 7.2 put the whole UI on `SpriteEffectBatch`,
so each of these is now a change to one shader and one batcher, with no second pass to move first. A fourth item -- the
terrain image rebuilt on every whole-tile scroll -- is research: measured first, built only if it shows.

## Context (as of 7.2, commit d8564b2)

- **The effect.** `Cadence/UIEngine/Shaders/SpriteUber.fx`, `vs_3_0`/`ps_3_0`, one technique. Input is
  `SpriteEffectVertex` version 4, 28 bytes: position, colour, texture coordinate, style (`Mode`, `Glow`, `OutlineOnly`
  and an `Unused` padding byte, as a `Color` element) and the ground row's y. `SpriteDrawMode` is `Plain`/`Desaturate`.
  The glow is a second sampler (`s1`, linear) over a texture of one texel per tile, placed by `GlowTransform` (draw space
  to texel coordinates, computed in the vertex shader). The outline reads neighbours one screen pixel apart from
  `ddx`/`ddy` of the texture coordinate -- the technique the glow rings and the overlay's shapes reuse.
- **The batch.** `SpriteEffectBatch`: quads in call order, one draw call per run of one texture, ended by `End` or a
  change of `Glow`. `Begin(sampler, rasterizer, transform)` -- the transform is a uniform, set at flush.
- **The render-state stack.** `UiRenderService.PushRenderState`/`PopRenderState` end and begin the batch. Their only
  caller is `Element.Draw`, twice per scrollable element (`RequiresContentViewport`): a viewport plus `CameraTransform`
  (a translation, "no rotation or zoom") around `DrawContent`, then a scissor (`ScissorClipRasterizerState`, the
  rectangle intersected with the inherited one) around the children. Each push and each pop is a flush: four per
  scrollable element with children, two without. Elements that force it: every `Tooltip`, `TextBox` (multiline),
  `TextWindow`, the inventory grid and tabs, `AreaMapView`, `SpriteSheetView`, the Sprites previews. A handful of types
  read `GraphicsDevice.Viewport` while drawing (`Tooltip`, `DropdownController`, `MenuModeDimRenderer`,
  `AbilityScoreWindow`, `PlayerHealthBarContent`) -- to be surveyed in Phase 2.
- **Glow rings.** `GlowRenderer` (UIEngine): `InteriorFull` is one quad; `InteriorFade`/`ExteriorFade` are
  `FadeRingCount` (5) rectangular 1 px rings of four edge rectangles each -- 20 quads -- at alpha
  `0.5 * (1 - (ring - 1) / 5)` times the caller's multiplier, ring 1 against the rectangle's edge. Edges don't overlap
  (`BorderThickness.GetEdgeRectangles`), so each ring is an exact Chebyshev band. Callers: `Element` (window glow and
  overlay glow), `GridSquareRenderer`, `Tooltip` rows, `HotbarContent`, `InventoryItemStackCell`, `EmptyTradeSlotCell`,
  `ToggleActiveMarker`, `MapGameOverlay` (selected tile, followed entity per footprint tile) and `MapRenderer` (a dodging
  entity's footprint). Several draw both fades on one rectangle (attention, compare-eligible): 40 quads.
- **The targeting overlay.** `MapGameOverlay.Draw`, called by `MapWindow` after `MapRenderer`'s world (so it lies over
  entities and walls): per tile, a flat wash at alpha 0.5 (targetable, hovered footprint), or a charge fill -- a backdrop
  wash at 0.15 plus a bottom-up `TileFillRenderer` fill at 0.5 whose height is `(int)(tileHeight * fraction)` -- for the
  player's locked windup and every other Local entity's (red undodgeable, yellow dodgeable); then the selection's
  `InteriorFade` ring per tile; then Dodge's WASD letters (text, unchanged by this plan). Telegraphs of several entities
  can cover one tile, each with its own fraction, blended in draw order.
- **The terrain image.** `MapTileLayerCache` renders the ground and the tops into render targets keyed by
  `MapImageKey` (scroll, grid, tile size, layer), so a camera following the player rebuilds both on every whole-tile
  step. It already reports itself: the frame-cost row `Update/Map/Terrain cache rebuild` (`MapRenderer.cs:271`).
- **Measuring.** IMPLEMENTATION-NOTES "Measuring draw": total draw is `Draw.GameLoop.Shell.Draw` plus
  `GameLoop/SpriteBatch.End`; same-sitting interleaved pairs against a saved build of the commit before, windowed,
  Release, seed 1, frames 600-3600; the CPU only, so shader cost needs a GPU frame capture (RenderDoc or PIX).

## Design

**Pixel-exact recreation of today's drawing is not a goal** (Decision 9): where a shader can draw the same idea faster or
more smoothly -- a continuous fade instead of 1 px steps, an anti-aliased edge instead of a rounded one -- it does. What
must survive is what each thing tells the player: where a glow is and how strong, which tiles a targeting shape covers,
how far a windup has charged, which tile is selected.

### Glow rings in one draw (UIEngine)

- Two new `SpriteDrawMode`s, `FadeInward` and `FadeOutward`. A fade quad is drawn with the unit rectangle (one white
  texel, so the texture coordinate runs 0-1 across the quad whatever it samples); the pixel shader finds the pixel's
  distance to the rectangle's edge in screen pixels from the texture coordinate and its `ddx`/`ddy` and fades the draw's
  alpha continuously from `FadeMaximumAlpha` at the edge to nothing at the depth -- a smooth gradient instead of five
  1 px steps. Inward: from the quad's edge inward. Outward: the quad is the rectangle grown by the depth, fading outward
  from the inner rectangle, nothing inside it. The distance is the nearer axis's, as the rings are, so corners stay
  square; rounding them (a true distance to a rectangle) is a one-line choice made by eye in Phase 1.
- The depth travels in the vertex's padding byte (`SpriteEffectVertexStyle.Unused` becomes `FadeDepthPixels`), so
  `FadeRingCount` stays the one constant callers inset by and the shader never hard-codes 5. The layout is unchanged;
  `Version` moves to 5 because the bytes mean more.
- `GlowRenderer.Draw` keeps its signature and modes; `InteriorFade`/`ExteriorFade` become one quad each. Every caller
  is untouched. The rings' docs and the "approximate the fade" remark go.
- Small rectangles: today an inset ring past the centre draws nothing or a sliver; the shader's distance meets in the
  middle instead, which reads better.

### Clipping without ending the batch (UIEngine)

- `SpriteEffectVertex` gains a clip rectangle (four `Short`s, screen pixels: 8 bytes, a 36-byte vertex, `Version` 6).
  The vertex shader passes the pixel's screen position along; the pixel shader `clip`s outside the rectangle. A batch
  with no clip writes the whole render target as its rectangle, so nothing is ever discarded by accident.
- The batch holds a current **clip rectangle** and a current **draw offset** (a translation), both set without
  flushing, and each quad takes them when it is added: the offset is added to its corners on the CPU, the rectangle is
  written to its vertices. A quad wholly outside the clip is dropped before it is queued (a long inventory list stops
  paying for rows it can't show).
- `PushRenderState`/`PopRenderState` for the UI set those two instead of ending the batch: a scrollable element's
  content pass is the offset of its viewport's origin plus `CameraTransform`'s translation, clipped to the viewport (the
  inherited clip intersected); its children's pass is the inherited offset, clipped to its content rectangle intersected.
  The device's viewport and scissor stay the frame's -- so nothing in the UI flushes for clipping or scrolling any more
  (Decision 3).
- What stays: `Begin`'s transform and rasterizer for passes outside the UI (`MapTileLayerCache` into its targets); the
  hardware scissor is no longer used by the UI.
- Anything that read `GraphicsDevice.Viewport` as "my own area" while drawing reads its element's rectangle instead
  (Phase 2 surveys the five readers; one that wants the screen keeps reading the device, which is now always the screen).

### A per-tile targeting overlay (Presentation, with a mode in UIEngine)

- `MapOverlayTexels` (Presentation, beside `MapGlowTexels`) writes what `MapGameOverlay` draws per tile into texels over
  the same grid and margin, in the order it draws today, each tile carrying up to three layers:
  - **wash**: the flat washes (targetable, hovered footprint, every charge backdrop) composited in draw order on the CPU
    into one premultiplied colour -- exact, since flat washes over one tile blend to one colour;
  - **fill**: a charge fill's colour and its fraction, at most two per tile (Decision 5) -- the two nearest to
    resolving -- each composited over the wash in order;
  - **ring**: the selection's ring colour (or none).
  Encoded as a texture a few texels wide per tile (colours as `Color`, fractions in a channel of their own), read with
  point sampling.
- `MapGameOverlay` draws one quad over the grid in a new `SpriteDrawMode.TileOverlay`, which reads a third sampler at
  the pixel's tile (the same `GlowTransform`-style mapping, through an `OverlayTransform`) and shades the pixel from the
  tile's layers: the wash, then each fill up to its fraction of the tile's height from the bottom, its top edge
  anti-aliased over one screen pixel (so a fill rises smoothly instead of in whole-pixel jumps), then the ring with
  `FadeInward`'s gradient.
- A tile under three or more telegraphs at once shows the two nearest to resolving as fills; the rest still darken its
  backdrop wash, so the tile reads as targeted. No quad fallback: the whole overlay is one draw.
- The texels are rebuilt each frame anything is shown (fractions move every frame), and nothing is drawn or uploaded
  when nothing is. Dodge's WASD letters stay text.
- Like the glow, the overlay texture is set on the batch as state (`Overlay`, ending the run when it changes), and the
  map clears it after its draw.

### Research: tilemap terrain in a shader (Presentation)

- Measure first: `Update/Map/Terrain cache rebuild` per rebuild and its frame spikes while the camera follows the player,
  windowed, at the farthest zoom (most tiles), the default and the closest, over lava and wall-heavy ground. Read with
  `--diagnostics=frame` (the frame report's worst frames) as well as the mean.
- If a rebuild is under a frame's slack at every zoom, record the numbers in IMPLEMENTATION-NOTES and stop: the TODO
  item closes as "measured, not needed".
- Otherwise research, before building anything, how each of these fits a lookup shader -- variants (cell seeds, already
  computed per cell by `MapTerrainCells`), linked walls (`MapWallPlan`, join masks), face plus top (two images today),
  overhangs drawn in the row walk, turned and flipped cells, tints, and sprites reaching past their tile (the reach box)
  -- and compare the cheaper alternative: a cache larger than the view that rebuilds only when the camera leaves it, or
  that scrolls by copying and redraws only the newly exposed rows. A lookup that draws one of these differently (an
  overhang, a reaching sprite) is acceptable if it reads as well (Decision 9). Write the comparison up and decide with the
  user.

## Decisions (proposed)

1. **Phase order: glow rings, clipping, targeting overlay, tilemap research.** The rings are the smallest change and
   prove the "shape from the texture coordinate's derivatives" technique the overlay reuses; clipping changes the vertex
   the overlay then builds on; the research needs nothing of the others.
2. **Fades are `SpriteDrawMode` members, not a style flag**: a draw is a fade or a desaturate, never both, and `Mode`
   is already a byte the shader branches on per quad.
3. **Go further than the TODO on clipping: fold the viewport and transform pushes into the batch's offset too**, so
   scrolling never flushes either. The TODO kept the scissor "for the viewport/transform pushes a scrolled element still
   needs"; with the clip in the shader that only halves the flushes, and `CameraTransform` is translation-only, so a CPU
   offset replaces it exactly. Fallback, if the viewport readers resist: keep the viewport push and clip only the
   children in the shader.
4. **Drop quads wholly outside the clip on the CPU** rather than letting the shader discard them.
5. **Two charge fills per tile in the overlay texture, the two nearest to resolving; any more only darken the
   backdrop.** The overlay stays one draw with no fallback path; the fill that matters most (the next to land) is always
   shown, and the tile still reads as targeted by the rest.
6. **The overlay is its own texture and sampler, not more channels of the glow texture**: the glow is rebuilt only when
   auras change and is linearly filtered; the overlay changes every frame it shows and is point-sampled.
7. **Each phase is benchmarked windowed against a saved build of the commit before it**, three interleaved pairs, plus
   the headless `-Compare` for the simulation (no Game code changes). Phase 1 and 3 also take one GPU frame capture each
   of a busy view, since what they move is shader work.
8. **SDF glyphs are not in this plan.** They change the font pipeline (FontStashSharp's atlas, a generator), not the
   effect's vertex or modes, and have their own TODO.md item; the epic's "7.3 ... and SDF glyphs" line is updated to say
   so.
9. **Pixel-exact recreation is not required** (stated by the user, 2026-10-23): a faster or smoother look wins over
   matching today's quads, as long as it tells the player the same thing. Fades are continuous, fill edges anti-aliased,
   and the tilemap research may weigh approximations (Phase 4).

## Benchmarks

Draw cost exists only in a windowed run. The comparison is total draw (`Draw.GameLoop.Shell.Draw` plus
`GameLoop/SpriteBatch.End`). Every run is Release, seed 1, frames 600-3600, map 3072 (`Invoke-FrameBenchmark.ps1
-Configuration Release`); before and after only compared within one sitting, interleaved (A B, B A, A B).

- **Baseline builds:** saved at the start of each phase with `-SaveBaseline -BaselineName pre-7-3-phase-N` from the commit
  before it.
- **What the benchmark shows:** the default view -- map following the player, hotbar, HUD, status effects, floating
  text. It has some glows (hotbar attention, toggles) and telegraphs only when fights happen, so each phase also gets a
  by-eye check of its own busy case with `--diagnostics=frame` live: Phase 1 an inventory with many compare-eligible
  cells and an attention-glowing hotbar; Phase 2 the inventory on a long tab with Item Details and a tooltip, and the
  Workshop's Items and Map tabs; Phase 3 an armed Burst spell over a crowd and several goblins winding up at once.
- **Before:** the 7.2 build (commit d8564b2), saved as `pre-7-3-phase-1` and run as A in Phase 1's pairs below.

### Results

| Phase | Pair | Build | `Draw.GameLoop.Shell.Draw` | `GameLoop/SpriteBatch.End` | Total draw | `Draw.Base.MapWindow` | `Draw.StaticHud.Window` | `EcsContext.Update` |
|---|---|---|---|---|---|---|---|---|
| 1 | 1 | A (7.2) | 0.380 | 0.022 | 0.402 | 0.296 | 0.042 | 2.61 |
| 1 | 1 | B (Phase 1) | 0.476 | 0.035 | 0.511 | 0.402 | 0.052 | 3.52 |
| 1 | 2 | B (Phase 1) | 0.335 | 0.022 | 0.357 | 0.283 | 0.036 | 2.13 |
| 1 | 2 | A (7.2) | 0.363 | 0.024 | 0.387 | 0.303 | 0.043 | 2.79 |
| 1 | 3 | A (7.2) | 0.348 | 0.023 | 0.372 | 0.289 | 0.043 | 2.26 |
| 1 | 3 | B (Phase 1) | 0.400 | 0.026 | 0.427 | 0.336 | 0.045 | 2.80 |

**Phase 1 (2026-10-23, reports `Log/phase-benchmarks/20261023-234658` to `-235225`):** level. Every run kept real time.
In each pair the run with the slower draw also had the slower simulation (windowed `EcsContext.Update` 2.1-3.5 ms/frame),
so the machine moved both; draw as a share of `EcsContext.Update` is 0.14-0.16 on both sides. Medians: total draw 0.387
(A) against 0.427 (B), inside the sitting's spread. The headless `-Compare` simulated the same world (fingerprint
DB4F042B96332F31), `EcsContext.Update` 2.014 against 2.044 ms/frame (+1.5%, inside A's 1.6% spread). The default view has
few glows (a toggle's marker, the occasional attention glow), so this is the expected result; the gain is where many
glows draw at once (Phase 1's by-eye case). The GPU capture (Decision 7) is open.

| 2 | 1 | A (Phase 1) | 0.440 | 0.024 | 0.464 | 0.351 | 0.043 | 3.19 |
| 2 | 1 | B (Phase 2) | 0.458 | 0.043 | 0.501 | 0.371 | 0.055 | 2.57 |
| 2 | 2 | B (Phase 2) | 0.650 | 0.060 | 0.710 | 0.541 | 0.075 | 4.41 |
| 2 | 2 | A (Phase 1) | 0.342 | 0.023 | 0.364 | 0.289 | 0.036 | 2.23 |
| 2 | 3 | A (Phase 1) | 0.530 | 0.032 | 0.562 | 0.448 | 0.055 | 2.55 |
| 2 | 3 | B (Phase 2) | 0.412 | 0.035 | 0.447 | 0.340 | 0.047 | 2.89 |

**Phase 2 (2026-10-24, reports `Log/phase-benchmarks/20261024-003321` to `-003848`):** level, within a very noisy sitting
(windowed `EcsContext.Update` 2.2-4.4 ms/frame; pair 2's B is the sitting's slowest run on every row, simulation
included). Medians: total draw 0.464 (A) against 0.501 (B); pairs disagree in direction (+8%, +95%, -20%). Every run kept
real time. `SpriteBatch.End` is higher on B in every run (0.035-0.060 against 0.023-0.032), as expected: with no
mid-frame flushes for scrolling, more of the frame's quads are drawn at the end, which total draw already counts. The
headless `-Compare` simulated the same world (fingerprint DB4F042B96332F31), `EcsContext.Update` 2.054 against 2.073
(+0.9%, inside A's 6.1% spread). The default view has few scrollable elements (the inspection window, text windows), so
flushes saved there are few; the case this phase is for -- a long inventory tab, Item Details and tooltips open -- is the
by-eye check. Checked in game 2026-10-24: clipping and scrolling correct.

| 3 | 1 | A (Phase 2) | 0.484 | 0.040 | 0.524 | 0.404 | 0.056 | 3.37 |
| 3 | 1 | B (Phase 3) | 0.361 | 0.024 | 0.386 | 0.305 | 0.040 | 2.17 |
| 3 | 2 | B (Phase 3) | 0.594 | 0.046 | 0.640 | 0.500 | 0.066 | 3.65 |
| 3 | 2 | A (Phase 2) | 0.360 | 0.027 | 0.387 | 0.301 | 0.041 | 2.43 |
| 3 | 3 | A (Phase 2) | 0.338 | 0.026 | 0.363 | 0.282 | 0.039 | 2.21 |
| 3 | 3 | B (Phase 3) | 0.358 | 0.025 | 0.383 | 0.302 | 0.039 | 2.16 |

**Phase 3 (2026-10-24, reports `Log/phase-benchmarks/20261024-011038` to `-011605`):** level. Medians: total draw 0.387
(A) against 0.386 (B); in the one pair with matching simulation cost (3: 2.21 against 2.16) B is +5%, and in each other
pair the slower run is the one whose simulation was slower too. Every run kept real time. The headless `-Compare`
simulated the same world (fingerprint DB4F042B96332F31), `EcsContext.Update` 1.986 against 2.011 (+1.2%, inside A's 2.1%
spread). A first build that uploaded one overlay texture every frame read worse -- pair 3 at matching simulation cost
was +73% (0.372 against 0.643; reports `20261024-010313` to `-010840`), map draw up the same: writing a texture the GPU
may still be reading from the frame before stalls the CPU. Rotating through three textures (`MapGameOverlay`'s
`TexturesInFlight`) removed it. The default view shows telegraphs only while fights happen nearby, so the saving in
quads is small here; the busy case (a Burst over a crowd, several goblins winding up) is the by-eye check.

## Phases

Each phase ends with a build, both test projects, a benchmark, and an in-game checklist; the next phase starts on the
go-ahead.

### Phase 1 -- Glow rings in one draw (landed 2026-10-23, checked in game; GPU capture open)

Built as below, with these specifics:
- `FadeOf` in `SpriteUber.fx` takes the pixel's distance to the quad's edge from the texture coordinate over its change per
  pixel, the nearer axis's. The fade is linear and evaluated per pixel, so at 1:1 it lands on today's ring alphas at every
  pixel centre (`0.5 * (1 - (ring - 1) / 5)`); it differs only where a quad isn't on whole pixels and on rectangles too
  small for the rings. Inward keeps the rings' clear first pixel (ring 1 was the bounds inset by 1, so a window's border
  stays unglowed); outward starts against the bounds.
- `SpriteDrawStyle.WithFade(mode, depth)` sets the mode over whatever the caller passed, so a lit or styled glow keeps its
  glow and ground row.
- Checked in game 2026-10-23: every glow correct. Corners were then tried rounded and kept (the user's choice): the
  distance to the clear area (inward) or the rectangle (outward) is Euclidean, so an outward fade's outer edge and an
  inward fade's clear area are rounded; straight edges are unchanged.

- `SpriteDrawMode.FadeInward`/`FadeOutward`; `SpriteEffectVertexStyle.FadeDepthPixels` in the padding byte; `Version` 5
  in the vertex and the `.fx`; `SpriteDrawStyle` gains the depth.
- `SpriteUber.fx`: the fade's ring step from the texture coordinate's derivatives, in `ShadeSprite`'s mode branch.
  Rebuilt `.fxb` and hash by the build (`fxc`), never by hand.
- `GlowRenderer`: one quad per fade; the ring loop goes.
- Tests: `GlowRenderer` draws one quad per fade mode through a recording target, with the inflated rectangle for
  Outward, the bounds for Inward, the fade mode and depth in its style, and the colour at `FadeMaximumAlpha` times the
  multiplier; `InteriorFull` unchanged; the style's bytes round-trip (`SpriteDrawStyle.ToVertexStyle`).
- In game (do X, expect Y):
  - Select a tile (Basic inspection): a light-blue inward fade, smooth rather than stepped, about five pixels deep, the
    sprite readable through it. Follow a Huge entity in Detail mode: every footprint tile has its glow.
  - Hover an inventory cell, arm Compare: compare-eligible cells have both fades, inner and outer, and still stand out.
  - Arm a hotbar slot, drag onto the hotbar: attention glows read as clearly as before; a toggle that is on shows its
    marker.
  - A dodging entity's footprint glows; a tooltip row's glow; an empty trade slot's.
  - Zoom the map to its closest and farthest: the selected-tile glow stays five screen pixels deep.
  - Square or rounded corners: decide by eye, on window glows and inventory cells.
  - Workshop: any glow in its tabs as before.

### Phase 2 -- Clipping in the shader, scrolling without a flush (landed 2026-10-24, checked in game)

Built as below, with these specifics:
- `SpriteEffectVertex` version 6, 36 bytes: `SpriteEffectClipRectangle` (four `short`s, `Short4`; `None` is the whole short range).
  The vertex shader turns it into four distances to the edges and the pixel shader discards on their minimum -- through a
  temporary, because MojoShader refuses `texkill` straight from an input register (`clip(input.ClipDistances)` compiled
  under `fxc` and crashed the game at startup; caught by the first benchmark run, since no test loads the effect).
- `SpriteEffectBatch.Begin()` takes nothing now (point sampling, no transform, no rasterizer choice: nothing varied them
  once the UI stopped pushing passes). `DrawOffset` and `ClipRectangle` are set without flushing, except an offset change
  while a glow is set, which flushes so the glow stays placed in the draw space it was set in
  (`SpriteEffectGlow.TransformAfterOffset`); a ground row's y moves with the offset. A quad wholly outside the clip is
  un-reserved after it is written (`SpriteEffectQuads.LiesOutside`).
- `UiRenderService.RenderState` is `(DrawOffset, ClipRectangle?)` with `Root` and `ClippedTo`; push and pop set the two on
  the batch and never end it. The device's viewport and scissor are never changed by the UI any more.
- `Element.Draw`: content at the viewport's origin plus `CameraTransform`'s translation, clipped to the viewport; children
  at the inherited offset, clipped to the content rectangle. `ScissorClipRasterizerState` is gone.
- The five `GraphicsDevice.Viewport` readers (`Tooltip`, `DropdownController`, `MenuModeDimRenderer`, `AbilityScoreWindow`,
  `PlayerHealthBarContent`) all want the screen, which the device's viewport now always is: no change needed.
- Tests as below, on `SpriteEffectQuads` (clip carried, clamped, `LiesOutside` including a turned quad) and
  `UiRenderStateTests` (root, `ClippedTo` from none, intersecting, disjoint; the glow transform after an offset).

- `SpriteEffectVertex` version 6 with the clip rectangle; the shader's screen-position interpolator and `clip`.
- `SpriteEffectBatch`: `ClipRectangle` and `DrawOffset`, captured per quad; quads outside the clip dropped.
- `UiRenderService`: Push/Pop set offset and clip for the UI's two element passes; `RenderState` carries them (and
  loses `Viewport`/`ScissorRectangle` if nothing else needs them); the root state's clip is the render target.
- Survey and fix the five `GraphicsDevice.Viewport` readers; `Element.Draw`'s comments on the viewport swap.
- Tests: quads take the offset and clip in force when added; a quad outside the clip is not queued, one straddling it
  is; nested pushes intersect clips and add offsets, and pop restores both exactly (through `UiRenderService` with a
  recording batch where possible -- the batch needs a device, so the per-quad capture is tested on
  `SpriteEffectQuads`' side).
- Benchmark plus a draw-call count before and after in the Phase 2 busy case (`--diagnostics=frame`, or a GPU capture's
  event list).
- In game:
  - Scroll a long inventory tab: rows clip at the window's edge, top and bottom; the scrollbar thumb tracks.
  - Item Details beside it, scrolled; a tooltip with a long body, scrolled; a multiline TextBox, scrolled and typed
    into (the caret and selection clip too).
  - A dropdown open over a scrolled list draws on top, unclipped by the list.
  - Ability scores window and the health bar hover (two of the viewport readers): laid out as before.
  - Open the menu: the dim covers the whole screen.
  - Workshop: Items tab's list and inspector scrolled; the Map tab's area view zoomed and panned (it draws in local
    coordinates); the Sprites tab's sheet view and previews; resize the window while it draws.

### Phase 3 -- The per-tile targeting overlay (landed 2026-10-24, checked in game)

Built as below, with these specifics:
- `SpriteEffectTileOverlay` (UIEngine) names the layout -- five texels a tile: wash, two fills, ring, fractions (the
  fills' in red and green, the ring's depth in pixels in blue) -- and `SpriteEffectBatch.TileOverlay` is set like `Glow`
  (ends the run, placed in the draw space it was set in; one `_placementDrawOffset` serves both). The shader's
  `TileOverlayAt` reads the five texels point-sampled from a third sampler; the ring shares `InwardFadeAt` with
  `FadeInward`, so the selection ring has the same rounded corners as every other glow. Vertex version 7 (no layout
  change: the overlay's tile coordinate is computed in the vertex shader).
- Composition order in a tile is wash (every flat wash and every charge backdrop, in the order added), then the fills
  (larger first), then the ring. Today's quads interleaved a later telegraph's backdrop over an earlier one's fill;
  Decision 9.
- `MapGameOverlay` rotates through three textures (`TexturesInFlight`): uploading one texture each frame stalled the CPU
  (see the Results). It draws one quad through the transformed `Draw` (a float origin and size, so a drag's sub-pixel
  offset lines up with the overlay's transform).
- `TileFillRenderer` and `TileFillDirection` had no other caller and are deleted; `GlowRenderer.FadeMaximumAlpha` is
  public so the ring matches the glows.

- `MapOverlayTexels` and its texture (rebuilt each frame anything shows, kept between frames otherwise),
  `SpriteDrawMode.TileOverlay`, the third sampler and `OverlayTransform` in the effect and the batch (`Overlay`, ending
  a run when it changes).
- `MapGameOverlay` fills the texels instead of drawing quads and draws one quad over the grid; Dodge's letters after.
  `TileFillRenderer` loses its last map caller.
- Tests (`MapOverlayTexels`, pure, over `FakeMapView` and a camera): a targetable tile's wash; a hovered footprint
  tile's; a charge tile's backdrop wash and fill with its fraction; two telegraphs on a tile composite in draw order;
  three keep the two nearest to resolving as fills and add the third's backdrop; the selected tile's ring; nothing
  outside the grid; an empty overlay reports nothing to draw.
- In game:
  - Arm a Burst spell and sweep the cursor: targetable tiles and the hovered splash (including tiles past Range) wash as
    before, the sprite visible through them.
  - Cast a Delayed spell: the backdrop shows on every target tile from the first frame and the fill rises smoothly
    bottom-up with the charge, full at activation -- most visible on a long windup at the closest zoom.
  - Goblins winding up: red and yellow telegraphs, overlapping each other and the player's own, fill independently.
  - Select a tile under a telegraph: the ring draws over the wash.
  - Arm Dodge: WASD letters on the four tiles, outlined.
  - Every zoom; a right-drag's smooth scroll keeps the overlay on its tiles.

### Phase 4 -- Research: tilemap terrain (measured 2026-10-24; decided: not built)

**How it was measured.** The benchmark's player stands still, so the camera never scrolls and the rebuild row never
appears in a report. A temporary, uncommitted probe in `MapWindow.Update` (reverted after) set the zoom from an
environment variable, turned camera-follow off and scrolled one tile every 10 updates -- 6 tiles a second, a brisk walk,
reversing every 20 steps -- and timed all of `MapRenderer.Update` as its own row. Release, windowed, seed 1, frames
600-3600 (300 rebuilds), two runs per zoom (reports `Log/phase-benchmarks/20261024-014828` to `-015354`). "Terrain cache
rebuild" times only `DrawGroundIntoCache` (reading the cells, planning walls, queuing floors and faces); the whole update
adds the tops image, the render-target switches and the batch's flush of both images.

| Zoom (tile) | Whole update per rebuild, mean | Worst single rebuild | Ground row per rebuild, mean | Ground row worst | Map draw per frame (live, unrelated to rebuilds) |
|---|---|---|---|---|---|
| Team (36 px, the default) | 0.6-0.9 ms | 0.8-2.5 ms | 0.28-0.37 ms | 0.4-1.0 ms | 0.30-0.42 ms |
| Neighborhood (18 px) | 1.7-2.2 ms | 4.1-5.2 ms | 0.9-1.2 ms | 2.2-3.1 ms | 0.66-0.87 ms |
| Borough (9 px) | 5.6-6.7 ms | 10.4-13.7 ms | 3.1-4.1 ms | 7.5-7.6 ms | 1.60-2.68 ms |

**What it says.**
- At the default zoom a step costs under a millisecond: no hitch, and nothing to build for.
- Neighborhood zoom costs about 2 ms a step, worst 5 ms: inside a 16.7 ms frame next to ~2-3 ms of simulation and ~1 ms
  of draw.
- Borough zoom costs about 6 ms a step, worst 10-14 ms. With the simulation and that zoom's own live draw (1.6-2.7
  ms/frame -- the row walk over ~4x the tiles, a separate cost), a walking step there can exceed a frame. Only Borough
  zoom is at risk, and only while the camera scrolls.
- About half of each rebuild is outside the ground row: the tops image, two render-target switches, and the batch
  uploading and drawing ~12,000 tiles' quads at Borough.

**The options, for the Borough case.**
1. **A tilemap shader** (the TODO's idea): one quad per layer looking each tile's sprite up from a tile-id texture.
   Variants are cheap (a seed per tile is already computed, `CellSeeds`), and turns, flips and tints could be per-tile
   bits. But the rest doesn't fit a per-tile lookup: a sprite may reach two rows above and two columns either side of its
   tile (`SpriteReach`), so each pixel would test up to 15 neighbouring tiles' sprites in draw order; walls are a face plus
   a top in two images with the wall plan deciding which pieces are cached and which are live overhangs (`MapWallPlan`),
   and join masks pick pieces from a linked set. A shader doing all that per pixel is a second renderer to keep in step
   with `MapRenderer`, for one zoom level. Not recommended.
2. **A cache larger than the view**: render the images with a margin of tiles beyond the grid and rebuild only when the
   scroll leaves it. A margin of 8 tiles at Borough turns a rebuild per step into one per 8 steps, each about 1.3x the
   area -- the same 10-14 ms spike, an eighth as often. Small change (the key's scroll becomes the cached area's, the blit
   offset grows), but it doesn't remove the spike.
3. **Scroll the cached image and redraw only what's exposed**: on a scroll, copy the old image shifted by the scroll into
   the other of two render targets (one full-screen quad), then redraw only the newly exposed rows or columns plus the
   reach margin (2 tiles) so walls and tops reaching across the seam are right. A one-tile step at Borough redraws about
   5 columns of ~90 tiles instead of ~12,000 tiles: well under a millisecond. Needs the cell read and the wall plan
   restricted to the strip (both read the margin they need already), and a full rebuild on a jump (zoom, teleport,
   terrain change). It is what tile engines with a scrolling camera have done since hardware tilemaps.

**Recommendation.** Don't build the tilemap shader. If walking at Borough zoom should be smooth, build option 3 as its
own TODO item (Presentation, Medium); otherwise close the TODO item as "measured: only Borough zoom rebuilds slowly".

**Decision (the user, 2026-10-24): build nothing.** Neither slow zoom is a playable one. Borough is a stand-in for a
mini-map, which will draw a simplified map of its own at that scale; Neighborhood will become adjustable zoom amounts
for aiming long-ranged actions, snapping back to the normal zoom when the action completes; and the player-triggered
zoom levels go away once the mini-map exists. At the zoom the player walks in, a rebuild is under a millisecond. The
TODO item is closed; the numbers are in IMPLEMENTATION-NOTES "The sprite effect".

- The measurements in the Design section, written up with the numbers.
- Either "not needed" recorded in IMPLEMENTATION-NOTES and the TODO item deleted, or the comparison written up and a
  decision taken with the user before anything is built (a plan of its own if built).

### Notes (with each phase as it lands)

IMPLEMENTATION-NOTES "The sprite effect" (fade modes, the clip and offset, the overlay texture, measurements), CLAUDE.md
(UIEngine's `UiRenderService` -- push/pop no longer end the batch; Presentation's map drawing -- the overlay is a
texture), TODO.md ("Shader follow-ups" shrinks as phases land and is deleted with the last), the epic's child 7 entry.

## Risks

- **Vertex size.** 28 to 36 bytes for every quad in the game. 7.1 measured ~10 ns a sprite at 40 bytes with no overall
  regression; the CPU drop of off-clip quads and the end of four flushes per scrollable element should more than pay for
  it, and Phase 2's benchmark decides.
- **`ddx`/`ddy` on a quad's edge pixels.** Derivatives come from 2x2 pixel blocks; on an axis-aligned quad the texture
  coordinate is linear, so they are exact, but a quad less than two pixels thick in an axis (none of the fades) would
  read a neighbour outside it. The outline already relies on the same.
- **Clip precision.** Clip rectangles are whole pixels and compared against pixel centres, as the scissor is; a
  sub-pixel offset (none in the UI -- scroll offsets are whole) would round differently from the scissor.
- **Map in a clipped window.** The map draws inside no scrollable element today; if it ever does, its draws take the clip
  like everything else.
- **The overlay's per-frame upload.** A grid at the farthest zoom is a few thousand tiles a few texels each, uploaded
  each frame a telegraph shows; small next to the quads it replaces, measured in Phase 3.
- **Mods** draw nothing in the UI yet, so no mod-facing API changes.

## Out of scope

- SDF glyphs and a scalable outline: TODO.md "Replace the text outline with SDF glyphs and an outline shader".
- The aura look and lights: TODO.md "Aura glow clarity" (touches `GlowAt`, not these modes).
- "Sprite effect: overlap the glow's two texture reads" (TODO.md, Medium).
