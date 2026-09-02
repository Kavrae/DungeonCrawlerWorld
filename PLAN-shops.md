# Shops

(Landed. Saved here per this repo's design-doc convention -- see `PLAN-loot-currency.md` for the
same shape used pre-implementation; this one documents what actually shipped, including several
fixes that only surfaced via live testing.)

## Context

`TODO.md`'s "Shops" item was the last piece of the storage-containers/loot-currency arc
(`PLAN-storage-containers.md`, `PLAN-loot-currency.md`): a shop is a container-like entity that
trades items for Gold at a price derived from a new per-item `Value` and a per-shop buy/sell
modifier. Standard RPG merchant convention -- the player pays *more* than an item's `Value` to buy
from a shop, and receives *less* than `Value` selling to one.

## Design

### Item pricing

`ItemDefinition.Value` (`Game/Modules/Inventory/ItemDefinition.cs`) -- an item's base worth in Gold,
not itself a price (a shop's own multiplier applies on top). Every current item hand-assigned a
unique 1-20 value. Shown in `ItemDetailsWindow` as a new row (icon+text, mirrors `BuildNameRow`)
just above the tags divider. Player's starting Gold changed from a random 1-10 roll to a flat 100
(`StartingCurrencyGrant.GrantFixedStartingGold`) -- deliberately not random, so a fresh spawn always
has enough to test shop buying.

### Shop blueprints -- composition, not inheritance

`Game/Blueprints/Objects/`:
- **`Shop`** -- the shared shell part (same component set `TreasureChest` merges -- `ContainerComponent`,
  1000 HP, Poison/Paralysis immunity, `CurrencyComponent` with 1000 starting Gold -- minus the
  loot-table fill loop). No `ShopComponent`, no stock: "a shop blueprint does not contain any items
  by itself."
- **`PotionShopStock`/`GeneralShopStock`** -- the "class" half of the pairing: adds `ShopComponent`
  (allowed tags + buy/sell multipliers) and a random stock pull via the shared `ShopStock.
  GrantRandomStock` helper. Potion Shop: `[Tag.Potion]`, 10% modifier (buy x1.10, sell x0.90).
  General Shop: any tag, 20% modifier (buy x1.20, sell x0.80) -- a specialist's focus earns a
  better deal than a generalist's convenience, and both margins leave headroom for a future
  Charisma/skill-based reduction.
- **`PotionShop`/`GeneralShop`** -- named `CompositeBlueprint` wrappers composing shell + stock,
  the exact same shape `GoblinEngineerBlueprint` already established for race+class. Each also
  carries an `overrides` step renaming the shared "Shop" shell to "Potion Shop"/"General Shop" via
  `TryUpdate`, not another `DisplayTextComponent` `Merge` -- `CoreModule`'s merge policy
  concatenates `Name`/`Description` across composed parts (by design, so e.g. `GoblinEngineerBlueprint`
  gets "Goblin Engineer" for free), which would otherwise read as "Shop Potion Shop" instead of
  cleanly replacing it.

`TestMapBuilder` spawns one of each near the player, replacing the old `TreasureChest` spawn.

### `Game/Modules/Shops/` -- trading rules

- **`ShopComponent`** (`AllowedTags`/`BuyMultiplier`/`SellMultiplier`) -- `AllowedTags: null` means
  "any tag" (General Shop).
- **`ShopActions`** -- `CanTrade` (tag match), `ComputeBuyPrice`/`ComputeSellPrice` (item Value *
  the shop's own multiplier), `TryBuyFromShop`/`TrySellToShop` (check-then-commit: tag, capacity,
  affordability all verified before either half of the swap runs; the currency leg commits first,
  rolled back if the item leg still somehow fails afterward). Reused everywhere an item crosses a
  shop boundary -- context-menu Give/Take and drag-and-drop alike.
- **`CurrencyActions`** gained an exact-amount `TryTransfer(..., CurrencyType, int amount)` overload
  (the existing one only ever moved a whole balance) -- what a shop trade's Gold leg actually calls.
- **`GoldGivenToShopEvent`** -- published whenever a player successfully gives Gold to a shop (never
  the reverse -- see Give-only currency below); the "Angel Investor" achievement's trigger
  (`Reward: None`, deliberately temporary per the original ask).

### Shop UI

- **`ShopWindow`/`ShopWindowController`** (`Presentation/UI/Shops/`) -- copies
  `SecondaryInventoryWindow`/`SecondaryInventoryWindowController`'s cascade-placement/toggle shape
  as a template rather than extending it (a shop's summary has no killer/died-tick, and it drives
  `MapViewState.OpenShopEntityId` instead of `LootedComponent`). A corpse/container window and a
  shop window are mutually exclusive -- opening either force-closes the other first (both cascade
  off the same player-inventory-window position).
- **`MapWindow`** gained a "Shop" context-menu option (gated on `ShopComponent`, excluding the
  generic "Loot" option a shop would otherwise also qualify for via its own `ContainerComponent`).
- **Give-only currency**: a player can Give Gold to a shop but never Take it back --
  `CurrencyRowContent.BuildCurrencyContextMenu` suppresses Take/Take All when the secondary target
  carries `ShopComponent`; `UiInputController.TryStartContentDrag` refuses to start a drag from a
  shop's own `CurrencyElement` (dragging the *player's* Gold onto a shop is unaffected).

### Shop item grid

`InventoryGridContent.CellSize` bumped 24->36 (50%, readability). A new shop mode -- both the shop's
own grid and the player's own inventory grid switch together, driven by
`MapViewState.OpenShopEntityId` -- swaps in **`ShopItemStackCell`** (`Presentation/UI/Content/`,
4x `CellSize`'s own width): sprite (left, a `CellSize.Y` square) + truncated name (top-right) +
price (bottom-right, `"{total}G ({perItem} each)"` for a stack, plain `"{price}G"` for one unit).
`InventoryItemStackCell` un-sealed so `ShopItemStackCell` could subclass it, reusing
`GridSquareRenderer`/`GlowRenderer` unchanged for base fill/hover/selected/eligible-glow -- only the
icon-and-quantity portion of `DrawContent` differs.

**Eligibility gating** reuses `CellCompareState` (`Eligible`/`Ineligible`/`None`) exactly as Item
Details Comparison already established it, just driven by a different predicate while a shop is
open: `InventoryGridContent.UpdateShopEligibilityState` resolves each cell's tag match (`ShopActions.
CanTrade`) and affordability (does whichever side is paying have enough Gold) every frame -- no new
draw code needed, the grey-out/green-glow language already existed. Ineligible cells can't be
dragged (`UiInputController.TryStartContentDrag`) or given/taken (`InventoryGridContent.
BuildItemContextMenu`) -- closes the currency-drain-style exploit a naive reuse of plain Give/Take
would otherwise open.

Shop mode always renders one cell per physical stack, regardless of `GroupDivergedStacks` -- see
Live-testing fixes below for why.

### `ShopWindow`'s own shape

Deliberately the *opposite* grid shape from the loot window: `ShopWindow` is 2 columns x (at least)
5 rows, `SecondaryInventoryWindow` is 5 columns x (at least) 2 rows -- a `ShopItemStackCell` holds
far more information per item (sprite, name, *and* price) than a loot cell (icon + quantity badge),
so a shop reads better as a scannable vertical list than a wide grid.

### Angel Investor achievement

`Game/Modules/Achievements/Definitions/AngelInvestorAchievement.cs`, registered in
`AchievementModule.Definitions` -- unlocks on the first `GoldGivenToShopEvent`
(`AchievementTriggerContext.SubscribeUntilTriggered`), `Lootbox: null`/`RewardText: ""` (temporary,
per the original ask).

## Live-testing fixes

Several bugs only surfaced once the feature was actually running:

- **Footer sizing**: `SecondaryInventoryWindow`/`ShopWindow`'s own `ComputeOuterSize` relied on
  `outerInsets` (`CurrentSize - ContentSize`) to already carry `FooterHeight` -- it didn't; the
  currency row visibly clipped/overlapped the grid's bottom row until `FooterHeight` was added
  explicitly on top.
- **Grid clipping**: the inner scrollable grid-hosting `Window` never zeroed its own
  `ContentPadding`, so the default 4px inset shrank its usable area below the exact row budget
  `ComputeGridHeight` computed -- the same one-line fix `GridControl.Build` already applies to
  itself.
- **Drag ghost stretch**: `UiInputController.TryStartContentDrag` captured a dragged cell's full
  `CurrentSize` as the ghost's draw size -- square for a plain cell, but a `ShopItemStackCell` is
  4x wider than tall, stretching the dragged icon. Fixed by using `Min(width, height)` on both axes.
- **Purchased items reading as permanently disabled**: the real cause, not a cosmetic one -- the
  player's starting kit and a shop's own random stock draw from the same item catalog, so buying
  something already partly owned is the *common* case. `InventoryActions.TryTransferStack` never
  merges into an existing stack on the destination, so the player ends up with two separate
  physical stacks of the same item id -- which `InventoryGridContent`'s default same-item grouping
  collapsed into one "Merged Stack" badge cell (no single `StackInstanceId`, so it could never
  actually be priced, given, taken, or dragged -- permanently Ineligible by construction). Shop
  mode now forces one cell per physical stack (`BuildCellEntries`), sidestepping merged-stack shop
  trading entirely rather than building batch-trade support for a case that shouldn't need to exist
  while shopping. Verified by temporarily reverting the fix and confirming the regression test
  failed exactly as the live bug did.
- Two rounds of price/name-line font-size and vertical-spacing tuning on `ShopItemStackCell`,
  settled by direct visual feedback rather than a fixed design spec.

## Verification

Unit tests across `Tests/Modules/Shops/ShopActionsTests.cs` (tag gating, pricing math, buy/sell
atomicity), `Tests/Blueprints/BlueprintTests.cs` (Shop/PotionShop/GeneralShop component sets),
`Tests/Modules/Currency/CurrencyActionsTests.cs` (exact-amount transfer), `Tests/Modules/
Achievements/AchievementModuleTests.cs` (Angel Investor), `Tests/Presentation/MapWindowTests.cs`
(Shop context-menu option), `Tests/Presentation/CurrencyRowContentTests.cs` (Give-only + the
achievement event), `Tests/Presentation/InventoryGridContentShopModeTests.cs` (shop-mode cell
switching, eligibility, the merge-collision regression), and real `UiInputController`-driven
drag/press/release tests in `Tests/Presentation/UiInputControllerTests.cs` (ineligible cells refuse
to even start a drag; an eligible drag actually sells at the shop's own price; the drag ghost stays
square). Full suite green throughout (the one intermittent failure across the whole session was
`AbilityScorePerformanceTests`' pre-existing, unrelated hardware-timing-sensitive flake). Manual
in-game verification at the end of each phase, plus after every live-testing fix, confirmed working
by the user.
