# Long-Term TODOs

Non-urgent architectural items worth revisiting later -- things noticed in passing that don't block current work, not a sprint backlog. Organized by layer (Engine, Game, Presentation, Global -- cross-cutting items that don't belong to a single layer), each split into High/Low priority.

## Engine

### High Priority

#### Inventory system

Storage + viewing landed (`Game/Modules/Inventory/`, `Presentation/UI/Inventory/`) -- infinite per-entity storage, identical-item stacking, an entity-wide and a per-stack disabled flag, and a read-only management window behind a new HUD folder. Built entirely on the existing generic `MultiComponentPool<T>` (see `NonBlockingComponent` for the same "many-per-entity" shape) -- no new Engine-layer primitive was actually needed. Still open: persisted per-slot modification state, so an item genuinely diverges from its `ItemDefinition` rather than always reverting to it -- e.g. a partially consumed potion stays partially consumed while sitting in inventory. Deliberately not designed for yet (`InventoryItemStackComponent` intentionally carries no placeholder field for it -- the shape of ammo-count vs. limited-uses vs. rolled-crafting-mods is different enough per case that guessing one now risks being wrong later).

### Low Priority

#### Equipment

Engine-side equipment support (slots, equip/unequip mechanics). Companion to the Game-layer equipment rules and the Presentation-layer equipment menu below. Unblocked now that Inventory (above) exists -- equipping is expected to move an `InventoryItemStackComponent` stack into a slot rather than invent its own separate storage.

## Game

### High Priority

#### Inventory management rules

Item interactions, storage rules, restricted items, etc. Governs how the Engine-layer inventory system above is actually used. Base storage and identical-stack merging (exact `ItemDefinition` match only) already exist -- remaining scope is interaction/restriction rules: stacking beyond exact-match, what can't be picked up by whom, interactions between items.

#### Consumable items

Items that get used up -- a potion drunk, a scroll read, ammo spent. Needs the item-instance-divergence design noted under Inventory system above (a consumable's remaining-uses count is exactly the kind of per-slot state that doesn't exist yet) plus an actual "use" action, which interacting-with-items work (out of scope for the storage/viewing pass that landed Inventory) will need to define.

#### ConsumableEffect effect shape doesn't scale

`ConsumableEffect` (`Game/Modules/Inventory/ConsumableEffect.cs`) grows a new dedicated field every time a consumable needs a new kind of effect -- `HealFraction`, `ManaFraction`, and now `HotkeySlotGrant` (the Hotkey Expansion Potion), each read independently by `ConsumableActivationSystem.ApplyPotionToTarget`. That's fine at 3 effects, but a real consumable system will eventually want dozens or hundreds of distinct effects (buffs, debuffs, teleports, summons, ...), and one dedicated record field per effect doesn't scale to that -- revisit with a real data-driven/composable effect list (closer to how `AbilityEffect`/`AbilityEffectResolver` already handle an ability's own varied effects) before adding a fourth or fifth one-off field here.

#### Scrolls (requires restructuring actions into ActionEffects/ActionActivators)

A scroll is a one-time-use item that triggers an ability-like effect without a caster needing mana or the ability itself, the same way a potion delivers a fixed effect today. Landing it needs splitting the current ability/consumable action shape into two pieces: **ActionEffects** -- what an action actually does (damage, heal, status effects, optional resource costs), the same shape `AbilityEffect`/`ConsumableEffect` already have -- and **ActionActivators** -- how, how often, and which of an ActionEffect's optional costs actually apply. A spell activator requires and spends the mana cost; a potion activator is one-time-use with splash targeting; a scroll activator is one-time-use with its own special targeting rules; a wand activator (see the Wands item below) spends a charge instead of mana. ActionActivators are what get added to inventories and hotbars; ActionEffects themselves are never added directly anywhere -- they're only ever triggered by whichever ActionActivator owns them. This is the same generalization the ConsumableEffect item above already flags as eventually necessary -- scrolls (and wands) are the concrete features that finally require doing it, since a scroll, a wand, and a spell all need to share the exact same effect definition under different activation rules.

#### Move inventory items to the hotbar

`ItemHotkeyBindingComponent` (`Game/Modules/Inventory/Components/`) plus `ConsumableActivationSystem`/`ActionTargetingController`'s item-arm/target/confirm/double-tap path have landed -- a slot can reference an item and activate it (splash-throw or double-tap-self for potions), separately from `ActionHotkeyBindingComponent` (renamed from `HotkeyBindingComponent`, the ability-only original). `Presentation/UI/Content/HotbarContent.cs` still only *renders* ability slots though (Phase 4), and the only way to actually bind an item to a slot today is `PlayerBlueprint`'s TEMPORARY hardcoded grant -- real click-and-drag assignment (Phase 5) still depends on the Standard widget set item below.

#### Shops and storage containers

Reuse the same `Game/Modules/Inventory/` storage any entity already gets -- a shop or a chest is just another entity with `InventoryItemStackComponent` stacks, no new storage primitive needed. What's missing is the trade/transfer UI and rules (pricing, what a shop restocks, container capacity if any).

#### Melee attack implementation (landed)

For NPCs and the player. Attacking sets the same shared ActionLockComponent that movement sets on a successful move, creating a tactical decision between moving more vs. attacking more -- choosing to attack this window means not moving this window, and vice versa; the decision/execution split behind `TestCombatBehaviorSystem`/`MovementSystem` (see the Goblins entry above) is what makes that tradeoff actually hold for NPCs, not just the player. Targets any entity in Adjacent's resolved footprint (the ring around the caster, excluding the caster's own tile(s) -- see `TargetShapeResolver`) -- deliberately *not* restricted to entities with physical collision (Blocking): a non-Blocking entity (e.g. Tiny/Phasing, or one with no `HealthComponent` at all) is still a valid target, since this allows status effects to be applied to otherwise-immortal entities. The "immortal but affectable" case is proven out already: `AbilityEffectResolver` grants `AbilityEffect.StatusEffects` through the shared `StatusEffectAuraApplierRegistry` (`Game/Modules/StatusEffects/`) regardless of whether the target has a `HealthComponent` -- an ability's own `AbilityEffect.StatusEffects` (e.g. a future Paralysis-on-hit melee ability, `Game/Modules/Paralysis/`) can use this path. Punch is the concrete example (`CoreAbilitiesModule.PunchId`, `TargetShape.Adjacent`); the player has always had it via the hotbar, and Goblins (and any other race carrying the right components) now activate it through `TestCombatBehaviorSystem`.

#### AbilityEffectResolver damage/heal consistency

`AbilityEffectResolver.Apply` (`Game/Modules/Abilities/AbilityEffectResolver.cs`) treats damage and healing asymmetrically today: damage is scaled by the *caster's* `StatModifierTarget.OutgoingDamage` before `HealthDamage.Apply` further reduces it by the *target's* `IncomingDamage` -- a real two-sided pipeline. `TryApplyHeal` has no equivalent -- `HealFraction` is only ever multiplied by the target's (modifier-adjusted) `MaximumHealth`, with no caster-side "healing power" or target-side "incoming healing" modifier in the chain at all. Once base stats (see the Stats item below) and Equipment (see the Equipment items below) can actually modify incoming/outgoing damage *and* healing, revisit this resolver so both paths go through the same caster-then-target modifier shape -- e.g. a `StatModifierTarget.OutgoingHealing`/`IncomingHealing` pair mirroring `OutgoingDamage`/`IncomingDamage`, consumed the same two-stage way.

### Low Priority

#### Show runner race

Randomly selected. Affects UI appearance, and gives a bias towards selected quests and enemy types.

#### End of level staircase

Game-side logic for descending/ascending a level. See the matching Presentation item below for the visual/interaction side.

#### Random map generation v1

#### Equipment

Game-side equipment rules (what can go in which slot, stat effects of equipping). Companion to the Engine-layer equipment item above and the Presentation-layer equipment menu below. Unblocked now that Inventory exists (see the Engine-layer Inventory system item).

#### Wands

A Wand is an Equipment item (see above) that must be equipped to activate, unlike a potion or scroll which activates straight from a hotbar slot. It carries a limited number of charges, spends one per activation via its own ActionActivator (see the Scrolls item above), and is destroyed once its charges run out. Depends on both ActionEffects/ActionActivators and Equipment landing first -- a wand's activator needs the ActionActivator split to express "spend a charge instead of mana or a one-time use," and equip/unequip mechanics to gate activation on actually being equipped.

#### Stats (infrastructure landed -- consumers still TODO)

`Game/Modules/AbilityScores/` now exists: `AbilityScoreComponent` (base value 1-300, precomputed `Total`) for the 5 Core scores (Strength, Intelligence, Constitution, Dexterity, Charisma) and 2 Hidden scores (Luck, Wisdom) never shown to the player or touched by level-up. Modifiers reuse `StatModifierComponent`/`StatModifierTarget` (`Game/Modules/StatModifiers/`) rather than a separate list -- grant one via `AbilityScoreEffects.GrantModifier`, not raw `StatModifierEffects.Apply`, so `Total` stays in sync (it's precomputed eagerly on grant/expiry, not lazily on read like every other stat -- see `AbilityScoreComponent`'s own doc comment). The player rolls randomized starting values (2-10, clustering 3-7); every other race (Goblin/Fairy/Ghost) currently defaults to a flat 5 across all 7 scores, adjustable in a balance pass. Remaining work:

- **Split hidden ability scores into composites.** Luck and Wisdom (and future hidden scores) should eventually be derived from combinations of *other* hidden ability scores rather than being standalone base values. Not designed yet -- needs its own pass once there are enough hidden scores for composition to make sense.
- **Wire the concrete "modifies" behaviors.** Strength->melee damage (retire the hardcoded `PunchDamage` consts in `PlayerBlueprint`/`Goblin`/`Fairy`/`Ghost`), Constitution->`MaximumHealth`(x10) still open (`HealthRegen` and potion cooldown -- `PotionCooldownEffects.ComputeDurationFrames`, 20s at total 1 down to 5s at total 300 -- have landed), Dexterity->`ActionLockComponent` duration (100% at 1 dex down to 25% at 300), Intelligence->mana once the Mana item below lands, Charisma->shop/charm/pet-bond mechanics once those exist, Luck->loot/AI once those exist.
- **Non-player starting ability scores.** Give race/class blueprints their own baseline scores instead of the flat default-5 placeholder above.
- **Level-up modifying Core scores.** Flat increases from the future level-up process (Hidden scores explicitly excluded). See the matching Presentation stats window item below.

#### Item weight and carry capacity scaling with Strength

No item has a weight today (`ItemDefinition`/`InventoryItemStackComponent`), and inventory storage is unlimited (see the Inventory system item above). Add a weight field to items and a carry-capacity limit derived from the holder's Strength `AbilityScoreComponent.Total`, then gate picking up (or otherwise receiving) an item on it not exceeding that capacity -- the same kind of restricted-pickup rule the Inventory management rules item above already anticipates. A concrete instance of the Stats item's own "wire the concrete modifies behaviors" bullet, which doesn't yet cover Strength -> carry capacity specifically.

#### Mana

A mana system, using `HealthComponent`/the health bar (`Game/Modules/Health/`) as a template -- a current/maximum pool plus regen, the same shape health already has. Heal (`Game/Modules/Abilities/CoreAbilitiesModule.cs`) should cost 2 MP and Magic Missile 5 MP once this lands -- both are free to cast until then. Starting `MaximumMana` should equal Intelligence's `Total` (`Game/Modules/AbilityScores/`) now that ability scores exist.

#### Scroll and spell durations scaling with Intelligence

Once scrolls and ActionEffects/ActionActivators exist (see the Scrolls item above), a spell or scroll's duration-based effects (buffs, DoTs, status effects granted through an ActionEffect) should scale with the caster's Intelligence `AbilityScoreComponent.Total` -- higher Intelligence extending how long the effect lasts, the same way Constitution now scales the potion cooldown (`PotionCooldownEffects.ComputeDurationFrames`) rather than leaving it flat. Needs the ActionEffect duration field(s) to exist as a real concept first, which they don't until the Scrolls restructuring lands.

#### Damage types

No damage-type concept exists anywhere in `HealthDamage`/`AbilityEffect` today -- every hit is an undifferentiated number. Starting set: Magic, Blunt, Explosive, Slashing.

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

#### Goblins attack adjacent targets with default melee instead of moving (landed, temporary)

Implemented via `TestCombatBehaviorSystem` (`Game/Modules/NpcBehavior/Systems/`): a self-heal -> melee-adjacent-threat -> wander priority chain for any `MovementMode.Random` entity, deliberately generic rather than Goblin-specific (no hardcoded race check -- any race carrying the right components, e.g. a Punch `AbilityInstanceComponent`, gets the behavior for free). Melee is not restricted to Blocking targets only -- a non-Blocking (Tiny/Phasing) entity sharing an adjacent tile is still attackable, matching `AbilityEffectResolver`'s own dual Blocking/non-Blocking loop.

Two follow-ups this landing deliberately leaves open:

- **Behavior composition.** `TestCombatBehaviorSystem` is a single hardcoded if/else chain, explicitly named as a stand-in -- overall entity behavior should eventually be a *composition* of small independent behaviors (self-heal, engage-adjacent-threat, flee, wander, ...) arbitrated by a real per-race-configurable priority/utility system (e.g. a goblin gets a random set of behaviors from a predetermined list -- aggressive, cowardly, prefers-melee, prefers-potions, etc.), not one fixed chain every Random-mode entity shares. One concrete, currently-accepted consequence of the current generic-but-unconfigurable filter: a Fairy (which also carries Punch) will attack *another* Fairy under the plain "player or Fairy" attackable-check, since nothing excludes "an entity of my own race" -- exactly the kind of rule the future composition system should make configurable.
- **Generalizing the "turn claimed" signal.** The decision/execution split this required (`TestCombatBehaviorSystem` runs before `MovementSystem` every frame, see both systems' own doc comments) means `MovementSystem` now has to check `_pendingAbilityActivations`/`_pendingConsumableActivations` presence directly to know whether something already claimed an entity's turn this tick -- i.e. it has to know about every specific kind of Pending*Component that could preempt it, which doesn't scale as more action types are added. A single shared "turn claimed" marker any decision-producing system could set, and any execution system could check generically (instead of each execution system enumerating every possible preempting component by name), is the seam a future pass should replace this with.

#### User feedback for actions is missing entirely

Casting a spell, cancelling a spell, an AOE effect landing, a melee effect landing, and a status effect being applied all currently have no player-visible feedback (visual/audio/etc.) beyond the underlying state change itself -- e.g. `AbilityEffectResolver.Apply` and `AbilityActivationSystem`'s cancel path do their work silently. Flagged as a future item; no design yet.

#### Corpse decay/destruction and destructible terrain

`DeathSystem` (`Game/Modules/Death/`) deliberately never calls `EntityManager.DestroyEntity` -- a corpse is reclassified non-Blocking (`World.ConvertToNonBlocking`) and marked `DeadComponent`, but stays a real, fully-populated entity indefinitely (design intent: a future corpse-looting mechanic, see the Achievement content backlog item below, needs the entity's data to still exist). `EntityManager.DestroyEntity` (full removal, all components gone, id freed for reuse) is reserved for a genuinely separate, deliberate action -- e.g. a corpse-decay timer or "loot then destroy" step once Inventory exists. The same primitive would also apply to a future destructible-terrain entity (e.g. a breakable wall): that case skips `DeadComponent`/the corpse system entirely, since it was never a `HealthComponent`-driven creature death, and would just call `DestroyEntity` directly on whatever triggers its destruction.

#### Self damage buff ability

An example FreeCast or Immediate ability that raises the caster's own outgoing damage for a duration -- exercises the ability system on a non-damage-dealing, self-targeted effect.

#### Toggle poison aura ability

A FreeCast-style ability that turns an existing Poison/StatusEffectAura source on/off around the caster -- exercises FreeCast's "usable during an Action Lock" behavior against the existing aura machinery.

#### Body parts

- Plan first
- Use multi-components somehow
- Position matters -- e.g. lava should damage feet first

#### Movement System

- `SeekTarget` movement mode

#### Achievement lootbox delivery

Achievements can name a `LootboxReward` (rarity + box type, see `Game/Modules/Achievements/LootboxReward.cs`), but nothing delivers it yet. `InventoryActions.AddItem` (`Game/Modules/Inventory/InventoryActions.cs`) is now available as the actual delivery primitive -- unblocked, but `AchievementModule`'s unlock path still doesn't call it, only describes the reward in the notification. Lootboxes themselves can only be *opened* in Safe Rooms once opening exists as a mechanic -- this is not a purchased gambling item, it's a pre-set reward tied to how it was earned.

#### Corpse looting

Opening the player's inventory and a dead entity's inventory side-by-side. The corpse stays a real, fully-populated entity after death specifically so this works (see the Corpse decay/destruction item above) -- once it has its own `InventoryItemStackComponent` stacks, this is a second `InventoryManagementWindow`-shaped view (`Presentation/UI/Inventory/`) targeting the corpse's entity id, opened alongside the player's own. Ties to the achievement backlog's "Loot a corpse for the first time" bullet below.

#### Achievement content backlog

The Achievement system (`Game/Modules/Achievements/`) currently ships fifteen achievements ("Loner", "You've Inflicted Damage on a Mob", "Unarmed Combat", "Early Adopter", "Inert Gas", "You've killed a mob!", "Empty Pockets", "Drinking Problem", "You're a wizard, apprentice", "What big muscles you have!", "Unbreakable", "The Shanghai Kid", "Revenge of the Nerds", "Killer Queen", "Min-Maxer") to prove the pipeline; the rest is a deliberate, incremental backlog -- a few added alongside each future feature rather than all at once. Volume/pacing target: many low-value achievements early (deliberately "drowning the player in low-level loot boxes" at the start), tapering to fewer, higher-value ones by the midgame.

TODO: every achievement currently has exactly one fixed Description string. Once there's enough content to make it worthwhile, give each achievement a pool of possible descriptions (and pick one at random on unlock) instead of always showing the same line.

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

Several depend on systems that don't exist yet (a real companion/party concept + Human race, levels/experience, magic/spell gear, corpse looting) -- implement each achievement once its underlying system actually lands, not before.

`LonerAchievement` (`Game/Modules/Achievements/Definitions/LonerAchievement.cs`) unlocks unconditionally on `Game.World.EnteredDungeonEvent`, published once by `GameLoop` right after `_playerSpawned` flips true (so `IPlayerQuery.PlayerEntityId` is already assigned by the time the handler reads it -- no timing hazard the way the old `EntityMovedEvent` spawn-sentinel trigger had). Once a real companion/party concept exists, this needs to actually check for a Human-race companion near the player at spawn instead of always succeeding.

`UnarmedCombatAchievement` (`Game/Modules/Achievements/Definitions/UnarmedCombatAchievement.cs`) unlocks on the same `EnteredDungeonEvent` event, same unconditional reasoning as `LonerAchievement` above (no equipment or start-equipment-selection system exists yet, so every player is unarmed today). Revisit once equipment/start-equipment selection lands: it should then check whether the player actually chose to start without a weapon.

`EmptyPocketsAchievement` (`Game/Modules/Achievements/Definitions/EmptyPocketsAchievement.cs`) unlocks on the same `EnteredDungeonEvent` event, same unconditional reasoning as `LonerAchievement`/`UnarmedCombatAchievement` above -- Inventory now exists, but the player starts with 5 Health Potions (`CoreItemsModule`, granted in `PlayerBlueprint`), so every player's inventory is still non-empty today for an unrelated reason. Revisit once start-equipment selection lands and the player's starting kit is no longer hardcoded: it should then check whether the player's inventory is actually empty (`InventoryQueries.CopyStacksForEntity`).

`SpellCasterAchievement` (`Game/Modules/Achievements/Definitions/SpellCasterAchievement.cs`) unlocks on `Game.World.AbilityActivatedEvent` (published by `AbilityEffectResolver.Apply` for every successful activation regardless of category), filtered by a real `ability.Tags.Contains(Tag.Spell)` check via `AchievementTriggerContext.Abilities` -- every Spell-tagged ability qualifies automatically, including the starter Heal spell (`CoreAbilitiesModule`), which makes this trivially easy to earn.

`BigMusclesAchievement`/`UnbreakableAchievement`/`ShanghaiKidAchievement`/`RevengeOfTheNerdsAchievement`/`KillerQueenAchievement` (`Game/Modules/Achievements/Definitions/`) each unlock the first time one core ability score's *base* value (ignoring any `StatModifierComponent`-driven `Total`) reaches 100; `MinMaxerAchievement` unlocks once all five reach the 300 cap simultaneously. All six react to `AbilityScoreBaseValueChangedEvent` (`Game/Modules/AbilityScores/AbilityScoreBaseValueChangedEvent.cs`), which only `AbilityScoreEffects.SetBaseValue` publishes -- nothing calls that method yet, since no level-up or "item of divine suffering" system exists (see the Experience and level up system item above), so none of these six can unlock today. They start working the moment either feature calls `SetBaseValue` to permanently raise a score. All six currently reward "None (TODO: 3 upgrade choices)" -- the reward notification always shows "You've received an upgrade!", but there's no upgrade-choice system yet to actually grant, matching the reward wording above.

#### Boundary-aware ProcessingTierSystem recompute

`ProcessingTierSystem` (`Game/Modules/ProcessingTier/Systems/ProcessingTierSystem.cs`) recomputes every movement-capable entity's tier once per its own 15-frame stripe turn, regardless of whether that entity's classification could actually have changed since last time. A targeted alternative: a coarse spatial grid over entity positions -- bucket by `(X / cellSize, Y / cellSize, Z)`, separate from `Map`'s own per-tile occupancy array (see `AuraGrid`, `Game/Modules/StatusEffectAura/AuraGrid.cs`, for an existing precedent of a Game-layer sparse spatial index keyed by flat cell position) -- so a player move only re-tiers entities in the thin band of cells straddling the Local-radius ring at the old and new player position, instead of waiting out each entity's own stripe turn regardless of whether anything relevant changed.

The Local ring (`LocalRadiusTiles`/`LocalExitBufferTiles`) moves with the player every step, so that band query needs to be genuinely cheap -- a handful of cell lookups, not a population scan. The Neighborhood/Borough boundaries are fixed absolute grid lines by contrast (`NeighborhoodSizeTiles`/`BoroughSizeTiles`), so they only need re-evaluating when the player's own cell index changes (rare) -- gate that behind a flag and drain it gradually rather than doing it in one frame. An entity moving under its own power (not the player) needs its own immediate recheck too, via the `EntityMovedEvent` buffer `MovementSystem` already publishes.

This is a real structural addition, not a small tweak: a new persistent spatial index with its own insert/remove/move bookkeeping on every entity move (the same shape of migration cost `TieredEntityStripeSet` already pays for tier-bucket membership, one layer earlier), plus a genuine correctness surface -- the boundary-band width has to account for how far the player can move between checks, or a transition gets missed, something today's brute-force periodic recompute can't get wrong by construction. Only worth taking on once `ProcessingTierSystem` is confirmed as an actual bottleneck via profiling, not assumed from a single snapshot -- its cost in one profiling pass this session was comparable to or higher than most other systems, but that pass also coincided with newly-added Paralysis load driving `StatModifierExpirySystem` up, so the two haven't been cleanly isolated yet.

## Presentation

### High Priority

#### Component ToString coverage for the selection inspector

`SelectionWindowContent`'s inspector (`Presentation/UI/Content/SelectionWindowContent.cs`, via `ComponentInspector`) displays whatever `ToString()` a selected entity's components return -- most component structs still fall back to the default `ToString()` (the type name only, no field values), so the inspector shows little beyond "this entity has a HealthComponent" without the actual numbers. `HealthComponent` is the one existing example of a component with a real, informative `ToString()` (a percentage bar plus current/max, see its own doc comment on why it degrades gracefully for an invalid MaximumHealth rather than throwing). Worth a pass giving every component struct (or at least the ones a player/dev would actually want to inspect -- `ManaComponent`, `AbilityScoreComponent`, `StatModifierComponent`, `InventoryItemStackComponent`, etc.) an equivalent field-dump `ToString()`, so the inspector actually earns its name instead of just confirming presence/absence.

#### Inventory management

The read-only view landed: `Presentation/UI/Inventory/InventoryManagementWindow.cs` behind a new Inventory HUD folder (`InventoryFolderController`), tabbed (`Presentation/UI/Content/TabbedContent.cs`, one static "All" tab today) over a scrolling icon grid (`InventoryGridContent`/`InventoryItemStackCell`) -- pause-while-open included. Remaining scope is interaction: searching, auto-sorting, click-and-drag organization, click-to-inspect (see Item inspection popup below). Depends on the Standard widget set item below for any of that which needs controls beyond what already exists.

#### Item inspection popup

Click an inventory item cell (`InventoryItemStackCell`, `Presentation/UI/Content/InventoryItemStackCell.cs`) to see its full detail -- name, description, and whatever properties land alongside it (tags are already on `ItemDefinition`, just not surfaced in the grid yet, which only shows sprite/glyph + quantity). Depends on Inventory management above for the grid to click into.

#### Dynamic per-tag inventory tabs

Each unique tag across the player's inventory (`ItemDefinition.Tags`, e.g. `Tag.Potion`/`Tag.Consumable`/`Tag.Healing` on the Health Potion) auto-creates an inventory tab, default-sorted by how many unique items in the inventory carry that tag, user-reorderable (overrides the default sort). Clicking a tab filters the grid to items with that tag. A trailing "+" tab lets the player create custom tags. First real second consumer of `TabbedContent` (`Presentation/UI/Content/TabbedContent.cs`) beyond the single static "All" tab it ships with today -- `TabbedContent.SwitchTab` already supports swapping to an arbitrary tab list, so this is additive, not a redesign.

#### Game over screen on player 0 HP

`Game/Modules/Death/` (`HealthDamage.Apply`/`DeathSystem`/`DeadComponent`) handles death at 0 HP for every entity except the player -- deliberately exempted for now, since the player dying today has no distinct end state or UI at all. Needs this Presentation-side piece (a real game-over screen) before the player-side exemption in `HealthDamage.Apply` can be lifted.

#### Context menu / mouse button coverage

Right-click dropdown of options. `UiInputController` today only ever reads `MouseState.LeftButton` -- no right-click, middle-click, or double-click detection exists anywhere, so building this needs that mouse-button coverage added first (also enables incidental wins like double-click-title-bar-to-maximize).

#### Player stats v1

Persisted view of the player's active stats. Always shows the same fixed set of important stats.

#### Player attack button or key

A button or key for attacking, distinct from the hotbar -- needs to be available outside the hotbar but usable more quickly than going through the context menu. Determine the best UI treatment for this class of "common interaction that should always be quickly accessible."

Partially addressed by the hotkey/ability system: Default Attack is bound to F by default and fires with a single press (or double-tap for auto-target), which covers the "quickly accessible" requirement functionally. What's still unaddressed is "distinct from the hotbar" specifically -- today it's just one more `HotbarContent` slot (`Presentation/UI/Content/HotbarContent.cs`) like any other, not a separate always-visible control outside it. Revisit whether that distinction is still wanted now that the hotbar itself is fast to use.

#### Standard widget set

The control set today is `Window`, `TextWindow`, `Button`, `MapWindow`, `Folder`, and `TabbedContent` -- no checkbox, radio button, dropdown/combo box, slider, list box, or tree view. Tabs no longer need building (`Presentation/UI/Content/TabbedContent.cs`, landed alongside Inventory management above); the inventory/spell hotbar and the equipment/stats windows still want list- or grid-like controls beyond what exists.

#### Text input

No editable text control exists -- `TextWindow` only ever displays text, never accepts it. Needed for anything resembling a settings screen, chat/console input, search/filter boxes, etc.

Focus (`Window.IsFocused`, `UiInputController`) and two keyboard-routing hooks already exist for a focused window to consume input: `Window.HandleKeyPress`/`OnKeyPressAction` (one discrete key-press event at a time) and `Window.HandleHotkeys`/`OnHotkeysAction` (the whole `KeyboardState`, for modifier-aware combos -- see `MapWindow.OnHotkeysAction`). Neither delivers actual typed *characters* (shifted case, punctuation, OS keyboard layout) though -- that needs a third hook fed from FNA's `TextInputEXT.TextInput` static event (the same "*EXT" extension-class pattern `UiInputController.UpdateCursor` already uses for `MouseCursorEXT`), mirrored the same way as the other two: `Window.HandleTextInput(char)`/`OnTextInputAction`/`IWindowContent.HandleTextInput`, fed by a new `UiInputController.RouteTextInputToFocusedWindow` subscribed to that event once.

A new `TextBox : TextWindow` control (reusing `TextWindow`'s existing wrap/scroll/draw machinery rather than rebuilding it, single-line just being a fixed-height case of the same class) would be the first thing to actually need all three hooks together:

- `OnTextInputAction` appends the typed character.
- `OnKeyPressAction` handles Backspace (removes the last character).
- `OnHotkeysAction` watches for Enter -- needs Shift-state, hence the whole-state hook rather than `HandleKeyPress`: plain Enter submits; Shift+Enter inserts a newline, multiline boxes only (a `Multiline` option, e.g. on a new `TextBoxOptions`/extended `TextOptions`, gates whether Shift+Enter does anything).

Behavior once submitted:

- Submitting (plain Enter) raises a `TextSubmitted` event (mirrors `Button.Clicked`) carrying the current text -- the TextBox itself stays generic; whatever hosts it decides what "submit" means.
- If the TextBox's parent window has another TextBox child, submitting moves focus to it rather than leaving focus on a dead end. Needs a new `Window.NextTextBoxAfter(Window? after)` helper (walks `ChildWindows` in order) plus a way for the TextBox to ask `UiInputController` to actually move focus, since `Window` has no reference to it -- a new `Window.FocusRequested` event, subscribed/unsubscribed by `UiInputController.SetFocus` exactly the way it already subscribes to `Closed`.
- Whenever a window with TextBox children becomes the focused window (click, Tab-cycle, or `FocusWindow`), redirect into its first TextBox automatically rather than leaving the container itself as the dead-end focus target. Natural place: `UiInputController.SetFocus` itself -- after focusing `newWindow`, check `newWindow.NextTextBoxAfter(null)` and redirect if found. This and the Enter-driven case above are the same underlying primitive (find the next TextBox sibling); `NextTextBoxAfter(null)` doubles as "find the first one."

A visual focus indicator is also needed specifically for this control -- not optional, since without one there's no way to tell a TextBox is focused at all: the existing indicator (`Window.FocusedTitleColor`) only paints a title bar, but a TextBox is expected to be titleless, so it needs its own border/highlight-based indicator instead.

First concrete implementation, landed: a popup window (`GameShellBootstrapper.OpenQuestComposer`, `WindowDisplayMode.Fixed`, closeable, explicitly resized to track its TextBox -- see the WrapContent-circularity item below for why not `WrapContent`) containing one multiline TextBox. Submitting sends the text to `NotificationCenter.AddNotification(NotificationCategory.Quest, ...)` and closes the popup. This demo is intentionally temporary -- see the "keep temporary quest-composer demo" note in project memory: don't remove it until a real second TextBox consumer exists.

Deliberately out of scope for this first pass -- start narrow; see Text Input Enhanced Features below for what's deferred and why.

Affected: `Presentation/UI/Window.cs` (new `HandleTextInput` hook, `NextTextBoxAfter`, `FocusRequested`), `Presentation/UI/IWindowContent.cs` (new hook), `Presentation/Input/UiInputController.cs` (new routing method, `SetFocus` auto-redirect), `Presentation/UI/TextBox.cs` (new), `Presentation/UI/Notifications/NotificationCenter.cs` (consumer for the demo).

### Medium Priority

#### Diagonal movement input timing

`PlayerMovementController.HandleInput` reads `KeyboardState` once per poll and only treats a move as diagonal if both direction keys happen to be down in that same instant. A human rarely presses two keys in the exact same frame -- a few-frame gap between, say, pressing W then D lands as a cardinal move (consuming its cooldown) before the second key registers, even though the player meant to move diagonally. Needs a short input-buffering window (hold the first key's delta briefly, waiting to see if a second orthogonal key follows, before committing to a cardinal move) instead of reading raw simultaneity.

Affected: `Presentation/UI/PlayerMovementController.cs` (`HandleInput`).

### Low Priority

#### Neighborhood/Borough zoom levels

`MapCamera`'s `Neighborhood`/`Borough` zoom levels (`Presentation/UI/MapCamera.cs`) will render static structures only (walls/terrain) plus special sprites for bosses and important locations -- no moving entities. These are fixed-grid "check the larger map" views, not playable zoom levels: instead of centering on the player like `Team`/current zoom levels do, they snap to preset square regions -- a `Neighborhood` is 1000x1000 tiles, a `Borough` is 2000x2000 (a 2x2 block of neighborhoods) -- the same region sizes `Game/Modules/ProcessingTier/Systems/ProcessingTierSystem.cs` uses for its distance-throttle tiers, so both features share one spatial vocabulary.

#### Extract a shared tick-fraction HUD bar element

`PlayerHealthBarContent` and `PlayerManaBarContent` (`Presentation/UI/Content/`) are near-duplicates: same outer-outline-plus-inset-fill draw shape, same `MajorTickFractions`/`MinorTickFractions` ruler graduations (`DrawTicks`/`DrawTick`), same `ContentSize`-not-`Size` sizing rationale, same no-component fallback-color pattern -- only the backing component/pool, `StatModifierTarget`, and palette (`HealthBarPalette`/`ManaBarPalette`) actually differ. Tolerable at two copies; if a third tick-fraction bar shows up (e.g. a Soul Essence bar for soul-based abilities), abstract the shared draw logic out into one generic element instead of copy-pasting a third time -- e.g. a base class or a small shared renderer taking (current, effectiveMax, palette, no-value fallback color) and leaving only the component/pool lookup to each concrete bar.

#### Per-entity sprite scale

`SpriteRenderer.Draw` (`Presentation/Rendering/SpriteRenderer.cs`) always stretches a sprite's source rectangle to fill its tile footprint exactly -- fine for tile-sized art (Wall, Grass) but wrong for character sprites, which don't all read at a consistent apparent size relative to their footprint: confirmed in-game, the player sprite needs to render larger and goblin sprites smaller. Needs a per-entity (or per-`SpriteComponent`) scale factor -- e.g. a `Scale` field on `SpriteComponent` (`Game/Modules/Core/Components/SpriteComponent.cs`) that `MapWindow.TryDrawEntityVisual` applies when computing the destination rectangle passed to `SpriteRenderer.Draw`, rather than always drawing at exactly the tile's own footprint size.

Affected: `Game/Modules/Core/Components/SpriteComponent.cs`, `Presentation/Rendering/SpriteRenderer.cs`, `Presentation/UI/MapWindow.cs` (`TryDrawEntityVisual`), `Game/Blueprints/SpriteManifest.cs` (Player/Goblin entries would set their chosen scale here).

#### Status effect stack count on the player's status bar

`PlayerStatusEffectsContent` (`Presentation/UI/Content/PlayerStatusEffectsContent.cs`) draws one icon per distinct status effect type the player currently has any stacks of, but never shows how many -- Poison/Burning/Paralysis all read as a single flat icon regardless of whether it's 1 stack or the type's max (e.g. `PoisonEffects.MaxStacks`). Should overlay the current stack count (`StatusEffectQueries.CountStacks`) on each icon, the same corner-text treatment `InventoryItemStackCell` already uses for item quantity.

#### Player stats v2

Allow the player to select which stats to display in their stats view. Follow-on to Player stats v1 above.

#### End of level staircase

Presentation-side rendering/interaction for the staircase. See the matching Game item above.

#### Equipment menu

Exists side-by-side with inventory for easy click-and-drag equipping. Collapsible either direction -- inventory collapsible to give the equipment menu full screen space, and vice versa. Pauses the game while open (see Pause modality under Global) -- Inventory management's own pause-while-open wiring (a third OR-term in `GameLoop.Update`, alongside `MapWindow.IsPaused`/`NotificationCenter.HasBlockingNotification`) is the seam to extend, not re-solve.

#### Stats window

No way to view a player's ability scores exists today -- add one alongside the inventory window (same `Folder` + pooled-`Window` pattern as `InventoryFolderController`/`InventoryManagementWindow`, `Presentation/UI/Inventory/`). Display the 5 Core scores' `Total` (Hidden scores stay invisible by design) and total buffs/debuffs, with an explanation popup showing the origin of each -- filterable straight out of `MultiComponentPool<StatModifierComponent>` by `Target` (`Game/Modules/StatModifiers/`). Lets the player assign stat points to increase stats once level-up exists. See the matching Game stats item above.

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

Scrolling itself works (`Window.ScrollBy`/`MaxScrollOffset`, mouse-wheel-driven via `UiInputController.UpdateMouseWheelScroll`), but there's no visual affordance for it -- no thumb, no track, nothing indicating a window's content extends past what's visible or where the current scroll position sits within it, and no way to click-drag to a position directly. Right now a user has to already know to try the mouse wheel.

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

No settings/options screen exists -- pressing Escape currently does nothing. Wanted: Escape (global and unconditional, the same way Tab is -- see `UiInputController.HandleFocusCycling`'s "must stay unconditional" note -- not gated to whichever window holds focus) opens an options menu, and the game pauses while it's open.

`MapWindow.IsPaused` (see `OnHotkeysAction`) is today the only pause trigger, and was flagged when it moved there as a seam to revisit once a second trigger showed up -- this is that second trigger. Worth generalizing pause into something both the options menu and MapWindow's own Space hotkey set, rather than the options menu reaching into MapWindow to flip its flag directly.

Directly related to Pause modality under Global: an open options menu is itself the kind of modal window that item wants -- solving "block/dim input to other windows while a modal is up" there would cover the options menu for free, not just System notifications.

Affected: `Presentation/Input/UiInputController.cs` (Escape handling), `Presentation/UI/` (a new options-menu window), `DungeonCrawlerWorld/GameShellBootstrapper.cs`/`GameLoop.cs` (wiring it in and gating the simulation update on it, alongside `MapWindow.IsPaused`/`NotificationCenter.HasBlockingNotification`).

#### Keybindings page on the options menu

After Options menu above -- needs somewhere to live. A page/tab within the options menu listing the game's hotkeys (today hardcoded in `MapWindow.OnHotkeysAction`, plus `UiInputController`'s own Tab/Escape handling) and letting the player remap them.

Depends on Options menu above and Standard widget set above -- listing/remapping actions needs more than `Window`/`TextWindow`/`Button`, at minimum something list-like. Would also eventually want persisted storage for the rebound keys -- see Data storage under Global, though today that item only covers window geometry.

Affected: the new options-menu content (see Options menu above), `Presentation/Input/UiInputController.cs` and `Presentation/UI/MapWindow.cs` (the hotkeys being made rebindable).

#### Targeted key-press routing instead of a full-keyboard scan

`UiInputController.RouteKeyPressesToFocusedWindow` calls `KeyboardState.GetPressedKeys()` every frame a window is focused (effectively always) -- confirmed via reflection against the actual FNA assembly that this is the only overload (no non-allocating variant like MonoGame added), so it allocates a new array every frame for the life of the session.

`HandleKeyPress`/`OnKeyPressAction` (what this routes into) has exactly one real consumer today -- `TextBox.OnKeyPressAction`, which only cares about `Keys.Back`; `IWindowContent.HandleKeyPress` defaults to a no-op for everything else. Rather than scanning the whole keyboard (or, worse, manually diffing all ~130 `Keys` values via `IsKeyDown` every frame as a naive fix), let the currently-focused window's content declare the small set of keys it actually wants checked, and only call `IsKeyDown` for that declared set.

Not actually dependent on the Keybindings page item above -- `HandleKeyPress` (discrete edit-type keypresses, e.g. Backspace) and `HandleHotkeys` (continuous/combo game commands, what Keybindings remaps) are deliberately separate hooks. Sequenced here as a followup for proximity to the other keyboard-routing work, not a real ordering requirement.

Affected: `Presentation/Input/UiInputController.cs` (`RouteKeyPressesToFocusedWindow`), `Presentation/UI/IWindowContent.cs`/`Window.cs` (a new way for content to declare its interested keys), `Presentation/UI/TextBox.cs` (the one current consumer, declaring interest in `Keys.Back`).

#### Chat and speech

Glowing speech bubbles over NPCs, clickable to open a larger text window for the full line -- an ambient, per-NPC presentation of dialogue rather than a single shared log. Separately, a WoW/other-MMO-style chat menu as a configurable output sink for debug info, loot drops, combat/damage numbers, NPC chatter, etc., with default built-in tabs ("Loot", "Combat", "Local Chat", "Notifications") and user-configurable routing of message types to tabs. `NotificationCenter` (`Presentation/UI/Notifications/`) is the closest existing precedent (categorized, tabbed-ish notification delivery) but is popup/toast-shaped, not a persistent scrollback log -- this is a different, bigger widget.

#### Visual improvement pass

A dedicated pass over UI sizing, placement, and colors across the whole Presentation layer once more of the HUD has landed and stopped churning -- today's values (`HudMetrics`, per-content `Size`/color constants scattered across `Presentation/UI/Content/`) were each chosen locally, one element at a time, not against a single coherent visual system.

## Global

### High Priority

#### Pause modality

A `NotificationCategory.System` notification pauses the simulation (`NotificationCenter.HasBlockingNotification`, checked in `GameLoop.Update`), but doesn't actually block input to or dim whatever's behind it -- other windows (map, selection, debug) stay fully interactive underneath a "blocking" notification, which reads as a bug the first time someone notices it. Needs an actual modal concept: input to other windows either ignored or visually indicated as unavailable while a modal window is up.

Promoted to High: both the new equipment menu and the Options menu (see Presentation) explicitly need "pause game while open" behavior, and neither should re-solve modality on its own. Inventory management landed as a third OR-term in `GameLoop.Update` (`_shell.Inventory.IsAnyWindowOpen`, alongside `MapWindow.IsPaused`/`NotificationCenter.HasBlockingNotification`) the same minimally-invasive way -- generalizing this into a real modal concept still hasn't happened, it just has one more un-generalized consumer now.

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

#### Long parameter lists

Several write-surface methods have grown a lot of positional/optional parameters as the features behind them expanded -- e.g. `AbilityDefinition`'s constructor (`Game/Modules/Abilities/AbilityDefinition.cs`, up to 12 params after `ManaCost` landed alongside Mana), `AbilityGrantEffects.Grant` (`Game/Modules/Abilities/AbilityGrantEffects.cs`), `StatModifierEffects.Apply` (`Game/Modules/StatModifiers/StatModifierEffects.cs`), and the near-identical `HealthRegenSystem`/`ManaRegenSystem` constructors (`Game/Modules/Health/Systems/`, `Game/Modules/Mana/Systems/`). Worth a pass once the current wave of stat/resource features (Mana, Stats consumers, Equipment) stops churning: candidates include grouping related params into small option records (the same shape `AbilityDefinition` already uses internally for `Targeting`/`Timing`/`Effect`), or builder-style construction for the worst offenders. Not urgent today -- most call sites still read fine with named arguments -- but worth revisiting before it gets worse.

#### Field and property cleanup

General pass over field/property usage across the codebase once UI and core gameplay systems stop churning as fast as they are now -- e.g. auto-properties with no logic that could just be public fields, or the reverse, plus consistency in when a type exposes plain mutable fields (see `WindowGeometryState`/`WindowTitleState`/`WindowBorderState`/`WindowContentState`'s own doc comments explaining why those are deliberately plain fields, not properties) versus properties elsewhere. Not a bug list -- a housekeeping pass, better done once the shape of things has settled than mid-churn.

#### Solution-wide code style cleanup

A few conventions got clarified while building the focus/keyboard-routing system (`Window.IsFocused`, `UiInputController`, `MapWindow.OnHotkeysAction`) that haven't been retroactively applied anywhere else in the solution:

- Comments should only explain the WHY when it's genuinely unique or non-intuitive (a hidden constraint, a subtle invariant, a bug workaround) -- not restate what well-named code already makes obvious.
- Ternary expressions are written on three lines (the condition, then the `?` and `:` branches each on their own indented line), not packed onto one line.
- Each method should contain a single return, except for leading guard clauses.

`UiInputController.cs`, `Window.cs`, and `MapWindow.cs` were brought up to these as part of that work, but only the parts actually touched -- pre-existing methods in those same files (e.g. `Window.FindTitleButtonAt`/`TryHitTestInteraction`, `UiInputController.GetResizeCursor`, most of `MapWindow`'s rendering code) still predate them, and nothing elsewhere in the solution has been touched at all. Worth a dedicated pass once things settle rather than drive-by reformatting unrelated code mid-feature. Related to Field and property cleanup above -- possibly the same pass.

#### Possible future UI gaps, likely out of scope for this project

Tooltips, localization/IME support, and accessibility (screen reader) hooks are all standard in general-purpose GUI frameworks, but this is currently an admin/debug UI layered over a game world rather than a general application shell. Noting these for completeness, not because they're expected to be built soon.

(Drag-and-drop, formerly listed here alongside these, has been promoted out of "probably never" -- the new inventory management and equipment menu items above both explicitly require click-and-drag organization, so it's now in scope as part of those items rather than a standalone speculative gap. Window-layout persistence, also formerly listed here, likewise has its own section above -- see Data storage.)
