# Long-Term TODOs

Non-urgent architectural items worth revisiting later -- things noticed in passing that don't block current work, not a sprint backlog. Ordered by priority: finishing out the UI comes before gameplay features, so UI items lead, then engine/gameplay items follow.

## Text input

No editable text control exists -- `TextWindow` only ever displays text, never accepts it. Needed for anything resembling a settings screen, chat/console input, search/filter boxes, etc. Focus (`Window.IsFocused`/`GameInputController`) now exists and routes both discrete key presses (`Window.HandleKeyPress`) and whole-keyboard-state hotkeys (`Window.HandleHotkeys`) to whichever window is focused, so this is unblocked -- a text-input control just needs to override one of those two hooks.

## Text copy to clipboard

`TextWindow.OnContentClickAction` has a standing `// TODO copy text to clipboard`. Lower effort than full text input, and doesn't need focus/keyboard routing first -- click-to-copy, not type-to-edit.

## Scrollbars

Scrolling itself works (`Window.ScrollBy`/`MaxScrollOffset`, mouse-wheel-driven via `GameInputController.UpdateMouseWheelScroll`), but there's no visual affordance for it -- no thumb, no track, nothing indicating a window's content extends past what's visible or where the current scroll position sits within it, and no way to click-drag to a position directly. Right now a user has to already know to try the mouse wheel.

Affected: `Presentation/UI/Window.cs`, `Presentation/UI/TextWindow.cs`.

## Pause modality

A `NotificationCategory.System` notification pauses the simulation (`NotificationCenter.HasBlockingNotification`, checked in `GameLoop.Update`), but doesn't actually block input to or dim whatever's behind it -- other windows (map, selection, debug) stay fully interactive underneath a "blocking" notification, which reads as a bug the first time someone notices it. Needs an actual modal concept: input to other windows either ignored or visually indicated as unavailable while a modal window is up.

## Data storage, starting with window locations and sizes

No serialization/save-and-load system exists anywhere yet. Window layout (`WindowRelativePosition`/`WindowCurrentSize`/`WindowDisplay` -- see `Window.cs`) is the first concrete use case: every launch starts from whatever `GameShellBootstrapper` hardcodes, with no way to remember where the player last left the map/debug/selection windows or which were minimized.

Worth treating as the first slice of a general data-storage system (entity/world save state will eventually need the same serialize-to-disk mechanism) rather than a one-off "just persist these three floats" hack -- but start narrow. Window geometry is small, self-contained, and has no cross-entity references to untangle, which makes it a good first slice specifically *because* it won't force premature decisions about how the general system should handle things like entity references that a save format will eventually need to solve.

(Supersedes the "window-layout persistence" mention that used to sit in the out-of-scope UI list below -- promoted out of "probably never" now that it's explicitly wanted.)

## Occupancy rendering/selection scans assume a small Tiny/Phasing population

`MapWindow.BuildOccupantsByPosition` (rebuilt fresh every single frame) and `SelectionWindowContent.RecomputeSelectedEntityIds` (also every frame, via `Update`) both find Tiny/Phasing entities by doing a full linear scan of the `OccupancyComponent` pool and reading each one's `TransformComponent.Position` -- there's no position-keyed index for them, because `Map`'s own occupancy array deliberately never records non-Blocking entities (see `World.IsBlocking`).

That design assumed ghosts/insects are always a small population relative to the map, cheap enough to rescan wholesale every frame. A randomly generated level that happens to be populated primarily by Tiny/Phasing entity types breaks that assumption -- at that point these become O(total occupant count) *per frame* scans instead of the intended "small enough not to matter" cost, on both the render path and the (also per-frame) selection-inspector path.

When this becomes a real bottleneck: replace the per-frame full-pool rescan with an actual position-keyed index for non-Blocking entities, kept incrementally in sync with placement/movement/removal the same way `Map`'s own creature-occupancy array is -- or at minimum, only rebuild `MapWindow`'s dictionary when the Occupancy pool or relevant transforms have actually changed since the last frame, rather than unconditionally every frame.

Affected: `Presentation/UI/MapWindow.cs` (`BuildOccupantsByPosition`), `Presentation/UI/Content/SelectionWindowContent.cs` (`RecomputeSelectedEntityIds`).

## Burning status effect from touching lava

- Damage over time
- Damage decreases over time
- Goes away when damage hits 0
- Can stack to increase damage and duration (so multiplicatively worse)
- Gets worse for each movement the entity ends in lava

## Generic status-effect system needed once buff/debuff variety grows

The occupancy markers (`NonBlockingComponent`/`ForceBlockingComponent`) are `MultiComponentPool`-backed "many independent sources, count-based" components -- a reasonable, low-risk special case for exactly two boolean occupancy questions. Do **not** copy that pattern (a bespoke marker-component type plus a hand-written precedence function) for every future buff/debuff once real effect variety shows up (dozens of effects on the player character, overlapping sources assumed throughout) -- it doesn't scale:

- Most real buffs carry data (magnitude, remaining duration, source), not just presence -- Burning already needs this (see its own TODO item above: damage decreases over time, stacks increase both).
- Precedence/interaction relationships between many effect types multiply, not add, and become impossible to audit as N separate "if pool A has, else if pool B has" checks hand-written and scattered across whichever system happens to care about each one.

When that need arrives: build a generic "active effect" record (`EffectType` id/enum, `SourceEntityId`, `RemainingDuration`, `Magnitude`), still `MultiComponentPool`-backed since overlapping sources/stacking is the same requirement either way, with a small number of *derived-question* functions (`IsBlocking`, `IsStunned`, `GetSpeedMultiplier`, etc.) each independently scanning the relevant subset of active effects and applying its own precedence rule -- structurally the same shape `IsBlocking` already is, just generalized from two dedicated pools to filtering one shared effects table.

## Additive and multiplicative bonuses

A general way to combine multiple simultaneous stat modifiers -- from equipment, buffs/debuffs, race, class, etc. -- needs both additive (flat `+N`) and multiplicative (`xN%`) stacking, with a defined, fixed order of operations: conventionally, sum every additive modifier onto the base value first, then apply every multiplicative modifier to that sum -- not interleaved in whatever order modifiers happen to be applied, which would make the same modifier set produce different results depending on ordering (confusing for a player to reason about, and hard to write a test for).

Directly related to the generic status-effect system above -- `Magnitude` there is exactly this kind of value, and the derived-question functions it proposes (`GetSpeedMultiplier`, etc.) are where additive/multiplicative combination would actually get applied.

## Body parts

- Plan first
- Use multi-components somehow
- Position matters -- e.g. lava should damage feet first

## Movement System

- `SeekTarget` movement mode
- Efficiency update

## Player character with movement replacing map scrolling

Today `MapWindow.OnHotkeysAction` (invoked by `GameInputController.RouteHotkeysToFocusedWindow` while the map window holds focus) wires `W`/`A`/`S`/`D` directly to `UpdateScrollPosition` (pans the camera) and `PageUp`/`PageDown` to `ChangeLayer` (changes which layer is viewed) -- there is no player-controlled entity anywhere; every entity's position only ever changes via the Movement module/system, never directly from input.

Wanted: a real player character entity that `W`/`A`/`S`/`D` actually moves -- through the same Movement system every other entity uses (see the `SeekTarget` TODO above, which may share machinery), not a special-cased direct position write, so it interacts with blocking/collision/etc. the same as anything else. The camera should then follow the character's position/layer automatically instead of being the thing `W`/`A`/`S`/`D` directly controls.

- Shift + move keeps today's behavior (independent camera scroll, decoupled from the character) for free-look.
- Space snaps the camera back to the character's current position and layer -- needed because Shift-scrolling or `PageUp`/`PageDown` (viewing a different layer than the character occupies) can leave the camera arbitrarily far from the character, with no way back except manually scrolling/paging back.

Affected: `Presentation/UI/MapWindow.cs` (current `W`/`A`/`S`/`D`/`PageUp`/`PageDown` wiring in `OnHotkeysAction`; `UpdateScrollPosition`/`ChangeLayer` are camera-only today and would need a "center on position" method), `Game/Modules/Movement` (the actual movement system a player-controlled entity should route through).

## Explore the C# `Span<T>` structure for component storage

Component pools (`DirectComponentPool<T>`/`PackedComponentPool<T>`/`MultiComponentPool<T>`, `Engine/ECS/Components/Stores`) are hot-path -- called every frame, per striped system (see `SystemManager`/`EntityStripeSet` in the ECS notes). Worth spiking whether exposing pool data as `Span<T>`/`ReadOnlySpan<T>` (bulk contiguous access, no per-element bounds-check/indirection, no allocation) is a meaningful win over the current per-entity-id indexed access pattern, particularly for systems that process most or all of a pool's population rather than a scattered subset.

Explore before committing -- this is a profiling question (does indexed access actually show up as a real bottleneck anywhere) as much as an API design one; not worth restructuring the pools around until there's a measured case for it.

## Distance-based processing ("onion" processing)

Complementary idea to the existing entity-striping scheme (`SystemManager` processes `Count`/`StripeCount` of a system's population per frame, round-robin regardless of position -- see the ECS notes). Instead of, or in addition to, pure round-robin striping, bucket entities into concentric distance-from-player rings ("onions") and process outer rings less frequently or at reduced fidelity compared to the innermost ring.

Depends on a real player-character position existing to measure distance from (see Player character above) -- doesn't make much sense against the camera-is-the-viewpoint model there is today, since the camera can scroll anywhere independent of any single reference point. Related to the Movement System efficiency-update item above -- likely the same investigation; whichever gets tackled first should consider the other.

## Field and property cleanup

General pass over field/property usage across the codebase once UI and core gameplay systems stop churning as fast as they are now -- e.g. auto-properties with no logic that could just be public fields, or the reverse, plus consistency in when a type exposes plain mutable fields (see `WindowGeometryState`/`WindowTitleState`/`WindowBorderState`/`WindowContentState`'s own doc comments explaining why those are deliberately plain fields, not properties) versus properties elsewhere. Not a bug list -- a housekeeping pass, better done once the shape of things has settled than mid-churn.

## Solution-wide code style cleanup

A few conventions got clarified while building the focus/keyboard-routing system (`Window.IsFocused`, `GameInputController`, `MapWindow.OnHotkeysAction`) that haven't been retroactively applied anywhere else in the solution:

- Comments should only explain the WHY when it's genuinely unique or non-intuitive (a hidden constraint, a subtle invariant, a bug workaround) -- not restate what well-named code already makes obvious.
- Ternary expressions are written on three lines (the condition, then the `?` and `:` branches each on their own indented line), not packed onto one line.
- Each method should contain a single return, except for leading guard clauses.

`GameInputController.cs`, `Window.cs`, and `MapWindow.cs` were brought up to these as part of that work, but only the parts actually touched -- pre-existing methods in those same files (e.g. `Window.FindTitleButtonAt`/`TryHitTestInteraction`, `GameInputController.GetResizeCursor`, most of `MapWindow`'s rendering code) still predate them, and nothing elsewhere in the solution has been touched at all. Worth a dedicated pass once things settle rather than drive-by reformatting unrelated code mid-feature. Related to Field and property cleanup above -- possibly the same pass.

## Mouse button coverage / context menus

`GameInputController` only ever reads `MouseState.LeftButton` -- no right-click, middle-click, or double-click detection exists anywhere, so there's no way to build a right-click context menu (rename/delete/inspect-style actions) or a double-click convenience gesture (e.g. double-click title bar to maximize/restore).

## Window minimize completeness

Two standing TODOs at the top of `Presentation/UI/Window.cs`: minimized windows don't hide/show their children (a minimized parent still draws children as if it weren't minimized, underneath a title-bar-only window), and sibling windows in a tiled parent don't retile when one of them minimizes or restores -- the same class of "stale RelativePosition leaves a gap" bug already fixed for `AddChildWindow`/`RemoveChildWindow` (see `Window.RetileChildrenFrom`), just not yet extended to cover `SetWindowDisplayMode`.

## Standard widget set

The entire control set today is `Window`, `TextWindow`, `Button`, and `MapWindow` -- no checkbox, radio button, dropdown/combo box, slider, list box, or tree view. Worth building once an actual settings/config screen or anything list-heavy (inventory, quest log) needs one, rather than speculatively now.

## Window docking / splitters

The map/debug/selection windows are independently positioned/sized rectangles today -- no way to resize the boundary between two adjacent panes at once, or dock a window to the screen or another window's edge.

## Window open/close/minimize animation

Everything -- opening, closing, minimizing, restoring, a notification appearing -- snaps instantly with no transition. Pure polish; lowest priority of the UI items here.

## Possible future UI gaps, likely out of scope for this project

Drag-and-drop, tooltips, localization/IME support, and accessibility (screen reader) hooks are all standard in general-purpose GUI frameworks, but this is currently an admin/debug UI layered over a game world rather than a general application shell. Noting these for completeness, not because they're expected to be built soon. (Window-layout persistence, formerly listed here, now has its own section above -- see Data storage.)
