# Long-Term TODOs

Non-urgent architectural items worth revisiting later -- things noticed in passing that don't block current work, not a sprint backlog. Organized by layer (Engine, Game, Presentation, Global -- cross-cutting items that don't belong to a single layer), each split into High/Low priority.

## Engine

### High Priority

### Medium Priority

#### FrameEventBuffer double-buffering, and a cleanup pass over the event system

`FrameEventBuffer<T>` (`Engine/ECS/Systems/FrameEventBuffer.cs`) now throws (`Record` called after `Items` was already read this cycle) instead of silently losing an entry -- a safety net for the "second producer, or a producer/consumer Dependencies ordering regression" hazard its own doc comment already warned about, not a fix for the underlying single-producer/strict-ordering restriction itself. A more thorough fix, following Bevy ECS's `Events<T>` precedent: double-buffer instead of single-buffer-and-clear -- two internal lists, swapped (not cleared) each cycle, so a consumer always reads the *previous* cycle's fully-written buffer while the current cycle's producer(s) write into the other one. That removes the ordering hazard and the single-producer restriction entirely (a reader and a writer are never touching the same list, so there's nothing left to race), at the cost of one frame of latency between a `Record` call and a consumer seeing it -- a real contract change from today's same-cycle-visible guarantee (see the class's own doc comment: "for one or more other systems to read within the same SystemManager.Update() cycle"), so `MovementSystem`'s `ContactDamageSystem`/`StatusEffectAuraSystem` consumers would need to accept that latency (probably imperceptible for hazard/aura detection, but worth confirming, not assuming).

Do this alongside a cleanup pass over the rest of the event system, not in isolation -- `Engine/Events/EventBus.cs` (`IBufferedEvent`/`SubscribeOnce`/`DispatchBuffered`) is a second, differently-shaped buffering mechanism solving a related but distinct problem (deferring a handler out of a mid-scan re-entrant context, not batching a high-frequency per-instance event -- see `EntityDiedEvent`'s own doc comment), and the two have never been looked at side by side for consistency: naming, doc-comment currency, whether `IFrameScoped`/`SystemManager.RegisterFrameScoped` and `EventBus.DispatchBuffered` could or should share more vocabulary, and whether the various event-adjacent doc comments across `Game/Modules/Achievements/`/`Game/Notifications/` still accurately describe both mechanisms once this lands.

### Low Priority

#### Equipment

Engine-side equipment support (slots, equip/unequip mechanics). Companion to the Game-layer equipment rules and the Presentation-layer equipment menu below. Unblocked now that Inventory (above) exists -- equipping is expected to move an `InventoryItemStackComponent` stack into a slot rather than invent its own separate storage.

## Game

### High Priority

#### Inventory management rules

Item interactions, storage rules, restricted items, etc. Governs how the Engine-layer inventory system above is actually used. Base storage and identical-stack merging (exact `ItemDefinition` match only) already exist -- remaining scope is interaction/restriction rules: stacking beyond exact-match, what can't be picked up by whom, interactions between items.

#### Consumable items

Items that get used up -- a potion drunk, a scroll read, ammo spent. Needs the item-instance-divergence design noted under Inventory system above (a consumable's remaining-uses count is exactly the kind of per-slot state that doesn't exist yet) plus an actual "use" action, which interacting-with-items work (out of scope for the storage/viewing pass that landed Inventory) will need to define.

#### Scrolls (landed -- Wand activator still open)

`Game/Modules/Actions/` now exists: **`ActionEffect`** -- a composable `IReadOnlyList<IActionEffectEntry>`, each entry (`DirectDamage`, `DirectHeal`, `DirectManaRestore`, `HotkeyExpansionGrant`, `StatusEffectGrant`, `StatModifierGrant`, `ChainedEffect`, `AuraSourceGrant`) owning its own application logic -- and **`IActionActivator`** -- `Guid Id`/`Targeting`/`Timing`/`IReadOnlyList<ActionEffect> Effects`/display fields, how/how-often an action triggers. `AbilityDefinition` (a spell) and `Game.Modules.Inventory.PotionActivator` both implement it today, both wrapping the same shared `ActionEffect`/`ActionTiming` shapes -- replacing the old separate `AbilityEffect`/`ConsumableEffect` types (see `PLAN-action-effect-activator.md` for the full design, including why per-activator-kind cost/consumption logic -- mana-spend, `PotionCooldown` abuse -- deliberately stayed in each system rather than becoming composable entries).

`ScrollActivator : IActionActivator` (`Game/Modules/Actions/Activators/`) has landed alongside `PotionActivator` -- one-time-use (stack-consuming, same as any item), no mana, Immediate timing, and `Tag.Self` gives double-tap-to-self for free. A real `TargetShape` addition *was* needed for manual-click self-targeting, though: `Scroll of Healing`'s original `TargetShape.Adjacent` deliberately excludes the caster's own tile (the melee-default ring), so a click on your own tile never resolved at all (never in `TargetableTiles` to begin with) -- fixed by splitting out `TargetShape.AdjacentWithSelf` (the same ring, plus the caster's own footprint), which `Scroll of Healing` uses instead. `Tag.Self`'s existing click-collapses-to-self-only special case (`ActionTargetingController.TryConfirmActivationAtTile`) still applies on top, unchanged. `ScrollScalingEffects` scales a scroll's Range/AreaSize (`Presentation/UI/ActionTargetingController.TryGetArmedTargeting`) and any duration its effect carries (`ActionEffectContext.DurationScaleMultiplier`) together off the caster's Intelligence, 100% at 1 up to 400% at 300. `ScrollMasteryEffects` (`Game/Modules/Actions/`) makes 200 uses of scrolls sharing a `ScrollActivator.SpellId` permanently grant that spell -- looked up in `ActionCatalog` first, synthesized at runtime from the scroll's own `ItemDefinition` if no spell is registered under that id yet, so most scrolls never need a hand-authored spell counterpart. Two concrete items prove it out: `Scroll of Healing` (`SpellId` = the existing `HealAction`, proving the lookup path) and `Scroll of Torch` (`SpellId` has no existing spell, proving synthesis; its effect is intentionally minimal today -- see the next TODO item). Two achievements grew out of this: "Most Boring Librarian Ever" (any scroll mastered -- purely observational, the spell grant itself already happened) and "Archivist" (5+ scrolls bound to hotkeys).

`AuraSourceGrant` (`Game/Modules/Actions/Effects/`, renamed from `AuraSourceToggleEntry`/`AuraSourceGrantEntry`) is what Scroll of Torch's effect actually is -- deliberately *not* a bespoke Torch-specific component, so `MapWindow` never needs ability-specific rendering knowledge (it already renders any `StatusEffectAuraSourceComponent` generically via `MapTintGrid`/`DrawGlowOverlay`, the same pipeline Lava's Burning glow uses). Two modes on one entry, chosen by whether `DurationFrames` is set: permanent (`null`, the original Toxic Idol case) flip-toggles via `AuraSourceEffects.Toggle`; timed (Scroll of Torch, granting `StatusEffectType.Light`) grants-or-refreshes via the new `AuraSourceEffects.Apply`/`Revoke` plus a new `AuraSourceExpiryComponent`/`AuraSourceExpirySystem` (`Game/Modules/StatusEffectAura/`, ticked the same `CountdownTicker`/`ITickCountdown` way `PotionCooldownComponent` is) that revokes it once the (Intelligence-scaled) duration runs out -- refreshing on re-apply rather than flip-toggling off, the behavior a duration-bearing grant needs that Toggle's flip semantics don't provide. Always grants on `context.TargetEntityId` -- no separate Source/Target choice (a `GrantRecipient` field was tried and then deliberately removed): a caller that wants to target itself does so via a Self-shaped `TargetingSpec`, which already resolves `TargetEntityId` to the caster, the same way every other effect entry reads "who this lands on" -- Toxic Idol's own `TargetShape.Self` targeting is what makes it self-centered, not anything `AuraSourceGrant` does.

Still open: a **Wand** activator (see the Wands item below) would similarly spend a charge instead of mana or a one-time consumption. Also still needed: the item "use" action design the Consumable items entry above already flags as a prerequisite.

#### Torch reveal + light-weakness damage, and Scroll Mastery power-scaling

Two follow-ups the Scrolls landing above deliberately left open:

- Scroll of Torch grants `StatusEffectType.Light` via `AuraSourceGrant` (see the Scrolls item above) -- today that's glow-only: no `IStatusEffectAuraApplier` is registered for `Light`, so `StatusEffectAuraSystem.GrantStacks` gracefully no-ops (see `StatusEffectType.Light`'s own doc comment) and the only visible effect is `MapTintGrid`'s white tile glow. The actual point of a torch (revealing fog of war in its AOE once fog of war exists, and damaging entities with a light weakness such as vampires) needs a real applier registered for `Light` -- nothing about the grant/expiry machinery itself needs to change to support that. Also worth reconsidering once fog of war lands: today's grant is per-*entity* (`AuraSourceGrant` always targets `context.TargetEntityId`, whichever entity the Burst footprint resolved against when it landed), but a light source reads more naturally as anchored to a *location* -- a persistent AOE tied to a place, not to whichever entity happened to be standing there. See the Torch V2 item below.
- `ScrollMasteryEffects.MasteryThreshold` (flat 200 for every scroll today) and a synthesized spell's placeholder `ManaCost: 0` should both eventually scale with the power of the spell/effect being taught -- a cheap effect shouldn't take as long to master, or cost as little mana once learned, as a strong one. Blocked on Action Effects gaining some form of power-scaling concept, which doesn't exist yet.

#### Torch V2 -- selectable attachment mode (follow the caster, fixed location, or a specific entity)

`AuraSourceGrant` always grants on `context.TargetEntityId` -- whichever entity the Burst footprint resolved against when it landed, or the caster itself for a Self-shaped scroll/spell (see the Scrolls item above for why that's enough to cover "target self" without any Source/Target choice on the entry itself). Fine for "light up whatever I hit or myself," but not the only attachment a torch-like effect should be able to choose. Three distinct modes are worth making selectable rather than each scroll/spell being stuck with one:

- **Follow the caster** -- already fully expressible today with a Self-shaped `TargetingSpec` (an aura source already follows its carrying entity's moves regardless of who granted it, see `MapTintGrid`'s `EntityMovedEvent` handling) -- no `AuraSourceGrant` change needed, just a scroll/spell whose targeting always resolves to the caster.
- **Fixed at a specific spot** -- the location-anchored case the Torch reveal item above already flags: needs a minimal stationary prop entity spawned at the target tile (`Transform` + `StatusEffectAuraSourceComponent`, no creature identity, see `Lava`) and despawned on expiry, rather than attaching to whatever's occupying the tile when it lands.
- **Attach to a specific (non-caster, non-resolved-target) entity** -- e.g. a companion or a summoned pet -- genuinely not expressible today: `AuraSourceGrant` only ever reads `context.TargetEntityId`, and neither `ActionEffectContext` nor any `TargetShape` has a notion of "some other entity, not the one this activation resolved against." Would need either a new `TargetShape` (e.g. "nearest ally") or a way for the entry itself to carry an explicit target override.

Worth designing as one shared "attachment mode" concept reusable by any future aura-granting effect, not just Torch.

#### Move inventory items to the hotbar (landed)

`ItemHotkeyBindingComponent` (`Game/Modules/Inventory/Components/`) plus `ConsumableActivationSystem`/`ActionTargetingController`'s item-arm/target/confirm/double-tap path have landed -- a slot can reference an item and activate it (splash-throw or double-tap-self for potions), separately from `ActionHotkeyBindingComponent` (renamed from `HotkeyBindingComponent`, the ability-only original). Real click-and-drag assignment from the inventory grid has since landed too (`UiInputController`'s content-drag path, `HotbarContent.BindItem`/`ResolveDroppedBinding`) -- `PlayerBlueprint`'s hardcoded starting binds are the only thing still assigned programmatically, which is expected (spawn-time setup, not a player action).

Binding is now keyed by `StackInstanceId`, not `ItemDefinitionId` (see the per-slot item divergence work) -- a slot references one exact physical stack, so a wand's own remaining charges keep depleting through the same bound slot as it fires, and dragging a still-collapsed Merged Stack or a bare divergent-without-context cell onto a slot is refused outright (disabled cursor) rather than binding something ambiguous. See the new "ItemBindingRule for hotkey-bound consumables" entry below for the one piece of that intentionally left unbuilt: what happens once the exact bound stack runs out.

#### Shops and storage containers

Reuse the same `Game/Modules/Inventory/` storage any entity already gets -- a shop or a chest is just another entity with `InventoryItemStackComponent` stacks, no new storage primitive needed. What's missing is the trade/transfer UI and rules (pricing, what a shop restocks, container capacity if any).

#### Melee attack implementation (landed)

For NPCs and the player. Attacking sets the same shared ActionLockComponent that movement sets on a successful move, creating a tactical decision between moving more vs. attacking more -- choosing to attack this window means not moving this window, and vice versa; the decision/execution split behind `TestCombatBehaviorSystem`/`MovementSystem` (see the Goblins entry above) is what makes that tradeoff actually hold for NPCs, not just the player. Targets any entity in Adjacent's resolved footprint (the ring around the caster, excluding the caster's own tile(s) -- see `TargetShapeResolver`) -- deliberately *not* restricted to entities with physical collision (Blocking): a non-Blocking entity (e.g. Tiny/Phasing, or one with no `SimpleHealthComponent` at all) is still a valid target, since this allows status effects to be applied to otherwise-immortal entities. The "immortal but affectable" case is proven out already: `AbilityEffectResolver` grants `AbilityEffect.StatusEffects` through the shared `StatusEffectAuraApplierRegistry` (`Game/Modules/StatusEffects/`) regardless of whether the target has a `SimpleHealthComponent` -- an ability's own `AbilityEffect.StatusEffects` (e.g. a future Paralysis-on-hit melee ability, `Game/Modules/Paralysis/`) can use this path. Punch is the concrete example (`CoreAbilitiesModule.PunchId`, `TargetShape.Adjacent`); the player has always had it via the hotbar, and Goblins (and any other race carrying the right components) now activate it through `TestCombatBehaviorSystem`.

#### ActionEffectResolver damage/heal consistency

`ActionEffectResolver.Apply` (`Game/Modules/Actions/ActionEffectResolver.cs`) treats damage and healing asymmetrically today: damage is scaled by the *caster's* `StatModifierTarget.OutgoingDamage` before `HealthDamage.Apply` further reduces it by the *target's* `IncomingDamage` -- a real two-sided pipeline. `DirectHeal` has no equivalent -- its `Fraction` is only ever multiplied by the target's (modifier-adjusted) `MaximumHealth`, with no caster-side "healing power" or target-side "incoming healing" modifier in the chain at all. Once base stats (see the Stats item below) and Equipment (see the Equipment items below) can actually modify incoming/outgoing damage *and* healing, revisit this resolver so both paths go through the same caster-then-target modifier shape -- e.g. a `StatModifierTarget.OutgoingHealing`/`IncomingHealing` pair mirroring `OutgoingDamage`/`IncomingDamage`, consumed the same two-stage way.

#### Generalize ActionInstanceComponent.DamageOverride into a per-action override system

`ActionInstanceComponent.DamageOverride` (`Game/Modules/Actions/Components/ActionInstanceComponent.cs`) is the only per-instance override an action supports today, hardcoded to one field (`DamageAmount`, read by `DirectDamage` alone) and set once at grant time by whichever blueprint calls `ActionGrantEffects.Grant` (e.g. Goblin's Punch grant overriding damage to a flat 10). Nothing else about an action -- targeting, an effect entry's other parameters, an activator's own settings like `SpellActivator.ManaCost` -- can be overridden per-instance or per-activation this way. Worth generalizing into a real override system once a second use case actually needs it (e.g. a buff that temporarily widens an action's targeting, or an NPC AI that wants to fire a cheaper/weaker version of a shared action): let the `IActionActivator` or whoever queues the activation (Presentation, NPC AI, a future equipment/buff system) supply an override for any part of the resolved action, not just damage. Likely requires `PendingActionActivationComponent` to carry the actual (possibly-overridden) action data through to `ActionActivationSystem`/`ActionEffectResolver`, not just a bare `Guid ActionId` re-looked-up fresh from `ActionCatalog` every time.

#### Experience module

Grants experience points to the player (and any XP-tracking entity) for killing enemies (hook off `EntityDiedEvent`/`HealthDamage.Apply`'s wasAlive transition, the same trigger `KilledAMobAchievement` already uses) and for finishing quests -- blocked on quest completion itself not existing as a real mechanic yet (`NotificationCategory.Quest` today is just a notification tag, not a trackable objective; see the quest-composer demo under the Presentation Text input item). Level-up grants stat boosts/abilities per class -- supersedes the old "Experience and level up system" stub this entry replaces (each class gets different stats, abilities, spells, and other benefits on level up; the default Engineer class gives simple level-up stat boosts and abilities as a proof-of-concept).

Needs a new Experience component (current/next-level-threshold, mirroring `SimpleHealthComponent`/`ManaComponent`'s current/maximum shape) plus an experience bar in the top-middle of the HUD -- a real candidate for finally triggering the "Extract a shared tick-fraction HUD bar element" item (`Presentation/UI/Content/`) instead of a third hand-copied `PlayerHealthBarContent`-shaped class. See the Skills and Spell leveling items for the same "gains XP with use, levels 0-15/20" shape reused at a much smaller per-thing scale -- worth checking whether Experience's own level-up math and Skills/Spells' leveling math can share one formula/curve rather than three independently-tuned ones.

#### Skills

Skills modify actions with static bonuses (via `StatModifierComponent`/`StatModifierTarget`, the same layering `AbilityScoreEffects.GrantModifier` already uses) and grant new effects on top of an action -- the concrete motivating use case for the "Generalize ActionInstanceComponent.DamageOverride into a per-action override system" item above, which already flags exactly this need ("a buff that temporarily widens an action's targeting," "let the IActionActivator or whoever queues the activation supply an override for any part of the resolved action"). Skills only ever increase in level, never decrease, up to hundreds per player entity and dozens per non-player entity -- needs a `MultiComponentPool<SkillComponent>`-shaped store (one instance per known skill, mirroring `ActionInstanceComponent`'s per-entity/per-action shape), not a `DirectComponentPool`/`PackedComponentPool` (single-instance) one.

Each skill has its own experience bar, gaining XP with use (hook wherever the skill's associated action successfully activates/resolves, e.g. `ActionEffectResolver.Apply`), starting at level 0, capped at 15 normally, unlockable to 20 (worth checking whether `HotkeyExpansionUnlockComponent`'s own "normal count, separately unlockable higher count" shape is directly reusable rather than re-invented). See the Spell leveling item for the identical level-up shape applied to spells instead of skills -- likely worth sharing one leveling primitive between the two rather than building it twice. See the Player selection menu item under Presentation for a concrete consumer of a skill's own level (gating what an observer can see about another entity).

### Medium Priority

#### Toggle item activator

Every item today activates through `Game.Modules.Actions.Activators.PotionActivator`, which `ConsumableActivationSystem` unconditionally consumes a stack for on every activation -- correct for a potion or scroll, wrong for a stateful toggle. Toxic Idol (`Game/Modules/Inventory/Definitions/ToxicIdol.cs`, the first real user of `AuraSourceGrant`'s permanent flip-toggle mode) is the concrete case that exposes it: turning its Poison aura back *off* costs a stack the same as turning it on, so a player down to their last one can't stop the effect without losing the item.

Needs a new `IActionActivator` kind (e.g. `ToggleItemActivator`) alongside `PotionActivator`/`DirectAction`/`SpellActivator` that `ConsumableActivationSystem` recognizes and does *not* consume a stack for. Open design questions to resolve before landing it, not just a mechanical addition:

- Does toggling still require the item to be present in inventory at all times it's active -- and if the stack empties, or the item is dropped/sold/traded away while toggled on, does the effect force-untoggle? (Mirrors the "a corpse retracts its still-active aura on death" cleanup `AuraSourceEffects.RemoveAll`/`DeathSystem` already do for the action-granted case -- the same class of "owner lost its ability to sustain this" cleanup, one layer up at the inventory-slot level instead of the entity-death level.)
- Is "currently toggled on" state that needs its own tracking, independent of whatever effect it drives? Today `AuraSourceGrant`'s permanent-mode on/off state is entirely implicit in whether the entity's `StatusEffectAuraSourceComponent` exists, which works because that's the only effect kind a toggle item grants so far -- it wouldn't generalize cleanly to a toggle item driving a different kind of effect (e.g. a future toggled `StatModifierComponent` buff), where "is this toggled on" and "does the entity have the effect" aren't necessarily the same question.

Once this lands, Toxic Idol should migrate from `PotionActivator` to it as the concrete proof/test case this item was already built to be.

#### Dexterity  scaling ActionLockComponent.StandardLockFrames

`ActionLockComponent.StandardLockFrames` (`Game/Modules/Core/Components/ActionLockComponent.cs`) is currently a flat per-entity value set once at construction (Goblin 54, Fairy/Ghost 48, Player 20, Engineer's 10% class bonus on top -- see the race/class blueprints). This is the concrete instance of the Stats item's own "wire the concrete modifies behaviors" bullet for Dexterity -- an entity's own agility/speed should scale it directly: lerp from `ActionLockGate.StandardLockFrames` (1 second) at Dexterity 1 down to a quarter of that (0.25 seconds) at Dexterity 300, using `AbilityScoreComponent.Total` the same way `PotionCooldownEffects.ComputeDurationFrames` already lerps a duration off Constitution's total.

Needs to compose with, not replace, the existing per-race/per-class baseline and Engineer's multiplicative bonus -- Dexterity's contribution is presumably another multiplier on top of (or instead of) the race's own flat `standardLockFrames` seed, not a value that ignores it. Exact composition (multiply together vs. Dexterity fully replacing the racial baseline) is an open design question, not decided here.

#### Spell leveling

Spells level up following the same rules as Skills above (level 0, normal cap 15, unlockable cap 20, XP gained with use, never decreases) -- worth landing after Skills specifically so both can share one leveling primitive (XP-to-next-level curve, level-up event, cap/unlock check) instead of two independently-built, likely-to-drift copies of the same math. A spell's own level would then modify its `ActionEffect` entries' magnitude/duration the same general way Skills modify a plain action -- e.g. a higher-level Heal restoring more, or a higher-level Magic Missile costing less mana -- though the exact "what does a spell's level actually change" design is still open.

#### Corpse looting rights based on damage dealt

Once multiple entities can plausibly have contributed to a kill, corpse looting shouldn't be a free-for-all -- rights should scale with damage dealt (see the Corpse looting item below for the looting mechanism itself, landed without this restriction: anyone adjacent can loot any corpse today). Needs per-entity damage-dealt tracking against a given target and a decision on when that tracking resets -- on death (simplest, but loses the record before looting even starts unless kept alongside `DeadComponent`), or on a timeout since the last hit (so a target that regenerates back to full between separate encounters doesn't keep crediting an attacker from an old, unrelated fight forever)? Open design question, not decided here.

#### Mobs looting corpses

V1: a mob loots a nearby corpse's inventory into its own, stopping once its own inventory is full (`InventoryCapacity.MaxNonPlayerStackCount`, see the Corpse looting item below) -- no preference among available items, just fills up. V2: preference based on the mob's own combat style and the item's rarity, once either concept exists.

### Low Priority

#### Show runner race

Randomly selected. Affects UI appearance, and gives a bias towards selected quests and enemy types.

#### End of level staircase

Game-side logic for descending/ascending a level. See the matching Presentation item below for the visual/interaction side.

#### Random map generation v1

No procedural generation exists yet -- `FloorBuilder.CreateMap`/`PopulateFloor` (`Game/Floors/FloorBuilder.cs`) currently populate the fixed `TestMapBuilder` layout every run. Whatever lands here should take a randomizer seed as an explicit input rather than always constructing an unseeded `MathUtility` the way `GameLoop.Initialize` does today (`new MathUtility()`, no seed passed) -- `MathUtility`'s constructor already accepts an optional `Random? randomizer` (`Engine/Math/MathUtility.cs`, currently only exercised by tests wanting determinism), so the seed just needs to actually reach it from a real game session instead of only from test code.

Two player-facing requirements once real generation exists: let a player supply their own seed at floor-start (so a specific layout can be deliberately reproduced), and display whatever seed was used -- player-supplied or randomly generated -- somewhere visible in the UI, so it can be read off and shared (the Minecraft precedent: a seed alone is enough for someone else to regenerate an identical map). Needs a concrete numeric/string seed representation `MathUtility`/`Random` can be constructed from, an input surface (title screen or a floor-start prompt) for a player to type one in, and a HUD/menu readout for the active seed -- none of which exist today since generation itself doesn't yet.

#### Equipment

Game-side equipment rules (what can go in which slot, stat effects of equipping). Companion to the Engine-layer equipment item above and the Presentation-layer equipment menu below. Unblocked now that Inventory exists (see the Engine-layer Inventory system item).

Once ComplexHealth exists (see the Body parts item below), some slot *counts* -- not just which slots exist -- should scale with an entity's own active, non-disabled body parts of the matching `BodyPartType` (see that item's BodyPartType followup) rather than being a fixed number per race: e.g. a ring slot per finger, a boot slot per foot, both shrinking if a hand/foot is lost or disabled. A `SimpleHealth` entity has no per-part detail to key off, so this only ever applies to Complex entities -- Simple ones keep whatever fixed slot layout this item otherwise defines.

#### Wands (landed, without the original Equipment gate)

`WandActivator` (`Game/Modules/Actions/Activators/`) landed as the concrete proof case for **per-slot item
divergence** (see the Inventory system item above): each physical wand carries its own remaining
`Charges`/`MaxCharges`, fixed once at grant time off the recipient's Intelligence (`WandGrantEffects`, 3
casts at Intelligence 1 up to 30 at 300) and ticking down per-instance via
`InventoryActions.PeelOneIntoDivergentStack` as it's actually fired -- the first item ever granted as a
diverged stack rather than a plain interchangeable one. `Game/Modules/Inventory/Definitions/WandOfFireball.cs`
is the concrete item: 10-range Burst, 25-35 direct damage, 5 stacks of Burning. Deliberately landed *without*
the originally-planned Equipment gate this entry used to describe -- a wand activates straight from a hotbar
slot exactly like a potion or scroll, no equip/unequip step, since Equipment itself still doesn't exist and
gating on it would have meant Wands couldn't land at all. Revisit whether an equip requirement is still wanted
once Equipment (see above) actually exists -- not designed in from the start this time.

Item hotkey binding had to move from `ItemDefinitionId` to `StackInstanceId` to make this work at all (see
`ItemHotkeyBindingComponent`'s own doc comment) -- once two stacks of the same item can legitimately differ,
"bind this slot to Wand of Fireball" is ambiguous; it has to mean one specific physical wand. The inventory
grid's own display followed: `InventoryGridContent`/`InventoryItemStackCell` merge same-item stacks into one
badged cell by default (a "Stack Diverged" toggle in `GridControl`, on by default), expandable back into the
individual stacks a click at a time.

#### Enchantment

Permanently modifies an item's stats -- the next real consumer of `InventoryActions.AddDivergentItem` (see
the per-slot item divergence work above), which was already built generic for exactly this: an enchant effect
builds a modified `ItemDefinition` `Override` (e.g. a `StatModifierGrant` added to `Effects`, or an existing
one's magnitude increased) the same way Wand of Fireball's charge-depleted state does, and calls the same
split-into-a-new-stack primitive. No design yet for what an enchant recipe/application UI looks like, what
materials it consumes, or where it's performed (a Safe Room mechanic, an NPC service, a consumable "scroll of
enchanting" item, etc.).

**Provenance tracking**: nothing records *why* or *how* a stack diverged today -- only its resulting state.
Once Enchantment is real, a diverged item's `Override` (or a small sidecar field) should probably carry
something like "enchanted with Ruby of Flame" for tooltip/flavor purposes, the way MMO item tooltips commonly
show an enchant's origin. Not needed for Wands (there's nothing to attribute -- charge depletion isn't an
event worth narrating), so left for whenever Enchantment itself is picked up.

**Acquisition provenance** (a separate, broader concept from the divergence provenance above): no
item stack tracks *where it came from* at all today -- a loot box, a specific corpse looted (see
the Corpse looting item under Game), a shop purchase, crafting, etc. Same open design question as
above (a field directly on `InventoryItemStackComponent` vs. a sparse side component), but scoped
to acquisition source rather than divergence cause -- and should record the *name*, not the id, of
the entity a stack was taken from, since entity ids get recycled once destroyed (corpses aside,
which never are -- see the Corpse decay/destruction item under Game).

#### ItemBindingRule for hotkey-bound consumables

Item hotkeys bind to one exact `StackInstanceId` (see the Wands item above) -- once that stack is depleted,
its slot just goes empty; there's no fallback to another stack of the same item id. A future `ItemBindingRule`
concept would let a hotkey instead bind to an item id with a user-selected rule for which divergent/plain
stack to prefer (e.g. lowest charges first, highest charges first, plain batch first), re-resolved each time
the currently-selected stack runs out instead of requiring the player to manually rebind.

#### Stats (infrastructure landed -- consumers still TODO)

`Game/Modules/AbilityScores/` now exists: `AbilityScoreComponent` (base value 1-300, precomputed `Total`) for the 5 Core scores (Strength, Intelligence, Constitution, Dexterity, Charisma) and 2 Hidden scores (Luck, Wisdom) never shown to the player or touched by level-up. Modifiers reuse `StatModifierComponent`/`StatModifierTarget` (`Game/Modules/StatModifiers/`) rather than a separate list -- grant one via `AbilityScoreEffects.GrantModifier`, not raw `StatModifierEffects.Apply`, so `Total` stays in sync (it's precomputed eagerly on grant/expiry, not lazily on read like every other stat -- see `AbilityScoreComponent`'s own doc comment). The player rolls randomized starting values (2-10, clustering 3-7); every other race (Goblin/Fairy/Ghost) currently defaults to a flat 5 across all 7 scores, adjustable in a balance pass. Remaining work:

- **Split hidden ability scores into composites.** Luck and Wisdom (and future hidden scores) should eventually be derived from combinations of *other* hidden ability scores rather than being standalone base values. Not designed yet -- needs its own pass once there are enough hidden scores for composition to make sense.
- **Wire the concrete "modifies" behaviors.** Strength->melee damage (retire the hardcoded `PunchDamage` consts in `PlayerBlueprint`/`Goblin`/`Fairy`/`Ghost`), Constitution->`MaximumHealth`(x10) still open (`HealthRegen` and potion cooldown -- `PotionCooldownEffects.ComputeDurationFrames`, 20s at total 1 down to 5s at total 300 -- have landed), Dexterity->`ActionLockComponent.StandardLockFrames` (see this item's own dedicated entry below for the current lerp spec), Intelligence->mana once the Mana item below lands, Charisma->shop/charm/pet-bond mechanics once those exist, Luck->loot/AI once those exist.
- **Non-player starting ability scores.** Give race/class blueprints their own baseline scores instead of the flat default-5 placeholder above.
- **Level-up modifying Core scores.** Flat increases from the future level-up process (Hidden scores explicitly excluded). See the matching Presentation stats window item below.

#### Item weight and carry capacity scaling with Strength

No item has a weight today (`ItemDefinition`/`InventoryItemStackComponent`), and inventory storage is unlimited (see the Inventory system item above). Add a carry-capacity limit derived from the holder's Strength `AbilityScoreComponent.Total`, then gate picking up (or otherwise receiving) an item on it not exceeding that capacity -- the same kind of restricted-pickup rule the Inventory management rules item above already anticipates. A concrete instance of the Stats item's own "wire the concrete modifies behaviors" bullet, which doesn't yet cover Strength -> carry capacity specifically. Depends on the Item weight (definition-only) and race weight ranges item under Presentation below for the weight field itself and its placement.

#### Mana

A mana system, using `SimpleHealthComponent`/the health bar (`Game/Modules/Health/`) as a template -- a current/maximum pool plus regen, the same shape health already has. Heal (`Game/Modules/Abilities/CoreAbilitiesModule.cs`) should cost 2 MP and Magic Missile 5 MP once this lands -- both are free to cast until then. Starting `MaximumMana` should equal Intelligence's `Total` (`Game/Modules/AbilityScores/`) now that ability scores exist.

#### Scroll and spell durations scaling with Intelligence

Once scrolls and ActionEffects/ActionActivators exist (see the Scrolls item above), a spell or scroll's duration-based effects (buffs, DoTs, status effects granted through an ActionEffect) should scale with the caster's Intelligence `AbilityScoreComponent.Total` -- higher Intelligence extending how long the effect lasts, the same way Constitution now scales the potion cooldown (`PotionCooldownEffects.ComputeDurationFrames`) rather than leaving it flat. Needs the ActionEffect duration field(s) to exist as a real concept first, which they don't until the Scrolls restructuring lands.

#### Damage types

No damage-type concept exists anywhere in `HealthDamage`/`AbilityEffect` today -- every hit is an undifferentiated number. Starting set: Magic, Blunt, Explosive, Slashing.

#### Level collapse timer

A countdown-driven pressure mechanic -- the current floor collapses/becomes unsurvivable once a timer expires, forcing the player to find the staircase (see the End of level staircase items above) before it runs out rather than exploring indefinitely. No existing infrastructure to build on -- this is a new timer concept, not a variant of `CountdownTicker`/`ITickCountdown` (those are per-entity/per-effect, this is one global per-floor timer). Needs a countdown UI element below the mana bar (`PlayerManaBarContent`, `Presentation/UI/Content/`) showing time remaining -- a plain numeric/bar countdown, not necessarily a fourth tick-fraction bar the way Experience above might be, since this isn't a current/maximum resource so much as a single ticking-down value.

#### Tomes

Rare consumable items that grant a specific spell to the caster outright, on consumption -- unlike Scrolls (see the Scrolls item above), which teach their spell only after `ScrollMasteryEffects.MasteryThreshold` (200) uses. Needs either a new `IActionEffectEntry` (e.g. a `SpellGrant` entry, alongside `DirectDamage`/`StatusEffectGrant`/etc.) that directly registers the caster's `ActionInstanceComponent` for the granted spell, or a Scroll-Mastery-style threshold of exactly 1 reused instead -- worth deciding which, since a bespoke entry is more honest about "instant grant" being a different mechanic than "mastery," but a threshold-of-1 reuses `ScrollMasteryEffects` entirely for free.

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

`DeathSystem` (`Game/Modules/Death/`) deliberately never calls `EntityManager.DestroyEntity` -- a corpse is reclassified non-Blocking (`World.ConvertToNonBlocking`) and marked `DeadComponent`, but stays a real, fully-populated entity indefinitely (design intent: a future corpse-looting mechanic, see the Achievement content backlog item below, needs the entity's data to still exist). `EntityManager.DestroyEntity` (full removal, all components gone, id freed for reuse) is reserved for a genuinely separate, deliberate action -- e.g. a corpse-decay timer or "loot then destroy" step once Inventory exists. The same primitive would also apply to a future destructible-terrain entity (e.g. a breakable wall): that case skips `DeadComponent`/the corpse system entirely, since it was never a `SimpleHealthComponent`-driven creature death, and would just call `DestroyEntity` directly on whatever triggers its destruction.

#### Self damage buff ability

An example FreeCast or Immediate ability that raises the caster's own outgoing damage for a duration -- exercises the ability system on a non-damage-dealing, self-targeted effect.

#### Defensive buff spell -- damage reduction + healing over time

A self-targeted (`TargetShape.Self`, like `HealAction`) spell combining two effects in one `ActionEffect` (mirroring `ToxicStrikeAction`'s existing two-`StatusEffectGrant`-entries-in-one-list precedent): a timed `StatModifierGrant(StatModifierTarget.IncomingDamage, ...)` reducing incoming damage for the buff's duration -- fully supported today, no new plumbing needed, same modifier `HealthDamage.Apply` already consumes for every damage source -- plus a periodic self-heal (a "Regeneration" status effect) applying a small heal each tick for the same duration, built the same way `Burning`/`Poison`'s damage-over-time are (`TimerBasedAuraApplier<T>`, a new stack-count timer component, granted via `StatusEffectGrant`), just healing instead of damaging on each tick.

This is the concrete case that actually motivates finishing the "ActionEffectResolver damage/heal consistency" item above: the regen tick would write directly to `SimpleHealthComponent` with no incoming/outgoing healing modifier in the chain at all, unlike `IncomingDamage` which every damage source (including Burning/Poison's own DoT ticks) already passes through -- so a defensive buff correctly shrinks the damage it's still taking but its own healing can't yet be buffed/reduced by anything else, an asymmetry that's easy to overlook in the abstract but obvious the moment both halves exist side by side on one concrete spell. Landing `StatModifierTarget.IncomingHealing`/`OutgoingHealing` (see that item) and routing the regen tick through it is the natural follow-up, not a prerequisite -- the spell is worth building first as the thing that makes the gap concrete.

#### Toggle poison aura ability

The item side landed: Toxic Idol (`Game/Modules/Inventory/Definitions/ToxicIdol.cs`, granted to the player) is the first real user of `AuraSourceGrant`'s permanent flip-toggle mode, toggling a Poison aura (range 4) on/off around whoever activates it -- this is also what the aura sync bug fix (`AuraSourceAddedEvent`/`AuraSourceRemovedEvent`, `Game/Modules/StatusEffectAura/AuraSourceEffects.cs`) and multi-aura-per-entity support were built for. Still open: the original ask was specifically a *FreeCast ability* (`Game/Modules/Actions/Definitions/`), not an item -- Toxic Idol uses `PotionActivator`/Immediate timing like every other item, so it doesn't exercise FreeCast's "usable during an Action Lock" behavior against the aura machinery, and it's consumed one-per-toggle (drinking/using it to turn the aura back off costs a stack, same as every other potion) rather than being a reusable toggle -- see the Toggle item activator item above, which would remove that specific quirk. Landing the actual FreeCast action version is still worth doing for that specific coverage.

#### Body parts (landed)

See `PLAN-body-parts.md` for the full design record (data model, dispatching facades, execution phases). Health and health regen overhaul, halfway between Fallout's per-limb HP and Dwarf Fortress's full simulation. Every entity keeps a single current/maximum pool by default -- `SimpleHealthComponent`/`SimpleHealthRegenSystem` (`Game/Modules/Health/`), the renamed-but-otherwise-unchanged original pool/system, so the "plain" path keeps its own explicit name rather than silently being the assumed default with no qualifier. A second, opt-in path gives Complex entities (crawlers, bosses) a set of individually-tracked body parts instead of one pool: one-or-more `BodyPartComponent` entries (`MultiComponentPool<BodyPartComponent>`, the "0..N components of the same type per entity" shape the Skills item above already anticipates for a similar per-entity/per-key store). Whether an entity uses Simple or Complex health is decided entirely by which components its blueprint grants at Build time -- no separate marker component needed to say "this entity is Complex," the same way `NonBlockingComponent.Kind` folds its own exemption-kind flag into the one component that grants the exemption rather than a second component that could drift out of sync (see CLAUDE.md's World & Map note). An entity is never expected to carry both.

**Goblin is the one race on Complex health today** (`Game/Blueprints/Races/Goblin.cs`, via `ComplexHealthEffects.GrantBodyParts`) -- Head/Torso Vital, two Arms and two Legs not, summing to its prior flat 200 total so the split itself didn't rebalance its toughness. Player/Fairy/Ghost all still use `SimpleHealthComponent` -- Complex health is opt-in per race, not a blanket replacement, and each race's own body part list is fully independent (no shared humanoid template) -- a Fairy could carry Wing parts a Goblin's list has no equivalent for, a cat-shaped race four Leg/Foot parts and zero Arm/Hand parts. This is also why `BodyPartType` (Head/Torso/Arm/Leg today -- see the BodyPartType followup below for expanding it) is a real per-part field checked by type, not inferred from list position or count.

**Total HP is a derived sum, not a stored field.** `HealthQueries.TryGetTotals` is the one shared chokepoint every "current/max HP" consumer goes through (`PlayerHealthBarContent`, `MapWindow.DrawHealthBar`, `HealthBarElement`, `InspectionWindowContent`) -- checks `SimpleHealthComponent` first, else sums every `BodyPartComponent` the entity owns. Mirrors `IMapQuery.IsBlocking`'s own single-chokepoint reasoning, applied here to Simple-vs-Complex. Deliberately doesn't fold in `StatModifierMath`'s `MaximumHealth` modifier -- callers apply that to the returned maximum themselves, the same as they always did against `SimpleHealthComponent.MaximumHealth` directly.

**Regen ticks one body part at a time.** `SimpleHealthRegenSystem` keeps its original per-tick behavior unchanged. `ComplexHealthRegenSystem` instead picks, per due entity, the single body part with the lowest *current/maximum percentage* -- not lowest raw HP, since a 5/10 arm is more wounded than a 40/100 torso -- and applies that tick's regen amount to it alone, skipping any part still inside its post-disable lockout (see below).

**Vital parts and death.** A `SimpleHealthComponent` entity still dies when its single pool hits 0, unchanged. A Complex entity dies the instant *any* Vital body part hits 0, independent of the entity's summed total -- Goblin's Head and Torso are both Vital, so losing either kills it even while its Arms/Legs still read well above 0. `ComplexHealthDamage.Apply`'s own death check scans for `IsVital && CurrentHealth == 0` on the hit part, rather than comparing a summed total against 0 the way the Simple path does.

**Non-vital parts disable, not die.** A non-Vital part hitting 0 sets `IsDisabled` and starts a 10-second (real time, `RegenLockoutFramesRemaining`) regen lockout rather than killing the entity -- prevents a part sitting right at the boundary between a damage tick and a regen tick from flickering disabled/enabled every other tick. Nothing reads `IsDisabled` for gameplay purposes yet (movement/melee/pickup gating) -- that consumption pass is the BodyPartType categorization followup below.

**Attacks hit a random body part (for now).** Any damage source resolving against a Complex entity (melee's `DirectDamage`, `ContactDamageSystem`, `PoisonSystem`/`BurningSystem`'s DoT ticks) picks one body part at random via `BodyPartSelection.PickRandom` to absorb the hit, via `ComplexHealthDamage.Apply`. Real per-part/multi-part targeting is the Targeted body part damage followup below.

**Damage and healing are both Simple/Complex dispatching facades.** `HealthDamage.Apply` and `HealthHeal.Apply` both dispatch on which pool actually has the target entity -- `HealthDamage.Apply` delegates to `ComplexHealthDamage.Apply` (one random part per hit, as above); `HealthHeal.Apply` delegates to `ComplexHealthHeal.ApplyFractionToAllParts`, which heals *every* body part at once by the same fraction of its own max -- a potion/scroll's heal is a broadcast across the whole entity, unlike passive regen's single-part focus. Every non-`ActionEffect` damage caller (`ContactDamageSystem`, DoT ticks) and `DirectDamage`/`DirectHeal` all thread both pools through to reach this dispatch.

Six followups, each unblocked now that this landed, are logged as their own TODO items below (Game and Presentation both) -- see `PLAN-body-parts.md`'s own "Not in scope" section for the full, up-to-date list rather than duplicating an enumeration here that would only drift out of sync as more get added.

#### Targeted body part damage and multi-part effects

Unblocked now that Body parts (above) has landed with its "random part" placeholder. Real per-part targeting for damage/effects, replacing that random pick: a single-target attack should be able to land on one specific part (once a targeting UI exists to choose it -- not designed here), and an area/environmental effect should be able to resolve against multiple parts at once by its own rule rather than uniformly-random -- e.g. lava contact damage (`Game/Modules/Burning/`, `ContactDamageSystem`) hitting legs specifically (a creature standing in lava is burning its feet, not its head), while a Fireball's `Burst` applies its damage/Burning equally across every part instead of picking one winner. Needs `ActionEffect`/`IActionEffectEntry` (`Game/Modules/Actions/Effects/`) to carry an optional body-part-selection rule per entry -- a new small enum or delegate (`Random` today's default, `Lowest`/`AllParts`/a specific `BodyPartType` for the lava/fireball cases) that `ComplexHealthDamage`/whatever applies status effects to a part reads instead of always rolling random. `ActionEffectResolver.Apply`'s existing caster-then-target modifier chain (see the ActionEffectResolver damage/heal consistency item above) would need to run once per selected part rather than once per hit.

#### BodyPartType categorization and gameplay effects

Unblocked now that Body parts (above) has landed. Each `BodyPartComponent` needs a `BodyPartType` (Head/Torso/Arm/Leg/Hand/Foot/... -- exact set not decided here) so other systems can key off *kind* of part rather than by name string. Two concrete consumers already anticipated: Equipment (see this TODO's own updated Equipment entry above -- slot count/availability keyed by which typed parts an entity currently has, e.g. a ring per finger); and actions/movement gated by a part's own disabled state -- Legs disabled slows or blocks movement (`MovementSystem`), Arms disabled blocks melee/lifting (`ActionEffectResolver`'s Adjacent-targeted attacks, `InventoryActions` pickup), the concrete "many future features" the original Body parts item flags above. No system reads a disabled part's state at all until this item lands -- the state exists (see the Body parts item's own "non-vital parts disable" note), nothing consumes it yet; this item is that consumption pass, once there's more than one gameplay system ready to key off it at once rather than wiring each in piecemeal.

#### Per-body-part vs whole-entity status effects

Unblocked now that Body parts (above) has landed. `StatusEffectStack`/`StatusEffectAuraApplierRegistry` (`Game/Modules/StatusEffects/`) today apply every status effect at the entity level -- correct for something like Poison (a systemic effect, no reason to localize it to one part), wrong for something like Burning on a Complex entity (a burning leg reads more naturally than a burning entity, and ties into the targeted-damage followup above -- lava burning the legs specifically should also apply Burning to the legs specifically, not the whole entity). Needs a way for a `StatusEffectGrant`/`IStatusEffectAuraApplier` to declare whether it's entity-scoped (today's behavior, unchanged default) or part-scoped, and for the part-scoped case, a place to actually track "this body part has N stacks of Burning" -- likely a second `MultiComponentPool`-shaped store keyed by (entityId, bodyPartId) rather than entityId alone, distinct from today's per-entity `StatusEffectStack`. Feeds directly into the HealthWindow item under Presentation, which needs to display per-part status effects once they can exist.

#### Limb-specific gameplay penalties beyond disable

Body parts itself (above) has landed; still blocked on its BodyPartType categorization follow-up
landing first -- this extends whatever binary disabled-at-0 gameplay hooks that follow-up wires (Legs
disabled blocks movement, Arms disabled block melee/lifting) into a graduated penalty curve
instead, closer to Fallout's own crippled-limb model than a hard cutoff: a Leg below some
percentage threshold (not chosen here) slows movement before fully blocking it at 0 HP, an Arm
below threshold reduces melee damage/carry capacity rather than an all-or-nothing gate. Needs a
real percentage-to-penalty curve designed per `BodyPartType` (movement-speed scaling for Legs,
damage/carry scaling for Arms, ...) once there's an existing binary gameplay hook on each type to
graduate rather than just gate -- see `PLAN-body-parts.md` for the underlying data model this and
every other Body parts follow-up build on.

#### Movement System

- `SeekTarget` movement mode

#### Lootbox delivery, and moving Lootbox out of the Achievements module

Achievements can name a `Lootbox` (rarity + box type, `Game/Modules/Achievements/Lootbox.cs`/`LootboxRarity.cs`), but nothing delivers it yet. `InventoryActions.AddItem` (`Game/Modules/Inventory/InventoryActions.cs`) is now available as the actual delivery primitive -- unblocked, but `AchievementModule`'s unlock path still doesn't call it, only describes the reward in the notification. Lootboxes themselves can only be *opened* in Safe Rooms once opening exists as a mechanic -- this is not a purchased gambling item, it's a pre-set reward tied to how it was earned.

`Lootbox`/`LootboxRarity` currently live in, and are named for, the Achievements module -- `IAchievementDefinition.Lootbox` is the only place a `Lootbox` is produced today, and every achievement definition (`Game/Modules/Achievements/Definitions/`) references the type directly. But a lootbox reward isn't conceptually achievement-specific -- quests, loot drops, level-up, and other future systems should be able to award one too, the same way `InventoryActions.AddItem` already isn't tied to any one caller. Worth moving `Lootbox`/`LootboxRarity` (and the eventual opening mechanic) into their own module once a second real awarder exists, with `AchievementModule` becoming just one caller of that module's own grant API (`IAchievementDefinition.Lootbox` would still describe *which* lootbox an achievement grants, but the granting/opening mechanics themselves wouldn't live under `Game/Modules/Achievements/` anymore).

#### Corpse looting (landed)

Selecting "Loot" from a corpse's right-click context menu (disabled when the player isn't adjacent -- see the Context menu item below) opens the player's own `InventoryManagementWindow` alongside a new `CorpseInventoryWindow` (`Presentation/UI/Looting/`), both Menu Mode windows -- a fixed summary (entity icon/glyph, name, killer, death tick) above a plain, non-tabbed item grid (always at least a 2x5 minimum so items dragged in later don't force scrolling), sized once at open time and pinned there via the new `Element.SetMinimumSize` so it can't be user-shrunk below its own content. `SecondaryInventoryWindowController` (deliberately not folded into `InventoryFolderController`, which is itself slated for a split -- see the entry under Presentation below) owns the open/close/replace-on-new-target lifecycle and is written generically ("open a second inventory window for some other entity") so a future chest/shop can reuse it directly rather than growing its own controller. A corpse carrying unlooted items shows the `LootBag-Red` badge (top-right of its actual footprint, not just its origin tile, for a multi-tile corpse); the badge tints grey once the corpse's loot window has been opened at least once (`CorpseLootedComponent`).

Items drag freely between any two entities' grids in either direction (`InventoryActions.TryTransferStack`/`TryTransferAllStacksOfItem` -- no auto-merge into a matching stack on the destination, a same-entity no-op guard, and a same-window drop is a safe no-op) via `UiInputController` locating the drop target's grid through the new `Element.Tag` property, not `Window.Content` -- `InventoryTabContent`'s own hosting pattern (the player's real inventory) never assigns `InventoryGridContent` as its host window's Content at all, driving it manually instead, which `Window.Content`-based matching silently missed entirely (confirmed by live testing: corpse-to-player transfers failed while player-to-corpse worked, since only the corpse's own simpler window happened to use `SetContent`). An item dragged from a non-player entity's own inventory never binds to, or even highlights, the hotbar. Non-player inventories are capped at 20 distinct stacks (`InventoryCapacity.MaxNonPlayerStackCount`) -- unlimited for the player.

No real loot table exists yet -- Goblins/Fairies/Ghosts get a **temporary** random 0-20-stack starting inventory instead (`Game/Blueprints/NPCs/TemporaryNpcLootGrant.cs`), to replace once a real one lands. Ties to the achievement backlog's "Loot a corpse for the first time" bullet below.

Deliberately out of scope for this pass, left for follow-ups of their own: stack splitting/merging (a transferred stack keeps its own identity rather than merging into a matching one on the destination), damage-based loot rights, and mobs looting corpses themselves (both new Medium Priority items above).

#### NPC component

A quick, direct way to identify "is this entity an NPC" -- today it's only ever inferred indirectly (excluding `IPlayerQuery.PlayerEntityId`, or a specific race check like `TestCombatBehaviorSystem.IsFairy`), with no single marker component. Raised by the Corpse looting item's own temporary random-loot grant (`TemporaryNpcLootGrant`), which currently has to target Goblin/Fairy/Ghost's blueprints individually rather than "every NPC."

#### In-game day/time tracking

`DeadComponent.DiedAtFrame` (see the Corpse looting item above) currently only shows a raw `EngineTime.FrameCount` tick in the corpse summary -- a real in-game calendar/clock (day/night, a date) would let that, and anything else wanting a timestamp, show something human-readable instead.

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

`SpellCasterAchievement` (`Game/Modules/Achievements/Definitions/SpellCasterAchievement.cs`) unlocks on `Game.World.ActionActivatedEvent` (published by `ActionEffectResolver.Apply` for every successful activation regardless of category), filtered by a real `action.Tags.Contains(Tag.Spell)` check via `AchievementTriggerContext.Actions` -- every Spell-tagged action qualifies automatically, including the starter Heal spell (`Game/Modules/Actions/Definitions/Spells/`), which makes this trivially easy to earn.

#### Tag.Spell can drift out of sync with the actions it's meant to describe

`Tag.Spell` (`Game/Modules/Tag.cs`) is a hand-authored content tag, independent of which `IActionActivator` an `ActionDefinition` actually uses -- nothing keeps them in sync, and `SpellCasterAchievement` (above) trusts the tag alone. Today's three `SpellActivator`-based actions (Heal, Magic Missile, Toxic Strike) all happen to carry it and Punch (the only `DirectAction`) doesn't, but that's discipline, not enforcement: a future `SpellActivator` action could ship untagged (achievement silently never fires) or a `DirectAction`/`PotionActivator` action could be mistagged `Spell` (achievement fires for something that isn't one). Two ways to close the gap: (1) drop `Tag.Spell` entirely and have `SpellCasterAchievement` key off `action.Activator is SpellActivator` instead -- the actual mechanism, not a label -- if "cast via SpellActivator" is really meant to always mean "reads as a spell"; or (2) keep `Tag.Spell` as an independent classification (it may end up meaning something narrower than "uses SpellActivator" once wands/scrolls exist) but have `SpellActivator`'s own construction or `CoreActionsModule`'s registration apply it automatically so a definition can't forget it.

`BigMusclesAchievement`/`UnbreakableAchievement`/`ShanghaiKidAchievement`/`RevengeOfTheNerdsAchievement`/`KillerQueenAchievement` (`Game/Modules/Achievements/Definitions/`) each unlock the first time one core ability score's *base* value (ignoring any `StatModifierComponent`-driven `Total`) reaches 100; `MinMaxerAchievement` unlocks once all five reach the 300 cap simultaneously. All six react to `AbilityScoreBaseValueChangedEvent` (`Game/Modules/AbilityScores/AbilityScoreBaseValueChangedEvent.cs`), which only `AbilityScoreEffects.SetBaseValue` publishes -- nothing calls that method yet, since no level-up or "item of divine suffering" system exists (see the Experience and level up system item above), so none of these six can unlock today. They start working the moment either feature calls `SetBaseValue` to permanently raise a score. All six currently reward "None (TODO: 3 upgrade choices)" -- the reward notification always shows "You've received an upgrade!", but there's no upgrade-choice system yet to actually grant, matching the reward wording above.

#### Boundary-aware ProcessingTierSystem recompute

`ProcessingTierSystem` (`Game/Modules/ProcessingTier/Systems/ProcessingTierSystem.cs`) recomputes every movement-capable entity's tier once per its own 15-frame stripe turn, regardless of whether that entity's classification could actually have changed since last time. A targeted alternative: a coarse spatial grid over entity positions -- bucket by `(X / cellSize, Y / cellSize, Z)`, separate from `Map`'s own per-tile occupancy array (see `AuraGrid`, `Game/Modules/StatusEffectAura/AuraGrid.cs`, for an existing precedent of a Game-layer sparse spatial index keyed by flat cell position) -- so a player move only re-tiers entities in the thin band of cells straddling the Local-radius ring at the old and new player position, instead of waiting out each entity's own stripe turn regardless of whether anything relevant changed.

The Local ring (`LocalRadiusTiles`/`LocalExitBufferTiles`) moves with the player every step, so that band query needs to be genuinely cheap -- a handful of cell lookups, not a population scan. The Neighborhood/Borough boundaries are fixed absolute grid lines by contrast (`NeighborhoodSizeTiles`/`BoroughSizeTiles`), so they only need re-evaluating when the player's own cell index changes (rare) -- gate that behind a flag and drain it gradually rather than doing it in one frame. An entity moving under its own power (not the player) needs its own immediate recheck too, via the `EntityMovedEvent` buffer `MovementSystem` already publishes.

This is a real structural addition, not a small tweak: a new persistent spatial index with its own insert/remove/move bookkeeping on every entity move (the same shape of migration cost `TieredEntityStripeSet` already pays for tier-bucket membership, one layer earlier), plus a genuine correctness surface -- the boundary-band width has to account for how far the player can move between checks, or a transition gets missed, something today's brute-force periodic recompute can't get wrong by construction. Only worth taking on once `ProcessingTierSystem` is confirmed as an actual bottleneck via profiling, not assumed from a single snapshot -- its cost in one profiling pass this session was comparable to or higher than most other systems, but that pass also coincided with newly-added Paralysis load driving `StatModifierExpirySystem` up, so the two haven't been cleanly isolated yet.

#### DelayedActionSystem polls every pending action every frame, untiered

`DelayedActionSystem` (`Game/Modules/Actions/Systems/DelayedActionSystem.cs`) uses a flat `EntityStripeSet` with `StripeCount = 1` -- unlike every sibling countdown/cooldown system (`ActionLockSystem`, `ActionCooldownSystem`, `StatModifierExpirySystem`, `BurningSystem`), which all use a `TieredEntityStripeSet` so a ProcessingTier-throttled (far-off) entity gets checked less often. It also borrows `ActionLockComponent.LockFramesRemaining` (a shared, generic windup/cooldown gate read by many unrelated systems) instead of owning its own countdown, so a long-windup Delayed action gets re-checked every single frame, for every entity with one pending, regardless of distance from the player, for however many frames the windup lasts.

Not a measured problem today -- no Delayed action currently has a windup long enough to matter, and no profiling has flagged this system. If it does become one: give `PendingDelayedActionComponent` its own `ITickCountdown` field and drive it through `CountdownTicker.Tick` (already used by `BurningSystem`/`PoisonSystem`/`ContactDamageSystem`/`StatusEffectAuraSystem`) instead of polling `ActionLockComponent`, and give `DelayedActionSystem` a `TieredEntityStripeSet` like its siblings above. A callback stored directly on `ActionLockComponent` (invoked by `ActionLockSystem` when a lock hits 0) was considered and rejected: that component is a generic, cross-module gate with no other consumer needing a callback, and resolving a Delayed action's effect from inside it would pull Actions-specific knowledge (`ActionCatalog`, `ActionEffectResolver`) into a lower-level Core primitive -- the tiering + shared-countdown approach above gets the same win with patterns already proven elsewhere in this codebase, no new coupling.

#### Entity displacement with damage

`World.MoveEntity`/`World.PlaceEntityOnMap` (`Game/World/World.cs`) both no-op outright when a Blocking entity's destination footprint is already occupied by a different Blocking entity (`IsFootprintFreeFor`) -- correct for ordinary movement, but too blunt for a forced-displacement effect (knockback, forced pull/push, a summon shoved into an occupied cell). No such effect exists yet. Once one does, it needs its own resolution for the "destination is occupied" case instead of silently failing to move -- e.g. dealing collision damage to the displaced entity (and/or whatever occupies the destination) in lieu of completing the move, or redirecting to the nearest free cell along the displacement path. Depends on a forced-movement ActionEffect existing first (see the Actions/ActionEffects work already landed in `Game/Modules/Actions/`) to have a real caller to design against.

#### Dungeon Anarchist's Cookbook

A rare item granted to the player on the third floor -- takes many different forms, each dedicated to one specialization (class/race/playstyle), containing recipes, hints, and secrets for that specialization to encourage replay under a different one next run. Depends on floor-specific guaranteed item drops not existing yet (today's fixed `TestMapBuilder` layout has no per-floor loot table -- see the Random map generation v1 item above) and, for the "many different forms" part, some notion of which specialization a given run's copy should target.

Each form's pages should let the player add their own notes, as multi-line text boxes -- a real second consumer of `TextBox`'s `Multiline` support (see the Text Input Enhanced Features item above) beyond the TEMPORARY quest-composer demo in `GameShellBootstrapper.cs`, and a natural fit for the upcoming Journal-shaped UI already anticipated there.

**Meta-progression across runs (two possible directions, neither designed yet):**

- Possibly grant permanent, tiny boosts to future runs' starting Ability Scores (`Game/Modules/AbilityScores/`) and Skills (see the Skills item above), derived from whichever ability scores and skills the just-ended run actually leaned on. Skills should carry over far more rarely than Ability Scores -- a skill's own effect (modifying actions with static bonuses and granting new effects outright, per the Skills item) is already a bigger swing than a single ability score point, so an equivalent permanent carryover needs to be correspondingly rarer to avoid stacking runaway power across repeated runs. Nothing today records "what this run's build actually was," and nothing about a new run's blueprint construction (`PlayerBlueprint`) reads from a prior run at all -- this needs its own persistent, save-file-level meta-progression store, distinct from anything in a single `EcsContext`.
- Possibly let each run's end contribute one unique action to a shared New Game Action Pool -- the run banks a single action (player-chosen, or picked by some "most used"/"most impactful" rule, not decided), and a fresh game start offers the player a choice of one out of X randomly-drawn actions from the accumulated pool as a starting bonus. Depends on the same persistent meta-progression store as the ability score/skill idea above, plus a rule for what qualifies a run's action as bankable in the first place.

## Presentation

### High Priority

#### Split InventoryFolderController into InventoryController, FolderController, and AbilityScoreController

`InventoryFolderController` (`Presentation/UI/Inventory/InventoryFolderController.cs`) currently owns three responsibilities at once: the folder/tile shell itself, opening/closing the player's own `InventoryManagementWindow`, and opening/closing `AbilityScoreWindow`. Split into three cooperating classes -- `FolderController` (the folder/tile shell, renamed away from "Inventory Folder" since it houses more than inventory; exact new name still open), `InventoryController` (the player's `InventoryManagementWindow` open/close/toggle), and `AbilityScoreController` (`AbilityScoreWindow`'s own). `SecondaryInventoryWindowController` (see the Corpse looting item below) depends on two small accessors this split moves: `OpenInventoryWindow()`/`PlayerInventoryWindow` land on the new `InventoryController` instead, updating that one call site.

Alongside the split, change the folder's own interaction model: today expanding/collapsing the folder (a click) is what opens/closes its windows (`InventoryFolderController.OnFolderDisplayModeChanged`). Instead, hovering the folder should open it, and a click should pin it open (survive the mouse leaving) rather than being the only way in.

#### Inventory management

The read-only view landed: `Presentation/UI/Inventory/InventoryManagementWindow.cs` behind a new Inventory HUD folder (`InventoryFolderController`), tabbed (`Presentation/UI/Content/TabbedContent.cs`, a horizontally-scrollable dynamic per-tag strip -- see the Dynamic per-tag inventory tabs item below) over a scrolling icon grid (`InventoryGridContent`/`InventoryItemStackCell`) -- pause-while-open included. Click-and-drag organization has since landed too, for the one destination that exists today (dragging a stack onto a hotbar slot -- see the Move inventory items to the hotbar item under Game, and per-slot item divergence's own Merged/Diverging Stack drag rules); dragging one grid cell onto another to reorder, and click-to-inspect (see the new "Selected item details window..." entry below), are still open, and remain blocked on the Standard widget set item below for whatever controls they end up needing.

#### Manual stack splitting and merging

Player-initiated stack manipulation -- shift-drag (or similar) to peel an arbitrary chosen quantity off a stack into a new one, and drag one stack onto a compatible one to merge them back together. Distinct from the automatic splitting/merging the per-slot item divergence work already does (that's system-triggered, by exact-state equality, with no player choice involved) -- this is a manual, player-facing gesture on top of it. Blocked on the click-and-drag organization work "Inventory management" above still has as open scope (the drag gesture itself needs to exist first) and on "Context menu / mouse button coverage" (a likely UI for choosing how much to split off, e.g. a quantity prompt) both landing first.

#### Selected item details window, generic stat-diff highlighting, and equipped-item comparison (landed, without diff-highlighting or equipped-item comparison)

The details-window half landed as `ItemDetailsWindow`/`ItemDetailsWindowController` (`Presentation/UI/Inventory/`): clicking a real single-stack item cell (the player's own inventory grid, or an open secondary/corpse grid -- both share `InventoryGridContent`, so this worked for free on both) opens/updates one persistent details pane next to the player's own Inventory window, rather than the old click-to-open-popup-that-closes-again shape -- name, description, tags, and every stat, the same "shows whatever is currently *selected*" idea `InspectionWindowContent`'s Basic mode already used for map entities. Clicking a different item updates the same window instead of opening a second one. Sections, left-aligned and `SeparatorBar`-divided (mirroring `InspectionWindowContent.BuildSpacer`): sprite+name (no header); Effects (header, one line per effect entry via `ActionEffectFormatting.FormatEntry` -- see the "Action activator, action effect..." item below, now landed too); Activation (header, omitted when the item has no `Activator` -- targeting shown as a small shape-preview grid plus `ActionActivatorFormatting.BuildLines`' Timing/per-activator-type text, same item below); full Description (no header, omitted when blank); comma-separated Tags (no header). Closes via its own Close button or a click outside both it and whichever inventory window(s) it's tied to (`ItemDetailsWindowController.IsOutsideClick`, wired into `UiInputController.HandleMousePress` next to `ContextMenuController`'s own identical-shaped outside-click check) -- no minimize. The selected stack also glows (`GlowRenderer`, a new `MapViewState.SelectedItemStackInstanceId`) in both the inventory grid and a bound hotbar slot.

Clicking a Merged Stack's own already-expanded member cells now opens Details for that member instead of collapsing the group back -- collapsing instead happens on a click that lands outside the expanded group entirely (`InventoryGridContent.OnCellClicked`), since there's no longer a persistent badge cell to re-click while expanded.

`DisplayMode.WrapContent`, not `Fixed`, is what actually makes the window's own height track its content correctly across repeated rebuilds -- a `Fixed`-mode window whose height shrinks between two `Configure` calls re-measures its children against its own already-small content size instead of a stable outer budget, silently clamping a later section's height toward 0 once an earlier section's content pushed it far enough down the column (confirmed by reproduction: Tags rendering on top of Description). `WrapContent`'s own Measure path threads its `MaximumSize` through to children unchanged instead, sidestepping that shrink-feedback loop -- the same mechanism `Tooltip` already relies on for its own auto-height content; worth remembering as the general answer any *other* window with variable, rebuild-driven content hits this same way.

Generic stat-diff highlighting and equipped-item comparison are both still fully open -- see the new "Item Details Comparison" item below, which now covers both in one place.

#### Action activator, action effect, targeting spec, and action timing ToString for item and action details (landed)

Landed as three pieces, all `Presentation/UI/`, none of them item-specific (every one takes the plain `Game.Modules.Actions` types directly, so a future Magic Menu gets them for free too -- `ActionDefinition` shares `ActivatableDefinition` with `ItemDefinition`):

- `ActionEffectFormatting.FormatEntry(IActionEffectEntry)` -- one readable line per concrete `Effects/` type (`DirectDamage`/`DirectHeal`/`DirectManaRestore`/`StatusEffectGrant`/`StatModifierGrant`/`AuraSourceGrant`/`HotkeyExpansionGrant`), recursing for `ChainedEffect` (collapsed onto one line: trigger chance plus every nested entry, semicolon-joined). Reuses `StatModifierComponent.ToString()`'s own `+`/`-`/`x`/`÷` Operation-x-Polarity symbol convention for `StatModifierGrant` so it reads consistently with the Ability Score window's own live modifier list.
- `ActionActivatorFormatting.BuildLines(IActionActivator, ActionCatalog)` -- `Timing.Category` always; `ActionLockFrames`/`CooldownFrames` only when actually set (null means "use the caster's own default," not "no lock," so it's omitted rather than printed as none); a `ScrollActivator`'s `SpellId` resolved to the spell's own name via the new `ActionCatalog` dependency; `WandActivator` Charges unconditionally; `SpellActivator` Mana Cost only when non-zero. Every duration converts frames to seconds (`Ceiling(frames / GameTiming.FramesPerSecond)`, the same rounding `ModifierDisplayFormatting.FormatDuration` already uses) -- nothing shows raw frame counts.
- `TargetShapePreviewGeometry` + `TargetShapePreviewElement` (`Presentation/UI/Content/`) -- targeting shape rendered as a small grid of squares (via `TargetShapeResolver.Resolve` directly, not `ActionTargetingController.ComputeTargetableTiles`'s own Burst-shaped overshoot stand-in, see the Targeting tile highlights item below) with a gold circle marking the caster's own cell, shrinking cell size to fit a fixed budget rather than growing the window for a large-`AreaSize` `Burst`. `ItemDetailsWindow.BuildTargetingRow` lays the Activation section out in three equal zones -- targeting caption + Timing/activator text lines in the left zone, the shape grid centered in the middle zone, the right zone deliberately empty for now (a natural home for a future addition, e.g. the Item Details Comparison item below). `Cone`/`Line` have no real cursor in a static preview, so both resolve against one fixed "north" convention direction, not real facing.

`InventoryGridContent.UpdateHover`'s own separate hand-picked "Target:"/"Charges:" hover text was deliberately left untouched -- out of scope for this item, which only ever named `ItemDetailsWindow`'s own sections.

#### Item Details Comparison (landed, without equipped-item comparison)

Landed as a same-`Activator`-type-gated comparison, not a generic field diff -- comparing a Sword against a Wand was judged meaningless, so only items whose `Activator?.GetType()` matches the anchor (whatever `ItemDetailsWindowController` currently shows) are eligible, per direct feedback during design. No shared comparison table either: each additional compared item opens its *own* independent `ItemDetailsWindow` instance beside the anchor (`ItemComparisonController`, `Presentation/UI/Inventory/`), every one self-coloring its own real Effects/Activation lines rather than a synthesized row grid -- sourced from an expanded `ItemComparisonStatExtraction.Extract` (now covering every line, not just a curated numeric subset, each carrying a `Key` for cross-item matching). A line goes green when at least one other compared item lacks a matching-`Key` line at all (an exclusive advantage); green/red-by-magnitude when every compared item has one (`ItemComparisonHighlighting.ComputeHighlight`); plain otherwise. Per more direct feedback, this coloring is *whole-line*, not just a "name" token -- `TextWindow` has one `TextColor` per string, no per-substring styling -- see the new "Rich inline text formatting" item below, logged specifically because of this constraint. The target-shape preview grid gets its own tile-level version of the same rule, gated additionally on every compared item sharing the exact same `Targeting.Shape` ("do not compare different shapes for color-coding") -- a tile highlights green if present in this item's own footprint but absent from at least one other's (e.g. comparing `Burst` sizes 2 and 4, the size-4 grid's outer two rings glow, the size-2 grid stays plain).

Triggered from an inventory item's context menu ("Compare") or from `ItemDetailsWindow`'s own title-bar button (a "↔" glyph, landed after two iterations -- a heavier "⇔" rendered too small/busy at title-button scale) -- either arms compare mode against that item as anchor; every eligible inventory cell (matching Activator type) glows light green, every ineligible one greys out (`InventoryItemStackCell.CompareState`, `MapViewState.CompareRequiredActivatorType`), and a persistent "Select next item..." message follows the cursor the whole time armed (`CursorTextContent.ShowPersistent`/`HidePersistent`, a new mode alongside that class's original fade-out toast). Clicking an eligible item adds it (or removes it, toggling, if already added); right-click stops adding without closing anything already open; the whole comparison clears if the anchor itself closes or changes to an unrelated item via a normal (non-Compare) click.

**Known limitation, since fixed:** every added column used to reposition *all* open columns chained off the previous column's own live position, letting a third-or-later column spawn off-screen with nothing to pull it back -- confirmed during testing. `ItemComparisonController.CreateColumn` now anchors every column to the fixed comparison anchor and places it via `WindowCascadePlacement` (`Presentation/UI/`), which cascades new columns diagonally and clamps to screen bounds instead.

Equipped-item comparison remains open -- still blocked on the Equipment item existing at all (Game and Presentation both), same as the original entry already noted.

#### Inventory item hover summary (landed)

Hovering an inventory item cell (`InventoryItemStackCell`, `Presentation/UI/Content/InventoryItemStackCell.cs`) highlights it and, after a brief delay, shows a popup with its Name/Summary (`ActivatableDefinition.Summary`, the short "read at a glance" field), anchored East with a 1px gap -- the same self-polled-mouse, delay-gated `HoverPopupWindow` pattern `AbilityScoreWindow` already used for its own hover popup (see `InventoryGridContent.UpdateHover`). A lighter-weight companion to the click-to-inspect popup above, not a replacement -- hover shows the short Summary text, click (once built) still opens the full Description/properties detail.

#### Dynamic per-tag inventory tabs (landed)

Every tag carried by at least one of the entity's current item stacks (`ItemDefinition.Tags`) gets an auto-generated tab, sorted left-to-right by distinct-stack count descending, ties broken alphabetically (`Game/Modules/Inventory/InventoryTagQueries.cs`); a tag no current stack carries gets no tab. A leading "All" tab shows everything; every tab's grid is sorted alphabetically by item name (`InventoryGridContent`). `TabbedContent` (`Presentation/UI/Content/TabbedContent.cs`) now supports a horizontally-scrollable, runtime-rebuildable tab list (`SetTabs`, called whenever the entity's inventory version changes) with real child-Element tab tiles (Outset border when selected, Inset otherwise) instead of the flat-color, fixed-list, non-scrolling hand-drawn strip it shipped with originally. Deliberately left open, since the tab count can get large fast: user-reordering the default sort, and a trailing "+" tab for custom user-created tags -- both still open. The Tab search with ghost text item below has since landed, easing exactly that "large number of tabs" case.

#### Inventory item sorting, filtering, and searching (landed)

`InventoryGridContent` now supports `SortOrder` (`InventorySortOrder`: NameAscending/NameDescending/QuantityDescending/QuantityAscending, default NameAscending -- reproduces the old always-alphabetical behavior exactly), `NameFilter` (case-insensitive `Name.Contains`), and `HideDisabled`, each a settable property that rebuilds the grid on change. Driven by `GridControl`'s row of controls (see the Tab Stats row item below) via `InventoryTabContent`, the Inventory-specific glue that translates `GridControl`'s generic events into these three properties -- see `PLAN-inventory-item-filtering-and-tab-stats.md` for the full design. Sort is click-to-cycle and Hide Disabled is a toggle button, both deliberately simple for now -- see the Advanced sort control and Checkbox widget items below for their respective upgrades.

#### Tab search with ghost text (landed)

A search box shares the tab strip's own row (`Presentation/UI/Content/TabbedContent.cs`), right-aligned to the Inventory window's content edge, fixed in place -- it does not scroll with the tab tiles, which narrow to make room for it. Typing debounces 300ms (`TabbedContent.SearchDebounceFrames`, `GameTiming.FramesForSeconds`) before applying as a case-insensitive `Label.Contains` filter over which tab header tiles exist; "All" (index 0) is exempt and always shown. Ghost text ("Search Tabs", light grey) landed as a small opt-in addition directly on `TextBox` (`GhostText`/`GhostTextColor` properties, `Presentation/UI/TextBox.cs`) rather than a one-off subclass, specifically so it's reusable -- hides once the box has real text OR gains focus, not just once something's typed.

**Follow-up landed:** the debounce/filter logic itself was pulled out into `DebouncedTextFilter` (`Presentation/UI/DebouncedTextFilter.cs`), and `GridControl`'s own item search (see the Tab Stats row item below) is its second real consumer -- reusing the pattern, not copying it by hand. `GridControl`'s item-count display also reflects `InventoryGridContent.VisibleItemCount`, the currently-filtered count, not the tab's raw total.

#### Tab Stats row (landed, as `GridControl`)

Despite the old name, this landed as `GridControl` (`Presentation/UI/GridControl.cs`) -- a reusable, fully generic row of grid-scoped controls (item count, click-to-cycle sort button, a `DebouncedTextFilter`-backed search box, and now a *list* of click-to-toggle filter buttons rather than just one -- see the per-slot item divergence work's own "Stack Diverged" toggle alongside the original "Hide Disabled"), not an Inventory-specific "stats" widget. Sits between the tab strip and the grid via `InventoryTabContent` (`Presentation/UI/Content/InventoryTabContent.cs`), which composes one `GridControl` per tab above an `InventoryGridContent` and is the only piece that knows either is about items -- `GridControl` itself never references items, tags, or `InventoryGridContent`, so a future Magic Menu (see that item below) can reuse it directly. Item count only -- total weight still blocked on the Item weight and carry capacity scaling with Strength item below (no `ItemDefinition`/`InventoryItemStackComponent` carries a weight field yet). See `PLAN-inventory-item-filtering-and-tab-stats.md` for the full design, including why `InventoryGridContent` itself was deliberately *not* genericized into a reusable `Grid` primitive this pass.

#### Advanced sort control -- icon that expands into a context-menu of sort options

The Tab Stats row's sort control (see the Tab Stats row item above) ships as a plain click-to-cycle button first (Name A-Z/Z-A, Quantity High-Low/Low-High) -- cycling blind through a fixed order instead of picking one directly. Replace it with a small sort icon that expands into a context menu listing every sort option by name, so the user picks the one they want in one click instead of cycling to it. Depends on the Context menu / mouse button coverage item below (`UiInputController` has no right-click/menu-popup primitive yet) -- or, if a context menu ends up being left-click-triggered instead, at least depends on some generalized "popup a list of choices near a control" primitive not existing yet either way.

Should also cover **sorting by a stat** (e.g. a wand's remaining charges, or a future item's power/roll quality) once per-slot item divergence gives items stats worth sorting by -- not just Name/Quantity. Note that which stats are even available to sort by varies per item type, so the option list this control's own context menu shows may need to be built dynamically per tab/selection rather than the fixed list this entry otherwise assumes -- worth confirming the context-menu mechanism this entry already depends on ("Context menu / mouse button coverage") actually supports a dynamic option set before assuming it does.

#### Search icon that expands into a search bar

Both the Tab search box (`TabbedContent`, filtering which tab headers show) and the Tab Stats row's own item-name search box (see the Tab Stats row item above) ship as always-visible boxes taking up a fixed amount of row width. A more space-efficient alternative once real screen real estate pressure shows up: a small search icon that expands into the actual search box (with its ghost text/debounce behavior unchanged -- see `DebouncedTextFilter`) when clicked, collapsing back to just the icon when empty and unfocused. Purely a presentation change over the existing `DebouncedTextFilter`/`TextBox.GhostText` machinery -- no new filtering logic needed.

#### Inventory item FirstAcquired timestamp

No inventory stack tracks *when* it was first obtained today -- `InventoryItemStackComponent` only carries `ItemDefinitionId`/`Quantity`/`IsDisabled`. Needed to eventually offer a "recently acquired" sort order (the Tab Stats row's sort control above only supports Name/Quantity orders today, since nothing else is available to sort by). Should apply only to the *first* unit of a given item ever obtained -- picking up a 2nd/3rd Health Potion shouldn't reset the timestamp, only merging into an existing stack's `Quantity` (see `InventoryActions.AddItem`/`InventoryGrant`). Open design question: a field directly on `InventoryItemStackComponent` (simplest, but every stack pays for a field only meaningful once), or a separate sparse component/pool (e.g. `MultiComponentPool<ItemFirstAcquiredComponent>`, only ever added once per distinct item a given entity has ever held, mirroring how `ForceBlockingComponent`/`NonBlockingComponent` are sparse marker pools) -- the latter avoids the former's per-instance cost for a value that's genuinely rare-to-touch (set once, read only when sorting by it), closer in shape to `AchievementUnlockedComponent`'s own "exists at all" semantics than to a dense per-stack field.

#### Checkbox widget to replace the Hide Disabled toggle button (landed, as `Toggle`)

`Toggle` (`Presentation/UI/Toggle.cs`) landed as a real, generic checkbox widget -- a plain `Element`
(`InventoryItemStackCell`'s own "ordinary ChildElements participant, not a hand-rolled title-button special
case" shape, not `Button`'s), owning its own on/off state and visual entirely: a small bordered square
(Outset when off, Inset with a tint and a white X mark when on) plus a text label positioned outside it via
the new `LabelPosition` enum (`Presentation/UI/LabelPosition.cs`, four cardinal directions, generic across any
future control needing a non-title label) rather than centered as content. A caller wires what a flip does
via an `Action<bool> onToggled` delegate at `Configure` time -- no index, no generic event to route
externally by position. Not scoped to the Tab Stats row (now `GridControl`) despite landing there first --
see `Toggle`'s own doc comment. `GridControl.Configure` takes `(string Label, bool DefaultOn, Action<bool> OnToggled)`
per toggle now, having dropped the parallel `List<bool>`/index-keyed `ToggleChanged` event it used to route
flips through.

#### Item weight (definition-only) and race weight ranges

Companion to the Item weight and carry capacity scaling with Strength item above, which already covers *deriving carry capacity from Strength and gating pickups* -- this item is specifically about the data model those depend on:

- **Weight lives on `ItemDefinition` only, never `InventoryItemStackComponent`.** Weight, like `Name`/`Description`/`SpriteName`/`Glyph`/`Tags`, never varies per instance of a given item -- it describes *what kind of item this is*, not any particular stack's own state, the same distinction that already keeps `Quantity`/`IsDisabled` (genuinely per-stack, genuinely mutable) off `ItemDefinition` and on the component instead. A stack's total weight is `definition.Weight * stack.Quantity`, computed on demand -- no separate per-stack weight field needed. Checked whether any *other* `ItemDefinition`/`ActionDefinition` property is missing this same split: no -- every currently-shared, per-kind property (`Name`, `Description`, `Summary`, `SpriteName`, `Glyph`, `GlyphColor`, `Tags`, `Effects`) is already definition-only today, and every per-instance component (`InventoryItemStackComponent`, `ActionInstanceComponent`) only carries genuinely mutable/per-instance state (`Quantity`/`IsDisabled`, cooldown/lock timers, `DamageOverride`). Weight is simply the first weight-specific case joining an already-correctly-split pattern, not evidence of a broader gap.
- **Units: pounds.** All potions and scrolls that exist today (`Game/Modules/Inventory/Definitions/`) default to 0.1 lbs -- a starting value for *that class of item*, not a hardcoded constant every future potion/scroll must share; a future heavier potion (e.g. a large flask) or lighter/heavier scroll is free to differ.
- **Race weight ranges (a new, separate concept from carry capacity):** each race blueprint (`Game/Blueprints/Races/`) gets a min-max body-weight range of its own, alongside the flat per-race ability-score defaults the Stats item above already tracks as adjustable-in-a-balance-pass placeholders. First-pass estimates, explicitly rough and open to a real balance pass like those same ability scores: **Goblin 40-70 lbs** (small, wiry humanoid); **Fairy 20-40 lbs** (small winged humanoid, lighter than Goblin) -- both guesses made without any established in-game lore/size spec to anchor them, so treat as placeholders, not final. **Player and Ghost still need their own ranges too**, deliberately not guessed here: Player because a real range depends on decisions (character customization? a fixed adult-human range?) outside this TODO's scope, and Ghost because "weightless/ethereal" is a genuinely plausible answer for an incorporeal race that a placeholder number would paper over -- worth deciding deliberately rather than defaulting to a guess.

#### Game over screen on player 0 HP

`Game/Modules/Death/` (`HealthDamage.Apply`/`DeathSystem`/`DeadComponent`) handles death at 0 HP for every entity except the player -- deliberately exempted for now, since the player dying today has no distinct end state or UI at all. Needs this Presentation-side piece (a real game-over screen) before the player-side exemption in `HealthDamage.Apply` can be lifted.

#### Context menu / mouse button coverage

Right-click tap/drag mouse coverage landed (`UiInputController.HandleRightDragStart/Drag/DragEnd`, `Element.HandleRightClickTap`/`OnRightClickTapAction`) -- MapWindow's own right-click-tap already used it to cancel an armed/pending action before any context menu existed. Middle-click/double-click still have no coverage (so double-click-title-bar-to-maximize remains a future incidental win, not something this unlocked).

The context menu mechanism itself has landed too: `ContextMenu`/`ContextMenuController` (`Presentation/UI/`) -- a single shared popup any right-click source can open with its own list of `ContextMenuOption`s (label, optional right-aligned hotkey text, enabled state, an action), positioned at the cursor via the same `PopupPositioning` math `Tooltip` uses. See those types' own doc comments for the "shared mechanics, distributed content" split: the popup/positioning/dismissal machinery is centralized, but each right-click source decides its own option list. First consumer: a corpse's right-click menu offers "Loot" (disabled, not omitted, when the player isn't adjacent) -- replaces the old click-to-loot (`MapWindow.TryOpenCorpseContextMenuAt`, see the Corpse looting item under Game).

Second consumer, not yet done: TextBox's Cut/Copy/Paste/Select All (see the Text input item above) are still keyboard-only -- exposing them via this same context-menu mechanism is the natural next use of it.

See also AdvancedMapContextMenu below, which has since landed and generalized this same mechanism to four more right-click menus beyond the map tile.

#### AdvancedMapContextMenu (landed)

Right-clicking a map tile now stacks context-menu contributions from everything on that tile -- every occupant entity (`world.GetOccupantEntityIdsAt`, not just the Blocking one) plus the terrain, each its own group led by a read-only name header (`ContextMenuOption.Header` -- bold text on a slightly darker background, doubling as the visual separator between one group and the next, so no separate blank-divider concept was needed) followed by its own options: "Loot" first if it's a corpse, always "Inspect" (`MapWindow.TryOpenEntityContextMenuAt`). No `IContextMenuProvider` interface was built -- `ContextMenuController.Open` already just takes a flat option list, so a tile's menu is built by concatenating each occupant's/terrain's own option list (the same idiom TextBox's own option-building method already used).

The same header-row mechanism, and the generic `Element.OnRightClicked` settable-delegate hook it's built on (any `Element`, not just `Window`, can opt into a right-click menu without its own `OnRightClickTapAction` override), also now drive four more right-click menus:
- Window -> Close/Close All, for every DynamicHud window (`DynamicHudContextMenus.BuildCloseMenu`, wired via `InventoryFolderController`/`SecondaryInventoryWindowController`).
- Notification popup -> Close/Close All always; Minimize/Minimize All only when not a System notification, and Minimize All itself skips any open System notification (`NotificationCenter`).
- NotificationSummary -> Open/Open All, scoped to just the right-clicked category, disabled when that category has nothing unread (`NotificationCenter`).
- Inventory item -> Give/Take, conditional on whether a secondary inventory window is open and which side owns the clicked stack (`InventoryGridContent.BuildGiveTakeMenu`). Needed a way for the player's own inventory grid to ask "is a secondary window open, and for whom" without a direct circular dependency on `SecondaryInventoryWindowController` (each depends on the other) -- resolved via a settable `InventoryFolderController.GetSecondaryTargetEntityId` callback, wired by `ShellBootstrapper` after both controllers exist, the same late-bound-delegate pattern `OnCorpseClicked`/`OnInspectionOpened` already use for an identical construction-order cycle.

**Deliberately not built this pass:** "Bind To..." (item -> hotbar slot via a context-menu sub-menu) -- see its own Low Priority TODO item below, since it needs a cascading-submenu capability this pass never otherwise required.

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

#### Tooltips, full description/stat views, context menus, and click-to-arm/cast on the inventory and magic menus

Partially covered already by two existing items above: Item inspection popup (click-to-see-full-detail) and Inventory item hover summary (hover-for-short-summary) -- both inventory-only today. This item's real incremental scope: (1) extend both to the new Magic Menu (see that item below) once it exists, so a spell gets the same inspect/hover treatment an item stack already would; (2) right-click context menus (arm, drop/discard, inspect, ...) on cells in either menu, which needs the Context menu / mouse button coverage item above landed first (`UiInputController` has no right-click detection at all yet); (3) click-to-arm/cast directly from a menu cell -- today the hotbar is the only way to arm an action or item for activation (`HotbarContent`/`ConsumableActivationSystem`/`ActionTargetingController`'s arm/target/confirm path), so this needs that same arm-state to be settable from an inventory/magic-menu click too, not just a hotbar slot press.

#### Player selection menu (non-debug) (landed as Inspection V2's Basic/Detail modes -- skill-gating still open)

`InspectionWindow`/`InspectionWindowContent` (`Presentation/UI/`) replaced the old debug-only `SelectionWindowContent` with the player-facing view this item asked for: Basic mode (click a map tile) shows curated, human-readable content -- icon, name/race/class rows, an HP bar, description -- for every entity on the tile plus terrain, not a raw component dump; Detail mode (right-click an entity -> Inspect) follows a single target the same way, on a shared global cooldown. `ComponentInspector`'s raw per-component `ToString()` dump didn't go away -- it's now Detail mode's always-appended Admin section (see `MapViewState.InspectionMode`'s own doc comment on why Admin isn't gated separately yet), rather than the only view that existed.

Still open, exactly as originally scoped: gating *content depth* by the observer's own Skill levels (see the Skills item under Game above) -- e.g. a low-level Perception-equivalent skill showing only name/rough health, a higher level revealing exact stats/resistances/status effects. Blocked on Skills actually existing first, same as before. Note this is a different gate from the Admin Mode Toggle item below (Basic vs. skill-scaled Detail content is a player-facing concern; the Admin Mode Toggle is a separate debug-only on/off switch for the raw component dump).

### Medium Priority

#### Diagonal movement input timing

`PlayerMovementController.HandleInput` reads `KeyboardState` once per poll and only treats a move as diagonal if both direction keys happen to be down in that same instant. A human rarely presses two keys in the exact same frame -- a few-frame gap between, say, pressing W then D lands as a cardinal move (consuming its cooldown) before the second key registers, even though the player meant to move diagonally. Needs a short input-buffering window (hold the first key's delta briefly, waiting to see if a second orthogonal key follows, before committing to a cardinal move) instead of reading raw simultaneity.

Affected: `Presentation/UI/PlayerMovementController.cs` (`HandleInput`).

#### Targeting tile highlights extend beyond the actual spell/scroll range

`ActionTargetingController.ComputeTargetableTiles` (`Presentation/UI/ActionTargetingController.cs`) computes the arm-time reachable-area highlight for every cursor-directed shape (`SingleTarget`/`Burst`/`Line`/`Cone`) via a `TargetShape.Burst`-shaped scatter out to `Range` -- "not the real Shape," per its own doc comment, since there's no cursor direction yet at arm time. That approximation overshoots for `Cone`/`Line`: a narrow cone or a straight line can't actually reach every tile in the full diamond-shaped Burst scatter at the same range, so the highlight shows tiles as targetable that the shape could never actually hit once a direction is chosen. Confirmed in-game. Needs a real per-shape reachable-area computation (or at least a `Cone`/`Line`-specific one) instead of the one-size-fits-all Burst-scatter stand-in.

#### Confirming activation on an empty tile still fires the spell/scroll

`ActionTargetingController.TryConfirmActivationAtTile` only checks that the clicked tile is in `MapViewState.TargetableTiles` (the reachable area) before calling `QueueArmedActivation` -- it never checks whether the resolved footprint (`TargetShapeResolver.Resolve`) actually contains an occupant. Clicking an empty, unoccupied tile within range still queues and fires the activation, wasting a scroll charge or mana with no effect landing on anyone (`ConsumableActivationSystem`/`ActionActivationSystem` then apply the effect to zero targets). Needs an occupant check before confirming -- at minimum for single-target-ish shapes (`SingleTarget`/`Adjacent`); an AOE shape (`Burst`/`Cone`/`Line`) landing on an empty tile but still catching something else in its footprint is a separate, less clear-cut case worth deciding deliberately rather than by accident.

#### Minimap, folded into the Neighborhood/Borough zoom-level refactor, plus Fog of War

A collapsed minimap in the bottom-right corner of the HUD; expanding it replaces/takes over the existing zoom-out feature rather than living alongside it as a separate control. Folded into the same work as `MapCamera`'s `Neighborhood`/`Borough` zoom levels (`Presentation/UI/MapCamera.cs`) below, since both are "look at more of the map than the current viewport" features that should share one implementation rather than two:

`MapCamera`'s `Neighborhood`/`Borough` zoom levels will render static structures only (walls/terrain) plus special sprites for bosses and important locations -- no moving entities. These are fixed-grid "check the larger map" views, not playable zoom levels: instead of centering on the player like `Team`/current zoom levels do, they snap to preset square regions -- a `Neighborhood` is 1000x1000 tiles, a `Borough` is 2000x2000 (a 2x2 block of neighborhoods) -- the same region sizes `Game/Modules/ProcessingTier/Systems/ProcessingTierSystem.cs` uses for its distance-throttle tiers, so both features share one spatial vocabulary. The collapsed minimap would be a small, always-visible rendering of the player's immediate surroundings at this same static/no-moving-entities fidelity; expanding it is what actually switches into the full Neighborhood/Borough zoom view.

Also add Fog of War: areas the player hasn't explored yet render blank/hidden on both the minimap and the expanded zoom views (and the main viewport), revealed permanently once seen (or per whatever design is chosen -- permanent-reveal vs. re-fogging unvisited areas is still an open question). No existing infrastructure for per-tile "has this been seen" tracking -- needs a new per-tile (or per-region, for performance at the Neighborhood/Borough scale) visibility store, likely keyed the same flat-index way `AuraGrid`/`MapTintGrid` already key their own per-cell dictionaries (`Vector3Int.FlatIndex`).

#### Magic Menu

Spell-equivalent of the inventory menu -- tracks which spells the player currently knows and lets them be armed/cast from it, mirroring `InventoryFolderController`/`InventoryManagementWindow`'s `Folder` + pooled-`Window` + `TabbedContent` pattern (`Presentation/UI/Inventory/`). "Known spells" isn't a distinct tracked concept yet -- a spell is just an `IActionActivator`/`AbilityDefinition` an entity has an `ActionInstanceComponent` for, filtered by `Tag.Spell` the same way `SpellCasterAchievement` already does (see the "Tag.Spell can drift out of sync" item above for the risk of trusting that tag alone) -- so this menu is a `MultiComponentPool<ActionInstanceComponent>` query/grid over Spell-tagged entries, not a new storage primitive. See the Tooltips/context menus/click-to-arm item below for the interaction layer once the grid itself exists, and the Tomes item below for a second way spells get granted besides Scroll Mastery.

#### Comprehensive control-selection feature

`UiInputController.SetFocus`'s `NextFocusableDescendant` redirect (a window with a focusable `TextBox` child is never itself the terminal focus target -- focusing the window redirects into its first `TextBox` instead) was the quest composer's whole mechanism for "click the popup to start typing," and for a while doubled as the Inventory tab search box's own click-to-select path too, piggybacked on top of whatever interaction a press happened to resolve to. That piggybacking caused two confirmed bugs once the Inventory window's search box (a `TextBox` child of a `CanUserMove`+`CanUserResize` window) made the interaction actually matter: starting a resize drag, and separately a move drag, on the window silently redirected keyboard focus into the search box neither drag had anything to do with. Both are fixed narrowly today (`HandleMousePress` only resolves focus for `ElementDragInteractionKind.None`, i.e. a plain click, never `Move`/`Resize`) -- but that fix also quietly removed the quest composer's original "drag the popup by its title bar to focus its TextBox" convenience, which had no other explicit affordance to fall back on.

Needs a real, explicit design for how a user selects a specific interactive control within a window that hosts more than one candidate (a `TextBox`, and in the future other input widgets) -- not an implicit side effect of whichever drag/click interaction happened to resolve against the window. Candidates worth weighing: click-to-focus scoped strictly to a direct hit on the control itself (no container-level redirect at all, pushing the quest composer to auto-focus its `TextBox` some other way, e.g. on open); a designated "default control" concept a window can declare, focused on `Initialize`/on becoming visible rather than on every click; or keeping a redirect but gating it explicitly to `Kind.None` clicks that land on the window's own content area specifically (already true today) while giving `Move`/`Resize` starts an explicit opt-in if a future window genuinely wants that. Affects `Presentation/Input/UiInputController.cs` (`HandleMousePress`/`SetFocus`), `Presentation/UI/Element.cs` (`NextFocusableDescendant`), and whichever window(s) end up wanting the "click title bar to start typing" convenience back (the quest composer today, `TabbedContent`'s search box potentially in the future).

#### UI visual overhaul

Every window today draws through the same small set of shared primitives (`BorderRenderer`'s Flat/Outset/Inset/FlatContrast styles, `GlowRenderer`'s outward glow, `WindowPalette`'s dark grey background/content colors) but nothing about the overall *look* has had a deliberate visual design pass -- it's whatever each feature landed with. Target direction: Elden Ring-style menus (dark, minimalist, understated chrome -- `WindowPalette`'s existing dark-grey-on-dark-grey palette is already pointed this way, just not pushed further) combined with FFXIV-style hotkey/action-bar presentation (`HotbarContent`, `Presentation/UI/Content/HotbarContent.cs` -- today's flat-bordered slot grid is the concrete thing an FFXIV-style pass would redo). Also: `GlowRenderer.Draw` (already used by `HotbarContent`'s armed-slot glow, `NotificationCenter`'s unread-glow, `Folder`) currently only draws an *outward* ring glow around a rectangle's border (`GlowRingCount` concentric 1px rings expanding outside `bounds`, `MaximumAlpha` 0.7) -- the element's own interior never highlights at all. Wanted instead: a highlight that covers the *whole* element (interior included, not just an outward-facing ring), and noticeably lighter/more subtle than today's 0.7 max alpha -- a restrained highlight, not a strong one. No concrete spec yet -- this is a direction to design against, not a worked-out visual language; a real pass would need mockups/reference images before touching `BorderRenderer`/`GlowRenderer`/`WindowPalette` themselves, since those three are shared by every window in the game.

#### Inventory grid item badge clarity

`InventoryItemStackCell` (`Presentation/UI/Content/InventoryItemStackCell.cs`) has accumulated several small
overlay badges -- a bottom-right quantity-or-charges number (`ItemIconRenderer.DrawQuantityBadge`, either
"x5" or "5/6", see its own doc comment for why the two never show together), a top-right "+" (`MergedStackBadgeVisible`,
meaning "this is a collapsed Merged Stack, click to expand" -- see the class's own Base/Diverging/Merged
Stack doc comment), and the black group-perimeter border an *expanded* Merged Stack's own member cells draw
instead of their normal one -- each landed incrementally, addressing its own immediate need, with no
deliberate pass over how they all read together at a glance. Confirmed source of real ambiguity already:
the bottom-right number silently changes *meaning* (how many I have, vs. how many uses this one specific
item has left) with zero visual distinction between the two -- directly at odds with `CLAUDE.md`'s own
"remove ambiguity" Presentation design principle. `HotbarContent`'s matching badge slot (`BuildItemVisual`) has
the identical quantity-vs-charges ambiguity and should be covered by the same pass, not fixed separately.
No concrete redesign specified here -- worth a deliberate look (icon instead of a bare "+", a visual tell
distinguishing "count" from "uses remaining," etc.) once there's room to design against rather than adding
more badges piecemeal.

#### Button tooltips

No icon-only or symbol-only button explains itself on hover today -- a new player has no way to learn what a title-bar button or folder tile does short of clicking it. Needs hover tooltips (`Tooltip`, the same delay-gated popup `InventoryGridContent`/`AbilityScoreWindow` already use for their own item/stat hovers) on: every `Window` title button (`CloseBehavior`'s "X", `MinimizeRestoreBehavior`'s restore/minimize glyph, `ItemDetailsWindow`'s own "↔" Compare button), the Inventory and Ability Score folder tiles (`InventoryFolderController.CreateTile`), and the Notification Manager and Inventory folder icons themselves (`NotificationCenter`/`InventoryFolderController`'s own `Folder` elements). `Window.AddTitleButton`'s per-button `Button` instances and `Folder`'s own icon are both plain `Element`s already, so this is "wire up an existing hover-popup pattern in a few more places," not a new mechanism.

### Low Priority

#### Inventory item "Bind To..." context-menu option, and context-menu sub-menus

Deferred from AdvancedMapContextMenu (see above, landed) -- right-clicking an inventory item stack should offer "Bind To..." (bind it to a hotbar slot without dragging), but there's no slot-picker UI to hang it off today: binding only ever happens by dragging a cell onto a hotbar slot (`HotbarContent.BindItem`/`ItemHotkeyBindingComponent`). The agreed shape is a context-menu sub-menu listing every hotbar slot (`HotkeySlotLayout.Entries`/`GetKeyLabel` for the label, `HotkeySlotLayout.IsLocked` to show a locked slot disabled rather than omitted, matching this codebase's established "show why, don't just hide" convention elsewhere) -- picking one calls the same bind primitive drag-and-drop already uses. Not available for a Merged Stack cell (`InventoryItemStackCell.CanBindToHotbar` false), same restriction drag-binding already enforces.

Needs a genuinely new capability neither `ContextMenuOption` nor `ContextMenu` has: a cascading sub-menu. An option would carry a nested `IReadOnlyList<ContextMenuOption>` instead of (or alongside) `OnSelect`; clicking it opens a second popup east of the clicked row (the same `PopupPositioning` math the main menu already uses) rather than invoking `OnSelect`/closing; `ContextMenuController` would need a second managed `ContextMenu` instance for it, opened/closed alongside the parent; and `UiInputController`'s existing outside-click-closes-the-menu check (today reads `ContextMenuController.Menu`'s rectangle alone) would need to check both rectangles. None of AdvancedMapContextMenu's five landed menus needed this, so it was deliberately left unbuilt rather than guessed at.

#### Split Presentation into Presentation + UIEngine projects

Mirrors the existing `Engine → Game` split (see CLAUDE.md's own Layers note: Engine is generic ECS/modding infra with "no game-specific knowledge," Game depends on it) one layer up: `UIEngine` would hold the generic Element/window framework and its basic supporting logic, `Presentation` would hold the game-specific concrete windows/content built on top of it. Dependency rule, settled: `UIEngine` may reference `Engine` only (never `Game`, keeping it exactly as game-agnostic as `Engine` itself is); `Presentation` may reference `UIEngine` and/or `Engine` (and, as today, `Game`) -- never the reverse in either case. `UIEngine` sits parallel to `Game` (both depend only on `Engine`), not stacked between `Game` and `Presentation`: `Engine → Game`, `Engine → UIEngine`, `Game + UIEngine → Presentation → DungeonCrawlerWorld(exe)`.

**Clearly `UIEngine` today** (no game-specific types referenced): `Element`/`Window`/`TextWindow`/`TextBox`/`Button`/`Folder`/`TabbedContent`, every `Element*Options`/`Element*State` file, `ElementPoolService`, `ElementDisplayMode`/`ChildElementTileMode`/`BorderStyle`/`BorderThickness`, `BorderRenderer`/`GlowRenderer`, `HoverPopupWindow`/`PopupAnchor`/`PopupPositioning`, `IElementContent`/`IChromeBehavior`/`ChromeBehaviors/*`, `VersionWatcher`, `SeparatorBar`, `LabelRenderer`/`SpriteBatchRenderer`/`ContrastTextRenderer`, `ElementInteraction`/`ResizeEdges` (all `Presentation/UI/`, `Presentation/Rendering/`, `Presentation/Input/`).

**Clearly stays in `Presentation`** (game-specific concrete UI): `MapWindow`/`MapCamera`/`MapTintGrid`/`MapBackgroundCache`/`MapViewState`, `HotbarContent`/`HotbarController`/`ArmedHotkeySummaryWindow`/`HotkeySlotLayout`, `ActionTargetingController`/`PlayerMovementController`, everything under `Presentation/UI/Inventory/`+`Presentation/UI/AbilityScores/`, `NotificationCenter`/`Notification`/`NotificationMinimizeBehavior`, `PlayerHealthBarContent`/`PlayerManaBarContent`/`PlayerStatusEffectsContent`/`ActionLockContent`, `InspectionWindow`/`InspectionWindowContent`/`DebugWindowContent`, `DragGhostContent`/`DragGhostRenderer`, `ItemIconRenderer`/`RadialFillRenderer`/`PotionCooldownTextRenderer`, `SpriteSheetService`/`SpriteRenderer`/`SpriteOrGlyphRenderer`/`TileRenderer` (all tied to `SpriteManifest`/game content).

**The genuinely hard part, and why this is high-complexity, not a mechanical file move:** `UiInputController` (`Presentation/Input/UiInputController.cs`) is mostly generic (hit-testing, Move/Resize drag, focus, scroll) but has game-specific logic woven directly into the same methods -- `HandleMousePress`/`HandleMouseRelease` call into `HotbarController`, `ActionTargetingController`, and content-drag payload capture that pattern-matches on `InventoryItemStackCell`/`HotbarContent` by concrete type. A clean split needs those branches pulled out from behind some `UIEngine`-defined hook/callback/event surface that `Presentation` implements and registers, not a straight move -- otherwise `UiInputController` itself becomes the thing that can't cleanly live in either project. `ColorPalettes` (`WindowPalette` reads fairly generic; `HealthBarPalette`/`ManaBarPalette` don't) and `HudMetrics` (generic sizing/margin constants, but currently only ever consumed by game-specific content) need the same kind of judgment call, not an obvious answer.

No solution designed here -- this is a scoping/direction note, not a worked-out migration plan. Low priority: today's single-project `Presentation` isn't blocking anything, this is purely an architectural-cleanliness investment for whenever `UIEngine`-shaped reuse (a second game, a tools app, ...) actually becomes a real need rather than a hypothetical one.

#### Abstract element pool factory registration

Every `ElementPoolService.RegisterFactory<T>` call (`ElementPoolService`'s own constructor for `Window`/`TextWindow`/`TextBox`, `GameShellBootstrapper.cs` for every other pooled type, `HotbarController.cs` for `ArmedHotkeySummaryWindow`) hand-writes `() => new T(fontService, elementPoolService, labelRenderer, ...)` -- the same `FontService`/`ElementPoolService`/`LabelRenderer` triple repeated verbatim in almost every registration, with only a handful of type-specific extras (`SpriteSheetService`/`SpriteRenderer`, `ComponentManager`, `itemCatalog`, `dynamicHudWindows`) varying per type. `GameShellBootstrapper.cs` alone has a dozen-plus of these near-identical two-line calls (see its `RegisterFactory<Folder>`/`RegisterFactory<InventoryManagementWindow>`/`RegisterFactory<AbilityScoreWindow>`/etc. block). Worth a pass abstracting the common shape out -- e.g. a factory helper that takes only the type-specific extra dependencies and supplies the repeated `fontService`/`elementPoolService`/`labelRenderer` trio itself -- so registering a new pooled Element type doesn't mean re-typing the same three arguments by hand every time. Low priority: today's repetition is boilerplate, not actually error-prone or blocking anything.

#### Red X marker over dead entities

`DeathSystem`/`DeadComponent` (`Game/Modules/Death/`) reclassify a corpse as non-Blocking and mark it dead, but nothing currently renders a corpse any differently from a living entity beyond whatever its own sprite/glyph already looks like -- `MapWindow.TryDrawEntityVisual` has no `DeadComponent` check at all today. Draw a red X overlay (mirroring how the health bar/status-effect icons already overlay extra detail on top of an entity's base sprite) whenever the entity being drawn has a `DeadComponent`, so a corpse reads as dead at a glance instead of looking identical to a living, just-motionless entity.

#### Folder glow blink

`Folder.SetGlow`/`Element`'s `_isGlowing`/`_glowColor` (`Presentation/UI/Element.cs`, consumed today by `NotificationCenter` to gold-glow a folder/summary window with unread notifications) is a flat on/off highlight with no animation. Make it pulse/blink while active instead of staying static -- more noticeable for "you have something unread" than a constant tint, especially once more things drive glow (a future Magic Menu's unread spells, Skills leveling up, etc.).

#### Solid highlight border for highlighted tiles

`DrawMaskedTileHighlight` (`Presentation/UI/MapWindow.cs`) draws every highlighted tile -- targetable/hovered ability tiles, the pending-Delayed-action fallback, and the inspector's Gold selection alike -- as one uniform translucent wash (`borderColor * TargetSelectionMaskAlpha`, 50% alpha) across the whole tile. Despite the name, there's no actual border/outline drawn at all today -- an earlier "inset mask with a solid opaque border ring" technique was deliberately replaced by the current whole-tile wash (see that method's own doc comment) so the sprite underneath stays visible. Add back a thin border ring at 100% opacity, using the same `borderColor` the wash already uses, on top of the translucent fill -- makes a highlighted tile's actual edges legible (useful once `ComputeTargetableTiles`'s Cone/Line overshoot above is fixed and the highlighted shape stops being a simple rectangle-ish blob) without giving up the "sprite stays visible" property the wash-only redesign was for.

#### Improved targeting visuals -- fainter glow plus opaque corner markers

A different design direction for the same code the "Solid highlight border for highlighted tiles" item above targets (`DrawMaskedTileHighlight`, `Presentation/UI/MapWindow.cs`) -- worth deciding between the two rather than landing both. Instead of a full-tile wash plus a thin full-perimeter border ring, make the translucent fill noticeably more transparent than today's flat `TargetSelectionMaskAlpha` (0.5), and replace the ring with four short corner marks -- one bracket at each of the tile's four corners, inset slightly from the actual tile edges -- drawn at full (100%) opacity in the same `borderColor` (`TargetableTileBorderColor`/`HoveredTargetTileBorderColor`) the wash already uses. Reads as a lighter, more legible highlight than either today's flat wash or a full opaque ring would, while still keeping the underlying sprite/terrain visible through the fainter glow. No concrete corner-mark geometry (bracket length, inset distance) worked out yet.

#### Extract a shared tick-fraction HUD bar element

`PlayerHealthBarContent` and `PlayerManaBarContent` (`Presentation/UI/Content/`) are near-duplicates: same outer-outline-plus-inset-fill draw shape, same `MajorTickFractions`/`MinorTickFractions` ruler graduations (`DrawTicks`/`DrawTick`), same `ContentSize`-not-`Size` sizing rationale, same no-component fallback-color pattern -- only the backing component/pool, `StatModifierTarget`, and palette (`HealthBarPalette`/`ManaBarPalette`) actually differ. Tolerable at two copies; if a third tick-fraction bar shows up (e.g. a Soul Essence bar for soul-based abilities), abstract the shared draw logic out into one generic element instead of copy-pasting a third time -- e.g. a base class or a small shared renderer taking (current, effectiveMax, palette, no-value fallback color) and leaving only the component/pool lookup to each concrete bar.

`MapWindow.DrawHealthBar` (`Presentation/UI/MapWindow.cs`) is arguably already a lighter third instance -- the same core `effectiveMax`-fraction inset-fill-rectangle computation (via `StatModifierMath.GetEffectiveValue` against `StatModifierTarget.MaximumHealth`), just without the tick-mark graduations and positioned above a map entity's glyph instead of in a HUD corner, and per-*any*-entity rather than player-only. Not identical enough to fold in as-is, but worth including in scope if this item is ever picked up, rather than treating it as a fourth future copy once a "real" third HUD bar shows up.

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

#### HealthWindow -- per-body-part health and status display

Unblocked now that the Body parts item (Game, above) has landed. A bar showing only the summed total (today's `PlayerHealthBarContent`/`MapWindow.DrawHealthBar`, both now driven by `HealthQueries.TryGetTotals`) can't show a Complex entity's real state -- a summed total bar hides which specific part is critical, and death-by-Vital-part-at-zero can happen while the bar still reads well above empty (see the Body parts item's own note on this). Needs a real window (`HealthWindow`, mirroring `AbilityScoreWindow`'s `Folder` + pooled-`Window` pattern) listing every body part the inspected entity owns -- name, current/maximum HP (reusing the "Extract a shared tick-fraction HUD bar element" item's eventual shared bar renderer if that lands first, or `PlayerHealthBarContent`'s own draw shape otherwise), Vital/disabled state, and whatever status effects (see the per-body-part status effects item under Game above) are currently active on that specific part. Likely a natural extension of `InspectionWindowContent`'s Basic/Detail modes (see the Player selection menu item above) rather than a fully separate window -- worth deciding which once ComplexHealth's actual query shape (`HealthQueries.TryGetTotals` and whatever per-part enumeration accompanies it) exists to design against.

#### Player health bar hover -- per-body-part HP dropdown

Unblocked now that the Body parts item (Game, above) has landed. Hovering the player's own health bar
(`PlayerHealthBarContent`) shows a small dropdown-style popup listing every body part the player
currently owns, one line each: name and current/maximum HP as a percentage. The first entry is
always the entity's total percentage (`HealthQueries.TryGetTotals`'s summed current/maximum,
matching whatever the bar itself is already showing), followed by one line per part in whatever
order `BodyPartComponent`'s own chain enumerates them. A `SimpleHealth` player (today, always)
has nothing to hover into beyond the single total line -- this popup only becomes more than that
once the player's own race is ever made Complex, which isn't planned by this item or the Body
parts item's own Phase 3 proof case (Goblin only).

Reuses the same delay-gated `HoverPopupWindow` pattern `InventoryGridContent`/`AbilityScoreWindow`
already use for their own item/stat hovers (see the Inventory item hover summary item above) --
not a new popup mechanism, just a new consumer of it. Deliberately lighter-weight than the
`HealthWindow` item above: a glanceable hover list (name + HP% only, no Vital/disabled state, no
status effects, no click-to-open), for a quick check without opening a real window -- `HealthWindow`
remains the click-driven, full-detail view once it lands. Worth checking whether this hover and
`HealthWindow`'s own per-part row rendering can share one small "format a body part's HP line"
helper once both exist, rather than each formatting the same name+percentage pair its own way.

No way to view a player's ability scores exists today -- add one alongside the inventory window (same `Folder` + pooled-`Window` pattern as `InventoryFolderController`/`InventoryManagementWindow`, `Presentation/UI/Inventory/`). Display the 5 Core scores' `Total` (Hidden scores stay invisible by design) and total buffs/debuffs, with an explanation popup showing the origin of each -- filterable straight out of `MultiComponentPool<StatModifierComponent>` by `Target` (`Game/Modules/StatModifiers/`). Lets the player assign stat points to increase stats once level-up exists. See the matching Game stats item above.

#### Text Input Enhanced Features (landed)

Follow-on to Text input above, once a TextBox actually needed more than "type to append, Backspace to remove from the end" -- driven by the upcoming Journal feature needing something close to a standard desktop text editor. Landed in `Presentation/UI/TextBox.cs` across five phases:

- Cursor-addressable editing (`_caretIndex`, insert/delete at an arbitrary position -- typing, Backspace, Delete, and Shift+Enter's newline all operate at the caret now, not just the end).
- Arrow-key navigation (Left/Right; Ctrl+Left/Right word-jump via `FindPreviousWordBoundary`/`FindNextWordBoundary`; Up/Down for multiline, preserving desired pixel X across consecutive moves; Home/End/Ctrl+Home/Ctrl+End).
- Click-to-position-cursor (`OnContentClickAction` override, `HitTestCaretIndex`), double-click-selects-word, triple-click-selects-line, Shift+click extends selection -- all sharing the same hit-test.
- A blinking visual caret (`CaretBlinkIntervalFrames`, the same `GameTiming.FramesForSeconds` delay-gated idiom used elsewhere), reset to solid-visible on every edit/navigation.
- Full selection: Shift+arrow/Home/End/Ctrl+Home/Ctrl+End, click-drag (new `UiInputController` plumbing -- `_textSelectionDragBox`/`HandleTextSelectionDrag`, mirroring the existing right-drag tap/drag distinction), Ctrl+A, Ctrl+Backspace/Ctrl+Delete word-deletion, and typing/Backspace/Delete/Shift+Enter replacing an active selection first (`TryDeleteSelection`).
- Ctrl+C/Ctrl+X/Ctrl+V clipboard support -- see the Text copy to clipboard item below, landed together with this.
- Key-repeat on held Backspace/Delete/Left/Right/Up/Down (`ShouldFire`, one shared initial-delay-then-interval timer) -- Backspace/Delete moved from the edge-triggered `HandleKeyPress` hook into the same per-frame `OnHotkeysAction` hook the arrows already used, so all six share one repeat mechanism instead of two different ones.
- I-beam cursor on hover (`UiInputController.GetHoverCursor`), and single-line boxes clip/scroll horizontally to keep the caret visible (`_visibleStartIndex`/`EnsureCaretVisible`/`GetVisibleWindowText`) instead of wrapping or overflowing.

Not landed: word-jump/double-click-select existed as stretch goals when first scoped and both landed; undo/redo did not -- see its own dedicated TODO item below. No context-menu (Cut/Copy/Paste/Select All) either -- no longer blocked (see the "Context menu / mouse button coverage" item, which now has a working ContextMenu mechanism), just not yet wired up to TextBox specifically.

Affected: `Presentation/UI/TextBox.cs`, `Presentation/UI/TextWindow.cs`, `Presentation/UI/Content/CursorTextContent.cs` (new), `Presentation/Input/UiInputController.cs`, `DungeonCrawlerWorld/GameShellBootstrapper.cs`.

#### Text input undo/redo (Ctrl+Z/Ctrl+Y)

Deliberately left out of the Text Input Enhanced Features pass that added cursor editing/selection/clipboard support to `TextBox` -- undo/redo needs a real edit-history design (a stack of applied edits or snapshots, coalescing rules for "was this keystroke part of the same undo step as the last," a cap on history depth), not a small addition on top of the existing caret/selection work. Also not yet clear whether that history should live on `TextBox` itself or on some shared primitive a future second editable control (see the Standard widget set item's still-missing list) would also want -- worth a dedicated design pass once a second editable control actually exists, rather than guessing the shared shape from `TextBox` alone.

#### WrapContent parent sizing collapses when a child resizes itself after being attached

Discovered building the quest-composer popup (see Text input above): a `WindowDisplayMode.WrapContent` window whose size depends on a child, paired with a child that later resizes *itself* (not at attach time -- `AddChildWindow`/`RemoveChildWindow` already re-fit a WrapContent parent correctly on attach/detach), collapses both windows toward `(0,0)` instead of settling on a real size. Confirmed with a failing test (a `WrapContent` parent + a multiline `TextBox` child, `TextBox.AutoSizeToContent` calling the parent's own `MeasureAndArrange` after each resize) before backing out of that design.

Root cause: `Window.Measure` unconditionally overwrites a child's own `_geometry.MaximumSize` with `_parentWindow.ContentSize - RelativePosition` on every pass (see the top of `Measure`), regardless of whatever `MaximumSize` the child was actually built with. For a `Fixed`-size parent this is harmless (`ContentSize` is already stable, independent of children). For a `WrapContent` parent it's circular: the parent's own `ContentSize` is *derived from* its children's current sizes, but a child that resizes itself gets its own cap silently rewritten to that same not-yet-correct parent `ContentSize` -- which starts at `(0,0)` before the parent has ever measured a child, so the loop starts degenerate and never escapes it (each side keeps "confirming" the other's near-zero size instead of converging on the child's actual intended size).

The quest-composer popup works around this today by staying `Fixed` and having `GameShellBootstrapper.OpenQuestComposer` explicitly resize the popup off the TextBox's own `Resized` event, with a chrome-overhead constant computed once up front -- see that method's own comments. That's a fine one-off answer but doesn't generalize: the *next* thing that wants "container shrinks to fit a child, then grows as that child grows" will hit the exact same wall.

A real fix likely means `Measure` shouldn't blindly overwrite a child's `MaximumSize` from `_parentWindow.ContentSize` when the parent is itself `WrapContent` mid-resolution -- e.g. a child's own explicitly-authored `MaximumSize` (captured once at `BuildWindow`, the way `TextBox` was almost given its own independent cap field before this got scoped down to the `Fixed`-parent workaround) should take precedence over whatever the parent's not-yet-settled `ContentSize` currently is. Worth a real design pass rather than a quick patch, since it touches the shared Measure/Arrange pipeline every window goes through.

Affected: `Presentation/UI/Window.cs` (`Measure`, `MeasureAndArrange`, `RecalculateWrapContentWindowSize`).

#### Text copy to clipboard (landed, superseded -- not click-to-copy)

Resolved as part of Text Input Enhanced Features' Ctrl+C/Ctrl+X, not the click-to-copy this item originally proposed -- click-to-copy was deliberately rejected (too easy to trigger by accident, and `TextWindow.OnContentClickAction` fires before the public `Clicked` event on every `Element`, which would have silently copied text on every existing click-to-open `TextWindow`, e.g. the Quest trigger and a notification's count window). A deliberate, per-feature copy *icon* affordance remains a real option later, wherever a specific feature actually wants click-to-copy -- not a blanket behavior on every `TextWindow`.

#### Selectable/copyable read-only text (move selection out of TextBox and into TextWindow)

`TextBox`'s selection machinery -- `_lineSpans`, click-to-position hit-testing (`HitTestCaretIndex`/`FindColumnForPixelX`/`FindLineIndexFor`), double/triple-click word/line select, click-drag (`UiInputController`'s `_textSelectionDragBox`/`HandleTextSelectionDrag`), Ctrl+A, selection rendering (`DrawSelectionIfAny`), and Ctrl+C -- is all built against `TextWindow`'s own `DisplayText`/word-wrap, not anything editing-specific. None of it needs a caret, typing, or Backspace/Delete to make sense; a plain read-only `TextWindow` (a notification body, a tooltip, a quest log entry, `InspectionWindowContent`'s description/admin-dump text) could support "click-drag to select, Ctrl+C to copy" the same way, without ever becoming editable. Worth moving the selection/hit-test/copy half of `TextBox`'s logic up onto `TextWindow` itself (leaving only caret/editing -- typing, Backspace/Delete, the blinking caret, Ctrl+X/Ctrl+V -- as what actually makes `TextBox` a `TextBox`), gated so a plain `TextWindow` never shows a caret and never accepts typed input, just selection + copy. `TextBox` would then extend that base selection support rather than duplicating it.

Investigated whether the same could easily extend to a `Window`'s title bar text: no -- `Window.TitleText` (`Presentation/UI/Window.cs`) is a separate, simpler mechanism, not built on `TextWindow`/`DisplayText` at all. It's a single raw string (`_titleText`) drawn with one direct `spriteBatch.DrawString` call in `DrawHeader`, with no word-wrap, no `_lineSpans`-equivalent, and no per-character measurement -- and `OnHeaderClickAction` only hit-tests title *buttons* today, not the title text itself. Reusing the selection logic above would need `TitleText` to be rebuilt on top of the same `DisplayText`/line-span infrastructure first, which is a much bigger change touching every `Window` in the game (title bars are universal, unlike `TextWindow`, which only some windows use) for comparatively low value -- most titles are short, static labels ("Inventory", "New Quest (Enter to submit)") nobody would want to copy. Worth revisiting only if a real need for copyable title text shows up; not bundled with the read-only-`TextWindow` selection work above.

Affected: `Presentation/UI/TextWindow.cs`, `Presentation/UI/TextBox.cs`, `Presentation/Input/UiInputController.cs`.

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

#### Direct menu-opening hotkeys (e.g. I for Inventory)

No keyboard shortcut opens any HUD window directly today -- the Inventory window (`InventoryFolderController`) only opens by clicking its Folder tile, which "toggles only the Inventory window (opens it if closed, closes it if open)" per that class's own doc comment -- exactly the behavior a hotkey should trigger too. Wanted: a global, unconditional hotkey per menu (I for Inventory to start; Stats/Equipment/etc. would follow the same pattern once they exist), the same "must stay unconditional, not gated to whichever window holds focus" treatment `UiInputController` already gives Tab (focus cycling) and reserves for Escape (see the Options menu item above).

Affected: `Presentation/Input/UiInputController.cs` (new hotkey handling), `Presentation/UI/Inventory/InventoryFolderController.cs` (the toggle method a hotkey would call, alongside the existing tile-click path).

#### Targeted key-press routing instead of a full-keyboard scan

`UiInputController.RouteKeyPressesToFocusedWindow` calls `KeyboardState.GetPressedKeys()` every frame a window is focused (effectively always) -- confirmed via reflection against the actual FNA assembly that this is the only overload (no non-allocating variant like MonoGame added), so it allocates a new array every frame for the life of the session.

`HandleKeyPress`/`OnKeyPressAction` (what this routes into) has exactly one real consumer today -- `TextBox.OnKeyPressAction`, which only cares about `Keys.Back`; `IWindowContent.HandleKeyPress` defaults to a no-op for everything else. Rather than scanning the whole keyboard (or, worse, manually diffing all ~130 `Keys` values via `IsKeyDown` every frame as a naive fix), let the currently-focused window's content declare the small set of keys it actually wants checked, and only call `IsKeyDown` for that declared set.

Not actually dependent on the Keybindings page item above -- `HandleKeyPress` (discrete edit-type keypresses, e.g. Backspace) and `HandleHotkeys` (continuous/combo game commands, what Keybindings remaps) are deliberately separate hooks. Sequenced here as a followup for proximity to the other keyboard-routing work, not a real ordering requirement.

Affected: `Presentation/Input/UiInputController.cs` (`RouteKeyPressesToFocusedWindow`), `Presentation/UI/IWindowContent.cs`/`Window.cs` (a new way for content to declare its interested keys), `Presentation/UI/TextBox.cs` (the one current consumer, declaring interest in `Keys.Back`).

#### Chat and speech

Glowing speech bubbles over NPCs, clickable to open a larger text window for the full line -- an ambient, per-NPC presentation of dialogue rather than a single shared log. Separately, a WoW/other-MMO-style chat menu as a configurable output sink for debug info, loot drops, combat/damage numbers, NPC chatter, etc., with default built-in tabs ("Loot", "Combat", "Local Chat", "Notifications") and user-configurable routing of message types to tabs. `NotificationCenter` (`Presentation/UI/Notifications/`) is the closest existing precedent (categorized, tabbed-ish notification delivery) but is popup/toast-shaped, not a persistent scrollback log -- this is a different, bigger widget.

#### Visual improvement pass

A dedicated pass over UI sizing, placement, and colors across the whole Presentation layer once more of the HUD has landed and stopped churning -- today's values (`HudMetrics`, per-content `Size`/color constants scattered across `Presentation/UI/Content/`) were each chosen locally, one element at a time, not against a single coherent visual system.

#### Investigate mask-based recoloring for shared sprites, using potions as the concrete case

Every distinct potion today needs its own fully-authored sprite even though most differ only by liquid color (Health/Mana/etc. potions share the same bottle silhouette) -- `SpriteManifest`/`SpriteSheetService` (`Game/Blueprints/SpriteManifest.cs`, `Presentation/Rendering/SpriteSheetService.cs`) have no concept of tinting a shared base sprite. Worth investigating a mask-based recolor approach: a separate grayscale/alpha mask region (or a dedicated mask channel) marking which pixels of a base sprite are recolorable, with `SpriteRenderer.Draw` (or a new draw variant) tinting just those pixels per-instance (e.g. a `Color` field on `SpriteComponent` or the item definition) instead of shipping a full duplicate sprite per color variant. Potions are the concrete first case, but the same mechanism would generalize to anything else that's "one silhouette, many color variants" (dyed equipment, faction-colored banners).

#### Investigate TextWindow draw cost (landed)

A live `latest.json` snapshot (via the new Diagnostics engine's per-window Draw timing, `Engine/Diagnostics/FrameBudgetTracker.cs`) showed `DynamicHudWindows.TextWindow` (`Presentation/UI/TextWindow.cs`) costing ~11.6ms/s -- 11.5% of total Draw budget, second only to `MapWindow` (~79.6ms/s, expected given it's the whole map viewport) and disproportionate for what should be simple text rendering.

Root cause wasn't the text rendering itself -- `TextWindow.DrawContent` is one or two cheap `SpriteBatch.DrawString` calls. Every `NotificationCenter` popup (`Presentation/UI/Notifications/NotificationCenter.cs`, `ShowActive`) was built with `CanUserScrollVertical = true` unconditionally, regardless of whether its content actually overflowed. That made `RequiresContentViewport` (`Element.cs`) true for every popup, which sends `Element.Draw` down a path that, per window per frame, does two full `SpriteBatch.End()`/`Begin()` cycles plus a `GraphicsDevice.Viewport` swap (to draw content in local coordinates) and -- unconditionally, even though `TextWindow` sets `_canContainChildren = false` -- a second `End()`/`Begin()` cycle plus a `GraphicsDevice.ScissorRectangle` swap to clip children that don't exist. Each `End()` flushes whatever's queued in the shared deferred `SpriteBatch` so far that frame (including `MapWindow`'s own tile batch), so the cost is GPU state churn and premature batch flushes, not glyph work. Confirmed empirically: idle baseline (0 active popups) measured ~0ms/s for `Draw.DynamicHud.TextWindow`; temporarily forcing 6 stacked notification popups open reproduced ~8-11ms/s, matching the original report.

Fixed two ways: (1) `Element.Draw`'s child-scissor pass now only runs when `_children.Count > 0` -- always wasted work for a leaf content window like `TextWindow` regardless of how many other windows force scrolling on. (2) `NotificationCenter.ShowActive` still builds with `CanUserScrollVertical = true` (needed as a WrapContent sizing input -- see `TextWindow.RecalculateWrapContentSize`), but turns it back off right after `Initialize()` if the computed `MaxScrollOffset` is zero, so the common short-notification case never pays `RequiresContentViewport`'s per-frame cost at all.

## Global

### High Priority

#### Pause modality

A `NotificationCategory.System` notification pauses the simulation (`NotificationCenter.HasBlockingNotification`, checked in `GameLoop.Update`), but doesn't actually block input to or dim whatever's behind it -- other windows (map, selection, debug) stay fully interactive underneath a "blocking" notification, which reads as a bug the first time someone notices it. Needs an actual modal concept: input to other windows either ignored or visually indicated as unavailable while a modal window is up.

Promoted to High: both the new equipment menu and the Options menu (see Presentation) explicitly need "pause game while open" behavior, and neither should re-solve modality on its own. Inventory management landed as a third OR-term in `GameLoop.Update` (`_shell.Inventory.IsAnyWindowOpen`, alongside `MapWindow.IsPaused`/`NotificationCenter.HasBlockingNotification`) the same minimally-invasive way -- generalizing this into a real modal concept still hasn't happened, it just has one more un-generalized consumer now.

#### Plan a refactor for long constructor parameter lists

Promoted to High from the old "Long parameter lists" note below (which had drifted stale -- it cited `Game/Modules/Abilities/`, since renamed to `Game/Modules/Actions/`). The convention is deliberate, not accidental: ECS systems and Presentation controllers take component pools as explicit, individually-typed constructor params (required vs. nullable-optional) instead of storing `ComponentManager` and resolving lazily, so a class's exact dependencies are visible and typed at its own call site, with each `*Module.RegisterSystems`/`GameShellBootstrapper` owning the actual resolve-plus-`IsRegistered`-guard logic. But it's grown long enough to be a real "long parameter list" smell by conventional standards: `ActionActivationSystem`/`DelayedActionSystem`/`ConsumableActivationSystem` (`Game/Modules/Actions/Systems/`, `Game/Modules/Inventory/Systems/`, ~15-16 params each), `ActionTargetingController`/`MapWindow` (`Presentation/UI/`, 14-15 params each), `StatusEffectAuraSystem` (`Game/Modules/StatusEffectAura/Systems/`, ~11).

This item is to *plan*, not execute, a refactor -- and the plan needs to resolve a real design fork before touching code: (1) group related pools into small parameter-object records per cluster (e.g. a targeting-pools bundle, an action-pending-components bundle), mirroring the shape `ActionTiming`/`TargetingSpec` already use internally for a handful of related fields; or (2) reconsider whether some of these classes should just take `ComponentManager` and resolve their own pools instead, giving up the call-site-visible-dependency benefit for a shorter signature. Whichever direction, it has to be applied consistently across every constructor above, not fixed one class at a time -- a half-migrated codebase with two different "how a system gets its pools" conventions would be worse than the current, at least-consistent state.

#### Clean up unit tests by removing or fixing fragile tests

`dotnet test Tests/Tests.csproj` currently has 15 pre-existing failures unrelated to whatever feature happens to be in flight -- confirmed unrelated to the Inspection V2 work (`Presentation/UI/InspectionWindow.cs` et al.) since none of the failing tests touch anything that work changed. Two clusters stand out: a `Tests/Presentation/MapWindowTests.cs` group (`SelectMapNodes_ClickOnMap_SetsSelection`, `RightMouseDrag_DecouplesCameraFromPlayer_UntilHomeRecouples`, `UpdateZoomLevel_RecalculatesMaxScrollAndReclampsCurrentPosition`, several `OnRightDragAction`/`OnRightDragEndAction`/`HandleHotkeys` cases) all failing on camera pixel-to-tile math, suggesting a shared fixture assumption (e.g. a hardcoded "Team zoom = 18px tiles" comment) has drifted out of sync with an actual default changed elsewhere; and a scattered set with no obvious shared cause (`Tests/Collections/FreeIdPoolTests.cs`'s `Release_Twice_ThrowsOnSecondRelease`/`Release_NotIssued_Throws`, an `EntityManager` `DestroyEntity_NotAlive_Throws`, `Tests/Diagnostics/PlayerActivityLogTests.cs`'s two `EntityDamaged_*_LogsNameAlongsideEntityId` cases, and `Tests/Blueprints/BlueprintTests.cs`'s `Fairy_Build_SetsRaceHealthMovementActionLockAndTransform` asserting a stale punch-damage constant).

A fragile/failing suite is worse than no suite -- it trains whoever's working nearby to ignore red output, and each of these should either get fixed (if it's catching a real, still-relevant regression) or removed (if it's testing a since-changed assumption that's no longer meaningful). Needs someone to actually run down each failure's root cause individually rather than a blanket fix -- the MapWindowTests cluster in particular looks like one shared root cause, not five independent bugs.

#### Data storage, starting with window locations and sizes

Promoted from Low: the new `WindowCascadePlacement`/`ScreenBoundsClamp`/`PopupPositioning.GetPositionWithinBounds` auto-placement (`Presentation/UI/`) is a simple always-cascade-and-clamp default precisely *because* real per-window saved positions don't exist yet -- once they do, a manually-dragged window's saved position must always win over that auto-placement, or the two would fight each other. Keyed per logical window/slot, not globally: e.g. if a player drags 3 open Item Details Comparison columns to custom spots, those same 3 slots should restore to those exact positions next time, while a 4th column beyond what's been customized still falls back to `WindowCascadePlacement`. This is the concrete motivating use case for doing this now, ahead of the more general "eventually needs the same serialize-to-disk mechanism" framing below.

No serialization/save-and-load system exists anywhere yet. Window layout (`WindowRelativePosition`/`WindowCurrentSize`/`WindowDisplay` -- see `Window.cs`) is the first concrete use case: every launch starts from whatever `GameShellBootstrapper` hardcodes, with no way to remember where the player last left the map/debug/selection windows or which were minimized.

Worth treating as the first slice of a general data-storage system (entity/world save state will eventually need the same serialize-to-disk mechanism -- including, eventually, inventory/equipment/stats state from the new Engine/Game items above) rather than a one-off "just persist these three floats" hack -- but start narrow. Window geometry is small, self-contained, and has no cross-entity references to untangle, which makes it a good first slice specifically *because* it won't force premature decisions about how the general system should handle things like entity references that a save format will eventually need to solve.

**Modded content must degrade gracefully, not corrupt a save.** Once entity/world save state (inventory items, granted abilities, and `IActionActivator`/`ActionEffect`-bearing catalog entries -- see `PLAN-action-effect-activator.md`) starts getting serialized, a saved reference (by `Guid`) to a mod-defined item/ability/effect can go stale if that mod is updated, disabled, or removed before the save is loaded again -- a well-known failure mode in every moddable game with real save compatibility (RimWorld, Path of Exile). Worth a fail hierarchy decided up front rather than crashing or silently corrupting state on a missing id: (1) prefer a mod-supplied replacement/migration for a renamed or updated id, (2) fall back to dropping just the affected reference (the one item stack, granted ability, or effect entry) while the rest of the save loads normally, (3) as a last resort, when the missing content is load-bearing for the entity itself, drop the whole entity. Consider letting a mod register its own fallback id (a vanilla or generic substitute) per content id it defines, so an update or removal degrades a save gracefully instead of just vanishing content outright.

### Low Priority

#### Debug/event logging with levels

`Game/Diagnostics/PlayerActivityLog.cs` (added alongside the Burning status effect) is a
narrow, single-purpose EventBus subscriber that writes the player's Burning damage and moves
straight to a file -- deliberately minimal, not a general logging facility. Worth a real
design pass once more than one thing wants to log: an actual log-level concept (e.g.
Debug/Info/Warn/Error, or a verbosity toggle), a general "subscribe any event type to a log
line" mechanism instead of one hardcoded handler per event, configurable sinks (file/console),
and eventually other entities/event types, not just the player's moves and damage.


See the Entity storage item below for a narrower, related need: suspending an entity from active simulation (and possibly restoring it in a later session) specifically, as opposed to this item's general "serialize anything to disk" scope.

#### Entity storage -- suspending an entity from system processing without per-system checks

`World.RemoveEntityFromMap` (`Game/World/World.cs`) already carries an inline TODO flagging exactly this gap: it unregisters an entity from Map's spatial index but zeroes `TransformComponent.Position` to `(0,0,0)` in the process, losing where the entity was -- its own comment names "persistent entity storage" as the intended fix. No production code calls it yet (only `WorldTests`) -- it exists today as a designed-but-unwired "full despawn" primitive, distinct from `DeathSystem`'s `ConvertToNonBlocking` (a corpse stays fully simulated, just non-Blocking, not removed from anything).

The actual gap is broader than Map's own index, though: taking an entity out of *every* system's processing -- not just map occupancy -- without each system needing its own "is this entity in storage" check. `ComponentManager.RemoveAllComponents(entityId)` already does the mechanical half (removes entityId from every registered pool, which is what actually keeps a system's `EntityStripeSet`/`TieredEntityStripeSet` from ever visiting it again, since pool membership is what drives those buckets) -- but it doesn't preserve what was removed anywhere, so today it's only usable for a real despawn (data gone for good), not a "put in storage, restore later" one.

Needs: a way to snapshot an entity's full component set (across every registered pool) into a serializable form, remove it from every pool the same way `RemoveAllComponents` already does, and later rehydrate it back into exactly the pools/values it had -- restoring the entity's actual accumulated state, not just re-creating a same-shaped one from a blueprint. Two open design questions, deliberately left unanswered here:

1. Does this ride on the general save/load system (see Data storage above -- entity/world save state was already anticipated as needing "the same serialize-to-disk mechanism"), or is it a narrower, same-session "freeze/thaw" mechanism first, with save-to-disk layered on top later -- mirroring how Data storage itself deliberately started narrow with window geometry before generalizing.
2. How do cross-entity references (an equipped item's owner, a pet's bonded player, an aura source's target) stay valid across a storage/restore cycle -- the same "a reference can go stale" problem Data storage's own "Modded content must degrade gracefully" section already raises for saves generally, just triggered by an entity leaving/re-entering the simulation instead of a mod changing underneath it.

Motivating use case: persistent characters (a tamed companion, a stored/caged NPC) that survive being taken out of the current session's active simulation and can come back -- same session, or a future session via save file -- with their exact accumulated state (stats, inventory, granted abilities) intact, not reset to a fresh blueprint instance.

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
