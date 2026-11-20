# Plan: sprites fit their container, a larger inspector portrait, and characteristic icons

From TODO "Sprites taller than a tile render wrongly outside the map", widened in three ways:
- the fit rule for every sprite drawn outside the map;
- the inspection window's portraits at twice the size;
- a row of characteristic icons with tooltips in the inspector's right column, the first one showing the entity's size
  and footprint.

Status: **complete 2026-11-20.** Phases 1-3 are built and checked in game, and the wrap-up is done: IMPLEMENTATION-NOTES
"Sprite fit outside the map" and "Inspection V2", CLAUDE.md, and the TODO entry removed.

Phase 3 as built:
- `CharacteristicIcon` is a value: tooltip text, then either a sprite name and glyph or a footprint.
  `CharacteristicIcons.For(OccupantView)` is the ordered list (Size alone today). The inspector builds the row from it
  with `IconElement` or `FootprintIconElement`.
- `EntitySizeCategory`/`EntitySizeCategories.Of` live in `Game.Blueprints`, beside `MaximumFootprintTiles`.
- The icons are 20 px with a 4 px gap. The row has 6 px above it (in-game testing: a 3x3 footprint at 2 px touched the
  text) and 2 px below, and sits between the text rows and the bar.
- Tooltips already break lines at `\n` (`StringUtility.FormatText`), so no UIEngine change was needed.
- **Not done:** the inspector test. Building `InspectionWindowContent` in a test needs a full game session and its 14
  dependencies. The new pieces are covered by unit tests (`EntitySizeCategoriesTests`, `CharacteristicIconsTests`:
  categories, tooltip text, footprint cell rectangles), and the inspector itself is left to the in-game checks.

Phase 2 as built:
- The health bar is the right column's last row: 8 px tall, centred in a 16 px row, as wide as the column. The old
  full-width bar row (75% width, centred) is gone.
- An aura block has no icon, so its header is now as tall as its rows. Before, it was at least the old 40 px icon
  height; keeping that rule would have made it 80 px.
- No inspector layout test exists yet. Phase 3's inspector test will add the harness.

Phase 1 as built:
- Every sprite drawn outside the Workshop's placement previews goes through `SpriteOrGlyphRenderer`. Item Details,
  ability scores, dropdown and chip rows, and the Workshop's rows reach it through `IconElement`, so the audit came down
  to 13 call sites.
- `Fill` is used only by the map's placed sprites and glyphs, and by `SpriteCellDrawer`, whose only callers are the
  placement and link previews. The Sprites tab has no thumbnails, and its sheet view draws the whole sheet directly.
- The map's loot-bag and charging badges use `Contain`: they are icons on the map, not placed sprites.

## The rule

**A sprite drawn outside the map is scaled to fit its container, keeping its width-to-height ratio, and sits on the
container's bottom edge, centred across.** It is scaled as large as it can be while staying inside the container. A
16x32 sprite in a 40x40 icon draws 20x40, centred across. A 32x16 sprite draws 40x20 along the bottom, so it stands
where its feet would be, as on the map. A quarter-turned cell fits by its turned size. A glyph is unchanged: it is
already centred on its ink.

The map is the one exception. It already places a sprite by its manifest entry's size and pivot inside the reach box
(`SpritePlacementTiles`/`SpritePlacementMath`), then stretches it into that rectangle. This plan doesn't change that.

The fit is decided once, in the shared drawing helper, never per call site:
- `SpriteRenderer` gets a fit mode, `SpriteFit` (UIEngine.Rendering), with two values:
  - `Fill`: stretch to the rectangle, which is what the map does once placement has sized it.
  - `Contain`: the rule above.
- `SpriteOrGlyphRenderer.Draw` passes it through.
- The parameter is **required**, so every call site must state its mode. The resulting compile errors list every site
  the TODO says to check, and none can keep the old stretch by default.
- The fitted rectangle comes from a pure function, `SpriteFitMath.Contain(containerTopLeft, containerSize, sourceSize,
  transform)`, and snaps through `PixelRectangle` as drawing does now.

## Phase 1 -- the fit rule, everywhere a sprite is drawn

1. Add `SpriteFit`, `SpriteFitMath.Contain` and the required parameter on `SpriteRenderer.Draw` and
   `SpriteOrGlyphRenderer.Draw`.
2. Go through every call site the build names and choose a mode:
   - `MapRenderer` (entities, terrain, walls, overhangs): `Fill`, because placement has already sized the rectangle.
   - The Sprites tab's `SpriteCellDrawer` placement preview: `Fill`, because it previews map placement. Its sheet view
     and entry thumbnails use `Contain`.
   - Every other site uses `Contain`. That covers `EntityIconElement` (inspector, loot and container windows),
     `IconElement` and `Button.SetIcon`, `InventoryItemStackCell`, `CurrencyElement`, `ActivatableDetailsSections`,
     `AbilityScoreWindowController`, `DragGhostRenderer`, `Dropdown` and `ListFilterChips` rows, `Folder`, and the
     Workshop's `RecordRow`, `GameInspectorSource` and `ControlGalleryTab`.
3. Tests (`Tests/UIEngine/Rendering`):
   - `SpriteFitMath`: tall, wide and square sources; bottom-edge placement; a quarter turn; a source larger and one
     smaller than the container; whole-pixel snapping.
   - A recording-target test checks that `SpriteOrGlyphRenderer` in `Contain` mode draws the fitted rectangle.

**Phase 1 checks**
- Inspect a tall creature or a shrine whose sprite reaches above its tile: the inspector icon shows the whole sprite,
  narrower than the square, centred across, not squashed.
- Inspect anything with a wide sprite: it sits on the icon's bottom edge, with the space above it.
- Inspect a normal 1x1 creature with a square sprite: it looks the same as before.
- Open a corpse's loot window: its icon fits the same way.
- On the map, tall sprites still draw in their place above their tile, unchanged.
- Workshop Sprites tab: the placement preview is unchanged, and entry thumbnails keep their ratio.
- Drag an item: the ghost keeps the item sprite's ratio.

## Phase 2 -- the inspector portraits at twice the size, health bar in the right column

1. `InspectionWindowContent.IconSize` goes from 40 to 80, for both the entity and the terrain/structure blocks. The
   glyph font follows through `IconGlyphFontFraction`.
2. The right column (text x = `IconSize + RowTextGap`, width `HeaderTextWidth`) holds, top to bottom:
   - name, race, class and "Unsimulated", as now;
   - the characteristic icon row (Phase 3; until then nothing);
   - the health bar.
3. The health bar moves into the header, as the right column's last row, at the column's width.
   `BuildHealthRowIfPresent` stops adding a full-width row to `_body`. The header's height is
   `max(IconSize, rows x RowHeight + icon row + bar row) + padding`, so a creature with every row present grows past
   the portrait instead of overlapping.

**Phase 2 checks**
- Inspect a goblin: the portrait is twice its old size. The name, race and class sit to its right, with the health bar
  under them in the same column. There is no full-width bar below the header.
- Inspect a tile with no occupant: the terrain icon is the same size as an entity's.
- Inspect a skeleton (an unsimulated creature in the Borough): "Unsimulated" and the bar both fit, and the block grows
  if they need more than the portrait's height.
- Inspect a chest or anything else without health: no bar, and no gap where it would be.
- Narrow the inspector: the text rows cut with "...", the bar narrows with the column, and nothing overlaps the
  portrait.
- Damage the inspected creature: the bar follows, as before.

## Phase 3 -- characteristic icons, with Size first

Every characteristic icon is a small square icon with a tooltip, in a horizontal row (`CharacteristicIconRow`, a
`ChildGap` between icons). An icon is a `CharacteristicIcon`: a container whose content is a child element, following
"Control content" (a sprite or glyph through `IconElement`, or a drawn picture), plus its own `TooltipText`.

They are never compared side by side, so each must be readable alone. This is why Size draws the entity's own
footprint rather than one shape at four sizes.

1. **Game: size category and footprint.** Add `EntitySizeCategory` (`Tiny`, `Normal`, `Large`, `Huge`) and one rule,
   `EntitySizeCategories.Of(Vector2Byte footprint, NonBlockingKind kind)`, in Game, because size names are a game
   concept:
   - `Tiny` when `kind` has `Tiny`.
   - Otherwise, by the footprint's largest dimension: 1 is `Normal`, 2 is `Large`, 3 is `Huge`. A 1x2 is `Large`.
   - A footprint larger than any named size would be refused by `ResolvedBlueprint.MaximumFootprintTiles` today. If
     that maximum grows, the rule and its test grow with it.

   Presentation reads the footprint and kind from `IMapViewQuery.TryGetOccupant` (`OccupantView.Size`/`Kind`), which
   skeletons already answer.
2. **The Size icon.**
   - **Tiny:** four dots in a circle, `⁘` U+2058, in Symbola (the font the browser row symbols use). Its sprite name is
     `characteristic-size-tiny`, so art can replace the glyph through the manifest.
   - **Every other size:** the entity's footprint, drawn by a small Presentation element, `FootprintIconElement`.
     - It draws one filled square per tile of the footprint, with a gap between cells, on a fixed scale: cell size =
       icon size / `ResolvedBlueprint.MaximumFootprintTiles`. A 1x1 is visibly smaller than a 2x2, and a 1x2 is one
       column of two.
     - It sits on the icon's bottom edge, centred across, as sprites now do.
     - It reads the maximum rather than hard-coding 3, so a larger maximum later only makes the cells smaller.
     - It draws through `FillRectangle` on the batch, so it needs no art or font.
3. **Tooltip:** two lines.
   - `Size : <size>`, the category name: `Tiny`, `Normal`, `Large` or `Huge`.
   - `Footprint : <columns>x<rows>`, for example `Footprint : 1x2`. Tiny shows its footprint too.

   Check that `Tooltip` lays out a line break as two lines. If it doesn't, add that in UIEngine (a general tooltip
   capability, not one built for this icon).
4. **The row** is built in `BuildHeaderRow`, between the text rows and the health bar.
   - Its icons come from one place, `CharacteristicIcons`, which builds each icon's content and tooltip for an entity.
     Only Size exists today, and a later icon is one more entry there.
   - Terrain and aura blocks have no row.
5. **Tests:**
   - `EntitySizeCategories.Of`: every footprint up to 3x3, Tiny on a larger footprint, and 1x2 as Large.
   - `FootprintIconElement`'s cell rectangles for 1x1, 1x2, 2x2 and 3x3, each bottom-aligned.
   - The tooltip text for each category, including Tiny's footprint line.
   - An inspector test: a creature's block holds the Size icon with its tooltip, and a tile's block has no row.

**Phase 3 checks**
- Inspect a goblin: one icon showing a single small square, above the health bar. Hovering it shows "Size : Normal" and
  "Footprint : 1x1" on two lines.
- Spawn a Tiny Goblin (Admin Mode, "Spawn here >"): four dots in a circle; "Size : Tiny", "Footprint : 1x1".
- Inspect the Goblin Foreman or a shop: a 2x2 block of squares; "Size : Large", "Footprint : 2x2".
- Inspect the player: 3x3 squares filling the icon; "Size : Huge", "Footprint : 3x3".
- Apply Tiny to a goblin ("Apply >"): the icon becomes the four dots.
- Inspect an unsimulated creature: its size shows, from its spawn record.
- Inspect an empty tile or an aura: no icon row.

## Wrap-up

- IMPLEMENTATION-NOTES: a section on "Sprite fit outside the map" (the rule, `SpriteFit`, why the map is `Fill`, the
  bottom edge) and one on "Characteristic icons" (the row, `CharacteristicIcons`, Game's size rule, the footprint drawn
  on the maximum's scale).
- CLAUDE.md: one line in UIEngine's drawing helpers on `SpriteFit` being required, and one in Presentation on
  characteristic icons.
- Remove the TODO entry.
