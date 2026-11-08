# Dropping Items on the Ground

(From TODO.md "Dropping items on the ground". Started 2026-11-07; landed 2026-11-08 -- all three phases, the fixes after
each in-game test, and the wrap-up. The sections below record what was built and every decision changed on the way.)

## Verdict in one paragraph

An **item pile** is an ordinary entity: a non-blocking blueprint with a `ContainerComponent`, no health, and ordinary
`InventoryItemStackComponent`s and `CurrencyComponent`, so the loot badge, the loot window, Take/Take All and the map's
drop-into-a-container path all work on it unchanged. One pile per tile and layer; dropping where one is adds to it, and an
empty pile is destroyed. Dropping is **targeted**: choosing Drop arms a "Drop" with the item (or currency) it carries, and the
player picks a tile from their own footprint and the ring around it (as built, `ItemDrops.Targeting`: SingleTarget, range 1,
diagonals included), checked by `TargetResolution.IsAimable`, the rule every aim uses. Units
move through the existing transfer path in `InventoryActions`, so a wand's `Override`, a lit unit and `FirstAcquired` travel
with the unit, and `ToggleItemHolderSync` follows with no caller remembering. A pile has no loot
rights, so any NPC that loots corpses also picks up piles. When a neighborhood unloads, its piles are kept in its
`NeighborhoodRecord` and put back when it loads again.

## Decisions (2026-11-07)

- **Dropping arms "Drop".** The player then picks a tile: Adjacent + Self.
- **Piles survive unloading.** They are kept in the neighborhood record when it unloads, and restored when it loads.
- **NPCs pick up from piles, with no loot rights.** That's what will make storage chests and safe rooms (which lock NPCs out)
  matter later.
- **Currency drops like items.**
- **Destroying an item isn't a drop.** It stays TODO.md "Destroyed items".
- **"Drop" is a game rule with its own targeting, not a content action** (2026-11-07). `ItemDrops.Targeting` is a fixed
  `TargetingSpec` (Adjacent | Self, Caster, `GroundOnly`, no range). What is armed is a third kind beside an action and an
  item stack: `MapViewState.ArmedDrop` (`ArmedDrop`: the stack instance id and the quantity, or a currency type and amount).
  A confirm queues `PlayerCommands.QueueDrop(drop, selection)`. Rejected: a content action (`BuiltInActionIds.Drop`) with an
  activation payload carried through the activation request and `EffectContext` for an `ItemDrop` entry. Which stack and how
  many are chosen when it's armed, so no definition can hold them, and a payload would be a new channel in the effect
  vocabulary that only this one entry would use ("no entry knows what applied it").
- **Timing: at once, with no action lock** (changed 2026-11-07, after the first in-game test; it was "as an Immediate action").
  Every drop -- onto the ground or into a container, dragged or from a menu -- lands the moment it is made, even with the
  simulation paused behind an open inventory, and neither waits for the lock nor sets it. A lock would make inventory
  management tedious.
- **A menu Drop works exactly like a drag** (changed 2026-11-08; it was "a Drop onto a chest or a corpse makes a pile there").
  Confirmed on a tile holding a corpse or a container, it goes into it, on any of its footprint tiles; on a standing shop
  it is refused; anywhere else it goes onto the ground. Wherever possible a menu option and its matching gesture behave the
  same: players get several ways to do one thing, and all of them do the same thing.
- **A pile is never marked looted** (`LootedComponent`), not when the player opens it and not when an NPC loots it, so it
  always shows the unlooted bag. A pile has no "opened" state worth showing.
- **"Regenerate" (Admin Mode) keeps the neighborhood's piles.** It unloads and loads, and both halves keep and restore piles,
  so this needs no extra code.
- **A lit item with upkeep goes out in a pile.** A pile has no mana, so a toggle whose periodic effect drains mana ends at its
  first interval (`ToggleUpkeepSystem` switches it off, `ToggleEndedEvent`). Torch and Lantern hold light only. A light worth
  setting down is one without upkeep.

## How it works today

- **Containers.** `TreasureChest` is a `ContainerComponent` with health, currency and stacks. `MapViewQuery` shows the loot
  bag for a container that isn't a shop, or for anything dead, while it holds a stack or currency (`LootBagState`). Opening
  the loot window marks the entity `LootedComponent` (`LootCommands.MarkLooted`). `ContainerDestructionSystem` empties a
  container whose health reaches 0, so a pile with no health is never touched by it.
- **Loot rights.** `LootRights.CanLoot` reserves only a corpse (`DeadComponent`). A non-corpse is always open, so piles need
  nothing there.
- **NPC looting.** `NpcCorpseLooting.IsLootCandidate` accepts only `DeadComponent` holders that aren't looted. It marks the
  corpse looted, then `InventoryActions.LootEveryStack` and `CurrencyActions.TryTransferAll`.
- **Transfers.** `InventoryActions.TryTransferStack` moves a stack verbatim (its `StackInstanceId`, `Override` and lit state).
  It refuses `CanTrade` false (loot boxes) and a destination with no room, and it re-stamps `AcquiredSequence` only onto the
  player. `TryTransferAllStacksOfItem` covers a Merged Stack. `CurrencyActions.TryTransfer`/`TryTransferAll` cover currency.
- **Context menus.** `InventoryGridContent.BuildItemContextMenu` offers "Give"/"Give All" and "Take"/"Take All" against the
  open secondary target. `CurrencyRowContent` mirrors it. `HotbarController` offers "Arm" and "Unbind".
- **Arming.** `ActionTargetingController` arms an action (`ArmedActionId`) or an item stack (`ArmedItemStackInstanceId`),
  closes every closable window, resolves the targetable tiles through `TargetingView`, and confirms on a map click into
  `PlayerCommands.QueueAction`/`QueueItemActivation`.
- **Map drops.** `MapDropHandler` puts a drag onto the first lootable occupant of the tile, through
  `InventoryTransferDropHandler`. A tile with no container refuses an item from an inventory. Its remarks name the ground
  case as the next resolver.
- **Unloading.** `NeighborhoodStreamer.Unload` destroys every entity in the neighborhood (`DestroyEntitiesIn`), announces
  the terrain, and drops the stores. `Load` loads the layout, restores straddling footprints and spawns the plan.
  `NeighborhoodRecord` holds only the seed and the population count, and survives unloading.

## Phase 1 -- Item piles and the drop rule (Game) (built 2026-11-07)

Built as below. Every test passes: 4150 game, 180 Workshop and 13 analyzer tests. Four Workshop launch tests timed out once
under load and passed when run again.

1. **`ItemPile` blueprint** (`Blueprints/Objects/ItemPile.cs`, registered in `BlueprintsModule`): `ItemPileComponent`,
   `ContainerComponent` and `UnlimitedStackCountComponent` (so a drop is never refused for room), `NonBlockingKind.None`,
   no health, glyph `%`, sprite `LootBag-Red`. It has no race, so it's always built at spawn.
2. **`ItemPilesModule`** (`Game.Modules.ItemPiles`, a built-in after Containers). It registers `ItemPileComponent` and
   `PendingItemDropComponent`, and observes `CurrencyComponent` (`ObserveChanges`).
   - **`ItemDrops`**, built by `ItemPilesModule.ItemDropsFor(context)`:
     - `Targeting`: SingleTarget at PathEnd, MaximumRange 1, `GroundOnly`. The rounded radius 1 reaches the diagonals.
     - `TryFindPileAt(tile)`: reads the cell's occupant span.
     - `GetRefusal(dropper, drop[, tile])`, and `TryDrop(dropper, drop, tile)`, which finds or spawns the pile and moves the
       goods.
     - What is dropped is an `ItemDrop`: `OneUnitOf(stack)`, `WholeStackOf(stack)`, `AllOf(currencyType)` or `AllCurrency`.
   - **As built, refusals are their own enum, not `ActivationBlocker` members.** `ItemDropRefusal` and
     `ItemDropRefusalText`: `DropperDead`, `NothingToDrop`, `CannotTrade`, `StackInUse` (a windup or a pending activation of
     that stack), `OutOfReach` (a wall, off the map, or beyond reach) and `ActionLocked`. A drop isn't an activation, and
     `ActivationBlocker` is what an action or item asks.
   - **"In a trade" isn't a Game refusal.** A stack staged in a trade already sits on the trade-offer entity, not on the
     player, so Phase 2 handles it by not offering Drop while a shop is open.
   - **As built, units move through a new `InventoryActions.TryGiveOneUnit`, and the whole stack through `TryLootStack`, not
     `TryTransferStack`.** There was no one-unit transfer between entities: "Give" moves the whole stack. `TryGiveOneUnit`
     tops up an interchangeable stack on the destination or starts one, keeping the unit's Override and lit state. The last
     unit moves as the stack. `TryLootStack` merges into what the pile already holds and keeps the remainder's
     `StackInstanceId`, so dropping onto your own pile reads as one stack.
   - **Empty piles end: `ItemPileEndings` and `ItemPileEndingSystem`.** The stack pool's `ComponentRemoving` and the currency
     pool's `ComponentChanged` queue a pile by `EntityKey`, never while it is being destroyed. The system destroys every
     queued pile that holds nothing, after the drop system each frame.
   - **As built, there is no `ItemDropCommands`.** Presentation reads the refusal through `ActionStateView.GetDropRefusal`
     and places through `PlayerCommands.QueueDrop` (Phase 2).
3. **Queued drops.** `PlayerCommands.QueueDrop(drop, tile)` and `CanQueueDrop()`, a fourth command kind. It waits for the
   lock as a move does, and writing it withdraws a pending activation (and a pending drop is withdrawn by any later
   command). `ItemDropSystem` (a plain system after `ContainerDestructionSystem`) consumes every `PendingItemDropComponent`.
   With the lock blocked it drops nothing. Otherwise it calls `TryDrop` and, on success, sets the dropper's standard lock
   (`StandardActionLockFrames`).
4. **NPC pickup.** `NpcCorpseLooting` is now `NpcLooting` (`TryLootNearby`, `TestCombatBehaviorSystem.TryDecideLoot`).
   `NpcBehaviorModule` `Requires` ItemPiles.
   - A pile is a candidate when it is simulated and holds anything. It has no rights check and no looted mark.
   - Waiting to retry reserved corpses never holds an NPC off a pile.
   - A pile it can take nothing from is passed over, not counted as its decision, so a full NPC never stands looting the
     same pile forever.
5. **The loot badge.** `LootCommands.MarkLooted` skips a pile.

**Found while building: a hotkey slot isn't repointed when its stack leaves the inventory.** Not by Give either:
`RepointAfterUnitMoved` runs only on activation and toggle paths. A slot bound to a dropped stack keeps naming it, and works
again if the same stack (same `StackInstanceId`, which a whole-stack drop keeps) is picked back up. Phase 2 keeps it that
way for a drop from a hotbar slot too.

Tests: `ItemDropsTests` (16), new pile cases in `NpcLootingTests` (3), `PlayerCommandsTests` (2), and the pinned system
order in `BuiltInModulesTests`.

Phase 1 manual test: none on its own. Without the Drop UI there was no way to fill a pile in game, so its checks are in
Phase 2's manual test.

## Phase 2 -- Drop in the UI (Presentation) (built 2026-11-07)

Built as below. Every test passes: 4165 game, 180 Workshop and 13 analyzer tests. Not yet looked at in game.

1. **Arming a drop.**
   - `MapViewState.ArmedDrop` (an `ItemDrop?`) is a third armed kind. `IsAnythingArmed` replaces the action-or-item checks in
     `ActionTargetingController` and `MapWindow`.
   - `ActionTargetingController.ArmDrop(drop, slot)` closes the closable windows, clears the other two kinds and resolves the
     targetable tiles from `ItemDrops.Targeting`.
   - **As built, a drop is aimed through a `DropTargeting` activator private to the controller**: an `IActionActivator`
     carrying `ItemDrops.Targeting` and Immediate timing. So hover, the highlight, the confirm, Escape, right-click and `Tick`
     all treat it exactly as an armed action. It lives in Presentation because every activator in Game must have a content
     codec (`ContentCodecTests`), and this is only a way to aim.
   - The confirm queues `PlayerCommands.QueueDrop(drop, aimedTile)` and disarms.
   - `IsArmedBlocked` reads `ActionStateView.GetDropRefusal`, so an armed drop whose stack is gone disarms on the next
     `Tick`. `CanConfirmArmed` reads `PlayerCommands.CanQueueDrop`.
2. **Refusals in Game.** `ItemDropRefusals` (pools and catalog only) now holds the per-entity checks. `ItemDrops` and
   `ActionStateView.GetDropRefusal` both use it.
   - A new kind, `ItemDropKind.EveryStackOfItem` (`ItemDrop.EveryStackOf(itemId)`), drops a Merged Stack cell through
     `LootAllStacksOfItem`.
   - `TargetingView.IsAimable` passes `TargetResolution.IsAimable` through for Presentation.
3. **One way in from the item windows.** `IPlayerItemDrops` (`CanArmDrop`, `ArmDrop`, `CanDropAt`, `TryDropAt`) is
   implemented by `ActionTargetingController` and carried on `InventoryServices.PlayerItemDrops`. `CanArmDrop` is false
   while a shop is open: a trade decides where items go until it closes.
4. **Inventory grid.** "Drop" (one unit) and "Drop All" (the stack) are offered on the player's own cells, after Give/Take,
   while no shop is open, for an item that can leave its owner. A Merged Stack cell offers "Drop All" (every stack of the
   item). A corpse's, a container's, a shop's or a trade offer's cell offers neither.
   - **As built, a refused drop is shown disabled with no reason text.** `DropdownOption` has no text for it; "Activate" is
     the same.
5. **Hotbar item slot.** A slot bound to an item offers "Drop": one unit of the bound stack, armed with the slot, so the slot
   glows and a re-press of it confirms the drop (handled first in `HandleHotkeySlotPress`, ahead of the item's own toggle or
   arm).
   - **As built, the slot is not repointed.** It keeps naming its stack, as after a Give: it still shows the units left, and
     after dropping the last one it names the dropped stack.
6. **Currency row.** "Drop" (all of the clicked currency) and "Drop All" (both), on the player's own row while no shop is
   the open secondary window. Trade footers are trade-offer entities, so they offer neither.
7. **Dragging onto a tile.** After the container case, `MapDropHandler` treats a drag from the player's own inventory or
   currency row onto a tile with no container as a ground drop, through `IPlayerItemDrops.TryDropAt`. It drops the whole
   stack, every stack of a Merged Stack, or all of one currency, with no arming.
   - It is refused (disabled cursor) beyond the player's tile and the 8 around it, onto a wall, from any other inventory, or
     while a shop is open.
   - A drag doesn't close the window it came from, as a drag into a container doesn't.
   - **As built, there is no red tile.** The disabled cursor is the refusal.
8. **Cursor text.** Built later as plain words, not the item's name: see "Drag arming and cursor text".

Tests: `ItemDropPresentationTests` (12: arming, the 3x3 highlight, confirm in and out of reach, refusals, a shop open,
disarm on a gone stack, the hotbar slot's drop, and the grid menu), new ground cases in `MapDropTests` (5, replacing "an
empty tile refuses"), and `TestPlayerItemDrops` for every item window test.

Phase 2 manual test:
- Right-click a stack of 3 potions, Drop. Expect the inventory to close and your tile and the 8 around it to light up.
  Click a neighbouring tile: expect a bag there and 2 potions left.
- Right-click the potions again, Drop All, and click the same tile. Open the bag: expect all 3, in one stack.
- Arm a Drop and click a tile 2 away. Expect nothing to happen and the drop to stay armed. Escape: expect it disarmed.
- Light a Toxic Idol (a toggle item), Drop All it next to you. Expect its aura to come from the bag and keep going as you
  walk away.
- Bind a stack of 2 potions to slot 2. Right-click slot 2, Drop: expect the slot to glow. Hover a neighbouring tile and
  press 2: expect one potion in a bag and slot 2 still showing 1.
- Drag a stack from your inventory onto a tile 3 away. Expect the disabled cursor and no drop. Drag it onto your own tile:
  expect a bag under you and the inventory to stay open.
- Right-click Gold on your currency row, Drop, and click a neighbouring tile. Expect a bag. Open it and Take All: expect the
  Gold back and the bag gone a frame later.
- Open a shop. Expect Drop disabled in every menu, and a drag onto an empty tile refused.
- Start a wand's windup, then right-click the wand. Expect Drop disabled.
- Stand next to a goblin with a bag of items between you, and let it finish fighting. Expect it to take the bag's contents.

## Fixes after the first in-game test (2026-11-07)

Every test passes: 4164 game, 180 Workshop and 13 analyzer tests. Not yet looked at in game.

1. **A pile wasn't drawn.** `MapDrawOrder` planned a living non-blocking entity only if it was Tiny or Phasing, so a pile
   (`NonBlockingKind.None`) was never drawn, though inspection found it. A new `MapDrawItemKind.FloorObject` draws a living
   non-blocking entity that is neither, lying at its full footprint like a corpse (its visual and the loot-bag badge, no corpse
   marker). An aura anchor is one too, and draws nothing: a blank glyph and no sprite.
2. **Dragged items seemed to vanish; a drop now lands at once with no action lock** (the Timing decision above). An open
   inventory is menu mode, which pauses the simulation, so a drag only queued its drop until the inventory closed, and the
   pile it made was invisible (fix 1).
   - The queued path is gone: `PlayerCommands.QueueDrop`/`CanQueueDrop`, `PendingItemDropComponent`, `ItemDropSystem` and
     `ItemDropRefusal.ActionLocked`.
   - `ItemDropCommands` (Game, in `GameCommands`: `GetRefusal`, `TryDrop`) drops for the player at once through `ItemDrops`.
   - In Presentation, `PlayerItemDrops` implements `IPlayerItemDrops` over it and is what `InventoryServices` and
     `MapDropHandler` carry. `ActionTargetingController` no longer implements it: it only aims, and a confirmed armed drop
     raises `DropConfirmed`, which `PlayerItemDrops` puts down. So the controller gained no constructor dependency.
3. **A drag onto a standing shop is refused** (red, the disabled cursor). With ground drops, it would otherwise have fallen
   through to a pile under the shopkeeper. Items reach a shop only through its window.
4. **The Toxic Idol's aura left behind after taking it back from a pile** no longer occurs in game. Two tests hold it
   (`ALitUnitTakenBackFromAPile_TakesItsAuraWithIt`, `...AfterWalkingAway_RadiatesFromWhereThePlayerNowStands`).

Manual test, on top of Phase 2's:
- Drop anything. Expect the bag sprite on the tile.
- With the inventory open, drag a stack onto a tile next to you. Expect a bag there at once, with the inventory still open.
- Drop several things in quick succession, by menu and by drag. Expect each to land at once, with no wait between them.
- Drag a stack onto a shopkeeper. Expect it refused, and no bag under them.

## Drag arming and cursor text (2026-11-08)

Requested after the fixes above. Every test passes: 4171 game, 180 Workshop and 13 analyzer tests. Not yet looked at in game.

1. **A held inventory drag arms its drop.** `DragDropArming` (an `IContentDropHandler` that claims no drop) counts the
   frames a drag of the player's own stack, Merged Stack or currency is held over the map. After the same grace Item Details
   gives the pointer to come back (`ItemDetailsWindowController.PointerAwayGraceFrames`, 0.3 s, now `internal`), it arms that
   drop through `IPlayerItemDrops.ArmDrop`, which closes the inventory while the drag goes on.
   - Leaving the map before the grace starts the count again.
   - `MapWindow` keeps the hovered tile during a drag once a drop is armed, so the 3x3 and the hovered tile show where the
     release will land. The release is still `MapDropHandler`'s.
   - When the drag ends, however it ends, the drop it armed is disarmed (`ActionTargetingController.DisarmDrop`).
   - What a drag puts down is now `DragPayload.TryGetGroundDrop`, shared by both handlers.
2. **Cursor text: "Drop", "Add", "Unbind".** `DropCursorText` is the one owner of these words on the shell's
   `CursorTextContent`. It shows a persistent message and hides only what it showed, so another persistent message (Item
   Details Comparison's) isn't ended by it.
   - "Drop" while any drop is armed, from a menu or by a drag, following `ActionTargetingController.ArmedDropChanged`.
   - "Add" while a drag-armed drop is held over a container it would go into (a corpse, or a container that isn't a
     standing shop).
   - "Unbind" while a drag from a hotbar slot is held anywhere but another slot.
3. **Confirmed, unchanged: a drag off a hotbar slot never drops.** It only unbinds the slot (`HotbarBindingDropHandler`), and
   `DragPayload.TryGetGroundDrop` refuses any payload with an `OriginSlot`. Only the slot's "Drop" menu option drops from the
   hotbar.

Tests: `DragDropArmingTests` (7).

Manual test:
- Open the inventory and drag a stack onto the map, holding it there. Expect the inventory to close after a moment, the drag
  to go on, your tile and the 8 around it to light up, the hovered one to follow the cursor, and the cursor to read "Drop".
  Release on a lit tile: expect a bag there.
- Do the same, but come back over the inventory within a moment. Expect it to stay open.
- Hold a drag over a chest or corpse next to you once it has armed. Expect "Add", and the item to go into it on release.
  Over a shopkeeper, expect "Drop" with the refused cursor.
- Release a drag-armed drop on a tile out of reach. Expect nothing dropped, and nothing left armed.
- Right-click a stack, Drop. Expect "Drop" at the cursor until you click a tile or press Escape.
- Drag an item off a hotbar slot over the map. Expect "Unbind", and on release the slot unbound and the item still in your
  inventory. Over another slot, expect no text.

## "Bind", and a menu Drop that works like a drag (2026-11-08)

Requested after the drag arming above. Every test passes: 4174 game, 180 Workshop and 13 analyzer tests. Not yet looked at in
game.

1. **"Bind" over a hotbar slot.** A drag of anything a slot can hold, from the player's inventory or from another slot, reads
   "Bind" over a slot (`DragDropArming`, `DropCursorText.BindText`). A Merged Stack or currency shows nothing there.
2. **One drop rule in Game, for every way to drop.** `ItemDrops` now decides where a drop goes, so a menu Drop and a drag
   can't differ:
   - A tile holding a corpse or a container takes the drop into it. A corpse wins over a container, which wins over an item
     pile. Any of its footprint tiles works, as long as its footprint touches the dropper's.
   - A corpse whose loot is reserved for someone else refuses (`LootRights`). A container with no room for any of it refuses
     (`NoRoom`). A standing shop refuses (`IntoShop`).
   - Any other tile takes the drop onto the ground, in reach as before.
   - New refusals: `IntoShop`, `LootReserved` and `NoRoom`. `ItemDrops` now takes the `SimulationClock` (for loot rights),
     and `ItemPilesModule` `Requires` Shops.
   - What moves is the drop's own kind: one unit, the stack (merged into what the container holds, as Take does), every
     stack of a Merged Stack, or currency.
3. **Every player drop goes through it.**
   - A drag of the player's own goods onto a container now goes through `IPlayerItemDrops.TryDropAt`, and its green or red
     wash reads `CanDropAt`. A drag from any other inventory (a corpse's or a chest's window) still goes through
     `InventoryTransferDropHandler`.
   - An armed drop is confirmed on the exact tile clicked, or under the cursor for a slot re-press, through
     `ActionTargetingController.PlaceDrop`. It replaces the `DropConfirmed` event, and `PlayerItemDrops` sets it. So a large
     corpse's far footprint tile works, even outside the 3x3. A refused placement leaves the drop armed.
4. **An armed drop shows what a drag shows.** `ArmedDropHover`, fed by `ActionTargetingController.ArmedDropHovered` every
   update while a drop is armed, lights the container under the cursor green or red (a standing shop always red) and has
   `DropCursorText` read "Add" there. `DropContainerLookup` is the one container lookup it, `MapDropHandler` and the drag
   share. The drag handler no longer computes "Add" itself.

Tests: in `ItemDropsTests`, every footprint tile of a Goblin Foreman's corpse, a reserved corpse, a shop, a chest and a full
one, and a container out of reach (5). `MapDropTests` now checks that player drags go through the rule on every footprint
tile, with green and red (3, replacing 7 that tested the old transfer). In `DragDropArmingTests`, "Bind", and an armed drop's
"Add" and wash (2).

Manual test:
- Drag an item, an action, or an item off another slot over a hotbar slot. Expect "Bind". Drag a Merged Stack or Gold there.
  Expect nothing.
- Kill a Goblin Foreman. Right-click a stack, Drop, and click each of its four tiles in turn, including the far ones. Expect
  each unit to go into the corpse, and "Add" with a green wash while hovering it.
- Drop the same way onto a chest. Expect it to go in. Onto a shopkeeper, expect a red wash, "Drop", and nothing happening.
- Drag a stack onto each tile of the Foreman's corpse. Expect the same results as the menu Drop.

## A menu Drop carries the drag ghost and the refusal cursor (2026-11-08)

Requested after the section above. Every game and analyzer test passes (4175, 13). The Workshop launch tests time out when
the suite runs in parallel on a busy machine; all 180 pass run serially, and none of them touch drops.

1. **The ghost.** While a drop is armed and nothing is dragged, `DragGhostContent` (which now takes `MapViewState`) draws the
   same ghost a drag of it would: the stack's item, a Merged Stack's item, or the currency (Gold for "Drop All" currency), at
   a hotbar slot's size (`HotbarContent.SlotSize`), since a menu has no picked-up element to take a size from.
2. **The refusal cursor.** `ActionTargetingController.IsArmedConfirmRefused` refuses an armed drop over a tile that wouldn't
   take it -- out of reach, a wall, a standing shop, a full or reserved container -- and off the map, through
   `CanPlaceDrop`, which `PlayerItemDrops` sets beside `PlaceDrop` to the same rule (`CanDropAt`). `MapWindow` and
   `ActivationRefusalInputHandler` already show the disabled cursor whenever that is true.

Tests: `ItemDropPresentationTests` (1: in reach, out of reach, off the map). The ghost is drawing only: check it in game.

Manual test:
- Right-click a stack, Drop. Expect its icon to follow the cursor, as when dragging it. Move over a tile 2 away, a wall and a
  shopkeeper: expect the disabled cursor on each. Over your own tile, a neighbour or a corpse beside you: expect the normal
  cursor.
- Right-click Gold, Drop. Expect the Gold icon at the cursor.

## Phase 3 -- Piles kept by the neighborhood record (built 2026-11-08)

Built as below. Every test passes: 4180 game, 180 Workshop (run serially; see above) and 13 analyzer tests. Not yet looked
at in game.

1. **Keep on unload.** `NeighborhoodStreamer.Unload` publishes `NeighborhoodUnloadingEvent(cellX, cellY, record)` (new,
   `Game.Floors`, beside `NeighborhoodPopulatedEvent`) before it destroys anything.
   - It is published when the unload job starts, so an eviction cancelled before it starts keeps nothing, and its piles
     simply stay in the world.
   - `ItemPileKeeping` (`Game.Modules.ItemPiles`, subscribed by `ItemPilesModule`) writes each pile in the neighborhood into
     `NeighborhoodRecord.KeptItemPiles` as an `ItemPileSnapshot`: position, the stack components themselves (so
     `StackInstanceId`, `AcquiredSequence`, `Override` and lit state are exactly kept), and the currency. The streamer then
     destroys the pile with everything else.
2. **Restore on load.** After the population is spawned, the streamer publishes `NeighborhoodPopulatedEvent`.
   `ItemPileKeeping` puts each kept pile back (`ItemDrops.TrySpawnPileAt`, now public, or joining a pile already on the
   tile), adds its stacks and currency, and clears the list. A lit unit's toggle comes back through `ToggleItemHolderSync`
   as its stack is added.
3. **Edge cases.**
   - A pile whose tile is now blocked moves to the nearest open tile within `ItemPileKeeping.RelocationRadius` (3) in the
     same neighborhood, or is lost with its contents when there is none.
   - A load dropped before it starts publishes nothing, so the list waits for the next one.
   - "Regenerate" unloads and loads, so it keeps piles.
   - Unloading and loading twice doesn't double a pile.
4. **As built, the record names a module type.** `NeighborhoodRecord` (`Game.World`) holds `List<ItemPileSnapshot>`
   directly, as the request asked ("kept in the neighborhood record"). It is the list "Save and load Beyond neighborhoods"
   will serialize.

Tests:
- `NeighborhoodStreamerTests` (1): a real regeneration keeps a pile's divergent stack by `StackInstanceId`, and its Gold and
  Credits.
- `ItemPileKeepingTests` (4): keep and restore through the module's subscriptions, a pile now on a wall moving one tile, a lit
  unit coming back lit with its toggle on, and no doubling.

Phase 3 manual test:
- Drop a stack, some Gold and a lit Toxic Idol in a few piles. Walk away until that neighborhood unloads (Admin Mode
  neighborhood borders; two neighborhoods away, then past the cache of three), and walk back. Expect each bag where you left
  it, with the same items, the Gold, and the idol's aura glowing again.
- Do the same, but use Admin Mode's "Regenerate" on that neighborhood from far enough away. Expect new creatures and the
  same bags.
- Walk away and turn straight back before the neighborhood unloads. Expect the bags untouched.

## After the last phase (done 2026-11-08)

- IMPLEMENTATION-NOTES "Item piles and dropping" (Game), beside "Corpse loot rights and mob looting", whose NPC line now
  names `NpcLooting`.
- CLAUDE.md: a "Dropping" bullet in Targeting (the one drop rule, the armed drop behaving like the drag, piles kept by the
  record), and `MapDropHandler`'s description in UIEngine.
- TODO.md: "Dropping items on the ground" deleted; `NpcCorpseLooting` renamed in "Mobs looting corpses by preference" and the
  multithreading notes; "Areas that follow the player" notes that piles are already kept. PLAN-unified-pointer-picking.md's
  ground-resolver note and PLAN-npc-behavior-composition.md's `NpcLooting` references updated.
