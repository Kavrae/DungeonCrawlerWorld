# Corpse Inventory / Looting

(Landed. Saved here per this repo's design-doc convention -- see `PLAN-action-effect-activator.md`/
`PLAN-inventory-item-filtering-and-tab-stats.md` for the same shape used pre-implementation; this
one documents what actually shipped, including several fixes that only surfaced via live testing.)

## Context

Death already left a corpse behind as a real, fully-populated entity (`DeathSystem` never destroys
it) specifically so a future looting mechanic could read its `InventoryItemStackComponent` stacks
-- see `TODO.md`'s old "Corpse looting" entry, which sketched exactly this shape in advance. This
plan makes it real: clicking an adjacent corpse opens the player's inventory and a new corpse
inventory window side by side, items drag freely between any two entities' grids in either
direction, non-player inventories are capped at 20 distinct stacks, and the map tile shows a
loot-bag indicator reflecting whether a corpse has unlooted items.

No real loot table exists, so NPCs get a **temporary** random starting inventory so corpses have
something to loot until one lands. Stack splitting/merging is explicitly out of scope: a
transferred stack keeps its own identity rather than merging into a matching one on the
destination -- duplicate stacks of the same item on one entity are accepted for now.

## Design

### Data model (`Game/Modules/Death/`, `Game/Modules/Inventory/`)

- **`DeadComponent`** gained a `long DiedAtFrame` field, stamped from `EngineTime.FrameCount` in
  `DeathSystem.Update` (stashed in a field before the buffered dispatch that synchronously invokes
  `OnEntityDied`, since that handler has no `EngineTime` of its own).
- **`CorpseLootedComponent`** (new marker, `PackedComponentPool`) -- set the moment a corpse's loot
  window opens, regardless of whether anything is taken. Drives the loot-bag badge's grey tint.
- **`InventoryCapacity`** (new, `Game/Modules/Inventory/`) -- `MaxNonPlayerStackCount = 20` and
  `HasRoomForNewStack`/`HasRoomForNewStacks`, unlimited for the player (`entityId ==
  playerQuery?.PlayerEntityId`). Wired into the transfer methods below only -- nothing else grants
  a non-player entity a *new* distinct stack yet (no loot table, no mob pickup); both future
  features (see `TODO.md`) should reuse this helper rather than duplicating the check.
- **`InventoryActions.TryTransferStack`/`TryTransferAllStacksOfItem`** -- move one stack (or every
  stack sharing an item id, for a dragged Merged Stack badge) from one entity to another, preserving
  exact identity (`StackInstanceId`/`Override`/`IsDisabled`/`IsDivergent`), never merging into an
  existing stack on the destination. Same-entity no-op guard. Capacity-checked against non-player
  destinations; the batch variant checks room for the whole group up front and is all-or-nothing.

### Temporary random NPC starting loot (`Game/Blueprints/NPCs/TemporaryNpcLootGrant.cs`)

Generalizes a precedent already in `Goblin.cs` (a random 0-2 Health Potion grant via
`InventoryActions.AddItem`, using the `MathUtility` every race blueprint already takes by
constructor) into a shared helper: rolls 0-20 stacks, each a random item from the 9 registered
`CoreItemsModule` definitions (via each item's own pure `Build()` factory, no `ItemCatalog`
injection needed) at a random quantity up to that item's `MaxStackSize`. Wired into
`Goblin`/`Fairy`/`Ghost.Build`, replacing Goblin's original ad-hoc block. `Ghost` has no
`HealthComponent` (a melee-status-effect test fixture) so it can never actually die and reach a
corpse-loot window -- granted loot on it is harmless but inert until Ghosts can die some other way.

### Map rendering -- the loot-bag badge (`Presentation/UI/MapWindow.cs`)

`DrawCorpses` draws `LootBag-Red` (already defined in `Content/SpriteManifest.json`, previously
unused) after the corpse's own grey-tinted sprite, anchored to the top-right corner of the corpse's
*full footprint* (not just its origin tile -- a multi-tile Huge corpse's badge sits on its actual
top-right tile). Tinted white while unlooted, grey once `CorpseLootedComponent` is set -- an
earlier before/after-draw-order version (badge drawn *underneath* the corpse to simulate "under the
grey filter") was too easily fully hidden by the corpse's own opaque sprite instead of reading as
dimmed; an explicit tint replaced it.

### Click-to-loot and adjacency (`Presentation/UI/MapWindow.cs`, `ShellBootstrapper.cs`)

`MapWindow.OnContentClickAction`'s fallback branch (after the armed-ability/item check) resolves
the clicked tile's occupants; if any carries `DeadComponent`, the click is consumed either way, but
`OnCorpseClicked` (a settable `Action<int>?`, wired by `ShellBootstrapper` once
`SecondaryInventoryWindowController` exists) only fires if the player is adjacent --
`GridDistance.ChebyshevDistance(corpseTransform.Position, playerTransform.Position) <= 1`, 8-
directional and inclusive of standing on the corpse's own tile (unlike melee's ring, which excludes
the caster's own tile -- looting your own tile is expected, punching yourself isn't).

### Corpse Inventory Window (`Presentation/UI/Looting/`, new folder)

- **`SecondaryInventoryWindowController`** -- deliberately *not* folded into
  `InventoryFolderController` (itself slated for a three-way split -- see `TODO.md`): opens a
  second inventory-grid window next to the player's own `InventoryManagementWindow`, targeting some
  other entity, one at a time (opening a different corpse replaces the current one; opening the
  same one again closes it, the codebase's usual re-press-to-confirm convention). Reuses the
  player's *existing* window via two small accessors added to `InventoryFolderController`
  (`OpenInventoryWindow()`/`PlayerInventoryWindow`). Positions the corpse window from the player
  window's *live* `RelativePosition`/`CurrentSize` (both are user-movable/resizable), not a
  hardcoded offset. Written generically enough that a future chest/shop reuses this same controller
  instead of growing its own.
- **`CorpseInventoryWindow`** -- a fixed summary (`EntityIconElement`, new: sprite-or-glyph
  identity portrait; name/killer/died-tick as plain `TextWindow` lines, white text) above a plain,
  non-tabbed, fixed-5-column item grid hosting `InventoryGridContent` (already fully generic on
  `entityId`/`filterTag`, no `GridControl`/`TabbedContent` needed for a 20-item-max grid). Grid
  background matches the player's own (`WindowPalette.PanelContentColor`); individual cell
  backgrounds stay transparent.

  **Sizing** was the trickiest part: the window computes its exact final size *once*, from the
  corpse's raw stack count, *before* building any children at all -- never a build-then-shrink-to-
  fit pass. An earlier version built the grid first, then resized it (and the outer window) to fit
  the actual cell count; resizing a window that already has real `InventoryGridContent` cells
  re-fires that content's own `Resized`-driven rebuild *reentrantly, mid-Measure* -- confirmed by
  live testing to be what broke dragging items back out of a corpse's grid (cells rebuilt during
  that reentrant call stopped hit-testing correctly). Sizing up front instead means the grid is
  built exactly once, at its final size. The grid is always at least a 2x5 minimum regardless of
  starting item count, so items dragged in later don't force scrolling on a window sized too small.
  `Element.SetMinimumSize` (new) pins `MinimumSize` to that same starting size after construction --
  it can only otherwise be set once, from `ElementOptions.Layout.MinimumSize` at `Build()` time,
  which doesn't work for a window computing its own natural size at runtime -- so the window can
  never be user-resized smaller than what it opened at.

### Drag-and-drop transfer (`Presentation/Input/UiInputController.cs`, grid/cell plumbing)

- `InventoryItemStackCell`/`InventoryGridContent` expose the owning entity's id.
- `UiInputController` captures the drag's origin entity on press; on release, walks up from the
  drop position's hit element through `ParentElement` looking for a `Window` whose **`Tag`** (not
  `Content`) is an `InventoryGridContent`. Calls `TryTransferStack`/`TryTransferAllStacksOfItem` if
  the destination differs from the origin.

  **Why `Tag`, not `Content`**, is the second thing confirmed only by live testing against the
  *real* `InventoryManagementWindow` structure (a hand-built test harness using a bare
  `Window.SetContent(InventoryGridContent)` passed even though it didn't match production): the
  player's real grid is hosted by `InventoryTabContent`, which drives its `InventoryGridContent`
  *manually* -- calls `Initialize`/`Update` on it directly -- and never calls `Window.SetContent` on
  the grid's own host window at all. `Window.Content`-based matching silently found nothing there,
  so corpse-to-player transfers failed outright while player-to-corpse worked (the corpse's own
  simpler window happens to use `SetContent`). `Element.Tag` (new -- the same role WPF/WinForms'
  `Tag` plays) is set by `InventoryGridContent.Initialize` on its host window regardless of which of
  the two hosting patterns built it, so both are found correctly. Reset to null on `Element.Build`
  for pooled reuse, the same discipline `_isFocused`/`_isGlowing` already follow.

  An item dragged from a non-player entity's own inventory never binds to, or even highlights, the
  hotbar (`IsDragFromNonPlayerInventory`, permissive/false whenever `IPlayerQuery` isn't wired at
  all, e.g. a test harness) -- a hotbar slot's whole premise is referencing one of the player's own
  stacks. `DragGhostContent`'s own stack lookup was hardcoded to the player's entity id, so a
  corpse-originated drag never resolved to a sprite for the ghost at all; `UiInputController`'s
  origin entity is now threaded through `DragGhostState` and used as the lookup entity (falling
  back to the player for a hotbar-origin drag, which is always the player's own item/action either
  way).

### `InventoryManagementWindow`'s tab-rebuild gating (`Presentation/UI/Inventory/`)

A fourth live-testing find, once dragging worked end to end: `InventoryManagementWindow.Update`
rebuilt its *entire* tab list (`TabbedContent.SetTabs`) on every inventory version bump --
`InventoryTagQueries.GetTagCounts` sorts by count descending, so even a same-tag quantity change
(no tag gained or lost) could look like "something changed." `SetTabs` recreates every tab's
`InventoryTabContent`/`InventoryGridContent`/`GridControl` from scratch even when it preserves the
active tab's *selection* by label, discarding that tab's sort order/hide-disabled/search state for
no reason on every single drag. Fixed by tracking the *set* of tags represented and only calling
`SetTabs` when it actually changes; each tab's own `InventoryGridContent` already refreshes its
displayed stacks independently via its own version watcher regardless of whether `SetTabs` runs, so
grid contents stay correct either way -- only the unnecessary tab-list rebuild is skipped.

## Verification

Unit tests: `Tests/Modules/Inventory/InventoryActionsTests.cs` (transfer same-entity guard,
identity preservation, no-merge, capacity refusal, player exemption), `Tests/Modules/Death/
DeathSystemTests.cs` (`DiedAtFrame`), `Tests/Presentation/UiInputControllerTests.cs` (grid-to-grid
transfer against both a simplified harness and the *real* `InventoryManagementWindow` structure --
the second is what actually caught the `Tag`-vs-`Content` bug, hotbar-bind/highlight restriction),
`Tests/Presentation/InventoryManagementWindowTests.cs` (tab/toggle survival across a same-tag-set
update, still-rebuilds on a genuine new tag). All confirmed working end-to-end in-game across
several rounds of manual testing.
