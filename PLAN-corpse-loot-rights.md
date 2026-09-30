# Corpse Loot Rights and Mob Looting

(Pre-implementation. Replaces TODO.md's "Corpse looting rights based on damage dealt" and "Mobs
looting corpses" entries -- V1 only; "Mobs looting corpses" V2 (preferences by combat style/rarity)
stays in TODO.md. Both entries are deleted when the phase that finishes them lands.)

## Context

What exists today:
- Anyone adjacent can loot any corpse. `MapWindow`'s context menu offers "Loot" (enabled when
  adjacent) for a corpse or a container, and `SecondaryInventoryWindowController.OpenLoot` opens the
  window and marks the target `LootedComponent` the moment it opens, whether or not anything is taken.
  Every item/currency move after that goes through `InventoryActions.TryTransferStack` /
  `CurrencyActions.TryTransfer*` from inside that window, so the window's opening is the only gate.
- `MapViewQuery.GetStatus` draws the LootBag badge only for a corpse/container that still holds
  inventory stacks: white when unlooted, grey once `LootedComponent` is present. Currency is not
  considered, so a corpse holding only Gold shows no badge.
- Nobody tracks damage per source. The final hit's `ActionSource` is kept as `DeadComponent.KilledBy`.
- Damage has four chokepoints, all in `Game/Modules/Health/`: `HealthDamage.Apply`'s Simple path,
  `ComplexHealthDamage.Apply`/`ApplyToAllParts` (via `BodyPartDamageEffects.PublishDamageEvents` /
  `PublishAggregateDamageEvents`), and `BodyPartBurningSystem`, which calls
  `BodyPartDamageEffects.ApplyToPart` + `PublishDamageEvents` directly. DoT ticks (Poison, Burning)
  carry the applier's `ActionSource`, so they can be credited to it; `ContactDamage` carries a
  Terrain source.
- `TestCombatBehaviorSystem` (tiered, before `MovementSystem`) runs self-heal -> melee -> wander.
  NPC inventories are capped at `InventoryCapacity.MaxNonPlayerStackCount` (20 distinct stacks), and
  `TryTransferStack` never merges into an existing stack of the same item.
- IMPLEMENTATION-NOTES.md's "Loot boxes" / TODO.md's "Advanced boss loot box" entry want per-fight contribution
  tracking too. The ledger below is built so that entry can read it at death without a second store.

## Design

### Rules

1. **Damage is recorded per (victim, source entity).** Only `ActionSourceKind.Entity` sources are
   recorded; terrain, Admin and AI damage credits nobody. Self-damage (source key == victim key) is
   not recorded. DoT ticks credit whoever applied the DoT.
2. **Recorded amount = health actually removed**, after IncomingDamage modifiers and capped at what
   the target (Simple) or the hit part (Complex) had left. Overkill is excluded, so one huge final
   hit can't outweigh a whole fight.
3. **The ledger resets when the victim has taken no recorded damage for 30 seconds** (1800 frames).
   The reset is per victim, not per source: a single hit from anyone keeps every contributor's
   total alive.
4. **At death, the top contributor owns the corpse's loot for 30 seconds from `DiedAtFrame`.** Ties
   go to whoever reached their total first (earliest first hit), so the result is deterministic.
   No recorded contributor -> no owner -> anyone may loot immediately.
5. **Rights relax, never tighten.** The corpse is free for all once any of these hold: the window
   has passed, the looter is the owner, the owner is no longer loaded (key doesn't resolve), or the
   owner is itself dead. Since rights only ever widen, checking them when a loot *starts* is
   sufficient -- no need to re-check each drag inside an already-open loot window.
6. **Containers have no loot rights.** Only `DeadComponent` entities are reserved.

### Storage

- **`DamageContributionComponent(EntityKey SourceEntityKey, uint TotalDamageDealt, uint FirstHitFrame)`**
  -- Multi pool on the victim, one instance per source. 16 B. Typical fights have 1-3 sources, so
  finding a source's entry is a linear scan of the victim's own instances.
- **`DamageLedgerExpiryComponent(uint LastDamagedFrame, uint NextTickFrame) : IScheduledTimer`** --
  Packed pool on the victim, driven by a `PackedTimerWheel` in a new `DamageLedgerExpirySystem`
  (plain `ISystem`, no stripe set -- it only drains its wheel). This is the "something happens"
  timer shape (the ledger is cleared).
  - A hit writes only `LastDamagedFrame`. `NextTickFrame` is set once when the ledger is created, so
    per-hit writes don't reschedule the wheel (the wheel ignores writes that leave the deadline
    alone). When it fires: if `LastDamagedFrame + 1800 > now`, re-arm to that deadline; otherwise
    remove every `DamageContributionComponent` on the victim and return true. That is at most one
    reschedule per 30 s per victim instead of one per hit -- this matters for aura DoTs, which tick
    on many entities at once.
  - Frozen victims: the wheel's `SimulationScope` rests the timer and fires it late on resume, which
    clears a stale ledger correctly (nothing can have damaged a frozen entity).
- **`DeadComponent` gains `EntityKey LootOwnerEntityKey`** (`EntityKey.None` = free for all). The
  exclusive window's end is `DiedAtFrame + LootRights.ExclusiveLootFrames`, so no second deadline
  field is stored. +8 B, corpses only.
- **`CorpseLootRetryComponent(uint RetryAfterFrame)`** -- Packed pool on an NPC that failed a loot
  attempt. A plain deadline read by `TestCombatBehaviorSystem` (the "nothing happens, it just stops
  gating" shape): no system, no wheel. Small `initialCapacity`.

None of these go on `EntityFactory.SkeletonComponentTypes`: a skeleton can't be damaged or loot.

### Code shape

- **`DamageLedger`** (Game/Modules/Health/): wraps the two pools. `Record(victimEntityId,
  ActionSource source, uint healthRemoved, long now)` and `TryGetTopContributor(victimEntityId, out
  EntityKey)`, `Clear(victimEntityId)`. Skips dead victims.
- Recording calls sit next to the existing `EntityDiedEvent` publishes, where the removed amount is
  already known: `HealthDamage.Apply`'s Simple path (before - after), and the Complex paths --
  `BodyPartDamageEffects.ApplyToPart` returns the amount it actually removed, and `ApplyToAllParts`
  sums it across parts. `BodyPartBurningSystem` gets the same call.
- **Passing it in**: a `DamageLedger?` parameter placed positionally beside `deadEntities` and
  *without* a default, so each caller (DirectDamage via `ActionEffectContext`, Poison, Burning,
  BodyPartBurning, ContactDamage) states it explicitly. ContactDamage passes it too even though a
  terrain source records nothing, so a future entity-sourced contact effect can't silently miss it.
- **`LootRights`** (Game/Modules/Death/): `ExclusiveLootFrames = 30 * GameTiming.FramesPerSecond`
  and `CanLoot(corpseEntityId, EntityKey looterEntityKey, long now)` implementing rule 5, over the
  `DeadComponent` pool and `EntityKeys`. Returns true for anything without a `DeadComponent`.
- **`DeathSystem.OnEntityDied`** reads `TryGetTopContributor`, writes the owner into the new
  `DeadComponent`, then clears the victim's ledger. (The advanced loot box entry, when it lands,
  reads the ledger in this same spot, before the clear.)

### Player looting (Presentation)

- `MapWindow`'s corpse "Loot" option: `Enabled = IsAdjacentToPlayer && CanLoot(player)`. When
  adjacency holds but rights don't, the label becomes `"Loot (reserved)"` so the disabled state is
  explained rather than silent (design principle: no ambiguous no-ops). `CanLoot` is reached through
  `MapViewQuery`/the interaction view, not by Presentation reading pools.
- Admin Mode bypasses the reservation (it's the in-game test tool).
- `DeadComponent.ToString` includes the owner, so the Admin inspection dump shows it for free.

### NPC looting (`TestCombatBehaviorSystem`)

- New branch after melee, before wander: heal -> attack -> **loot** -> wander. An adjacent enemy is
  still fought first.
- `TryDecideLootCorpse`:
  1. Gate on `CorpseLootRetryComponent.RetryAfterFrame > now` -> skip the branch.
  2. Scan the entity's own footprint tiles ("on top of") and the Adjacent footprint already resolved
     for melee ("next to") for a candidate: `DeadComponent`, no `LootedComponent`, simulated tier
     (same seam rule as `IsAttackable`), and holding at least one stack or any currency.
  3. `LootRights.CanLoot` fails -> write `RetryAfterFrame = now + 1800`, return false (fall through
     to wander; a refused attempt doesn't use the turn).
  4. Rights passed = the NPC opens the corpse: `Merge(corpse, LootedComponent)` right away, the
     same rule as the player opening the loot window (see Looted marking). It is marked even if the
     NPC then can't fit anything, which also stops it (and every other NPC) from rescanning that
     corpse.
  5. Take everything: every stack via `InventoryActions.TryLootStack` (see Stack merging; copy the
     corpse's stacks into a reused buffer first, since transfers remove from the chain being walked),
     then `CurrencyActions.TryTransferAll`. Anything that fits neither an existing stack nor a new
     stack under the NPC's 20-stack cap stays on the corpse. Return true (consumes the decision).
- Opportunistic only: nothing paths an NPC toward a corpse. Wandering past one is enough.
- The system gains `ComponentManager`, `EntityKeys`, `IPlayerQuery`, `LootRights` and the
  `LootedComponent`/`CorpseLootRetryComponent` pools. The footprint scan can share the melee scan's
  pass over `_adjacentTilesBuffer` if benchmarking shows it matters.

### Looted marking

- **Opening a corpse marks it looted, whoever opens it.** The player already marks it when the loot
  window opens (`SecondaryInventoryWindowController.OpenLoot`); an NPC marks it when its rights check
  passes (step 4 above). Taking nothing still counts: "I don't want any of these items" shouldn't
  leave the corpse reading as untouched.
- A reserved corpse is never marked by someone who isn't allowed in: the player can't open it (the
  menu option is disabled) and a refused NPC backs off before step 4.
- `LootedComponent`'s pool comment ("rare, player-action-only") and `initialCapacity: 32` no longer
  hold -- raise the capacity, update the doc.

### Loot badge counts currency

- `MapViewQuery.GetStatus`: a corpse/container "has loot" when it holds any inventory stack **or**
  any Gold/Credits (`CurrencyComponent`, fetched like every other built-in pool). A Gold-only corpse now shows the
  bag.
- An NPC takes currency along with items, so a corpse it fully emptied still shows no badge at all.
  The grey badge appears when something was left behind (items that didn't fit, or a player who
  opened it and took nothing).

### Stack merging when looting

- **`InventoryActions.TryLootStack(componentManager, sourceEntityId, destinationEntityId,
  stackInstanceId, playerQuery)`**: moves a whole stack like `TryTransferStack`, but first merges
  into an existing destination stack that is equivalent -- same `ItemDefinitionId`, same
  `IsDivergent`, and `Override`s both null or `AreEquivalentOverrides` -- up to the destination's
  `GetEffectiveMaxStackSize`. Whatever doesn't fit moves as its own stack, keeping its
  `StackInstanceId`, which needs a free stack slot (`InventoryCapacity`); if there's none, the
  remainder stays on the source with its quantity reduced. Returns true if any quantity moved.
- The equivalence match reuses the predicates `AddItemWithOverride`/`AddDivergentItem` already use,
  pulled into one private helper rather than a third copy.
- A merged stack keeps the destination stack's identity and acquisition fields. A stack that moves
  on its own is re-stamped for the player exactly as `TryTransferStack` does today.
- **Where it's used (looting only):** NPC looting; the loot window's Take / Take All (item and
  merged-stack cells) in `InventoryGridContent`; and a plain drag whose origin is the open loot
  window's target (`SecondaryInventoryWindowController.OpenTargetEntityId`) and whose destination is
  the player. Give, shop buy/sell and trade keep `TryTransferStack`: trade staging undoes a move by
  `StackInstanceId`, and a merged-away stack would have no id left to move back.
- `TryTransferAllStacksOfItem`'s all-or-nothing room check has to count room *after* merging on the
  loot path (a looting variant of it).

## Phases

Stop after each phase for in-game testing.

1. **Damage ledger.** Components, `DamageLedger`, `DamageLedgerExpirySystem`, the four recording
   sites, the `ApplyToPart` return value. Tests: per-source totals; overkill capped; Terrain/self
   not recorded; DoT credited to applier; reset after 30 s of no damage; a hit at 29 s keeps every
   source; expiry re-arms rather than clearing. Admin dump shows the ledger. Benchmark with
   `phase-performance-testing` (aura DoTs are the hot path).
2. **Loot rights + player gating.** `DeadComponent.LootOwnerEntityKey`, `LootRights`, `DeathSystem`
   wiring, the context-menu gate/label, Admin bypass. Tests: top contributor wins; tie -> earliest
   first hit; no contributor -> free; window expiry; owner unloaded/dead -> free; container
   unaffected; driven through the real context-menu path, not a direct `CanLoot` call.
3. **Loot stack merging.** `TryLootStack` and the shared match helper; switch the player's loot
   window Take / Take All / drag-to-player onto it. Tests: merges into an equivalent stack; never
   merges across different overrides or divergence; overflow past the max stack size becomes its own
   stack; no free slot leaves the remainder on the source; Give/shop/trade still move stacks intact.
   Drag tests go through the real `UiInputController` path.
4. **NPC looting.** `CorpseLootRetryComponent`, the new branch. Tests: loots own tile and adjacent
   corpses; skips looted/empty/frozen corpses; refused -> 30 s backoff across all corpses, no looted
   mark; allowed -> marked looted even when the NPC can't fit anything; takes all stacks (merged) +
   currency; leaves what doesn't fit; heal/attack still win. Benchmark again (the branch runs for
   every Random-mode NPC visit that reaches it).
5. **Badge + cleanup.** Currency counts toward the badge, pool capacity/doc, delete both TODO.md
   entries (V2 of mob looting stays), IMPLEMENTATION-NOTES.md "Corpse looting" section updated.

## Decisions

- Opening a corpse marks it looted, whether the player or an NPC opens it, even if nothing is taken.
- An NPC's refused attempt blocks all its corpse looting for 30 s (per NPC, not per corpse). Revisit
  with the future NPC behavior composition work.
- Currency counts toward the loot badge.
- Looting merges into equivalent stacks (player and NPC); give/shop/trade transfers don't -- except that a
  completed purchase (a shop buy, or a completed trade's shop column) merges into the player's equivalent
  stack via `InventoryActions.MergeIntoEquivalentStack` (2026-09-29): a bought item the player already
  carried otherwise became an unbindable Merged Stack cell.

## Comparison with industry practice

| Topic | Common MMO practice | This plan |
|---|---|---|
| Who gets rights | First hit ("tag") in WoW and OSRS's taggable monsters; **most damage** for OSRS's untaggable monsters and Ironman mode; EverQuest moved from first-tag + 51% damage to aggregate damage. | Most damage. Hard to grief by tagging, but a late, high-DPS joiner can take a fight someone else started. |
| Exclusive window | OSRS: drops are owner-only for 60 s, then anyone can see/take tradeable items. | 30 s from death, then free for all. Same idea, half the window. |
| Groups | Group/raid damage is pooled (EQ aggregate damage; WoW grouped players share a tap). | No parties: two goblins fighting the player split credit individually. Pool by party/faction when those exist. |
| Multiple winners | WoW retail: up to 5 ungrouped tappers. GW2: everyone past a threshold (~5-10% of health) gets their own personal loot, capped around 50 players. | Single owner, one shared corpse inventory. Personal loot would need per-looter loot. |
| Support credit | GW2 counts healing/boons toward participation; WoW counts threat. | Damage only. |
| Reset | WoW: you keep the tap while on the threat table; a mob that evades resets to full health and loses its taps. | Timed (30 s without damage) and the victim does **not** heal back. A mob can be worn down, reset, and finished by someone who dealt little total damage. If that's a problem, heal on reset or reset per source. |
| DoTs / pets | Generally, DoTs credit the caster and pets/summons credit their owner (the sources below don't state this directly). | DoTs credit the applier. No pets yet; they'll need an owner redirect in `DamageLedger.Record`. |
| Overkill | Varies by game and isn't well documented (unverified). | Capped at health removed. |
| Mobs looting | Corpse looting is a player-only action in mainstream MMOs. | NPCs loot too. Novel, so it has no tuning precedent. Watch how fast NPCs get rich. |

Sources: [OSRS/RS3 Drops (RuneScape Wiki)](https://runescape.wiki/w/Drops),
[Tap (Warcraft Wiki)](https://warcraft.wiki.gg/wiki/Tap), [Tap (Wowpedia)](https://wowpedia.fandom.com/wiki/Tap),
[Loot (Guild Wars 2 Wiki)](https://wiki.guildwars2.com/wiki/Loot),
[GW2 forum: minimum damage for tagging](https://en-forum.guildwars2.com/topic/84928-minimum-damage-required-for-tagging/),
[MMORPG.com: origin of mob tagging](https://forums.mmorpg.com/discussion/366749/what-was-the-original-purpose-of-mob-tagging-mechanics-in-mmo),
[EverQuest: Advanced Looting](https://www.everquest.com/news/advanced-looting-system).
