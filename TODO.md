# Long-Term TODOs

Non-urgent architectural items worth revisiting later -- things noticed in passing that don't block current work, not a sprint backlog. Organized by layer (Engine, Game, Presentation, Global -- cross-cutting items that don't belong to a single layer), each split into High/Low priority.

## Engine

### High Priority

#### Inventory system

Infinite storage per entity. The player character will carry a large amount (hundreds of items); NPCs will carry very little each (a dozen items or so). Items have persisted state within an inventory slot rather than always reverting to their starting values -- e.g. a partially consumed potion stays partially consumed while sitting in inventory, rather than resetting.

### Low Priority

#### Equipment

Engine-side equipment support (slots, equip/unequip mechanics). Companion to the Game-layer equipment rules and the Presentation-layer equipment menu below.

#### Explore the C# `Span<T>` structure for component storage

Component pools (`DirectComponentPool<T>`/`PackedComponentPool<T>`/`MultiComponentPool<T>`, `Engine/ECS/Components/Stores`) are hot-path -- called every frame, per striped system (see `SystemManager`/`EntityStripeSet` in CLAUDE.md's ECS notes). Worth spiking whether exposing pool data as `Span<T>`/`ReadOnlySpan<T>` (bulk contiguous access, no per-element bounds-check/indirection, no allocation) is a meaningful win over the current per-entity-id indexed access pattern, particularly for systems that process most or all of a pool's population rather than a scattered subset.

Explore before committing -- this is a profiling question (does indexed access actually show up as a real bottleneck anywhere) as much as an API design one; not worth restructuring the pools around until there's a measured case for it.

## Game

### High Priority

#### Inventory management rules

Item interactions, storage rules, restricted items, etc. Governs how the Engine-layer inventory system above is actually used -- what can stack, what can't be picked up by whom, interactions between items, and similar rules.

#### Melee attack implementation

For NPCs and the player. Attacking sets the same shared ActionLockComponent that movement sets on a successful move, creating a tactical decision between moving more vs. attacking more -- choosing to attack this window means not moving this window, and vice versa. Can target any entity one tile away that has physical collision -- even entities without hit points, since this allows status effects to be applied to otherwise-immortal entities. The "immortal but affectable" case is proven out already: `AbilityEffectResolver` grants `AbilityEffect.StatusEffects` through the shared `StatusEffectAuraApplierRegistry` (`Game/Modules/StatusEffects/`) regardless of whether the target has a `HealthComponent`, and `MeleeModule`'s Default Attack grants Paralysis (`Game/Modules/Paralysis/`) this way today.

### Low Priority

#### Replace MeleeModule with a general ability library

`Game/Modules/Melee/MeleeModule.cs` registers exactly one ability definition ("Default Attack") directly in its own `Configure` -- a reasonable first step (see the Melee attack implementation item above), but it hard-codes "melee module = one fallback attack" rather than being a real content library. Once more than one race/class-agnostic ability exists (e.g. the self-buff/poison-toggle/self-heal examples below), replace it with a proper catalog of premade, off-the-shelf `AbilityDefinition`s that aren't tied to any specific race or class -- race/class blueprints then pick whichever ones they want to grant (Default Attack among them), rather than every generic ability living inside a module named after one specific attack.

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

#### Petrification status effect

Distinct from Paralysis (`Game/Modules/Paralysis/`, which only locks `ActionLockComponent` and tracks its own `StatusEffectStack` entry) -- Petrification additionally turns the target to stone, forcing it to become `ForceBlockingComponent`-blocking for its duration regardless of the target's normal blocking state (`Game/Modules/Core/Components/ForceBlockingComponent.cs`, precedence in `World.IsBlocking`). That makes it a real map-occupancy problem, not just an ActionLock one: `Map` only tracks one Blocking occupant per `(x,y,MapLayer)` slot (see CLAUDE.md's World & Map notes), so turning an already-non-blocking or currently-moving entity into a forced-blocking one while it may be sharing a tile with another Blocking or non-Blocking occupant has no defined resolution policy yet -- what happens to whichever entity/tile already held the Blocking slot, whether a Tiny/Phasing entity petrifying mid-overlap becomes the new sole occupant or is rejected, etc. Needs its own design pass through `World`/`Map` placement logic, not a drop-in extension of `ParalysisEffects.Apply`.

#### Goblins attack adjacent targets with default melee instead of moving

Today's `MovementMode.Random` walks a goblin into an occupied tile as if it were empty (blocked by `CanMove`, so it just doesn't move) rather than attacking. Now that melee is a real action any entity can trigger, goblin AI should prefer activating its Default Attack against an adjacent blocking entity over its normal random-wander check.

#### Corpse decay/destruction and destructible terrain

`DeathSystem` (`Game/Modules/Death/`) deliberately never calls `EntityManager.DestroyEntity` -- a corpse is reclassified non-Blocking (`World.ConvertToNonBlocking`) and marked `DeadComponent`, but stays a real, fully-populated entity indefinitely (design intent: a future corpse-looting mechanic, see the Achievement content backlog item below, needs the entity's data to still exist). `EntityManager.DestroyEntity` (full removal, all components gone, id freed for reuse) is reserved for a genuinely separate, deliberate action -- e.g. a corpse-decay timer or "loot then destroy" step once Inventory exists. The same primitive would also apply to a future destructible-terrain entity (e.g. a breakable wall): that case skips `DeadComponent`/the corpse system entirely, since it was never a `HealthComponent`-driven creature death, and would just call `DestroyEntity` directly on whatever triggers its destruction.

#### Self damage buff ability

An example FreeCast or Immediate ability that raises the caster's own outgoing damage for a duration -- exercises the ability system on a non-damage-dealing, self-targeted effect.

#### Toggle poison aura ability

A FreeCast-style ability that turns an existing Poison/StatusEffectAura source on/off around the caster -- exercises FreeCast's "usable during an Action Lock" behavior against the existing aura machinery.

#### Self heal ability

Companion example to the self-buff/poison-toggle items above -- a positive, self-targeted effect using the same plumbing.

#### Body parts

- Plan first
- Use multi-components somehow
- Position matters -- e.g. lava should damage feet first

#### Movement System

- `SeekTarget` movement mode

#### Achievement lootbox delivery

Achievements can name a `LootboxReward` (rarity + box type, see `Game/Modules/Achievements/LootboxReward.cs`), but nothing delivers it yet -- the Inventory system above doesn't exist. Once it does, `AchievementModule`'s unlock path needs to actually add the lootbox's contents to the player's inventory instead of only describing it in the notification. Lootboxes themselves can only be *opened* in Safe Rooms once opening exists as a mechanic -- this is not a purchased gambling item, it's a pre-set reward tied to how it was earned.

#### Achievement content backlog

The Achievement system (`Game/Modules/Achievements/`) currently ships seven achievements ("Loner", "You've Inflicted Damage on a Mob", "Unarmed Combat", "Early Adopter", "Inert Gas", "You've killed a mob!", "Empty Pockets") to prove the pipeline; the rest is a deliberate, incremental backlog -- a few added alongside each future feature rather than all at once. Volume/pacing target: many low-value achievements early (deliberately "drowning the player in low-level loot boxes" at the start), tapering to fewer, higher-value ones by the midgame.

Design-target examples, not yet implemented:
- Enter the dungeon with a cat (random starting-item selection)
- Find a Borough Boss
- Attempt to punch a slime
- Kill an armed enemy bare-handed
- Kill more than 20 non-combatant NPCs in one attack
- Reach level 2
- Wear magical gear for the first time
- Increase the Magic Missile spell to level 3
- Loot a corpse for the first time
- Store 10 tons of weight in inventory

Several depend on systems that don't exist yet (Inventory, a real companion/party concept + Human race, levels/experience, magic/spell gear, corpse looting) -- implement each achievement once its underlying system actually lands, not before.

`LonerAchievement` (`Game/Modules/Achievements/Definitions/LonerAchievement.cs`) unlocks unconditionally on `Game.World.EnteredDungeon`, published once by `GameLoop` right after `_playerSpawned` flips true (so `IPlayerQuery.PlayerEntityId` is already assigned by the time the handler reads it -- no timing hazard the way the old `EntityMoved` spawn-sentinel trigger had). Once a real companion/party concept exists, this needs to actually check for a Human-race companion near the player at spawn instead of always succeeding.

`UnarmedCombatAchievement` (`Game/Modules/Achievements/Definitions/UnarmedCombatAchievement.cs`) unlocks on the same `EnteredDungeon` event, same unconditional reasoning as `LonerAchievement` above (no equipment or start-equipment-selection system exists yet, so every player is unarmed today). Revisit once equipment/start-equipment selection lands: it should then check whether the player actually chose to start without a weapon.

`EmptyPocketsAchievement` (`Game/Modules/Achievements/Definitions/EmptyPocketsAchievement.cs`) unlocks on the same `EnteredDungeon` event, same unconditional reasoning as `LonerAchievement`/`UnarmedCombatAchievement` above (no Inventory or start-equipment-selection system exists yet, so every player's inventory is empty today). Revisit once Inventory/start-equipment selection lands: it should then check whether the player's inventory is actually empty.

#### Boundary-aware ProcessingTierSystem recompute

`ProcessingTierSystem` (`Game/Modules/ProcessingTier/Systems/ProcessingTierSystem.cs`) recomputes every movement-capable entity's tier once per its own 15-frame stripe turn, regardless of whether that entity's classification could actually have changed since last time. A targeted alternative: a coarse spatial grid over entity positions -- bucket by `(X / cellSize, Y / cellSize, Z)`, separate from `Map`'s own per-tile occupancy array (see `AuraGrid`, `Game/Modules/StatusEffectAura/AuraGrid.cs`, for an existing precedent of a Game-layer sparse spatial index keyed by flat cell position) -- so a player move only re-tiers entities in the thin band of cells straddling the Local-radius ring at the old and new player position, instead of waiting out each entity's own stripe turn regardless of whether anything relevant changed.

The Local ring (`LocalRadiusTiles`/`LocalExitBufferTiles`) moves with the player every step, so that band query needs to be genuinely cheap -- a handful of cell lookups, not a population scan. The Neighborhood/Borough boundaries are fixed absolute grid lines by contrast (`NeighborhoodSizeTiles`/`BoroughSizeTiles`), so they only need re-evaluating when the player's own cell index changes (rare) -- gate that behind a flag and drain it gradually rather than doing it in one frame. An entity moving under its own power (not the player) needs its own immediate recheck too, via the `EntityMoved` buffer `MovementSystem` already publishes.

This is a real structural addition, not a small tweak: a new persistent spatial index with its own insert/remove/move bookkeeping on every entity move (the same shape of migration cost `TieredEntityStripeSet` already pays for tier-bucket membership, one layer earlier), plus a genuine correctness surface -- the boundary-band width has to account for how far the player can move between checks, or a transition gets missed, something today's brute-force periodic recompute can't get wrong by construction. Only worth taking on once `ProcessingTierSystem` is confirmed as an actual bottleneck via profiling, not assumed from a single snapshot -- its cost in one profiling pass this session was comparable to or higher than most other systems, but that pass also coincided with newly-added Paralysis load driving `StatModifierExpirySystem` up, so the two haven't been cleanly isolated yet.

## Presentation

### High Priority

#### Inventory management

Tabs, sorting, click-and-drag organization, icons, click-to-inspect. Depends on the Standard widget set item below for list/tab-style controls, and on the Engine inventory system and Game inventory rules above for the data it's displaying.

#### Game over screen on player 0 HP

`Game/Modules/Death/` (`HealthDamage.Apply`/`DeathSystem`/`DeadComponent`) handles death at 0 HP for every entity except the player -- deliberately exempted for now, since the player dying today has no distinct end state or UI at all. Needs this Presentation-side piece (a real game-over screen) before the player-side exemption in `HealthDamage.Apply` can be lifted.

#### Context menu / mouse button coverage

Right-click dropdown of options. `GameInputController` today only ever reads `MouseState.LeftButton` -- no right-click, middle-click, or double-click detection exists anywhere, so building this needs that mouse-button coverage added first (also enables incidental wins like double-click-title-bar-to-maximize).

#### Player stats v1

Persisted view of the player's active stats. Always shows the same fixed set of important stats.

#### Player attack button or key

A button or key for attacking, distinct from the hotbar -- needs to be available outside the hotbar but usable more quickly than going through the context menu. Determine the best UI treatment for this class of "common interaction that should always be quickly accessible."

Partially addressed by the hotkey/ability system: Default Attack is bound to F by default and fires with a single press (or double-tap for auto-target), which covers the "quickly accessible" requirement functionally. What's still unaddressed is "distinct from the hotbar" specifically -- today it's just one more `HotbarContent` slot (`Presentation/UI/Content/HotbarContent.cs`) like any other, not a separate always-visible control outside it. Revisit whether that distinction is still wanted now that the hotbar itself is fast to use.

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

#### Neighborhood/Borough zoom levels

`MapCamera`'s `Neighborhood`/`Borough` zoom levels (`Presentation/UI/MapCamera.cs`) will render static structures only (walls/terrain) plus special sprites for bosses and important locations -- no moving entities. These are fixed-grid "check the larger map" views, not playable zoom levels: instead of centering on the player like `Team`/current zoom levels do, they snap to preset square regions -- a `Neighborhood` is 1000x1000 tiles, a `Borough` is 2000x2000 (a 2x2 block of neighborhoods) -- the same region sizes `Game/Modules/ProcessingTier/Systems/ProcessingTierSystem.cs` uses for its distance-throttle tiers, so both features share one spatial vocabulary.

#### Per-entity sprite scale

`SpriteRenderer.Draw` (`Presentation/Rendering/SpriteRenderer.cs`) always stretches a sprite's source rectangle to fill its tile footprint exactly -- fine for tile-sized art (Wall, Grass) but wrong for character sprites, which don't all read at a consistent apparent size relative to their footprint: confirmed in-game, the player sprite needs to render larger and goblin sprites smaller. Needs a per-entity (or per-`SpriteComponent`) scale factor -- e.g. a `Scale` field on `SpriteComponent` (`Game/Modules/Core/Components/SpriteComponent.cs`) that `MapWindow.TryDrawEntityVisual` applies when computing the destination rectangle passed to `SpriteRenderer.Draw`, rather than always drawing at exactly the tile's own footprint size.

Affected: `Game/Modules/Core/Components/SpriteComponent.cs`, `Presentation/Rendering/SpriteRenderer.cs`, `Presentation/UI/MapWindow.cs` (`TryDrawEntityVisual`), `Game/Blueprints/SpriteManifest.cs` (Player/Goblin entries would set their chosen scale here).

#### Ability summary on hotkey hover

A tooltip-style panel showing an ability's name/effect/cooldown when hovering its hotbar slot (`Presentation/UI/Content/HotbarContent.cs`) -- depends on the Hotbar UI existing first, which it now does.

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

#### Review MapWindow for properties that belong on MapViewState instead

MapWindow has accumulated a growing set of its own instance fields (camera/zoom state, hotkey-arming bookkeeping, hover-tracking buffers) alongside `MapViewState`, which already holds the shared state other windows/content need to read (`SelectedMapNodePosition`/`CurrentMapLayer`/`ArmedAbilityId`/`ArmedSlot`/`TargetableTiles`/`HoveredTile`). Worth a pass to check whether any of MapWindow's own private fields are actually shared/inspectable state that belongs on `MapViewState` -- the established convention for state another window/content might need to read -- rather than staying private to MapWindow, particularly as more Presentation work (Hotbar UI, activation flow) lands on top and may need some of that same state.

Affected: `Presentation/UI/MapWindow.cs`, `Presentation/UI/MapViewState.cs`.

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

#### Targeted key-press routing instead of a full-keyboard scan

`GameInputController.RouteKeyPressesToFocusedWindow` calls `KeyboardState.GetPressedKeys()` every frame a window is focused (effectively always) -- confirmed via reflection against the actual FNA assembly that this is the only overload (no non-allocating variant like MonoGame added), so it allocates a new array every frame for the life of the session.

`HandleKeyPress`/`OnKeyPressAction` (what this routes into) has exactly one real consumer today -- `TextBox.OnKeyPressAction`, which only cares about `Keys.Back`; `IWindowContent.HandleKeyPress` defaults to a no-op for everything else. Rather than scanning the whole keyboard (or, worse, manually diffing all ~130 `Keys` values via `IsKeyDown` every frame as a naive fix), let the currently-focused window's content declare the small set of keys it actually wants checked, and only call `IsKeyDown` for that declared set.

Not actually dependent on the Keybindings page item above -- `HandleKeyPress` (discrete edit-type keypresses, e.g. Backspace) and `HandleHotkeys` (continuous/combo game commands, what Keybindings remaps) are deliberately separate hooks. Sequenced here as a followup for proximity to the other keyboard-routing work, not a real ordering requirement.

Affected: `Presentation/Input/GameInputController.cs` (`RouteKeyPressesToFocusedWindow`), `Presentation/UI/IWindowContent.cs`/`Window.cs` (a new way for content to declare its interested keys), `Presentation/UI/TextBox.cs` (the one current consumer, declaring interest in `Keys.Back`).

## Global

### High Priority

#### Pause modality

A `NotificationCategory.System` notification pauses the simulation (`NotificationCenter.HasBlockingNotification`, checked in `GameLoop.Update`), but doesn't actually block input to or dim whatever's behind it -- other windows (map, selection, debug) stay fully interactive underneath a "blocking" notification, which reads as a bug the first time someone notices it. Needs an actual modal concept: input to other windows either ignored or visually indicated as unavailable while a modal window is up.

Promoted to High: both the new equipment menu and the Options menu (see Presentation) explicitly need "pause game while open" behavior, and neither should re-solve modality on its own.

### Low Priority

#### Debug/event logging with levels

`Game/Diagnostics/PlayerActivityLog.cs` (added alongside the Burning status effect) is a
narrow, single-purpose EventBus subscriber that writes the player's Burning damage and moves
straight to a file -- deliberately minimal, not a general logging facility. Worth a real
design pass once more than one thing wants to log: an actual log-level concept (e.g.
Debug/Info/Warn/Error, or a verbosity toggle), a general "subscribe any event type to a log
line" mechanism instead of one hardcoded handler per event, configurable sinks (file/console),
and eventually other entities/event types, not just the player's moves and damage.

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
