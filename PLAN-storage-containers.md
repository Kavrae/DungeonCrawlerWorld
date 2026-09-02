# Shops and Storage Containers -- Phase 1: Treasure Chest + Currency

(Landed. Saved here per this repo's design-doc convention -- see `PLAN-corpse-inventory-looting.md`
for the same shape used pre-implementation; this one documents what actually shipped, including
several follow-up fixes that only surfaced via live testing.)

## Context

`TODO.md`'s old "Shops and storage containers" item called for reusing `Game/Modules/Inventory/`
storage on a non-creature entity. This plan landed the storage-container half concretely (a
`TreasureChest` blueprint, lootable independent of death) plus a new `Currency` component
(Gold/Credits) that Player/Goblin/Fairy start with -- the resource shops will eventually spend.
Shops themselves (trade UI, pricing) were deliberately kept out of scope and got their own new
high-priority `TODO.md` item (Shops), alongside a second (Loot currency) for making a container's
own Gold actually takeable.

## Design

### Currency (`Game/Modules/Currency/`)

- **`CurrencyComponent`** (`struct`, packed pool, overwrite merge) -- plain `Gold`/`Credits` ints.
  `ToString()` follows `DeadComponent`/`ManaComponent`'s own "Label : Value" per-line convention
  (`"Gold : {Gold}\nCredits : {Credits}"`) so `ComponentInspector`'s admin dump shows real values
  instead of falling back to `ValueType`'s bare-type-name `ToString()` (a plain struct, not a
  record struct, gets no field-value `ToString()` for free -- confirmed missing via live testing).
- **`StartingCurrencyGrant.GrantRandomStartingGold`** (`Game/Blueprints/`) -- 1-10 Gold, 0 Credits,
  mirroring `TemporaryNpcLootGrant`'s injected-`MathUtility` shape. Called from
  `PlayerBlueprint`/`Goblin`/`Fairy.Build`. `TreasureChest` grants its own smaller 0-5 Gold range
  inline instead (found loot, not a personal purse -- not worth a second shared helper for one
  consumer with a different range).

### Containers (`Game/Modules/Containers/`)

- **`ContainerComponent`** (empty marker, packed pool) -- marks an entity as always-lootable (see
  MapWindow below) and subject to `ContainerDestructionSystem`'s destroy-time handling.
- **`ContainerDestructionSystem`** -- subscribes to `EntityDiedEvent` independently of `DeathSystem`
  (both react to the same buffered event; `DeathSystem` is the one dedicated system that actually
  calls `DispatchBuffered<EntityDiedEvent>()` each frame, so this system's own `Update` has nothing
  to do -- an earlier version redundantly called `DispatchBuffered` a second time, caught and
  removed). On an entity carrying `ContainerComponent`: clears every `InventoryItemStackComponent`
  stack (`MultiComponentPool.Remove(entityId)`) and overwrites `DisplayTextComponent` to
  ("Destroyed", a short remains description) via `DirectComponentPool.TryUpdate`. A creature's
  corpse keeps its name/inventory intact; a destroyed container does not.
- **`ContainersModule`** (`Dependencies = [InventoryModule]`) registers both, added to
  `GameBootstrapper`'s built-in module list.

### `TreasureChest` (`Game/Blueprints/Objects/TreasureChest.cs`)

A `Wall`/`Lava`-style stationary prop (`DisplayText`/`Glyph`/`Sprite`/`Transform`, no creature
identity, no `NonBlockingComponent` -- physically blocks like `Wall`). Golden "T" glyph, reuses the
`"Inventory"` sprite key (there is no separate `"InventoryFolder"` key). 100 starting health --
high enough that a stray AOE hit won't randomly destroy one. Immune to Poison and Paralysis
(`StatusEffectImmunityComponent`, permanent, the same mechanism every status effect's own
`ApplyStack` already checks) but not Burning, so fire still destroys it. Starts with 1-10 random
items (stack sizes 1-5, drawn from a `CoreItemsModule`-definition loot table built the same way
`TemporaryNpcLootGrant.AllCoreItems` is) and 0-5 Gold. Uses the existing global
`InventoryCapacity.MaxNonPlayerStackCount` (20) rather than a bespoke per-container cap -- per
explicit direction, simpler than threading a new per-entity-type capacity concept through
`InventoryCapacity` for one consumer.

`TestMapBuilder` spawns several scattered near the player's spawn point, mirroring the existing
`BuildTinyGoblins` fixed-position-loop shape.

### Lootable-while-alive (`Presentation/UI/MapWindow.cs`)

The "Loot" context-menu option (`AddEntityGroup`) was gated purely on `_deadPool?.Has(entityId)`.
Widened to `_deadPool?.Has(entityId) == true || _containerPool?.Has(entityId) == true` -- the one
change needed, since everything downstream (`SecondaryInventoryWindowController`,
`SecondaryInventoryWindow`, drag/transfer) already worked off a plain `entityId` with no
`DeadComponent` dependency. The grey-tint/loot-bag-badge map visuals stay corpse-only by design (no
map badge for an intact chest, discoverable via right-click instead) -- flagged as a possible future
visual-affordance gap, not fixed here.

### Renames (generalizing past "corpse")

- **`CorpseLootedComponent` -> `LootedComponent`** (`Game/Modules/Death/Components/`) -- applies to
  any lootable entity's "has this been opened at least once" marker now, not just a corpse.
- **`CorpseInventoryWindow` -> `SecondaryInventoryWindow`** (`Presentation/UI/Looting/`) -- matches
  its existing opener, `SecondaryInventoryWindowController`, rather than inventing a parallel name;
  reads better once a shop reuses the same window later. No behavior change, every reference/doc
  comment across `MapWindow`/`InventoryGridContent`/`ItemDetailsWindow`/`UiInputController`/
  `ElementFactoryRegistry`/tests updated alongside.

### Currency UI row (`Presentation/UI/CurrencyRow.cs`)

Shared "Gold : X    Credits : Y" `TextWindow`-row builder, reading an *optional*
`PackedComponentPool<CurrencyComponent>` (degrades to `0/0` rather than throwing when
`CurrencyModule` isn't registered, e.g. in an older hand-built test `ComponentManager`).

- **`InventoryManagementWindow`** previously handed its entire `ContentSize` to `TabbedContent` via
  `SetContent` directly on itself. To make room for a fixed-height Currency row at the bottom,
  `TabbedContent` is now hosted in a new inner `Window` sized `ContentSize - (0, CurrencyRow.Height)`
  instead, built (along with the row) in a new `OnChildrenInitialized` override rather than
  `Configure` (real `ContentSize` isn't available until after `MeasureAndArrange` -- same constraint
  `SecondaryInventoryWindow`/`AbilityScoreWindow` already document). Since the window is
  user-resizable, a `Resized` handler keeps both the inner window and the row in sync -- no manual
  unsubscribe needed (`ElementPoolService.CloseElement` clears every event on close, the same
  discipline `TabbedContent`'s own host-window subscription already relies on). Shows the
  **player's own** Gold/Credits. This extra nested window is flagged in `TODO.md`'s new Element
  footer entry as a cleanup candidate once a real generic footer primitive exists.
- **`SecondaryInventoryWindow`** was simpler -- it already hand-composes its children once, up
  front, in `OnChildrenInitialized`, sizing itself exactly once from the target entity's raw stack
  count. Folded `CurrencyRow.Height` into that one-time size computation and added the row below the
  grid, positioned once and never repositioned on resize (consistent with the summary lines/grid
  above it, which don't reposition either). Shows the **looted entity's own** Gold/Credits -- reads
  `0/0` for a container today (containers don't hold Currency reachable by looting yet, see
  `TODO.md`'s Loot currency entry), real values for an actual corpse.

## Live-testing find (unrelated bug, fixed alongside)

`TextWindow.Build` reset `OriginalText`/`TextColor`/`Bold` on every pooled reuse (not just first
construction) but never `ContentFont` -- `ElementOptions`/`TextOptions` carries no font-size field,
so every consumer wanting a non-default size sets `ContentFont` imperatively after `CreateElement`
(e.g. `HealthWindow`'s bigger buff/debuff font, `ItemDetailsWindow`'s doubled name font). Without a
reset, a size set by one consumer leaked into whichever window's `TextWindow` the pool handed out
next -- confirmed via `InspectionWindowContent`'s admin-dump rows rendering at a stale size. Fixed
generically at the pool-reset choke point (`TextWindow.Build`), not per call site, matching this
codebase's established fix pattern for the same class of bug (`ElementPoolService.CloseElement`'s
own generic event-clearing).

## Verification

Unit tests: `Tests/Blueprints/BlueprintTests.cs` (`TreasureChest_Build_...`, Player/Goblin/Fairy
starting-Gold range assertions), `Tests/Modules/Containers/ContainerDestructionSystemTests.cs`
(inventory-clear + rename-to-"Destroyed" on `EntityDiedEvent`), existing
`Tests/Presentation/InventoryManagementWindowTests.cs` suite (unaffected by the inner-window
restructure -- its tree-search helpers don't assume a fixed nesting depth). Full suite green
(1369/1370 -- the one failure is `AbilityScorePerformanceTests`' unrelated, pre-existing
hardware-timing-sensitive flake). Manual in-game verification: chest spawn/loot-while-alive/
immunity/burn/destroy-and-rename, and the Currency row in both the player's Inventory window and
the loot window, confirmed working by the user across several rounds of testing.
