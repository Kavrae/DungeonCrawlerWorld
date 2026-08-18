# Inventory item filtering, sorting, and a reusable GridControl row

(Approved plan, saved for later implementation. Addresses TODO.md's "Inventory item sorting,
filtering, and searching" and "Tab Stats row" items together.)

## Context

Dynamic per-tag Inventory tabs and tab search landed (see project memory
`project_inventory_tabs_landed`/`project_inventory_tab_search_landed`). Two follow-up TODO items
were deliberately left open at the time: per-tab item sorting/filtering/searching beyond the
fixed alphabetical default, and a "Tab Stats row" between the tab strip and the grid (item count,
weight, and search/sort/filter controls scoped to the active tab).

Weight is explicitly **out of scope** for this plan -- deferred until TODO.md's "Item weight
(definition-only) and race weight ranges" item lands (no `ItemDefinition` carries a weight field
yet).

## Naming/scoping corrections from the original draft

- **"Stats-Row Window" was a bad name.** It's a `GridControl` element -- a reusable row of
  grid-scoped controls (count, sort, filter, search), not an Inventory-specific "stats" concept.
  Both the control row and the grid it sits above are meant to be reusable building blocks for
  future windows (e.g. the Magic Menu), not one-off Inventory UI.
- **Disabled items are controlled by a toggle button for now**, not a checkbox -- checkbox is its
  own TODO item ("Checkbox widget to replace the Hide Disabled toggle button"), blocked on the
  Standard widget set TODO.
- **Sorting is click-to-cycle for now**, not a picker -- a context-menu-based sort picker is its
  own TODO item ("Advanced sort control"), blocked on context-menu/mouse-button coverage not
  existing yet.

## Design

**`GridControl`** (`Presentation/UI/GridControl.cs`, new -- a `Window` subclass, same
"subclass warranted" reasoning as `MapWindow`/`AbilityScoreWindow`: it composes multiple children
and owns its own event/update logic). Fully generic -- no reference to items, tags, or
`InventoryGridContent` anywhere in it:

- An item-count display, updated via `SetItemCount(int)`.
- A cyclable button driven by a caller-supplied `IReadOnlyList<string>` of option labels (e.g.
  `["A-Z", "Z-A", "Qty ↓", "Qty ↑"]`) -- clicking cycles to the next label and fires
  `SortOptionCycled(int index)`. `GridControl` never knows what the indices *mean*.
  Click-to-cycle only, per the scoping correction above.
- A toggle button driven by a caller-supplied label (e.g. "Hide Disabled") -- Outset/Inset for
  on/off, the same convention `TabbedContent`'s tab tiles already use for selected/unselected --
  firing `ToggleChanged(bool isOn)`. Toggle button only, per the scoping correction above.
- A search box, backed by `DebouncedTextFilter` (see below) -- firing `SearchFilterChanged(string)`.

Configured via a `Configure(IReadOnlyList<string> sortOptionLabels, string toggleLabel, string
searchGhostText)` call after `CreateElement`, before `Initialize()` -- the same contract
`InventoryManagementWindow.Configure`/`AbilityScoreWindow.Configure` already use.

**`DebouncedTextFilter`** (`Presentation/UI/DebouncedTextFilter.cs`, new) -- extracted from
`TabbedContent`'s existing search-debounce logic (poll `TextBox.OriginalText` each `Update`,
debounce `GameTiming.FramesForSeconds(0.3f)`, fire once text has sat still and isn't already the
applied value). `TabbedContent`'s own tab search refactors onto this; `GridControl`'s item search
becomes its second real consumer, matching this codebase's own "generalize on the second real
consumer" convention (`TabbedContent`, `HoverPopupWindow`, etc. all followed this same path).

**`InventorySortOrder`** (`Presentation/UI/Content/InventorySortOrder.cs`, new enum) --
`NameAscending`/`NameDescending`/`QuantityDescending`/`QuantityAscending`. `NameAscending` is the
default, matching today's existing alphabetical-only behavior exactly.

**`InventoryGridContent.cs`** -- add settable `SortOrder` (`InventorySortOrder`, default
`NameAscending`), `NameFilter` (`string`, default empty, case-insensitive contains), and
`HideDisabled` (`bool`, default false) properties, each triggering `RebuildCells()` when changed.
Add a `VisibleItemCount` (`int`) reflecting how many cells the last rebuild actually produced
(post-filter). Defaults reproduce today's exact behavior bit-for-bit -- this widens
`RebuildCells`'s existing filter+sort step (currently: tag filter, then alphabetical sort) into a
strictly more general version of the same pipeline, not a rewrite.

**`InventoryTabContent`** (`Presentation/UI/Content/InventoryTabContent.cs`, new,
`IElementContent`) -- the Inventory-specific glue connecting the two: composes one `GridControl`
above an `InventoryGridContent`, translating `GridControl`'s generic events into
`InventoryGridContent.SortOrder`/`HideDisabled`/`NameFilter` property sets, and pushing
`VisibleItemCount` back into `GridControl.SetItemCount` after each rebuild. `TabbedContent`'s
`TabDefinition.Content` holds one `InventoryTabContent` per tab now (built by
`InventoryManagementWindow`'s existing `BuildTabDefinitions`), instead of an `InventoryGridContent`
directly -- everything already wired for per-tag tabs (filterTag, hoverPopup, entity id)
continues to flow into the `InventoryGridContent` this class owns internally.

## Deliberately not doing this pass

- **Not genericizing `InventoryGridContent` itself into a reusable `Grid` primitive.**
  `GridControl` is built generic from day one since it's new code with no existing behavior to
  preserve; genericizing the grid means abstracting cell rendering away from items/tags entirely,
  real scope best done once a second real grid consumer (e.g. Magic Menu) actually exists --
  matching how this codebase already prefers to generalize on a second real consumer rather than
  upfront (`TabbedContent`'s own tab list was single-purpose until per-tag tabs needed more).
- **Not a checkbox, not a context-menu sort picker, not weight** -- all three are their own TODO
  items, listed above.

## Phased implementation (matches this project's own "stop after each phase, let the user test
in-game" convention)

1. Extract `DebouncedTextFilter`; refactor `TabbedContent`'s tab search onto it (behavior
   unchanged -- a pure extraction, verify tab search still works exactly as before).
2. Add `SortOrder`/`NameFilter`/`HideDisabled`/`VisibleItemCount` to `InventoryGridContent` --
   no UI yet, defaults only; verify existing tabs still render identically (alphabetical, no
   filter, nothing hidden).
3. Build `GridControl` standalone, unwired to Inventory -- verify count/cycle/toggle/search work
   correctly in isolation (a throwaway test harness or a temporary wiring, whichever is faster).
4. Build `InventoryTabContent`, wire `GridControl` to `InventoryGridContent`, swap it into
   `InventoryManagementWindow.BuildTabDefinitions` in place of the bare `InventoryGridContent`.
   Verify count/sort/toggle/search all work together across a real tab switch.
5. Mark both TODO.md items ("Inventory item sorting, filtering, and searching", "Tab Stats row")
   landed, referencing this file the way other landed-per-plan items do.
