# Plan: the target schematic

Replace the tile grid that previews a targeting shape in Item Details, Action Details and the Workshop
(`TargetShapePreviewElement`, `TargetShapePreviewGeometry`) with a **schematic**: a small drawing that says how an
activation gets where it lands and what it covers, with every distance written on it as a number.

Status: **proposed** (2026-11-27). Nothing built.

## Why the grid falls short

- **It draws to scale, so it has to shrink or lie.** Fireball reaches 13 tiles (range 10 + radius 3), and the preview
  fits 11. `FitToPreview` shortens the range until the circle fits, so the picture shows a range the spell doesn't have,
  and the real number only appears in the caption beside it.
- **Most of what a spec says isn't tiles.** The path (Placed, Projectile, Lobbed), the hit count, what walls do, line of
  sight to the aim and across the shape, the minimum range and where a projectile stops. The grid shows none of these,
  so they're either missing or written as more text lines.
- **The numbers sit in a different place from the picture.** "Range: 10" and "Area Size: 3" are lines in the left zone.
  The grid numbers only the caster's own row and counts the caster as 1, so the player has to work out which number
  goes with which part.
- **Comparison only works when the shapes match.** `ComparisonLineStyles.ShapeHighlight` gives up when the compared
  shapes differ, and even when they match it only marks extra tiles green.

## The two inspirations, and what each gives

**The London Underground map: topology over geography.** Distances and angles are made up. What's true is the order
of the stations, what connects to what, and which kind of line it is. In the schematic:

- **Stations.** The caster, the aim (or where the path ends), and the shape's centre are stations: fixed glyphs in
  fixed places.
- **Lines.** The path between them is a line, and its style is its kind. Projectile is a solid line ending in an arrow,
  Lobbed is an arc, and Placed is a thin dotted connector because nothing travels.
- **Length is ordinal, not metric.** A distance is drawn at one of a few fixed lengths (bands, below), so a longer
  range never draws shorter than a shorter one. Two pictures side by side still read the right way round, which is all
  comparison needs from the drawing.
- **Always horizontal, caster on the left.** It reads left to right like the lines beside it, and it fills the wide,
  short space the details row has. The current "aimed north" convention goes.

**The dimension drawing: exact numbers on inexact geometry.** On an engineering drawing the picture can be
approximate, because the dimensions carry the truth. In the schematic:

- **Dimension lines** with extension lines and arrowheads at both ends give a distance and its value: range under the
  path, a line's or cone's length along it, a wall's width across it.
- **Leaders** (a short line from a label to the feature, ending in an arrowhead) carry a radius ("r 3", like "3 × ⌀ 0.53
  THRU ALL") and the rules that have no length: "stops at 1st hit", "needs sight".
- **Centre marks** (a small cross with dashed centre lines through it) mark the aim, as a hole's centre is marked.
- **A break mark** (two short slashes across the path) shows that a long path is drawn shorter than it is. That is the
  drawing's own way of saying "not to scale here", and the number on the dimension still says how long it is.

## The rules

1. **Every number on the schematic is the value as used**, the same value the line it replaces showed: the previewed
   activation's (`ActivatablePreview`: a scroll's range and area are scaled by Intelligence). The schematic never
   works out a number itself.
2. **Numbers sit on their dimension.** "Range" and "Area Size" leave the text column and become the labels on their
   dimension lines. Each label keeps its stat's tooltip (the breakdown), its comparison colour and, in Advanced mode,
   its change: "10 (+2)". "Shape: Circle" leaves the text column too, because the glyph and the label's tooltip say it.
   That frees about three text rows, which the one-screen rule needs.
3. **Bands, never scale.** Lengths in schematic units (u, about one glyph width):

   | Distance | Path drawn | Shape drawn (radius or length) |
   |---|---|---|
   | 0 (on the caster) | none | — |
   | 1 | 1 u stub | 1 u |
   | 2–4 | 2 u | 1.5 u |
   | 5–9 | 3 u | 2 u |
   | 10 and more | 4 u with a break mark | 2.5 u |

   The order of distances is kept within each column. Between path and shape it isn't (a range of 10 next to a radius
   of 3 isn't drawn 10:3), and the dimensions carry that.
4. **One glyph per shape**, drawn at its band size at its anchor: Circle as a circle outline with centre lines, Ring as a
   circle with its band thickened, Square, Cross, Cone as a wedge opening away from the caster, Line as a bar along the
   path, Wall as a bar across it, Sweep as the three facing tiles, Adjacent as a ring of eight small squares, Self as the
   caster's station filled, SingleTarget as the aim's centre mark alone. Flag combinations draw each part (Adjacent |
   Self, the ring plus the filled station). A shape at `TargetAnchor.Caster` has no path: its glyph starts at the
   caster's station, and its dimension measures the shape.
5. **The caster is always the same gold station** (today's marker colour), so the eye starts in the same place every
   time.
6. **Simple mode draws where it lands. Advanced mode adds how it gets there.** Simple: stations, path, shape, range
   and area dimensions. Advanced also draws, each only when the spec departs from the default:
   - **Minimum range:** a hatched dead zone from the caster to a tick, with dimension "min 2".
   - **Projectile hit count:** a ⊗ on the path with a leader, "stops at 1st hit", "stops at 3rd hit" or "passes through
     all".
   - **Path end at maximum range:** the path runs on past the aim's centre mark to its own end tick, with a second range
     dimension.
   - **Walls:** a short hatched wall block across the path. It stops the arrow (StopsAtWall), the path crosses it
     (PassesThroughWalls), or the path folds back off it with "↯ ×2" (BouncesOffWalls).
   - **Line of sight:** an eye glyph and "sight to aim" on the aim station (`AimLineOfSight`), and the shape's outline
     dashed with "walls shield" (`ShapeLineOfSight`).
   - **Overall reach:** a chained dimension above, from the caster to the far edge of the shape ("13"). It's written
     only as a dimension, never computed by the planner: it's the previewed range plus the previewed area size.
   - **Marked only:** `TargetModeAffects.MarkedOnly` draws the shape glyph faint with "one target" (potions).
7. **Timing isn't drawn.** Windup, cooldown and channelling stay as text lines. The schematic is about space only.
8. **Comparison colours the labels, not the geometry.** Each dimension label takes the `LineStyle` its stat already
   gets from `ComparisonLineStyles.ForStat` (green, red or plain, with the difference in Advanced). This works whatever
   the compared shapes are, so `ShapeHighlight` and its tile diff are deleted.
9. **Size.** The schematic takes the middle and right zones of the Activation row (the right one is empty today), and
   its height is capped at the current 96 px. Bands and glyph sizes come from one metrics record, scaled by the row's
   font size, so the Workshop's larger panel draws the same picture bigger, not longer.

## Shape of the code

- **UIEngine: stroke drawing** (`Rendering/StrokeRenderer`, static, holds nothing, any `ISpriteDrawTarget`). A line of
  any angle and thickness (one rotated `FillRectangle`), a dashed line, an arc or circle outline (segments, count from
  the radius), an open arrowhead, a centre mark, a break mark, and a hatch fill of a rectangle. A drawing tool with no
  game would use these unchanged, so they go in UIEngine. `TargetShapePreviewElement.DrawCircle` becomes
  `StrokeRenderer.FillCircle`.
- **Presentation: the planner** (`Presentation.UI.Targeting.TargetSchematicPlanner`, pure). It takes a `TargetingSpec`, the
  mode and `TargetSchematicMetrics`, and returns a `TargetSchematic`: strokes (line, arc, arrowhead, mark, hatch, each
  with a role that picks its colour), the shape glyph, and label slots. Each slot has an anchor point, a side and a
  `TargetSchematicLabel` (Range, AreaSize, MinimumRange, HitCount, Reach, Sight, Walls, MarkedOnly). It never reads a
  pool or a catalog, and it's built once when the element is configured, never per frame.
- **Presentation: the element** (`TargetSchematicElement`, replacing `TargetShapePreviewElement`). It draws the strokes
  in `DrawContent`, in local coordinates. Labels are child `Label` elements placed at their slots, so tooltips, the
  comparison colour and the Advanced change come from `AddStatLine`'s existing path and aren't drawn by hand. Its size
  is the planner's bounding box, worked out up front as the grid's is.
- **`ActivatableDetailsSections.BuildActivationSection`** builds the schematic from the previewed spec and the stats it
  already extracts, and leaves the moved stats (shape, range, area size) out of the text column.
  `ItemComparisonStatExtraction` keeps producing them, because comparison and tooltips still read them.
- **Deleted:** `TargetShapePreviewGeometry.ComputeOffsets`, `ComputeReach`, `FitToPreview` and `ComputeCellSize`'s
  targeting use, `ComparisonLineStyles.ShapeHighlight`, and the `GameFontChrome.TargetShapePreview*` number font
  entries. The aura reach grid keeps `ComputeDiamondOffsets`, `ComputeBounds` and the element's cell drawing until
  Phase 5 decides.

## Phases

Each phase ends with an in-game check before the next one starts.

### Phase 1: strokes in UIEngine
`StrokeRenderer` and its tests (a recording `ISpriteDrawTarget`: a line's quad has the right angle, length and
thickness; an arc's segment count grows with its radius; the arrowhead's two strokes are symmetric about the line).
Swap the grid's player marker onto `FillCircle`.
*Test:* open any spell's Action Details and expect the gold caster marker to look the same as before.

### Phase 2: the planner
`TargetSchematicPlanner`, the metrics, the band table and every shape glyph, in Simple mode only. Tests:
- Every `TargetShape` member, and the flag combinations content uses, gives a glyph.
- The bands are monotonic: for ranges 0–30, a longer range never draws a shorter path.
- A Caster-anchored shape has no path and no Range slot.
- Fireball's plan: caster, solid path with an arrowhead and a break mark, centre mark, circle glyph, Range slot "10"
  under the path, AreaSize slot as a leader to the circle's edge.

### Phase 3: the element in Details
`TargetSchematicElement` replaces the grid in Item Details, Action Details and the Workshop inspector. The moved stats
leave the text column, labels take their tooltips and comparison styles, and `ShapeHighlight` is deleted.
*Test:*
- Open Fireball and expect the schematic shown below in Simple mode, with no "Shape", "Range" or "Area Size" lines on
  the left.
- Hover "10" and expect the range tooltip.
- Read a Fireball scroll with Intelligence above base and expect the scaled range, and in Advanced mode its change.
- Compare a Fireball scroll with a Fireball wand of a different shape and expect the labels coloured.
- Open Melee and Dodge (Caster or Adjacent) and expect no path.
- Open the Workshop's Actions tab and expect the same picture in its inspector.

### Phase 4: Advanced callouts
Minimum range, hit count, path end, walls, line of sight, overall reach and marked only, each only when it departs from
the default. Planner tests for each, plus one that a default spec in Advanced draws exactly what Simple does plus
reach.
*Test:*
- Switch Fireball to Advanced and expect "stops at 1st hit", "sight to aim", "walls shield" and the 13 reach.
- Open a potion and expect "one target".
- In the Workshop, set an action's walls to Bounces with 2 and expect the fold and "↯ ×2".

### Phase 5 (decide before starting): the aura reach
Either draw the aura reach (`BuildLinesBesideReach`) the same way, as a source station, a diamond glyph and a
dimension "r 4", with the falloff as a gradient along the radius leader and "linear" or "flat" on it, or keep the
shaded grid, which already shows falloff per tile and fits within the radius anyway. Recommendation: keep the grid. Its
falloff shading is information the schematic would reduce to a word.

## Open questions

1. Should the moved stats keep a text line as well (a screen reader, copy text)? Recommendation: no. The labels are
   real `Label` elements, so they're already text.
2. Does a card in the Actions window's Card view get a mini schematic (stations and shape, no labels)? Recommendation:
   not in this plan. Cards have no targeting preview today.
