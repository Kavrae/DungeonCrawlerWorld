# Plan: shadows under flying entities

Replace the blue "^" layer badge -- "something is on this tile one layer up" -- with a shadow on the ground under
whatever flies there. The shadow is the main way the player knows something is flying above them.

Status: **complete** (2026-11-24). All four phases tested in game; opacity raised to 0.55 after Phase 1's test. The
results are in IMPLEMENTATION-NOTES "Shadows under flying entities".

## The decision: a linked shadow set, not "Visual 2D lighting"

The shadow is drawn from the manifest's existing **`Shadows`** entry: one Cardinal linked set of 16 pieces. The
"Visual 2D lighting" TODO item (Presentation, Medium) stays as it is.

Why not the lighting item:

1. **The lighting item can't start yet.** Its ambient level and light sources draw the gameplay light map that "Field of
   view and perception" (Game, High) will provide, and that item isn't built. The flying cue is information the player
   needs now, so it shouldn't wait for that work.
2. **What the player needs to know is per tile.** The question is "is something flying over this tile?" The map already
   stores that answer as one bit per cell (the Flying bit of `OccupiedLayerMaskByColumn`), and the "^" badge reads that
   same bit today. A linked set only needs the bit plus the bits of the four neighbouring tiles.
3. **A shadow shaped like the sprite costs a lot and adds little.** It needs a render pass of the flying layer's
   silhouettes, a render target and a projection. In this three-quarter top-down view, a squashed silhouette doesn't make
   "something is above you" any easier to read than a soft footprint-shaped patch.
4. **Lighting can still feed it later.** The new draw mode (Phase 2) reads the light under each pixel. When the lighting
   item lands, its light goes into the same texture, so this shadow follows it without any change here.

## The rules

- **Only on Ground, only from Flying.** A Ground tile is shadowed when any occupant on the Flying layer covers it: every
  tile of every footprint, which is the exact bit the "^" badge reads. Underground never receives a shadow, and nothing
  on Ground casts one onto Underground.
- **The badge goes only where the shadow replaces it.** When Ground is shown, the "^" badge is gone. When Underground is
  shown, "^" stays for an occupied Ground tile, because nothing else marks it there. "v" stays everywhere.
- **Shape comes from the linked set.** A shadowed tile draws the `Shadows` piece for its Cardinal mask (which of N, E, S, W
  are shadowed too), so neighbouring flyers' shadows join into one patch with soft outer edges. Neighbours are read one
  tile past the grid, so a shadow cut by the screen edge stays joined. The set has one variant, so the piece is chosen
  with a fixed seed and a moving shadow never flickers between variants.
- **Partial opacity, over what is already there.** The shadow is drawn after everything on its tile in that row (see Draw
  order), as a translucent dark sprite blended over it. It darkens whatever it lands on and never replaces it.
- **It stacks with darkness already on the ground.** Because it is blended, it compounds with darkness that is already
  there: shadows painted into the art, the building shadow sheets, and later the lighting item's ambient. Two Flying
  occupants on one tile (a Blocking one and a Phasing one) set one bit, so they cast one shadow, not a darker double.
- **Bright light on the same layer cancels it.** The shadow's strength is multiplied by `1 - glow strength` under each
  pixel, so strong light washes it out and weak light only thins it. The glow is the one built for the layer shown
  (Ground), so a light on the Flying layer never cancels a shadow.
- **Missing sprite:** if the manifest has no `Shadows` entry, each shadowed tile draws as a plain dark square in the same
  mode, at the same opacity. That's hard-edged, but it never fails.

## Draw order

`MapDrawOrder` plans one `Shadow` item per shadowed grid tile. Each row's shadow items go **after that row's occupants
and before the next row's overhangs**:

- Corpses, item piles, Tiny occupants and anything anchored in the row are drawn first, so the shadow lies over them.
- Anything anchored in a lower row draws after the shadow, so a creature standing in front isn't darkened.
- Known limitation: a tall creature standing in a shadowed tile is darkened only across its own tile, and the part of its
  sprite that reaches up stays lit. Check this in game. If it reads badly, the fix belongs to the lighting item: darken
  standing sprites along their ground row, as the glow already lights them. That stays out of this plan.
- Tiles in the overscan (rows below the grid, columns beside it) get no shadow items. A shadow stays inside its tile, the
  same rule the badges follow.

## Phase 1 -- shadows planned and drawn, "^" gone on Ground

- `MapDrawItemKind.Shadow`, with a `LinkMask` byte on `MapDrawItem` (0 for every other kind).
- `MapDrawOrder.Plan`: when `mapLayer` is Ground, collect the Flying-occupied columns over the grid plus a one-tile
  margin (`GetOccupiedColumnsIn` with the Flying bit), mark them in a reused bit grid, and while walking each grid row,
  emit that row's shadow items after its occupied cells, each with its Cardinal mask from the bit grid. The badge scan
  leaves the Flying bit out of `higherLayerMask` when Ground is shown.
- `MapRenderer`: resolve `Shadows` once in the constructor (`TryGetTerrainSprites`, whose face is the Cardinal set,
  `TryGetFace(seed: 0, mask)`), and draw a `Shadow` item into its tile at `FlyingShadowOpacity` (start at 0.45, tune in
  game). Phase 1 draws with `SpriteDrawStyle.Plain` and no glow, so light doesn't cancel it yet. Fall back to a dark
  square as described in the rules.
- Tests (`MapDrawOrderTests`, through `FakeMapView.SetOccupiedLayers`):
  - Ground shown: shadow items appear on Flying-occupied tiles, in the right row position.
  - The masks join neighbours, including a neighbour just past the grid edge.
  - No shadow items when Underground or Flying is shown.
  - No "^" badge when Ground is shown; "^" still appears when Underground is shown; "v" is unchanged.

Test in game (fairies spawn on the Flying layer):
- Stand on Ground near a fairy. Expect a soft dark patch on the ground under it and no blue "^".
- Watch two fairies side by side. Expect one joined patch with no seam between them.
- Watch a fairy cross a corpse, an item pile and a goblin. Expect the shadow over each, with the goblin's head above its
  tile left lit.
- Switch to Underground under a Ground creature. Expect "^" still shown and no shadow.
- Switch to Flying. Expect no shadows and "v" badges as before.
- Scroll so a fairy's shadow sits at the screen edge. Expect the edge piece to stay joined, not capped.
- Check the Workshop Map tab with an area that places a Flying entity. Expect its shadow on the Ground view.

## Phase 2 -- bright light cancels the shadow

- UIEngine: `SpriteDrawMode.Shadow = 5`, documented as "Plain, its strength reduced by the glow under each pixel: a
  darkening that light cancels". It names no game concept, so it belongs in UIEngine.
- `SpriteUber.fx`: in Shadow mode, read `GlowAt(input.GlowTexel)` (the draw asks for `SpriteGlow.AtPixel`, so the texel
  is computed), multiply `shaded` by `1 - glow.a`, and skip `Lit`. Rebuild the `.fxb` through the build (`fxc`). Never
  hand-edit the binary or its hash.
- `MapRenderer` draws `Shadow` items with that mode, lit at the pixel.
- If "bright" should mean a threshold rather than proportional, the curve is one constant in the shader:
  `saturate(1 - glow.a / BrightGlowStrength)`. Try proportional first.
- Tests: any `SpriteDrawStyle` packing or vertex test that lists modes gains the new value. The shader is checked in game.

Test in game:
- Lead a fairy over lava. Expect its shadow to fade to almost nothing over the bright tiles.
- Lead a fairy over a Healing Shrine's glow edge. Expect the shadow thinner toward the shrine and full outside the glow.
- Lead a fairy past a Torch Light anchor. Expect the shadow to fade as it nears the torch.
- Check that walls' tops, which are never lit, still show the shadow at full strength when it falls on them.

## Phase 3 -- notes

- IMPLEMENTATION-NOTES: a "Shadows under flying entities" section covering the decision above, the rules and the draw
  order, including the tall-sprite limitation.
- CLAUDE.md, under Map drawing rules: one sentence. Ground shows a `Shadows` linked piece under every Flying-occupied
  tile, after its row's occupants, cancelled by glow (`SpriteDrawMode.Shadow`); "^" only when Underground is shown.
- TODO "Visual 2D lighting": one bullet saying the flying shadow's mode reads the glow texture, so lights added there
  cancel it too, and the tall-sprite limitation is solved there if it's ever wanted.

## Phase 4 -- the Inspection window names the shadow

Inspecting a shadowed tile says so, because the shadow is the only sign that something is flying overhead.

- **When:** Basic inspection with Ground shown, when the selected tile has a shadow (the same rule the map uses: something
  on the Flying layer covers it). Underground and Flying inspection get no section.
- **What:** a block with no icon, after the auras: a "Shadow" header, then one line naming the biggest size among the Flying
  occupants covering the tile (`EntitySizeCategories.Of` over each `OccupantView`'s footprint and kind):
  - Normal or Tiny: "Something is in the sky above this tile."
  - Large: "Something large is in the sky above this tile."
  - Huge: "Something huge is in the sky above this tile."
- **Nothing more.** The block doesn't say what the flyer is, and it shows no sprite or count. The player learns what it
  is by switching to the Flying layer.
- **Rebuilds:** the size category shown (or none) joins the values `UpdateBasic` compares before rebuilding, so the block
  appears, changes and goes as flyers move over the selected tile.
- **Where the rule lives:** "the biggest size casting a shadow on this tile" is one method shared by the map and the
  inspector, so the two can't disagree. The map's planner keeps reading the occupancy bit (it only needs yes or no), and
  the inspector reads the Flying occupants through `IMapViewQuery`.
- Tests (Presentation): a shadowed tile with a Normal, a Large and a Huge flyer gives the three texts; an unshadowed tile,
  and any tile while Underground or Flying is shown, gives no block.

Test in game:
- Select the tile under a fairy while Ground is shown. Expect a "Shadow" block saying something is in the sky above it.
- Let the fairy fly off the selected tile. Expect the block to disappear.
- Spawn a Large flyer (Admin Mode, "Spawn here >" on the Flying layer, then switch to Ground) and select one of its
  tiles. Expect "Something large...".
- Do the same with a Huge flyer. Expect "Something huge...".
- Select the same tile while Underground or Flying is shown. Expect no Shadow block.
