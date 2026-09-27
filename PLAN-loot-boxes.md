# Loot Boxes

(Pre-implementation. Replaces the `Lootbox` placeholder record in `Game/Modules/Achievements` and
TODO.md's "Lootbox delivery, and moving Lootbox out of Achievements" entry, which is deleted when
Phase 2 lands.)

## Context

A loot box is an inventory item that, when activated, grants the player items and is then
destroyed. Boxes are awarded by achievements, boss kills, quests, viewer gifts and sponsor gifts,
0 or 1 per award alongside other rewards. They are the main source of items outside shops, looting
and crafting: common early on, rarer as the player goes down floors.

What exists today:
- `Lootbox(LootboxRarity Rarity, string BoxType)` and `LootboxRarity` (Bronze..Celestial) in
  `Game/Modules/Achievements`. Four achievements declare one. The box only feeds the notification
  text as `LootboxLabel`. Nothing grants it.
- `InventoryActions.AddItem` merges stacks by `ItemDefinitionId`, so "same type and rarity stack"
  is free if each (type, rarity) pair is its own `ItemDefinition`.
- `InventoryActions.AddItemWithOverride` grants units carrying a shared `Override` definition. It
  merges only into a stack whose override is equivalent (`AreEquivalentOverrides`), which is how a
  batch of wands with baked charges stacks with each other and not with plain wands.
- `InventoryActions.TryTransferStack` is the single path for every item that leaves an inventory:
  Give/Take, the corpse and container windows, shop buy/sell (`ShopActions`), staging a trade
  (reserved trade-offer entities), and plain drag-drop (`PlainInventoryDragDropResolver`).
- Inventory tabs are generated from tags (`InventoryTagQueries.GetTagCounts`), so adding
  `Tag.Lootbox` gives loot boxes a tab with no UI work.
- `Catalog<T>.Register` works at any time, not only during `Configure`. `ScrollMasteryEffects`
  already registers a synthesized spell into `ActionCatalog` at runtime.
- Item hover/click: `InventoryItemStackCell` (sprite and quantity), `TooltipController`,
  `ItemDetailsWindowController.Open(entityId, stackInstanceId)`, which requires the player
  inventory window to be open because it docks beside it.
- `NotificationCenter.MinimizeNotification` closes the popup through the same `Window.Close()` ->
  `Closed` path a real close uses, so "the popup closed" and "the popup was dismissed" can't be told
  apart today. This matters for achievement-granted boxes (Phase 2).

## Design

### Module: `Game/Modules/Lootboxes/`

This is a new `LootboxModule` (IGameModule), and the second real awarder (bosses) is what justifies
moving it out of Achievements. `AchievementModule` depends on it.

- **`LootboxRarity`** moves here unchanged. The ordinal order is the rarity order (the sort key
  for opening).
- **`LootboxTypeDefinition`**: `Guid Id`, `string Name` ("Adventurer"), and optional
  `SpriteName`/`Glyph`. It has no contents of its own yet: every box uses the placeholder contents
  below until "Lootbox drop tables" (TODO.md) gives each type and rarity real contents. Registered
  into a `LootboxCatalog` (`Catalog<LootboxTypeDefinition>`) in `Configure`, so a mod can add or
  replace a type by `Id`.
- **`LootboxReward(Guid TypeId, LootboxRarity Rarity, IItemContents? Contents = null)`** replaces
  the `Lootbox` record. Award declarations (achievements and the boss facet now, quests later) hold
  one. `Contents` overrides what this box grants (see Content overrides). `DisplayLabel` resolves
  the type name through the catalog: "Bronze Adventurer Box".
- **Item definitions are created on demand**: there will be dozens of types, and most (type,
  rarity) pairs will never be granted, so none are registered up front.
  `LootboxCatalog.GetOrCreateItem(TypeId, Rarity)` builds the pair's `ItemDefinition` the first
  time the pair is granted and registers it into `ItemCatalog` then.
  - Its `Id` is derived deterministically from `(TypeId, Rarity)` (a name-based Guid, the same idea
    as `BlueprintRegistry.ComposedId`), so a pair has the same id in every session and save.
  - The definition has: name "{Rarity} {Type} Box", tags `[Tag.Lootbox]`, `CanTrade: false`,
    `GoldValue` 0, `Contents` set to the placeholder contents, a sprite per rarity (a tinted frame,
    see Phase 4), and no `Activator` (see Opening). The catalog remembers which pair each created id
    stands for.
  - A loaded save can hold a box whose pair hasn't been created in this session yet. An
    `ItemCatalog` miss therefore falls back to `LootboxCatalog.TryResolveItem(Guid)`, which creates
    the definition on first lookup. The first time that fallback runs, it builds its id-to-pair map
    by hashing each registered type with each of the 6 rarities: a few hundred Guids and no
    definitions. Nothing in the game saves yet, so this only has to exist once saves do. It's noted
    here so the id scheme doesn't close that door.
- **`Tag.Lootbox`** is appended to `Tag`.

Initial types: Adventurer, Alchemist, Exorcist, Investor, Librarian, Weapon, Boss, Quest,
Viewer Gift, Sponsor Gift.

### Contents: on the item definition, overridable per box

What a box grants is a property of the item itself, so a single box can differ from its type and
rarity's default without a separate lookup table.

- **`IItemContents`** (in `Game/Modules/Inventory`, next to `ItemDefinition`, because "an item that
  grants items when opened" isn't specific to loot boxes):
  `IReadOnlyList<(Guid ItemId, ushort Quantity)> Roll(SeededRandom rolls, ItemCatalog items)`.
  Implementations:
  - **`RandomSingleStackContents`**: the placeholder every box uses today. One stack of 1–10 of a
    single item picked uniformly from `ItemCatalog`, excluding `Tag.Lootbox` items and items with
    `CanTrade: false`. Type and rarity don't affect it. "Lootbox drop tables" (TODO.md) adds the
    real implementations.
  - **`SetContents(IReadOnlyList<(Guid ItemId, ushort Quantity)>)`**: fixed rewards, used by
    content overrides. It's a record that compares its entries in order, so two identical overrides
    built separately still count as equal.
- **`ItemDefinition.Contents`** (`IItemContents?`, default null). The on-demand loot box
  definition sets it to the placeholder.
- **Content overrides**: when a `LootboxReward` carries `Contents`, `LootboxActions.Grant` grants
  `definition with { Contents = reward.Contents }` through `InventoryActions.AddItemWithOverride`
  instead of `AddItem`. This is the existing per-stack `Override` mechanism, so:
  - The box keeps its type and rarity's item id: same name, sprite, tab and sort position.
  - It stacks with other boxes that have an equivalent override (`AreEquivalentOverrides` gains an
    `Equals(a.Contents, b.Contents)` term). It never stacks with the generic box of the same type
    and rarity, because they'd open to different things.
  - Opening reads the stack's effective definition (`InventoryQueries.TryResolveEffectiveItem`),
    so an overridden box and a generic box need no separate code path.
- Example: a quest step's award declares
  `new LootboxReward(Boss.TypeId, LootboxRarity.Gold, new SetContents([(ForemansHammer.Id, 1)]))`.
  The player gets a Gold Boss box that opens to that item.
- An override is an immutable definition, shared by every grant of the same award rather than
  built per grant (the CLAUDE.md Scale note on sharing immutable definitions).

### Tradeable items: a rule on the item, not the tag

`Tag.Lootbox` is for tabs. The "cannot be traded, looted, sold, bought, dropped or destroyed" rule
is a separate `ItemDefinition.CanTrade` flag (default `true`), so a future quest item can reuse it
without being filed under loot boxes. The name matches `CanBindToHotbar` and avoids confusion with
hotbar binding. Where it's enforced:

1. `InventoryActions.TryTransferStack` / `TryTransferAllStacksOfItem` refuse a stack whose
   effective definition has `CanTrade: false`. This one check covers give, take, corpse looting,
   shop buy/sell, trade staging and plain drag-drop.
2. `ShopActions.CanTrade(shop, item)` (the existing shop tag check) also returns false when
   `!item.CanTrade`, so the shop and trade UI grey it out through the existing eligibility pass
   instead of letting a drag fail silently.
3. `InventoryGridContent.BuildItemContextMenu` leaves out Give, Take, Sell All, Buy All and
   "Add to trade" for such a stack. `UiInputController.TryStartContentDrag` refuses to start a drag
   of one onto anything except the player's own grid. Refusing is the right answer here: the drag
   has no valid destination, so it shouldn't pretend to have one.
4. Drop and Destroy don't exist yet. The TODO entries "Destroyed items" and "Item damage and
   repair" each get a line saying they must respect `CanTrade`.
5. The only path that removes a loot box stack is opening it.

**Not hotbar-bindable**: this is separate from `CanTrade`, because an untradeable quest item might
still be usable from the hotbar. `InventoryItemStackCell.CanBindToHotbar` is false for a
`Tag.Lootbox` stack, and the hotbar's own drop and bind path (`ItemHotkeyBindingComponent` writes)
refuses one too. The check lives in one place, `ItemHotkeyBindingQueries.CanBind(definition)`, which
both callers use.

### Granting

`LootboxActions.Grant(componentManager, lootboxCatalog, entityId, LootboxReward, ushort quantity = 1)`
is the one chokepoint. It gets or creates the item definition, grants it plainly or with the
content override (see above), and publishes `LootboxGrantedEvent(entityId, reward)`, which later
work (the HUD, a loot box achievement) can hook onto.

Sources:
- **Achievements**: `IAchievementDefinition.Lootbox` becomes `LootboxReward?`. The box is granted
  when the popup is closed, not when the achievement unlocks:
  - On unlock, `AchievementModule.Unlock` adds an `UnclaimedAchievementLootboxComponent`
    (Multi pool, keyed by achievement id) to the player. This is Game state, so a pending box
    survives save/load and never lives only in a UI object.
  - `AchievementNotificationDetails` gains `Guid AchievementId`.
  - `NotificationCenter` gains `event Action<Notification>? NotificationDismissed`. It is raised
    only for a real close (Close button, the "Close" or "Close All" menu options), never by
    `MinimizeNotification`, which sets a flag the `Closed` handler checks.
  - `ShellBootstrapper` wires `NotificationDismissed` to `AchievementRewards.TryClaimLootbox(...)`.
    That removes the unclaimed record and calls `LootboxActions.Grant`. It is idempotent, so a
    double close can't grant twice.
  - A minimized or never-opened notification never pays out its box. The box stays unclaimed until
    the player opens that notification and closes it, and there is no timeout or other fallback.
  - Achievement boxes:

    | Achievement        | Box                      |
    |--------------------|--------------------------|
    | AngelInvestor      | Bronze Investor          |
    | Archivist          | Bronze Librarian         |
    | DrinkingProblem    | Bronze Alchemist         |
    | EarlyAdopter       | Silver Adventurer        |
    | EmptyPockets       | Bronze Adventurer        |
    | InertGas           | Bronze Exorcist          |
    | UnarmedCombat      | Bronze Weapon            |

    DrinkingProblem changes from "Potion" to Alchemist. AngelInvestor, Archivist and InertGas are
    new. AngelInvestor's doc comment ("Reward: None (temporary)") is updated to match.
- **Boss kills**: `BlueprintDefinition` gains a `Lootbox` facet (`LootboxReward?`, which can carry
  a content override), resolved like the other facets (a later include replaces an earlier one).
  The `Boss` trait declares a default (Bronze Boss), and a named boss such as `GoblinForeman` can
  override it.
  - A new `BossLootboxAwarder` subscribes to `EntityDiedEvent`. If the dead entity's resolved
    blueprint (spawn record plus applied parts) has a `Lootbox` and
    `Source.IsEntity(PlayerEntityKey)`, it grants the box to the player.
  - Only the player's killing blow counts, and each boss grants at most one box. Several boxes per
    fight (killing blow, most damage, started the fight, ...) is TODO.md's "Advanced boss loot box
    awards".
  - It reads the definition, never a component, so it works the same whether or not the boss was
    ever built past a skeleton.
  - The quest example above (a specific boss killed during a specific quest) is decided by the
    quest, not the boss. When quests exist, the quest's own award replaces or adds to the boss's
    box. That is recorded in "Advanced boss loot box awards".
- **Quests, viewer gifts, sponsor gifts**: none of these systems exist yet. Their box types (Quest,
  Viewer Gift, Sponsor Gift) exist so the vocabulary is there. Each future system calls
  `LootboxActions.Grant` with a `LootboxReward` on its own award declaration, the same shape
  achievements use, with or without a content override.
- **Admin Mode**: the map context menu gets "Grant loot box >" (type, then rarity) next to
  "Spawn here >" and "Apply >". It is the in-game test tool, and it stays.

### Opening

Activating any loot box opens every loot box the player holds. Opening is never blocked: no action
lock check, and no location check. Restricting where boxes can be opened is TODO.md's safe-room
item.

**It's a direct call, not a System.** Opening is a one-off response to a click, and systems exist
for work done every frame.
- `ConsumableActivationSystem` has to be a system because a consumable needs to run inside the
  simulation frame: the action lock, targeting resolved against the map, ordering against other
  systems, and the Debug skeleton guard (which only checks while `SystemManager.IsUpdating`).
- Opening a loot box needs none of that. It takes no lock, has no target, and only reads and writes
  the player's own always-built inventory.
- Presentation already mutates inventories directly between frames (`TryTransferStack` and
  `ShopActions` from context menus), and CLAUDE.md already says grants from Presentation are safe.
- A system would check a queue that's empty in almost every frame. So there's no system, no request
  queue and no event: the caller gets the results as a return value.

- **`LootboxOpener`**: a Game service built once per session and handed to Presentation the same
  way `EntityFactory` is. It holds `LootboxCatalog`, `ItemCatalog`, the item grant helpers and the
  roll sequence. `IReadOnlyList<OpenedLootboxGroup> OpenAll(int entityId)`:
  1. Collect the entity's loot box stacks and group them by `(TypeId, Rarity)`. Stacks of one pair
     form one group: a stack split by the stack cap, or overridden and generic boxes of the same
     type and rarity.
  2. Sort the groups by rarity ascending, then type `Name` ordinal ascending.
  3. For each unit in each group, roll that unit's effective `Contents`, grant each reward, and
     consume one unit (`ConsumeItemByStackInstanceId`).
  4. Return `OpenedLootboxGroup` = `(Guid TypeId, LootboxRarity Rarity, int Count,
     IReadOnlyList<GrantedItem> Items)`. `Items` is combined across every box in the group: one
     entry per item id, quantities summed. `GrantedItem` = `(Guid ItemId, ushort Quantity,
     uint StackInstanceId)`, where the stack id is the one the grant returned for the last unit of
     that item.
- **Trigger**: `InventoryGridContent.CanActivate` also accepts a player-owned loot box stack (it
  has no `Activator`). Activate and double-click on one skip `ActionTargetingController` entirely:
  - They call `LootboxOpener.OpenAll(playerId)` and pass the result to
    `LootboxResultsWindowController.Show`.
  - Arming a target shape for something with no target would be an unexpected action.
  - "Activate" is always enabled for a loot box, even while the player's action lock is up or the
    game is paused.
- **Rolls**: `LootboxOpener` owns one `SeededRandom` sequence, seeded from the session seed. It
  draws from its own sequence (like the crawler number allocator), so opening boxes shifts no other
  roll, and seeded runs and tests reproduce. It is never `Random.Shared`.
- **Grant-time item state**: a reward goes through the same grant helper a direct grant would use
  (e.g. `WandGrantEffects.Grant` for a wand, which bakes charges from Intelligence), not a bare
  `AddItem`. A small `ItemGrants.Grant(definition, quantity)` dispatch picks the right helper. If
  one doesn't exist when Phase 3 starts, it's added there and `PlayerKit`/`TreasureChest` move onto
  it too. A group's combined `Items` sums quantities by item id even when two wand grants landed in
  different stacks.
- **Rewards are always items.** A non-item reward is authored as a one-time consumable whose
  effects do the work (e.g. a potion that grants a spell). It uses the existing
  `ActionEffect`/activator pipeline, with no loot-box-specific effect path.

### Results window (Presentation)

**`LootboxResultsWindow`/`LootboxResultsWindowController`**, in `Presentation/UI/Lootboxes/`:
- `Show(IReadOnlyList<OpenedLootboxGroup>)` opens the window, or replaces its contents if it's
  already open. It is a movable, closable, vertically scrollable window (Escape closes it through
  `UiLayerStack.HandleEscape` like any other).
- One section per group, in opening order:
  - The header is in the rarity color: "Bronze Adventurer Box", or "Bronze Adventurer Box ×3" when
    Count > 1.
  - Below the header is a `GridControl` holding the group's combined items.
  - Dozens of boxes opened at once therefore make one section per distinct type and rarity, not one
    per box.
- The cells are `InventoryItemStackCell` (sprite and quantity badge), with no drag, no context menu
  and no hotbar binding. The quantity shown is how much that group granted, not the size of the
  stack it landed in.
- Hover uses the shared `TooltipController` (the same basic info the inventory grid shows). Click
  calls `ItemDetailsWindowController`:
  - When the reward's `StackInstanceId` still resolves on the player, it uses the stack-backed
    `Open`, so Compare works as usual.
  - Otherwise (the stack was consumed since) it uses a new definition-only
    `Open(ItemDefinition)`: read-only, with no Compare button.
  - `ItemDetailsWindowController` currently docks beside the player inventory window and returns
    early without it. It gains a dock anchor (`Func<Rectangle>`), which the results window supplies
    when it opens details itself. Each open window gets its own anchor, never one shared
    `Func<Rectangle>` fallback (the lesson from the item comparison work).
- Rarity colors go in a new `LootboxChrome` under `Presentation/UI/Chrome/`, next to the other
  palettes.

## Phases

Each phase ends with a manual in-game check before the next one starts.

1. **Data and granting**:
   - Build: module, catalog, `LootboxRarity` moved, `LootboxTypeDefinition`/`LootboxReward`,
     on-demand item definitions and the id fallback, `Tag.Lootbox`, `IItemContents` with
     `RandomSingleStackContents` and `SetContents`, `ItemDefinition.Contents`, the override grant
     path and its equivalence term, `LootboxActions.Grant`, the Admin Mode "Grant loot box >" menu,
     and the initial types.
   - Check: grant several from Admin Mode; the same type and rarity stack; the Lootbox tab appears.
2. **Achievement delivery**:
   - Build: `LootboxReward` on `IAchievementDefinition`, the table above, the unclaimed-box pool,
     `NotificationDismissed` (minimize excluded), and the claim on close. Delete the old `Lootbox`
     record and the TODO entry.
   - Check: close grants; minimize doesn't; Close All grants every box.
3. **Tradeability, hotbar refusal and opening**:
   - Build: `CanTrade` and its enforcement points, `ItemHotkeyBindingQueries.CanBind`,
     `LootboxOpener`, the Activate/double-click route, seeded rolls, and `ItemGrants`.
   - Check: every trade, loot, sell, drag and hotbar path refuses a box; opening one opens all of
     them in the right order.
4. **Results window**:
   - Build: the window, the grouped sections, cell reuse, tooltip, details with an anchor and the
     definition-only fallback, rarity chrome, and loot box sprites.
   - Check: visual, including a large mixed batch.
5. **Boss rewards**:
   - Build: the `Lootbox` blueprint facet, `Boss` default, and `BossLootboxAwarder`.
   - Check: the player killing a foreman grants a box; a kill by anything else doesn't; a boss that
     was never built still pays out.

## Tests

- `LootboxCatalogTests`:
  - No item definition exists until a pair is granted.
  - Created ids are deterministic per (type, rarity).
  - `TryResolveItem` recreates an unseen pair from its id.
  - A mod replaces a type by `Id`.
- Stacking:
  - Two grants of one pair give one stack of 2; different rarities give separate stacks.
  - Two grants with equal `SetContents` overrides (built separately) give one stack.
  - An overridden box and a generic box of the same pair give two stacks.
- `CanTrade: false`: `TryTransferStack`, `TryTransferAllStacksOfItem`, `ShopActions.TryBuyFromShop`
  and `TrySellToShop`, and trade staging each refuse, with no state change.
- Hotbar: dragging or binding a loot box to a hotbar slot is refused.
- `LootboxOpener.OpenAll`:
  - Order is rarity, then name.
  - Every unit is opened and the stacks are removed.
  - Stacks of one pair (including overridden ones) form one group with the right Count, and its
    items are combined by id.
  - A generic box grants exactly one item id at quantity 1–10, never a loot box.
  - An overridden box grants exactly its `SetContents`.
  - The same seed gives the same contents.
- Achievement claim: driven through `NotificationCenter` itself (close, minimize, Close All), not a
  direct call, per the live-testing feedback. Label assertions in `AchievementModuleTests` are
  updated for the new table.
- Results window: driven through `UiInputController` (Activate, double-click, hover, click, details
  anchor), not direct-call shortcuts.
- `SpawnRecordRebuilderTests` still pass: the blueprint facet is definition-only and adds no roll.
