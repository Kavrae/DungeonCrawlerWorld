# 7.2: The whole UI on the sprite effect

(Epic child 7, after `PLAN-shaders-and-sprites-tab.md` (7, since deleted) and the shader performance pass (7.1, commit
e394572). Planned 2026-10-23; Decisions 1-7 confirmed 2026-10-23; Phases 1 and 2 landed 2026-10-23 and were checked in
game; Phase 3 landed 2026-10-23 and was checked in game. Only Phase 2's GPU check is still open; once it is done the plan
is complete and can be deleted, its decisions and measurements being in IMPLEMENTATION-NOTES "The sprite effect".
Plan 7.3 follows it: clipping in the shader, glow rings in one draw, the per-tile targeting overlay and SDF glyphs.)

## Why

Outlined text outside the map is still nine `DrawString` calls a string (`ContrastTextRenderer.DrawCopies`: hotbar and HUD
numbers, floating text, buttons' hotkey labels, resource bars, item cell badges, the targeting overlay, the Workshop's map
labels). The map already draws it in two through the sprite effect, measured at about a fifth of the cost (300 outlined
labels: 0.277 ms on `SpriteBatch`, 0.063 ms on the batch) and matching the nine copies within 2/255.

Moving text alone is the wrong cut. The UI's pass is a `SpriteBatch`, and the outline needs `SpriteEffectBatch`, so text
would either switch passes per string (two flushes a string) or be queued and drawn later (out of order: a lower element's
text over what is drawn above it). Mainstream engines don't separate text at all:
- **Unity (UGUI/TextMeshPro):** text is quads with an SDF material whose outline is a material setting; the canvas draws
  one list in hierarchy order and merges neighbouring draws only where nothing between them overlaps.
- **Unreal (Slate):** draws carry a layer id (later windows and children higher); batching is by layer, then shader and
  texture, so nothing from a lower layer lands on a higher one.
- **Godot:** canvas items in tree order, neighbours with one material merged; font outlines come from the font.
- **Dear ImGui:** a command list per window, windows back to front.

The common rule is one shader for sprites and text, draw order from submission, and batching that never changes what is on
top. Here that means the UI's pass becomes `SpriteEffectBatch`. Then outlined text is two quads a glyph in call order with
no flushes, the map stops being a pass of its own, and plan 7.3's work (all of it waiting on "windows on the batch") can
start.

## Context (as of 7.1, commit e394572)

- **`UiRenderService` owns drawing.** `UIEngine/Rendering/UiRenderService.cs` holds the device, the shared `SpriteBatch`
  (`SpriteBatch`), `SpriteEffectBatch`, the unit rectangle and the render-state stack; elements reach it as
  `Element.Render`, and anything outside the tree that draws (`UiLayerHost`, `MapRenderer`) takes it. `UiEngineContext`
  builds it, and `UiEngineContext.LoadContent` fills it from `SpriteBatchRenderer.GetSpriteBatch()`.
- **One root pass, a render-state stack.** `GameLoop.Draw` and `WorkshopHost.Draw` begin the shared `SpriteBatch`
  (`SpriteBatchRenderer.StartSpriteBatch`: Deferred, AlphaBlend, PointClamp) and call `UiRenderService.ResetRenderState`.
  `PushRenderState`/`PopRenderState` (Element.Draw's two `RequiresContentViewport` blocks: viewport and transform for a
  scrolled element, scissor for a clipped one) end the batch and begin it again under the new state. `RenderState` also
  carries `Effect` and `DepthStencilState`, which nothing sets; every pass samples `PointClamp`.
- **The map and the Sprites previews switch passes.** `UiRenderService.BeginSpriteEffectPass`/`EndSpriteEffectPass` end
  the `SpriteBatch`, run `SpriteEffectBatch` under the current state, and resume. `MapRenderer.Draw` (in a try/finally),
  `SpritePlacementPreview` and `SpriteLinkPreview` use them. `MapTileLayerCache` renders into its own render target through
  the batch outside any pass; since 7.1 the ground image is cleared to the map's fill colour, so its blit is opaque and
  the fill is drawn separately only on the uncached path.
- **`SpriteEffectBatch` after 7.1:** a 28-byte vertex (`SpriteEffectVertex` version 4: 2D position, colour, texture
  coordinate, the style as three bytes in a `Color` element plus a padding byte, the ground row's y as one float) against
  `SpriteBatch`'s 24; the glow's read point and texel transform are computed in the vertex shader, and one `GlowAt` serves
  both glow modes, so an unlit draw costs the pixel shader one texture read and a few compares. Quads are placed exactly as
  `SpriteBatch` places them (`SpriteEffectQuadsTests`), one draw call per run of one texture, text through FontStashSharp's
  `IFontStashRenderer`, the viewport's projection after the Begin's transform, scissor through the rasterizer state. Its
  glow (`Glow`) is read once, at flush, for every quad in the pass.
- **`ISpriteDrawTarget`** lets the drawing helpers (`SpriteRenderer`, `SpriteOrGlyphRenderer`, `LabelRenderer`,
  `ContrastTextRenderer`, `GlowRenderer`) draw to either target; each still has a `SpriteBatch` overload wrapping
  `SpriteBatchDrawTarget`, whose `DrawOutlinedString` is the nine copies.
- **Size of the change (re-surveyed 2026-10-23, after 7.1):** `SpriteBatch`/`spriteBatch` appears about 560 times in
  about 70 files: UIEngine 37 files (261, 15 of them `UiRenderService`), Presentation 29 (239), the Workshops 2 (49),
  GameShell 1, Tests 5 (comments and the draw-target tests). By argument count the calls are `Draw(texture, rectangle,
  color)`-shaped (41), `Draw(texture, rectangle|position, source, color, ...)` (38 six-argument, 9 eight, 3 nine), and a
  few `DrawString`. The heaviest files: `AreaMapView` (33), `HotbarContent` (32), `BorderRenderer` (29), `MapGameOverlay`
  (23), `PlayerStatusEffectsContent` (18), `Element` (17), `TextBox` (15), `InventoryItemStackCell` (14). Fifteen calls go
  to `ContrastTextRenderer.Draw`.
- **Shader work queued in TODO.md that touches the same effect** but not this plan: "Sprite effect: overlap the glow's two
  texture reads" and "Aura glow clarity" (both map-only, about `GlowAt`). Neither depends on this plan, nor this on them.

## Design

### The batch is the UI's pass (UIEngine)

- The host begins and ends the root pass through `UiRenderService` (`BeginFrame`/`EndFrame`, replacing
  `SpriteBatchRenderer` and the separate `ResetRenderState` call), which begins `SpriteEffectBatch` under the root state.
- `UiRenderService.Batch` (a `SpriteEffectBatch`) replaces both `UiRenderService.SpriteBatch` and
  `UiRenderService.SpriteEffectBatch` (Decision 2). `PushRenderState`/`PopRenderState` end and begin the batch, as they do
  the `SpriteBatch` now. `RenderState` loses `Effect` and `DepthStencilState`.
- `SpriteEffectBatch` gains the `SpriteBatch`-shaped overloads the UI calls -- `Draw(texture, rectangle, color)`,
  `Draw(texture, position, color)`, `Draw(texture, rectangle, source, color)`, the rotation/origin/scale/effects forms with
  a `float` or `Vector2` scale, `DrawString(font, text, position, color)` -- each with an optional `SpriteDrawStyle`
  (default Plain), so a call site changes its type and nothing else. A layer depth is not taken (Deferred ignores it).
- **Setting `Glow` ends a run:** quads queued before the change draw under the old glow. So the map sets the glow, draws,
  and sets it back to None inside the shared pass, with no pass of its own. `BeginSpriteEffectPass`/
  `EndSpriteEffectPass` go.

### Text

- `ContrastTextRenderer.Draw` and `LabelRenderer`'s outlined path hand every outlined string to
  `SpriteEffectBatch.DrawOutlinedString` (an outline layer, then the fill). `DrawCopies` and `SpriteBatchDrawTarget` go.
- Plain text needs nothing: it is `DrawString` on the batch, through `IFontStashRenderer`, as map text already is.
- `ItemIconRenderer`'s quantity is a drop shadow (two `DrawString`s), not an outline; it stays two draws.

### What stays

- `ISpriteDrawTarget` stays, so a test can record what a helper draws; its only implementation in the game becomes the
  batch. The helpers' `SpriteBatch` overloads go.
- Draw order is call order, as now. No queue, no sort, no reordering for batching.
- `MapTileLayerCache` keeps its own Begin/End into its render target, outside the root pass.

## Decisions (confirmed 2026-10-23)

1. **The whole UI draws through `SpriteEffectBatch`**; no `SpriteBatch` is left in UIEngine, Presentation or the
   Workshops. This replaces the out-of-scope line "Outlined text outside the map's pass ... on the effect" in the shader
   plan.
2. **Keep the name `SpriteEffectBatch`.** `UiRenderService.SpriteBatch` would keep its name if it were still a
   `SpriteBatch`; it no longer is, so it becomes `Batch`, and `UiRenderService.SpriteEffectBatch` folds into it.
3. **`SpriteBatch`-shaped overloads on the batch**, with the style optional: the conversion is a type change at each call
   site, and the API reads the way FNA code does.
4. **Setting `Glow` ends a run** rather than glow being part of `RenderState`: the map changes glow without changing
   viewport, scissor or transform, and the only thing that reads it is the flush.
5. **Keep `ISpriteDrawTarget`** for the helpers and their tests; delete `SpriteBatchDrawTarget`.
6. **Keep the benchmark row name `GameLoop/SpriteBatch.End`** (CLAUDE.md: row names stay stable for the benchmark skill
   and saved results), now timing the root batch's end.
7. **Clipping in the shader, glow rings in one draw, the per-tile targeting overlay and SDF glyphs are plan 7.3**, after
   this one. Each is easier once this lands; none is needed for it.

## Benchmarks

Draw cost exists only in a windowed run (the headless benchmark draws nothing). The comparison is total draw --
`Draw.GameLoop.Shell.Draw` plus `GameLoop/SpriteBatch.End` -- not one window's row: on a shared batch, a window's quads are
paid for by whichever element next ends the batch. Every run is Release, seed 1, frames 600-3600, map 3072
(`Invoke-FrameBenchmark.ps1 -Configuration Release`, `-ExecutionPolicy Bypass` where scripts are disabled). Machine drift
moves whole tables between sittings, so a before and an after are only compared when run in the same sitting, interleaved.

- **Baseline build:** `Log/phase-benchmarks/baseline-build-release-pre-ui-batch` (commit e394572, saved 2026-10-23 with
  `-SaveBaseline -BaselineName pre-ui-batch`). Each after-measurement runs it with `-ExePath` alongside the current build,
  interleaved (A B, B A, A B).
- **Simulation unchanged:** after each phase, `-Compare -BaselineName pre-ui-batch` (headless) must report the same world
  fingerprint and no simulation regressions; this plan touches no Game code.
- **What the benchmark shows:** the default view -- the map following the player, the hotbar, HUD bars, status effects
  and floating text from whatever fights happen; no inventory or Item Details open. Busy windows (inventory with many tabs,
  Item Details, the Workshop's tabs) are checked by eye in game, and with `--diagnostics=frame` live if they look slower.

### Before (7.1, commit e394572, 2026-10-23)

Three windowed Release runs, back to back (ms per frame; reports `Log/phase-benchmarks/20261023-193222.json`,
`-193328`, `-193434`). Every run kept real time (50.0 s for 3000 frames), so Draw is comparable.

| Run | `Draw.GameLoop.Shell.Draw` | `GameLoop/SpriteBatch.End` | Total draw | `Draw.Base.MapWindow` | `Draw.StaticHud.Window` | `EcsContext.Update` |
|---|---|---|---|---|---|---|
| 1 | 0.416 | 0.025 | 0.441 | 0.334 | 0.062 | 2.81 |
| 2 | 0.432 | 0.029 | 0.461 | 0.343 | 0.067 | 2.15 |
| 3 | 0.587 | 0.037 | 0.624 | 0.467 | 0.090 | 3.84 |

Run 3 moved every row together, simulation included (`EcsContext.Update` +79% over run 2, every draw row +35-45%): the
machine, not the code. Runs 1 and 2 are the reference, total draw 0.44-0.46 ms/frame, of which the map is ~0.34 and the
static HUD (hotbar, bars, status effects -- the outlined text this plan moves) ~0.065. The other windows in view
(buttons, text windows, the inspection window, folders) are under 0.005 each. Because of run 3, each after-measurement is
three *interleaved* pairs against the baseline build, never these numbers alone.

### After

Recorded at the end of Phase 1 (expected level with before: nothing moves to the batch yet) and Phase 2 (the real
change), each as three interleaved pairs against the baseline build, in this table's shape:

| Phase | Pair | Build | `Draw.GameLoop.Shell.Draw` | `GameLoop/SpriteBatch.End` | Total draw | `Draw.Base.MapWindow` | `Draw.StaticHud.Window` | `EcsContext.Update` |
|---|---|---|---|---|---|---|---|---|

| 1 | 1 | A (baseline) | 0.514 | 0.031 | 0.545 | 0.415 | 0.075 | 3.71 |
| 1 | 1 | B (Phase 1) | 0.652 | 0.042 | 0.694 | 0.522 | 0.099 | 4.26 |
| 1 | 2 | B (Phase 1) | 0.392 | 0.024 | 0.416 | 0.314 | 0.060 | 2.39 |
| 1 | 2 | A (baseline) | 0.385 | 0.024 | 0.409 | 0.307 | 0.059 | 2.14 |
| 1 | 3 | A (baseline) | 0.587 | 0.041 | 0.628 | 0.462 | 0.095 | 2.19 |
| 1 | 3 | B (Phase 1) | 0.632 | 0.039 | 0.671 | 0.509 | 0.094 | 4.35 |

**Phase 1 (2026-10-23, reports `Log/phase-benchmarks/20261023-200853` to `-201420`):** level, within a noisy sitting.
Every run kept real time. The headless `-Compare` simulated the same world (fingerprint DB4F042B96332F31) with
`EcsContext.Update` 1.893 against 1.885 ms/frame (-0.5%, inside A's 1.6% spread), so the simulation is unchanged -- yet
the windowed `EcsContext.Update` swung 2.1-4.4 ms/frame between runs, B's pairs 1 and 3 among the slowest: the machine
was busy, and every draw row moved with it. The one calm pair (2) reads 0.409 before and 0.416 after (+1.7%). Phase 1
changes nothing the default view draws differently (a glow assignment that flushes nothing, two fewer fields per render
state), so this is the expected result; Phase 2's measurement wants a quiet machine, and more pairs if it isn't.

| 2 | 1 | A (baseline) | 0.665 | 0.044 | 0.709 | 0.531 | 0.101 | 3.59 |
| 2 | 1 | B (Phase 2) | 0.352 | 0.023 | 0.375 | 0.291 | 0.043 | 2.43 |
| 2 | 2 | B (Phase 2) | 0.551 | 0.043 | 0.594 | 0.450 | 0.072 | 2.21 |
| 2 | 2 | A (baseline) | 0.587 | 0.042 | 0.629 | 0.459 | 0.096 | 2.18 |
| 2 | 3 | A (baseline) | 0.616 | 0.040 | 0.656 | 0.490 | 0.096 | 4.31 |
| 2 | 3 | B (Phase 2) | 0.417 | 0.027 | 0.445 | 0.347 | 0.050 | 3.38 |

**Phase 2 (2026-10-23, reports `Log/phase-benchmarks/20261023-213440` to `-214008`):** faster in every pair, by more
than the sitting's noise. Every run kept real time; the headless `-Compare` simulated the same world (fingerprint
DB4F042B96332F31), `EcsContext.Update` 1.915 against 1.900 ms/frame (-0.8%, inside A's 1% spread). The machine was noisy
again (windowed `EcsContext.Update` 2.2-4.3), so the pairs disagree on size but not direction:
- Total draw, medians: 0.656 before, 0.445 after (-32%); per pair -47%, -6%, -32%. The pair with matching simulation cost
  (pair 2, 2.21 against 2.18) is the conservative reading: -6%.
- `Draw.StaticHud.Window` (the hotbar, bars and status effects, where the outlined text is): medians 0.096 before, 0.050
  after (-48%); per pair -57%, -25%, -48%. This is the nine-copies outline becoming two quads a glyph.
- `Draw.Base.MapWindow` is down too (medians 0.490 to 0.347): the map no longer ends and restarts the shared pass around
  itself. Part of the map's flush now lands wherever the batch next ends, which the total already counts.

On a shared batch the map's quads are flushed by whichever element next ends the batch, so after Phase 2 part of
`MapWindow`'s cost may show under `StaticHud.Window` or `SpriteBatch.End`; read total draw first.

A total-draw regression above the run-to-run spread is investigated before the phase is called done (first suspect:
`BorderRenderer`'s and `GlowRenderer`'s many 1 px rectangles; first fix: a cheaper plain-quad path in the batch, not a
return to `SpriteBatch`).

## Phases

Each phase ends with a build, both test projects, and an in-game checklist; the next phase starts on the go-ahead.

### Phase 1 -- The batch as a drop-in (no visible change) (landed 2026-10-23, checked in game)

Built as below, with these specifics:
- The `SpriteBatch` shapes are extension methods on any `ISpriteDrawTarget` (`SpriteDrawTargetExtensions`, generic so a
  struct target isn't boxed) rather than members of the batch: one set serves the batch, `SpriteBatchDrawTarget` until
  Phase 3, and the tests' recording target, and a call reads exactly as on `SpriteBatch` (Decision 3). Shapes:
  `Draw(texture, rectangle, color[, style])`, `Draw(texture, rectangle, source, color)`, `Draw(texture, position,
  color[, style])`, `Draw(texture, position, source, color[, style])`, the turned/scaled form with a `float` or `Vector2`
  scale, and `DrawString(font, text, position, color)`. None takes a layer depth; the few calls passing `0f` drop it in
  Phase 2.
- A glow change flushes what is queued (`SpriteEffectBatch.Glow`'s setter; an equal value does nothing). The batch needs a
  GraphicsDevice, which no test has, so this is not unit-tested; nothing in Phase 1 changes glow mid-pass (the map sets it
  on an empty pass), and Phase 2's checklist is where it shows -- the map's glow must survive the map clearing it.
- `RenderState` is sampler, rasterizer, transform, viewport and scissor.

- `SpriteEffectBatch`: the `SpriteBatch`-shaped overloads (Decision 3); setting `Glow` ends a run (Decision 4).
- `RenderState` without `Effect` and `DepthStencilState`.
- `MapRenderer` and the Sprites previews still use their own pass; nothing else moves yet.
- Tests (as built, `SpriteDrawTargetTests`): each `SpriteBatch` shape reaches the target's rectangle or transformed draw
  with `SpriteBatch`'s defaults (no source, unturned, origin top-left, unscaled, unflipped) and the Plain style unless one
  is given; `DrawString` is the plain string. Where those land on screen is `SpriteEffectQuadsTests`' already.
- Benchmark: three interleaved pairs against the baseline build (expected level).
- In game: the map looks exactly as before -- lava glow, a corpse desaturated, glyph outlines, a wall top fading over the
  player. Workshop: the Map and Sprites tabs as before.

### Phase 2 -- Everything on the batch (landed 2026-10-23, checked in game; the GPU check is open)

Built as below, with these specifics:
- `UiRenderService.BeginFrame`/`EndFrame` replace `SpriteBatchRenderer` (deleted) and `ResetRenderState`; `Batch` is the
  only batch it holds, and `Initialize` takes only it. The `GameLoop/SpriteBatch.End` row now times `EndFrame`.
- The conversion was a type change in 61 files (`SpriteBatch` parameters, locals and `Render.SpriteBatch` reads) plus a
  `using UIEngine.Rendering` where missing; the only call changes were three dropped layer depths (`Toggle`'s check mark,
  `RadialFillRenderer`'s and `TargetShapePreviewElement`'s slivers). `LabelRenderer.DrawLeftAligned`/`DrawRightAligned`
  existed only for `SpriteBatch` and became generic over the target; the helpers' other `SpriteBatch` overloads are unused
  now and go in Phase 3.
- `MapRenderer.Draw` sets the glow, draws, and clears it in a `finally`; the Sprites previews and the sheet view draw
  straight into `Render.Batch`.

- Hosts: `GameLoop.Draw` and `WorkshopHost.Draw` (including its draw-during-resize path) begin and end the frame through
  `UiRenderService`; `SpriteBatchRenderer` goes, and `UiEngineContext.LoadContent` hands `UiRenderService` only the batch.
- `UiRenderService.Batch`; Push/Pop on the batch; `BeginSpriteEffectPass`/`EndSpriteEffectPass` go, and `MapRenderer`,
  `SpritePlacementPreview` and `SpriteLinkPreview` draw into the shared pass (the map setting `Glow` and clearing it).
- Every `SpriteBatch` parameter, local and field in UIEngine, Presentation and the Workshops becomes the batch: the
  element tree (`Element`, `Window`, `Folder`, `TextBox`, `ListView`, `Toggle`, `DropdownField`, `ColorField`, `Button`),
  the renderers (`BorderRenderer`, `BorderedBoxRenderer`, `GridSquareRenderer`, `TileFillRenderer`, `ResourceBarRenderer`,
  `SparklineRenderer`, `MenuModeDimRenderer`, `GlowRenderer`, `RadialFillRenderer`, `ItemIconRenderer`,
  `DragGhostRenderer`, `ToggleActiveMarker`), the game's contents (`HotbarContent`, `PlayerStatusEffectsContent`,
  `PlayerHealthHoverContent`, `InventoryItemStackCell`, `TargetShapePreviewElement`, the ability score rows, `HealthWindow`,
  `TradeWindow`), `UiLayerHost`, `MapGameOverlay`, `FloatingTextRenderer`, `AreaMapView`, `SpriteSheetView`.
- Outlined text reaches the shader by itself: `ContrastTextRenderer.Draw` on the batch is `DrawOutlinedString`.
- Tests: the draw-target tests drop the `SpriteBatch` path; a recording target still sees `DrawOutlinedString` once per
  outlined string. `HotbarContentTests`/`FolderTests` comments name the batch.
- Benchmark: three interleaved pairs against the baseline build, into the After table; the headless `-Compare` for the
  simulation.
- GPU check (the windowed benchmark times the CPU only, so it can't see the shader): one frame captured in RenderDoc (or
  PIX), Release, on the baseline build and on Phase 2, of the same busy view -- the hotbar full, floating text over lava,
  the inventory open on a long tab with Item Details beside it. Compare the UI's draws' total GPU time (the events after
  the map's) and the frame's GPU time, and note the draw-call count of each. If the UI's GPU time grew noticeably, the
  first fix is a branch-free plain technique compiled from the same `.fx`, chosen per flush when every quad in it is
  plain (no extra flushes) -- built only then.
- In game (do X, expect Y):
  - Hotbar: key labels, quantities, costs and cooldown numbers keep a hard 1 px black outline; a locked slot's text fades
    fill and outline together.
  - Floating damage text over lava and over a wall: outlined, rising and fading as before, never lit by the glow.
  - Stand by lava and Holy Ground: the map's glow still shows, smooth across tiles, on floors and entities (the map sets
    the glow and clears it in the shared pass; were the clear not to flush the map's quads first, they would draw unlit),
    and the hotbar, HUD bars and an inventory window over the glowing tiles are untinted.
  - Status effect countdowns, HUD bar numbers ("cur / max"), a button's hotkey label (disabled at half alpha), a merged
    stack's badge: all outlined as before.
  - Scroll a long inventory tab and Item Details: content clips at the window's edge as before; a dropdown and a tooltip
    over the map draw on top of it.
  - Open the menu (the dim layer): everything under it dims, nothing drawn over it.
  - The map: unchanged from Phase 1, including inside a scrolled window over it; targeting overlay text outlined.
  - Workshop: every tab, the Map tab's labels and loading text, the Sprites tab's sheet zoomed with its grid; resize the
    window while it draws.

### Phase 3 -- Cleanup and notes (landed 2026-10-23, checked in game)

Built as below, with these specifics:
- Deleted: `ContrastTextRenderer.DrawCopies` (and its offsets), `SpriteBatchDrawTarget`, and the `SpriteBatch` overloads
  of `LabelRenderer` (`Draw`, `DrawCentered`), `SpriteRenderer`, `SpriteOrGlyphRenderer`, `ContrastTextRenderer` and
  `GlowRenderer`; their docs moved onto the generic versions.
- The architecture test reads source, not types: `UiDrawsThroughTheSpriteEffectArchitectureTests` fails any code line
  (comment lines pass, since docs compare the batch with `SpriteBatch`) in `Cadence/UIEngine`, `Cadence/Workshop`,
  `DungeonCrawlerWorld/Presentation` or `DungeonCrawlerWorld/Workshop` naming `SpriteBatch`, which catches a local or a
  `new` as well as a member -- and the Tests project may not reference the Workshops' assemblies. A second test proves the
  line check catches code and passes comments.
- `SpriteDrawTargetTests` lost the nine-copies test and gained one for contrast text: one outlined string, rounded, white
  by default.
- Notes: IMPLEMENTATION-NOTES "The sprite effect" (the UI as one pass, why not a text queue or a second shader, the
  measurements, how to measure draw), CLAUDE.md (UIEngine, the bootstrapper's services, the map's pass), TODO.md ("Shader
  follow-ups" and "Replace the text outline with SDF glyphs" are plan 7.3 and no longer wait on windows; "Sprite shaders
  and tinting"'s "Outside the map" line), the epic's child 7 entry. The shader plan had already been deleted, so its
  out-of-scope line went with it.

- Delete `ContrastTextRenderer.DrawCopies`, `SpriteBatchDrawTarget` and the helpers' `SpriteBatch` overloads; remove
  anything left reading `SpriteBatch` in the three roots (an architecture test: no type in UIEngine, Presentation or the
  Workshops names `SpriteBatch`).
- Notes: IMPLEMENTATION-NOTES (the sprite effect section: the UI's pass is the batch, why no text queue, the benchmark
  results), CLAUDE.md (the UIEngine and Presentation bullets on `SpriteEffectBatch`, `ISpriteDrawTarget`,
  `UiRenderService` and the map's pass), TODO.md ("Replace the text outline with SDF glyphs": every caller is on the batch;
  "Shader follow-ups": windows are on the batch, so plan 7.3 can start; "Sprite shaders and tinting": the "Outside the map"
  line), the epic's child 7 entry (7.2 landed, 7.3 next), the shader plan's out-of-scope line.
- In game: a quick pass over Phase 2's checklist after the deletions.

## Risks

- **Cost per sprite.** A `SpriteEffectVertex` is 28 bytes against `SpriteBatch`'s 24, built at draw time; before 7.1 the
  map measured ~10 ns more a sprite at 40 bytes and no regression overall. Windows draw far fewer quads than the map, but
  `BorderRenderer` and `GlowRenderer` draw many 1 px rectangles; the Phase 2 benchmark decides.
- **Every UI pixel through the uber shader** (kept deliberately: a second shader for plain draws would end a run at
  every plain/styled boundary, and the UI interleaves them -- an icon, its outlined text, the next icon). Since 7.1 an
  unlit, unoutlined draw does one texture read and compares on flags that are uniform per quad, so its branches don't
  diverge; the outline's eight taps run only on outline layers. Phase 2's GPU check measures it.
- **Draw calls.** Unchanged in number: one run per texture, ended by the same push/pop as now. Plan 7.3's clipping in the
  shader is what removes the push/pop flushes.
- **Mods** draw nothing in the UI yet, so no mod-facing API changes.

## Out of scope: plan 7.3

- Clipping in the shader, glow rings in one draw, the per-tile targeting overlay: TODO.md "Shader follow-ups" (Engine,
  High).
- SDF glyphs and a scalable outline: TODO.md "Replace the text outline with SDF glyphs and an outline shader".
