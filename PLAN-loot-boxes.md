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
  `Game/Modules/Achievements`. Four achievements declare one (DrinkingProblem, EarlyAdopter,
  EmptyPockets, UnarmedCombat). The box only feeds the notification text as `LootboxLabel`. Nothing
  grants it.
- `InventoryActions.AddItem` merges stacks by `ItemDefinitionId`, so "same type and rarity stack"
  is free if each (type, rarity) pair is its own `ItemDefinition`.
- `InventoryActions.TryTransferStack` is the single path for every item that leaves an inventory:
  Give/Take, the corpse and container windows, shop buy/sell (`ShopActions`), staging a trade
  (reserved trade-offer entities), and plain drag-drop (`PlainInventoryDragDropResolver`).
- Inventory tabs are generated from tags (`InventoryTagQueries.GetTagCounts`), so adding
  `Tag.Lootbox` gives loot boxes a tab with no UI work.
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
- **`LootboxTypeDefinition`**: `Guid Id`, `string Name` ("Adventurer"), `SpriteName`/`Glyph`, and
  `IReadOnlyDictionary<LootboxRarity, LootboxContents> Contents`. A type does not have to define
  every rarity. Granting a rarity the type doesn't define throws when the award is registered, not
  when the box is opened. Registered into a `LootboxCatalog` (`Catalog<LootboxTypeDefinition>`) in
  `Configure`, the same way items and achievements are, so a mod can add or replace a type by
  `Id`.
- **`LootboxContents`**: one of
  - `SetContents(IReadOnlyList<(Guid ItemId, ushort Quantity)>)`: fixed rewards.
  - `RandomContents(IReadOnlyList<LootTableEntry> Table, LootboxValueBudget Budget)`: pulls from
    the table.
- **`LootboxReward(Guid TypeId, LootboxRarity Rarity)`** replaces the `Lootbox` record. Award
  declarations (achievements, the boss facet, and later quests) hold one. `DisplayLabel` resolves
  the type name through the catalog: "Bronze Adventurer Box".
- **Item definitions per (type, rarity)**: at `Configure` the module registers one `ItemDefinition`
  into `ItemCatalog` for every defined pair. Its `Id` is derived deterministically from
  `(TypeId, Rarity)` (a name-based Guid, the same idea as `BlueprintRegistry.ComposedId`), so it
  stays the same across sessions and saves. Stacking then follows from `AddItem` with no special
  case. Name "{Rarity} {Type} Box", tags `[Tag.Lootbox]`, `GoldValue` 0, a sprite per rarity (a
  tinted frame, see Phase 4), and no `Activator` (see Opening). A reverse map from item id to
  `(TypeId, Rarity)` lives in the catalog.
- **`Tag.Lootbox`** is appended to `Tag`.

### Bound items: a rule on the item, not the tag

`Tag.Lootbox` is for tabs. The "cannot be traded, looted, sold, bought, dropped or destroyed" rule
is a separate `ItemDefinition.IsBound` flag, so a future quest item can reuse it without being
filed under loot boxes. Where it's enforced:

1. `InventoryActions.TryTransferStack` / `TryTransferAllStacksOfItem` refuse a bound stack. This one
   check covers give, take, corpse looting, shop buy/sell, trade staging and plain drag-drop.
2. `ShopActions.CanTrade` returns false for a bound item, so the shop and trade UI grey it out
   through the existing eligibility pass instead of letting a drag fail silently.
3. `InventoryGridContent.BuildItemContextMenu` leaves out Give, Take, Sell All, Buy All and
   "Add to trade" for a bound stack. `UiInputController.TryStartContentDrag` refuses to start a drag
   of one onto anything except the player's own grid. Refusing is the right answer here: the drag
   has no valid destination, so it shouldn't pretend to have one.
4. Drop and Destroy don't exist yet. The TODO entries "Destroyed items" and "Item damage and
   repair" each get a line saying they must respect `IsBound`.
5. The only path that removes a bound stack is opening it.

### Granting

`LootboxActions.Grant(componentManager, lootboxCatalog, entityId, LootboxReward, ushort quantity = 1)`
is the one chokepoint. It resolves the item id and calls `InventoryActions.AddItem`, and it
publishes `LootboxGrantedEvent(entityId, reward)`, which later work (the HUD, a loot box achievement)
can hook onto.

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
  - A notification queued with `ShowImmediately: false` and never opened keeps its box unclaimed
    until the player opens and closes it. That is the literal rule; see Open questions.
- **Boss kills**: `BlueprintDefinition` gains a `Lootbox` facet (`LootboxReward?`), resolved like
  the other facets (a later include replaces an earlier one). The `Boss` trait declares a default
  (Bronze "Boss"), and a named boss such as `GoblinForeman` overrides it. A new
  `BossLootboxAwarder` subscribes to `EntityDiedEvent`. If the dead entity's resolved blueprint
  (spawn record plus applied parts) has a `Lootbox` and `Source.IsEntity(PlayerEntityKey)`, it
  grants the box to the player. It reads the definition, never a component, so it works the same
  whether or not the boss was ever built past a skeleton.
- **Quests, viewer gifts, sponsor gifts**: none of these systems exist yet. Phase 1 defines their
  box types ("Quest", "Viewer Gift", "Sponsor Gift") so the vocabulary is there. Each future system
  calls `LootboxActions.Grant` with a `LootboxReward` on its own award declaration, the same shape
  achievements use.
- **Admin Mode**: the map context menu gets "Grant loot box >" (type, then rarity) next to
  "Spawn here >" and "Apply >". It is the in-game test tool, and it stays.

### Opening

Activating any loot box opens every loot box the player holds.

- **Trigger**: `InventoryGridContent.CanActivate` also accepts a player-owned loot box stack (it
  has no `Activator`). Activate and double-click on one skip `ActionTargetingController` entirely
  and enqueue a request on `LootboxOpenRequests`. Arming a target shape for something with no
  target would be an unexpected action.
- **`LootboxOpeningSystem`** is a plain `ISystem`, StripeCount 1, with no stripe set. It drains
  the request queue, following the "no per-entity population" rule in CLAUDE.md. For each request:
  1. Collect the entity's loot box stacks and expand each to Quantity units.
  2. Sort by rarity ascending, then type `Name` ordinal ascending.
  3. For each unit, roll the contents, grant each reward, and consume one unit
     (`ConsumeItemByStackInstanceId`).
  4. Publish a buffered `LootboxesOpenedEvent(entityId, IReadOnlyList<OpenedLootbox>)`.
     `OpenedLootbox` = `(LootboxReward Box, IReadOnlyList<GrantedItem> Items)` and
     `GrantedItem` = `(Guid ItemId, ushort Quantity, uint StackInstanceId)`, where the stack id is
     what `AddItem` returned.
- **Rolls**: one `SeededRandom` per opening, seeded from the session seed plus a per-player opened
  counter, so seeded runs and tests reproduce. It is never `Random.Shared`.
- **Grant-time item state**: a reward goes through the same grant helper a direct grant would use
  (e.g. `WandGrantEffects.Grant` for a wand, which bakes charges from Intelligence), not a bare
  `AddItem`. A small `ItemGrants.Grant(definition, quantity)` dispatch picks the right helper. If
  one doesn't exist when Phase 3 starts, it's added there and `PlayerKit`/`TreasureChest` move onto
  it too.
- **Rewards are always items.** A non-item reward is authored as a one-time consumable whose
  effects do the work (e.g. a potion that grants a spell). It uses the existing
  `ActionEffect`/activator pipeline, with no loot-box-specific effect path.
- **Action lock**: opening does not take or check the action lock. It isn't a combat action. See
  Open questions.

### Rarity → value

"Rarity determines the value of items": `RandomContents` rolls against a Gold value budget.
Placeholder numbers, roughly ×3 per tier:

| Rarity    | Budget (Gold) | Max pulls |
|-----------|---------------|-----------|
| Bronze    | 10–20         | 3         |
| Silver    | 30–60         | 4         |
| Gold      | 90–180        | 5         |
| Platinum  | 270–540       | 6         |
| Legendary | 800–1600      | 7         |
| Celestial | 2400–4800     | 8         |

Each pull picks from the table entries whose `GoldValue` fits the remaining budget, weighted
toward higher value as rarity rises. When the budget or pull cap is reached, the remainder goes to
quantity of the last pull. Today every item is worth 1–20 Gold, so anything above Gold rarity turns
into large stacks of cheap items until higher-value items exist. The table is tuning data, not
structure.

**Taper by floor**: boxes are front-loaded by content, not by a mechanic. Early achievements are
many and cheap (already the policy in TODO.md's "Achievement content backlog"). Boss box rarity is
per-boss authoring. No per-floor multiplier is built until floors exist (see
`project_floors_vs_layers_design`).

### Results window (Presentation)

**`LootboxResultsWindow`/`LootboxResultsWindowController`**, in `Presentation/UI/Lootboxes/`:
- Opens on `LootboxesOpenedEvent` for the player. If it's already open, the new results replace
  the old ones. It is a movable, closable, vertically scrollable window (Escape closes it through
  `UiLayerStack.HandleEscape` like any other).
- One section per opened box, in opening order. Each section has a header in the rarity color
  ("Bronze Adventurer Box") and a `GridControl` of cells below it.
- The cells are `InventoryItemStackCell` (sprite and quantity badge), with no drag, no context menu
  and no hotbar binding. The quantity shown is how much that box granted, not the size of the stack
  it landed in.
- Hover uses the shared `TooltipController` (the same basic info the inventory grid shows). Click
  calls `ItemDetailsWindowController`:
  - When the reward's `StackInstanceId` still resolves on the player, it uses the stack-backed
    `Open`, so Compare works as usual.
  - Otherwise (the stack was consumed since) it uses a new definition-only
    `Open(ItemDefinition)`: read-only, with no Compare button.
  - `ItemDetailsWindowController` currently docks beside the player inventory window and returns
    early without it. It gains a dock anchor (`Func<Rectangle>`), which the results window supplies
    when it opens details itself. The fallback-ordering lesson from the comparison work applies:
    each open window gets its own anchor, never one shared `Func<Rectangle>` fallback.
- Rarity colors go in a new `LootboxChrome` under `Presentation/UI/Chrome/`, next to the other
  palettes.

## Phases

Each phase ends with a manual in-game check before the next one starts.

1. **Data and granting**: module, catalog, `LootboxRarity` moved, `LootboxTypeDefinition`/
   `LootboxContents`/`LootboxReward`, per-(type, rarity) item definitions, `Tag.Lootbox`,
   `LootboxActions.Grant`, and the Admin Mode "Grant loot box >" menu. Initial types: Adventurer,
   Potion, Weapon, Boss, Quest, Viewer Gift, Sponsor Gift. Check: grant several from Admin Mode;
   same type and rarity stack; the Lootbox tab appears.
2. **Achievement delivery**: `LootboxReward` on `IAchievementDefinition`, the unclaimed-box pool,
   `NotificationDismissed` (minimize excluded), and the claim on close. Delete the old `Lootbox`
   record and the TODO entry. Proposed new achievement for the user's own example: first time the
   player attacks an NPC grants a Bronze Adventurer box (name TBD). Check: close grants; minimize
   doesn't; Close All grants every box.
3. **Bound items and opening**: `IsBound` and its enforcement points, the Activate/double-click
   route, `LootboxOpenRequests`, `LootboxOpeningSystem`, seeded rolls, `ItemGrants`, and
   `LootboxesOpenedEvent`. Check: every trade, loot, sell and drag path refuses a box; opening one
   opens all of them in the right order.
4. **Results window**: the window, the sections, cell reuse, tooltip, details with an anchor and
   the definition-only fallback, rarity chrome, and loot box sprites. Check: visual.
5. **Boss rewards**: the `Lootbox` blueprint facet, `Boss` default, `GoblinForeman` override, and
   `BossLootboxAwarder`. Check: killing a foreman grants a box, killing it with anything other than
   the player doesn't, and a boss that was never built still pays out.

## Tests

- `LootboxCatalogTests`: item ids are deterministic per (type, rarity); an undefined rarity throws
  at registration; mod replacement by `Id`.
- Stacking: two grants of one pair give one stack of 2; different rarities give separate stacks.
- `IsBound`: `TryTransferStack`, `TryTransferAllStacksOfItem`, `ShopActions.TryBuyFromShop` and
  `TrySellToShop`, and trade staging each refuse, with no state change.
- `LootboxOpeningSystem`: rarity-then-name order, every unit opened, stacks removed, the same seed
  giving the same contents, set contents exact, random contents within budget.
- Achievement claim: driven through `NotificationCenter` itself (close, minimize, Close All), not a
  direct call, per the live-testing feedback.
- Results window: driven through `UiInputController` (hover, click, details anchor), not
  direct-call shortcuts.
- `SpawnRecordRebuilderTests` still pass: the blueprint facet is definition-only and adds no roll.

## Open questions (recommendations in bold)

1. **Stack of 3 identical boxes**: 3 sections or 1 "×3" section? **Three.** Each box is its own
   reveal, and it matches "the type and rarity of every lootbox".
2. **Should opening respect the action lock or be refused mid-combat?** **No**, until the Safe Room
   restriction (TODO) replaces it with a stronger rule.
3. **Achievement popup minimized forever**: its box is never claimed. Should unlocking also grant
   after some time, or on "Open All"? **Leave it as specified**: the unread badge already shows it's
   waiting.
4. **Hotbar-bindable?** **No** for now: activating opens everything, so a hotbar slot for one
   specific box is misleading. Revisit with "ItemBindingRule for hotkey-bound consumables".
5. **Boss credit when a companion or another NPC lands the killing blow**: **player-only for now**.
   Revisit with "Corpse looting rights based on damage dealt".
