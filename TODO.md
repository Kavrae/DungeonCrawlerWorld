# Long-Term TODOs

Non-urgent architectural items worth revisiting later -- things noticed in passing that don't block current work, not a sprint backlog. Organized by layer (Engine, Game, Presentation, Global -- cross-cutting items that don't belong to a single layer), each split into High/Low priority.

## Engine

### High Priority

#### Inventory system

Infinite storage per entity. The player character will carry a large amount (hundreds of items); NPCs will carry very little each (a dozen items or so). Items have persisted state within an inventory slot rather than always reverting to their starting values -- e.g. a partially consumed potion stays partially consumed while sitting in inventory, rather than resetting.

### Low Priority

#### Equipment

Engine-side equipment support (slots, equip/unequip mechanics). Companion to the Game-layer equipment rules and the Presentation-layer equipment menu below.

#### Achievement center

Tracks achievement progress and fires the notification system when one completes. Needs its own `AchievementNotification` type.

#### Generic status-effect system

The occupancy markers (`NonBlockingComponent`/`ForceBlockingComponent`) are `MultiComponentPool`-backed "many independent sources, count-based" components -- a reasonable, low-risk special case for exactly two boolean occupancy questions. Do **not** copy that pattern (a bespoke marker-component type plus a hand-written precedence function) for every future buff/debuff once real effect variety shows up (dozens of effects on the player character, overlapping sources assumed throughout) -- it doesn't scale:

- Most real buffs carry data (magnitude, remaining duration, source), not just presence -- Burning already needs this (see its own TODO item under Game below: damage decreases over time, stacks increase both).
- Precedence/interaction relationships between many effect types multiply, not add, and become impossible to audit as N separate "if pool A has, else if pool B has" checks hand-written and scattered across whichever system happens to care about each one.

When that need arrives: build a generic "active effect" record (`EffectType` id/enum, `SourceEntityId`, `RemainingDuration`, `Magnitude`), still `MultiComponentPool`-backed since overlapping sources/stacking is the same requirement either way, with a small number of *derived-question* functions (`IsBlocking`, `IsStunned`, `GetSpeedMultiplier`, etc.) each independently scanning the relevant subset of active effects and applying its own precedence rule -- structurally the same shape `IsBlocking` already is, just generalized from two dedicated pools to filtering one shared effects table.

Worth revisiting sooner rather than later given the new melee-attack item (under Game, High) explicitly wants status effects applicable to entities without hit points -- that's exactly the kind of effect this system is meant to generalize.

#### Additive and multiplicative bonuses

A general way to combine multiple simultaneous stat modifiers -- from equipment, buffs/debuffs, race, class, etc. -- needs both additive (flat `+N`) and multiplicative (`xN%`) stacking, with a defined, fixed order of operations: conventionally, sum every additive modifier onto the base value first, then apply every multiplicative modifier to that sum -- not interleaved in whatever order modifiers happen to be applied, which would make the same modifier set produce different results depending on ordering (confusing for a player to reason about, and hard to write a test for).

Directly related to the generic status-effect system above -- `Magnitude` there is exactly this kind of value, and the derived-question functions it proposes (`GetSpeedMultiplier`, etc.) are where additive/multiplicative combination would actually get applied. Also directly relevant to the new stats items under Game/Presentation (stat points, buffs/debuffs) and equipment above.

#### Explore the C# `Span<T>` structure for component storage

Component pools (`DirectComponentPool<T>`/`PackedComponentPool<T>`/`MultiComponentPool<T>`, `Engine/ECS/Components/Stores`) are hot-path -- called every frame, per striped system (see `SystemManager`/`EntityStripeSet` in CLAUDE.md's ECS notes). Worth spiking whether exposing pool data as `Span<T>`/`ReadOnlySpan<T>` (bulk contiguous access, no per-element bounds-check/indirection, no allocation) is a meaningful win over the current per-entity-id indexed access pattern, particularly for systems that process most or all of a pool's population rather than a scattered subset.

Explore before committing -- this is a profiling question (does indexed access actually show up as a real bottleneck anywhere) as much as an API design one; not worth restructuring the pools around until there's a measured case for it.

#### Distance-based processing ("onion" processing)

Complementary idea to the existing entity-striping scheme (`SystemManager` processes `Count`/`StripeCount` of a system's population per frame, round-robin regardless of position). Instead of, or in addition to, pure round-robin striping, bucket entities into concentric distance-from-player rings ("onions") and process outer rings less frequently or at reduced fidelity compared to the innermost ring.

Depends on a real player-character position existing to measure distance from (see Player character item under Game) -- doesn't make much sense against the camera-is-the-viewpoint model there is today, since the camera can scroll anywhere independent of any single reference point. Related to the Movement System efficiency-update item under Game -- likely the same investigation; whichever gets tackled first should consider the other.

## Game

### High Priority

#### Inventory management rules

Item interactions, storage rules, restricted items, etc. Governs how the Engine-layer inventory system above is actually used -- what can stack, what can't be picked up by whom, interactions between items, and similar rules.

#### Melee attack implementation

For NPCs and the player. Attacking uses energy, creating a tactical decision between moving more vs. attacking more. Can target any entity one tile away that has physical collision -- even entities without hit points, since this allows status effects to be applied to otherwise-immortal entities. Depends on the Engine-layer generic status-effect system above for the "immortal but affectable" case, and on the player-character item below for the player's side of it.

#### Player character with movement replacing map scrolling

Today `MapWindow.OnHotkeysAction` (invoked by `GameInputController.RouteHotkeysToFocusedWindow` while the map window holds focus) wires `W`/`A`/`S`/`D` directly to `UpdateScrollPosition` (pans the camera) and `PageUp`/`PageDown` to `ChangeLayer` (changes which layer is viewed) -- there is no player-controlled entity anywhere; every entity's position only ever changes via the Movement module/system, never directly from input.

Wanted: a real player character entity that `W`/`A`/`S`/`D` actually moves -- through the same Movement system every other entity uses (see the `SeekTarget` item below, which may share machinery), not a special-cased direct position write, so it interacts with blocking/collision/etc. the same as anything else. The camera should then follow the character's position/layer automatically instead of being the thing `W`/`A`/`S`/`D` directly controls.

- Shift + move keeps today's behavior (independent camera scroll, decoupled from the character) for free-look.
- Space snaps the camera back to the character's current position and layer -- needed because Shift-scrolling or `PageUp`/`PageDown` (viewing a different layer than the character occupies) can leave the camera arbitrarily far from the character, with no way back except manually scrolling/paging back.

Promoted to High: the melee attack item above ("attack any entity 1 tile away") and the player attack button/key item under Presentation both assume a real player-controlled entity with a map position already exists.

Affected: `Presentation/UI/MapWindow.cs` (current `W`/`A`/`S`/`D`/`PageUp`/`PageDown` wiring in `OnHotkeysAction`; `UpdateScrollPosition`/`ChangeLayer` are camera-only today and would need a "center on position" method), `Game/Modules/Movement` (the actual movement system a player-controlled entity should route through).

### Low Priority

#### Show runner race

Randomly selected. Affects UI appearance, and gives a bias towards selected quests and enemy types.

#### End of level staircase

Game-side logic for descending/ascending a level. See the matching Presentation item below for the visual/interaction side.

#### Random map generation v1

#### Equipment

Game-side equipment rules (what can go in which slot, stat effects of equipping). Companion to the Engine-layer equipment item above and the Presentation-layer equipment menu below.

#### Stats

Randomly generated starting stats within a range. Increases can be automatic (level-up) or player-chosen (spending stat points). See the matching Presentation stats window item below.

#### Experience and level up system

Defeat enemies to get experience points. Each class gets different stats, abilities, spells, and other benefits on level up. The default Engineer class gives simple level-up stat boosts and abilities as a proof-of-concept.

#### Burning status effect from touching lava

- Damage over time
- Damage decreases over time
- Goes away when damage hits 0
- Can stack to increase damage and duration (so multiplicatively worse)
- Gets worse for each movement the entity ends in lava

#### Body parts

- Plan first
- Use multi-components somehow
- Position matters -- e.g. lava should damage feet first

#### Movement System

- `SeekTarget` movement mode
- Efficiency update

## Presentation

### High Priority

#### Inventory management

Tabs, sorting, click-and-drag organization, icons, click-to-inspect. Depends on the Standard widget set item below for list/tab-style controls, and on the Engine inventory system and Game inventory rules above for the data it's displaying.

#### Inventory and spell hotbar

#### Context menu / mouse button coverage

Right-click dropdown of options. `GameInputController` today only ever reads `MouseState.LeftButton` -- no right-click, middle-click, or double-click detection exists anywhere, so building this needs that mouse-button coverage added first (also enables incidental wins like double-click-title-bar-to-maximize).

#### Player stats v1

Persisted view of the player's active stats. Always shows the same fixed set of important stats.

#### Player attack button or key

A button or key for attacking, distinct from the hotbar -- needs to be available outside the hotbar but usable more quickly than going through the context menu. Determine the best UI treatment for this class of "common interaction that should always be quickly accessible." Depends on the player-character item under Game.

#### Standard widget set

The entire control set today is `Window`, `TextWindow`, `Button`, and `MapWindow` -- no checkbox, radio button, dropdown/combo box, slider, list box, or tree view. Previously "build once something list-heavy needs one" -- that need has now arrived: inventory management, the inventory/spell hotbar, the equipment menu, and the stats window above/below all want list- or grid-like controls that don't exist yet.

#### Text input

No editable text control exists -- `TextWindow` only ever displays text, never accepts it. Needed for anything resembling a settings screen, chat/console input, search/filter boxes, etc.

Focus (`Window.IsFocused`, `GameInputController`) and two keyboard-routing hooks already exist for a focused window to consume input: `Window.HandleKeyPress`/`OnKeyPressAction` (one discrete key-press event at a time) and `Window.HandleHotkeys`/`OnHotkeysAction` (the whole `KeyboardState`, for modifier-aware combos -- see `MapWindow.OnHotkeysAction`). Neither delivers actual typed *characters* (shifted case, punctuation, OS keyboard layout) though -- that needs a third hook fed from FNA's `TextInputEXT.TextInput` static event (the same "*EXT" extension-class pattern `GameInputController.UpdateCursor` already uses for `MouseCursorEXT`), mirrored the same way as the other two: `Window.HandleTextInput(char)`/`OnTextInputAction`/`IWindowContent.HandleTextInput`, fed by a new `GameInputController.RouteTextInputToFocusedWindow` subscribed to that event once.

A new `TextBox : TextWindow` control (reusing `TextWindow`'s existing wrap/scroll/draw machinery rather than rebuilding it, single-line just being a fixed-height case of the same class) would be the first thing to actually need all three hooks together:

- `OnTextInputAction` appends the typed character.
- `OnKeyPressAction` handles Backspace (removes the last character).
- `OnHotkeysAction` watches for Enter -- needs Shift-state, hence the whole-state hook rather than `HandleKeyPress`: plain Enter submits; Shift+Enter inserts a newline, multiline boxes only (a `Multiline` option, e.g. on a new `TextBoxOptions`/extended `TextOptions`, gates whether Shift+Enter does anything).

Behavior once submitted:

- Submitting (plain Enter) raises a `TextSubmitted` event (mirrors `Button.Clicked`) carrying the current text -- the TextBox itself stays generic; whatever hosts it decides what "submit" means.
- If the TextBox's parent window has another TextBox child, submitting moves focus to it rather than leaving focus on a dead end. Needs a new `Window.NextTextBoxAfter(Window? after)` helper (walks `ChildWindows` in order) plus a way for the TextBox to ask `GameInputController` to actually move focus, since `Window` has no reference to it -- a new `Window.FocusRequested` event, subscribed/unsubscribed by `GameInputController.SetFocus` exactly the way it already subscribes to `Closed`.
- Whenever a window with TextBox children becomes the focused window (click, Tab-cycle, or `FocusWindow`), redirect into its first TextBox automatically rather than leaving the container itself as the dead-end focus target. Natural place: `GameInputController.SetFocus` itself -- after focusing `newWindow`, check `newWindow.NextTextBoxAfter(null)` and redirect if found. This and the Enter-driven case above are the same underlying primitive (find the next TextBox sibling); `NextTextBoxAfter(null)` doubles as "find the first one."

A visual focus indicator is also needed specifically for this control -- not optional, since without one there's no way to tell a TextBox is focused at all: the existing indicator (`Window.FocusedTitleColor`) only paints a title bar, but a TextBox is expected to be titleless, so it needs its own border/highlight-based indicator instead.

First concrete implementation, landed: a popup window (`GameShellBootstrapper.OpenQuestComposer`, `WindowDisplayMode.Fixed`, closeable, explicitly resized to track its TextBox -- see the WrapContent-circularity item below for why not `WrapContent`) containing one multiline TextBox. Submitting sends the text to `NotificationCenter.AddNotification(NotificationCategory.Quest, ...)` and closes the popup. This demo is intentionally temporary -- see the "keep temporary quest-composer demo" note in project memory: don't remove it until a real second TextBox consumer exists.

Deliberately out of scope for this first pass -- start narrow; see Text Input Enhanced Features below for what's deferred and why.

Affected: `Presentation/UI/Window.cs` (new `HandleTextInput` hook, `NextTextBoxAfter`, `FocusRequested`), `Presentation/UI/IWindowContent.cs` (new hook), `Presentation/Input/GameInputController.cs` (new routing method, `SetFocus` auto-redirect), `Presentation/UI/TextBox.cs` (new), `Presentation/UI/Notifications/NotificationCenter.cs` (consumer for the demo).

### Low Priority

#### Player stats v2

Allow the player to select which stats to display in their stats view. Follow-on to Player stats v1 above.

#### End of level staircase

Presentation-side rendering/interaction for the staircase. See the matching Game item above.

#### Equipment menu

Exists side-by-side with inventory for easy click-and-drag equipping. Collapsible either direction -- inventory collapsible to give the equipment menu full screen space, and vice versa. Pauses the game while open (see Pause modality under Global).

#### Stats window

Display current stats and total buffs/debuffs, with an explanation popup showing the origin of each buff/debuff. Lets the player assign stat points to increase stats. See the matching Game stats item above.

#### Text Input Enhanced Features

Follow-on to Text input above, once a TextBox actually needs more than "type to append, Backspace to remove from the end" -- deliberately deferred out of that item's first pass rather than gold-plating a control before anything exercises the basics:

- Cursor-addressable editing: insert/delete at an arbitrary position within the string, not just the end.
- Arrow-key navigation (Left/Right, and Up/Down for multiline) to move the cursor without the mouse.
- Click-to-position-cursor: clicking within a TextBox's text sets the cursor to that character position.
- Selection (Shift+arrow or click-drag) and copy/paste, building on the clipboard mechanism from the Text copy to clipboard item below.
- Key-repeat on a held Backspace/Delete -- `Window.HandleKeyPress` is edge-triggered (fires once per press, not while held), so this needs either a per-window repeat timer or a second, repeat-aware routing path. Typed characters don't have this gap: OS-level `TextInputEXT` text input already auto-repeats while a printable key is held.

Affected: `Presentation/UI/TextBox.cs` (once it exists, see Text input above).

#### WrapContent parent sizing collapses when a child resizes itself after being attached

Discovered building the quest-composer popup (see Text input above): a `WindowDisplayMode.WrapContent` window whose size depends on a child, paired with a child that later resizes *itself* (not at attach time -- `AddChildWindow`/`RemoveChildWindow` already re-fit a WrapContent parent correctly on attach/detach), collapses both windows toward `(0,0)` instead of settling on a real size. Confirmed with a failing test (a `WrapContent` parent + a multiline `TextBox` child, `TextBox.AutoSizeToContent` calling the parent's own `MeasureAndArrange` after each resize) before backing out of that design.

Root cause: `Window.Measure` unconditionally overwrites a child's own `_geometry.MaximumSize` with `_parentWindow.ContentSize - RelativePosition` on every pass (see the top of `Measure`), regardless of whatever `MaximumSize` the child was actually built with. For a `Fixed`-size parent this is harmless (`ContentSize` is already stable, independent of children). For a `WrapContent` parent it's circular: the parent's own `ContentSize` is *derived from* its children's current sizes, but a child that resizes itself gets its own cap silently rewritten to that same not-yet-correct parent `ContentSize` -- which starts at `(0,0)` before the parent has ever measured a child, so the loop starts degenerate and never escapes it (each side keeps "confirming" the other's near-zero size instead of converging on the child's actual intended size).

The quest-composer popup works around this today by staying `Fixed` and having `GameShellBootstrapper.OpenQuestComposer` explicitly resize the popup off the TextBox's own `Resized` event, with a chrome-overhead constant computed once up front -- see that method's own comments. That's a fine one-off answer but doesn't generalize: the *next* thing that wants "container shrinks to fit a child, then grows as that child grows" will hit the exact same wall.

A real fix likely means `Measure` shouldn't blindly overwrite a child's `MaximumSize` from `_parentWindow.ContentSize` when the parent is itself `WrapContent` mid-resolution -- e.g. a child's own explicitly-authored `MaximumSize` (captured once at `BuildWindow`, the way `TextBox` was almost given its own independent cap field before this got scoped down to the `Fixed`-parent workaround) should take precedence over whatever the parent's not-yet-settled `ContentSize` currently is. Worth a real design pass rather than a quick patch, since it touches the shared Measure/Arrange pipeline every window goes through.

Affected: `Presentation/UI/Window.cs` (`Measure`, `MeasureAndArrange`, `RecalculateWrapContentWindowSize`).

#### Text copy to clipboard

`TextWindow.OnContentClickAction` has a standing `// TODO copy text to clipboard`. Lower effort than full text input, and doesn't need focus/keyboard routing first -- click-to-copy, not type-to-edit.

#### Scrollbars

Scrolling itself works (`Window.ScrollBy`/`MaxScrollOffset`, mouse-wheel-driven via `GameInputController.UpdateMouseWheelScroll`), but there's no visual affordance for it -- no thumb, no track, nothing indicating a window's content extends past what's visible or where the current scroll position sits within it, and no way to click-drag to a position directly. Right now a user has to already know to try the mouse wheel.

Affected: `Presentation/UI/Window.cs`, `Presentation/UI/TextWindow.cs`.

#### Occupancy rendering/selection scans assume a small Tiny/Phasing population

`MapWindow.BuildOccupantsByPosition` (rebuilt fresh every single frame) and `SelectionWindowContent.RecomputeSelectedEntityIds` (also every frame, via `Update`) both find Tiny/Phasing entities by doing a full linear scan of the `OccupancyComponent` pool and reading each one's `TransformComponent.Position` -- there's no position-keyed index for them, because `Map`'s own occupancy array deliberately never records non-Blocking entities (see `World.IsBlocking`).

That design assumed ghosts/insects are always a small population relative to the map, cheap enough to rescan wholesale every frame. A randomly generated level that happens to be populated primarily by Tiny/Phasing entity types breaks that assumption -- at that point these become O(total occupant count) *per frame* scans instead of the intended "small enough not to matter" cost, on both the render path and the (also per-frame) selection-inspector path.

When this becomes a real bottleneck: replace the per-frame full-pool rescan with an actual position-keyed index for non-Blocking entities, kept incrementally in sync with placement/movement/removal the same way `Map`'s own creature-occupancy array is -- or at minimum, only rebuild `MapWindow`'s dictionary when the Occupancy pool or relevant transforms have actually changed since the last frame, rather than unconditionally every frame.

Affected: `Presentation/UI/MapWindow.cs` (`BuildOccupantsByPosition`), `Presentation/UI/Content/SelectionWindowContent.cs` (`RecomputeSelectedEntityIds`).

#### Window minimize completeness

Two standing TODOs at the top of `Presentation/UI/Window.cs`: minimized windows don't hide/show their children (a minimized parent still draws children as if it weren't minimized, underneath a title-bar-only window), and sibling windows in a tiled parent don't retile when one of them minimizes or restores -- the same class of "stale RelativePosition leaves a gap" bug already fixed for `AddChildWindow`/`RemoveChildWindow` (see `Window.RetileChildrenFrom`), just not yet extended to cover `SetWindowDisplayMode`.

#### Window docking / splitters

The map/debug/selection windows are independently positioned/sized rectangles today -- no way to resize the boundary between two adjacent panes at once, or dock a window to the screen or another window's edge.

#### Window open/close/minimize animation

Everything -- opening, closing, minimizing, restoring, a notification appearing -- snaps instantly with no transition. Pure polish; lowest priority of the UI items here.

#### Options menu

No settings/options screen exists -- pressing Escape currently does nothing. Wanted: Escape (global and unconditional, the same way Tab is -- see `GameInputController.HandleFocusCycling`'s "must stay unconditional" note -- not gated to whichever window holds focus) opens an options menu, and the game pauses while it's open.

`MapWindow.IsPaused` (see `OnHotkeysAction`) is today the only pause trigger, and was flagged when it moved there as a seam to revisit once a second trigger showed up -- this is that second trigger. Worth generalizing pause into something both the options menu and MapWindow's own Space hotkey set, rather than the options menu reaching into MapWindow to flip its flag directly.

Directly related to Pause modality under Global: an open options menu is itself the kind of modal window that item wants -- solving "block/dim input to other windows while a modal is up" there would cover the options menu for free, not just System notifications.

Affected: `Presentation/Input/GameInputController.cs` (Escape handling), `Presentation/UI/` (a new options-menu window), `DungeonCrawlerWorld/GameShellBootstrapper.cs`/`GameLoop.cs` (wiring it in and gating the simulation update on it, alongside `MapWindow.IsPaused`/`NotificationCenter.HasBlockingNotification`).

#### Keybindings page on the options menu

After Options menu above -- needs somewhere to live. A page/tab within the options menu listing the game's hotkeys (today hardcoded in `MapWindow.OnHotkeysAction`, plus `GameInputController`'s own Tab/Escape handling) and letting the player remap them.

Depends on Options menu above and Standard widget set above -- listing/remapping actions needs more than `Window`/`TextWindow`/`Button`, at minimum something list-like. Would also eventually want persisted storage for the rebound keys -- see Data storage under Global, though today that item only covers window geometry.

Affected: the new options-menu content (see Options menu above), `Presentation/Input/GameInputController.cs` and `Presentation/UI/MapWindow.cs` (the hotkeys being made rebindable).

## Global

### High Priority

#### Pause modality

A `NotificationCategory.System` notification pauses the simulation (`NotificationCenter.HasBlockingNotification`, checked in `GameLoop.Update`), but doesn't actually block input to or dim whatever's behind it -- other windows (map, selection, debug) stay fully interactive underneath a "blocking" notification, which reads as a bug the first time someone notices it. Needs an actual modal concept: input to other windows either ignored or visually indicated as unavailable while a modal window is up.

Promoted to High: both the new equipment menu and the Options menu (see Presentation) explicitly need "pause game while open" behavior, and neither should re-solve modality on its own.

### Low Priority

#### Data storage, starting with window locations and sizes

No serialization/save-and-load system exists anywhere yet. Window layout (`WindowRelativePosition`/`WindowCurrentSize`/`WindowDisplay` -- see `Window.cs`) is the first concrete use case: every launch starts from whatever `GameShellBootstrapper` hardcodes, with no way to remember where the player last left the map/debug/selection windows or which were minimized.

Worth treating as the first slice of a general data-storage system (entity/world save state will eventually need the same serialize-to-disk mechanism -- including, eventually, inventory/equipment/stats state from the new Engine/Game items above) rather than a one-off "just persist these three floats" hack -- but start narrow. Window geometry is small, self-contained, and has no cross-entity references to untangle, which makes it a good first slice specifically *because* it won't force premature decisions about how the general system should handle things like entity references that a save format will eventually need to solve.

#### Field and property cleanup

General pass over field/property usage across the codebase once UI and core gameplay systems stop churning as fast as they are now -- e.g. auto-properties with no logic that could just be public fields, or the reverse, plus consistency in when a type exposes plain mutable fields (see `WindowGeometryState`/`WindowTitleState`/`WindowBorderState`/`WindowContentState`'s own doc comments explaining why those are deliberately plain fields, not properties) versus properties elsewhere. Not a bug list -- a housekeeping pass, better done once the shape of things has settled than mid-churn.

#### Solution-wide code style cleanup

A few conventions got clarified while building the focus/keyboard-routing system (`Window.IsFocused`, `GameInputController`, `MapWindow.OnHotkeysAction`) that haven't been retroactively applied anywhere else in the solution:

- Comments should only explain the WHY when it's genuinely unique or non-intuitive (a hidden constraint, a subtle invariant, a bug workaround) -- not restate what well-named code already makes obvious.
- Ternary expressions are written on three lines (the condition, then the `?` and `:` branches each on their own indented line), not packed onto one line.
- Each method should contain a single return, except for leading guard clauses.

`GameInputController.cs`, `Window.cs`, and `MapWindow.cs` were brought up to these as part of that work, but only the parts actually touched -- pre-existing methods in those same files (e.g. `Window.FindTitleButtonAt`/`TryHitTestInteraction`, `GameInputController.GetResizeCursor`, most of `MapWindow`'s rendering code) still predate them, and nothing elsewhere in the solution has been touched at all. Worth a dedicated pass once things settle rather than drive-by reformatting unrelated code mid-feature. Related to Field and property cleanup above -- possibly the same pass.

#### Possible future UI gaps, likely out of scope for this project

Tooltips, localization/IME support, and accessibility (screen reader) hooks are all standard in general-purpose GUI frameworks, but this is currently an admin/debug UI layered over a game world rather than a general application shell. Noting these for completeness, not because they're expected to be built soon.

(Drag-and-drop, formerly listed here alongside these, has been promoted out of "probably never" -- the new inventory management and equipment menu items above both explicitly require click-and-drag organization, so it's now in scope as part of those items rather than a standalone speculative gap. Window-layout persistence, also formerly listed here, likewise has its own section above -- see Data storage.)
