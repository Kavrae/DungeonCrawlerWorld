# Plan: the Actions window and the menu sidebar

A window listing what the player can do, built from the Inventory window: tabs, a grid of cells, a search and sort row,
details on hover, activation by double-click or menu, and drag to the hotbar to bind. It also brings in a third kind of
hotbar binding, the **menu command** (open a window, switch the targeting mode). The four HUD menu buttons become a
**menu sidebar**: five ordinary hotbar slots, tiled vertically, bound by default to the five menu commands, as FFXIV
puts its main commands on a bar.

It replaces two TODO items, "Magic Menu" (Presentation) and "Any command on the hotbar" (Global, Low), and it comes
before the Workshop's Actions editor. The pieces that editor will reuse are named under "What the Workshop editor
inherits".

Status: **complete 2026-11-25** (decisions settled 2026-11-24, see Decisions). Phase 1 built 2026-11-24; Phases 2-6 built 2026-11-25 and tested in game.

## Decisions (settled 2026-11-24)

1. **The HUD menu buttons become a sidebar of five hotbar slots**, tiled vertically where the buttons were, keys **Y,
   U, I, O, P** from the top down, bound by default to Health, Actions, Inventory, Ability Scores and Switch targeting
   mode. Health moves from U to Y, and the Actions window opens on U. Left Alt still switches the targeting mode, as a
   fixed key beside the bindable command.
2. **Tabs: Menu, Core, a gap, then All and the category tabs.** All doesn't include menu commands or Core actions,
   and neither do the category tabs, since they split up All. The gap marks the two groups as different kinds of tabs.
3. **Core actions belong to crawlers**: an entity spawned as one (`SpawnFlags.Crawler`, see Core actions).
4. **The window opens where Inventory opens.** Opened while Inventory is open, it cascades to the left of it.
5. **Compare works for actions**, with the item rule: the same activator type.
6. **Menu commands are Health, Actions, Inventory, Ability Scores and Switch targeting mode.** F-key commands
   (Diagnostics F3, Admin Mode F12, and later save and load) are special: never bindable, never on the Menu tab.

## What carries over from the Inventory window, and what doesn't

| Inventory window | Actions window |
|---|---|
| Opened by a HUD button with an icon, key label and tooltip | Opened by the Actions command, on sidebar slot U by default, icon `Spell-Moderate`. There is no separate button |
| Pooled window, made fresh on each open; close only, movable, resizable; width 0.33 of the map window, height 455 | Same, through its own `ActionsWindowChrome` |
| Button greyed while `InventoryDisabledComponent` holds | The Inventory command's `CanRun`, which greys whichever slot holds it. Nothing disables the Actions window |
| Tabs: All, then one per item category (Item.* tag), rebuilt only when the set of categories changes | Menu, Core, gap, All, then one per action category, rebuilt the same way |
| `GridControl` row: count, sort cycle, toggles, search | Count, sort (A-Z, Z-A), search "Search Actions". No toggles: **Stack Diverged is gone** |
| Sorts Qty Hi/Lo, New | Gone: an action has no quantity and no acquisition order |
| Merged Stacks, expansion, divergent cells | Gone: **one cell per action id**, showing the effective definition (the player's override if there is one, as Magic Missile's) |
| Cell: sprite or glyph, quantity badge, grey when blocked, lit-toggle marker, selection and context-menu highlight | Sprite or glyph, grey when blocked (`GetActionBlocker`), cooldown and cost as the hotbar draws them, `ToggleActiveMarker`, selection and context-menu highlight |
| Hover opens Item Details; click selects; Compare pins | Same, with **Action Details** (see that section) |
| Double-click / "Arm" (or Turn on/off) arms the item | Same, for the action, through the same path as a hotbar press |
| Drag to a hotbar slot binds the stack | Drag to a hotbar slot (sidebar included) binds the action or command |
| Drag or Drop onto the ground, a container, another inventory | **Refused**: no Drop option, and the refusal cursor over every target that isn't a hotbar slot |
| Give/Take, Buy/Sell, trade staging, shop mode, shop pricing | Gone |
| Currency row in the footer | Gone: no footer |
| Joins the item windows' `ElementGroup` | Joins an equivalent group, so a press on the window leaves its Action Details open |

## Rules

- **All lists every action the player can use except Core ones.** "Can use" is `EntityActions.CopyActionIds`: the
  player's own grants, its applied parts', its spawn blueprint's, and the Core actions (see "Core actions"). The Core
  tab lists those the player can use that are Core. An action whose effective definition is an override shows that
  override, and appears once.
- **Activating from the window is the first press of an unarmed hotbar slot holding that action**, with no slot:
  `ActionTargetingController.ArmActionFromWindow(actionId)` does `ArmItemFromStack`'s job for an action. It is inert
  when the action is blocked. A toggle that `ToggleArming` doesn't arm first is queued at once on the player's tile.
  Everything else is armed (which closes every closable window, the Actions window too), and confirmed by a map click.
  `ArmAction` takes a nullable slot, as `ArmItem` already does. The double-click and the menu option share this path.
  The option's label is `ActivationMenuLabels.For(...)` and its Enabled comes from the same blocker check. A menu
  command's cell runs the command, through the same path as its slot.
- **Windows close when play is committed to** (settled 2026-11-25): arming anything (an action, an item, a drop) and
  turning a toggle on (action or item, from the hotbar, the Actions window or the inventory) close every closable
  window, Actions and Inventory included; turning a toggle off leaves them open. Action Details popups (Phase 5) close
  the same way.
- **A toggle turned on in Ground mode is aimed as a drop is** (settled 2026-11-25): the player's footprint and the ring
  of tiles around it (`TargetingSpec.FootprintOrAdjacent`, which `ItemDrops.Targeting` also is). A toggle item already
  went down that way (armed as a drop of one unit); a toggle action now offers the same tiles, and what it places (an
  aura anchor) goes on the tile aimed at (`Toggles.TurnOn`'s `placedAt`; a tile out of reach falls back to the
  holder's own).
- **A confirm uses the mode as it is now** (fixed 2026-11-25): whatever mode something was armed in, the selection it is
  confirmed with is built in the player's current mode (unless its targeting is Ground-only), as the highlight already
  was -- a shape that needs no aim (a Self toggle) confirmed by a map click was always built in Ground mode, so a Lantern
  or toggle item armed in Ground mode and confirmed after switching to Target was still put down.
- **A buffered press is never stranded** (fixed 2026-11-25): a hotbar press made while its action or item is cooling
  down is buffered by `PlayerCommands`; the hotbar's key handler writes it once ready every frame
  (`FlushBufferedActivation`), not only the map's keys while the map has focus -- which, once the sidebar took focus,
  left a toggle that could not be switched until its cooldown had passed.
- **A drag carries the action or command, never a copy of it.** `DragPayload` gets a new origin, `ActionCell`, with
  `ActionId` or `MenuCommandId` set. `CanBindToHotbar` is true, and `TryGetGroundDrop` is false. `HotbarBindingDropHandler`
  binds it. Each other handler (`MapDropHandler`, `InventoryTransferDropHandler`) refuses a payload that carries an
  action or a menu command, so the refusal cursor shows over them and a release does nothing. A drag starting closes
  Action Details, as `ItemDragDetailsDismissal` does for items.
- **The hotbar and the window highlight each other.** A cell's context menu lights the hotbar slots bound to its
  action, and a slot's lights the cell (IMPLEMENTATION-NOTES "Context menu"). `IShowsItemStacks` gets a sibling,
  `IShowsActions.ShowsAction(Guid)`, and `ContextMenuItemStacks` gets one for actions (and one for commands). The same
  goes for the selected cell while Action Details is open.
- **The Menu tab lists every bindable menu command**, whether or not one is bound. A command cell is never greyed for
  cost or cooldown, because commands have neither. It is greyed only when the command can't run now (Inventory while
  the inventory is disabled).

## The menu sidebar

- **Five new slots**, `HotkeySlot.Menu1`..`Menu5`, appended to the enum so no existing value moves, in a new
  `HotkeyCategory.Menu` in `HotkeySlotLayout` (keys Y, U, I, O, P; one column, rows 0-4). They are never locked:
  `IsLocked` only reads the Expansion category.
- **Ordinary slots.** A sidebar slot holds anything a slot can hold (an action, an item, a command), and a command can
  sit on any slot. Press, click, tooltip, context menu, drag on and drag off all work as they do on the hotbar, through
  `ActionTargetingController.HandleHotkeySlotPress`.
- **Both bars' keys work the same** (settled 2026-11-25): every slot key, the bar's and the sidebar's, is one input
  handler (`HotbarKeyInputHandler`), working wherever focus is and never while a text box has focus. What a press does
  while the game is paused depends on what the slot holds, never on which bar it is on: a menu command runs; an action
  or item arms; anything that would activate -- a confirm by re-press or map click, a toggle switched -- is refused,
  leaving it armed, with "Denied while Paused" at the cursor
  (`ActionTargetingController.RefusesActivationWhilePaused`, shown by `PausedActivationCursorText`). An armed drop is
  put down while paused, as a drag onto the map is (decided 2026-11-25): dropping is not an activation. The player's pause
  lives in `MapViewState.IsPausedByPlayer` for this.
- **Drawn by the hotbar**, as a second group of `HotbarSlotElement`s in their own vertical window where the HUD buttons
  stood (`MenuSidebarChrome.Position`, today's `HealthWindowChrome.ButtonPosition`). It is exempt from menu mode, like
  the hotbar. A slot that isn't bound is drawn empty, as any unbound slot is.
- **Default bindings** go in `PlayerKit` next to the action bindings: Menu1 Health, Menu2 Actions, Menu3 Inventory,
  Menu4 Ability Scores, Menu5 Switch targeting mode. Game holds the ids (`BuiltInMenuCommandIds`, plain Guid constants
  beside `BuiltInActionIds`) and never knows what they do.
- **Removed:** the Health, Inventory and Ability Scores buttons (and the chrome positions for them), and the U, I and O
  `KeyPressInputHandler`s. The keys now reach the windows through the slots. Windows that cascaded off a button's
  position (Ability Scores off Inventory) keep cascading off the window, not the button. `InventoryWindowController.Update`'s
  button greying becomes the Inventory command's `CanRun`.

## Core actions

"Core" is what every crawler can do, whatever its race and class. It starts with Power Attack, Quick Attack and Dodge.

- **A definition field, not a tag.** `ActionDefinition.Core` (`bool`, `init`, default false) is written `"core":true`
  by the action codec and declared in its `ContentSchema`, so the Workshop inspector shows it as a checkbox with no
  extra work. A tag was rejected for three reasons. "Core" is about who has the action, not about what a use is, so a
  tag would wrongly be something a disable rule or stat modifier could match. It would also produce a second "Core" tab
  among the tag tabs. And it would need excluding from the Activation domain.
- **Read from the summaries.** `DefinitionSummary` gets `IsCore` (from the indexed line, as `CanTrade` is), and
  `ActionCatalog.CoreActionIds` lists them from `Summaries`, cached per catalog version. A whole-catalog question never
  enumerates definitions (`WholeCatalogReadsArchitectureTests`).
- **Granted by resolution, not by writing components.** `EntityActions` resolves a Core action last, after own,
  applied and spawn-blueprint grants, for a crawler: an entity whose spawn record holds `SpawnFlags.Crawler`. The flag,
  not `CrawlerComponent`, because the spawn record is a foundation pool every build has (CrawlerModule's pool would make
  every module that builds an `EntityActions` require it), and the flag is cleared exactly when a crawler can't be
  given a number, so it stays a crawler until then. `Has`, `TryGetEffectiveAction` and
  `CopyActionIds` follow. A race's derived grant (Goblin's Quick Attack damage) still wins, because it is found first.
  The catalog's definition is used only when nothing else grants the action. Nothing is added per entity, and a crawler
  built before a definition became Core gets it at its next session load.
- **Content declared.** A crawler blueprint's build doesn't read Core actions, so `UsesActions` is unchanged. By-id
  loading needs the Core records decoded before a crawler first asks for one: `ContentCatalogLoader` loads every Core
  record at startup, as it loads a save layer's winners. They are few.
- Master: Dodge, Quick Attack and Power Attack get `"core":true` in `Actions.jsonl`. The races keep their grants,
  since those carry the race's damage.

## Action categories (the tag tabs)

- A new tag domain, `GameTagDomains.ActionCategory` ("The categories an action falls in: Actions window tabs"), which
  CoreModule declares over the `Action` and `Spell` roots, so it is not extensible.
- `ActionTagQueries.GetActionCategoryCounts(entity)` mirrors `InventoryTagQueries.GetItemCategoryCounts`, leaves out
  Core actions as All does, and counts each action once under each category. A category is any tag the domain accepts
  other than the `Action` root itself: `Action.Attack` ("Attacks") and `Spell` ("Spells", implied by `SpellActivator`).
  A future `Action.*` or `Spell.*` tag gets a tab with no code change. It is exposed through `ActionStateView`, so the
  effective definition's tags (implied tags included, through `ActivatableDefinition.Tags`) are counted, the
  override's for an overridden action.
- Category tabs are ordered as the inventory's are: most populated first, then by display name. Menu and Core are
  shown even when empty, so there is always somewhere to drag a command or a Core action from.
- **The gap** is UIEngine's: `TabbedContent.TabDefinition` gets `StartsGroup` (default false), and the header strip
  leaves `TabGroupGap` before a tile that starts a group. Nothing else about tabs changes (search, scrolling and the
  tab index bookkeeping skip the gap). All starts a group.
- The window opens on **All**.
- Rebuild trigger: `ActionStateView.GetActionSetVersion(entity)`, a version that changes when the entity's grants change
  (its `ActionInstanceComponent` and `AppliedBlueprintComponent` entries). As in the inventory, the tab list is rebuilt
  only when the set of categories changes. A cell grid refreshes on its own.

## Action Details

Action Details behaves as Item Details does: a hover popup at the cursor, a click selects until the window closes,
Simple and Advanced modes (the same session-wide `ItemDetailsModeSetting`, so the player's choice holds across both),
Compare, light dismiss, a grace period when the pointer leaves, and "one screen, no scrolling". It shows **no pricing
section**: no gold line, no shop rows, and no item-only lines (restrictions, "Opens into", item placement).

- **One window, two hosts.** `ActionDetailsWindow` (today definition-only, used by the Workshop's Actions tab) gains an
  optional *user* entity. With one, it shows Item Details' entity-bound lines: the Active line for a toggle that is on,
  the blocker (`ActivationBlockerText`), "Cooling down: Ns", values as used (`ActivationPreviews.Preview(player,
  action)`, which already takes any `ActivatableDefinition`) and the mode button. Without one (the Workshop), it stays
  as it is: always Advanced, catalog values. The section order is the one `ActivatableDetailsSections` already shares
  with Item Details.
- **One controller shape.** The popup lifecycle in `ItemDetailsWindowController` (cursor offset, the pointer-away grace,
  pinning, light dismiss, width taken from the host window) moves to a shared base,
  `DetailsPopupController<TTarget, TWindow>`. Item Details and Action Details are each a thin subclass. `ItemHoverDetails`
  gets the same treatment, with an `ActionHoverTarget(entityId, actionId)`.
- **Menu commands** show a small details popup of their own: name, description, and the slots it is bound to. The rest
  is shared lifecycle. A sidebar or hotbar slot's tooltip shows the same name and description.
- **Compare**: actions compare with actions. The rule that decides which cells may join a comparison is the item rule
  (`MapViewState.CompareRequiredActivatorType`): the same activator type, so a spell with spells and a melee attack with
  melee attacks. `ItemComparisonStatExtraction` already extracts from `ActivatableDefinition` everywhere but its item
  entry point. That entry point gets an action sibling, and the type is renamed when it stops being only about items.

## Menu commands: the third bindable

Commands are Presentation's alone. Game stores the binding and never knows what a command does.

- **Game: an opaque binding.** `MenuHotkeyBindingComponent(HotkeySlot Slot, Guid MenuCommandId)` (Actions module,
  Multi pool, `[DescribedComponent]`, implementing `IHotkeySlotBinding<Guid>`, with a new
  `GamePropertyMeanings.MenuCommandId` whose persistent form is the Guid as it is). `HotkeyBindingCommands.BindMenuCommand`/
  `UnbindMenuCommand` publish `MenuCommandHotkeyBoundEvent`, and `ClearSlot` clears all three kinds, so a slot still
  holds at most one binding. `HotkeyBindingView.TryGetBoundMenuCommand`. Game never validates the id. An id no
  command claims draws as an empty, unusable slot.
- **Presentation: the commands.** `MenuCommand(Guid Id, string Name, string Description, string SpriteName,
  string Glyph, Action Run, Func<bool> CanRun)` in a `MenuCommandCatalog`, built in `ShellBootstrapper` once the
  window controllers exist. Built-ins: Health, Actions (`Spell-Moderate`), Inventory, Ability Scores (each toggles its
  window), Switch targeting mode (`TargetingModeSwitch.Switch`, which Left Alt also calls). An F-key command is a
  `KeyPressInputHandler` as today and is never a `MenuCommand`.
- **On a slot**, a command is never armed, has no cost or cooldown, and never targets. A press runs it at once,
  through a third branch in `HandleHotkeySlotPress`, after the locked-slot check. The slot draws the command's icon,
  its tooltip shows the name and description, its context menu offers "Open" ("Switch" for the targeting mode) and
  "Unbind", a drag off it unbinds, and `CanRun` greys it. `DragPayload` gets `MenuCommandId`, which `CanBindToHotbar`
  accepts. `HotbarContent.ResolveDroppedBinding` takes the third id.

## What the Workshop editor inherits

- `ActionDetailsWindow`, the same window with or without a user.
- `ActionDefinition.Core` in the codec schema, so the inspector checkbox comes with it. `DefinitionSummary.IsCore` also
  gives the Actions tab a "Core" choice in its Show only filter group (`RecordFilterGroups`) for free.
- The Action category domain: the Actions tab's filter gets a category value group from the same query.
- The action cell drawing (icon, cost, cooldown and toggle marker in one helper shared with `HotbarSlotElement`).
- Not inherited: the grid and tabs. The Workshop's browsers are `ListView` rows with `ListFilter`, and they stay that
  way.

## Phases

Each phase ends with in-game testing, and the next waits for a go-ahead.

### Phase 1 -- Core actions and categories (Game, no UI)

- `ActionDefinition.Core`, codec key, schema, `DefinitionSummary.IsCore`, `ActionCatalog.CoreActionIds`, startup load
  of Core records in by-id sessions, and `EntityActions` resolving Core last for crawlers.
- `"core":true` on Dodge, Quick Attack and Power Attack in master.
- `GameTagDomains.ActionCategory`, `ActionTagQueries.GetActionCategoryCounts`, `ActionStateView.GetActionSetVersion`
  and the category counts on it.
- Tests: codec round trip and default omitted; `CoreActionIds` from summaries in a by-id session (nothing decoded but
  the Core records); a crawler with no race grant has all three Core actions, and a Goblin crawler's Quick Attack is
  still the Goblin's; a non-crawler gains nothing; category counts leave Core out (Attacks, Spells, an override's tags
  counted); `BuiltInModulesTests` is unchanged.
- Test in game: nothing visible yet. Spawn a Fairy through Admin Mode "Spawn here >" and check its attacks still use
  the Fairy's damage.

### Phase 2 -- Menu commands and the sidebar

- The Game binding (component, commands, view, meaning, event, `BuiltInMenuCommandIds`), `HotkeySlot.Menu1`..`Menu5`
  and `HotkeyCategory.Menu`, the default bindings in `PlayerKit`, `MenuCommandCatalog` with Health, Inventory, Ability
  Scores and Switch targeting mode, and the hotbar's third branch (draw, press, tooltip, context menu, drag off).
- The sidebar replaces the three buttons and the U/I/O key handlers. Slot U is bound to the Actions command from the
  start and draws empty until Phase 3 registers it (the unknown-id rule).
- Tests: binding a command clears an action or item on the slot and the reverse; a press runs the command; an unknown
  id draws empty and does nothing; the key, the slot click and the slot menu run a command once each; a sidebar slot
  takes an action dragged onto it; Admin inspection shows the binding with its meaning resolved;
  `GameEntitySnapshots.Check` passes; `HotkeySlotLayout` has Menu slots on Y, U, I, O, P, never locked.
- Test in game: expect five slots down the left edge where the buttons were, keyed Y, U, I, O, P, the second empty.
  Press Y, expect Health; I, Inventory; O, Ability Scores; P, the targeting-mode text at the cursor, and Left Alt the
  same. Drag Inventory from slot I onto hotbar slot 2, expect I empty and 2 opening Inventory. Drag Power Attack from
  Q onto slot I, expect I to arm Power Attack. Disable the inventory through Admin Mode, expect the Inventory command
  greyed wherever it sits.

### Phase 3 -- The window (look and browse)

- The Actions command (`Spell-Moderate`) and `ActionsWindowController`. `ActionsWindow`: tabs Menu, Core, gap, All,
  categories; opens on All. `ActionsTabContent` (`GridControl` with A-Z/Z-A and "Search Actions", no toggles),
  `ActionGridContent` and `ActionCell` (icon, cost, cooldown, toggle marker, grey when blocked, every frame, as
  `InventoryGridContent.UpdateBlocked` does), command cells on the Menu tab. `TabbedContent`'s `StartsGroup` gap.
- Tests: tab list order and the gap's position; rebuild only on a category-set change; All and the category tabs hold
  no Core action and no command; search and sort; a blocked action greys.
- Test in game: press U, expect the window at the inventory's size, on All, with tabs Menu, Core, a gap, then All,
  Attacks, Spells. Core holds Dodge, Power Attack and Quick Attack, and All holds neither of them. Menu holds the five
  commands. Type "fire" in search on All, expect Fireball, Fire Fan, Wall of Fire and Ring of Lava. Drain mana, expect
  Fireball greyed until it regenerates. Turn on Lantern from the hotbar, expect its cell marked on. Press I with the
  window open, expect Inventory beside it, not over it.

### Phase 4 -- Activate, bind, refuse drops, cross-highlight

- `ArmActionFromWindow`, the double-click, the context menu (activation option only, labelled and enabled as the
  hotbar's; "Open" on a command cell), the `ActionCell` drag source, the hotbar binding, refusals from the map and
  inventory handlers, and `IShowsActions` highlighting both ways.
- Tests through the real `UiInputController`: double-click arms and closes the window; the menu option and the
  double-click do the same; a blocked action's option is disabled and its double-click does nothing; a drag onto an
  unlocked slot binds it, onto a locked slot is refused, onto the map or the Inventory window is refused with nothing
  changed; a command cell dragged onto a slot binds the command; a slot's context menu lights the cell, and the
  cell's lights the slot.
- Test in game: double-click Fireball, expect the window closed and Fireball armed, then a map click casts it.
  Right-click Lantern in Ground mode, expect "Arm"; in Target mode, "Turn on", which lights it with the window still
  open. Drag Heal onto slot 1, expect it bound; drag it onto the map, expect the refusal cursor and nothing on release.
  Drag Ability Scores from the Menu tab onto slot 2, expect 2 to open it. Right-click Power Attack's cell, expect slot
  Q lit.

### Phase 5 -- Action Details and Compare

- Entity-aware `ActionDetailsWindow`, `DetailsPopupController` extracted with Item Details moved onto it,
  `ActionHoverTarget`, the command details popup, the mode button sharing `ItemDetailsModeSetting`, the selection
  highlight, drag dismissal, and Compare for actions.
- Built as 5a (2026-11-25): `ActionDetailsWindowController` beside Item Details' controller, with the same delay, grace,
  light dismiss and placement constants, rather than a shared `DetailsPopupController` -- the extraction waits for 5b,
  where Compare decides what the shared base has to carry. `ActionDetailsUser` makes the window entity-aware (Active,
  blocker, cooldown, values as used); the Workshop passes none. `ItemDetailsModeSetting` moved onto `ShellServices`
  (`DetailsMode`). Found on the way: `TabbedContent` updated its active tab twice a frame (the body window already
  does), so every tabbed window's hover delay ran at double speed; fixed at the root.
- Built as 5b (2026-11-25): the shared pieces the plan named, extracted from Item Details -- `DetailsPopupController<TWindow>`
  (the popup lifecycle; `ItemDetailsWindowController` and `ActionDetailsWindowController` are thin subclasses),
  `HoverDetails<TTarget, TContext, TPopup>` (`ItemHoverDetails`, `ActionHoverDetails` over `ActionHoverTarget(Subject, Name)`),
  `ComparisonLineStyles` (the line and shape colouring both windows use) and `ComparisonInputHandler` over `IComparisonMode`.
  `ActionComparisonController` mirrors `ItemComparisonController` with its own `MapViewState.CompareRequiredActionActivatorType`,
  so an action comparison never greys inventory items, and starting either compare mode ends the other.
  `ActionDetailsCoordinator` wires them, as `ItemWindowCoordinator` does for items. A click on an action cell now only
  selects, as a click on an item cell does (5a opened details on click); the selection ends when the Actions window closes.
- Tests: Item Details' existing tests still pass on the shared base; the Action Details sections hold no gold or
  pricing line; values as used match `ActivationPreviews`; Simple and Advanced switch together for items and actions;
  Compare offers only same-activator actions.
- Test in game: hover Magic Missile, expect a popup at the cursor showing the player's 5 damage (the override), with no
  price line. Switch to Advanced, then open Item Details on a potion, expect Advanced there too. Hover Fireball with
  too little mana, expect the blocker line. Compare Fireball, then hover Lightning Bolt, expect a comparison; hover
  Power Attack, expect it not eligible. Hover the Inventory command, expect its name, description and "Bound to I". In
  the Workshop's Actions tab, expect previews unchanged (no user, Advanced).

### Phase 6 -- Icon and Card views (added 2026-11-25)

A switch between two ways of drawing a grid, in the Inventory window, the Actions window and the shop:

- **Icon** is what the Inventory and Actions windows draw today: a square cell, the icon filling it, its numbers in
  the corners (`InventoryItemStackCell`, `ActionCell`).
- **Card** is what the shop draws today: a wide cell with the icon at its left, then the name, then the price
  (`ShopItemStackCell`, 4 cells wide).

The names stay as they are: the cells keep their class names, and "Icon" and "Card" are what the switch says.

- **The switch** is a toggle on the window's shared toolbar (`GridControlToolbar`, beside sort and search), so every
  tab of a window shows the same view and keeps it from tab to tab, as the search now does. It is a view setting, held
  for the session per window kind (Inventory, Actions, shop), as `ItemDetailsModeSetting` holds Simple and Advanced. It
  is never saved in content.
- **The shop:** switching it changes both sides together -- the shop's stock and the player's inventory beside it --
  because a trade compares one side with the other. While a shop is open, the player's Inventory window follows the
  shop's switch (today it is forced to Card); once the shop closes, it returns to its own.
- **A card for an item** outside a shop shows icon, name and quantity (or charges), with no price. In a shop, as today,
  it adds the price and the stock colouring.
- **A card for an action** shows icon, name and what it costs, with the cooldown wedge over its icon and greyed as the
  icon cell is. A menu command's card shows its icon and name.
- **One layout rule:** a grid's column count and cell size come from the view (`ActiveCellSize` today reads only shop
  mode), so the Inventory grid, the shop's grid and the Actions grid size their cells one way.
- **Unchanged by the view:** what a cell does -- click, double-click, its menu, dragging, hover details, selection and
  the context-menu highlight -- is the same in both views. The trade window's grids are left as they are.
- Tests: switching the view rebuilds the shown tab's cells as the other kind and keeps the search, sort and selection;
  every tab and a tab opened later show the chosen view; the shop's switch changes both of its grids; the player's
  Inventory window follows the shop's view while it is open and goes back to its own after; a cell does the same on a
  click and a drag in either view.
- Test in game: open Inventory and switch to Card: expect wide rows with names and quantities, no prices; change tab,
  expect Card still. Open the Actions window, switch to Card: expect names and costs. Open a shop: expect Card on both
  sides; switch it to Icon, expect both sides to switch; close the shop, expect the Inventory window back to its own
  view. Reopen the Inventory window: expect the view you left it in.

Phase 6 decisions (taken as recommended, 2026-11-25): the switch is a "Card view" checkbox on the toolbar row (the
shop, which has no toolbar, has it at the right of its grid header), and an action card shows no summary line.

Built (2026-11-25): `GridViewSetting` per window kind (`GridViewSettings`: Inventory and Actions start Icon, the shop
Card), on `InventoryServices` and `ActionsWindowServices`. `InventoryGridContent` reads its view each update
(`ComputeShowsCards`: the shop's while a shop is open, else its own; the loot window and the trade columns never
change) and rebuilds on a change; a card outside a shop is a `ShopItemStackCell` without its price
(`SetQuantityWithoutPrice`). `ActionCell` draws a card when it is wider than tall. The shop window's columns follow
its view (two cards, or as many squares as fit the same width), and `ContainerInventoryWindow` refits when its column
count changes.

## Not built by this plan

- "Bind To >" on a cell's context menu (TODO "Bind To..." on an inventory item). When it is built, it covers actions
  and commands too, through the same binding path.
- Rebinding the global keys, and extra hotbars (TODO "UI edit mode").
- A "Traits" view of passive grants, and any learning or forgetting of actions.
- The Workshop Actions editor itself.

## Bookkeeping

- Done 2026-11-25: TODO "Magic Menu" and "Any command on the hotbar" removed, "Marked targets" pointed at the Menu tab;
  IMPLEMENTATION-NOTES "Actions window, menu commands and the sidebar" added; CLAUDE.md updated.
- Done with the plan's settling: `Spell-Modate` (a misspelt duplicate of `Spell-Moderate`) removed from
  `SpriteManifest.json`.
