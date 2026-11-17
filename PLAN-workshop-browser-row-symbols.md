# Workshop: symbols for a browser row's markers

Plans the TODO.md entry "Workshop: symbols for a browser row's markers" (High): draw a record's state on its browser
row as small coloured symbols, each with a tooltip of its own, instead of bracketed text that crowds the record's name
out of the 340-wide column.

**Status:** started 2026-11-17 from the TODO entry and a read of the code. Decisions confirmed 2026-11-17: symbols
packed right, the glyphs as proposed (sprites may replace them later), and Phase 2 in scope with chip tooltips. Phase 1
built and checked 2026-11-17 (the row moved into `Browsing/RecordRow`, which `RecordRowTests` drives). Phase 2 built
and checked 2026-11-17 (`GlyphIcon`, `Button.LeadingIcon`, `DropdownOption.Icon`/`TooltipText`, `ListFilterChoice.Icon`;
the chips' remove mark a capital "X") -- complete -- and the "After the last phase" notes written (IMPLEMENTATION-NOTES
"Workshop browser row symbols", CLAUDE.md, TODO.md).

## What the code says today

- `ContentRecordState` (`Builders/Editing`, flags: Unsaved, Draft, Disabled, Deleted, HasErrors, DiffersFromMaster) is
  worked out once, by `ContentEditingTable.StateOf`; the row markers, the Show only filter choices
  (`ContentRecordFilters.ShowOnly`) and the To Publish list all read it. Nothing about that changes.
- `ContentRecordMarkers` turns a state into three things for the row: `Of` (one string: `" *"`, then
  `" [Disabled][Replaced][Draft][Deleted]"` as apply, then `" (!)"`; Replaced only for a disabled record with a
  replacement), `MeaningOf` (a line per marker, the replacement named) and `IsDimmed` (Disabled or Deleted).
- `ContentEditingTab.AppearanceOf` puts them on `RecordRowAppearance` (`Builders/Browsing/RecordBrowserTab.cs`) as
  `Markers` (string), `MarkersMeaning` (string?) and `IsDimmed`. Read-only tabs (Actions, Auras, Map) leave them empty.
- `RecordBrowserTab.CreateRecordRow` builds a row as a pooled `Window` (340 wide less padding, `RowHeight` tall) holding
  an `ItemIconElement` (20 px, at x 4) and one `TextWindow` from x 28 to the end. The name is measured, cut with "..." to
  leave room for the marker string, and drawn as `name + markers` in one colour (`DimmedRowTextColor` when dimmed).
  The row and the name share one tooltip: the full name if cut, then `MarkersMeaning`.
- A pinned row (the selected record the filter or search hides) appends `PinnedNote`, `" (not in the filtered list)"`,
  to the marker string -- 26 characters more of the same column.
- Rows are rebuilt whole (`RebuildRows`) when the list or a record's state changes, never per frame, so building a
  small element per marker costs nothing per frame.
- Clicks on a row's children already reach the row (the icon and name select the record; right-click opens its
  menu): a new child must keep that.
- Tooltips are per element (`Element.TooltipText`); the controller asks the hovered element, the deepest one hit, so a
  child with its own text shows its own over the row's.
- `ItemIconElement` (Presentation) draws a sprite by name or else a glyph through `SpriteOrGlyphRenderer`, sized from
  its box (`GameFontChrome.IconGlyphFontFraction`). `FontService` loads Symbola-Emoji as a fallback, so symbols outside
  DroidSans (⚠, ✎, 📌) draw; the game already relies on it (Burning's 🔥, Poison's ☠).
- `DropdownOption` (the filter menu's rows) has a label, right-hand text and a checkbox, but no icon; `ListFilterChoice`
  has a label only. `ListFilterChips` draws each chip as its label.
- `ContentRecordStateTests` pins the marker string (`Of`), the meaning text and dimming, and checks that the rows
  marked with a marker are the rows the matching filter lists by searching the marker string.

## Decisions

Confirmed 2026-11-17.

1. **One table of symbols, one per marker.** A `ContentRecordMarker` enum in reading order -- Unsaved, Disabled,
   Replaced, Draft, Deleted, HasErrors -- and one `ContentRecordMarkerSymbol` per member (glyph, optional sprite name,
   colour, tooltip text, filter label) in `ContentRecordMarkers`. `ContentRecordMarkers.Of(state, replacementName)`
   returns the markers that apply, in order, each as a `RecordRowMarker` (glyph, sprite name, colour, tooltip); only
   Replaced's tooltip is built per row ("Replaced by Scroll of Healing"). `MeaningOf` goes. DiffersFromMaster stays
   unmarked on the row, as now (the To Publish list is where it shows).
2. **The row holds a list, not a string.** `RecordRowAppearance.Markers` becomes `IReadOnlyList<RecordRowMarker>`
   (empty by default) and `MarkersMeaning` goes. `RecordRowMarker` is the browser's (`Builders/Browsing`), naming no
   content concept, so the read-only tabs can mark rows later (a mod's record) without touching Editing.
3. **An element per symbol, reusing `ItemIconElement`.** Each marker is a pooled `ItemIconElement` child of the row with
   its own `TooltipText` -- the tooltip path, the hit-test and the click-through the icon already has, with no new
   element type and no hit-testing inside one element. Glyphs first; a marker's sprite name is there for art later
   and is null in Phase 1. Alternative: one element drawing every symbol and answering a tooltip per hit sub-rectangle
   -- fewer elements, but tooltips would need a per-point hook `Element` doesn't have.
4. **Right-aligned, packed, in reading order.** Symbols sit at the row's right end, 16 px each with a 2 px gap, packed
   from the right so the last marker is flush; the name takes everything left of them and is cut only past that. With
   nothing marked the name keeps the whole width. Alternative: fixed slots per marker (the same marker always in the
   same column) -- easier to scan down, but six slots reserve ~108 px on every row, most of what this frees.
5. **Coloured by meaning, greyed with the row.** Settled in the Phase 1 check (every glyph is Symbola's; DroidSans has
   none of them, and Symbola has no ⊘ or ⦸):
   - Unsaved: ✎ (U+270E), dark green.
   - Disabled: 🚫 (U+1F6AB), red.
   - Replaced: ↪ (U+21AA), red.
   - Draft: 📄 (U+1F4C4), black.
   - Deleted: ✕ (U+2715), red.
   - Has errors: ⚠ (U+26A0), red.
   - Pinned: 📌 (U+1F4CC), black.
   **A marker another already says is left out**: Replaced says Disabled, and Unsaved stands for Draft. The filters
   still read the state, so Show only > Draft lists an unsaved draft whose row shows only ●.
   A dimmed row draws every symbol blended most of the way to `DimmedRowTextColor`, so a disabled record's symbols
   still tell markers apart but read as part of a greyed row. Colours go in `WindowPalette` only if the framework reads
   them; otherwise beside the symbols in `ContentRecordMarkers`.
6. **The pinned note becomes a symbol too**: 📌 (U+1F4CC), tooltip "Selected; not in the filtered list", always the
   last symbol. It is the browser's (it applies to any tab's records), so `RecordBrowserTab` appends it rather than
   `ContentRecordMarkers`.
7. **Tooltips.** Each symbol: its own text (Disabled: "Disabled: kept in master, never loaded"; Draft: "Draft: not
   published to master"; Deleted: "Deleted: gone once the deletion is published or saved"; Unsaved: "Unsaved
   changes"; Has errors: "Has errors"; Replaced: "Replaced by <name>"). The row and the name: the full name when it is
   cut, else none -- no more list of every marker.
8. **The filter menu and chips show the same symbols** (Phase 2): `DropdownOption` gains an optional leading icon
   (`IconGlyph`, `IconColor` -- UIEngine, naming nothing of the IDE), `ListFilterChoice` an optional icon the button
   passes through and `ListFilterChips` draws before the label; `ContentRecordFilters.ShowOnly` takes each choice's
   icon and label from the one symbol table, so a choice and its marker can never disagree. "Differs from master" has
   no marker and so no icon. The inventory, which may host the filter controls later, needs nothing new: no icon is the
   default.
   - **A chip's tooltip is its symbol's tooltip** (and so is the menu row's): `ListFilterChoice` gains an optional
     tooltip, `ListFilterChips` sets it on the chip, and the symbol table supplies it -- the same text as the row
     symbol, except Replaced, which reads "Replaced: disabled, another record loads in its place" without naming one.
   - **Replaced becomes a Show only choice**, so it has a chip at all: `ContentRecordState.Replaced` (disabled, and a
     replacement exists), worked out in `ContentEditingTable.StateOf` like every other fact, and read by the row marker
     and the filter alike -- `Of` no longer takes the replacement's existence from its argument, only its name.
9. **Tests read markers as values, not text.** `ContentRecordStateTests` checks `Of` returns the right members in
   order and Replaced's tooltip names the replacement; the marked-rows-equal-filtered-rows check compares marker
   members against the filter's state rather than searching a string.

## Phases

### Phase 1: symbols on the row

- `ContentRecordMarker`, the symbol table and the new `Of` in `ContentRecordMarkers`; drop `MeaningOf`.
- `RecordRowMarker` and `RecordRowAppearance.Markers` as a list; `ContentEditingTab.AppearanceOf` fills it.
- `CreateRecordRow`: the name sized to what the symbols leave; an `ItemIconElement` per marker, right-aligned, greyed
  when dimmed, each with its tooltip; the pinned symbol last; the row and name tooltip only the cut name.
- Tests: `ContentRecordStateTests` rewritten (Decision 9); a `Workshop.Tests` case through a real `UiInputController`:
  a long-named disabled draft with errors -- the name is cut, every symbol is inside the row, hovering each shows its
  own text, clicking one selects the record and right-clicking one opens the record's menu.
- In-game check (Workshop, Items tab):
  - Disable an item, then make it a draft and give it an error: expect ⊘ ✎ ⚠ at the row's right end, the name longer
    than before, the row greyed with its symbols still told apart.
  - Hover each symbol: expect its own tooltip; hover the name of a cut row: expect the full name only.
  - Disable an item something replaces: hover ↪, expect "Replaced by <its name>".
  - Edit a record without saving: expect ●; Ctrl+S: expect it gone.
  - Delete a record: expect ✕ and the greyed row; Restore: expect both gone.
  - Check Show only > Draft, select a record, then change the search so it's hidden: expect the pinned row with 📌
    and its tooltip.
  - Click and right-click a symbol: expect the record selected / its menu.
  - Look at each glyph at the Workshop's font size; swap any that reads badly or falls back to a box.

### Phase 2: the same symbols in the filter menu and chips

- `DropdownOption` leading icon, drawn by `Dropdown` in a fixed column before the label (or the checkbox's right) for
  every row when any row has one, so labels stay aligned; `ListFilterChoice` icon; `ListFilterChips` draws it.
- `ListFilterChoice` tooltip; `ListFilterChips` and the menu rows show it.
- `ContentRecordState.Replaced` in `StateOf`; a Replaced Show only choice.
- `ContentRecordFilters.ShowOnly` reads icon, label and tooltip from the symbol table.
- Tests: a dropdown with and without icons keeps its row height and label alignment; `ListFilterButton` passes each
  choice's icon to its option; the Show only labels and tooltips equal the symbol table's; the rows marked Replaced
  are exactly what the Replaced filter lists.
- In-game check: open the Items filter menu -- expect the Show only rows with their symbols, Differs from master
  without one, aligned with the rest; check Draft -- expect the chip to show ✎ and "Draft", and hovering it the row
  symbol's tooltip; check Replaced -- expect only disabled records something replaces, and the chip's tooltip naming
  no record; the Source and Type groups look as before.

## After the last phase

- IMPLEMENTATION-NOTES: under "Workshop browser filters", or a short section of its own, the symbol table as the one
  place a marker's look and words live, and the per-symbol tooltip rule.
- CLAUDE.md: the Workshop paragraph's mention of "the row markers" to say they are symbols with their own tooltips.
- TODO.md: remove the entry.
