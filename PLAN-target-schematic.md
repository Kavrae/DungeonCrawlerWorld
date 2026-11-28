# Plan: the target schematic

Replace the tile grid that previews a targeting shape in Item Details, Action Details and the Workshop
(`TargetShapePreviewElement`, `TargetShapePreviewGeometry`) with a **schematic**: a small drawing that says how an
activation gets where it lands and what it covers, with every distance written on it as a number.

Status: **complete** (2026-11-27). All five phases were tested in game; Phase 2 was checked from the planner's output
drawn as SVG. Phase 3's in-game check added the path stretch (rule 3), and Phase 4's revised its callouts (rule 6).
Callouts with no place of their own (sight, path end, walls, one target) stack in a column right of the drawing, each on
a leader. Advanced mode's schematic may be up to 200 px tall. The results are in IMPLEMENTATION-NOTES "Target
schematic". What wasn't built is in TODO.md "Target schematic: what's left".

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

   Where the row has room, a path is stretched to up to three times its band's length (`PathStretch`, the same for
   every band in one schematic, so order is kept). Stretch eases first, in quarters, and only then does the unit
   shrink. Shapes are never stretched.

   The order of distances is kept within each column. Between path and shape it isn't (a range of 10 next to a radius
   of 3 isn't drawn 10:3), and the dimensions carry that.
4. **One glyph per shape**, drawn at its band size at its anchor: Circle as a circle outline with centre lines, Ring as an
   outer and an inner circle (an "r" leader to each: the outer up and to the right, the inner up and to the left), Square,
   Cross, Cone as a wedge opening away from the caster (measured along it and across its far end, so a cone whose width
   ever differs from its length shows it), Line as a bar along the
   path, Wall as a bar across it, Sweep as the three facing tiles, Adjacent as a ring of eight small squares, Self as a
   ring around the caster's station (the station is always filled), and SingleTarget as one small tile square around the
   aim's centre mark. Flag combinations draw each part (Adjacent |
   Self, the ring plus the filled station). A shape at `TargetAnchor.Caster` has no path: its glyph starts at the
   caster's station, and its dimension measures the shape.
5. **The caster is always the same gold station** (today's marker colour), so the eye starts in the same place every
   time.
6. **Simple mode draws where it lands. Advanced mode adds how it gets there.** Simple: stations, path, shape, range
   and area dimensions. Advanced also draws, each only when the spec departs from the default:
   - **Minimum range:** a hatched dead zone from the caster to a tick, with dimension "min 2".
   - **Projectile hit count:** a ⊗ on the path with a leader, "hits 1", "hits 3", "hits all" or "hits none". The full
     sentence is in the tooltip. The leader runs up and to the right where the label clears the shape and any wall,
     else up and to the left.
   - **Path end at maximum range:** the aim's centre mark moves partway along the path, which runs on to its own end
     tick, with "Set distance".
   - **Walls:** a hatched wall block across the path, about two units tall. The path crosses it, with "through walls"
     (PassesThroughWalls), or folds back off it, with "bounces ×2" (BouncesOffWalls). StopsAtWall is the default and
     isn't drawn.
   - **Line of sight:** "Required LoS" on a leader to the aim (`AimLineOfSight`). Its tooltip is "Line of Sight to the
     targeted tile is required." The shape's outline is dashed, with "Blocked by walls" (`ShapeLineOfSight`).
   - **Marked only:** `TargetModeAffects.MarkedOnly` draws the shape glyph faint, with "one target" (potions). In both
     modes its radius reads "Splash 1" rather than "r 1": it hits the marked target alone, or splashes that far.
   - **No overall reach:** range plus radius already says it, so there's no reach dimension.
   - **On a broken path:** the hit mark comes after the break mark, and so do a path-end aim and a wall.
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
  the radius), a filled or outlined diamond (a rotated square), an open arrowhead, a centre mark, a break mark, and a hatch fill of a rectangle. A drawing tool with no
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
  Phase 5 replaces it, and then both grid types are deleted.

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

### Phase 5: the aura reach
The aura reach (`BuildLinesBesideReach`) is drawn in the same style, so the Details window doesn't switch from the
dimension-drawing look to a grid of filled squares partway down:
- **The source** is a station at the centre, the same glyph and colour as the caster.
- **The reach** is a diamond glyph (Manhattan reach, `AuraField.ReachOf`'s rule) at its band size from the shape column.
  It's a square rotated 45°, drawn as a stroke outline over its shading.
- **The shading is the falloff.** Inside the outline, nested diamonds step from the aura's glow colour at full strength
  at the centre to its strength at the edge (`AuraFalloff.ValueAt`, the shades `ReachCellFill` uses today). There's one
  band per distance up to a cap, after which bands sample the distance range evenly. `None` falloff is one flat fill.
  The bands are drawn outermost first, each a filled rotated square (`StrokeRenderer.FillDiamond`).
- **The radius** is a dimension from the station to the diamond's right vertex, labelled "r 4", on its own row under
  any range and length dimensions. The label is a stat line whose tooltip names the aura ("Light aura radius: 3").
- **Planner:** `TargetSchematicPlanner.PlanAuraReach(size, falloff, power, glow, metrics)`, which returns the same
  `TargetSchematic`.
- **One diagram for a placed aura.** When an activation places an aura (an `AuraSourceGrant` among its effects), the
  aura's diamond is drawn at the station where the aura lands, in the Activation row's schematic. There is no second
  reach row. For a Blizzard (range 20, places an r 4 aura) that's the caster, a dotted Placed connector with a break
  mark, and the shaded diamond at the aim's centre mark, with dimensions "20" and "r 4". The aura's heading and lines
  (its effects, "Lasts 10 s") take the text column beside it, and timing stays text (rule 7).
  - When the spec's own shape covers more than its end tile (a Circle that also deals damage on landing), both are
    drawn. The shape's outline goes over the diamond's shading, and each gets its own dimension.
  - A toggle's aura lands on its holder, so its diamond is drawn around the caster's station.
  - Only an aura nothing places (a blueprint's, terrain's) keeps a schematic of its own: the source station and the
    diamond.
- **Deleted:** with this, `TargetShapePreviewElement`, `TargetShapePreviewGeometry` and `AuraReachPreviewMaxHeight` go
  entirely. So do the `GameFontChrome.TargetShapePreview*` number font entries and `BorderedBoxRenderer`, if nothing
  else uses it, because the aura grid is the last thing that does.

Planner tests:
- A Linear reach's band colours never get stronger outward.
- A `None` reach has one band.
- A size over the cap samples evenly and keeps the edge's exact strength.

A further planner test: an activation that places an aura gives one schematic, with the diamond at the aim station.

*Test:*
- Open a Torch's Item Details, or the Toxic Aura action. Expect one schematic, with the diamond around the caster
  station, fading outward.
- Open the Anti-Magic shrine's inspection. Expect a flat diamond with the source station and "r 4".
- In the Workshop, give a Range 20 SingleTarget spell an `AuraSourceGrant`. Expect the Blizzard drawing: one row, no
  second reach grid.

## Decisions

1. The moved stats get no text line of their own. The labels are real `Label` elements, so they're already text.
2. Cards in the Actions window's Card view get no schematic.
3. The aura reach becomes a schematic (Phase 5): a diamond glyph station, a radius dimension, and shading for the
   falloff.
4. An activation that places an aura is one diagram: the diamond at the station where the aura lands, not a second
   row.
