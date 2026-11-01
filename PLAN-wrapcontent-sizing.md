# WrapContent sizing: one cap, one constraint, one place to measure from

(From TODO.md "WrapContent parent sizing collapses when a child resizes itself after attach". Investigated 2026-11-01.
A diagnosis, then phases. Decisions are at the end; one of them still needs confirming.)

## Verdict in one paragraph

The collapse is real, but the TODO's account of it is out of date: the code moved from `Window.Measure` to
`Element.Measure`, and a child shrinks to its WrapContent parent's title width, not to (0,0). It comes from three
defects, plus a fourth caused by the same mix-up. **`_geometry.MaximumSize` means two things**: the cap an author set,
and the space the last measure offered. `Measure` writes the second over the first on every pass. **A child that
measures itself asks a WrapContent parent for its `ContentSize`**, a value that is only ever derived from that same
child. **`TextBox` writes its clamped width back as its authored size.** The fix is to keep the cap and the constraint
in separate fields, and to have a child of a WrapContent parent measure from the nearest ancestor whose size doesn't
depend on it. Both changes stay inside `Element`, and they let the workarounds around them be deleted (the quest
composer's Fixed popup, `TextBox`'s parent poke, and the `SetMaximumSize` calls and comments that exist only to survive
the overwrite).

## How it works today

### Measure (`Cadence/UIEngine/UI/Element.cs`)
- `Build` sets `_geometry.MaximumSize = Layout.MaximumSize ?? _parent.ContentSize ?? Layout.Size ?? (0,0)`
  (Element.cs:451). A child with no cap therefore inherits whatever its parent's content size was when the child was
  built, and a root window with no cap is capped at its starting size.
- `MeasureAndArrange` (Element.cs:1507) is the entry point for every self-started relayout: `Initialize`, `SetSize`,
  `SetBounds`, `SetRelativePosition`, `SetDisplayMode`, and the parent call in `SetIsVisible`. A root measures against
  its own `MaximumSize`. A child measures against `ComputeChildAvailableSize(_parent, this, _parent.ContentSize)`.
- `Measure(availableSize)` begins with `_geometry.MaximumSize = availableSize` (Element.cs:1570). Fixed, Fill and
  Minimized all clamp to that value.
- A WrapContent element measures its children first and passes its **own** available size down to them unchanged
  (Element.cs:1632). It doesn't subtract its border, header, padding or scrollbar gutter, and the base
  `RecalculateWrapContentSize` never clamps its own size to anything. (`TextWindow`'s override does clamp.)
- `ComputeChildAvailableSize` gives a scrollable parent's child its own `_geometry.MaximumSize` on the scrolling axis.
  It is meant to be the authored cap, but by then it holds the previous pass's constraint.
- `AddChild`/`RemoveChild` re-measure a WrapContent parent, deferred while a `BeginLayoutBatch` is open. Nothing
  re-measures it when a child that is already attached resizes itself. `TextBox.AutoSizeToContent` works around that
  by calling `ParentElement.MeasureAndArrange()` itself.

### Reproduction (probe test, since deleted)
Root WrapContent window, title "New Quest", max 800×600. Child: multiline `TextBox`, Size = MaximumSize = 400×190.
Four lines typed.

| Step | TextBox size | TextBox max | Popup size |
|---|---|---|---|
| After it's built | 400×190 | 400×190 | 91×32 |
| Its own first measure (`Initialize`) | 89×8 | 89×8 | 91×32 |
| After `AddChild` | 89×36 | 800×600 | 99×68 |
| After each new line | grows 8 px, then 6 more px (two `Resized` events) | 800×600 | follows |

### The four defects
1. **The cap is overwritten.** The authored 190 is gone after the first measure, and the box can grow to the popup's
   600.
2. **The constraint depends on who starts the measure.** When the child measures itself, it is bounded by the parent's
   current `ContentSize`, which is its own previous size. When the parent's pass measures it, it is bounded by the
   parent's available size. Each new line therefore resizes the box twice, first clamped to the old size and then
   corrected.
3. **A clamped width becomes permanent.** `AutoSizeToContent` calls `SetSize(new Vector2(CurrentSize.X, h))`
   (TextBox.cs:1179) just after the clamped first measure, so 89 becomes `OriginalSize.X`.
4. **`RelativePosition` is subtracted twice for a child `TextWindow`.** `TextWindow.RecalculateWrapContentSize`
   (TextWindow.cs:89-94) subtracts it from `MaximumSize`, assuming the value is still the parent's content size from
   build time. `ComputeChildAvailableSize` has already subtracted it, so a child `TextWindow` that isn't at (0,0)
   wraps narrower and shorter than the space it has.

### Code that exists because of the overwrite
- Every caller that sets a cap only so the overwrite or the build-time fallback doesn't hurt:
  - `AbilityScoreWindow` (222-246) and `TradeWindow` (180-191), each calling `SetMaximumSize`.
  - The doc comments on `PopupChrome.DropdownMaximumSize` and `NotificationChrome` (16-19).
  - `UnboundedChildHeight`/`UnboundedRowHeight` caps on scrolled rows in `InspectionWindowContent`,
    `ActivatableDetailsSections` and `HealthWindow`.
- Comments explaining it as a fact: `Dropdown` (13, 86), `Tooltip` (28-35), `GridControl` (66), `ItemDetailsWindow`
  (46), `ItemDetailsWindowController` (139), `HealthWindow` (251), `ItemComparisonController` (261).
- The quest composer (`ShellBootstrapper.OpenQuestComposer`, TEMPORARY demo, kept) is Fixed and resizes itself from the
  box's `Resized` event. `TextBoxTests.FixedParent_ExplicitlyResizedOffTextBoxResizedEvent_ShrinksThenGrowsWithIt`
  pins that pattern, and its doc comment repeats the old explanation.

## Phase 1 -- Separate the cap from the constraint

*Built 2026-11-01; unit tests pass (`ElementMaximumSizeTests`); in-game check pending.* `EffectiveMaximumSize` is a
root's `MaximumSize` itself, so a root follows `SetMaximumSize` and a resized screen without waiting for a re-measure.
The "follows a changed screen size" check needs a graphics device, so it is covered in the Workshop, not by a unit
test.

- `ElementGeometryState` gets two fields:
  - `MaximumSize`: the authored cap. Only `Layout.MaximumSize` and `SetMaximumSize` write it. It is nullable, and
    null means "not set".
  - `AvailableSize`: the constraint from the last measure. Only `Measure` writes it.
- An element with no authored cap is capped at the screen. The screen is the back buffer of the game window, which
  the Workshop can resize.
  - `UiRenderService.ScreenSize` returns the back buffer's size from the device (`PresentationParameters`).
    `UiRenderService` already owns the device, and every element reaches it as `Render`. With no device (a headless
    test), there is no screen and no cap from it.
  - `Element.MaximumSize` returns the authored cap, or `Render.ScreenSize` when none is set. It is never stored, so it
    follows a resized game window with no copy to update.
  - A child is already bounded by its parent, so the screen default only matters for a root, and for a child on its
    scrolling axis. A scrolled row that can be taller than a screen keeps an explicit cap
    (`UnboundedChildHeight`/`UnboundedRowHeight`).
- After the back buffer changes, the host re-measures every root in its `UiLayerStack`
  (`UiLayerStack.MeasureAndArrangeRoots`), so roots shrink to a smaller screen. The Workshop calls it from
  `WorkshopShell.Resize`. The game window can't be resized today, so GameShell has nothing to call.
- `Element` exposes both. It also exposes `EffectiveMaximumSize = Vector2.Min(MaximumSize, AvailableSize)`, and every
  clamp reads that:
  - Fixed, Fill and Minimized in `Element`.
  - `Window.ApplyMinimizedSize` (Window.cs:449).
  - `TextWindow`'s wrap width and height.
  - `Tooltip`'s fixed width and its `_laidOutMaximumSize` cache.
  - `UiInputController.ComputeResize`.
  - `MapPreviewWindow.Rebuild`'s overflow check.
- `Build` loses both fallbacks, so there is no copy of the parent's `ContentSize` and no `Layout.Size` ceiling.
- A root measures against its `MaximumSize`, which is the screen unless an author set a cap.
- `ComputeChildAvailableSize` reads the child's `MaximumSize` on a scrolling axis: its authored cap, else the screen.
- `Measure` stops writing `MaximumSize`.
- `TextWindow` drops its `RelativePosition` subtraction (defect 4).

**Behaviour changes to check:**
- Root windows created without a cap can now grow up to the screen instead of stopping at their starting size: Item
  Details, comparison columns, notifications, the trade window and the ability score window.
- The five resizable root windows can be dragged up to the screen size: inventory, health, ability scores, loot box
  results and diagnostics.
- `AbilityScoreWindow` and `TradeWindow` call `SetMaximumSize` only to get past the starting-size ceiling. Once it is
  gone, those calls are no longer needed. Phase 4 removes them.

**Test checklist:**
- Unit test: a child's authored `MaximumSize` is unchanged after `AddChild`, `SetSize` on its parent, and a scroll.
- Unit test: a root with no authored cap reports the screen as its `MaximumSize`, and follows a changed screen size
  after `MeasureAndArrangeRoots`.
- Unit test: a WrapContent `TextWindow` at (20, 30) inside a Fixed parent wraps at `parent.ContentSize - (20, 30)`, not
  20/30 narrower and shorter than that.
- In game: open Inventory, Health, Ability Scores, Loot Box Results and F3 Diagnostics, and resize each by every edge
  and corner. Each should grow until it reaches the screen size, and shrink to its minimum as before.
- In the Workshop: shrink the window with a WrapContent window open (for example the Items tab's details window). The
  window should shrink with the screen; enlarging the screen again should let it grow back.
- In game: hover inventory cells, ability scores and hotbar slots. Tooltips should be the same width as before. The
  hotbar summary keeps its fixed width.
- In game: right-click a corpse and a TextBox, and open a DropdownField. Rows should be full width and a long list
  should scroll.
- In game: Item Details, a comparison and the trade window should look as they do now.

## Phase 2 -- Measure from the layout root

*Built 2026-11-01; unit tests pass (`WrapContentLayoutTests`); in-game check pending.* Built slightly differently from
the bullets below: an element still measures itself first, against exactly what its parent's pass would offer
(`ContentSizeOfferedToChildren`), and then a WrapContent parent is re-fitted, recursively up every WrapContent
ancestor, each deferred to a layout batch open on it. That keeps a child's size readable straight after `AddChild` or
`SetSize` inside a batch, where measuring only from the top would leave it at its build-time size until the batch
ends. `AddChild`'s separate WrapContent re-fit is gone (the child's `Initialize` does it). `TextBox` compares against
`OriginalSize.Y`, so a box at its cap no longer calls `SetSize` on every keystroke.

- `MeasureAndArrange` walks up while the parent is WrapContent. If it reaches a Minimized parent, it stops and
  measures nothing, as `Initialize` already does. Otherwise it measures and arranges that ancestor (the **layout
  root**), so the child gets exactly the constraint the parent's own pass would give it.
- If the layout root has a layout batch open, the measure is deferred to the batch's end, as `AddChild` defers it
  today.
- With that in place, `Initialize`'s first measure of a child in a WrapContent parent is the parent's measure. The
  WrapContent re-fit in `AddChild`/`RemoveChild` becomes the same call, and `SetIsVisible`'s `_parent.MeasureAndArrange()`
  goes through the same route.
- `TextBox.AutoSizeToContent`:
  - Drop the `ParentElement.MeasureAndArrange()` poke.
  - Keep the authored width (`SetSize(new Vector2(OriginalSize.X, h))`, defect 3). This needs an `OriginalSize` getter,
    or a `SetHeight` that leaves the width alone.

**Test checklist:**
- Unit test: a WrapContent popup holding a multiline `TextBox` (the reproduction above) keeps its 400 width. The box
  starts at two lines, grows one line per new line with **one** `Resized` per change, and stops at 190.
- Unit test: a child's `SetSize` inside a WrapContent element inside another WrapContent element re-fits both.
- Unit test: ten `SetSize` calls on children inside one `BeginLayoutBatch` on the layout root measure it once.
- In game: type multiple lines in the quest composer. It should behave as before (it is still Fixed until Phase 4).
- In game: expand and collapse the Notifications folder with several notifications queued. It should look as it does
  now.

## Phase 3 -- A WrapContent element keeps to its own cap

*Built 2026-11-01; unit tests pass (`WrapContentLayoutTests`); in-game check pending.* One addition beyond the bullets
below: a WrapContent element's `ContentSize` now means what it means for Fixed and Fill, the area its children
occupy, with padding and scrollbars outside it. Before, it included the padding on both sides, so its content
rectangle (the children's clip) ran past its background by the padding, and the overflow it scrolled was short by
twice the padding. Its outer size is unchanged. Anything that sizes itself from a WrapContent parent's `ContentSize`
now gets twice the padding less.

Found in Phase 3's in-game check: an element that resized itself never told a parent that tiles its children, so the
siblings after it kept their old positions until something re-arranged the parent (scrolling did). The Inspection
window's component dump showed it whenever a component's text changed height. `TextWindow.RelayoutText` resized the
window on the side, with no `Resized` and no parent notified at all; it is now a `MeasureAndArrange`. Also,
`MeasureAndArrange` now re-tiles a non-WrapContent parent that tiles its children whenever the element's size
changed.

Then consolidated (2026-11-01, after an audit of every size, position and visibility write). A parent now reacts to a
child in one place, `Element.OnChildLayoutChanged`:
- **What it does:** a WrapContent element re-fits, deferred to a layout batch (with tiled children re-tiled at once).
  Any other element re-tiles if it tiles its children, then recalculates its scroll range.
- **Who calls it:** `MeasureAndArrange` (when the element's size changed, or it was attached or moved since),
  `RemoveChild` and the `IsVisible` setter.

This replaced five copies of that logic: `AddChild`'s and `RemoveChild`'s own tails, the per-child `Resized`
scroll-bounds subscription, the unused `SetIsVisible`, and `MeasureAndArrange`'s inline re-fit/re-tile.

Two gaps closed:
- Showing or hiding a child now re-lays out its parent; shown again, the child is measured first.
- Moving a child inside a scrolling parent now updates its scroll range.

Wrap-around and scroll range share one `ChildrenExtent`. For tiled children it uses where `RetileChildren` will put
them, not where the last `Arrange` left them, so a tiled WrapContent element fits children whose size changed in its
own measure. A scrolling element recalculates its scroll range on every measure.

Chrome setters (`ContentPadding`, `CanUserScroll*`, `ScrollbarVisibility`, `SetMinimumSize`/`SetMaximumSize`) still
re-lay out nothing; their callers lay out afterwards.

- A WrapContent element offers its children its effective maximum, minus its border, header, padding and gutter, and
  clamps its own size to its effective maximum.
- Today it passes its whole available size down unchanged, so the base WrapContent path can exceed its cap by the size
  of its own chrome. `TextWindow` is unaffected; it already clamps itself.

**Test checklist:**
- Unit test: a WrapContent window with a 200×100 cap whose child asks for 500×500 measures as 200×100, with the child
  inside its content area.
- In game, check each WrapContent window: Item Details with a long description, a comparison column, the notification
  folder, the Workshop record browser's preview, details and related windows, and the Inspection window's blocks. Each
  should stop at its cap. Where content is allowed to scroll, it should scroll there instead of spilling past the
  border.

## Phase 4 -- Remove the workarounds

*Built 2026-11-01; in-game check pending.*
- **Caps removed:**
  - The `SetMaximumSize` calls in `AbilityScoreWindow`, `PlayerHealthBarContent` and `TradeWindow`; none of
    those windows authors a cap, so the screen default does what they did.
  - The large height caps on Fixed rows in the Health, Inspection and details windows, whose size is given.
- **Cap renamed:** the large cap on wrapping text rows, which can be taller than the screen, is now
  `MaximumTextRowHeight`.
- **Docs kept, corrected:** `DropdownMaximumSize`, `FolderMaximumSize` and `HotbarContent.MaximumSize` stay as
  real caps; only their docs were wrong.
- **Bookkeeping:** stale comments rewritten, the TODO entry deleted, IMPLEMENTATION-NOTES and CLAUDE.md updated.

- ~~The quest composer becomes WrapContent and loses its `Resized` handler and comment.~~ Done early, during Phase 2
  testing (2026-11-01). Its workaround resized the Fixed popup to the box's height plus a hard-coded 30 px of chrome,
  but the real chrome is 32 px. Whenever the box was clamped, each `Resized` shrank the popup by 2 px and clamped it
  again, down to just the title bar. A headless probe of the last commit collapses the same way, so the workaround was
  fragile, not something Phase 2 broke on purpose. It stays a TEMPORARY demo, per the standing decision to keep it.
- Rewrite the `TextBoxTests` Fixed-parent test's doc comment. The pattern is still valid, but the reason it gives is not.
- Delete the `SetMaximumSize` calls and comments listed under "Code that exists because of the overwrite". A call
  stays only where it states a real cap. Keep the `UnboundedChildHeight`/`UnboundedRowHeight` caps where a row can be
  taller than a screen, and rename them to say that.
- Delete the TODO entry. Add a short IMPLEMENTATION-NOTES entry, and update the UIEngine bullet in CLAUDE.md to say
  that `MaximumSize` is authored only, and that a child of a WrapContent element measures from its layout root.

**Test checklist:**
- In game: the quest composer should open at two lines and 400 wide, grow as you type, and scroll after its cap.
  Submitting should still post the Quest notification.
- Repeat the in-game checks from Phases 1 and 3 once, since this phase removes the code that was patching around the
  old behaviour.
- Run `dotnet test Tests/Tests.csproj` and `dotnet test Workshop.Tests/Workshop.Tests.csproj` (the Workshop's tabs use
  `SetMaximumSize` heavily).

## Not in scope

**Choosing good size limits for each window is separate work, and this plan doesn't replace it.** That work is TODO.md
"Global hard minimum/maximum element sizes, and horizontally scrolling title text": a global hard minimum and maximum
that every resize obeys, with each window setting its own narrower limits for a better experience. This plan only
makes sure an authored cap is never lost, and that an element with no cap stops at the screen rather than at its
starting size or at a stale parent size. The screen is a fallback, not a chosen limit. When that TODO gives a window
its own limits, they take precedence through the authored `MaximumSize` exactly as any cap does here. That TODO entry
stays open.

## Decisions

1. **An element with no authored cap is capped at the screen** (the game window's back buffer), and follows it when
   the window is resized. Decided 2026-11-01. *Interpretation to confirm: "the window size" here means the game
   window, not each UI window's own starting size.*
2. **Phase 3 is part of this plan.** Decided 2026-11-01.
3. **There is no "unbounded" value to store.** Decision 1 removes the question: a cap is either authored or the screen.
   The one exception is a headless test with no device, where `ScreenSize` reports no limit; that stays inside
   `UiRenderService`.
