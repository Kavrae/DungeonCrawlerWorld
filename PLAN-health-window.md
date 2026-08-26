# HealthWindow: per-body-part health/status window, opened from a new HUD button

(Design plan, saved for implementation per this repo's convention -- see `PLAN-body-parts.md`/
`PLAN-human-race.md`/`PLAN-player-health-hover.md`. Item 5 of the current body-parts follow-up
work, per TODO.md's "HealthWindow -- per-body-part health and status display" entry.)

## Context

`PLAN-player-health-hover.md` (item 6) landed a lightweight hover popup on the player's HUD health
bar -- name-only rows, a bar each, no click-to-open, no status effects. TODO.md's own entry
explicitly scopes `HealthWindow` as the heavier counterpart: a real, click-opened window with full
per-part detail (current/max HP text, not just a bar) plus status effects.

## Investigated: what's actually available to build this from

**Opening mechanism.** The only existing "click an icon, open/close a pooled window" pattern is
`InventoryFolderController`'s `WindowSlot<T>` (private to that file, used twice today -- Inventory,
Ability Score) paired with a `Folder` (an expand/collapse icon revealing tiles underneath). The
user's ask is a plain **`Button`** (`Presentation/UI/Button.cs`), not a `Folder` -- `Button` is
text-based (`LeftText`/`TextColor`), but already supports exactly this shape: a single centered
glyph reads as an icon button today for title-bar buttons ("X", "_", "O" -- see its own doc
comment), so `LeftText = "<heart glyph>"`, `TextColor = Color.Red` produces a red heart icon button
with no new Element type needed. Unlike `Folder`, `Button` never expands to reveal sub-tiles -- it
just toggles one thing, which is exactly right here (one button, one window, no second tile the
way Inventory/Stats share a `Folder`).

**Positioning.** `InventoryFolderController.FolderPosition = HudMetrics.Margin +
(0, NotificationCenter.FolderMaximumSize.Y) + FolderGap` -- "above the Inventory Folder" means the
new button occupies roughly that same slot, and `FolderPosition` itself shifts down by the button's
own height + a gap. This is a real (small) change to `InventoryFolderController`'s layout math, not
just an addition next to it.

**Ownership.** `InventoryFolderController`'s own doc comment scopes it explicitly to "the Inventory
Folder and the two windows it can open" -- Health isn't a third tile inside that folder, it's a
sibling trigger. Proposing a new, minimal `HealthWindowController` (mirrors `InventoryFolderController`'s
shape but far smaller: one `Button`, one `WindowSlot<HealthWindow>`), rather than growing
`InventoryFolderController` past its own stated scope. `WindowSlot<T>` itself is private to
`InventoryFolderController.cs` today -- worth extracting into its own file at this point (a third
real consumer), rather than a second copy-paste of the same generic logic.

**Per-part data.** `HealthQueries.TryGetTotals` for the header total; walking
`MultiComponentPool<BodyPartComponent>`'s own chain (`GetFirstDenseIndex`/`GetNextDenseIndex`,
exactly like `PlayerHealthHoverContent`/`InspectionWindowContent` already do) for the per-part
rows, each needing `StatModifierMath.GetEffectiveValue(..., MaximumHealth, part.MaximumHealth)`
for its own effective max (per the last several fixes -- get this right from the start here rather
than needing a fourth bug report).

**Status effects -- the one genuinely open question.** Status effects are entity-scoped only
today, never per-body-part (that's TODO.md's still-unbuilt "Per-body-part vs whole-entity status
effects" item, listed as item 3 in your own ordering, after this one) -- there is no data source
this window could read to know "this specific Leg has Burning on it," only "this entity has
Burning, entity-wide." `PlayerStatusEffectsContent` (the existing HUD status-effect icon row) is
the closest precedent for *reading* active effects (`StatusEffectQueries.GetActiveEffectTypes`/
`CountStacks`), but even it doesn't show a generic remaining-duration number -- Poison/Burning/
Paralysis each have their own separately-shaped timer component
(`PoisonTimerComponent`/`BurningTimerComponent`/`ParalysisTimerComponent`), and
`PlayerStatusEffectsContent` only ever shows a stack count, never a duration, for any of them. A
real "remaining duration" display needs a new type-specific lookup per effect type, the same shape
`PlayerStatusEffectsContent`'s own `GetGlyph`/`GetColor` switches already use for icon/color.

Given status effects aren't part-scoped yet, your spec ("one body part per vertically tiled
section, showing hit points current/total and status effects with remaining duration") reads two
ways:
- **(A)** Each per-part section repeats the *same* entity-wide status effect list underneath its
  own HP line -- technically matches the literal wording, but redundant (six copies of the same
  list) and actively misleading (looks like each part has its own status effects, which isn't
  true yet).
- **(B)** Status effects render once, in their own section (top or bottom of the window, not
  repeated per part) -- accurate to what the data actually represents today, with a TODO to move
  each effect into its owning part's own section once the per-body-part status effect item lands.

**Recommend (B)** -- it's what the underlying data actually supports honestly, and per this repo's
own design principle ("remove ambiguity, remove unexpected actions" -- CLAUDE.md), (A) would show
something that isn't true (implying six independently-afflicted parts). Confirm before I build the
wrong one.

## Design

### `HealthWindowController` (new, `Presentation/UI/`)

Mirrors `InventoryFolderController`'s `WindowSlot<T>` usage shape, minus the `Folder`:
```csharp
public sealed class HealthWindowController(
    ElementPoolService elementPoolService,
    World world,
    ComponentManager componentManager,
    FontService fontService,
    LabelRenderer labelRenderer)
{
    private Button _button = null!;
    private WindowSlot<HealthWindow> _slot = null!;

    public void Initialize(UiLayerStack layers) { /* button creation, click -> _slot.Toggle */ }
}
```
`Button.Clicked` (or whatever `UiInputController` actually wires for a plain child-added `Button`
-- confirm the exact event/wiring path used elsewhere, e.g. `InventoryFolderController.CreateTile`'s
`tile.Clicked += _ => onClick();`) toggles `_slot`.

### `WindowSlot<T>` extraction (`Presentation/UI/WindowSlot.cs`, new file)

Move the existing private generic out of `InventoryFolderController.cs` verbatim (behavior-preserving
refactor), update both existing usages (Inventory, Ability Score) to the new location, add
`HealthWindowController` as the third consumer.

### `HealthWindow` (new, `Presentation/UI/`)

One vertically-tiled section per body part (`ChildElementTileMode.Vertical`, the same tiling
`InspectionWindowContent`'s own subject blocks and Admin dump already use, rather than manual Y
math) -- each section: part name, current/maximum HP as text (not just a bar this time -- the hover
popup already covers "glanceable bar," this window is the detail view TODO.md's own entry calls
for), computed against the effective maximum throughout. A Simple-health entity (not the player
today) shows one section for its single pool instead of per-part -- degrade gracefully, same
pattern `HealthQueries`/every other consumer already follows.

Status effects (pending your answer above): a single section, either above the per-part list (an
entity-level "Status Effects" header) or below it -- each active effect's icon/name plus remaining
duration via new type-specific lookups mirroring `PlayerStatusEffectsContent`'s own
`GetGlyph`/`GetColor` pattern, extended with a `GetRemainingDurationFrames` counterpart reading
each effect type's own timer component.

**TODO note (per your explicit request)**: add a TODO.md entry for position-based body part
display -- today's list is flat/unordered (whatever order `MultiComponentPool`'s chain enumerates
parts in), not laid out by the part's actual anatomical position (Head above Torso above
Legs, Arms to the sides). Ties directly into your upcoming item 1 question ("how to
determine/mark body parts in a specific position") -- once `BodyPartType`/a position concept
exists, revisit this window's layout to actually reflect it instead of a flat list.

## Test plan

- New `HealthWindow`/`HealthWindowController` tests, following `PlayerHealthBarContentTests.cs`'s
  established style (drive the real Update/click pipeline, not shortcut direct-calls -- per this
  session's own "live testing catches what code review misses" lesson): button click opens the
  window, a second click closes it; one section per body part for a Complex fixture, one section
  total for a Simple fixture; each section's current/maximum reflects the effective maximum (a
  MaximumHealth-buff regression test, matching the last three fixes' own pattern); status effects
  section (once its exact shape is confirmed) shows active types with correct remaining durations.
- `Tests/Presentation/InventoryFolderControllerTests.cs` (if one exists -- check) or new coverage
  confirming `FolderPosition` actually shifted down by the new button's height, no visual overlap.
- Full `dotnet build`/`dotnet test`, matching the existing pre-existing-failure baseline.

## Execution phases

1. `WindowSlot<T>` extraction (behavior-preserving, verify existing Inventory/Ability Score windows
   still open/close correctly) + `HealthWindowController`/`HealthWindow` skeleton (button appears,
   opens an empty window) -- verify positioning/click behavior before adding real content.
2. Real content: per-part sections, status effects section, TODO.md note. Verify: `dotnet build`,
   `dotnet test`, then manual in-game test -- click the heart button, confirm one row per body part
   with live current/max numbers (not just a bar), confirm it doesn't clip/overlap the Inventory
   folder now sitting below it, confirm status effects show with correct remaining durations (apply
   Poison/Burning/a StatModifier buff and watch the numbers tick down), confirm closing/reopening
   behaves the same as Inventory/Ability Score's own windows.

## Addendum 4: restyled to match ItemDetailsWindow, bars instead of numbers

By explicit follow-up request, `HealthWindow` now borrows its color/style directly from
`ItemDetailsWindow` -- `WindowPalette.PanelBackgroundColor` background, `Color.White` body text,
`WindowPalette.TitleColor`-labeled `TextDivider` headers (95%-width, 12.5%-label-position, the
exact shape `ItemDetailsWindow.BuildDivider`'s own Effects/Activation headers use) for every
section, including "Status Effects" (originally a bold text row) and now each individual body
part's own name (originally folded into a single "Name: current/max" text line). Poison's status
row color brightened from `PlayerStatusEffectsContent`'s own `DarkGreen` to `LightGreen` -- fine
against that content's white icon tile, unreadable against this window's own dark background.

Each body part's HP is now a resource bar, not text -- a new, small, entity-agnostic
`FractionBarElement` (`Presentation/UI/FractionBarElement.cs`), the counterpart to
`HealthBarElement` for a caller that already has a fraction in hand rather than an entityId to
resolve one from. **Caught via testing, would have crashed the real game**: `FractionBarElement`
needed registering in `DungeonCrawlerWorld/ElementFactoryRegistry.cs` (the production
`ElementPoolService`'s factory list) -- the test suite's own `TestElementPoolServiceFactory` has a
separate, smaller registered-type list, and the first pass only updated the test one, so
`dotnet test` passed while the real game would have thrown `"No factory registered for parent type
FractionBarElement"` the moment a player with any body parts opened the window (i.e. always, for
the current Human-race player).

## Decisions (confirmed)

- Status effects: one section, not repeated per body part (option B).
- Placement: above the per-part list.

## Additional grounding: per-effect-type remaining duration has no uniform field

Investigated each status effect type's actual timer component before implementation, since none of
them expose duration the same way:

- **Poison** (`PoisonTimerComponent`): has a real `RemainingDurationTicks` (in "ticks," not frames)
  plus `FramesUntilNextTick` (time to the *next* tick within the current one). Total remaining
  frames = `FramesUntilNextTick + (RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames`
  (confirm `PoisonEffects.TickIntervalFrames`'s exact name/value in the actual source first).
- **Burning** (`BurningTimerComponent`): no duration field at all -- only `FramesUntilNextTick` and
  `StackCount` (each tick removes exactly one stack, see the component's own doc comment). Total
  remaining frames = `FramesUntilNextTick + (StackCount - 1) * BurningEffects.TickIntervalFrames`.
- **Paralysis** (`ParalysisTimerComponent`): `FramesUntilNextTick` *is* the remaining duration
  directly -- a single-fire countdown to expiry, per the component's own doc comment ("unlike
  Burning/Poison there's no repeating action to fire partway through").

No shared interface/field covers this uniformly -- `ITickCountdown`/`IStatusEffectStackCount` only
standardize `FramesUntilNextTick`/`StackCount`, not "total remaining." A new per-type
`GetRemainingDurationFrames(StatusEffectType) -> ushort?` lookup (querying the right timer pool per
type, `null` if the type isn't currently active) is the only honest way to build this, mirroring
`PlayerStatusEffectsContent`'s own `GetGlyph`/`GetColor` per-type switches.
