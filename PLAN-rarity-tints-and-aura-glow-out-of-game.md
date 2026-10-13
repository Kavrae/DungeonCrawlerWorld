# Rarity Tints and Aura Glow out of Game

(Child 2 of `EPIC-sprites-ui-engine-and-dev-tools.md`. Landed 2026-10-13, both phases; durable notes in
IMPLEMENTATION-NOTES.md "Loot boxes" and "Auras, terrain contact, Healing Shrine, Holy Ground". Where the build
differs from the text below, each phase's "As built" says so.)

## Context

What exists today:
- **Loot box rarity is a colour Game picks.** `Game/Modules/Lootboxes/LootboxRarityColors` maps each
  `LootboxRarity` to an XNA `Color`. `LootboxCatalog.GetOrCreateItem` uses it twice on every box's
  `ItemDefinition`: as `GlyphColor` and as `SpriteTint`, multiplied into the type's sprite (the chest,
  `LootboxCatalog.DefaultSpriteName = "Inventory"`, for every built-in type -- none sets `SpriteName`).
- `ItemDefinition.SpriteTint` exists only for loot boxes. Readers: `InventoryGridContent` and
  `LootboxResultsContent` pass it to `InventoryItemStackCell.Configure`, `ItemDetailsWindow` to
  `ItemIconElement.Configure`. `InventoryActions.AreEquivalentOverrides` compares it.
- **The manifest already tints.** `SpriteManifestCell.Tint` ("#RRGGBB(AA)") is resolved into
  `SpriteView.Tint` by `SpriteManifestResolver` and multiplied by `SpriteOrGlyphRenderer` into whatever tint
  the draw site passes -- every reader, icons included. A tinted variant needs only a manifest entry.
- Presentation's rarity text colour (`LootboxChrome.HeaderColor`, the loot box results headers) reads
  Game's `LootboxRarityColors`.
- **Game blends the glow.** `AuraField.TryGetGlow(position, out Color, out int totalPower)` averages the
  `AuraDefinition.GlowColor` of every aura reaching a cell, weighted by power. `Game.Views.AuraGlowView` wraps
  it with `Version`; `MapWindow.DrawGlowOverlay` reads it per visible tile into the glow cache
  (`MapTileLayerCache`, re-rendered on camera moves and when `Version` changes), scaling by
  `Min(totalPower, FullGlowPower) / FullGlowPower * GlowOpacityMultiplier`.
- `TryGetGlow` is also the only thing in Game that reads `GlowColor`, and the only reader that calls
  `AuraField.EnsureBuilt()` outside `AuraSystem` (so a glow read before the first frame still sees the
  terrain's auras).
- Tests read `TryGetGlow` as a "does the aura reach here" check: `AuraFieldTests`, `AuraSystemTests`,
  `HolyGroundTests`, `HealingShrineTests`. `TestMapWindows` builds an `AuraGlowView`.

## Design

### Loot box sprites: one manifest entry per rarity

- **Naming is a convention on the type's base name**: `LootboxCatalog.SpriteNameFor(LootboxTypeDefinition
  type, LootboxRarity rarity)` = `$"{type.SpriteName ?? DefaultSpriteName}-{rarity}"`. `DefaultSpriteName`
  becomes `"Lootbox"`, so a built-in box is `Lootbox-Bronze` ... `Lootbox-Celestial`. `LootboxTypeDefinition.
  SpriteName` is documented as a base name: a mod type that sets one ships its six entries.
- **The manifest gains six entries**, each the chest cell `"Inventory"` uses (Furniture.png, column 47,
  row 11) with a `Tint` equal to today's rarity colour (`#CD7F32`, `#C0C0C0`, `#FFD700`, `#A0E6E1`,
  `#FF8000`, `#BE8CFF`). The multiply is the same one `SpriteTint` did, so every box looks exactly as before.
  Real per-rarity art later replaces an entry's cell and drops its tint, and nothing else changes. The
  entries are authored in `SpriteManifestBuilder` (its tint swatch already edits a cell's `Tint`), not by
  hand.
- `"Inventory"` stays the inventory UI's own sprite; box art no longer shares its name.
- `ItemDefinition.SpriteTint` is deleted, with its `AreEquivalentOverrides` term, the `spriteTint`
  parameters of `InventoryItemStackCell.Configure` and `ItemIconElement.Configure`, and the three call
  sites' arguments. `InventoryItemStackCell` keeps its greyed-out `Color.Gray` (a draw-site state, which the
  cell's tint multiplies into as it does for every other item).
- A missing rarity entry is the ordinary missing-sprite case: the box draws its glyph, and startup warns.
  `ContentSpriteNames` lists each loot box type once per rarity (`SpriteNameFor`) instead of the base name,
  so `MissingSpriteCheckTests`' built-in set test fails if a built-in box's entry is missing.

### Rarity colour is Presentation's

- `LootboxRarityColors` leaves Game. Its table moves to `LootboxChrome.RarityColor(LootboxRarity)`
  (Presentation/UI/Chrome, where window colours live), replacing `HeaderColor`. It is for text and chrome --
  the results headers today, tooltips or names later -- never sprites.
- **A box's glyph colour is its type's**: `LootboxTypeDefinition` gains `Color? GlyphColor` (null for
  `LootboxCatalog.DefaultGlyphColor`, `Color.Gold`), used for every rarity. Game holds no rarity colours at
  all. The glyph is only seen when a sprite is missing; the rarity is still in the box's name.
- The manifest tints and the Presentation palette are the same colours today by choice, not by rule, so no
  test ties them together: per-rarity art would break such a test for no reason.

### Aura glow: Game reports power, Presentation blends

- **`Game.Views.AuraGlowView` becomes `AuraPowerView`** -- glow is a Presentation word; Game reports where
  auras reach and how strongly. Built from `context.AuraField` and `context.Auras`. Members, each calling
  `AuraField.EnsureBuilt()` first as `TryGetGlow` did (a bool check):
  - `Version` -- unchanged meaning (source added, removed, moved, or a definition replaced).
  - `AuraIdsInField` -- the auras that can reach anything (`AuraField.AuraIdsInField`).
  - `AnyAuraReaches(x, y, mapLayer)` -- the coverage bit, for ruling a tile out.
  - `PowerAt(x, y, mapLayer, auraId)` -- the cell's total for that aura (`AuraField.GetTotalPowerAt`).
  - `GlowColorOf(auraId)` -- the definition's `GlowColor`, read from the catalog by id each time, as
    `TryGetGlow` did, so a re-registered definition shows on the next re-render.
- `AuraField.TryGetGlow` is deleted; Game no longer blends colours. `AuraDefinition.GlowColor` stays: it is
  content (what the aura looks like), read by Presentation through the view.
- **Presentation blends**: `AuraGlowBlend` (Presentation.Rendering) -- `BeginPass(AuraPowerView)` copies
  the in-field ids and their colours once per cache render (one catalog read per aura, not per tile per
  aura), and `TryGetGlow(x, y, mapLayer, out Color glowColor, out int totalPower)` does today's weighting
  over that list. `MapWindow` holds one and calls it from `DrawGlowOverlay`; the opacity curve
  (`FullGlowPower`, `GlowOpacityMultiplier`) stays in `MapWindow` -- child 3 moves both into the renderer.
- Per-aura power at a cell is what "Aura glows pulse outward from the source" (TODO.md) needs, so that
  item no longer needs a Game change.
- An area read (a buffer of powers per aura, like `CopyCells`) is not added: the glow is drawn into a cache
  rebuilt on camera moves and field changes, and the per-tile reads are the ones `TryGetGlow` already made.
  If the glow cache rebuild ever shows up in a frame, the area read belongs to child 3's renderer.

### Out of scope

- Moving the glow under wall tops ("Aura glow shouldn't tint wall tops", TODO.md, Low): child 7 (shaders)
  or child 3.
- Per-instance tinting masks for potions ("one silhouette, many colors", TODO.md): shaders, child 7.
- Mod manifests (a mod type's six entries need "Content-only mods" to load); until then a mod box draws its
  glyph and warns.

## Decisions (confirmed 2026-10-13)

1. **Base name + rarity**: a box's sprite is `"{type base}-{Rarity}"`, default base `"Lootbox"`; six
   tinted manifest entries for the built-ins. No per-type rarity map.
2. **A box's glyph colour is its type's** (`LootboxTypeDefinition.GlyphColor`, default gold); Game keeps no
   rarity colours, and the rarity palette lives only in Presentation for text.
3. **Per-aura power reads** through `AuraPowerView`; Presentation blends. No area read in this plan.

## Phases

Each phase builds, passes `dotnet test`, and is checked in-game before the next.

### Phase 1 -- loot box rarity sprites in the manifest; rarity colours to Presentation
- `SpriteManifestBuilder`: add `Lootbox-Bronze` ... `Lootbox-Celestial` (the chest cell, each tinted).
- Game: `LootboxCatalog.SpriteNameFor`, `DefaultSpriteName = "Lootbox"`, `DefaultGlyphColor`;
  `LootboxTypeDefinition.GlyphColor`; `GetOrCreateItem` names the rarity sprite and the type's glyph colour
  and sets no tint. Delete `ItemDefinition.SpriteTint` (and its `AreEquivalentOverrides` term) and
  `LootboxRarityColors`. `ContentSpriteNames` lists each type per rarity.
- Presentation: `LootboxChrome.RarityColor` replaces `HeaderColor`; drop the tint parameter from
  `InventoryItemStackCell.Configure`, `ItemIconElement.Configure` and their three callers.
- Tests: `LootboxCatalogTests` -- a box names `"{base}-{Rarity}"` (default and a type with its own base),
  carries its type's glyph colour (default and set); `ContentSpriteNames` lists six names per type, declared
  by the type; `MissingSpriteCheckTests`' built-in set still pins only its four known gaps (so the six entries
  exist); `LootboxResultsContentTests` read `LootboxChrome.RarityColor`; the shipped manifest round-trips
  (`SpriteManifestFormatTests`, unchanged).
- As built: the six entries were written into the JSON directly, in the builder's own serialized form (the
  manifest round-trip test, `Serialize(Parse(file))` byte for byte, passes with them); the in-tool check
  below confirms the builder reads and saves them unchanged.
- **In-game / in-tool:** open the six entries in the builder -- tint swatches show the rarity colours; save
  unchanged, `git diff Content/SpriteManifest.json` empty. In game (Admin grant a box of each rarity): every
  box in the inventory, hotbar, Item Details and loot box results looks as before; results headers keep
  their rarity colours; boxes of one kind still stack, boxes with overridden contents still don't. No new
  missing-sprite warning at startup. Temporarily rename one entry: that rarity draws a gold "L" and warns
  once; revert.

### Phase 2 -- aura glow blended in Presentation; docs
- Game: `AuraGlowView` -> `AuraPowerView` (members above); `GameViews.AuraGlow` -> `AuraPower`; delete
  `AuraField.TryGetGlow`. Fix the remarks that name the glow view (`AuraField`, `ScrollOfTorch`,
  `ActionsModule`).
- Presentation: `AuraGlowBlend`; `MapWindow` and `MapTileLayerCache`'s remarks use it;
  `ElementFactoryRegistry` passes `views.AuraPower`.
- Tests: `AuraFieldTests`' glow tests become power tests (`AnyAuraReaches`/`GetTotalPowerAt`: reach, edge
  value, overlap adds, removal clears, across a neighborhood border); the blend's colour and power
  assertions move to `AuraGlowBlendTests` (Presentation) over a real `AuraField` -- one aura, two auras
  blended toward the stronger, a replaced definition's colour after `BeginPass`, nothing where nothing
  reaches, a read before any system ran still sees terrain auras. `AuraSystemTests`, `HolyGroundTests`,
  `HealingShrineTests` assert reach through the field's power. `TestMapWindows` builds an `AuraPowerView`.
- Docs: CLAUDE.md (Auras bullet: `AuraField` reports power, `GameViews.AuraPower` hands it to Presentation,
  which blends; Presentation bullet: the rarity palette is `LootboxChrome`'s); IMPLEMENTATION-NOTES "Loot
  boxes" (rarity sprites by name, glyph colour per type, no `SpriteTint`) and the aura notes (where the glow
  is blended); the epic's child 2 marked landed; TODO.md -- delete "SpriteDefinition: tint on the sprite
  itself" (done: tints are the sprite's, sizes already were), and point "Aura glows pulse outward" and "Aura
  glow shouldn't tint wall tops" at `AuraPowerView`/`AuraGlowBlend`; `PLAN-data-driven-actions-and-items.md`
  drops `SpriteTint` from the item fields.
- As built:
  - `AuraGlowBlend` takes the view in its constructor (`MapWindow` builds it from the one it is given), and
    `BeginPass()` takes nothing; `DrawGlowOverlay` begins a pass, so the cached and uncached paths both do.
  - Without `TryGetGlow`'s build-on-read, `AuraFieldTests.TerrainUnloadingThenLoaded_...` builds its field
    explicitly; the read-before-any-system case is now pinned on the view (`PowerView_BeforeAnythingBuilt...`)
    and on the blend.
  - `HolyGroundTests.GlowsWhite` asserts the power and the definition's colour through the view.
  - TODO.md's "Tile size" note named "SpriteDefinition" sprite scale; it now names the manifest's
    `PixelsPerTile`.
- **In-game:** lava rows glow orange, Holy Ground white, the healing shrine and Toxic Idol their colours,
  overlapping auras blend toward the stronger; Scroll of Torch's light moves with its holder and fades;
  Toxic Aura on and off; walk until a neighborhood unloads and back -- glow returns; zoom to Borough and pan
  -- no stale glow.

## Tests (summary)

- Game: box sprite names by convention, glyph colour per type, no tint on items, `ContentSpriteNames` per
  rarity, aura reach and power through the view, no colour blending left in Game.
- Presentation: rarity palette for headers, `AuraGlowBlend` weighting and colour refresh, built-in sprite
  names present (through the missing-sprite check).
- No benchmark: the glow does the same reads into the same cache, and loot boxes are UI only.
