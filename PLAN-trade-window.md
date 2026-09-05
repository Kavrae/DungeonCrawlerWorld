# Trade Window (Shops, iteration 3)

## Context

Shops today (`PLAN-shops.md`) support exactly two transaction shapes, both immediate and both
whole-stack: drag one stack from the player's grid onto the shop's grid (sell), or the reverse
(buy) -- see `ShopActions.TryBuyFromShop`/`TrySellToShop`, each moving and pricing one exact
`stackInstanceId` via `InventoryActions.TryTransferStack`. Trading five different items for two
others today means five-plus separate drags, each landing at whatever the shop's *current* stock
band happens to price it at -- tedious, and it gives the player no way to build a multi-item offer
and see its total value before committing.

Baldur's Gate 3's own trade screen is the reference: opening a shop shows the player's inventory on
one side, the shop's on the other, and a barter panel in the middle where items dragged from either
side sit until the trade is confirmed, with both sides' running totals shown live.

This plan adds that middle panel -- a **Trade Window** -- as a third window alongside the two that
already exist, built almost entirely out of primitives Phases 1-5 (`PLAN-stock-based-shop-pricing.md`)
already landed: `ShopStockPricing`'s bulk pricing, `InventoryGridContent`'s grid/drag/context-menu
machinery, `InventoryActions`/`CurrencyActions`'s transfer primitives. Nothing here needs a new
pricing model -- it reuses the existing one against a *staging area* instead of an immediate trade.

## Entity model: two reserved trade-offer entities

Two plain ECS entities, reserved once at startup the same way the player entity is
(`FloorBuilder.ReservePlayerEntity`, called from `WorldSessionBootstrapper.cs`) -- add
`FloorBuilder.ReserveTradeOfferEntities(EcsContext)` returning `(int PlayerSide, int ShopSide)`,
called once alongside `ReservePlayerEntity`. Reused across every trade session for the life of the
game (created once, never destroyed) -- "only one trade can be opened at a time" is already true by
construction, since only one shop can be open at a time (`MapViewState.OpenShopEntityId` is a single
nullable field, not a set), and the trade window's own lifecycle is tied 1:1 to the shop's.

Neither entity needs `TransformComponent`, map placement, or a display name -- just whatever
`InventoryActions.AddItem`/`TryTransferStack` already auto-provisions
(`InventoryGrant.EnsureInventoryComponentExists`) plus a `CurrencyComponent` for the footer, which
`CurrencyActions.TryTransfer` already auto-merges on first write. Both ids are fixed for the whole
session -- thread them in via constructor injection wherever the trade grids/controller are built
(the same way `IPlayerQuery`/`world.PlayerEntityId` already gets threaded into controllers), not as
mutable `MapViewState` fields -- `MapViewState`'s existing fields are all genuinely per-frame/per-
session UI state (`OpenShopEntityId`, hover slots); these two ids never change after bootstrap.

**Clearing.** Every code path that ends a trade (Complete, Cancel, shop closes) moves every stack and
every currency unit *out* of both trade entities back to their real owners -- by the end of any of
those three paths both trade entities are empty by construction, so no separate "clear" step is
needed. (A defensive `componentManager.GetMultiPool<InventoryItemStackComponent>().Remove(entityId)`
per side is cheap insurance if a future bug ever leaves something behind, but shouldn't be load-bearing.)

## Window layout

Today: `ShopWindowController.OpenShop` opens the player's `InventoryManagementWindow` first (if not
already open), then places `ShopWindow` via `WindowCascadePlacement.ComputePosition(playerWindow.
Rectangle, playerWindow.CurrentSize, siblingCount: 0, ...)` -- 12px to the right, same size, offset
diagonally per sibling. That cascade shape (diagonal stagger, one anchor per window) is built for
popups/comparison columns and doesn't fit what this feature needs.

**New layout, anchored off the trade window instead of the inventory window:**

1. Trade window opens first, centered on screen both horizontally and vertically (`(screenSize -
   tradeWindowSize) / 2`) -- a new, plain centering calculation, not `WindowCascadePlacement`
   (nothing here cascades off a sibling).
2. Player inventory window is positioned to the trade window's *left*, its own right edge `Gap` (the
   existing 12px) from the trade window's left edge, **top edge aligned to the trade window's own
   top edge** (same Y) -- not a diagonal cascade offset.
3. Shop window is positioned symmetrically to the trade window's *right*, same top-edge alignment.

Net effect at open time: `[Player Inventory] [Trade Window] [Shop Window]`, all three top-aligned,
trade window dead-center on screen. This is a distinct, new positioning routine (call it e.g.
`TradeWindowLayout.ComputeInitialPositions`) rather than a reuse of `WindowCascadePlacement` --
different shape (aligned-beside vs. diagonal-cascade), so forcing it through the existing helper
would just be `siblingCount: 0` with extra unused stagger logic sitting dead.

After opening, the player can freely move and resize the inventory and shop windows exactly as
today (no change there) -- but **the trade window itself cannot be resized** (`CanUserResize` simply
left at its default `false`, unlike every other window here which explicitly opts in with
`CanUserResize = true`; see Trade grid capacity below for why -- a fixed 20-slot, non-scrolling body
has no reason to grow). It can still be *moved*.

**Opening/closing.** A new `TradeWindowController` owns the `TradeWindow`'s own lifecycle (creation,
positioning, the footer's three buttons). It opens right after the shop window in
`ShopWindowController.OpenShop`, and all three windows close together -- closing any one of the
three closes the other two as well.

**`CanUserClose = true` on the trade window (landed correction)** -- confirmed live that leaving it
`false` (this plan's original design, "no X of its own, only Cancel/shop-close/inventory-close end a
trade") broke Escape entirely: `UiInputController.CloseTopmostClosableWindow`/`CloseAllClosableWindows`
both give up and do *nothing* the instant the topmost menu window has `CanUserClose` false, rather
than falling through to whatever's behind it -- and `UiLayerStack.Add` auto-promotes any window added
to a layer while menu mode is already active into the open-menu-window set regardless of whether the
caller wants that, so the trade window became exactly that unclosable topmost blocker the moment it
opened. Making it a normal closeable menu window (its own X, and reachable by Escape) fixes this using
the existing, already-proven mechanism, at the cost of a visible X the original design didn't have --
worth it over inventing a workaround (menu-mode-exempt was considered and rejected: it draws exempt
elements in a separate, always-bottommost pass, which risks a full window with children landing behind
its siblings if their rectangles overlap during a resize).

Also confirmed live and fixed as part of the same investigation: the trade window's own close handler
must call `UiLayerStack.CloseMenuWindow` (mirroring `ShopWindowController.HandleClosed`'s own call),
or the auto-promoted menu-window entry never gets removed -- `IsMenuModeActive` (`_menuWindows.Count >
0`) then never returns to false even once every window is actually closed, freezing simulation
updates and menu-mode input routing permanently. This was the second half of the "close shop, then
close inventory" freeze/hang bug.

**Three-way close cascade -- all four ways this ends now close all three windows, full stop
(landed correction).** Closing the trade window directly (its X or Escape), clicking Cancel, or
clicking Complete all close the shop and inventory windows too; the shop closing closes the trade
and inventory windows; the inventory window closing closes the trade and shop windows. **Cancel and
Complete are no longer exceptions** -- an earlier, narrower design left them closing only the trade
window (letting the player keep shopping with the same shop/inventory still open), but confirmed
live this wasn't actually wanted: a trade session is scoped to exactly one open shop, so there is
never a reason to leave the shop or inventory window standing once the trade itself is over, however
it ended. `TradeWindow.OnCancelClicked`/`OnCompleteClicked` (two callbacks distinct from `Closed`)
still exist so `TradeWindowController` can tell *why* the trade window is closing -- not to decide
whether to cascade (every reason now does) but to decide whether to run `ReturnEverythingToOwners`
(every reason except Complete, which already ran its own swap). Implemented as three narrow entry
points (`CloseForShopClosed`, `HandleInventoryWindowClosed`, and the generic `HandleWindowClosed`
funnel for every other close), each closing only the *other* two windows, never the one whose own
`Closed` event triggered it -- calling `Close()` a second time on an `Element` already mid-close
corrupts `ElementPoolService`'s pool (its `Closed` event fires before the element is actually
returned, so re-entering `Close()` on the same instance double-returns it), so a `CloseReason` flag
(`Direct`/`Cancel`/`Cascaded`/`Complete`) distinguishes "did the shop or inventory window already
close first and already own closing the other one" (`Cascaded`, the one reason that skips the
cascade here) from everything else, and every downstream call re-checks current state
(`ShopWindowController.CloseIfOpen`, this controller's own `_window`/`_subscribedInventoryWindow`
null-checks) before acting, so a cascade that loops back around always finds its target already
gone and no-ops.

Tying the trade window's close to the *inventory* window closing too (not just the shop) is a
deliberate guard, not incidental: if the player's inventory window could be closed and reopened (or
reused fresh) while a trade with one shop is still mid-flight, then walking up to a second, different
shop must never resume or get confused by stale trade state left over from the first.

## The trade grid: reusing `InventoryGridContent`, not forking a new class

`InventoryGridContent` already does everything a trade-column body needs: an arbitrary-column,
scrollable grid over one entity's own `InventoryItemStackComponent`s, per-cell drag source/target
handling, hover tooltips, and context menus. Today it has one mode switch, `_isShopMode` (bool,
derived from `mapViewState.OpenShopEntityId is not null`), which does three things: swaps the cell
type (`ShopItemStackCell` vs `InventoryItemStackCell`), forces one-cell-per-stack layout instead of
merged badges, and -- the important one for this feature -- picks which multiplier prices a cell via
`isThisGridTheShop` (`true` = price as a *purchase* off the shop's `BuyMultiplier`, `false` = price
as a *sale* off the shop's `SellMultiplier`).

That `isThisGridTheShop` axis already maps exactly onto the trade window's two columns: the
player-side trade grid should price its contents exactly like the player's own grid does today
(sell pricing), and the shop-side trade grid exactly like the shop's own grid does (buy pricing).
So the trade grid needs **no new pricing logic at all** -- only:

1. **A third `GridMode`** (replacing the bare `_isShopMode` bool with an enum: `Normal`, `Shop`,
   `TradeOffer`) so drag-eligibility and context-menu options can branch on "is this specifically a
   trade-offer grid" independent of the pricing-direction question, which stays governed by
   `isThisGridTheShop` as today.
2. **Two `InventoryGridContent` instances** for the trade window's body, one per column, each
   configured `GridMode.TradeOffer` and targeting one of the two reserved trade-offer entity ids
   (`isThisGridTheShop: false` for the player-side column, `true` for the shop-side column -- same
   flag, same meaning, just now also controlling pricing on a column that isn't the real shop/player
   grid).
3. **New drag-eligibility rules** (below) -- today's shop-mode eligibility is a straight two-party
   check (this grid vs. the one other open grid); trade mode needs to know about three parties.

**Landed (cell type + pricing direction, and now drag-drop eligibility -- see its own "Landed" note below; still not the full `GridMode` enum, since context-menu changes/currency drag/merged-stack drag remain unbuilt)**:
`InventoryGridContent` gained an optional `tradeGridIsShopSide` constructor parameter (`bool?`,
default `null`) rather than the full enum sketched above -- narrower than item 1, since drag-
eligibility/context-menus aren't wired yet and don't need it. When set, it overrides the
`entityId == shopEntityId` check every pricing/stock-status call site used (a trade-offer entity is
never the real shop, so that check alone could never tell the two trade columns apart) and selects
`TradeItemStackCell` instead of `Shop`/`InventoryItemStackCell`. `TradeWindow.BuildColumn` passes
`false` for the player-side column, `true` for the shop-side column.

**Landed visual tweaks (confirmed live):** the trade grid's own background is transparent
(`Color.Transparent`, not `WindowPalette.PanelContentColor`), and its currency footer's text is
white -- `CurrencyElement` gained an optional `textColor` `Configure` parameter (`CurrencyRowContent`
threads its own same-named constructor parameter through to it), null everywhere except this
window's two footers, which pass `Color.White`; every other `CurrencyElement`/`CurrencyRowContent`
consumer is unaffected and keeps the shared `WindowPalette.BodyTextColor`. `TradeWindow`'s own
`ColumnGap` (positions the header/grid/currency footer alike, so all three stay aligned within a
column) is 10px, +2px over the original 8px -- confirmed live clearer separation between the two
columns, especially once the grid's own background went transparent and lost the panel-color edge
that used to visually mark where the gap began.

**Landed: empty-slot decoration (confirmed live).** Every unused slot in a trade grid -- it's
always fixed at exactly `InventoryCapacity.MaxNonPlayerStackCount` (20), never scrolling, so
"unused" is well-defined -- is filled with an `EmptyTradeSlotCell`, a pure decoration signalling
"you can still drop something here" rather than leaving that space looking identical to the
transparent gap around it. `InventoryGridContent.RebuildCells` places these only when
`tradeGridIsShopSide is not null` (never for any other grid -- the player's own scrolling
inventory, a corpse's, or the shop's own grid all have no equivalent "how much room is left"
concept worth signalling), continuing the exact same column/row math the real-cell loop just used,
so real items always occupy the first N slots and decoration always fills the remainder, in order.
`EmptyTradeSlotCell` (`Presentation/UI/Content/EmptyTradeSlotCell.cs`, new, registered in
`ElementFactoryRegistry`) draws nothing but a single white `GlowMode.InteriorFade` glow -- no
border, no background, no sprite/text -- and overrides `IsHitTestable` to always read `false`
(not just the usual `_isVisible` default), so it can never be hovered, clicked, dragged, or
right-clicked; it is also never added to `InventoryGridContent`'s own `_cells` list, so none of the
hover/selection/compare-state per-frame sync ever touches it either. Destroyed and freshly
recreated on every rebuild exactly like a real cell (`RebuildCells`'s own
`elementPoolService.CloseAllChildren` at the top already covers it).

### `TradeItemStackCell`: a third, distinct level of cell detail

Not a smaller `ShopItemStackCell` or a `InventoryItemStackCell` with price bolted on -- each of the
three grids has a genuinely different job, so each cell type shows exactly what that job needs and
nothing else:

| | Inventory grid | Shop grid | Trade grid |
|---|---|---|---|
| Cell shape | `CellSize` square | 4x-wide rectangle | `CellSize` square |
| Sprite/glyph | ✓ | ✓ | ✓ |
| Item name | -- | ✓ | -- |
| Quantity | ✓ (bottom-left) | ✓ (bottom-left, in the price row) | ✓ (bottom-left) |
| Price | -- | ✓ (bottom-right, favorable/unfavorable colored) | hover only (see "Price dropped from the cell entirely" below) |
| Job | glance/sort/filter by sprite; count; click/hover for detail | methodically browse/compare name + quantity + price | glance at what's currently offered -- sprite + count only, price already seen once while adding it |

`TradeItemStackCell` extends `ShopItemStackCell` (not `InventoryItemStackCell` directly) purely to
reuse `SetPrice`/`SetStockStatus`/the favorable-vs-unfavorable price coloring without redeclaring
them (`_totalPrice`/`_quantity`/`FavorableColor`/`UnfavorableColor` widened from `private` to
`protected` for this), then overrides `DrawContent` entirely with the small-square layout: sprite
filling the cell, quantity bottom-left, total price bottom-right, both shadowed for legibility over
the sprite the same way `InventoryItemStackCell`'s own quantity badge already is (a new
`ItemIconRenderer.DrawBottomAligned` generalizes that shadowed-corner-text styling to either corner
and a caller-chosen text color, with `DrawQuantityBadge` itself rewritten on top of it so the
existing bottom-right-only callers -- `InventoryItemStackCell`, `HotbarContent` -- are unaffected).

**Fixes since first landed**: `TradeItemStackCell` was missing from `ElementFactoryRegistry.RegisterAll`
-- the trade window itself opened fine (`TradeWindow` was registered), but the moment a real item
needed a cell, `ElementPoolService.CreateElement` threw (no pool type for it). The hover *content*
needed no separate fix -- `InventoryGridContent.ComputeHoverRows` already applies uniformly to every
cell in a grid, and `tradeGridIsShopSide` already gives each trade column the correct buy/sell
pricing direction for it, so `TradeItemStackCell` gets the identical band-table/"Shop will not buy"
tooltip `ShopItemStackCell` does for free -- but hover itself didn't fire at all until the trade
window was clicked/focused once, confirmed live. Root cause: `TradeWindow` is the only window in
this family built with no `Layout.Size` at `CreateElement` time (its final size depends on
`OnChildrenInitialized`, unlike Shop/SecondaryInventoryWindow, whose final size is already known
when they're created) -- `Element.Build`'s own `MaximumSize` fallback chain (`Layout.MaximumSize ??
parent.ContentSize ?? Layout.Size ?? Vector2.Zero`) resolves to a bare `Vector2.Zero` for a
parentless window with none of those set, silently relying on `MinimumSize > MaximumSize`'s own
clamp-ordering to paper over it rather than a correct ceiling. Fixed the same way the
`AbilityScoreWindow` column-count bug was: `SetMaximumSize(finalSize)` in `OnChildrenInitialized`,
right before `SetMinimumSize`/`SetSize`. **Confirmed live this alone didn't fix hover** -- the
`MaximumSize` issue was real (worth fixing regardless) but not the actual cause of this bug.

**Real root cause, found after the above didn't fix it**: both trade columns' own `InventoryGridContent`
instances were sharing one `TradeWindowController._hoverPopup` Tooltip -- the exact bug
`InventoryFolderController`'s own `_abilityScoreHoverPopup`/`_inventoryHoverPopup` split already
exists to avoid (see that field's own doc comment: "both windows self-poll the mouse independently
every frame, and sharing one popup would let whichever window updates second stomp the other's
ShowNear/Hide call"). `TradeWindow._children` updates the shop-side column's grid *after* the
player-side one every frame (build order: player grid, player footer, shop grid, shop footer,
buttons), so the shop-side grid's own `Hide()` -- fired every frame nothing under it is hovered --
permanently overwrote whatever the player-side grid had just tried to show that same frame,
regardless of `_hoveredFrames` ever reaching the delay threshold. This reproduces as "never shows"
until something reorders `TradeWindow`'s own `_children` in the losing column's favor -- e.g.
clicking empty grid space (not a cell -- clicking a cell only reorders the cell within its own
immediate parent, not the grid window itself within `TradeWindow`) raises that specific `gridWindow`
past its sibling via `Element.RaiseToFront`, so its own `Update` call -- and therefore its own
`ShowNear`/`Hide` decision -- runs last from then on. Fixed by giving each column its own Tooltip
(`TradeWindowController._playerColumnHoverPopup`/`_shopColumnHoverPopup`, both added to
`UiLayer.Tooltip`; `TradeWindow.Configure` takes both, `BuildColumn` picks the matching one per
column) -- no shared mutable popup left to race over.

**Price dropped from the cell entirely (landed, supersedes the original "quantity bottom-left, price
bottom-right" design).** First pass drew both, quantity on `_quantityFont` (matching the plain
inventory badge) and price on `ShopItemStackCell`'s own smaller size (`PriceFontSizeFraction`,
renamed `CompactStatFontSizeFraction` once `TradeItemStackCell` started reusing it too) -- confirmed
live that even the smaller size read cramped once a 3-digit-or-more quantity had to share the row
with a price string. Rather than shrinking further, price was dropped from this cell's own draw
entirely: the player already saw it once while dragging the item in from their own inventory, and
can see it again on hover (the same tooltip described above, unaffected by this). Quantity alone now
uses `CompactStatFontSizeFraction`, not `_quantityFont` -- large enough to read, small enough that a
3-digit quantity still fits the corner comfortably on its own.

Each column is capped at **20 stacks**, fixed and non-scrolling -- the trade window doesn't grow and
never needs a scrollbar, unlike every other grid in the game. This cap needs **no new code at all**:
`InventoryCapacity.HasRoomForNewStack`/`HasRoomForNewStacks` already refuse a transfer onto any
non-player entity once it holds `MaxNonPlayerStackCount` (already 20) distinct stacks, and neither
trade-offer entity is ever the player -- so as long as "Add to trade" is implemented as an ordinary
`InventoryActions.TryTransferStack` call (same as every other transfer in this plan already is),
it's automatically refused once a column hits 20, for free, the exact same way any other
capacity-full non-player entity already refuses a drop today. No new cap constant, no new check.
Sized so all 20 slots are visible without a scrollbar (exact row/column arrangement -- e.g. one
column of 20 vs. a 2-wide layout -- is chrome, not pinned here).

**Merged-stack pricing gotcha to watch for** (same class of bug flagged during Phase 5): if a trade
column ever holds *two separate stacks* of the same item (e.g. two divergent wand instances), Value
computation (below) must sum their quantities and price the combined total through
`ComputeBulkSellPrice`/`ComputeBulkBuyPrice` **once**, not price each stack independently and add
the two results -- pricing 5+5 as two separate calls would each restart at the band the shop's *real*
stock is currently in, double-counting whichever band edge sits at the boundary, instead of correctly
walking all 10 units through the bands in one pass.

## Drag-drop eligibility

| From ↓ / To → | Player Inventory | Trade: player column | Trade: shop column | Shop grid |
|---|---|---|---|---|
| Player Inventory | -- | **allowed** (add to trade) | not allowed | allowed (direct sell, unchanged) |
| Trade: player column | **allowed** (remove from trade) | -- | not allowed | **allowed** (direct sell) |
| Trade: shop column | **allowed** (direct buy) | not allowed | -- | **allowed** (remove from trade) |
| Shop grid | allowed (direct buy, unchanged) | not allowed | **allowed** (add to trade) | -- |

The only remaining "not allowed" cells are between the trade window's own two columns -- a confirmed
assumption, not explicit in the ask: items must return to their own owner (or straight out to the
*other* real inventory, see below) before crossing to the other side; there is no direct
player-column-to-shop-column drag. Direct player-inventory-to-shop-grid dragging (today's existing
immediate buy/sell) is explicitly kept working unchanged, per the ask.

"Add to trade" onto the shop column respects the same eligibility `ShopActions.CanTrade`/stock-
availability already gates "Buy All" behind -- a shop that won't sell an item can't have it added to
a trade offer either.

**Landed**: every cell of the table above, for a single `StackInstanceId`-tracked stack (the
overwhelmingly common case -- see `InventoryActions.AddItem`'s own merge behavior). Implemented as
`UiInputController.ResolveTradeAwareItemDrag`, a new branch `ResolveContentDrag` routes into whenever
either drag endpoint is one of the two reserved trade-offer entities (`MapViewState.
TradeOfferPlayerEntityId`/`TradeOfferShopEntityId`, set once by `ShellBootstrapper` right after
`MapViewState` is constructed and never changed again) -- the ordinary `originIsShop`/
`destinationIsShop` check the non-trade branch already used would otherwise misfire on a trade-offer
entity, since neither is ever itself shop-registered but `ShopActions.TryBuyFromShop`/`TrySellToShop`
debit/credit whichever entity id they're handed directly (see "direct sell/direct buy" below for why
that matters). A **Merged Stack** drag (no single `StackInstanceId`, see `TryStartContentDrag`'s own
doc comment) or a **currency** drag touching either trade-offer entity is refused outright rather than
falling through to an unpriced/unrouted transfer -- neither is designed for yet (Currency footer's own
drag-and-drop is still unbuilt, see below), matching the existing precedent of refusing a Merged Stack
drag that touches a shop.

### Trade column dragged straight to the *other* real inventory: direct sell / direct buy

**Landed**, exactly as composed below (`ResolveTradeAwareItemDrag`'s own two branches) -- confirmed
live via a real `ShopStockPricing` bulk price, not a hand-verified flat rate: with the test harness's
`preferredStockLevel: 0` (the same setup `Tests/Presentation/UiInputControllerTests.cs`'s own shop-drag
harness already uses), a direct buy prices *cheaper* than the flat `BuyMultiplier` rate, because step 1
below has already returned the stack to the real shop's own inventory by the time `ShopStockPricing`
reads its current stock -- any stock at all already reads as overstocked relative to a preferred level
of 0. Worth knowing before assuming a direct sell/buy through the trade window always matches the
flat/no-stock-history price an item's very first sale would get.

Dragging a stack from the **player** trade column onto the **shop grid** is a direct sell -- not
"remove from trade, then separately drag it onto the shop later." Dragging a stack from the **shop**
trade column onto the **player inventory grid** is a direct buy, symmetrically. Both are priced live
at the moment of the drop (the shop's *current* `ComputeBulkSellPrice`/`ComputeBulkBuyPrice`), exactly
like an ordinary direct sell/buy already is -- not the trade window's own frozen Value total, and not
routed through Complete/Balance Offer at all.

`ShopActions.TrySellToShop`/`TryBuyFromShop` (`Game/Modules/Shops/ShopActions.cs:42,89`) can't act on
these stacks directly as written -- both hard-code where they look the stack up
(`TryFindByStackInstanceId(stacks, shopEntityId, ...)` for buy, `playerEntityId` for sell), i.e. they
already assume the stack sits on the *real* shop/player entity, not a trade-offer entity. Rather than
widen those two methods to take an arbitrary source entity, this plan composes them from a two-step
move that's already needed elsewhere:

1. Move the stack out of the trade entity onto the real owner it came from -- the exact same transfer
   "remove from trade" already performs (`InventoryActions.TryTransferStack`, which preserves
   `StackInstanceId`).
2. Immediately call the ordinary `TrySellToShop`/`TryBuyFromShop` with that same `stackInstanceId`,
   now resolvable on the real entity, exactly as an ordinary direct-sell/buy drag already would.

If step 2 fails (shop at its stock cap, player out of room, either side can't afford it -- any of the
existing preconditions those methods already check), step 1 is undone -- the stack moves back into
the trade entity, so the whole gesture is atomically all-or-nothing from the player's point of view,
matching every other transfer in this plan's own "fails with no state changed" contract. No changes
needed inside `ShopActions` itself.

### Drop target resolution: whole-window drop zones, not just the grid/row under the cursor

**Landed.** Larger drop targets with implied logic beat small precisely-targeted ones -- a player
shouldn't have to land a drag exactly on a narrow currency row to give currency, or exactly inside a
cramped grid to give an item. This changes *what counts as a valid drop*, not the eligibility rules
above:

- **Direct buy / direct take-items / direct take-currency**: the drop target is the player
  inventory window's entire client area (title bar down to the last pixel), not just the item grid
  or just the currency row inside it.
- **Direct sell / direct give-items / direct give-currency**: the drop target is the entire shop (or
  loot) window's client area, same reasoning.
- **Trading**: the drop target is the entire trade window's client area -- dropping anywhere on the
  trade window's shop-side half counts as "add to the shop-side trade offer," anywhere on the
  player-side half as "add to the player-side trade offer," regardless of which specific sub-widget
  (grid vs. currency row) happens to sit under the cursor at that pixel.

**Routing, once a window/half is established as the target**: what's actually being dragged decides
which *component* it lands in, not which widget it was dropped on top of -- a dragged item always
goes to that side's item-stack inventory, a dragged currency element always goes to that side's
`CurrencyComponent`, even if the drop point is visually on top of the other one. The example from the
ask: an item dragged from the player's inventory and dropped anywhere on the trade window's shop
half -- including directly on top of that half's currency row -- still adds the item to the shop-side
trade *item* grid, not currency; a currency drag dropped on top of that half's item grid still adds
Gold to the shop-side trade *currency* footer, not the item grid.

**Implementation, landed:** a new `IWholeWindowDropTarget` interface
(`Presentation/UI/Content/IWholeWindowDropTarget.cs`) with two methods, `ResolveItemDropEntityId
(Point)` and `ResolveCurrencyDropEntityId(Point)`, implemented by the three top-level receiving
windows -- `InventoryManagementWindow`, `ShopWindow`, and `TradeWindow`. `UiInputController.
FindDropTargetEntityId`'s existing ancestor walk (looking for the nearest `Window` whose `Tag`
implements the narrower, existing `IInventoryDropTarget` -- unchanged, still checked first, so
landing exactly on a grid cell or currency element still resolves through that same specific path)
now falls back to checking `IWholeWindowDropTarget` on each ancestor once that narrower check has
failed all the way up to the containing top-level window -- which it always eventually reaches,
since the hit-test that produces the drop's own Element never returns something outside its
containing window's own `Rectangle`. `ResolveContentDrag` already had the drop's own `Point` and
already knew the drag's payload type (`_contentDragCurrencyType is not null`) in scope at the exact
call site, so no new state needed threading through.

- `InventoryManagementWindow`/`ShopWindow`: one entity per whole window, so both resolver methods
  just return that window's own stored `_entityId` unconditionally, regardless of position -- no
  positional math needed at all (multiple tabs in `InventoryManagementWindow`'s case all still
  represent the same entity, so which tab happens to be active doesn't matter either).
- `TradeWindow`: the one implementer that actually needs `dropPosition` -- splits by which half of
  its own width the point falls in (`dropPosition.X < ContentAbsolutePosition.X + ContentSize.X /
  2f`), matching `BuildColumn`'s own fixed left/right layout exactly, returning
  `_playerSideEntityId` or `_shopSideEntityId`. Item and currency resolve identically for every
  implementer, since each entity's own inventory and currency balance are the same entity.

Confirmed via `TradeWindowTests`: two direct unit tests on `TradeWindow`'s own
`ResolveItemDropEntityId`/`ResolveCurrencyDropEntityId` (left half → player-side entity, right half
→ shop-side entity), plus one full end-to-end test driving a real `UiInputController` drag from the
player's own inventory grid onto `TradeWindow`'s own header area (y below `HeaderHeight`, above
both columns' grids -- no grid/currency child sits there at all) confirming the item still lands in
the player-side trade column rather than the drop silently failing.

### Effective shop stock: a staged item still counts as the shop's own

**Bug, landed fix**: a stack staged in the trade-shop column priced itself (both the hover
tooltip's per-trade bracket receipt and the cell's own `SetPrice`-driven total) as if only *one*
unit existed to buy, no matter its real `Quantity` -- confirmed live as "shop has 5 Scrolls of Torch
at 25G each (125G total); drag the stack into the trade window and the tooltip still shows 25G each
but a 25G *total*; buying it back out of the trade window still correctly charges 125G." The
mispriced total was purely a tooltip/eligibility display bug -- the actual charge on a completed
direct buy/sell was always correct, because `UiInputController.ResolveTradeAwareItemDrag`'s own
composed direct-buy/sell already returns the stack to the real shop entity *before* pricing it (see
"Trade column dragged straight to the *other* real inventory" above).

Root cause: `InventoryGridContent` derived "the shop's current stock" for every stock-based
pricing/status read (`ComputeHoverRows`'s band table and per-trade receipt, `UpdateShopEligibilityState`'s
affordability check, `ComputeShopTotalPrice`, `ComputeShopStockStatus`) from
`ShopStockPricing.GetTotalStock(shopEntityId, ...)` alone. A stack staged in the trade-shop column
has *physically* moved off the real shop entity (a plain `InventoryActions.TryTransferStack`, see
"Drag-drop eligibility" above), so that read drops to 0 the moment the whole stack leaves --
`ShopStockPricing.ComputeBulkPrice`/`ComputeBulkBreakdown`'s bracket walk then clamps to a single
stock level (`low = max(0, currentStock - quantity + 1)`, `high = currentStock`, both 0), pricing
exactly 1 unit regardless of the stack's real `Quantity`.

The fix treats a staged item as still economically owned by the shop, matching the answer already
given to "does a future phase recalculate current shop quantity to include items in the shop's
trade column?" -- no separate recalculation needed, because the goods haven't actually changed
hands yet (no Gold has moved, the trade could still be cancelled), so pricing should read the
shop's *true* holdings, not just wherever the UI happens to have physically parked the
`InventoryItemStackComponent` for staging purposes:

- `Game/Modules/Shops/ShopStockPricing.cs` gained explicit-stock overloads of
  `ComputeBulkBuyPrice`/`ComputeBulkSellPrice`/`ComputeBulkBuyBreakdown`/`ComputeBulkSellBreakdown`
  (taking `currentStock`/`preferredStockLevel` directly instead of `ComponentManager`/`shopEntityId`)
  -- the existing entity-based overloads (used everywhere else, including the hot
  `ShopActions.TryBuyFromShop`/`TrySellToShop` execution path, left untouched) now just resolve
  those two values and delegate. Same pattern `GetStockStatus` already had (a raw-value overload
  alongside its entity-based convenience wrapper).
- `InventoryGridContent.GetEffectiveShopStock(shopEntityId, itemDefinitionId)` -- new: real shop
  stock plus whatever `MapViewState.TradeOfferShopEntityId` currently holds of the same item. Every
  stock-based read in the file now goes through this instead of a bare `GetTotalStock` call, so a
  stack sitting in the trade-shop column prices/bands identically to one sitting in the shop's own
  grid, and the real shop's own *remaining* stock of the same item (if any) prices as if it were the
  tail end of the same, larger total -- fully consistent either way.
- This also fixed a real (if less visible) correctness issue in `UpdateShopEligibilityState`, not
  just a cosmetic one: a cell's `CompareState` (Eligible/Ineligible, gating whether it can even be
  dragged at all) was being decided against the same understated price.

## Currency footer

Each column's footer is a currency row over its own trade-offer entity's `CurrencyComponent`,
mirroring `CurrencyRowContent`'s existing Gold/Credits elements.

**Landed**: `CurrencyElement` gained a `showLabel` `Configure` parameter (default `true`, unchanged
for every existing consumer) -- the trade window's own two footers pass `false`, so they read "10
[sprite]" instead of "Gold : 10 [sprite]", confirmed live too narrow a column for the label.

**Landed: currency drag-and-drop.** `CurrencyRowContent` already implemented `IInventoryDropTarget`
(plumbing that anticipated this before it existed) and `TryStartContentDrag` already picked up a
plain player-to-shop currency drag -- the first real gap was `ResolveContentDrag` blocking any
currency drag that touched a trade-offer entity outright. `UiInputController.
ResolveTradeAwareCurrencyDrag` (mirroring `ResolveTradeAwareItemDrag`'s structure) now allows the
two symmetric stage/unstage pairs -- Player Gold &lt;-&gt; Trade: player column, Shop Gold &lt;-&gt;
Trade: shop column -- and refuses everything else touching a trade-offer entity (crossing between
the trade window's own two columns, or between a trade column and the *other* real entity), the same
"no direct drag between the trade window's own two columns" rule the item eligibility table already
established. Unlike an item stack, currency has no buy/sell price against itself, so there is no
"direct give/take" analog to the item table's composed direct-sell/direct-buy -- a trade column's
currency only ever moves to/from its own real owner.

**Second gap, also landed**: `TryStartContentDrag`'s own long-standing "a shop's own Gold can never
be dragged away" guard (correctly closing the direct-Take exploit for an ordinary player/shop pair)
also blocked ever *picking up* the shop's currency element at all -- meaning Shop Gold -> Trade:
shop column could never actually be reached by a drag, even though `ResolveTradeAwareCurrencyDrag`
above already supported that direction. Fixed by letting the pickup through specifically when
`currencyElement.EntityId == MapViewState.OpenShopEntityId` (this exact shop, currently open, so a
trade window exists to stage into) -- `ResolveContentDrag`'s own plain-transfer branch (the
non-trade path) still separately refuses a shop-origin drag landing anywhere but a trade-offer
entity (`!originIsShop` guard), so a direct Take (dropped straight on the player, bypassing the
trade window) still fails exactly as before; only the trade-shop-column destination newly succeeds.

Underlying transfer: `CurrencyActions.TryTransfer(componentManager, source, dest, type)` (the
3-arg, whole-balance overload -- the same one an ordinary player-to-shop currency drag already
used) -- a drag of the *entire* currency element moves the entity's whole Gold balance in one call
(mirrors "Give All"/`TryTransferAll`'s whole-balance shape); this plan doesn't need a
partial-currency-drag amount picker (that's `TODO.md`'s separate, still-open Context menu amount
picker item) since the footer is never a source/destination for anything but "the whole stack of
Gold currently sitting there."

**Landed: "Angel Investor" achievement fires from every give-currency-to-a-shop gesture, not just
the context menu (confirmed live gap).** `ShopActions.TryGiveCurrencyToShop` is now the one
chokepoint every such gesture routes through -- `CurrencyRowContent`'s own context-menu Give/Give
All, `UiInputController.ResolveContentDrag`'s plain (non-trade) player-to-shop currency-drag
branch, and `TradeWindow.CompleteTrade`'s player-side Gold leg all call it instead of a bare
`CurrencyActions.TryTransfer`, so `GoldGivenToShopEvent` publishes the same way regardless of which
UI gesture the player used, including a trade offering only Gold and no items at all.
`TryGiveCurrencyToShop` takes an optional `eventPlayerEntityId` (defaulting to the transfer's own
source entity) specifically for `CompleteTrade`: its transfer moves currency out of the *trade-offer
player column* (a reserved placeholder, not the real player -- see "Entity model" above), so it
passes `world.PlayerEntityId` explicitly rather than letting the event report that placeholder.

**Landed: the currency context menu's "Give" is suppressed once the secondary target is a shop,
leaving only "Give All"** (confirmed live: both used to show while a shop was open, redundant since
a shop only ever wants the whole balance) -- `CurrencyRowContent.BuildCurrencyContextMenu` gates the
"Give" option on `_shopPool?.Has(secondaryTargetEntityId) != true`; a corpse/container secondary
target still gets both, unaffected.

## Header: Player Value / Shop Value

**Landed**: both header lines (the "Player Value"/"Shop Value" labels and their own value line
beneath) are drawn centered over their own column, not left-flush -- `TextWindow.DrawContent` has no
centered-text mode, so `TradeWindow` draws these two directly in its own `DrawContent` override via
`LabelRenderer.DrawCentered` instead of two child `TextWindow`s.

That fix also surfaced, and fixed globally, the "bottom of the text is cut off" bug last seen in the
Tooltip/ability-score work: every render pass in this codebase uses `SamplerState.PointClamp`
(nearest-neighbor), and point-sampling a font atlas crops or shifts a glyph whenever it's drawn at a
fractional pixel position -- exactly what `LabelRenderer`'s own centering math (`/ 2f`) produces
whenever the footprint-minus-`LineHeight` difference is odd. Confirmed by this window's own header
labels showing the exact same clipped-bottom symptom, with no `RequiresContentViewport` workaround in
sight to blame this time. Root-cause fixed once, in `LabelRenderer.Draw` (rounds the draw position to
the nearest whole pixel before calling `SpriteBatch.DrawString`) -- every direct
`SpriteBatch.DrawString` call site in the UI layer (`TextWindow.DrawContent`, `TextBox`'s two,
`TextDivider`, `Window`'s own title bar, `CurrencyElement`) now routes through it instead, so the fix
covers every consumer at the source rather than requiring each one to separately rediscover the
`CanUserScrollVertical = true` workaround. Cheap (a couple of `MathF.Round` calls, no extra SpriteBatch
Begin/End the way the viewport-push workaround cost), so no reason not to apply it everywhere.

**Landed.** Each header shows a live-computed total: the sum of every item stack's *current* trade
price (via the exact same `ShopStockPricing.ComputeBulkSellPrice`/`ComputeBulkBuyPrice` calls the
grid cells themselves already use to price a cell) plus that column's own footer Gold, recomputed
every frame in `TradeWindow.ComputeColumnValueText` (the same "poll every Update, don't wire change
events" convention every other per-frame UI read in this codebase already follows -- cheap, at most
20 stacks per column). "0G" whenever no shop is open at all.

- **Player Value** (left) = Σ `ComputeBulkSellPrice(shop, item, quantity)` over every stack in the
  player-side trade grid, priced off the *real* shop's current `SellMultiplier` and *real* current
  stock bands -- plus the left footer's Gold.
- **Shop Value** (right) = Σ `ComputeBulkBuyPrice(shop, item, quantity)` over every stack in the
  shop-side trade grid, priced off the real shop's `BuyMultiplier` and real current stock -- plus
  the right footer's Gold.

Each item is priced independently against the shop's *actual* current stock, not a running
simulation of "what would stock be if every other item already in this trade had already been
sold/bought" -- the same simplification the rest of the pricing system already makes (a bulk
purchase's own bracket math prices strictly off stock at the moment the trade window computes it,
recomputed fresh each frame as items enter/leave, never assuming a partially-built offer has already
moved real stock). Real stock only actually changes at Complete, when items physically move. "The
shop's *actual* current stock" is `InventoryGridContent.GetEffectiveShopStock` (now `internal
static`, shared by both classes) -- the real shop entity's own stock plus whatever's currently
staged in the shop-side trade column for that same item, the identical "still the shop's true
ownership until the trade completes" correction the grid cells themselves already needed (see
"Effective shop stock" under Drag-drop eligibility above) -- without it, an item priced while
sitting in the shop column would price against a stock count that's short by its own quantity.

Same-item stacks are grouped and summed into one combined quantity *before* a single bulk-price
call per item, not priced independently per physical stack then summed -- confirmed live via a
dedicated regression test (`TradeWindowTests.
Update_TwoSeparateStacksOfSameItem_CombinesQuantitiesBeforeOneBulkPriceCall`): bulk pricing is a
non-linear, band-crossing curve, so two 5-unit stacks priced independently (each from the *same*
starting stock level) would double-count the first half's band rate instead of ever pricing the
second half's, overstating (or understating) the column's real Value whenever the combined quantity
crosses a band boundary the two individual halves wouldn't have crossed on their own.

### Worked example

Shop: `BuyMultiplier = 1.10`, `SellMultiplier = 0.90`. Both items currently in the shop's Normal
band (flat multiplier, no band skew, for a clean example).

- Widget, `GoldValue = 10` → sells to the shop at `10 × 0.90 = 9G`/unit, buys from the shop at
  `10 × 1.10 = 11G`/unit.
- Gadget, `GoldValue = 20` → buys from the shop at `20 × 1.10 = 22G`/unit.

Player drags 10 Widgets into the player column: Player Value = `10 × 9G = 90G` (+0G footer) = **90G**.
Player drags 3 Gadgets from the shop into the shop column: Shop Value = `3 × 22G = 66G` (+0G footer)
= **66G**. Unequal → Complete is disabled.

## Footer buttons: Balance Offer, Cancel, Complete

**Landed.**

**Balance Offer** (top button, `TradeWindow.BalanceOffer`) rebuilds both columns' Gold from a clean
slate every click, then tops up whichever side that leaves short using *that side's own real,
outside-the-trade currency*:

1. Remove **all** Gold from both trade columns first, returning each amount to its own real owner
   (the whole-balance `CurrencyActions.TryTransfer` overload -- a no-op if a column already has
   none) -- both columns' Values now reflect only their item contents.
2. If Shop Value > Player Value: move `min(deficit, player's real Gold balance)` Gold from the
   player's real `CurrencyComponent` into the **left** footer.
3. If Player Value > Shop Value: move `min(deficit, shop's real Gold balance)` Gold from the shop's
   real `CurrencyComponent` into the **right** footer.
4. If the payer can't fully cover the deficit, it adds as much as it has and stops -- values stay
   unequal, Complete stays disabled (matches the ask's "until the two are equal or the \[payer\] runs
   out of currency" verbatim).

**Redone from an earlier version (confirmed live correction), also user-specified:** that version
only netted out `min(playerColumnGold, shopColumnGold)` before topping up -- returning just the
*smaller* of the two overlapping amounts, which left whichever side started with more Gold still
holding the leftover difference rather than a fully Gold-free baseline on both sides. The current
version removes *all* Gold from both, unconditionally, before ever computing the deficit -- see
`TradeWindowTests.BalanceOffer_RemovesAllGoldFromBothSides_BeforeRebalancing` for the worked numbers
(both columns land at exactly 0 Gold when neither side holds any items, since there's nothing left
to rebalance once the wipe alone already leaves both Values equal at 0).

Continuing the worked example: Player Value (90G) > Shop Value (66G), so step 1 finds nothing to
wipe (both Values here come entirely from items, no footer Gold on either side to begin with), then
Balance Offer moves `min(90 − 66, shop's real Gold) = 24G` (assuming the shop has ≥24G) from the
shop's real balance into the right footer. New Shop Value = `66 + 24 = 90G` = Player Value → equal.

**Complete is enabled when Player Value ≥ Shop Value -- not only when the two are exactly equal.**
Shops accept a trade that's equal *or favorable to the shop* (the player giving up equal-or-more
value than they receive); they never accept one favorable to the player. So Complete's own gate is
`playerValue >= shopValue`, a strict widening of "equal" to "equal or better for the shop." Balance
Offer's own target is unchanged -- it still only ever moves currency to reach *exact* equality (see
above), so a player relying purely on Balance Offer always lands exactly on the boundary, never
past it; a player can still manually add more value than Balance Offer would (dragging extra items/
Gold beyond what's needed) and Complete stays enabled, since that's even more favorable to the shop.
There is no mechanism for the reverse -- an unequal trade favoring the player never enables Complete,
full stop. Baldur's Gate 3 itself uses a "Barter" button instead of a plain Complete, giving a
high-Charisma character a chance to push through an unequal trade anyway; this game already expresses
Charisma's effect a different way (directly on the shop's own buy/sell margins, not as a per-trade
success roll -- see `PLAN-shops.md`), so that chance-based override doesn't need to exist here. A
plain Complete button with a hard `>=` gate is the whole mechanic.

Both **Complete** and **Balance Offer** default to **disabled on an empty trade** (nothing offered on
either side) -- confirmed, not just a UX-guard default: an empty trade has nothing to balance and
nothing to complete.

Completing the trade (`TradeWindow.CompleteTrade`) is a **direct swap, not routed through
`ShopActions.TryBuyFromShop`/`TrySellToShop`** -- those methods do their own per-call
bulk-pricing-and-charge; here, pricing already did its job (gating the button), so completion only
needs to move what's physically sitting in each column, via the shared `TransferAllStacksTo` helper
(collects the stack list first, then transfers each one -- `TryTransferStack` removes from the
source's own dense chain mid-call, so walking it directly while transferring would be unsafe, the
same "collect then act" shape `InventoryActions.TryTransferAllStacksOfItem` already uses):

1. Every item stack in the player-side trade entity → the real shop entity (`InventoryActions.
   TryTransferStack` per stack, same primitive every other transfer already uses).
2. Every item stack in the shop-side trade entity → the real player entity.
3. The left footer's whole Gold balance → the real shop entity (`CurrencyActions.TryTransfer`,
   whole-balance overload).
4. The right footer's whole Gold balance → the real player entity.

Continuing the example: 10 Widgets + 0G → shop; 3 Gadgets + 24G → player.

Because step 1/2 move real `InventoryItemStackComponent` stacks into/out of the real shop entity,
the shop's stock bands for the *next* trade or purchase already reflect the change for free --
`ShopStockPricing.GetTotalStock` reads live state, no special post-trade recompute needed.

**Completing closes all three windows, same as Cancel/X/Escape/shop-close/inventory-close** (see
"Window layout"'s own "Three-way close cascade" for the landed correction over an earlier, narrower
design that left the shop/inventory windows open after Complete). `TradeWindowController` still
tracks this as its own `CloseReason.Complete`, the one close reason that must *not* also run
`ReturnEverythingToOwners` below (the swap already happened; unwinding afterward would undo it) --
that distinction is the only thing `CloseReason` still needs to make, now that every reason cascades.

**Cancel**, **closing the shop window**, and **closing the player inventory window** (X button or
Escape, either window) all run the identical unwind (`TradeWindow.ReturnEverythingToOwners`, called
from `TradeWindowController.HandleWindowClosed` for every `CloseReason` except `Complete`), just in
the opposite direction from Complete -- return everything to where it started instead of swapping:

1. Every item stack in the player-side trade entity → back to the real player.
2. Every item stack in the shop-side trade entity → back to the real shop.
3. Left footer Gold → back to the real player.
4. Right footer Gold → back to the real shop.

**Landed gotcha, confirmed live**: the real shop entity id used above is captured once by
`TradeWindow.Configure` (a new `_shopEntityId` field), *not* re-read from
`MapViewState.OpenShopEntityId` at unwind time -- `ShopWindowController.HandleClosed` already
clears `OpenShopEntityId` back to `null` *before* invoking `TradeWindowController.CloseForShopClosed`
(its own doc comment: "fired at the end of HandleClosed, after every other cleanup"), which is what
eventually triggers this unwind. Re-reading `OpenShopEntityId` at that point would already find it
cleared, silently no-opping the entire unwind and permanently stranding whatever was staged in
either trade-offer entity -- exactly the failure mode a dedicated regression test now guards
(`TradeWindowTests.
ReturnEverythingToOwners_StillWorks_AfterOpenShopEntityIdHasAlreadyBeenClearedToNull`). The same
capture is now used for every other real-shop reference `TradeWindow` needs (`ComputeColumnValue`,
`CompleteTrade`, `BalanceOffer`), not just the unwind.

`ShopWindowController.HandleClosed`'s existing `OnClosed` hook (already wired to
`TradeWindowController.CloseForShopClosed`) and `InventoryManagementWindow`'s own `Closed` event
(already wired to `TradeWindowController.HandleInventoryWindowClosed`, see Window layout above)
were both already in place from the earlier close-cascade phase -- Cancel is the same unwind,
manually triggered, without closing either window.

## Context menu changes

**Landed.** Only while shop mode is active (same gating "Give"/"Take" already used, unchanged):

- Player grid's **"Give"** → relabeled **"Sell All"** only when the secondary target is the shop
  (`_shopPool.Has(secondaryTargetEntityId)`) -- same underlying action (`ShopActions.TrySellToShop`
  on the clicked stack), label only, no behavior change. A non-shop secondary target (a corpse/
  container) keeps the plain "Give" label, unaffected.
- Shop grid's **"Take"** → relabeled **"Buy All"** under the same shop-secondary-target condition --
  same underlying action (`ShopActions.TryBuyFromShop`), label only.
- New **"Add to trade"** on both the player grid (`cell.EntityId == world.PlayerEntityId`, targets
  `MapViewState.TradeOfferPlayerEntityId`) and the real shop's own grid (`cell.EntityId ==
  shopEntityId`, targets `TradeOfferShopEntityId`) -- a plain `InventoryActions.TryTransferStack`
  into the matching trade-offer entity, gated by the same `isShopIneligible` check (CompareState,
  i.e. tag match + affordability) "Sell All"/"Buy All" already use. Gated purely on
  `MapViewState.OpenShopEntityId` being set, not on any secondary-target check -- a shop being open
  implies the trade window is too (`TradeWindowController` opens in lockstep with
  `ShopWindowController`), and this option has nothing to do with whichever corpse/container
  secondary window might separately be open.

**Trade grid cells don't get a context menu at all.** Right-clicking a stack in either trade column
(`InventoryGridContent.RemoveFromTrade`, wired in place of `BuildItemContextMenu` whenever
`tradeGridIsShopSide is not null`) removes it from the trade immediately -- the same transfer-back-
to-real-owner "Add to trade" already uses in reverse (player column → `world.PlayerEntityId`, shop
column → `MapViewState.OpenShopEntityId`), fired directly off `OnRightClicked` instead of opening a
menu with a "Remove from trade" option in it. Confirmed as a deliberate exception to "right-click
opens a context menu," which otherwise holds everywhere else in the game -- trade-grid cells only
ever offer the one action, so a menu would just be one extra click for no benefit.

## Compare mode

`InventoryGridContent.UpdateCompareState` already makes shop mode and Compare mode mutually
exclusive (shop-eligibility coloring wins outright while a shop is open) -- see the separate
"Fix Compare in shop mode" bug entry in `TODO.md`. The trade grids should simply inherit whatever
that fix ends up being (they're shop-mode-adjacent grids, not a new case) -- not addressed here.

## Confirmed decisions

- Two reserved entities (not one, not per-trade-created-and-destroyed) -- see Entity model.
- `GridMode` enum replaces the bare `_isShopMode` bool; pricing direction stays governed by the
  existing `isThisGridTheShop` flag, now also applied to the two trade columns.
- No direct drag between the trade window's own two columns -- items return to their owner (or
  straight out to the *other* real inventory as a direct sell/buy, see Drag-drop eligibility) first.
- A trade column dragged straight onto the *other* real inventory is a direct sell/buy, priced live
  at drop time via the existing `ShopActions.TrySellToShop`/`TryBuyFromShop` -- composed from a
  remove-from-trade step plus the ordinary direct-sell/buy call, not a new pricing path and no
  changes inside `ShopActions` itself.
- Currency drag-and-drop (not just context-menu) is new work this feature depends on finishing.
- Value is computed fresh each frame off real shop stock, never a simulated running total across
  items already staged in the same trade.
- Same-item multiple-stack Value computation must combine quantities before one bulk-price call, not
  price each stack independently (the merged-stack-cell gotcha).
- Balance Offer only ever adds currency, never removes anything already placed, and only ever targets
  exact equality (never overshoots).
- **Complete's gate is `playerValue >= shopValue`, not exact equality** -- shops accept an
  equal-or-shop-favorable trade, never a player-favorable one. See Footer buttons above.
- Complete Trade is a direct item/currency swap between the two trade entities and the two real
  entities -- it does not call `ShopActions.TryBuyFromShop`/`TrySellToShop`.
- Cancel, shop-close, and player-inventory-close all perform the identical unwind (return-to-owner),
  just never the swap -- see Window layout above for why inventory-close is included.
- Both Complete and Balance Offer default to disabled on an empty trade.
- Trade columns cap at 20 stacks each, fixed and non-scrolling -- free via `InventoryCapacity`'s
  existing non-player 20-stack cap, no new constant or check.
- The trade window is centered on screen at open, cannot be resized (`CanUserResize` left false), and
  the inventory/shop windows open top-aligned beside it -- all three remain freely movable afterward.
- **`CanUserClose = true` on the trade window (corrected from the original `false`)**, and all three
  windows now close together regardless of which one closes first -- see Window layout above for why
  (Escape breaks entirely otherwise).
- Larger, whole-window drop targets for every flow (direct buy/take, direct sell/give, trade) --
  destination *component* (item grid vs. currency) is decided by drag payload type and, for the trade
  window, which half was dropped on, not by which narrow sub-widget the cursor happened to land on.
- Trade grid cells have no context menu -- right-click removes the stack from the trade immediately.
- **An unaffordable-but-tag-eligible shop item can still be staged in the trade window.**
  `InventoryItemStackCell.CanStageInTrade` (tag match only, ignoring affordability) gates
  `TryStartContentDrag`'s pickup and `BuildItemContextMenu`'s "Add to trade" instead of the stricter
  `CompareState` (tag match AND affordability) "Sell All"/"Buy All"/direct-buy/direct-sell still use
  -- the item still reads Ineligible/greyed-out and still can't be directly bought/sold on the shop's
  own grid or the trade window (`ShopActions.TryBuyFromShop`/`TrySellToShop`'s own affordability
  check refuses that, no state changed), but it can be dragged into the trade window to sit there as
  part of a larger offer (e.g. bartered against other items/Gold the player *does* have) without
  first needing to already afford it outright. A wrong-tag item (`ShopActions.CanTrade` false) or a
  Merged Stack (no single stack to trade) stays fully blocked either way.
- The trade window's own header labels are centered, drawn directly rather than via child
  `TextWindow`s; its currency footers hide the "Gold"/"Credits" label, showing just the amount and
  sprite (`CurrencyElement`'s new `showLabel` parameter).
- The whole-pixel text-position rounding fix (`LabelRenderer.Draw`) is global, applied at every direct
  `SpriteBatch.DrawString` call site in the UI layer, not just this window's own headers.

## Open questions

- Exact `TradeChrome` sizing (window/cell pixel dimensions) and exact row/column arrangement for the
  20-slot body -- left to implementation, no gameplay impact.

## Implementation sketch (files touched)

- `Game/Floors/FloorBuilder.cs` -- `ReserveTradeOfferEntities`.
- `DungeonCrawlerWorld/WorldSessionBootstrapper.cs` -- call it alongside `ReservePlayerEntity`.
- `Presentation/UI/Content/InventoryGridContent.cs` -- landed: `tradeGridIsShopSide` parameter
  (cell-type selection + pricing direction), `GetEffectiveShopStock` (see "Effective shop stock"
  above), the full "Context menu changes" section above ("Add to trade", "Sell All"/"Buy All"
  relabeling, `RemoveFromTrade` bypassing the context menu for trade-grid cells), and
  `CanStageInTrade` (see Confirmed decisions' own "unaffordable-but-tag-eligible" entry). Still to
  do: the full `GridMode` enum (today's narrower `tradeGridIsShopSide` bool has covered every need
  so far).
- `Presentation/UI/Content/InventoryItemStackCell.cs` -- landed: `CanStageInTrade` (see its own doc
  comment), set alongside `CompareState` by `InventoryGridContent.UpdateShopEligibilityState`.
- `Presentation/UI/Content/TradeItemStackCell.cs` -- new, landed: the third cell-detail level (see
  "`TradeItemStackCell`" above).
- `Presentation/UI/Content/EmptyTradeSlotCell.cs` -- new, landed: the empty-slot decoration (see
  "Landed: empty-slot decoration" above), registered in `ElementFactoryRegistry`.
- `Presentation/UI/Content/ShopItemStackCell.cs` -- landed: unsealed, `_totalPrice`/`_quantity`/
  `FavorableColor`/`UnfavorableColor` widened to `protected` for `TradeItemStackCell` to reuse.
- `Presentation/Rendering/ItemIconRenderer.cs` -- landed: `DrawBottomAligned`, generalized from
  `DrawQuantityBadge`'s own bottom-right-only shadowed-text styling.
- `Presentation/UI/Content/CurrencyRowContent.cs` -- landed: `showLabels` passthrough to
  `CurrencyElement`; already implemented `IInventoryDropTarget` before this feature existed, no
  changes needed for currency drag-and-drop itself (see `UiInputController.cs` below).
- `Presentation/UI/Content/CurrencyElement.cs` -- landed: `showLabel` `Configure` parameter.
- `Presentation/Rendering/LabelRenderer.cs`, `Presentation/UI/TextWindow.cs`, `TextBox.cs`,
  `TextDivider.cs`, `Window.cs` -- landed: the global whole-pixel text-rounding fix (see Header:
  Player Value / Shop Value above).
- `Presentation/Input/UiInputController.cs` -- landed: `ResolveTradeAwareItemDrag`, the full
  drag-drop eligibility table for a `StackInstanceId`-tracked stack (see "Landed" under "Drag-drop
  eligibility" above); `ResolveTradeAwareCurrencyDrag`, the currency drag-and-drop stage/unstage
  pairs (see "Landed" under "Currency footer" above); `TryStartContentDrag`'s own pickup gate now
  reads `CanStageInTrade` instead of `CompareState`, so an unaffordable-but-tag-eligible item can
  still be picked up to stage (see Confirmed decisions); `FindDropTargetEntityId`'s own
  `IWholeWindowDropTarget` fallback, widening drop-target resolution from "the Element directly
  under the cursor" to "the whole window, then route by drag-payload type and (trade window only)
  which half" (see "Drop target resolution" above).
- `Presentation/UI/Content/IWholeWindowDropTarget.cs` -- new, landed (see "Drop target resolution"
  above), implemented by `InventoryManagementWindow`/`ShopWindow`/`TradeWindow`.
- `Presentation/UI/Trade/TradeWindow.cs` -- landed: sizing/position constants (kept as this class's
  own private consts, not split into a separate `TradeChrome.cs` -- no second consumer of them
  exists yet, same "no premature split" reasoning `PresentationBootstrapper`'s own static service
  set already follows), the centered-plus-top-aligned-siblings positioning routine (in
  `TradeWindowController`), two headers (Value labels, `ComputeColumnValue`/`ComputeColumnValueText`,
  now live -- see "Header: Player Value / Shop Value" above), two `InventoryGridContent` bodies
  (20-slot, non-scrolling), two `CurrencyRowContent` footers, and the full three-button footer's own
  logic (`BalanceOffer` with its netting correction, `CompleteTrade`, `ReturnEverythingToOwners`,
  the `playerValue >= shopValue`/non-empty enablement gates -- see "Footer buttons" above). The
  Currency footer's `TryGiveCurrencyToShop`/Angel Investor fix (see "Currency footer" above) is
  also landed here, in `CompleteTrade`'s player-to-shop Gold leg.
- `Presentation/UI/Trade/TradeWindowController.cs` -- landed: open/close (shop-close *and*
  inventory-close)/cancel/complete orchestration (a 4th `CloseReason.Complete`, alongside
  `Direct`/`Cancel`/`Cascaded`), the centered-plus-top-aligned-siblings positioning, and calling
  `TradeWindow.ReturnEverythingToOwners` from `HandleWindowClosed` for every reason except
  `Complete`.
- `Presentation/UI/Shops/ShopWindowController.cs` -- landed: opens the trade window alongside the
  shop window.
- Tests: landed -- trade-grid item/currency drag eligibility including the direct-sell/direct-buy
  composition (`UiInputControllerTests`), Value computation including the merged-stack-quantity case,
  Balance Offer (both directions, the remove-all-Gold-then-rebalance correction, and the
  payer-runs-out-of-currency case), Complete (swap + the `>=`-not-just-`==` gate), the unwind
  including the `OpenShopEntityId`-already-cleared regression case, whole-window drop
  resolution (`TradeWindow`'s own left/right split, plus one full `UiInputController` drag onto its
  header area), the "item dropped on the *other* component of the same column (its currency footer,
  not its item grid) still lands as an item stack in that column's own entity, not miscategorized as
  a currency transfer" cross-component-routing case (confirmed not actual special-cased logic -- a
  column's grid and currency-footer windows share one entityId by construction, see `TradeWindow.
  BuildColumn`), and the 20-stack cap refusing a 21st "Add to trade" (confirmed already covered by
  `InventoryActions.TryTransferStack`'s own `InventoryCapacity.HasRoomForNewStack` check, no code
  change needed -- just previously unverified against this specific UI flow) (`TradeWindowTests`,
  `InventoryGridContentShopModeTests`). Also landed: the Give/Give-All-for-shops and Angel
  Investor-chokepoint fixes (see "Currency footer" above) (`CurrencyRowContentTests`,
  `UiInputControllerTests`, `TradeWindowTests`).
