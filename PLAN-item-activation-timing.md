# Activation parity: actions and items share one pipeline

Replaces the TODO.md entry "ItemActivationSystem.Update: one method per activator?" (removed when Phase 1 was built).

**Rule for this plan and after it:** actions and items differ only where a difference is decided and written down
(the "Intentional divergences" list below, then IMPLEMENTATION-NOTES). Every change to one is checked against the other.
Where both do the same thing, they do it through the same code.

## Decisions (settled 2026-11-10)
- **Every item activator honours its whole `ActionTiming`**: Category, ActionLockFrames, CooldownFrames, ReleasesActionLock.
  Nothing is hard-coded per activator. Examples: a long scroll Delayed, a standard scroll Immediate, an emergency glyph FreeCast.
  Existing content keeps its behaviour: every master potion, scroll and wand is `"tm":{}` (Immediate), tomes are Immediate in code, and the Toxic Idol is Delayed already.
- **Turning a toggle off follows the actions' rule** for both: it takes no activation effects, never winds up, and is never refused by a blocker.
  It waits for the action lock and sets it unless the timing is FreeCast.
- **Dropping an item stays lock-free** (`ItemDrops`). Actions have no counterpart.
- **Cooldowns follow the actions' rule** for both:
  - A cooldown is checked before anything else, so a cooling toggle can't be turned off either.
  - Every use that goes ahead starts it, turning off included; a Delayed use's starts at windup start. This prevents toggle spam.
  - Toggle items with cooldowns are created with care.
- **What holds a cooldown differs (intentional):**
  - An action's belongs to its user and the action, because it is the user recovering.
  - An item's belongs to the unit, because it is the item's own rate limit (a weapon overheating).
  - A potion is limited only by how fast its user can drink: the action lock.
- **A Delayed use is spent when its windup starts:** the activation effects, plus an item's unit or wand charge. Nothing is refunded on cancel.
- **A windup captures its definition at windup start, for actions and items alike**, and resolves with that.
  An override applied mid-windup affects the next use, never one already wound up.
- **One effect resolver for actions and items**: path hits first, the simulated/frozen seam, Dodge (`TraitDodgeable`), Stagger (`TraitStaggering`), and the marked entity's once-placed entries.
  - Dodge and Stagger stay tag-driven, so an item is dodged or staggers exactly when its tags say so, as an action is.
  - No master item carries either tag today, so nothing changes until content tags one.
- **One activation event**: `ActivationUsedEvent(EntityId, ActivatableKind, DefinitionId, Tags)` replaces `ActionActivatedEvent`. It is published at the same moments for both: when the effects land, and when a toggle flips.
  - Readers filter on what they mean: `PlayerCommands`' Dodge step reads the Dodge action's id, and Spell Caster reads `Spell` in Tags (see Settled).
  - Shared, because "something was used" is one fact. A reader that wants only actions says so, rather than a second event existing that each new reader must remember to subscribe to.

## Intentional divergences (after this plan)
- What holds a cooldown: user + action vs the unit.
- What a use spends: actions only their activation effects; items also a unit or a wand charge.
- Hotkey slots follow a unit that moves stacks: items only, since only items move.
- Dropping: items only, always lock-free.
- Ground-mode toggles: an action anchors its aura on the holder's tile; an item is put down as a one-unit Drop, and the pile owes its upkeep.
- Kind-specific rules: potions' cooldown-abuse Poison (on whoever receives it); scrolls' Intelligence scaling (range, area size, durations) and mastery. These run as per-target and per-use hooks around the shared resolver, where an action kind's own rules (spell scaling, later) plug in the same way.
- Toggles at the holder's death: an action's always end; an item's only with periodic effects.
- Firing a wand isn't casting (`Magic`, not `Spell`).
- Step on activation: actions only, until an item needs it.
- A Delayed toggle item whose stack left the holder mid-windup lights nothing, while a spent unit's windup still lands (it was already used).
- A potion counts only a target with health (Simple or Complex), dead or alive. Everything else follows the shared rule.

## Settled 2026-11-10 (were open)
- **Dead targets are reached by actions and items alike** (the actions' rule).
  - This leaves room for destroying corpses, resurrection spells and the like.
  - Items' dead-target skips go away (`ApplyPotionToTarget`, `ApplyScrollToTarget`, `ApplyToLivingTarget`).
  - A potion still counts only a target with health, so a corpse with health takes the potion, its abuse check and its cooldown reset.
- **Reading a scroll counts as casting its spell.** "Casts a spell" applies to an action and to an item, so it becomes one top-level tag, `Spell`, replacing `Action.Spell` ("combine, don't nest").
  - `SpellActivator` and `ScrollActivator` both imply it.
  - Its readers switch to it:
    - Spell Caster reads `ActivationUsedEvent.Tags`.
    - `SourceCause` keeps naming an item an Item first.
    - `ActivationMenuLabels`' "Cast" becomes `tags.Has(Spell)`, dropping its scroll special case.
  - No content names `Action.Spell` today, and tag ids are never persisted.
  - This lands in Phase 2 with the event.

## Settled in review (2026-11-10)
- **A. Toggles at the holder's death -- intentional.**
  - An action toggle always ends: it is the creature's doing.
  - An item toggle ends only when it has periodic effects (`ToggleItemHolderSync.EndsWhenHolderDies`): a lit lantern on a corpse keeps glowing.
- **B. Refusing a use that would do nothing -- shared** (Phase 2).
  - Today only a `ConsumableActivator` reaching only its user is asked its effects first (`ActivationQueries.GetBlocker`), so a tome of a known spell is refused.
  - Proposal: any action or item whose targeting reaches only its user asks its effects first.
  - Only entries with an explicit refusal (`SpellAlreadyKnown`) refuse, so no current content changes behaviour (a heal at full health still goes ahead).
- **C. Disabled -- one status for actions and items** (Phase 5).
  - It replaces the unused per-stack `InventoryItemStackComponent.IsDisabled`.
  - Proof of concept: an Anti-Magic Shrine and an Anti-Literacy Shrine, forerunners of anti-magic and null zones.
- **D. Activation scaling -- per activation type; scrolls are the first.** A scroll scales range, area size and durations by its reader's Intelligence.
  - Later activation types (spells included) get scaling rules of their own.
  - Scaling is a per-kind hook, not an item-only one, so a spell action's rule plugs in the same way.
- **E. Wands don't cast -- intentional.** A wand implies `Magic` but not `Spell`, so firing one isn't casting a spell (Spell Caster, "Cast" label).
- **F. Step on activation -- intentional.** `PlayerCommands`' step after an activation (Dodge) is an action's buffered-command option only. An item that moves its user would add it then.

## Phase 1 -- item dispatch refactor, no behaviour change (built 2026-11-10)
- `Update` keeps the request lookup, the stack and definition lookup, `GetBlocker`, and a `switch` that calls one private method per activator (`UsePotion`, `UseScroll`, `UseWand`, `UseConsumable`, `UseToggleItem`).
- The dead stack lookup is gone: `TryBeginActivation` became `TryConsumeOneUnit` (lock check, then use up one unit).
- The wand's `charges > 0` check stays. A wand can be granted with 0 charges (`Wand_NoChargesRemaining_DoesNothing`), so an empty wand is inert. Only spending the last charge destroys one.
- `ToggleItemActions.TryLight` (light one unit, then put it down if in Ground mode) is shared by `UseToggleItem` and `ToggleItemWindupResolver`.
- No new allocations.

Proof: every existing test passes unchanged.

Test checklist:
- Drink a Healing Potion: health rises, one potion is gone, and the hotbar slot fills with the lock.
- Throw a potion at a goblin: the goblin's potion cooldown starts and yours doesn't.
- Fire a wand: charges go down by one, and the slot stays on that wand. At 0 charges it's gone.
- Light and put out the Toxic Idol in Target mode (winds up, then lit) and in Ground mode (put down on the tile).

## Phase 2 -- one activation pipeline (built 2026-11-10)
- **`Activations` (Game.Modules.Actions)**, the one sequence both systems run:
  1. Cooldown check.
  2. Blocker check. Anything whose targeting reaches only its user also asks its effects (B), for actions and items alike.
  3. Lock check (unless FreeCast).
  4. Activation effects (not when turning a toggle off).
  5. The timing step:
     - Immediate and FreeCast apply now. Immediate sets the lock.
     - Delayed spends, then `Windups.Begin` with the captured definition.
     - A toggle flips or winds up, under the rule above.
  6. Start the cooldown.
  7. `ReleasesActionLock`.
- **What each side supplies:** the definition, the cooldown store (`EntityActions` / the unit), what a use spends (nothing / a unit, a charge), what applying it does, and the toggle's owner reference.
  - It is a struct implementing `IActivationUse` (`ActionUse`, `ItemUse`), passed by `ref` to the generic `Activations.TryUse<TUse>`, so the sequence is specialized per kind and nothing allocates per use. Not `unmanaged`: each holds a reference to its system.
- **`PendingWindupComponent` holds the captured `ActivatableDefinition`** (a reference field, written through a write scope).
  - `DelayedActionSystem` resolves an action's with it instead of looking it up again.
  - `IItemWindupResolver` resolves an item's with it, so a spent last potion still lands.
  - Every windup reader reads the captured definition instead of looking it up through `ActivatableLookup`: the telegraph (`TargetingView.TryResolveWindup`), `TargetingView.IsWindupDodgeable` and `MapViewQuery`'s windup read.
    Without this, a spent last potion's windup draws no telegraph, and a wand whose charge moved it to a new stack telegraphs the old stack.
- **`ActivationUsedEvent`** replaces `ActionActivatedEvent` (its readers and tests move to it).
- Readers of "does this wait for the lock" read the same answer for both: `PlayerCommands.IgnoresActionLock`/`CanQueueItemActivation`, and the hotbar fill for items (no lock fill when FreeCast).
- **What changes for players:**
  - Delayed and FreeCast items work.
  - An item toggle with Immediate timing still waits for the lock when turned off, as it does today; it now also sets the lock when turned off.
  - An action override applied mid-windup no longer changes that windup.

Test checklist (temporary test items through Admin "Spawn here >", or a locally edited master record):
- Delayed potion: winds up with the charge-fill telegraph, the potion is gone at once, and it heals when the windup ends.
  Get staggered mid-windup: no heal, and the potion stays gone.
- Delayed scroll in Target mode on a moving goblin: it lands where the goblin is when the windup ends.
- FreeCast scroll during another action's lock: it fires at once, and the lock is unchanged.
- Turn off an Immediate toggle item and an Immediate toggle action: both wait for the lock and set it. FreeCast ones do neither.
- On a fresh save, reading a scroll unlocks Spell Caster. Casting a spell action still does too, and Dodge still steps the player.
- A scroll's menu option still reads "Cast".
- Every master item and action behaves as before, apart from the turn-off lock above.

As built, beyond the steps above:
- `ActivatableDefinition.ActivatorOrNone` names any definition's activator, so the windup readers need no lookup. `ActivatableLookup` is gone, and `TargetingView` and `MapViewQuery` no longer take catalogs.
- `ItemActivationSystem` is the item windup resolver (`ToggleItemWindupResolver` is gone). A non-toggle windup applies the captured item; a toggle windup still lights a unit only if the holder has it unlit.
- A dead entity's pending item request is dropped, as an action's always was (it used to be left on the corpse).
- New tests: FreeCast and Delayed potions, the item use event, a dead requester's request, putting an Immediate toggle item out under the lock, a mid-windup override, and a scroll unlocking Spell Caster.

## Phase 3 -- one effect resolver (built 2026-11-10)
- `ActionEffectResolver` becomes `ActivationEffectResolver` over `ActivatableDefinition` (one per-target walk, dodge, stagger, seam, marked-entity rule, once and per-tile entries).
  - Items' `CollectTargets`/`ApplyOnce` go away.
  - Kind hooks:
    - Potion: needs health, then the abuse check and cooldown reset per target reached.
    - Scroll: the duration scale on the context, and mastery after the use.
  - A hook is a struct, as in Phase 2.
- The shared resolver reaches dead targets for items too (see Settled).

As built:
- The hook is `IActivationTargetRule` (`Admits`, `BeforeEffects`, `AfterEffects`): `EveryTargetRule` for actions, wands, consumables and scrolls, and `PotionTargetRule` for potions. A target a rule doesn't admit counts as not reached, so it isn't staggered and, if it was the marked entity, gets only the AtLocation entries.
- Mastery and the scroll's duration scale stay in `ApplyItem`, around the walk, so they don't need a hook.
- The resolver publishes `ActivationUsedEvent` for both kinds (the caller passes the kind). Callers pass a reusable `HashSet` for reached entities; previously each multi-tile action use allocated one. The action-only `Apply` overload is kept for tests.
- `ItemActivationSystem` takes the Dodging pool and drops the fields it no longer reads (and its `Toggles` parameter).
- New tests: a Dodgeable potion is dodged with no cooldown and an untagged one lands, a Staggering potion staggers, and a potion thrown at a corpse with health lands and starts its cooldown.

Test checklist:
- Tag a temporary thrown potion `Trait.Dodgeable`. Throw it at a dodging goblin: "Dodged", no effect, no potion cooldown on it.
  Untagged: it lands.
- A wand bolt tagged `Trait.Staggering` staggers its target's windup, as a staggering action does.
- A projectile wand stops on the first blocking entity and hits it once, even if the shape also covers it.
- Throw a Healing Potion at a goblin corpse: the potion is spent, and the corpse gets the potion cooldown (visible on inspection). The heal itself is a no-op.
- A scroll aimed at a corpse and a living goblin reaches both.
- Every master item lands on the same targets as before, plus any corpses in its shape.

## Phase 4 -- per-unit item cooldowns (built 2026-11-10)
- **State:** `InventoryItemStackComponent.CooldownReadyAtFrame` (`uint`, `FrameDeadline.Never` when none).
  - It is part of what makes units interchangeable (`StackTemplate`), so a cooling unit sits in its own stack, shared with units ending on the same frame.
  - It is runtime state, never in the `Override` definition.
- **Phase 2's cooldown store for items:**
  - Checking reads the stack's deadline.
  - Starting moves the one unit to a stack cooling until `now + CooldownFrames` (`InventoryActions.MoveOneUnitIntoCooldown`). It happens after what the use spends (a wand peels its charge first) and before its effects or flip, so a toggle flips, and a Delayed use winds up, the unit already cooling. Every unit move keeps the unit's cooldown (`MoveOneUnit`, transfers).
  - The cooldown move repoints with no pressed slot (`RepointAfterUnitMoved`), so a slot follows the unit only when the stack it was bound to is gone, and a slot on a stack of several keeps using the ready ones. A wand still moves its pressed slot to the peeled unit first (its charges make it its own), so a wand slot follows its wand into cooldown.
- **Ending re-merges:**
  - The stack pool becomes an `IKeyedScheduledTimer` (keyed by `StackInstanceId`). An `ItemCooldownSystem` drives a `MultiTimerWheel` and, at the deadline, moves the stack's units into the equivalent ready stack.
  - Slots on the merged-away stack are repointed.
  - Not tiered: a deadline has no per-frame cost. As with every timer wheel, an unsimulated holder's stack re-merges once the holder resumes.
- **Not ready, not blocked**, as for actions: an item form of `ActivationQueries.FramesUntilReady` (the unit's cooldown, then the lock unless FreeCast).
  Read through `ActionStateView`, the hotbar fill (cooldown fraction first, as for actions), and `PlayerCommands`: `CanQueueItemActivation`, plus `IsBufferedCommandReady`, which then asks the same "frames until ready" for items as for actions.
- **Content check:** a Potion, Scroll or Consumable declaring `CooldownFrames` fails the build, since the unit is destroyed on use and its cooldown could never be seen.
- **Item Details:** "Cooling down: Ns" on a cooling unit (`ActionStateView.ItemCooldownFramesRemaining`). **Inventory grid:** a cooling stack keeps a cell of its own, as a lit one does (`InventoryGridContent.KeepsItsOwnCell`), so the ready units beside it stay usable.

Test checklist (a temporary wand or toggle item with `"cd"` set):
- A wand with a 5 s cooldown, a stack of 1: the slot fills from the cooldown and refuses until it ends, then fires.
- Two lanterns-with-a-cooldown in one stack: the first press lights one and it splits off cooling, the slot stays on the ready one, and the second press lights the other at once.
  Once both cooldowns end, the two lit units are one stack again.
- A toggle with a cooldown: lighting starts it, putting it out is refused until it ends, and putting it out starts it again.
- A potion record with `"cd"` fails the build, naming file and line.

As built:
- `ItemUse` follows the used unit as it moves (spend, then cooldown), so the flip and the windup name the stack it is in by then. `IActivationUse.StartCooldown` now runs before a use's effects or flip, for actions too (no visible change for an action).
- A lit unit moving stacks (into or out of cooldown) is taken off and put back on by `ToggleItemHolderSync`, as any lit transfer is, so its upkeep interval restarts then: at most one late upkeep per cooldown.
- Readiness is one rule for both: `ActivationQueries.FramesUntilReady(entity, timing, cooldownFramesRemaining, ...)`; `InventoryQueries.FramesUntilReady` is the item form. `PlayerCommands` now refuses to buffer a stack the player doesn't hold, as it refuses an action the player doesn't have.
- New tests: a toggle item whose units cool on their own (each refused until its cooldown ends, merged back after), and the build refusing a potion with a cooldown.
- Review fixes: a windup naming a cooling stack follows it when `ItemCooldownSystem` merges it away, as hotkey slots do, so a Delayed toggle item whose cooldown ends before its windup still lights a unit. `IActivationUse.TurnOn`/`TurnOff` return nothing: the sequence has started the cooldown and taken the activation effects by then, and nothing checked earlier can make the flip fail.

## Phase 5 -- Disabled: one status for actions and items, and two null-zone shrines (built 2026-11-10)
Decided 2026-11-10:
- Disabled is a property of the user, matched by tags, so one rule covers an action and an item alike.
- Anti-Magic disables everything tagged `Spell` (spell actions and scrolls).
- Being disabled also ends what is already in progress.

- **The status:** `DisabledActivationComponent` (Actions, Multi pool on the user).
  - It holds a `GameplayTagQuery` (a shared instance on its definition), a deadline, and the source that granted it.
  - It is an `IKeyedScheduledTimer`, removed by a timer wheel at its deadline: something happens (the entry goes away), so it gets a wheel, not a reader comparison.
  - A grant from the same source refreshes its deadline instead of adding another entry.
- **The effect entry:** `ActivationDisableGrant(Query, DurationFrames, FixedDuration)`, Actions-owned, with a codec, schema and `ContentCodecTests` coverage. It is placed `OnEachTarget`.
  - Its duration goes through `EffectModifiers` like every duration, and it declares its modifier pair.
  - It works from an aura, a contact, an action or an item, which is what null zones, silencing spells and the like will reuse.
- **The blocker:** a new `ActivationBlocker.Disabled` with its own text (`ActivationBlockerText`: "Disabled").
  - `GetBlocker` checks it structurally, before costs: regaining mana doesn't help.
  - It never refuses turning a toggle off.
  - Players and NPCs both refuse through it, and the hotbar, inventory cell, menu options and tooltips show it as they show any blocker.
- **Ending what is in progress:** when a grant lands on an entity (and on each refresh, which is cheap):
  - It cancels a windup whose captured definition matches (`WindupCancel`).
  - It switches off each matching toggle through its owner (`IToggleOwner`), as a toggle whose upkeep fails is switched off. That is not a use: no lock, no cooldown, no activation effects.
  - A lit item lying in a pile inside the zone goes out too, since the pile is exposed like any entity.
- **Remove `InventoryItemStackComponent.IsDisabled`,** `InventoryActions.SetStackDisabled`, their tests, and the inventory grid's "hide disabled" filter.
  - The grid greys a cell from the stack's blocker instead, as the hotbar does.
  - `InventoryDisabledComponent` (a whole inventory's window refused) is a different thing and stays.
- **Auras (content, `Auras.jsonl`, `BuiltInAuraIds`):**
  - **Anti-Magic:** an `ActivationDisableGrant` of `Spell`, duration 1.5 s.
  - **Anti-Literacy:** an `ActivationDisableGrant` of `Item.Consumable.Scroll` or `Item.Consumable.Tome`, duration 1.5 s.
  - Both use `Flat` magnitude and falloff `None` (the same at every tile in reach), with a glow colour each.
  - An aura applies only on its once-a-second tick, so a zone takes hold within a second of entering and lets go up to 1.5 s after leaving.
    This is acceptable for a proof of concept; a null zone that must be exact would want its own field read at the blocker, and that is later work.
- **Blueprints** (`Game/Blueprints/Objects`, registered in `BlueprintsModule`, modelled on `HealingShrine`): `AntiMagicShrine` (aura size 4) and `AntiLiteracyShrine` (aura size 3).
  - Same durability and immunities as the Healing Shrine.
  - Each has its own glyph colour, and sprite entries `Shrine-AntiMagic` and `Shrine-AntiLiteracy` as tinted manifest entries (a missing one draws its glyph).
- **Placement:** fixed placements in `Areas/starting-area.json`.
  - `anti-magic-shrine` in the left hallway at (6, 13): radius 4 reaches the junction's edge at x = 10.
  - `anti-literacy-shrine` in the upper hallway at (13, 7): radius 3 reaches y = 10, clear of the Goblin Foreman at (13, 5).
  - Neither reaches the player's spawn at (17, 13).
- **Parity:** nothing here is item-only or action-only. If something turns out to be, it goes on the divergence list.

Test checklist:
- Walk into the left hallway: within a second, spell actions on the hotbar show Disabled and refuse, and scrolls in the inventory grey out. Potions and wands still work.
  Walk out: they come back within about 1.5 s.
- Start a Delayed spell windup, then (Admin Mode, "Spawn here >") spawn an Anti-Magic Shrine beside you before it ends: within a second the zone reaches you and the windup is cancelled. (Moving into the zone mid-windup isn't possible: movement waits for a windup, and Dodge cancels one.)
- Turn on a Spell toggle, then walk in: it switches off, with no lock and no cooldown.
- Walk into the upper hallway: scrolls and tomes are disabled, while spell actions still cast.
- A Fairy led into the Anti-Magic zone stops casting.
- Inspect a tile in each zone: the aura's name and description show.
- Destroy a shrine: its zone ends.

As built:
- `ActivationDisableGrant` is a general entry (`Game.Effects.Entries`), its codec Core's (`EffectEntryCodecs`) rather than Actions': every build reads the aura table, including builds without Actions. Its query is written as three tag lists (`all`, `any`, `none`). The rules pool is on `EffectServices.DisabledActivations`.
- `DisabledActivationSystem` (Actions) is both the rules' timer wheel and the observer that ends what a rule disables. A cancelled windup keeps its lock, as a Stagger does; a switched-off toggle goes through `Toggles.SwitchOff` (new `Toggles.TryResolveDefinition`).
- The inventory grid greys a player's cell from `ActionStateView.GetItemBlocker` every frame (`InventoryItemStackCell.IsBlocked`), so a cost or the Disabled status shows with no inventory change. The "Hide Disabled" tab toggle is gone with the flag.
- The three shrines share their durability and immunities (`ShrineDurability`, also the Healing Shrine's build now).
- New tests: a spell refused as Disabled before its cost while another action isn't; a rule cancelling a matching windup, switching off a lit toggle, refusing new uses and lifting when it expires; the starting area spawning both shrines. Disabled-stack tests became cooling-stack ones (same identity rules).
- Review fixes:
  - `ActivationDisableGrant.FixedDuration` (`"fd"`): the duration is used as written, scaled by nothing. Both null zones set it, since a debuff-duration modifier of a third or more would otherwise end each grant before the next aura tick and let spells through. Any other use still scales as a debuff.
  - A record naming no tag in `all`, `any` or `none` fails: an empty query matches every action and item.
  - A rule's key is the one after the highest its entity holds (no process-wide counter).
  - The grid records each cell's items as it builds the cell and asks `ActionStateView.GetItemBlocker(entity, item)` of those, so the per-frame check finds no stack. A Merged Stack greys when every stack in it is blocked; an item with no activator is never greyed.
