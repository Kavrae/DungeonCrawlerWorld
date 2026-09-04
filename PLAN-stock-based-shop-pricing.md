# Stock-Based Shop Pricing

## Context

`ShopActions.ComputeBuyPrice`/`ComputeSellPrice` (`PLAN-shops.md`) only look at an item's own
`ItemDefinition.GoldValue` and the shop's fixed `BuyMultiplier`/`SellMultiplier` -- no supply/demand.
This plan adds a per-shop, per-item stock curve on top: a shop that's overstocked on an item sells
it cheap and won't pay much to buy more; a shop that's understocked pays a premium to buy and charges
a premium to sell what little it has. Pricing stays entity-scoped (the shop's own current stock),
so hauling goods from an overstocked shop to sell at a distant, understocked one stays a viable,
intended strategy (per the original ask) -- what this plan closes off is looping the *same* shop
back and forth on itself for free money, which stays unprofitable even after a future Charisma/skill
mechanic narrows `BuyMultiplier`/`SellMultiplier` toward parity.

## Data model

### `ItemDefinition.MaximumShopStock` (new, nullable `int`, default `null` -> 999)

Mirrors the existing optional-override shape `MaxStackSize`/`GoldValue` already use. 999 covers most
items; a handful (rare/heavy items) can override it lower. This is the stock level at which the
overstock curve bottoms out.

### `ShopStockPreferenceComponent` (new, `MultiComponentPool` on the shop entity)

```csharp
public readonly struct ShopStockPreferenceComponent(Guid itemDefinitionId, byte preferredStockLevel)
{
    public Guid ItemDefinitionId { get; } = itemDefinitionId;
    public byte PreferredStockLevel { get; } = preferredStockLevel;
}
```

One instance per item type a shop has ever carried, registered in `ShopModule` alongside
`ShopComponent`. Deliberately decoupled from the actual `InventoryItemStackComponent` stacks (which
can fluctuate, split, or drop to zero) -- the preference persists once assigned, exactly like
`ItemDefinition.GoldValue` persists independent of how many units exist right now. Looked up by a
linear scan the same shape `InventoryQueries.TryFindByStackInstanceId` already uses (a shop stocks a
handful of item types, not thousands -- this is not a hot loop).

**Assigned once, the moment an item is first added to a shop** (per the ask): `ShopStock.
GrantRandomStock`'s stock table changes from a flat `IReadOnlyList<ItemDefinition>` to a small
hand-authored `(ItemDefinition Item, byte PreferredStockLevel)` pairing per shop-type stock list --
the same "hand-tuned per item" convention `GoldValue` already established (`PLAN-shops.md`: "every
current item hand-assigned a unique 1-20 value"). If a General Shop is later sold an item type it has
never stocked before (its `AllowedTags` is `null`, so this is possible), the same "first time this
item is added to this shop" moment fires with a `DefaultPreferredStockLevel` fallback constant (e.g.
`20`) instead of a hand-authored one.

Because both the preference and the actual stacks live on the shop's own entity, "per shop, not
global" falls out of the ECS for free -- no extra work needed to isolate shops from each other.

### Aggregate stock, not per-stack

A shop can (and per the existing "no merge on transfer" behavior, `PLAN-shops.md`'s live-testing
section, routinely does) hold the *same* item across several separate physical stacks. Stock status
and pricing are a property of the **item type's total quantity on this shop**, not of any one stack:

```csharp
public static int GetTotalStock(ComponentManager componentManager, int shopEntityId, Guid itemDefinitionId)
    // sum of Quantity across every InventoryItemStackComponent on shopEntityId with this ItemDefinitionId
```

## Stock status bands

`PreferredStockLevel` alone is a single point -- the ask wants a *range* around it before the item
counts as under/overstocked ("percentage margins... gives a range of stock values before the item is
under or over stocked"). This is the same shape a supply-chain reorder-point buffer uses: a dead zone
around the par level where nothing special happens.

**Suggested default: a symmetric 25% band.**

```
UnderstockThreshold = floor(PreferredStockLevel * 0.75)
OverstockThreshold  = ceil(PreferredStockLevel * 1.25), clamped to MaximumShopStock
```

25% is a reasonable starting point (common range in inventory-buffer conventions is 20-30%) --
narrow enough that the status label still means something (a shop sitting close to its par level
reads as "Normal"), wide enough that ordinary buy/sell traffic doesn't flip the label every other
transaction. It's a single tunable constant (not per-item), easy to retune after playtesting.

**Edge case: `PreferredStockLevel == 0`.** `UnderstockThreshold` is then `0`, so the item can never
read as Understocked (there's no meaningful "below zero demand" state) -- only the overstock band
applies. Worth a comment at the call site since it's a real, reachable case (an item a shop doesn't
really want to carry, sold to it anyway).

**Status:**

```
stock < UnderstockThreshold                       -> Understocked
UnderstockThreshold <= stock <= OverstockThreshold -> Normal
stock > OverstockThreshold                          -> Overstocked
```

## Price curve

One multiplier `P(stock)` per item, shared by both buy and sell (why it's shared, not independent
curves, is the anti-exploit section below):

```
Normal band:       P(s) = 1.0
Overstocked:        t = clamp((s - OverstockThreshold) / (MaximumShopStock - OverstockThreshold), 0, 1)
                     P(s) = Lerp(1.0, MinStockPriceMultiplier, t)
Understocked:        t = clamp((UnderstockThreshold - s) / UnderstockThreshold, 0, 1)
                     P(s) = Lerp(1.0, MaxStockPriceMultiplier, t)
```

**Suggested defaults: `MinStockPriceMultiplier = 0.5`, `MaxStockPriceMultiplier = 1.5`** -- symmetric
+/-50%, layered on top of the shop's own existing flat `BuyMultiplier`/`SellMultiplier`:

```
BuyPricePerUnit(s)  = round(GoldValue * BuyMultiplier  * P(s))
SellPricePerUnit(s) = round(GoldValue * SellMultiplier * P(s))
```

### Worked example -- the Potion Shop from the ask

Healing Potion, `GoldValue = 5` in the real catalog; using a round `GoldValue = 10` below purely so
the arithmetic isn't full of `.5` rounding ties. Potion Shop: `BuyMultiplier = 1.10`,
`SellMultiplier = 0.90` (today's actual values). `PreferredStockLevel = 50`, `MaximumShopStock = 999`
(the ask's own numbers) -> `UnderstockThreshold = 37`, `OverstockThreshold = 63`.

| Stock on hand | Status | P(s) | Buy price/unit | Sell price/unit |
|---|---|---|---|---|
| 0 | Understocked (ceiling) | 1.50 | 16G | 14G |
| 10 *(the ask's own example)* | Understocked | 1.36 | 15G | 12G |
| 37 | Normal (threshold) | 1.00 | 11G | 9G |
| 50 | Normal (preferred) | 1.00 | 11G | 9G |
| 63 | Normal (threshold) | 1.00 | 11G | 9G |
| 500 | Overstocked | 0.77 | 8G | 7G |
| 999 | Overstocked (floor) | 0.50 | 6G | 4G |

So: a potion shop stocked at 10 (the ask's own "understocked" example) sells potions for 15G instead
of the base 11G, and pays 12G instead of 9G buying them back -- both up, as specified.

## Bulk / bracket pricing, and why it closes the round-trip exploit

### The exploit

Naively pricing a whole stack at "whatever `P(currentStock)` is right now" is gameable: push a shop
deep into overstock (sell it a pile of the item), buy the whole discounted pile back in one shot
(price computed once at the pre-purchase stock level), then -- since the shop is now near-empty and
Understocked -- sell the same pile back for the high understock price. Two trades, same shop, same
items, guaranteed profit, no travel or risk. This is exactly the round-trip the "brackets" requirement
is there to close.

### The fix: per-unit marginal pricing ("brackets") + a floored spread

**Bracket pricing.** A bulk transaction of `N` units doesn't get one flat per-unit price -- each unit
is priced at the stock level that exists *at the moment it moves*, exactly as described ("buy/sell at
the current amount until they reach the next pricing bracket, then sell at that amount"):

```
Buying N units starting at stock S0:
  TotalBuyPrice  = sum for i in 0..N-1 of round(GoldValue * BuyMultiplier  * P(S0 - i))

Selling N units starting at stock S0:
  TotalSellPrice = sum for i in 0..N-1 of round(GoldValue * SellMultiplier * P(S0 + i))
```

This is a straightforward `O(N)` loop (`N` is at most a few hundred per stack in practice, this runs
once per trade, not per frame -- not worth the complexity of a closed-form per-bracket sum unless
profiling ever says otherwise). It replaces the flat `ComputeBuyPrice(shop, item) * stack.Quantity`
line in `ShopActions.TryBuyFromShop`/`TrySellToShop`, and the matching affordability check in
`InventoryGridContent.UpdateShopEligibilityState` (today: `perItemPrice * stack.Quantity`) needs the
same replacement so the eligibility glow matches what the trade will actually charge.

Because the curve is continuous, this alone already blunts the exploit (the price rises back toward
Normal as you buy deeper into a pile, and falls back toward Normal as you sell into an empty shop) --
but it does not, by itself, guarantee the round trip is unprofitable. That guarantee comes from the
second piece:

**A floored buy/sell spread.** Buy and sell both read off the *same* curve `P(stock)`, differing only
by the shop's own flat `BuyMultiplier` vs `SellMultiplier`. As long as `BuyMultiplier` stays strictly
greater than `SellMultiplier` by some minimum floor -- e.g. **never let a future Charisma/skill
reduction close the gap below 0.05** (`BuyMultiplier - SellMultiplier >= 0.05`, always, even at max
Charisma/skill) -- a same-shop round trip is a mathematical, guaranteed loss:

For *any* sequence of buys and sells at one shop that returns its stock to where it started (a plain
buy-then-sell-back, or any more elaborate interleaving), the total units bought and sold walk the same
set of stock levels. Since every unit's price is `GoldValue * P(stock-at-that-moment) * (BuyMultiplier
or SellMultiplier)`, and the two legs share the identical `P(stock)` values (same stock range, just
walked in opposite order), the round trip's net result reduces to:

```
Profit = GoldValue * (sum of P(s) over the walked stock range) * (SellMultiplier - BuyMultiplier)
```

`SellMultiplier - BuyMultiplier` is negative by construction (and floored away from zero), and the
summed `P(s)` term is always positive (P ranges from `MinStockPriceMultiplier` to
`MaxStockPriceMultiplier`, both positive) -- so `Profit` is always negative, **regardless of curve
shape, band width, starting stock level, or how narrow Charisma/skill bonuses eventually make the
margin.** This is the actual guarantee the ask is after, not just "the constants happen to feel safe
today."

### Worked round-trip example

Same Potion Shop as above (`GoldValue = 10`, `BuyMultiplier = 1.10`, `SellMultiplier = 0.90`,
`Preferred = 50`, thresholds 37/63, `Min/Max = 0.5/1.5`). Shop starts fully overstocked at 999; player
buys out all 999 in one bulk purchase, then immediately sells all 999 back in one bulk sale.

Splitting the 0-999 range into its three brackets and averaging `P(s)` in each (exact for a linear
ramp: average of the two endpoints):

- Overstock bracket, stock 64-999 (936 units): `P` ranges 0.50 -> ~1.00, average ~0.75
- Normal bracket, stock 37-63 (27 units): `P = 1.00` flat
- Understock bracket, stock 0-36 (37 units): `P` ranges ~1.01 -> 1.50, average ~1.26

Weighted average `P` across all 999 units: `(936*0.75 + 27*1.00 + 37*1.26) / 999 ~= 0.776`.

```
TotalBuyCost     ~= 999 * 10 * 1.10 * 0.776 ~= 8,530 G
TotalSellRevenue ~= 999 * 10 * 0.90 * 0.776 ~= 6,980 G
Net result: a ~1,550 G LOSS for buying out the whole shop and selling it straight back.
```

Matches the formula: `loss = GoldValue * sum(P(s)) * spread = 10 * 775.6 * 0.20 ~= 1,551`. Even if a
future Charisma/skill bonus narrows the 20% spread all the way down to the proposed 5% floor, the
same round trip still loses `10 * 775.6 * 0.05 ~= 388 G` -- smaller, but never a profit. Meanwhile,
buying that same 999-unit pile at ~8,530G and hauling it to a *different*, undrained shop to sell at
its own Normal-band price (~9G/unit * 999 ~= 8,990G, or more if that shop happens to be understocked
too) stays a legitimate profit -- exactly the transport-arbitrage strategy the ask wants preserved.

## UI changes

### Price line color + "Overstocked"/"Understocked" hover text

`ShopActions` gains a stock-status query (`GetStockStatus(componentManager, shopEntityId,
itemDefinitionId) -> StockStatus` enum: `Normal`/`Understocked`/`Overstocked`), usable from both the
shop's own grid and the player's own grid while shop mode is active (both already share pricing/
eligibility logic today, per `InventoryGridContent.UpdateShopEligibilityState`).

- **`ShopItemStackCell.DrawContent`** -- the price line's `textColor` (currently always plain
  white/grey) switches to `Color.LightGreen` when this `StockStatus` is a *favorable* price for
  whichever direction this specific grid trades in, `Color.IndianRed` when it's *unfavorable* --
  **not** a fixed Overstocked=red/Understocked=green mapping, since "favorable" flips with
  direction: Overstocked is a good deal buying (cheap) but a bad deal selling (the shop won't pay
  much for more of what it already has), and Understocked is the reverse. Concretely: on the shop's
  own grid (`IsThisGridTheShop`, a purchase), Overstocked -> favorable/green, Understocked ->
  unfavorable/red; on the player's own grid (a sale), Understocked -> favorable/green, Overstocked
  -> unfavorable/red. Matches the existing Better/Worse convention `ItemDetailsWindow` already uses
  for the same framing (`ItemDetailsWindow.BetterColor`/`WorseColor`). Only the price line changes
  color -- the name line stays as-is.
- **Hover tooltip** -- `InventoryGridContent.UpdateHover` builds `summary` from `definition.Summary`
  today; while shop mode is active it appends one more line ("Overstocked" / "Understocked") after
  the description, in the matching color. `Tooltip`/`TextWindow` currently only support a single
  `TextColor` for their entire body, so this needs a small, additive change to `Tooltip`: an optional
  `SetStatusLine(string? text, Color color)` that draws one extra line below the wrapped body via
  `LabelRenderer.DrawLeftAligned` directly (same idea as `ShopItemStackCell`'s own custom multi-color
  draw, not a rework of `TextWindow`'s single-color pipeline), with `RecalculateWrapContentSize`
  extended to add that line's height when present -- the same shape `UseFixedWidth` already uses to
  extend the base recalculation. This is new tooltip-composition capability, not a change to any
  *other* Tooltip consumer (`AbilityScoreWindow`, `HotbarController`) -- they simply never call it.

### On-hand quantity in the price line

Today: `"{total}G ({perItem} each)"` for a multi-unit stack, plain `"{price}G"` for a single unit
(`ShopItemStackCell.DrawContent`). The ask wants the on-hand count visible too, e.g. `"50G (10 each x
5)"` for a 5-unit stack at 10G/unit.

**Suggested more concise format: `"{total}G ({quantity}x{perItem}G)"`** -- e.g. `"50G (5x10G)"` instead
of `"50G (10 each x 5)"`. Same information (per-unit price and stack size, from which the total is
derivable), noticeably shorter, and reads left-to-right as "how many, at what price" rather than
repeating "each" and "x" as separate words. Single-unit stacks keep the existing plain `"{price}G"`
(no `"1x"` clutter). This is a cosmetic call, not a hard recommendation -- happy to keep the literal
`"{perItem} each x {quantity}"` phrasing from the ask if it reads clearer in the actual cramped cell
width once it's on screen.

Note this is the *stack's own* quantity, not the item's total on-hand across every stack on the shop
(the number pricing is actually keyed off) -- those can differ once a shop holds an item across
several separate stacks. If that distinction matters to a player deciding whether to buy now vs. wait
for a restock, the hover tooltip's new status line is the more natural place to surface the real
total (e.g. an optional `"Stock: 233/999"` line) rather than cluttering the compact per-cell price
text with a number that isn't this cell's own.

## Alternatives considered

- **Independent buy/sell curves** (rather than one shared `P(stock)` scaled by `BuyMultiplier`/
  `SellMultiplier`) -- rejected: without a shared curve, there's no clean invariant that guarantees
  round-trip unprofitability; it would come down to hand-tuning `Min`/`MaxStockPriceMultiplier` pairs
  per direction and hoping they never cross, which is exactly the fragile approach the ask is trying
  to avoid ("this may require... brackets" implies wanting something structurally sound, not just
  well-tuned constants).
- **A hard cooldown/decay** on how fast a shop's stock (and thus its price) can move, instead of a
  floored spread -- rejected as the primary mechanism: it would also suppress legitimate large single
  trades and adds a time dimension (needs a tick/save-compatible timer) for a problem the spread floor
  already solves algebraically, with no extra state. Worth revisiting later only if playtesting shows
  price swings feel too responsive to single trades for reasons unrelated to the exploit.
- **Per-unit rounding vs. one rounding pass per bracket** -- both were considered for the bulk-price
  sum; per-unit (the simple `O(N)` loop) was chosen over a closed-form per-bracket arithmetic-series
  sum for implementation simplicity, since `N` is small and this isn't a hot path. The closed-form
  version is an available, purely internal optimization if it's ever needed.

## Confirmed decisions

- `PreferredStockLevel` stays a `byte` (255 cap) -- confirmed acceptable, no planned item needs a par
  level above that.
- Band-width 25%, curve 0.5x-1.5x -- confirmed as final, not just a starting recommendation.
- `MaximumShopStock` (999 default) is also a **hard sell cap**: a shop refuses to buy more of an item
  once its total on-hand for that item is already at `MaximumShopStock` (`TrySellToShop` gains this
  check alongside its existing tag/capacity/affordability checks -- fails with no state changed, same
  shape every other precondition there already follows).

## Live-testing fixes

Same shape as `PLAN-shops.md`'s own section -- bugs/gaps only surfaced once this was actually
running:

- **A hung, then crashing, test run**: the first `Tests/Presentation/TooltipTests.cs` pass hung for
  several minutes, then crashed with a FreeType "stack overflow" once forcibly killed and re-run.
  Root cause: `TestFonts.Shared` (a single, explicitly non-thread-safe FreeType-backed FontService
  shared across the whole test run) requires every consuming test class to carry `[DoNotParallelize]`
  -- documented on `TestFonts` itself -- which the new class was missing, so MSTest ran its cases
  concurrently against the shared font state. Adding the attribute fixed it outright; the underlying
  `SetStatusLine`/`RecalculateWrapContentSize` logic was correct the whole time.
- **Tooltip status line visually cut off -- initially misdiagnosed**: first suspected (and "fixed")
  as `Tooltip` not reserving enough box height, via an unconditional extra-padding constant. Turned
  out to be wrong: the same cutoff was independently confirmed on `AbilityScoreModifierRow`'s own
  text and elsewhere -- components that never touch `Tooltip` at all -- which pointed at shared
  infrastructure instead. The actual bug: `LabelRenderer.GetLeftAlignedPosition`/
  `GetRightAlignedPosition` centered text vertically against `font.MeasureString(text).Y`, the same
  "generic line box sits well below where the ink actually renders" issue `GetCenteredPosition`'s own
  doc comment already documented and fixed for *itself* years earlier (e.g. "g" at font size 24
  measures a ~29px box but its ink occupies only the bottom two-thirds) -- `GetLeftAlignedPosition`/
  `GetRightAlignedPosition` were just never given the same fix. Centering a tight, `LineHeight`-sized
  footprint against that oversized box pushed every line's text low enough to bleed a descender past
  the footprint's own bottom edge -- affecting every `DrawLeftAligned`/`DrawRightAligned` consumer
  (`AbilityScoreModifierRow`, `ShopItemStackCell`'s name/price lines, `Button`'s title glyphs,
  `Tooltip`'s own status line), not just this one. Fixed at the source: both methods now center
  against `font.LineHeight` instead, matching `TextDivider`'s own already-correct technique (its
  `DrawContent` does this by hand) -- see `Tests/Presentation/Rendering/LabelRendererTests.cs`'s new
  regression tests. The speculative per-Tooltip padding was reverted once the real fix landed.
- **"Overstocked"/"Understocked" wording was misleading when a sale is flatly refused**: on the
  player's own grid, a wrong-tag item or one the shop is already at `MaximumShopStock` for (the hard
  sell-cap) still showed a stock-status word implying the sale just costs more -- when the shop
  actually won't buy it at all. Both cases now show "Shop will not buy" instead, red, checked before
  falling through to the ordinary stock-status line. Shop-side grid unaffected (buying from the shop
  has no analogous cap).

## Phase 5: discrete price bands + two new displays (scoped, not yet implemented)

### The problem

Live playtesting surfaced a real legibility problem the color/status-line work doesn't fully solve:
the *same* "favorable, green" price can appear on both sides of a round trip that actually lost the
player money -- e.g. a General Shop at 0 stock quotes 683G to *sell* 93 Volatile Concoctions (green,
11G each); after selling, the shop's own grid quotes 1044G to *buy* those same 93 back (also green,
10G each) -- both individually correct and favorable-for-their-direction (per the confirmed color
rule), but nothing in the UI explains *why* the numbers moved, or that buying them straight back
would cost 361G more than was just received.

### Options considered

Four were mocked up and compared against real precedent (Skyrim's "Dynamic Pricing Framework" mod
ships almost exactly today's Phase 3/4 baseline -- a cheaper/pricier highlight in the barter menu --
confirming that part is already a proven pattern, not a placeholder; EVE Online/RuneScape's
market-history browsers are a different problem shape entirely, built for many-trader economies with
real elapsed-time history, not a single-player curve driven only by the player's own trades):

- **A full price/stock curve graph** -- most information-dense, but needs genuinely new rendering
  primitives (plotted lines, axis ticks) this codebase's UI layer has never had, and this exact
  session found three separate, subtle text-rendering bugs in the *existing* primitives
  (`LabelRenderer`, `TextWindow`, `Tooltip`'s viewport handling). Rejected -- the risk of a novel,
  unproven rendering path outweighs the payoff here.
- **A simple delta badge** ("Understocked, +33% vs normal") -- cheap, but barely adds anything Phase
  4's status line doesn't already say. Rejected -- doesn't address the actual round-trip confusion.
- **A discrete band table** -- a handful of named bands, each a flat multiplier, shown as a short
  table with the current band highlighted. Same shape as any bulk/volume-discount pricing table (a
  well-established convention), and reuses UI primitives this codebase already has proven (rows of
  text, a highlighted row -- the same shape `ItemDetailsWindow` already uses). **Adopted.**
- **A per-trade bracket receipt** -- shown for the specific stack about to be traded, walking the
  actual bands crossed: `21 x 12G`, `42 x 9G`, `30 x 7G`, total `840G`. Directly answers "why did
  this specific trade cost what it did." **Adopted**, as a complement to the band table.

### Replacing the continuous curve with 5 discrete bands

Banding isn't just a display choice -- it changes the underlying pricing model
(`ShopStockPricing.ComputeStockPriceMultiplier`), and does so for the better on two counts besides
legibility:

- **The anti-exploit guarantee is unaffected.** The Phase 2 proof (`profit = GoldValue * sum(P(s)) *
  (SellMultiplier - BuyMultiplier)`) never depended on `P(s)` being continuous -- only that the
  *same* `P(s)` applies to both buy and sell, and that `BuyMultiplier` stays above `SellMultiplier`
  by a floored gap. A stepped `P(s)` satisfies both identically. Same guarantee, same proof, still
  holds.
- **Bracket pricing becomes exact, not just simpler.** Every unit within one band shares the *same*
  flat multiplier, so a band's contribution to a bulk trade is `unitsInBand * round(GoldValue *
  shopMultiplier * bandMultiplier)` -- one rounding operation per band, mathematically identical to
  rounding each of those units individually and summing (they're all the same value), not an
  approximation. `ComputeBulkBuyPrice`/`ComputeBulkSellPrice` go from an `O(N)` per-unit loop (up to
  a few hundred iterations) to an `O(bands crossed)` loop -- at most 5. This closed form is also what
  makes the per-trade receipt possible at all: with a continuous curve, a "line per price" breakdown
  needs one row per unit (nonsense) or just an average (uninformative); with bands, it's naturally a
  handful of rows.

**Suggested default: 5 bands, uniform 25%-of-`PreferredStockLevel` width, extending today's already-
confirmed 25% band/0.5x-1.5x range rather than replacing the numbers, just discretizing the space
between them:**

Let `P` = `PreferredStockLevel`, `M` = the item's `MaximumShopStock`. Four edges, each still `floor`/
`ceil` the same way today's two thresholds already are:

```
E1 = floor(0.50 * P)              -- Desperate / Understocked boundary
E2 = floor(0.75 * P)              -- Understocked / Normal boundary (== today's UnderstockThreshold)
E3 = ceil(1.25 * P), clamped to M -- Normal / Overstocked boundary (== today's OverstockThreshold)
E4 = ceil(1.50 * P), clamped to M -- Overstocked / Flooded boundary
```

| Band | Stock range | Multiplier |
|---|---|---|
| Desperate | `< E1` | 1.50 |
| Understocked | `E1` to `< E2` | 1.25 |
| Normal | `E2` to `E3` | 1.00 |
| Overstocked | `> E3` to `E4` | 0.75 |
| Flooded | `> E4` | 0.50 |

Evenly spaced in 0.25 steps between the already-confirmed 0.5x/1.5x extremes -- easy to remember,
nothing new to tune beyond the two extra edges. The two inner bands are exactly today's existing
Understocked/Overstocked zones, just no longer continuously varying within them; Desperate and
Flooded are new, catching everything further out. `E1`/`E2` collapsing to 0 when `P = 0` reproduces
today's existing edge case (an item with no par level can never register as Understocked/Desperate)
with no special-case code needed, same as today.

**Known tradeoff, worth confirming rather than deciding silently:** because `E4` is anchored to `P`
(not to `M`), a shop can hit the flat 0.5x Flooded floor much sooner, relative to `M`, than the old
continuous curve did -- e.g. at `P = 50`, `M = 999`, Flooded starts at stock 76, whereas the old
lerp from `OverstockThreshold` (63) to `M` (999) was still barely discounted at stock 76 (`P(76) ~=
0.99`). Bands make deep discounts arrive faster. If that's not the intended feel, the alternative is
anchoring `E4` (and `E1`'s counterpart, if it matters at the understock end) to the *midpoint* between
`E3`/`E2` and `M`/`0` instead of a fixed `1.5P`/`0.5P` -- more faithful to the old curve's shape, more
moving parts. Recommend starting with the simpler, purely-`P`-relative version above and only adding
the `M`-relative variant if playtesting says shops discount too aggressively.

#### Worked example (Potion Shop, `GoldValue = 10`, `BuyMultiplier = 1.10`, `SellMultiplier = 0.90`, `Preferred = 50`)

`E1 = 25`, `E2 = 37`, `E3 = 63`, `E4 = 75`.

| Band | Stock range | Buy price/unit | Sell price/unit |
|---|---|---|---|
| Desperate | 0-24 | 16G | 14G |
| Understocked | 25-36 | 14G | 11G |
| Normal | 37-63 | 11G | 9G |
| Overstocked | 64-75 | 8G | 7G |
| Flooded | 76-999 | 6G | 4G |

### `StockStatus` grows to 5 values

`Normal`/`Understocked`/`Overstocked` becomes `Desperate`/`Understocked`/`Normal`/`Overstocked`/
`Flooded` -- strictly more informative for free, since the price line/hover status line already just
draws `status.ToString()`. The one real code change: `ShopItemStackCell.PriceIsFavorable`/
`PriceIsUnfavorable` currently compare against exactly one enum value per direction (`Overstocked` on
the shop's own grid, `Understocked` on the player's); with 5 values, "favorable" needs to mean
*either* outer band on the correct side. Cleanest as a signed band index (`Desperate = -2`,
`Understocked = -1`, `Normal = 0`, `Overstocked = +1`, `Flooded = +2`) so favorability collapses to a
sign check (`index > 0` favorable on the shop's grid, `index < 0` favorable on the player's) instead
of enumerating cases -- and the same index is what a band table (below) iterates to render its rows.

### The two new displays

**Band table** (replaces/extends today's plain hover status line while shop mode is open): the 5
rows from the worked-example table above, current band highlighted, shown for whichever item is
hovered. Lives in the same `Tooltip` popup Phase 4 already extends -- another few lines under the
description, same shape as the existing status line, just a short table instead of one word.

**Per-trade bracket receipt**: for the *specific* stack being hovered (its actual `Quantity`, not a
hypothetical), the per-band subtotals `ComputeBulkBuyPrice`/`ComputeBulkSellPrice`'s closed form
already computes internally -- surfaced instead of collapsed into just the final total. Natural home
is the same tooltip, appended below the band table when the hovered cell represents more than one
unit (a single-unit stack has nothing to break down -- one band, one line, already exactly what the
existing price line already shows).

Both are additive to the existing Tooltip/status-line work, not a replacement of it -- the favorable/
unfavorable color rule, "Shop will not buy" wording, and everything else from Phases 3-4 carry over
unchanged.

### Landed

`ShopStockPricing`'s band lookup/closed-form bracket math, the `StockStatus` enum expansion (explicit
`-2..+2` values doubling as a severity index), and `ShopItemStackCell`/`InventoryGridContent`'s
signed-index favorability change are all in and tested (`ShopStockPricingTests.cs`,
`InventoryGridContentShopModeTests.cs` -- new Desperate/Flooded coverage alongside the existing
Understocked/Overstocked/Normal cases). The confirmed same-shop round-trip loss for the worked
example (buying out a fully-`Flooded` 999-stock shop, then selling it all back) is **~1988G** under
banding (Buy 6489G, Sell 4501G) -- higher than the old continuous curve's ~1550G for the same
scenario, since `Flooded`'s flat 0.5x now covers most of the 76-999 range instead of only approaching
0.5x right at the very top. Still a guaranteed loss either way, confirmed at both today's actual
10%/20% shop margins and a razor-thin 0.01 spread (a stand-in for a maxed-out future Charisma/skill
reduction) -- the anti-exploit proof holds exactly as predicted.

The two new `Tooltip` displays are also landed and tested:

- **`Tooltip` itself** grew from a single optional status line (`SetStatusLine`) to an ordered list of
  `TooltipRow`s (`SetRows`) -- `LeftText`/`RightText`/`Color` per row, mirroring `Button`'s own
  existing LeftText/RightText row shape (`DrawLeftAligned` + `DrawRightAligned` on the same line) so
  the price column reads flush-right rather than just concatenated into one string. Box height grows
  by one `ContentFont.LineHeight` per row plus a single `LinePadding` gap above the whole block, same
  general shape the single-status-line version already had.
- **`ShopStockPricing`** gained `GetAllBands()` (fixed display order), `GetBandPricePerUnit`, and
  `ComputeBulkBuyBreakdown`/`ComputeBulkSellBreakdown` (the same band-overlap logic
  `ComputeBulkBuyPrice`/`ComputeBulkSellPrice` already used internally, now also exposed per-band
  instead of pre-summed -- kept as a separate method rather than having the price functions build a
  list and sum it, so the hot trade-execution path doesn't allocate one on every real trade).
- **`InventoryGridContent.ComputeHoverRows`** (renamed from `ComputeHoverStatusLine`) builds the band
  table unconditionally while shop mode is open (all 5 rows, each labeled with its own stock range via
  `ShopStockPricing.GetBandRange`, e.g. `Understocked (10-14)`, `Flooded (76+)` for the open-ended top
  band) and appends the per-trade bracket receipt -- one row per band the specific hovered stack's own
  `Quantity` actually crosses, plus a Total row -- only when that stack is more than 1 unit *and*
  actually crosses more than one band (a single-band trade's exact price is already the band table's
  own row for it). "Shop will not buy" is unchanged, still a single row replacing the whole table.

**First live-testing round produced 6 refinements**, all landed:

- Band-table rows no longer color their own text by favorability. Instead the row matching the shop's
  *current* band gets an inner-fade glow (`GlowRenderer.Draw`, `GlowMode.InteriorFade`) behind it --
  green for `Desperate`/`Understocked`, red for `Overstocked`/`Flooded`, white for `Normal`. This is a
  fixed mapping keyed only on the band itself (`InventoryGridContent.GlowColorFor`), unlike
  `ShopItemStackCell`'s own price-line color which still flips by grid direction (buying vs. selling) --
  the band table always describes the shop's own stock, not a trade direction.
- Each row's range is now sourced from `ShopStockPricing.GetBandRange`, promoted from `private` to
  `public` for exactly this purpose.
- A divider row (`TooltipRow.Divider`, a new `IsDivider`/`GlowColor`-bearing variant of `TooltipRow`)
  separates the description text from the band table, and a second one separates the band table from
  the per-trade receipt when present.
- `Tooltip` grew 2px of padding on all four sides of the row block (`RowPadding`) and a flat 8px width
  allowance (`ExtraWidthForRows`) to fit the wider range-annotated row text without wrapping.

All backed by new tests (`ShopStockPricingTests.cs`'s `GetAllBands`/`GetBandPricePerUnit`/
`ComputeBulk*Breakdown` coverage, `TooltipTests.cs`'s multi-row sizing and divider/glow rendering). The
actual in-game rendering -- row alignment, whether the receipt reads clearly against a real tooltip's
cramped width, whether the glow reads clearly against the row text -- still needs a live look, same as
every other UI piece in this plan.

**Second live-testing round produced 3 more refinements**, all landed:

- The shop-mode hover popup (`InventoryFolderController._inventoryHoverPopup`, `InventoryChrome.
  InventoryHoverPopupMaximumSize`) is now a fixed 225px wide whenever it's showing rows
  (`Tooltip.UseFixedWidth = rows is not null`, toggled per-call in `InventoryGridContent.UpdateHover`)
  -- the range-annotated band rows no longer fit the old shrink-to-content width at 220. A plain
  description-only tooltip (no shop mode, no rows) is untouched and still shrinks to content as
  before.
- The band range moved out of `LeftText` (`"Understocked (10-14)"`) into its own `MiddleText` column
  (`TooltipRow`'s third text field), drawn left-aligned starting at a shared x computed from the
  widest `LeftText` among the rows that use one -- so every row's range lines up in its own column
  instead of trailing at a different x per row depending on how long that row's band name is.
- The per-trade receipt is no longer conditional on the stack being more than 1 unit or crossing more
  than one band -- it's now always appended (divider included) whenever there's a concrete quantity to
  price at all. If the trade stays within a single band, the per-band listing itself is skipped (that
  band's own row in the table above already shows the identical per-unit price) and only the divider +
  Total remain, rather than omitting the receipt section entirely.

Also backed by new tests (`TooltipTests.cs`'s `MiddleText` construction coverage and a
`UseFixedWidth`-pins-width-regardless-of-content case). Still needs the same live look as everything
else here -- in particular whether 225px is enough once real (not test) band names and gold values are
on screen together.

**Third live-testing round**: `Tooltip`'s own background is now 90% opaque (`new Color(45,45,45) *
0.90f`), up from the shared `WindowPalette.PanelBackgroundColor`'s 85% -- applied only inside `Tooltip.
Build`, not to the palette constant itself, so every other window still using that constant (HealthWindow,
ItemDetailsWindow, ShopWindow, etc.) is unaffected.

With the band table and per-trade receipt now covering the per-unit price in full, `ShopItemStackCell`'s
own price row on the grid itself was simplified: instead of `"{total}G ({quantity}x{perItem}G)"`, it now
shows just the stack's quantity (left-aligned, plain text, omitted for a single-unit stack) and the
total cost (right-aligned) -- only the total gets the favorable/unfavorable/neutral color, the quantity
stays plain white/gray. `ShopItemStackCell.SetPrice` dropped its now-unused `perItemPrice` parameter, and
`InventoryGridContent.ComputeShopPrices` (renamed `ComputeShopTotalPrice`) dropped the now-unused
per-unit half of its return tuple.

**Fourth live-testing round**: the band table's `RightText` (per-unit price) is now inset from the row's
right edge by `GlowRenderer.FadeRingCount` (newly public, 5px) before being right-aligned, applied to
every row uniformly (not just the glowing one, so the price column stays flush down the block) --
without it, the current band's own price sat directly under its own glow's brightest, innermost ring.
Also, `Color.IndianRed` -- previously three independently-declared but intentionally-matched constants
(`ShopItemStackCell.UnfavorableColor`, `InventoryGridContent.UnfavorableStatusColor`, `ItemDetailsWindow.
WorseColor`) -- read too dark/muted against the tooltip's dark panel background; all three moved to
`Color.LightCoral` together, keeping the existing "same pair everywhere" convention those three already
documented for themselves.

**Follow-up correction**: the "price under the glow" report was actually about `ShopItemStackCell`'s own
grid-square price row, not the Tooltip band table (which had already been fixed and reads fine) --
`ShopItemStackCell.DrawContent`'s own total-price `DrawRightAligned` call sits at the cell's right edge,
exactly where `CompareState.Eligible`'s own `InteriorFade` glow fades in from. Same fix, same reasoning:
inset the total's own footprint by `GlowRenderer.FadeRingCount` before right-aligning it. The stack-count
(left column) is untouched -- nowhere near that edge.

## Implementation sketch (files touched)

- `Game/Modules/Inventory/ItemDefinition.cs` -- `MaximumShopStock` optional field.
- `Game/Modules/Shops/Components/ShopStockPreferenceComponent.cs` -- new.
- `Game/Modules/Shops/ShopModule.cs` -- register the new `MultiComponentPool`.
- `Game/Modules/Shops/ShopStockPricing.cs` -- new: `GetTotalStock`, `GetStockStatus`,
  `ComputeStockPriceMultiplier`, `ComputeBulkBuyPrice`, `ComputeBulkSellPrice`.
- `Game/Modules/Shops/ShopActions.cs` -- `TryBuyFromShop`/`TrySellToShop` swap their flat price calc
  for the new bulk calc.
- `Game/Blueprints/Objects/ShopStock.cs`, `PotionShopStock.cs`, `GeneralShopStock.cs` -- stock tables
  gain hand-authored preferred levels per item.
- `Presentation/UI/Content/InventoryGridContent.cs` -- `UpdateShopEligibilityState` bulk-price parity;
  `UpdateHover` appends the status line in shop mode.
- `Presentation/UI/Content/ShopItemStackCell.cs` -- price-line color, on-hand text format.
- `Presentation/UI/Tooltip.cs` -- `SetStatusLine` addition.
- Tests: `Tests/Modules/Shops/ShopActionsTests.cs` (bulk pricing math, round-trip-loss regression
  test), `Tests/Presentation/InventoryGridContentShopModeTests.cs` (status color/label wiring).
