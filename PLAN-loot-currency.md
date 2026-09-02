# Loot Currency

(Landed. Saved here per this repo's design-doc convention -- see `PLAN-corpse-inventory-looting.md`
for the same shape used pre-implementation; this one documents what actually shipped, including a
couple of fixes that only surfaced via live testing.)

## Context

`PLAN-storage-containers.md` landed `CurrencyComponent` (Gold/Credits) and a read-only "Gold : X
Credits : Y" text row (`CurrencyRow`), but nothing could actually move a balance between entities.
This plan makes Currency transferable the same way item stacks already are -- hover, drag-and-drop,
and a right-click Give/Give All/Take/Take All menu -- deliberately mirroring the existing
`InventoryItemStackCell`/`InventoryGridContent` UX as closely as possible rather than inventing a
parallel mechanism.

## Design

### `CurrencyActions` (`Game/Modules/Currency/CurrencyActions.cs`)

`TryTransfer(componentManager, sourceEntityId, destinationEntityId, CurrencyType type)` and
`TryTransferAll`, mirroring `InventoryActions.TryTransferStack`'s shape (same-entity guard first,
no-op if nothing to move). `CurrencyType` (`Gold`/`Credits`, `Game/Modules/Currency/CurrencyType.cs`)
is an explicit enum, not an `isGold` bool -- a bool hard-limits to exactly two currencies; a future
third one is just a new enum case, and `TryTransferAll` already iterates `Enum.GetValues<CurrencyType>()`
rather than naming Gold/Credits individually, so it picks up a new currency automatically. Always
moves the source's *entire* current balance of that currency -- no partial amounts, see `TODO.md`'s
Context menu amount picker entry for the deferred follow-up. Reads then writes the *whole*
`CurrencyComponent` on each side (never a partial-field `Merge`) since `CurrencyModule`'s registered
merge policy is a full overwrite (`existing = incoming`) -- merging just a delta would silently zero
the untouched currency field. No capacity check: `InventoryCapacity` is purely a distinct-stack-count
concept, meaningless for a single packed Gold/Credits pair.

### Starting Credits (`Game/Blueprints/StartingCurrencyGrant.cs`, `TreasureChest.cs`)

`StartingCurrencyGrant` gained `GrantRandomStartingGoldAndCredits` (1-10 Gold, 0-1 Credits -- picked
to match `CurrencyComponent`'s own "extremely rare" framing for Credits) alongside the existing
Gold-only `GrantRandomStartingGold`. One `Merge` call for both fields together, never two sequential
grants on the same entity -- the overwrite merge policy would let a second grant silently zero what
the first just set. Goblin/Fairy switched to the new combined grant; `PlayerBlueprint` deliberately
kept the Gold-only one (per the task: Credits go to "all fairies, goblins, and treasure chests," not
Player). `TreasureChest` grants its own 0-5 Gold / 0-1 Credits inline (its own const range, distinct
from `StartingCurrencyGrant`'s).

### `CurrencyElement` + `CurrencyRowContent` (`Presentation/UI/Content/`)

The old static `CurrencyRow` (one read-only `TextWindow`) is gone, replaced by two classes:

- **`CurrencyElement`** -- one currency's own "{Label} : {n} [sprite]" unit, a plain `Element`
  (not `Window`, same reasoning `InventoryItemStackCell`/`Folder`/`Button` use). Mirrors
  `InventoryItemStackCell`'s shape: `EntityId`/`IsHovered` public settables, base-fill +
  hover-overlay draw via `GridSquareRenderer` (the same "highlights like a hovered item" treatment),
  `OnRightClicked` inherited free from `Element`. Text drawn first, then the sprite (`"Currency-Gold"`/
  `"Currency-Credit"`, already in `Content/SpriteManifest.json`) `IconGap` (4px) past the text's own
  measured width -- not pinned to the element's far edge, which read as a large, ugly gap on first
  pass (live-testing fix). No `IsSelected`/click-to-inspect -- hover + drag + right-click only,
  matching exactly what was asked.
- **`CurrencyRowContent`** -- owns one `CurrencyElement` per currency (left/right halves of the
  row), hover polling (`Mouse.GetState()`, self-polled every `Update`, same idiom
  `InventoryGridContent.UpdateHover` uses), amount refresh (`RefreshAmounts`, unconditional re-read
  every `Update` -- Currency has no version watcher), and the context menu. Implements
  `IInventoryDropTarget` (see below) by setting the *parent* window's own `Tag` (both elements are
  its direct children, same convention `CurrencyRow.Build` used for its single `TextWindow`).

Wired into `InventoryManagementWindow` (shows the player's own Gold/Credits, resize-handled via
`Reposition`) and `SecondaryInventoryWindow` (shows the looted entity's -- positioned once at open
time, never repositioned, consistent with its summary/grid).

### Context menu (`CurrencyRowContent.BuildCurrencyContextMenu`)

Mirrors `InventoryGridContent.BuildItemContextMenu`'s exact Give/Take decision logic
(`getSecondaryTargetEntityId()` open + clicked element's own entity is the player vs. the secondary
target). "Give"/"Take" move only the right-clicked element's own currency
(`CurrencyActions.TryTransfer(..., element.Type)`); "Give All"/"Take All" move both via
`CurrencyActions.TryTransferAll`, regardless of which element was clicked.

### Drag-and-drop (`Presentation/Input/UiInputController.cs`, `DragGhostContent.cs`)

- **`IInventoryDropTarget`** (`Presentation/UI/Content/`, `{ int EntityId { get; } }`) --
  implemented by both `InventoryGridContent` and `CurrencyRowContent`. `UiInputController`'s
  drop-target walk (renamed `FindHostingGrid` -> `FindDropTargetEntityId`) now matches `Window {
  Tag: IInventoryDropTarget target }` and returns `target.EntityId` instead of the concrete grid --
  the one change that makes both "for consistency" requirements true at once: an item stack dropped
  on a currency row, and a currency element dropped on a grid, both resolve to the right destination
  entity and transfer correctly.
- **Drag capture**: `_contentDragCurrencyType` (`CurrencyType?`, mirrors
  `_contentDragItemStackInstanceId`'s shape exactly) is set in `TryStartContentDrag` when the press
  hit a `CurrencyElement`, and folded into every existing gate that previously only checked the
  item/action fields (`ContentDragGhostVisible`, the held-frames increment, `ResolveContentDrag`'s
  early-exit guard, the `finally` cleanup). Currency drags never bind to a hotbar slot -- there's no
  hotbar concept for it at all -- handled the same way a Merged Stack drag already refuses to bind:
  an early `return` in `ResolveContentDrag` right after the drop-target-resolution branch, and
  folded into `IsContentDragBlockedAt`'s gate so hovering a hotbar slot mid-drag shows the blocked
  cursor.
- **Ghost**: `DragGhostState` gained `CurrencyType`; `DragGhostContent.DrawContent` resolves the
  initially read `_contentDragSourceSize` from `CurrencyElement.CurrentSize` -- the whole "Gold : 10
  [sprite]" element bounds, much wider than tall (unlike an item cell, which *is* square) -- and
  rendered visibly stretched horizontally. Fixed by adding `CurrencyElement.IconSize` (just the
  square icon region) and reading that instead, mirroring how an item cell's own `CurrentSize`
  happens to already be square.

## Verification

Unit tests: `Tests/Modules/Currency/CurrencyActionsTests.cs` (same-entity/zero-balance no-ops, a
transfer zeroes only the moved currency and adds to -- not overwrites -- the destination's existing
balance of both fields, `TryTransferAll` moves both). Updated `Tests/Blueprints/BlueprintTests.cs`
for the new Credits ranges. Fixed two test harnesses that hand-build the real
`InventoryManagementWindow` structure (`Tests/Presentation/InventoryManagementWindowTests.cs`,
`Tests/Presentation/UiInputControllerTests.cs`) to register the new `CurrencyElement` factory. Full
suite green throughout (the one intermittent failure across every run in this session was
`AbilityScorePerformanceTests`' pre-existing, unrelated hardware-timing-sensitive flake). Manual
in-game verification at the end of each phase (static rendering/hover/context-menu, then
drag-and-drop in both directions against both destination types, then the two live-testing fixes
above) confirmed working by the user.
