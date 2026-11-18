# Workshop: filters on the browser tabs

Plans the TODO.md entry "Workshop: filters on the editing tabs" (Engine, High): a way to narrow a tab's browser to the
drafts, the records with errors, what differs from master, one mod's records, or one kind of record -- beside the
search box, in the 340-wide column the browser has.

**Status:** written 2026-11-15 from the TODO entry's research and a read of the code. Decisions 1-10 were confirmed
the same day. Phases 0-1 built and checked 2026-11-15, the button reading ⛛ (U+26DB, from the Symbola fallback font: Unicode has no funnel; chosen 2026-11-17); Phase 2 built
and checked 2026-11-15; Phase 3 built and checked 2026-11-15; Phase 4 built and checked 2026-11-15 -- complete -- and the
"After the last phase" notes written (IMPLEMENTATION-NOTES "Workshop browser filters", CLAUDE.md, TODO.md).

## What the code says today

- `RecordBrowserTab<TRecord, TPreview>` is in `DungeonCrawlerWorld/Workshop/Builders/Browsing`, not in
  `Cadence/Workshop` as the TODO entry assumes: it draws rows with Presentation's `ItemIconElement`. Items
  (`ContentEditingTab`), Actions, Auras and Map derive from it. The Sprites tab (`Cadence/Workshop/Sprites/SpritesTab`)
  is an `IIdeTab` of its own with no search box at all.
- The browser is a search box (`DebouncedTextFilter`, 0.3 s), an optional New button (56 wide) to its right, and a
  list rebuilt whole by `RebuildRows` from `BuildSections(search)`. Each tab's sections builder does the search match
  itself through `RecordBrowserSections.Build` (name or a tag's display name).
- Everything a State filter needs is already on `ContentRecordDocument<T>` and `ContentEditingTable<T>`: `IsChanged`,
  `IsDraft`, `IsInMaster`, `IsDisabled`, `HasErrors`, `SaveProblems`, `DifferencesFromMaster(types)` (cached per
  `Record.Version`), `Table.IsDeleted`, `Table.IsDeletionUnsaved`, `ModName`. But the rules that turn them into the
  row's markers ("*", "[Draft]", "[Disabled]", "[Deleted]", "(!)") are written inline in
  `ContentEditingTab.AppearanceOf` -- a filter written beside them would be a second copy.
- A mod's records are named through `ContentOrigins.ModOf(table, id)`; read-only tabs have no document, only that.
- The shared `Dropdown` already has what the menu needs: header rows (`DropdownOption.Header`), checkbox rows that
  flip and keep the list open (`IsChecked`), a right-aligned second text per row (`HotkeyText`), disabled rows,
  Up/Down/Enter, Escape and outside-press dismissal. It does **not** refresh the other rows after a flip, which a
  count per row needs.
- `TrySelectRecord` already selects a record the search hides (Error List and Used By navigation), so "selected but
  not listed" exists today and says nothing.
- Per-user settings: `DebugLaunchOptionsStore` writes `%LOCALAPPDATA%/Cadence/Workshop/<game>/launch-options.json`,
  versioned, a missing or unreadable file reading as defaults.

## Decisions

Confirmed 2026-11-15.

1. **Two kinds of group, because the TODO entry's rule and its own check disagree.** The entry says "within a heading
   the choices are either-or" and puts Draft and Has errors under one heading (State), then checks that "Draft + Has
   errors lists exactly the drafts with errors" -- which is both, not either. Resolve it by what a choice is:
   - **Show only** (was State): independent yes/no facts about a record -- Unsaved, Draft, Differs from master, Has
     errors, Disabled, Deleted. Every one that is checked must hold. Draft + Has errors is the drafts with errors.
   - **Value groups** (Source, an item's Type, an action's Timing, a map record's Kind): a record has exactly one
     value, so any checked value matches. Master + ExampleMod is both.
   - Across groups, and with the search text, everything must hold.
   The menu says which is which in the heading ("Show only" / "Source: any of"). Alternative: one rule (either-or
   everywhere) with each State fact as its own heading -- six one-row headings, and no shorter.
2. **Filters are not typed, and never appear as text.** No `is:draft` tokens: the checked choices are the one state,
   the menu and the chips are two views of it, and the search box holds only what was typed -- one line, as today.
   What grows with the number of filters is the chip row (Decision 6), so the search box needs no second line. Typed
   tokens would need a grammar for labels with spaces and mod names, an answer for a mistyped token, and would spend
   the ~220 px the search box has left. Each choice does have a stable text id (`show:draft`,
   `source:mod:ExampleMod`), but only as its name in the saved file (Decision 8) -- it is never shown or typed.
   A typed form is "Workshop filters: field-value queries" (TODO, Medium), which decides then whether the search box
   grows to several lines.
3. **A choice's rule and its row marker are one rule.** A `ContentRecordState` flags value (Unsaved, Draft, Disabled,
   Deleted, HasErrors, DiffersFromMaster), worked out in one place from the document and its table; `AppearanceOf`
   builds the markers from it and the filters test it. So "the rows marked [Draft]" and "the Draft filter" can never
   list different records. Definitions, as the markers are today:
   - Unsaved: `IsChanged`, or a deletion not yet saved. Draft: `IsDraft || !IsInMaster`. Has errors: `HasErrors` or
     any `SaveProblems`. Disabled: `IsDisabled`. Deleted: `Table.IsDeleted`.
   - Differs from master: any field difference, or not in master, or deleted from master -- what a publish would
     write. To hold equal to the bottom panel's To Publish list; if that list's rule differs, one of them moves.
   - A mod's record (read-only) is none of these.
4. **Each choice shows the count it would leave**, in the row's right-hand text: the records that would be listed if
   that row were flipped, given the search text and every other choice as they are. A choice that would leave nothing
   is dimmed and can't be checked; a checked one can always be unchecked. Counts are worked out when the menu opens
   and after each flip, never per frame. The last row is "Clear filters".
5. **A selected record the list doesn't hold stays selected and is pinned above the list**, one row reading its name
   and "(not in the filtered list)". One rule for every way it happens: a filter checked while it is selected; an
   edit, save or publish that makes it stop matching (fix the last error under "Has errors" and the row would
   otherwise vanish mid-edit); New under a filter the new record doesn't match; navigation from the Error List; and
   the search text, which hides the selected row silently today. The inspector, preview and commands are unchanged.
6. **The controls, in 340 px:**
   - A filter button at the right end of the search box (the box narrowed by its width), reading a funnel glyph and
     the number checked when any are. It is a tab stop, opened by click, Space, Enter or Down, as `MultiChoiceField`
     is -- the mouse and the keyboard do the same thing. Whether the font has a usable funnel glyph, or the button
     reads "Filter", is settled by looking at it in Phase 1.
   - Checked choices are chips in a wrapping row between the search box and the list, with Clear last. The row has
     no height while nothing is checked; the list takes what is left. A chip is one button: a click anywhere on it
     removes it (an "x" drawn at its end, tooltip "Remove filter"). No on-but-paused chips (Unreal's pills): that is
     a second state.
   - The browser's title counts what is listed: "Items (4 of 13)" while anything narrows it.
7. **An empty list says why and offers the way out**: one row, "No items match Draft, Has errors" (with the search
   text quoted when there is any) and a Clear filters button.
8. **Kept per user, per game, per tab**: `%LOCALAPPDATA%/Cadence/Workshop/<game>/browser-filters.json` -- a version
   and, per tab title, the checked choice ids. Written on every change. A saved id the tab no longer offers (a mod
   that is gone) is dropped on load, never left as an invisible filter. The search text is not saved, as today.
9. **The filter model and its controls are UIEngine's** (`Cadence/UIEngine/UI/Filtering`), not the IDE's, so the
   game's inventory can host them later: Presentation may not reference `Cadence/Workshop`
   (`WorkshopArchitectureTests`). They name no IDE or game concept -- see "The engine side, and the inventory" below.
   Only the file the Workshop keeps the checked choices in (`BrowserFilterStore`) is `Cadence/Workshop`'s.
   `RecordBrowserTab` hosts the controls and applies the filter to what `BuildSections(search)` returns -- the
   sections' records are the search's result, the filter removes from them and drops emptied headings -- so no tab's
   sections builder changes. A tab offers groups through a virtual (`FilterGroups`, none by default: no button). The
   Sprites tab and "Workshop Error List: columns, severity and filters" host the same controls over their own lists.
10. **Which groups each tab offers:**
    - Every `ContentEditingTab`: Show only (Decision 3) and Source (Master, then each mod that supplied a record of
      the table).
    - Items: Type (Potions, Scrolls, Wands, Consumables, Toggles, Other -- `ItemBrowserSections`' own rule, shared).
    - Actions: Timing (FreeCast, Immediate, Delayed) and Source. Auras: Source. Both gain Show only when they edit.
    - Map: Kind (Area, Palette, Placement) and Source.
    - Sprites: Kind (Options, Linked set, Wall) and Has problems -- with a search box, which it lacks (Phase 4).

Not in this plan:
- **Tags** as a filter: waits for "Workshop tag selection: a popup with checkboxes", which is the picker it would use
  and whose TODO entry now says to add the Tags group here.
- **"Testing selection"**: its own TODO entry adds one Show only choice.
- **Saved filter sets** and **field-value queries**: TODO entries of their own (Engine, Medium).
- **Has warnings**, and **hiding** (everything but Disabled): add when asked for; the model takes either as a choice.
- **Filters in the inventory itself**: this plan only keeps the engine side fit for it.

## The engine side, and the inventory

The inventory's bar is UIEngine's `GridControl` -- one 24-high row: count, sort, toggles, a 112-wide search box --
hosted by Presentation's `InventoryTabContent`, over stacks that change while the game runs. So that the same model
and controls can be added there later without change, the engine side holds to these, and Phase 0's tests hold them:

- **In UIEngine, generic over any item type** (`ListFilterGroup<TItem>`, `ListFilterChoice<TItem>`,
  `ListFilterSelection`), struct or class, with no "record", "draft" or "tab" in a name. Labels, headings and
  tooltips are the host's text.
- **The model holds no items.** It holds groups, predicates and the checked set; the host passes its items when it
  asks what matches or what a flip would leave. A list that changes while the menu is open (loot arriving) tells the
  menu to count again; nothing is counted per frame.
- **Matching one item allocates nothing**, so the inventory can test every stack whenever its grid rebuilds: the
  checked set is a bit per choice, and a predicate is a delegate built once per group.
- **Two controls the host places, not one bar**: the filter button (fits a row beside any search box, its height and
  font given by the host -- `GridControl`'s row is 24 high with its own font) and the chip row (any width, wrapping,
  no height while empty, its height changing through the ordinary layout path, `OnChildLayoutChanged`). The Workshop
  puts the row under its search box; the inventory would put it under `GridControl`.
- **Both are pooled elements** taking `ElementServices`, configured before `Initialize`, and reset fully on reuse.
- **Saving is the host's.** The selection gives and takes its checked ids and raises a change event; the Workshop
  writes a file in `%LOCALAPPDATA%`, a game would keep them with its own player settings.
- **What a hidden selection does is the host's** (Decision 5 is `RecordBrowserTab`'s): the inventory has no selected
  record to pin.
- **Colours are `WindowPalette`'s**, nothing of the Workshop's look.

## Phases

Each phase stops for testing in the Workshop before the next starts.

### Phase 0: the model

`Cadence/UIEngine/UI/Filtering`: `ListFilterGroup<TItem>` (id, heading, all-of or any-of, choices),
`ListFilterChoice<TItem>` (id, label, predicate), `ListFilterSelection` (the checked set, a version, a change event,
its ids out and in), matching, and the count a flip would leave. `Cadence/Workshop`: `BrowserFilterStore`
(Decision 8). `ContentRecordState` in Builders, with `AppearanceOf` rebuilt on it (no visible change). Tests in
`Tests/UIEngine`, over a plain list of a struct type and no Workshop type: all-of and any-of groups alone and
together; counts for checked and unchecked choices; a zero-count choice; the list changing between two counts;
matching allocates nothing. In `Workshop.Tests`: store round trip, a missing file, an unreadable file, an id no
longer offered; the markers of every state unchanged.

Check: tests only, and the Items tab's rows read exactly as before.

### Phase 1: the button, the menu and the filtered list

UIEngine's filter button and its menu (the open `Dropdown` takes a refreshed option list in place, keeping its
position, scroll and highlight -- used for the counts after a flip), hosted by `RecordBrowserTab` and shown in the
Control Gallery tab over a plain list, as the inventory would host it; the list filtered; the title's "4 of 13"; the
empty-list row. Items offers Show only.

Checks:
- Items tab: the filter button sits at the search box's right end; the search box and New still work.
- Open it: a "Show only" heading, six checkbox rows, each with a count; the counts match the rows marked in the list.
- Check Draft: the menu stays open, the list shows only rows reading [Draft], the title reads "Items (n of m)", the
  other rows' counts change, and the button shows 1.
- Also check Has errors: exactly the drafts marked (!). Uncheck Draft: every record with errors.
- A choice that would leave nothing is dimmed and a click does nothing; a checked one always unchecks.
- Type in the search box with a filter on: both narrow the list; the counts follow the search text.
- Check a combination that lists nothing: the row says which filters and Clear filters restores the list.
- Tab from the search box reaches the button; Space opens it; Up/Down/Enter flip rows; Escape closes it.
- With Differs from master on: edit a published item's name -- it appears; type the old name back -- it leaves.

### Phase 2: chips and the pinned selection

UIEngine's chip row and Clear (in the Control Gallery too); the list resized as the row appears, wraps and goes; the pinned row of Decision 5.

Checks:
- No filters: no gap between the search box and the list.
- Check three: three chips and Clear; the count on the button is 3; narrow enough that they wrap, and the list
  starts under the second line.
- Click a chip: it goes, the list widens, the menu's row is unchecked next time it opens. Clear: all go.
- Select a published item, check Draft: the item stays in the inspector and preview, pinned above the list as not
  in the filtered list; uncheck and it is back in its section, selected and in view.
- Under Has errors, fix the selected record's only error: its row moves to the pinned place, the inspector keeps
  focus, nothing else is selected.
- Under Disabled, press New: the new item is selected and pinned.
- Search for text the selected record doesn't match: pinned the same way.
- Double-click an Error List line for a record the filters hide: selected and pinned.

### Phase 3: remembered, Source, and every tab's own groups

The store wired through the shell to each tab; Source; Items' Type; Actions' Timing; Auras; Map's Kind.

Checks:
- Check Draft and ExampleMod's source on Items, quit, start the Workshop: both chips are there and the list is
  filtered; the Actions tab has none.
- Source lists Master and each mod that supplied a record of that table; Master + a mod lists both.
- Source: ExampleMod with Show only: Draft lists nothing, and the Draft row is dimmed first.
- Items' Type: Potions + Scrolls lists two sections; Actions' Timing and Map's Kind the same way.
- Remove the mod's folder, start the Workshop: its chip is gone and the list isn't empty because of it.
- Publish with filters on (the session is rebuilt and the tab reopened): the filters and chips are unchanged.
- `--tab=Items` opens with its saved filters.

### Phase 4: the Sprites tab

A search box (by entry name) and the same control over the Sprites tab's entry list, with Kind and Has problems.
Separable: it can wait for "Workshop: edit sprites" if that reshapes the entry list first.

Checks:
- Search narrows the entries; Has problems lists exactly the entries the Error List names; Kind: Wall lists the walls.
- Its filters are remembered apart from every other tab's.

### After the last phase

IMPLEMENTATION-NOTES "Workshop browser filters" (the two group kinds and why, the one state rule, the pinned
selection, the list for giving a tab a group); CLAUDE.md's Workshop paragraph; the TODO entry removed, and the
entries that name it ("Workshop tag selection", "Workshop testing selection", "Workshop filters: saved filter sets",
"Workshop filters: field-value queries", "Workshop Error List") pointed at `Cadence/UIEngine/UI/Filtering`; `PLAN-workshop-draft-to-master-compare.md`'s deferred filter marked as built here.
