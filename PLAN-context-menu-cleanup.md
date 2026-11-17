# UIEngine: submenus beside their menu, options indented under headings

Plans TODO.md's "Context menu cleanup" (High). Menus are built from controls since `PLAN-control-content.md`; this
changes how they read: a submenu opens beside the menu that opened it instead of replacing it, and an option under a
heading is indented rather than the heading being bold.

**Status:** written 2026-11-17 from a read of the code. Decisions 1-12 confirmed the same day. Phase 1 built
2026-11-17 (plain headings; Admin Mode's "Tile (x, y)" heading). From its check: the first indent, a spacer child,
left a checkbox row's checkbox in place (the toggle draws it); the indent is now `Element.ContentIndent`
(`ElementHierarchyOptions.ContentIndent`), before everything an element shows -- what it draws itself and its tiled
children -- checked. Then (decision 13) the indent became optional per menu: `indentsUnderHeadings` on
`ContextMenuController.Open`/`DropdownController.OpenAt`/`Dropdown.Show`, off unless asked; the map's right-click menu
asks (`MapWindow.TryOpenEntityContextMenuAt`, the only game menu with headings), the filter menus don't. Checked.
Phase 2 built 2026-11-17 (`PlaceBeside`, `CloseDeepest`, Left/Right; the root keeps its name `Menu`, with `OpenMenus`
beside it; a submenu's rows scroll past the screen's height). From its check: choosing the row whose submenu is open
closes it (decision 4), and submenus come from the element pool and go back to it rather than a list the controller
kept (decision 1). Checked. Phase 3 done 2026-11-17 (IMPLEMENTATION-NOTES "Context menu", the CLAUDE.md UIEngine
bullet, TODO.md). **Complete.**

## What the code says today

- **One menu instance.** `DropdownController` (`Cadence/UIEngine/UI/DropdownController.cs`) creates a single pooled
  `Dropdown` in `Initialize`, adds it to `UiLayer.ContextMenu`, and is itself the one `ILightDismissable` for it
  (`CountsAsInside`: the menu or its `Owner`). `IsOpen` is that menu's visibility. `ContextMenuController` (a right
  click's menu, `OpenAt`) and `DropdownField`/`MultiChoiceField`/`ListFilterButton` (`OpenBelow`) all share it, so only
  one list is ever open.
- **A submenu replaces the menu.** `DropdownOption.Submenu` (built by `DropdownOption.Opening`, which puts
  `Dropdown.SubmenuMarker` ">" in the hotkey column) makes `Dropdown.Choose` park the list in `_pendingSubmenu`; the
  next `Dropdown.Update` calls `OpenSubmenu`, which the controller set to `Place(options, -1)` -- the same instance
  rebuilt at the same anchor. It is deferred because rebuilding inside the click would pool-return the row whose click
  is still running. There is no way back to the parent.
- **Who uses submenus:** only Admin Mode's map menu (`Presentation/UI/AdminContextMenuOptions.cs`): "Spawn here",
  "Apply", and "Grant loot box" -> type -> rarity (three levels). TODO.md's "TextBox context menu wiring, and 'Bind
  To...' sub-menu" waits on cascading submenus for an item's "Bind To...".
- **Choosing closes from inside.** An ordinary option runs `OnSelect` then `Dropdown.Hide()` on its own menu; the
  controller notices only through `IsOpen` (and `UiLayerStack.TopmostLightDismiss` drops it once it reports closed).
- **Input** (`Cadence/UIEngine/Input/UiInputController.cs`): Up/Down/Enter reach the open dropdown before the focused
  element (`RouteNavigationKeysToOpenDropdown` -> `DropdownController.HandleNavigationKey`); Escape closes it before
  anything else; a right press closes it and goes on; a left press outside it light-dismisses it
  (`TryLightDismissAt`). Hit-testing checks the `ContextMenu` layer first, topmost (last) element first, even in menu
  mode.
- **Headings:** `DropdownOption.Header` rows (`Dropdown.AddHeaderRow`) are shorter (18 px vs 22), on the panel colour,
  inset by `RowTextInset` like every row, and bold (`Label.IsBold`). An option row's label starts at the same inset, so
  nothing but the weight separates a heading from its options. Headings are used by the map's right-click menu (one per
  occupant, the structure and the terrain: `MapWindow`), Admin Mode's neighborhood group, and the browser filter menus
  (`ListFilterButton.BuildOptions`: "Show only" and each value group), whose rows are flat-checkbox `Toggle`s.
- **Placement:** `PopupPositioning.GetPositionWithinBounds` (`PopupAnchor.cs`) flips on the clipping axis and clamps.
  `East`/`West` centre the popup on the target vertically; no anchor aligns tops, which a submenu wants.
- Tests reach the menu through `ContextMenuController.Menu`/`DropdownController.Menu` (internal) and its
  `ChildElements`; `UiInputControllerTests.ClickSubmenuOption_ThenClickItsOption_InvokesTheSubmenuOption` pins today's
  replace-on-next-update behaviour, `AdminContextMenuOptionsTests` the options themselves.

## Decisions (to confirm)

1. **A chain of menus, one controller.** `DropdownController` keeps an ordered list of open `Dropdown`s -- the root
   (what `OpenAt`/`OpenBelow` opened) and each submenu after it. The root is one `Dropdown` kept for the controller's
   life; each submenu is taken from the element pool when it opens and added to `UiLayer.ContextMenu` last, so it is
   drawn and hit above its parent. A closed submenu is hidden at once and returned (out of the layer, back to the pool)
   when the next menu opens -- never inside the click that closed it, which may still be running in one of its rows.
   The controller stays the one `ILightDismissable`:
   `CountsAsInside` is any open menu or the `Owner`, so a press outside every menu closes them all and a press in any of
   them is inside. `IsOpen` is the root's.
2. **What opens a submenu: choosing its row** -- a click, Enter on it, or Right on it. Hovering does not open one
   (no hover timer): an option does what choosing it does, nothing happens by resting the pointer. Hover-to-open can be
   added later behind the same `OpenSubmenu` path if it is missed.
3. **Opening is immediate, not deferred.** Opening a submenu rebuilds only the child menu (and closes any deeper ones),
   never the menu whose row was clicked, so `_pendingSubmenu` and the next-update swap go away.
4. **One submenu per level.** Choosing another submenu row in a parent closes what is open below that parent and opens
   the new one; choosing the row whose submenu is already open closes it and everything below it. The row whose submenu is open keeps its
   highlight fill while its child is open (the "this is where it came from" cue), whatever the pointer does.
5. **Placement beside the row.** The submenu's first row lines up with the row that opened it: its top-left at the
   parent menu's right edge and the row's top (less the submenu's border and padding), flipping to the parent's left
   edge when it would clip the right of the screen, and moved up (never flipped) when it would clip the bottom. A
   placement method of the controller beside `PlaceBelow` (`PlaceBeside(parentRectangle, rowRectangle, menuSize,
   screenBounds)`, static and unit-tested), not a new `PopupAnchor`: the tooltip anchors have no "tops aligned" member
   and nothing else wants one.
6. **Returning to the parent.** Left (when a submenu is open) and Escape close the deepest submenu only and leave the
   parent's highlight on the row that opened it; Escape on the root closes it as today. A press in a parent menu on
   anything but an option row (a heading, empty space, its scrollbar) closes the menus below it; a press on an option
   row chooses it (an ordinary option runs and closes the whole chain). Scrolling a parent closes the menus below it,
   since they would be left beside a row that has moved.
7. **Choosing closes the chain.** An ordinary option runs `OnSelect` and the controller closes every open menu -- the
   `Dropdown` tells the controller it was chosen (a callback the controller sets, replacing `OpenSubmenu`'s current
   shape) instead of hiding itself. A checkbox option still turns and leaves every menu open.
8. **Keys go to the deepest menu.** Up/Down/Enter move and choose in the deepest open menu. Right and Left are added to
   the routed keys but taken only when they mean something (Right on a highlighted submenu row, Left with a submenu
   open), so a `DropdownField`'s text box under its list keeps Left/Right for its caret. A submenu opened by a key
   highlights its first choosable option; one opened by a click highlights nothing.
9. **Indentation under headings.** An option row that follows a heading in the list is indented by a fixed step
   (`HeadingIndent`, 12 px) added to its left padding; options before the first heading are not. One level only:
   headings don't nest. The indent is part of the row's measured width, so the widest row and the hotkey column still
   line up. The marked-row accent and the highlight fill stay full-width.
10. **Headings are not bold.** `AddHeaderRow` drops `IsBold`; the heading keeps the shorter row and the panel colour.
    No other change to how a heading looks unless testing says the indent alone doesn't read.
11. **`DropdownOption.Submenu` keeps its shape.** Its doc and `Opening`'s change from "replaces the menu" / "in the
    menu's place" to "opens beside it".
12. **Admin Mode's tile options get their own heading.** `AdminContextMenuOptions.AddTileOptions` adds the
    neighborhood heading and "Regenerate", then "Spawn here", the wall option and "Teleport here" -- so once options
    indent, those three would read as the neighborhood's. They go under a heading of their own, "Tile (x, y)" (layer
    too if more than one is in play), after the neighborhood group. The structure and terrain headings, which have no
    options, are unchanged.
13. **Indenting is the menu's choice** (from Phase 1's check). A menu indents the options under its headings only when
    its opener asks; the game's context menus do, the browser filter menus don't. When a menu indents, the whole row
    moves -- checkbox, icon, label and hotkey (`Element.ContentIndent`).

## Phases

### Phase 1 -- Headings: indented options, plain headings

- `Dropdown.BuildRows`: track whether a heading has been passed; `AddOptionRow` takes an `isUnderHeading` flag and
  gives its row `ContentIndent = HeadingIndent`. `Element.ContentIndent` (new, `ElementHierarchyOptions`) is where
  tiled children start, before `LeadingTileInset`, and `Toggle` draws its checkbox past it. `AddHeaderRow` stops
  setting `IsBold`.
- Then (decision 13): opening a menu says whether it indents (`indentsUnderHeadings`, default off); the map's
  right-click menu does, the filter menus don't. A list opened under its field (`OpenBelow`) never indents.
- `AdminContextMenuOptions.AddTileOptions`: the "Tile (x, y)" heading before "Spawn here" (decision 12);
  `AdminContextMenuOptionsTests` updated for the extra heading.
- Tests (`Tests/UIEngine`, beside the existing dropdown tests): an option after a heading starts its label
  `HeadingIndent` further right than one before any heading; a heading's label is not bold; every row is as wide as the
  widest, indent included.
- Check:
  - Right-click a tile with an occupant on the map: each occupant's options sit indented under its name; the name is
    no longer bold.
  - In Admin Mode (F12), right-click a tile: "Regenerate" is indented under the neighborhood heading; "Spawn here",
    "Build stone wall" and "Teleport here" are indented under a "Tile (x, y)" heading after it.
  - Workshop Items tab: open the filter button's menu: the "Show only" and value-group choices are indented under
    their headings, checkboxes still lined up, counts still right-aligned in one column.
  - A `DropdownField` with no headings (an inspector enum field) looks unchanged.

### Phase 2 -- Submenus beside their menu

- `DropdownController`: `_menus` (open chain) and `_menuInstances` (pooled, created on first use, initialized and added
  to the layer as `Initialize` does for the root). `OpenAt`/`OpenBelow` close any open submenus and place the root as
  today. New `OpenSubmenu(int parentLevel, int optionIndex, IReadOnlyList<DropdownOption> options, bool fromKey)`:
  closes levels above `parentLevel`, shows the next instance, places it with `PlaceBeside`, raises it to front.
  `Close` hides every open menu. `CloseSubmenusBelow(level)`. `CountsAsInside` checks every open menu. `Menu` becomes
  `RootMenu`, plus `internal IReadOnlyList<Dropdown> OpenMenus` for tests. `HandleNavigationKey` routes to the deepest
  menu and handles Left/Right per decision 8.
- `Dropdown`: `_pendingSubmenu` removed; `Choose` on a submenu row calls the controller's callback with its index;
  an ordinary option calls the chosen callback rather than `Hide()`. New `OpenSubmenuIndex` (-1 for none), drawn with
  the highlight fill in `DrawContent`; `FollowPointer` leaves it alone. A press on a non-row part of the menu and a
  scroll report to the controller (decision 6) -- through `Element.HandleClick` reaching the menu itself, and its
  scroll offset changing.
- `UiInputController`: Escape calls a controller method that closes the deepest menu (root last) instead of `Close`;
  Left/Right join `DropdownNavigationKeys`. Right press still closes the whole chain.
- `DropdownOption` docs per decision 11.
- Tests:
  - Replace `ClickSubmenuOption_ThenClickItsOption_InvokesTheSubmenuOption`: clicking "More" opens a second menu at
    once with the root still open and unchanged; clicking "Pick" in it runs it and closes both.
  - Choosing a second submenu row swaps the child; choosing the open one leaves it.
  - Three levels (the loot box shape): a press outside all closes all; a press in the middle menu's heading closes only
    the third; a press inside any menu is not a light dismiss.
  - Escape closes the deepest only, then the next; Left the same; Right on a highlighted submenu row opens it with its
    first choosable option highlighted; Left/Right with a `DropdownField` list open and no submenu reach the text box.
  - `PlaceBeside`: right of the parent with tops aligned; flipped left at the screen's right edge; moved up at the
    bottom; never above the screen's top.
  - The row that opened a submenu stays highlighted while the pointer is in the submenu.
  - `AdminContextMenuOptionsTests` needs nothing more than Phase 1's heading change.
- Check:
  - Admin Mode, right-click a creature: "Grant loot box >" opens the types beside the menu, the main menu still shown
    and its "Grant loot box" row highlighted; choose a type: the rarities open beside that, three menus visible; choose
    a rarity: a box is granted and all three close.
  - Right-click near the right edge of the screen: the submenu opens to the left of the main menu instead.
  - Right-click near the bottom: a long submenu ("Spawn here", every spawnable blueprint) moves up to fit, scrolls if
    taller than the screen allows, and its first row is as near the opening row as fits.
  - With the rarities open, press Escape: only the rarities close; again: only the types; again: the main menu.
  - With the types open, click "Apply >" in the main menu: the types close and the blueprints open beside "Apply".
  - With submenus open, click the map: everything closes and the click does nothing else (as a light dismiss does
    today). Right-click elsewhere: everything closes and a fresh menu opens there.
  - Keyboard: Down to "Spawn here", Right: the blueprints open with the first highlighted; Left: back to "Spawn here".
  - A `DropdownField` in the Workshop inspector: Left/Right still move the caret in its text while the list is open.

### Phase 3 -- Notes and TODO

- IMPLEMENTATION-NOTES: rewrite "Context menu" (it still names `ContextMenu`/`ContextMenuOption`): one controller, a
  chain of pooled menus, submenus beside their row, how they close, keys; and in "Control content" that a heading's
  options are indented.
- CLAUDE.md UIEngine bullet: "the one shared `Dropdown` popup" becomes the controller's chain (a context menu or a
  field's list, and its submenus beside it).
- TODO.md: remove "Context menu cleanup"; in "TextBox context menu wiring, and 'Bind To...' sub-menu", drop the
  cascading-submenu work it lists as missing (now built) and keep what is left.

## Not part of this

- Hover-to-open submenus and a submenu's open delay (decision 2).
- Nested headings or headings inside a submenu getting a second indent level.
- Any change to which options the game's menus offer.
