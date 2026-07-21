# Long-Term TODOs

Non-urgent architectural items worth revisiting later -- things noticed in passing that don't block current work, not a sprint backlog.

## Occupancy rendering/selection scans assume a small Tiny/Phasing population

`MapWindow.BuildOccupantsByPosition` (rebuilt fresh every single frame) and `SelectionWindowContent.RecomputeSelectedEntityIds` (also every frame, via `Update`) both find Tiny/Phasing entities by doing a full linear scan of the `OccupancyComponent` pool and reading each one's `TransformComponent.Position` -- there's no position-keyed index for them, because `Map`'s own occupancy array deliberately never records non-Blocking entities (see `World.IsBlocking`).

That design assumed ghosts/insects are always a small population relative to the map, cheap enough to rescan wholesale every frame. A randomly generated level that happens to be populated primarily by Tiny/Phasing entity types breaks that assumption -- at that point these become O(total occupant count) *per frame* scans instead of the intended "small enough not to matter" cost, on both the render path and the (also per-frame) selection-inspector path.

When this becomes a real bottleneck: replace the per-frame full-pool rescan with an actual position-keyed index for non-Blocking entities, kept incrementally in sync with placement/movement/removal the same way `Map`'s own creature-occupancy array is -- or at minimum, only rebuild `MapWindow`'s dictionary when the Occupancy pool or relevant transforms have actually changed since the last frame, rather than unconditionally every frame.

Affected: `Presentation/UI/MapWindow.cs` (`BuildOccupantsByPosition`), `Presentation/UI/Content/SelectionWindowContent.cs` (`RecomputeSelectedEntityIds`).

## Burning status effect from touching lava

- Damage over time
- Damage decreases over time
- Goes away when damage hits 0
- Can stack to increase damage and duration (so multiplicatively worse)
- Gets worse for each movement the entity ends in lava

## Window Chrome

- Drag header to move
- Drag borders to resize
- Fix header button order
- 3D borders with inset and outset modes
- Mouse down changes a button to inset, mouse up changes it back to outset

## Body parts

- Plan first
- Use multi-components somehow
- Position matters -- e.g. lava should damage feet first

## Movement System

- `SeekTarget` movement mode
- Efficiency update

## TextWindow

- Way to copy text to clipboard

## Window

- Clean up resize/move/etc. calculations
- Clean up properties via better composition
- Border overhaul

## Generic status-effect system needed once buff/debuff variety grows

The occupancy markers (`NonBlockingComponent`/`ForceBlockingComponent`) are `MultiComponentPool`-backed "many independent sources, count-based" components -- a reasonable, low-risk special case for exactly two boolean occupancy questions. Do **not** copy that pattern (a bespoke marker-component type plus a hand-written precedence function) for every future buff/debuff once real effect variety shows up (dozens of effects on the player character, overlapping sources assumed throughout) -- it doesn't scale:

- Most real buffs carry data (magnitude, remaining duration, source), not just presence -- Burning already needs this (see its own TODO item above: damage decreases over time, stacks increase both).
- Precedence/interaction relationships between many effect types multiply, not add, and become impossible to audit as N separate "if pool A has, else if pool B has" checks hand-written and scattered across whichever system happens to care about each one.

When that need arrives: build a generic "active effect" record (`EffectType` id/enum, `SourceEntityId`, `RemainingDuration`, `Magnitude`), still `MultiComponentPool`-backed since overlapping sources/stacking is the same requirement either way, with a small number of *derived-question* functions (`IsBlocking`, `IsStunned`, `GetSpeedMultiplier`, etc.) each independently scanning the relevant subset of active effects and applying its own precedence rule -- structurally the same shape `IsBlocking` already is, just generalized from two dedicated pools to filtering one shared effects table.
