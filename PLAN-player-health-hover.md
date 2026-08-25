# Player health bar hover: per-body-part HP dropdown

(Design plan, saved for implementation per this repo's convention -- see `PLAN-body-parts.md`/
`PLAN-human-race.md`. Item 6 of the current body-parts follow-up work, per TODO.md's "Player health
bar hover -- per-body-part HP dropdown" entry.)

## Context

`PlayerHealthBarContent` (`Presentation/UI/Content/`) is the permanent top-right HUD health bar --
always visible, shows the player's summed current/max fraction via `HealthQueries.TryGetTotals`.
The player is Complex health today (the `Human` race, `PLAN-human-race.md`), so it genuinely has
per-part detail worth surfacing on hover -- TODO.md's own note that "a SimpleHealth player has
nothing to hover into beyond the total line" is now stale.

The ask: hovering the bar shows a small dropdown-style popup, one row per body part (plus a Total
row first), each row showing the part's name and a small visual HP bar rather than a name+percentage
text line (TODO.md's original, more conservative spec).

## Investigated: the existing hover-popup mechanism is text-only

TODO.md's own entry claims this reuses "the same delay-gated `HoverPopupWindow` pattern
`InventoryGridContent`/`AbilityScoreWindow` already use." That class doesn't exist under that name.
The real, single hover-popup mechanism in this codebase is **`Tooltip`** (`Presentation/UI/Tooltip.cs`,
a `TextWindow` subclass) -- `HotbarController`'s Armed Hotkey Summary popup and
`InventoryFolderController`'s item-hover popup both drive one directly via
`tooltip.ShowNear(targetBounds, anchor, gap, bodyText, titleText)`. Its content is exactly one
`string` (`UpdateText`, inherited from `TextWindow`) -- **it cannot host a real pixel-drawn bar**,
only text.

This is a real fork, not a detail to gloss over -- two ways to satisfy "small visual HP bar":

**Option A -- text-rendered bar, reusing `Tooltip` as-is.** Build each row as a line of monospace
text using `StringUtility.BuildPercentageBar` -- the exact helper `BodyPartComponent.ToString()`
already uses (landed earlier this session): `Head       [====______] 40/40`. Call
`tooltip.ShowNear(barBounds, anchor, gap, bodyText)` with all rows joined by newlines, one call,
no new popup type. Cheapest by far -- zero new rendering infrastructure, matches the "glanceable,
lightweight" framing TODO.md's own entry already uses to contrast this against the real `HealthWindow`
(item 5), and is visually consistent with the `BodyPartComponent.ToString()` debug bars already
built this session.

**Option B -- a real pixel-drawn bar, new popup type.** A new small popup (not `Tooltip` -- needs a
top-level `Window` with real child content, drawing each row as a name label via `LabelRenderer`
plus a small `ResourceBarRenderer.Draw` call, the same renderer `HealthBarElement`/
`PlayerHealthBarContent`'s own big bar already use) sized to the entity's row count. Matches "visual
bar" the most literally -- looks like every other health bar in the game, not ASCII art -- at the
cost of being the first hover popup with real non-text content, needing its own positioning/sizing/
hover-lifecycle code roughly mirroring what `Tooltip`+its callers already do for free.

**Recommendation: Option A.** The monospace bar is still a real visual bar (same one
`BodyPartComponent.ToString()`/the debug inspector already show), costs a fraction of the code,
and matches this item's own "lightweight glance, not the full HealthWindow" framing better than a
second custom popup-rendering system would. Worth it only if you want this to visually match the
game's pixel health bars specifically (Option B) rather than a console-style bar (Option A).

## Design (both options share this shape; only row *rendering* differs)

### Hover detection (`PlayerHealthBarContent`)

`PlayerHealthBarContent` already has its own per-frame `Update(GameTime)` hook (used today to
compute `_healthFraction`). Add self-polled hover tracking there, mirroring
`InventoryFolderController.UpdateHover`'s `Mouse.GetState()` self-poll (rather than routing through
`UiInputController`, which only resolves hit-tests for things that need click/drag handling --
this bar is hover-only, read-only): hit-test the mouse position against `_hostWindow`'s own
absolute bounds, track consecutive hovered frames, gate showing the popup behind
`HudMetrics.HoverTooltipDelayFrames` (the same constant `HotbarController`/`InventoryFolderController`
already use), hide immediately on hover loss (no delay on hiding, matching `HotbarController`'s own
convention).

### Row content

First row is always **Total** -- `HealthQueries.TryGetTotals`'s summed current/maximum (the exact
fraction the big bar itself already shows), labeled "Total" rather than a body-part name. Then one
row per `BodyPartComponent` the player owns, in whatever order `MultiComponentPool`'s own per-entity
chain enumerates them (no re-sorting -- matches how every other body-part enumeration in this
codebase, e.g. `HealthQueries.TryGetTotals`'s own Complex-path sum, already walks the chain
unordered). A Simple-health player (not true today, but the code must degrade gracefully rather
than assume Complex) shows only the Total row -- nothing to enumerate.

### Popup ownership/lifecycle

`PlayerHealthBarContent` owns the popup instance directly, created once via
`ElementPoolService.CreateElement` and added to `UiLayer.Tooltip`, mirroring
`HotbarController`'s own `_summaryWindow` field/`Initialize` pattern exactly. Shown/repositioned/
hidden every `Update` call based on the hover state above -- `ShowNear`-style positioning anchored
to the health bar's own bounds (`PopupAnchor.South`, since the bar sits in the top-right HUD corner
per `PlayerHealthBarContent`'s own doc comment -- North would risk going off the top of the screen).

### Option A specifics

Build the multi-line body text once per `Update` call the popup's visible (or every call while
hovered -- current health changes over time from regen/damage, so a stale cached string would
drift): `Total` row plus one row per part, each via `StringUtility.BuildPercentageBar(name, current,
maximum, barSize)` at a smaller `barSize` than `SimpleHealthComponent.ToString()`'s 20 (something
like 10-12, to keep the popup narrow) followed by ` current/maximum`. Call the existing shared
`Tooltip` instance's `ShowNear` with the joined text, no title.

### Option B specifics (if chosen instead)

New `PlayerHealthHoverPopup` (or similar) -- a top-level pooled `Window` (added to `UiLayer.Tooltip`,
same tier as `Tooltip` itself, for the same "always draws above whatever it's describing" reason).
One row per part drawn directly in its `IElementContent.DrawContent` (no per-row child Elements
needed -- mirrors `PlayerHealthBarContent`'s own direct-draw style rather than
`InspectionWindowContent`'s per-row child-Element style, since the row count/positions are simple
enough to lay out procedurally): a name label via `LabelRenderer.Draw`, then a small
`ResourceBarRenderer.Draw` call at a fixed small size (e.g. 60x8px) to its right. Height grows with
row count (1 for Simple, up to 7 for Human/Goblin); width fixed to fit the longest part name plus
the bar.

## Test plan

- `Tests/Presentation/PlayerHealthBarContentTests.cs` (new, or extend an existing file if one
  already covers `PlayerHealthBarContent`): hover-delay gating (no popup before
  `HudMetrics.HoverTooltipDelayFrames`, shown after), hides immediately on hover loss, Total row
  always present and matches `HealthQueries.TryGetTotals`, one row per body part for a Complex
  player (Human/Goblin-shaped fixture), zero part rows for a Simple-health fixture.
- (Option A) A small `StringUtility`-level check isn't needed -- `BuildPercentageBar` is already
  tested; this only needs to confirm `PlayerHealthBarContent` assembles the right rows/values.
- (Option B) Add coverage for the new popup's row count/sizing matching the entity's actual part
  count, mirroring how `Tests/Presentation/MapWindowTests.cs` already covers `HealthBarElement`-style
  rendering paths.
- Full `dotnet build`/`dotnet test`, matching the existing pre-existing-failure baseline.

## Execution phases

1. Implementation (single phase -- small enough not to split.) Verify: `dotnet build`, `dotnet
   test`, then manual in-game test -- hover the player's health bar, confirm the popup appears after
   a short delay, confirm it lists Total plus every body part with a live-updating bar, confirm it
   disappears immediately on mouse-out, confirm it repositions/doesn't clip off-screen, confirm the
   bars visibly change as you take damage/regen/heal while still hovering.

## Addendum: Total row removed

After landing, the Total row (player's display name + summed fraction) was removed by explicit
follow-up request -- redundant with the big bar the popup is attached to. `PlayerHealthHoverContent`
now takes only `World`/`MultiComponentPool<BodyPartComponent>`/`FontService` (dropped
`PackedComponentPool<SimpleHealthComponent>`/`MultiComponentPool<StatModifierComponent>`/
`DirectComponentPool<DisplayTextComponent>`, none of which any remaining row needs), `MaxRowCount`
is 6, not 7, and `BuildRows` returns zero rows for a Simple-health entity (no Total row left to
fall back to -- not exercised by the player today regardless).

**Also fixed in the same pass, found via this popup actually surfacing per-part numbers for the
first time**: `ComplexHealthHeal.ApplyFractionToAllParts` never applied
`StatModifierTarget.MaximumHealth` when clamping each part's `CurrentHealth`, unlike
`ComplexHealthDamage`/`ComplexHealthRegenSystem`, which both already clamp against each part's own
*effective* (modifier-adjusted) maximum. With the player's permanent +50% `MaximumHealth` buff
(`PlayerBlueprint`), this meant a health potion (or any `DirectHeal`) could only ever heal a part up
to its raw maximum, never the true buffed one -- every part could read "100%" (`CurrentHealth ==
raw MaximumHealth`) while the big bar, built from `HealthQueries.TryGetTotals`'s raw sum times the
modifier applied once, showed ~67% (`250/375`). Fixed by giving `ApplyFractionToAllParts` an
optional `MultiComponentPool<StatModifierComponent>?` parameter and applying the same per-part
`StatModifierMath.GetEffectiveValue` chain the damage/regen paths already use -- see
`Tests/Modules/Health/ComplexHealthHealTests.cs`'s
`ApplyFractionToAllParts_MaximumHealthBuffActive_HealsPastRawMaximumToTheEffectiveOne` for the
regression test.

## Addendum 2: the same bug class, a third location -- passive regen

After the `ComplexHealthHeal` fix above, passive regen showed the identical symptom (parts read
100% individually, aggregate bar stuck below 100%) for a different reason:
`BodyPartSelection.PickLowestPercentage` -- the method `ComplexHealthRegenSystem` uses to pick
which part gets each tick's regen -- computed each part's fraction against its **raw**
`MaximumHealth` and skipped (`continue`) any part whose fraction reached 1.0, treating it as
"already full." With the player's +50% buff active, a part hitting 100% of its *raw* max got
permanently excluded from ever being selected again, even though its true effective cap was still
higher and regen should have kept closing that gap. Fixed by giving `PickLowestPercentage` the same
optional `MultiComponentPool<StatModifierComponent>?` parameter and computing fraction against each
part's modifier-effective maximum (`StatModifierMath.GetEffectiveValue`, the same chain
`ComplexHealthDamage`/`ComplexHealthRegenSystem`'s own clamp already used) -- `PickRandom` needed no
equivalent fix, it has no max-based cutoff at all. See
`Tests/Modules/Health/BodyPartSelectionTests.cs`'s two new `..._ActiveBuff_...` tests.

This is the same bug class as the `ComplexHealthHeal` one, in a third place: any Complex-health code
that reads/compares against `BodyPartComponent.MaximumHealth` directly, rather than going through
the modifier-effective value, will reintroduce this exact symptom. Audited every remaining direct
`part.MaximumHealth`/`.MaximumHealth` read in `Game/Modules/Health/` after this fix -- everything
else already goes through `StatModifierMath.GetEffectiveValue` correctly, except one more: this
popup's own `PlayerHealthHoverContent.BuildRows` computed each row's fraction against the raw max
too (not yet reported, caught proactively by the same audit before it could surface as a fourth bug
report) -- fixed the same way, threading an optional `MultiComponentPool<StatModifierComponent>?`
into `PlayerHealthHoverContent`'s constructor.

## Addendum 3: Admin inspection dump also showed the raw maximum

`SimpleHealthComponent.ToString()`/`BodyPartComponent.ToString()` are parameterless overrides,
called by `Engine.ECS.Components.Stores.MultiComponentPool<T>.CopyInspectionDataForEntity` --
generic Engine-layer code with no access to `entityId` or the `StatModifierComponent` pool (per
CLAUDE.md, Engine has no game-specific knowledge), so neither can ever show the buffed maximum on
its own. The one place this raw text actually reaches a player is
`InspectionWindowContent`'s Admin inspection dump (Detail mode's full component breakdown).

Fixed there instead: a new static, pool-parameterized `ReplaceHealthEntriesWithEffectiveMaximum`
(mirrors `HealthQueries`/`BodyPartSelection`'s own static-helper shape, directly unit-testable) --
called from both `BuildAdminDump` and `RefreshAdminDump` right after `ComponentInspector` populates
the raw list, before the alphabetical sort. It removes the generic `SimpleHealthComponent`/
`BodyPartComponent` entries and re-adds hand-built ones computed against
`StatModifierMath.GetEffectiveValue`, same chain as everywhere else in this arc. Every other
component type's entry (Race, ActionLock, ...) passes through untouched. See
`Tests/Presentation/InspectionWindowContentTests.cs`.

## Decisions (confirmed)

- **Option B** -- pixel-drawn bar via a new popup type, not text-only `Tooltip`.
- Total row is labeled with the player's own display name (`DisplayTextComponent.Name`), not the
  literal word "Total".
- `PopupAnchor.South` as originally proposed (no objection raised).

## Additional grounding for Option B, found before implementation

- **Positioning**: `PopupPositioning.GetPositionWithinBounds(target, popupSize, anchor, gap,
  screenBounds)` (`Presentation/UI/PopupAnchor.cs`) is the exact screen-bounds-aware, self-flipping
  placement math `Tooltip.ShowNear` already uses internally -- reusable directly for a plain
  `Window`, not `Tooltip`-specific.
- **Hover self-poll pattern to mirror**: `AbilityScoreWindow.Update`/`UpdateHover(MouseState)`
  (`Presentation/UI/AbilityScores/AbilityScoreWindow.cs`) -- calls `Mouse.GetState()` directly every
  `Update`, hit-tests candidates, delay-gates showing via `HudMetrics.HoverTooltipDelayFrames`,
  hides immediately with no delay on loss. `PlayerHealthBarContent` is an `IElementContent`, not a
  `Window` subclass, but `Update(GameTime)` is just as available there -- `Mouse.GetState()` is a
  bare FNA/XNA call, no injection needed.
- **Row text rendering**: `LabelRenderer.DrawLeftAligned(spriteBatch, font, text, footprintTopLeft,
  footprintSize, color)` (`Presentation/Rendering/LabelRenderer.cs`) -- despite the class's
  single-glyph-focused doc comment, this overload takes an arbitrary string (`spriteBatch.DrawString`
  under the hood) and is exactly what a body-part name label needs.
- **Fixed size, not dynamic**: the popup's row count is *not* actually variable for this specific
  consumer -- it's always shown for the player, who is always the `Human` race (6 body parts,
  fixed by `Human.BodyParts`) today. No need to build generic dynamic-resize-by-row-count logic;
  size the popup for Total + 6 rows and always draw all 7. (A Simple-health player -- not real
  today -- would need this revisited, but building for a hypothetical that doesn't exist would be
  speculative scope per this repo's own anti-over-engineering convention.)

## Implementation notes

- `IElementContent.Initialize(Window hostWindow)` is a fixed interface signature shared by every
  content class (`PlayerManaBarContent`, `DebugWindowContent`, `InspectionWindowContent`, ...) --
  do NOT widen it. Instead thread `UiLayerStack layers` into `PlayerHealthBarContent` via its own
  constructor (it already takes `World world, ComponentManager componentManager` this way) --
  `DungeonCrawlerWorld/ShellBootstrapper.cs`'s one construction site
  (`new PlayerHealthBarContent(world, ecsContext.ComponentManager)`) already has `layers` in scope
  right next to it. `PlayerHealthBarContent.Initialize(Window hostWindow)` then creates the popup
  `Window` via `hostWindow.ElementPoolService.CreateElement<Window>(...)` (the same
  `ElementPoolService` access every `IElementContent` already has through the host window) and
  adds it via the constructor-injected `layers.Add(UiLayer.Tooltip, popup)` -- mirroring
  `HotbarController`/`InventoryFolderController`'s own `Tooltip`-creation shape, just sourcing
  `ElementPoolService` differently since this is an `IElementContent`, not a controller that
  already takes it directly.
- New `PlayerHealthHoverContent : IElementContent` (name open) is likely unnecessary -- since the
  popup's content never needs to react to window-level events independently, it can just be a
  plain `Window` with its row-drawing logic living directly in `PlayerHealthBarContent` (a second
  private method called from a `DrawContent`-hooked `IElementContent` on that popup window, or --
  simplest -- give the popup window itself a small dedicated `IElementContent` implementation
  that `PlayerHealthBarContent` constructs and drives, e.g. `PlayerHealthHoverContent : IElementContent`
  taking a delegate/reference back to `PlayerHealthBarContent` for the row data. Pick whichever
  reads cleanest once actually writing it -- this is an implementation-detail choice, not a design
  fork worth pausing on.
- `PlayerHealthBarContent.Update` gains the hover self-poll (mirroring `AbilityScoreWindow`'s
  shape): hit-test `_hostWindow`'s absolute content rectangle (the same rect `DrawContent` already
  builds for the main bar itself) against `Mouse.GetState()`, delay-gate via
  `HudMetrics.HoverTooltipDelayFrames`, show/reposition/hide the popup accordingly.
- Popup content, one row per: player's display name (Total, using `HealthQueries.TryGetTotals`'s
  summed fraction) then each `BodyPartComponent` in chain order (name + its own fraction) -- each
  row is `LabelRenderer.DrawLeftAligned` for the name on the left half of the row's width, then
  `ResourceBarRenderer.Draw` for a small bar (propose ~60x8px) on the right half. Values must be
  live -- recompute every frame the popup is visible, not cached from when it was first shown
  (health changes from regen/damage/heals while the player sits there hovering).
- A Simple-health entity (defensive, not exercised by the player today) shows only the Total row --
  the same `HealthQueries.TryGetTotals`-first-then-enumerate-parts pattern `HealthQueries` itself
  and `ComplexHealthDamage` already use elsewhere.
