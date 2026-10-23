# The whole UI on the sprite effect

(Follows `PLAN-shaders-and-sprites-tab.md` (epic child 7). Planned 2026-10-23; Decisions 1-7 proposed, awaiting
confirmation.)

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
no flushes, the map stops being a pass of its own, and the TODO.md items waiting on "windows on the batch" are unblocked:
clipping in the shader, glow rings in one draw, and SDF glyphs.

## Context

- **One root pass, a render-state stack.** `GameLoop.Draw` and `WorkshopHost.Draw` begin the shared `SpriteBatch`
  (`SpriteBatchRenderer.StartSpriteBatch`: Deferred, AlphaBlend, PointClamp) and call `ElementPoolService.ResetRenderState`.
  Elements read `ElementPoolService.SpriteBatch`. `PushRenderState`/`PopRenderState` (Element.Draw's two
  `RequiresContentViewport` blocks: viewport and transform for a scrolled element, scissor for a clipped one) end the batch
  and begin it again under the new state. `RenderState` also carries `Effect` and `DepthStencilState`, which nothing sets,
  and every pass samples `PointClamp`.
- **The map and the Sprites previews switch passes.** `ElementPoolService.BeginSpriteEffectPass`/`EndSpriteEffectPass`
  end the `SpriteBatch`, run `SpriteEffectBatch` under the current state, and resume. `MapRenderer.Draw`,
  `SpritePlacementPreview` and `SpriteLinkPreview` use them. `MapTileLayerCache` renders into a render target through the
  batch outside any pass.
- **`SpriteEffectBatch` already does what the UI needs:** quads placed exactly as `SpriteBatch` places them (corners,
  origin, scale, turns and flips; `SpriteEffectQuadsTests`), one draw call per run of one texture, text through
  FontStashSharp's `IFontStashRenderer`, the viewport's projection after the Begin's transform, scissor through the
  rasterizer state. Its glow (`Glow`) is read once, at flush, for every quad in the pass.
- **`ISpriteDrawTarget`** lets the drawing helpers (`SpriteRenderer`, `SpriteOrGlyphRenderer`, `LabelRenderer`,
  `ContrastTextRenderer`, `GlowRenderer`) draw to either target; each still has a `SpriteBatch` overload wrapping
  `SpriteBatchDrawTarget`, whose `DrawOutlinedString` is the nine copies.
- **Size of the change (2026-10-23):** `SpriteBatch`/`spriteBatch` appears about 550 times in about 70 files: UIEngine 38
  files (259), Presentation 29 (239), the Workshops 2 (49), GameShell 1, Tests 5 (comments and the draw-target tests). By
  argument count the calls are `Draw(texture, rectangle, color)`-shaped (41), `Draw(texture, rectangle|position, source,
  color, ...)` (38 six-argument, 9 eight, 3 nine), and a few `DrawString`. The heaviest files: `BorderRenderer` (25),
  `AreaMapView` (17), `MapGameOverlay` (10), `Element` (9), `HotbarContent`, `TextBox` (8 each).
- **Benchmarks:** draw cost is the windowed benchmark's `Draw.GameLoop.Shell.Draw` and `GameLoop/SpriteBatch.End` rows
  (the headless benchmark draws nothing). Phase 1 of the shader plan compared total draw, not one window's row, because a
  window's sprites are flushed by whichever element next ends the shared batch.

## Design

### The batch is the UI's pass (UIEngine)

- The host begins and ends the root pass through `ElementPoolService` (`BeginFrame`/`EndFrame`, replacing
  `SpriteBatchRenderer` and `ResetRenderState`'s separate call), which begins `SpriteEffectBatch` under the root state.
- `ElementPoolService.Batch` (a `SpriteEffectBatch`) replaces `ElementPoolService.SpriteBatch`. `PushRenderState`/
  `PopRenderState` end and begin the batch, as they do the `SpriteBatch` now. `RenderState` loses `Effect` and
  `DepthStencilState`.
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

## Decisions (proposed)

1. **The whole UI draws through `SpriteEffectBatch`**; no `SpriteBatch` is left in UIEngine, Presentation or the
   Workshops. This replaces the out-of-scope line "Outlined text outside the map's pass ... on the effect" in the shader
   plan.
2. **Keep the name `SpriteEffectBatch`**, and rename `ElementPoolService.SpriteBatch` to `Batch` (no
   `SpriteBatch` left to name).
3. **`SpriteBatch`-shaped overloads on the batch**, with the style optional: the conversion is a type change at each call
   site, and the API reads the way FNA code does.
4. **Setting `Glow` ends a run** rather than glow being part of `RenderState`: the map changes glow without changing
   viewport, scissor or transform, and the only thing that reads it is the flush.
5. **Keep `ISpriteDrawTarget`** for the helpers and their tests; delete `SpriteBatchDrawTarget`.
6. **Keep the benchmark row name `GameLoop/SpriteBatch.End`** (CLAUDE.md: row names stay stable for the benchmark skill
   and saved results), now timing the root batch's end. Alternative: rename it to `Batch.End` and update the skill.
7. **Out of this plan:** clipping in the shader, glow rings in one draw and the per-tile targeting overlay (TODO.md
   "Shader follow-ups"), and SDF glyphs ("Replace the text outline with SDF glyphs"). Each is easier once this lands; none
   is needed for it.

## Phases

Each phase ends with a build, both test projects, and an in-game checklist; the next phase starts on the go-ahead.

### Phase 1 -- The batch as a drop-in (no visible change)

- `SpriteEffectBatch`: the `SpriteBatch`-shaped overloads (Decision 3); setting `Glow` ends a run (Decision 4).
- `RenderState` without `Effect` and `DepthStencilState`.
- `MapRenderer` and the Sprites previews still use their own pass; nothing else moves yet.
- Tests: each new overload places the same quad as the general `Draw` with the matching arguments (and as `SpriteBatch`
  would, through `SpriteEffectQuads`); quads queued before a glow change flush under the old glow, those after under the
  new one; an unchanged `Glow` assignment doesn't end a run.
- In game: the map looks exactly as before -- lava glow, a corpse desaturated, glyph outlines, a wall top fading over the
  player. Workshop: the Map and Sprites tabs as before.

### Phase 2 -- Everything on the batch

- Hosts: `GameLoop.Draw` and `WorkshopHost.Draw` (including its draw-during-resize path) begin and end the frame through
  `ElementPoolService`; `SpriteBatchRenderer` goes.
- `ElementPoolService.Batch`; Push/Pop on the batch; `BeginSpriteEffectPass`/`EndSpriteEffectPass` go, and `MapRenderer`,
  `SpritePlacementPreview` and `SpriteLinkPreview` draw into the shared pass (the map setting `Glow` and clearing it).
- Every `SpriteBatch` parameter, local and field in UIEngine, Presentation and the Workshops becomes the batch: the
  element tree (`Element`, `Window`, `Folder`, `TextBox`, `ListView`, `Toggle`, `DropdownField`, `ColorField`, `Button`),
  the renderers (`BorderRenderer`, `BorderedBoxRenderer`, `GridSquareRenderer`, `TileFillRenderer`, `ResourceBarRenderer`,
  `SparklineRenderer`, `MenuModeDimRenderer`, `GlowRenderer`, `RadialFillRenderer`, `ItemIconRenderer`,
  `DragGhostRenderer`, `ToggleActiveMarker`), the game's contents (`HotbarContent`, `PlayerStatusEffectsContent`,
  `PlayerHealthHoverContent`, `InventoryItemStackCell`, `TargetShapePreviewElement`, the ability score rows, `HealthWindow`,
  `TradeWindow`), `MapGameOverlay`, `FloatingTextRenderer`, `AreaMapView`, `SpriteSheetView`.
- Outlined text reaches the shader by itself: `ContrastTextRenderer.Draw` on the batch is `DrawOutlinedString`.
- Tests: the draw-target tests drop the `SpriteBatch` path; a recording target still sees `DrawOutlinedString` once per
  outlined string. `HotbarContentTests`/`FolderTests` comments name the batch.
- Benchmark: windowed Release, three interleaved pairs against a `git archive` build of Phase 1 (`Log/b7`-style), at a
  busy zoom with the hotbar, floating text from a fight, the inventory open with many tabs, and Item Details. Compare
  `Draw.GameLoop.Shell.Draw` plus `GameLoop/SpriteBatch.End`.
- In game (do X, expect Y):
  - Hotbar: key labels, quantities, costs and cooldown numbers keep a hard 1 px black outline; a locked slot's text fades
    fill and outline together.
  - Floating damage text over lava and over a wall: outlined, rising and fading as before, never lit by the glow.
  - Status effect countdowns, HUD bar numbers ("cur / max"), a button's hotkey label (disabled at half alpha), a merged
    stack's badge: all outlined as before.
  - Scroll a long inventory tab and Item Details: content clips at the window's edge as before; a dropdown and a tooltip
    over the map draw on top of it.
  - Open the menu (the dim layer): everything under it dims, nothing drawn over it.
  - The map: unchanged from Phase 1, including inside a scrolled window over it; targeting overlay text outlined.
  - Workshop: every tab, the Map tab's labels and loading text, the Sprites tab's sheet zoomed with its grid; resize the
    window while it draws.

### Phase 3 -- Cleanup and notes

- Delete `ContrastTextRenderer.DrawCopies`, `SpriteBatchDrawTarget` and the helpers' `SpriteBatch` overloads; remove
  anything left reading `SpriteBatch` in the three roots (an architecture test: no type in UIEngine, Presentation or the
  Workshops names `SpriteBatch`).
- Notes: IMPLEMENTATION-NOTES (the sprite effect section: the UI's pass is the batch, why no text queue), CLAUDE.md (the
  UIEngine and Presentation bullets on `SpriteEffectBatch`, `ISpriteDrawTarget` and the map's pass), TODO.md ("Replace the
  text outline with SDF glyphs": every caller is on the batch; "Shader follow-ups": windows are on the batch, so clipping
  is unblocked; "Sprite shaders and tinting": the "Outside the map" line), the epic's child 7 entry, the shader plan's
  out-of-scope line.
- In game: a quick pass over Phase 2's checklist after the deletions.

## Risks and open questions

- **Cost per sprite.** A `SpriteEffectVertex` is 40 bytes against `SpriteBatch`'s 24, built at draw time; the map measured
  ~10 ns more a sprite and no regression overall. Windows draw far fewer quads than the map, but `BorderRenderer` and
  `GlowRenderer` draw many 1 px rectangles; Phase 2's benchmark decides. If it regresses, the first fix is a cheaper
  plain-quad path in the batch, not a return to `SpriteBatch`.
- **Every UI pixel through the uber shader.** Its branches are uniform per quad and the outline's eight taps run only on
  outline layers; measure with full-screen windows open.
- **Draw calls.** Unchanged in number: one run per texture, ended by the same push/pop as now. Clipping in the shader
  (Decision 7) is what removes the push/pop flushes later.
- **Mods** draw nothing in the UI yet, so no mod-facing API changes.

## Out of scope / follow-ups

- Clipping in the shader, glow rings in one draw, the per-tile targeting overlay: TODO.md "Shader follow-ups" (Engine,
  High).
- SDF glyphs and a scalable outline: TODO.md "Replace the text outline with SDF glyphs and an outline shader".
