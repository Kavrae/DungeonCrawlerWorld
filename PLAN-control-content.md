# UIEngine: controls hold content, as windows do

Plans the split raised in review of `Button.cs` while building `PLAN-workshop-browser-row-symbols.md`. `Button` has
become three controls switched by flags, and a dropdown row with a checkbox is drawn by a button though it behaves as
a toggle. Separate what makes a control pressable from what it shows, let what it shows be ordinary child elements laid
out as a window's are, and let a menu's rows be built from those same pieces.

Replaces the first draft (`PLAN-button-faces.md`: a fixed `ButtonFace` value and a `DropdownRow` subclass), dropped
after review: a fixed menu of face kinds is the same limit in a new place, and a checkbox row is not a button.

**Status:** written 2026-11-17 from a read of the code and the comparison below. Decisions 1-12 confirmed the same
day. Phase 1 built 2026-11-17 (`ChildGap`, `ChildCrossAlignment`, `FillsRemaining`; `Label`, `IconElement` replacing
`ItemIconElement`), checked. Phase 2 built 2026-11-17 (`PressableElement`; `Button` and `Toggle` hold children; hover,
press and tooltips through the nearest pressable; `Button.IsSelected`), checked. The shorthands are
`Button.SetText`, `Button.SetIcon`, `Button.ShowKeyLabel` and `Toggle.SetLabel` (each adds or updates the child), and
`Toggle.Configure` lost its label and `LabelPosition` (every use was East). `Dropdown` rows and filter
chips drew through `LeftText`, `RightText`, `LeftAlign`, `CheckBoxState`, `LeadingIcon`, `IsHighlighted` and
`IsMarked` until Phase 3 removed them. After Phase 2's check, from review: a selected `Button` (`IsSelected`, the Map
tab's switches) stays inset rather than darkened; the tab tiles (the game's inventory tag tabs, the Workshop's tab
strip and its command tiles, the bottom panel's tabs, the Debug tab's section, Live view and Reports tabs through
`DebugTabElements.CreateTile`) are `Button`s, so they darken on hover, their open tab raised
through `Button.RestingBorderStyle`; the Debug tab's copy buttons are gone (its read-only boxes copy). Phase 3 built 2026-11-17 (menu rows are `Button`s and flat-checkbox `Toggle`s, the menu drawing fills, highlight
and mark behind them, widths from layout; chips and `RecordRow` from `IconElement`s and `Label`s; `Button` lost its row
members), awaiting its check; IMPLEMENTATION-NOTES "Control content" and the CLAUDE.md UIEngine bullet written.

## What the code says today

- `Button` (`Cadence/UIEngine/UI/Button.cs`, sealed, pooled through one factory in `UiEngineContext`): about 20
  creation sites across UIEngine, the Cadence Workshop, the game's Workshop and Presentation, plus 5 title-button
  sites (`Window.AddTitleButton`, placed outside the layout by `PositionInHeader`). `DrawContent` has three branches
  chosen by flags:
  - **Icon**: `ElementOptions.Button.SpriteName` -- a sprite, or `LeftText` as its glyph, centred (Folder, the HUD's
    window buttons, the Debug tab's Copy), with `HotkeyLabel` drawn in a corner over it on the HUD.
  - **Centred glyph**: no `RightText` and not `LeftAlign` -- title buttons and every plain text button.
  - **Row**: `RightText` or `LeftAlign` -- checkbox (`CheckBoxState`), leading icon (`LeadingIcon`), `LeftText`,
    `RightText` right-aligned.
- `CheckBoxState` and `IsMarked` are set only by `Dropdown` rows; `RightText`, `LeftAlign` and `LeadingIcon` only by
  `Dropdown` rows and `ListFilterChips`. `IsHighlighted` means the dropdown's keyboard highlight and, in
  `MapPreviewWindow`, "on" (the grid switch, the chosen layer and neighborhood). Label, sprite and hotkey label come
  through `ElementOptions` at `Build`; the rest are properties reset by hand in `Build`.
- Widths are summed outside the button, copying its row layout: `Dropdown.MeasureWidth` and
  `ListFilterChips.ChipWidth`.
- `Toggle` (6 creation sites) is a separate element drawing its own checkbox and its own label beside it
  (`LabelPosition`), with a different checkbox look from the dropdown row's.
- The element tree already offers the parent a click its child didn't handle (`Element.HandleClick`), which is how a
  click on the name or a symbol selects a browser row (`Builders/Browsing/RecordRow`, already built from child
  elements, each symbol with its own tooltip). Hover and press do not follow: `UiInputController` shows them only on
  the `Button` it hit (`ElementInteraction.Button`, `HoveredButton`, `PressedButton`).
- Tiling is `Floating`, `Horizontal` or `Vertical`, edge to edge, with a per-child `SpacingAfter`; no cross-axis
  alignment, no child that takes the space left.
- `TextWindow` (a whole `Window`) is the only text element; the sprite-or-glyph icon element (`ItemIconElement`) is
  Presentation's.
- `ListView` draws its rows itself ("a list of thousands costs only the rows on screen"); tooltips draw their rows
  through `TooltipRowDrawing`.

## How other frameworks do it

| | Button's content | Checkbox | Inner content and input | Row layout | Menu rows | Long lists |
|---|---|---|---|---|---|---|
| **WPF / Avalonia / WinUI** | `Content`: any element, a string wrapped in a `TextBlock` | `CheckBox : ToggleButton : ButtonBase` -- shares the pressable base, a sibling of `Button` | Routed events bubble; `IsMouseOver`/`IsPressed` hold on the button whatever child is under the pointer; `IsHitTestVisible` | `StackPanel` (`Spacing`), `DockPanel`/`Grid` star columns, alignment | `MenuItem`: `Header` (any), `Icon`, `InputGestureText`, `IsCheckable` | Virtualizing panels |
| **Qt Widgets** | Text and icon only | `QCheckBox : QAbstractButton` | Per widget | Layouts with stretch | `QAction`: checkable, icon, shortcut | Item views with delegates that draw rows |
| **Flutter** | `child`: any widget | `Checkbox` alone; `CheckboxListTile` composes it into a row | One gesture detector wraps the subtree | `Row` with `Spacer`/`Expanded`, axis alignment | Rows of ordinary widgets | `ListView.builder` |
| **SwiftUI** | A label view builder (`Label(text, systemImage:)`) | `Toggle` with any label; inside a `Menu` it renders as a checkmark item | Whole button is the target | `HStack(spacing:)` with `Spacer` | `Button`, `Toggle`, `Picker` composed into a `Menu` | `List`, lazy stacks |
| **HTML/CSS** | Any content | `<input type=checkbox>` in a `<label>` | Events bubble; `:hover`/`:active` apply to ancestors; `pointer-events: none` | Flexbox: `gap`, `align-items`, `margin-left: auto`/`flex-grow` | Ordinary elements | Virtual scrolling by hand |
| **Unity UI Toolkit** | `Button` is a `VisualElement` holding any children | `Toggle` is a field (`BaseField<bool>`), not a button | Events bubble; `pickingMode = Ignore` | Flexbox (Yoga): `flex-grow`, alignment | Elements | `ListView` with `makeItem`/`bindItem`, recycled |

What nearly all agree on:
1. **A button's content is any elements**; text alone is a shorthand wrapped into a text element. Qt is the exception,
   and pays for it with style sheets and delegates.
2. **Pressable is shared, checkable is its own control.** WPF and Qt put the press-and-click behaviour in a base
   (`ButtonBase`, `QAbstractButton`) that `Button` and `CheckBox` both build on; Flutter, SwiftUI and UI Toolkit make
   the toggle a separate control. None makes a checkbox row a button with a checkbox drawn in it.
3. **Hover and press belong to the control, not the leaf under the pointer**: events bubble and the pressed and
   hover states hold on the ancestor control (or the content is made unpickable).
4. **A row is a stack with a gap, cross-axis centring and a filler** that takes the space left, which is what pushes a
   hotkey or a count to the right edge (`Spacer`, `flex-grow`, a star column).
5. **A checkable menu row is a toggle in a menu.** SwiftUI composes it exactly so; WPF and Qt model it as a menu item
   with a check column, which is the same thing kept in one class.
6. **Long lists don't build an element per cell**: they recycle a few (virtualizing panels, `ListView.builder`,
   `makeItem`/`bindItem`) or draw rows themselves (Qt delegates). Composition is for short, bounded content.

## Decisions

Confirmed 2026-11-17.

1. **Pressable is a base; Button and Toggle build on it.** A `PressableElement` (UIEngine, abstract) holds what both
   share: hover, pressed, the click, enabled, focus and keyboard activation. `Button` (an action: bevel, hover overlay)
   and `Toggle` (a choice: holds `IsOn`, flips on click, draws its checkbox) derive from it. `UiInputController`
   tracks hover and press on a `PressableElement` rather than on `Button`. The WPF and Qt shape (point 2).
2. **A pressable's content is its children.** `Button` and `Toggle` become containers: what they show is child
   elements, laid out by the element's tiling like any window's content. `LeftText`, `RightText`, `LeftAlign`,
   `CheckBoxState`, `LeadingIcon`, the sprite branch and `Toggle`'s own label and `LabelPosition` go. A `Toggle`'s
   checkbox is drawn by the toggle, first in its content row; everything after it is its children. (Point 1.)
3. **Hover and press go to the nearest pressable above what was hit**, the rule a click already follows. Content
   children need no flag to pass the pointer through; a child that is itself pressable (none yet) would take it.
   A tooltip still belongs to the deepest element that has one, so a symbol inside a row keeps its own. (Point 3.)
4. **Tiling grows three things** (point 4), all on the existing layout pass (`Measure`/`Arrange`,
   `OnChildLayoutChanged`), never a second layout system:
   - **A gap** between tiled children (`Gap` on the parent; `SpacingAfter` stays for one child's own spacing).
   - **Cross-axis alignment** (`Start`, `Center`, `End`) for a tiled parent's children; a menu row centres them.
   - **A filler**: a child marked to take the main-axis space left (`FillsRemaining`), so trailing text sits at the
     right edge. At most one per parent.
5. **Two small content elements in UIEngine.** `Label` (one line of text: font, colour, cut with "..." to its width,
   measured once when the text changes) and `IconElement` (a sprite by name, else a coloured glyph, through
   `SpriteOrGlyphRenderer`; Presentation's `ItemIconElement` becomes it). Neither is a `Window`, so a row of them
   stays cheap. `TextWindow` stays for multi-line and scrolling text.
6. **Shorthands keep the common cases one line**, as WPF wraps a string `Content` and SwiftUI has `Button("text")`:
   `Button.Text(label)`, `Button.Icon(spriteName, glyph)` and `Toggle.Labelled(label)` -- each just adds the children.
   The HUD's key label is a `Label` child placed in the corner (`Floating`), over the icon.
7. **A menu row is an ordinary control; the menu draws which row is current.** `Dropdown` builds each option as a
   `Button` (an action) or, for a checkbox option, a `Toggle` -- holding an optional icon, the label, a filler and the
   hotkey or count. The keyboard highlight and the "current value" mark are the menu's knowledge, so `Dropdown` draws
   them behind the rows; `IsHighlighted` and `IsMarked` leave `Button`. `DropdownOption` stays the menu's data (label,
   icon, right-hand text, tooltip, checked). The SwiftUI shape (point 5); no `DropdownRow` class.
8. **"On" gets its own name.** The Map tab's switches move from `IsHighlighted` to `Button.IsSelected`, drawn as today.
9. **Widths come from layout.** `Dropdown.MeasureWidth` and `ListFilterChips.ChipWidth` go: a row's width is its
   `Measure`, so the row layout lives in one place.
10. **Where it is not used** (point 6): `ListView`, tooltip rows and anything that can list thousands keep drawing
    their own rows. A bounded list -- a menu, the chips, a browser's rows -- builds elements. Recycling rows
    (`makeItem`/`bindItem`) is not part of this plan.
11. **Other controls adopt it as they are touched, in this plan where they are small:** the chips (icon, label, "X"),
    title buttons (a centred glyph `Label`), Folder's header icon, the HUD buttons, `FieldRow`'s parts, and
    `RecordRow` (whose icon and symbols become `IconElement`s). Larger rewrites wait for their own work.
12. **No look changes.** Every button, toggle, row and chip draws as it does now (the two checkbox looks included --
    the menu's flat box is a `Toggle` style), and each phase's check compares against today.

## Phases

### Phase 1: the layout and content pieces

- Tiling gap, cross-axis alignment and the filler; `Label` and `IconElement` (Presentation's `ItemIconElement`
  replaced by it).
- Tests: a horizontal parent with a gap, centred children of mixed heights and a filler places each child where
  expected and re-lays out when one child's text changes (`OnChildLayoutChanged`); `Label` cuts with "..." and measures
  once per change.
- Check: nothing visible changes yet beyond `ItemIconElement`'s users (Item Details, the Workshop's browser rows and
  inspector icons), which look as before.

### Phase 2: pressable, Button and Toggle hold content

- `PressableElement`; `Button` and `Toggle` derive from it and hold children; the input controller's hover and press go
  to the nearest pressable; the shorthands.
- Every `Button` and `Toggle` creation site and title button moves to children or a shorthand; the HUD key label
  becomes a corner `Label`; `Button.IsSelected` for the Map tab.
- Tests: hovering and pressing a label inside a button lights and presses the button and clicks it on release; a
  tooltip on a child still shows; a disabled button's children take nothing; `Toggle` flips from a click on its label;
  the existing button, toggle, title-bar and HUD tests read children instead of `LeftText`/`RightText`.
- Check, game and Workshop: title buttons, the HUD's three window buttons with their keys, the inventory and trade
  buttons, the Workshop command bar, the Debug tab's Copy and its feature checkboxes, the Map tab's switches -- each
  looks and responds as before.

### Phase 3: menus and chips from the same pieces

- `Dropdown` builds rows as `Button`s and `Toggle`s with icon, label, filler and right-hand text, and draws the
  highlight and mark behind them; `CheckBoxState`, `IsMarked`, `IsHighlighted`, `LeadingIcon` and `MeasureWidth` go.
- `ListFilterChips` builds each chip from an `IconElement`, a `Label` and the "X" `Label`; `ChipWidth` goes.
- `RecordRow` uses `IconElement` and `Label`.
- Tests: the dropdown and filter tests read rows as controls (a checkbox option is a `Toggle`, `IsOn` its state); Up/Down
  moves the drawn highlight; a dropdown field's current value is marked; chips keep their icon, tooltip and width
  rule.
- Check: a context menu with hotkeys, a dropdown field, the filter menu (checkboxes, icons, counts, Clear filters) and
  its chips, and the Items browser rows -- each looks and responds as before.

## After the last phase

- IMPLEMENTATION-NOTES: a section on control content -- pressable as the base, content as children, hover and press to
  the nearest pressable, the tiling gap, alignment and filler, and where composition is not used (long lists).
- CLAUDE.md: the UIEngine bullet's list of controls (`PressableElement`, `Label`, `IconElement`, the tiling additions)
  and the input rule.
