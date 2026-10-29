# Pointer input: one hit test, one gesture, events to the hit

(From TODO.md "Compare input hit-testing against a unified picking pipeline". Researched 2026-10-27. A design review
first, then phases; nothing here is decided until the questions at the end are answered.)

## Verdict in one paragraph

The element hit test is **already** a single pipeline: `UiInputController.TryHitTestInteraction` runs at most once a
frame (cached on position and `UiLayerStack.Version`), respects layers and menu mode, and every `IUiInputHandler` and
`IContentDropHandler` is handed its result instead of scanning. Recent work built that. What keeps producing hit-test,
popup and tap-versus-drag bugs is mostly **code that goes around it**, not the lack of a Bevy-style multi-backend
picker: content that polls `Mouse.GetState()` and checks rectangles itself, outside-press checks built from lists of
rectangles, four separate tap thresholds with three different rules, a second hit test inside click dispatch, and
handlers that identify a target by type pattern (`Window { Content: HotbarContent }`) plus their own geometry. A full
`bevy_picking` port (backends, depth-sorted hit stacks, `Pickable` flags, map entities as pickables) would fix those
only as a side effect, at the cost of rewriting the controller. **Recommendation: no rewrite. Bring everything onto the
one hit test, give it one gesture recognizer, and add enter/leave and bubbling events to elements, then make the map a
second source of hits so an item dragged out of a window can land on a tile or an entity.** Items will be dragged onto
the map for several purposes (dropping them, putting them in a container without opening its window), so the last
phase is part of this plan, not deferred.

## How it works today

### The element hit test (`Cadence/UIEngine/Input/UiInputController.cs`, `UI/Element.cs`)
- `TryHitTestInteraction(position)`: layers descending (ContextMenu, User, Tooltip, DynamicHud, StaticHud, Base),
  topmost-raised first in each. In menu mode the order is context menu, then menu windows, then User/Tooltip, then
  menu-mode-exempt elements.
- `Element.TryHitTestInteraction`: rectangle check, then header button, header (Move), resize band (scrollbar wins
  except at corners), its own scrollbar, `IsPointerDragHandleAt` (splitter), children topmost-first, else itself.
  Result `ElementInteraction`: the one leaf element, a drag kind, edges, button, scrollbar part. `IsHitTestable = false`
  already acts as Bevy's `should_block_lower = false` (used by `EmptyTradeSlotCell`).
- `RefreshHoveredInteraction` caches the result; it is invalidated after a press, release, wheel and keyboard change.
  `PointerState.HoveredElement` publishes it; `PointerState.IsOver(element)` walks its ancestors.

### Gestures
| Gesture | Where | Threshold | Rule |
|---|---|---|---|
| Right tap vs pan | `HandleRightDrag`/`HandleRightDragEnd` | 4 px | `>`, latched once exceeded |
| Text selection drag | `HandleTextSelectionDrag` | 4 px | `>=`, latched |
| Content drag vs click | `ExceededContentDragTapThreshold` | 4 px | `>=`, **release distance only, not latched** |
| Hotbar tap | `HotbarInputHandler.OnRelease` | 4 px (its own constant) | `>=`, release distance only |
| Double-click | `HandleDoubleClickAwareClick` | 0.3 s | per element, opt-in |

A content drag that goes out past 4 px and comes back within 4 px of the press releases as a **click** on the origin
cell (and drops nothing). A right-drag that does the same is a pan. The hotbar measures on its own and could disagree
with the controller about the same release.

### Clicks
`HandleMouseRelease` → `DispatchClick(_activeInteraction.Element)` → `Element.HandleClick` → header or content →
`OnContentClickAction` → `FindTopmostHit(_children, position)` → the child's `HandleClick` again. That is a **second
hit test** by plain rectangle (no scrollbar, header-button, clip or menu-mode rules). It only matters where the leaf has
children, but it is the same decision made twice by different code.

### Hover that goes around the hit test
Each polls `Mouse.GetState()` in its own `Update` and tests rectangles:
- `InventoryGridContent.UpdateHover`/`FindHoverCandidate` (cell highlight and item tooltip)
- `CurrencyRowContent.UpdateHover`
- `LootboxResultsContent.UpdateHover`
- `PlayerHealthBarContent.Update`
- `AbilityScoreWindow.UpdateHover` (its doc already lists a known gap: not suppressed during a title-bar drag)
- `MapWindow.Update` reads `Mouse.GetState()` for the hovered tile (gated by `PointerState.IsOver`, so it is right, but
  it takes the position from the OS rather than from the controller -- tests drive it separately through
  `UpdateHoveredTile`).

None of the rectangle checks know about occlusion (a window drawn over the grid), the scrolled-out part of a list (a
cell scrolled out of view keeps its rectangle under the window's header, tabs or footer), menu mode, or an
interaction in progress. **Suspected, not yet reproduced** -- Phase 0 checks each.

### Outside-press dismissal
- Dropdown: `HandleMousePress` checks `Menu.Rectangle` and `Owner.Rectangle`.
- Item Details: `ItemDetailsOutsideClickInputHandler` → `ItemDetailsWindowController.IsOutsideClick`, a hand-kept list
  of six rectangle sources (itself, the player's inventory, secondary, trade, loot box results, comparison columns).
  Every new related window must be added to it, and each is checked by rectangle, not by what was hit.

### Drops
`IContentDropHandler`s are tried in order. `InventoryTransferDropHandler.FindDropTargetEntityId` walks up from the hit
element through `ParentElement` looking for `Window { Tag: IInventoryDropTarget }` or `IWholeWindowDropTarget` -- a
hand-written bubble. `HotbarBindingDropHandler` matches `Window { Content: HotbarContent }` and asks
`HotbarContent.TryGetSlotAt(position)` (slots are geometry, not elements).

### The map
`MapWindow` is one element. Tiles and entities are resolved inside it: `MapCamera.TryGetHoveredMapPosition`, then
`ActionTargetingController.TrySelectActivationTarget` (Target/Ground rules, fuzzy nearest search) for both the preview
and the confirm, `TryOpenEntityContextMenuAt` for right taps, `SelectMapNodes` for plain clicks. The 2026-10-27 fixes
already put the preview and every confirm through one selection. Nothing drags onto or off the map.

## Compared with `bevy_picking`

| bevy_picking | Here | Gap that matters |
|---|---|---|
| Backends report hits with depth; one pass sorts | One backend (elements), layer order is the depth | None for UI. The map would be a second backend only if something drops onto it. |
| `Pickable { should_block_lower, is_hoverable }` | `IsHitTestable` | Nothing needs pass-through hover yet. |
| `Pointer<Over>`/`<Out>` | None; content polls | **Yes** -- the self-polled hover above. |
| `Press`/`Click`/`DragStart`/`Drag`/`DragDrop`, one drag threshold | Click via `HandleClick`; drags split across 4 mechanisms | **Yes** -- one recognizer. |
| Events bubble up `ChildOf` until stopped | Wheel bubbles (`OfferMouseWheel`); drop targets bubble by hand; click re-hit-tests downward | Partly -- drops and clicks. |
| Map entities and UI in one drag | Map is opaque to the controller | **Yes** -- items will be dropped on tiles and entities. |

## Phases

Scope settled 2026-10-27: all five. Each phase ends with live testing before the next (see the test bullets).
Phases 1, 2 and 4 stand alone; 3 builds on 1 and 2; 5 builds on 3.

### Phase 0 -- Reproduce the suspected bugs (no code)
Run the game and check each, recording which are real:
- Open Item Details or a comparison column over the inventory grid; hover a cell under it. Expect (bug): the cell
  under the window highlights and its tooltip opens.
- Scroll a long inventory so cells sit above the grid's visible area; hover the tab strip / header just above it.
  Expect (bug): a hidden cell's tooltip.
- Open a menu window over the currency row or health bar; hover through it.
- Drag an item out 20 px and back to the press point, release. Expect (bug): the origin cell's click (Item Details
  opens or toggles) instead of nothing.
- Same on the hotbar: press a slot, move out and back, release. Compare with the inventory.
- Drag the Ability Score window by its title over its own rows (the documented gap).

**Results (2026-10-27).** Reproduced headlessly through the real `UiInputController` (temporary probe tests, removed):
- **Confirmed:** a content drag out 30 px and back to 1 px from the press fires the origin cell's click
  (`onItemSelected` once) -- Item Details opens for an item the player dragged.
- **Confirmed, the contrast:** the same out-and-back with the right button is a pan (0 taps, 1 drag end).
- **Confirmed:** with a window drawn over an inventory cell, the controller's hovered element is the covering window,
  but `InventoryGridContent.UpdateHover` still sets the cell's `IsHovered` (highlight, and its tooltip after the delay).
- **Same mechanism, by code:** `CurrencyRowContent`, `LootboxResultsContent`, `PlayerHealthBarContent` and
  `AbilityScoreWindow` test `Rectangle.Contains` against `Mouse.GetState()` the same way, so the same occlusion holds.
- **Hotbar out-and-back:** taps (arms or confirms) the slot, the same as the inventory's click -- consistent with the
  inventory today, inconsistent with right-drag and text selection. Phase 1 makes all of them a drag.
- **Live check still open:** a cell scrolled out of the inventory's scroll window keeps a rectangle above it (the
  sprite effect clips the draw, not the rectangle), so hovering the tab strip or search box above a scrolled grid
  should highlight and tooltip a hidden cell. Needs the real layout to confirm.

### Phase 1 -- One gesture recognizer (landed 2026-10-27)
- `PointerGesture` (UIEngine, internal): one per button, press position, `HasExceededTapThreshold` latched once the
  cursor goes `TapThresholdPixels` (one constant, 4 px) from the press. Left and right each hold one.
- Content drag, text-selection drag and right pan read it; `ExceededContentDragTapThreshold` becomes the latch, so out-
  and-back is a drag that does nothing (no click; the drop on its origin is a no-op).
- `IUiInputHandler.OnRelease` gains `bool isTap`; `HotbarInputHandler` drops its own threshold and position.
- Tests: `UiInputControllerTests` for out-and-back on content drag, text selection and right pan; hotbar tap rules.
- Live: the Phase 0 drag cases; a right-drag pan; a jittery click on a slot still clicks.

### Phase 2 -- Hover from the one hit test (landed 2026-10-28)
As built: `Element.IsPointerOver` only -- no enter/exit events, since every consumer reads hover in its own Update
with its own delay counter. `Element.TreeVersion` (a child added or removed, visibility changed) joins the hit test's
cache key, so a list rebuilt under a still mouse is hovered at once. A sixth site turned up, `HealthWindow`'s rows.
`IsHovered` is gone from `InventoryItemStackCell`, `CurrencyElement`, `AbilityScoreColumnHeader` and
`AbilityScoreModifierRow` (they draw from `IsPointerOver`); `PointerState.IsOver` is gone; `AbilityScoreWindow` and
`HealthWindow` no longer take `PointerState`.

As planned:
- The controller keeps the hovered chain (leaf plus ancestors) and on change sets `Element.IsPointerOver` and raises
  `PointerEntered`/`PointerExited` on the elements that left or joined it (DOM `mouseenter` semantics, no bubbling), not
  refreshed during a move, resize or scrollbar drag (as `OnPointerUpdate` is today). A cell rebuilt under a still
  mouse is seen when the layer version or position changes, the same rule as now.
- `InventoryGridContent`, `CurrencyRowContent`, `LootboxResultsContent`, `PlayerHealthBarContent` and
  `AbilityScoreWindow` read `IsPointerOver` of their cells/rows instead of `Mouse.GetState()` and rectangles; the hover
  delay counters stay theirs.
- `MapWindow.Update` takes the cursor from `PointerState.CursorPosition`, not `Mouse.GetState()`.
- Architecture test: no `Mouse.GetState()` in UIEngine, Presentation or the Workshops outside `UiInputController` and
  the host loop.
- Live: every Phase 0 hover case; tooltips still delay and hide as before; hotbar summary unchanged.

### Phase 3 -- Events bubble to the element that handles them (landed 2026-10-28)
As built: `Element.HandleClick` and `HandleRightClickTap` return whether the element handled it (`OnContentClickAction`
and `OnRightClickTapAction` return bool; a `Clicked` or `OnRightClicked` listener counts); the controller offers each
from the hit up, and the downward re-test is gone (`TextWindow`'s empty override too, so a label clicks its row). Drop
targets are named, as payloads are: `IContentDropTarget.DropTargetFor(payload, position)` on an element, or attached as
`Element.DropTarget` for manually hosted content (replacing `Window.Tag`); the controller hands the nearest answer to
`IContentDropHandler.TryDrop`/`IsDropRefusedAt` as an opaque object. The game's are `InventoryDropTarget` (grids,
currency rows, the three whole windows -- `IInventoryDropTarget`, `IWholeWindowDropTarget` and
`FindDropTargetEntityId` are gone) and `HotbarSlotDropTarget`. Hotbar slots are `HotbarSlotElement`s; a click raises
`HotbarContent.SlotClicked` and `HotbarController.OnSlotClicked` arms or confirms (its press/tap bookkeeping is gone).
`HotbarInputHandler` stays, only to tick the hover summary each frame. The hotbar window has no content padding, so slots
sit exactly where the bar drew them. The Workshop's record rows dropped the click wiring on their icon and name.

As planned:
- Click: dispatch to the hit leaf and bubble up `ParentElement` until one handles it (`OnPointerClick` returning
  handled), replacing the downward `OnContentClickAction` → `FindTopmostHit` re-test. Header clicks stay as they are
  (the hit test already returns the header button).
- Drop: `IContentDropTarget` on an element (`TryAcceptDrop(payload, position)`), found by bubbling from the release's
  hit; `InventoryTransferDropHandler` asks the target for its entity instead of walking for `Window.Tag`.
  `IContentDropHandler` stays for drag-wide concerns (refusing a start, the ghost-shown glow, unbinding a slot dragged
  off the hotbar wherever it lands).
- Right tap already goes to the hit element; it bubbles too, so a cell without a menu leaves it to its window.
- **Hotbar slots become elements** (`HotbarSlotElement`, one per laid-out slot, fixed children of the hotbar window,
  rebuilt only when expansion slots unlock, never pooled per frame). `HotbarContent` keeps the layout math
  (`SlotBounds`: base slots, default attack, expansion rows and pages, locked slots) and hands each slot its bounds;
  each slot draws itself (`DrawSlot`, its text caches, radial fill, accepting glow, active alpha), names its own drag
  payload (`IContentDragSource`, as `InventoryItemStackCell` does), handles its own tap through Phase 1's gesture and
  its hover through Phase 2's `IsPointerOver`, and is an `IContentDropTarget` for binding. `HotbarInputHandler` goes;
  `HotbarBindingDropHandler` keeps only the drag-wide part (the accepting glow, unbinding a slot dragged off wherever
  it lands); `ActivationRefusalInputHandler` asks the hovered slot. `TryGetSlotAt` goes. A slot's right-click menu
  is the ordinary `OnRightClicked` hook, ready for TODO.md's hotbar "Drop". Unchanged: re-press confirms, hover
  summary timing (ticks live), which slot an activation names.
- Live: every click path (buttons, cells, tabs, list rows, double-click cells), every drop path (give, loot, buy, sell,
  trade staging, hotbar bind/unbind/swap, currency), context menus on cells and windows.
- Live, hotbar: tap a slot to arm, tap again to confirm; a disabled slot shows the refusal cursor and refuses; hover a
  bound slot for its summary after the usual delay, with a window dragged over the hotbar hiding it; drag an item and
  an action onto slots, swap two slots, drag a slot off onto the map and onto empty space (unbinds); unlock an
  expansion page (Admin) and use its new slots; a Merged Stack, currency and a corpse's item show the refusal cursor
  over a slot.

### Phase 4 -- Outside-press dismissal by hit, not rectangle (landed 2026-10-28)
As built: `ILightDismissable` (UIEngine: `IsOpen`, `CountsAsInside(element)`, `Dismiss`), opened and closed on
`UiLayerStack` (`OpenLightDismiss`/`CloseLightDismiss`; `TopmostLightDismiss` is the latest still open, dropping one
that closed without saying). On a press the controller hit-tests once; when neither the element hit nor any ancestor
counts as inside the topmost one, it is dismissed and the press goes no further. Only the topmost is asked: a press
outside an open dropdown over Item Details closes the dropdown alone. `DropdownController` is one (inside: the menu and
its owner), replacing the controller's rectangle check. `ItemDetailsWindowController` is one (inside: its window, the
player's Inventory window, and `IsTiedWindow` -- the corpse/container, shop, trade and loot box results windows and the
comparison columns, by identity), replacing `IsOutsideClick` and its four rectangle hooks; the inside set is asked
rather than kept, so no window has to report opening or closing. `ItemDetailsOutsideClickInputHandler` is gone; the
tied controllers expose their `Window` instead of a `Rectangle`, and `ItemComparisonController.Columns` replaces
`ColumnRectangles`.

As planned:
- A light-dismiss scope (`UiLayerStack` or the controller): an owner plus the elements that count as inside; a press
  whose hit chain contains none of them dismisses it and is consumed. The dropdown and Item Details both register one;
  Item Details' inside set is the windows it is tied to, added and removed as they open and close, instead of
  `IsOutsideClick`'s rectangle list. `ItemDetailsOutsideClickInputHandler` goes.
- Live: Item Details stays open for clicks in the inventory, secondary, trade, loot box results and comparison
  columns, closes for the map and HUD, and a click on a window drawn over the inventory counts as outside.

### Phase 5 -- The map as a source of hits, and drops onto it (landed 2026-10-28)
As built: `MapWindow.TryGetPointerHit(screenPosition)` is the one place a position becomes a `MapPointerHit` (the tile on
the layer shown, its occupants topmost first in the order the map actually draws them -- Phasing, Blocking, Tiny,
corpses, then anything undrawn), reused while the tile and its occupants are unchanged; click selection, the right-tap
menu (its groups now topmost first) and drops read it. Not built: the UIEngine `IPointerSubHitProvider` and a hit
published in `PointerState` -- the camera and occupants change under a still cursor, so it would be rebuilt every
frame, and nothing reads it yet; the targeting preview and confirm still resolve their tile through the same
`MapCamera` call. `MapWindow` is an `IContentDropTarget` naming the hit. `MapDropHandler` (registered after
`InventoryTransferDropHandler`, before the hotbar's): the first occupant that can be looted (a container that isn't a
standing shop, or anything dead) takes the drop through `InventoryTransferDropHandler` as if dropped on its window --
so items, Merged Stacks and currency follow Give's rules; refused (cursor and drop) for a tile nothing takes, a
container out of reach, a corpse reserved for someone else, and anything picked up from a shop or a trade column; a
hotbar-slot drag is left to the hotbar. No resolver chain yet: the ground resolver adds one when item piles land.
Menu mode: a drag held where menu mode leaves nothing hovered finds its drop target among every layer
(`UiInputController.DropElementAt`), so items can be carried out of an open inventory onto the map; clicks are still
blocked. While something is dragged the map shows no targeting preview.

As planned:
Items will be dragged onto the map to drop them on the ground and to put them in a container (a chest, a corpse)
without opening its window -- more than one way to do the same thing. This phase builds the pipeline for that and
the first use that exists today (into a container); dropping on the ground plugs in when TODO.md "Dropping items on
the ground" lands its item pile.
- **Map hits.** An element can report what part of itself is under the cursor (`IPointerSubHitProvider`, UIEngine,
  opaque to the controller like a drag payload). `MapWindow` reports a `MapPointerHit`: the tile (column, row, the
  shown layer) and its occupants in the order they are drawn on top (`MapDrawOrder`'s rule: Blocking, then Tiny, then
  Phasing, then corpses). Computed once when the hit test runs, from the controller's position -- the hovered tile,
  the targeting preview, right-tap context menus and plain-click selection all read it instead of each calling
  `MapCamera.TryGetHoveredMapPosition`. Published in `PointerState` beside `HoveredElement`.
- **Drops on the map.** `MapWindow` is an `IContentDropTarget` (Phase 3). What a drop there means is a chain of
  resolvers in Presentation, beside the inventory ones, tried in order until one claims it:
  1. Onto a container or corpse on the tile (the first occupant that holds an inventory and can be given to): the
     existing transfer of the whole stack (`InventoryCommands`, as a drag between inventory windows does), refused unless the player is adjacent to any tile of its
     footprint (the Loot rule, `IsAdjacentToPlayer`).
  2. Onto the ground: claimed only once the item pile exists; until then not registered.
  Each resolver also answers `IsDropRefusedAt` for the cursor, with the same rules, so the refusal cursor shows over a
  container out of reach or a tile nothing accepts. Shops and trade-offer entities never accept a map drop (a shop is
  bought from and sold to only through its window).
- **Arming and targeting are untouched.** A drop is a drag release, never a click, so it never confirms an armed action
  or moves the inspector selection. Dragging over the map shows no targeting preview.
- Tests: `MapPointerHit` over each occupant kind and a large footprint; drop into an adjacent chest and corpse, refused
  when not adjacent, refused on a shop, a Merged Stack and currency (per what Give accepts); the cursor while hovering
  each.
- Live: drag a potion from the inventory onto an adjacent chest (it moves, chest shows it in its loot window), onto a
  distant one (refusal cursor, nothing moves), onto a corpse, onto a shopkeeper (refused), onto an empty tile
  (refused until piles exist); with an action armed, a drop doesn't fire it; hover preview, right-tap menus and click
  selection still pick the same tile and entity as before.

### Follow-ups (landed 2026-10-28)
- A bound hotbar slot has a right-click menu (`HotbarController.OnSlotRightClicked`): Arm, or Activate/Deactivate for a
  toggle by its state -- each exactly a press of the slot -- then Unbind; an option the slot can't take is disabled.
- A hotbar item's drag names the player as its origin, so it goes into a container (on the map or its window) like the
  same item from the inventory. `HotbarBindingDropHandler` runs first: binding a slot claims the drop; a drag from a
  slot that lands elsewhere unbinds it and leaves the drop to the handlers after it.
- A drop any handler refuses is offered to none (`UiInputController`), so a refused drop never unbinds a slot or moves
  anything. `IContentDropHandler.OnDragOver` tells handlers what a drag with its ghost showing is over each frame.
- Capacity: `InventoryTransferDropHandler` refuses items an inventory has no room for (`InventoryCapacity`, 20 stacks
  for anything but the player; shops and the player are never full here). A container on the map under a held item is
  washed green when it would take it and red when it wouldn't (`MapViewState.ContainerDropHighlight`, drawn by
  `MapGameOverlay`).

## What this does not change
Keyboard routing, focus, Escape, scrolling and wheel bubbling, the cached hit test and its invalidation, menu mode's
reach rules, `ActionTargetingController`'s selection rules, `IUiInputHandler` for app-wide concerns (Admin Mode,
hotkeys).

## Decided
- Scope: Phases 1-5 (2026-10-27).
- Items will be dragged onto the map for several purposes (dropping them, putting them in a container without opening
  its window), so the map is a source of hits and a drop target here, not later.
- A content drag that goes out past the threshold and back does nothing: no click, and the drop on its own origin is
  a no-op.
- A drop onto the map moves the whole stack, like every drag today. Half / exact amounts on map drops are v2 of TODO.md
  "Stack Controls and Partial Stacks". TODO.md "Dropping items on the ground" points at Phase 5 for the drag.
- Hotbar slots become elements, in Phase 3.
