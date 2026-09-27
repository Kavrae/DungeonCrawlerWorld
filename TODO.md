### Floating Combat Text
Combat text whenever an entity is damaged, healed, or given a status effect.
Research industry standard
Appear above the entity at 50% opacity, move up the distance of a single tile fading to 100% opacity, then move while fading to 0 opacity. Damage and status effect text moves up while "Dodge" and "Immune" text moves down. Remove the text at 0 opacity.
Text should start with a randomized horizontal offset (half of a tile width in either direction) to improve readability by reducing stacking.
Text should move diagonally, rather than directly up/down, to futher improve readability. Randomly chosen between negative vertical speed and positive vertical speed.
Start with 2 second duration.
Draw order is oldest (bottom) to newest (top)
Bordered text.
Red numbers for direct damage
Orange numbers for status effect damage
Green numbers for direct healing
Light green for health regen
Bolt for critical hits
Sprite + number of stacks when status effect stacks are added
"Dodge" whenever an action fails due to a dodgeComponent.
"Immune" whenever an action fails due to immunity.
Since multiple sources of damage and effects can happen simultaneously, which would be difficult to read, buffer them into a damage list and a status effect list. Each frame, display the oldest damage/healing number (drawn on left) and oldest status effect(drawn on right) in the list . This will result in a "waterfall" of text.
Should this feature go through the eventBus (for entities on the screen) rather than directly passed to whatever controls the floating combat text?
The text creation, movement, fade in/out, and removal combination of mechanics is similar to many game particle effects. Should this be generalized into a particle effect with text as the UI piece of it?

### Combat Overhaul : Dodge

**Core mechanic landed** -- see `IMPLEMENTATION-NOTES.md`'s own "Combat Overhaul: Dodge" section for
what's built. Still open below: Block, Counterspell, the AdvancedDodge buff (Low Priority, this file),
and independently validating the 0.5s-1.0s window against Dark Souls 3/other games' dodge timings.

# Clean these up
Keep Movement, QuickAttack, and magic missle as immediate actions, Dodge as the only FreeCast action, and change all others to delayed. Immediate and FreeCast actions should be rare. 
All delayed actions keep the target shape drawn on the map (so enemies show their attack target area) until they activate. Adjust the colors to make it clear which are enemy vs player. Red for enemy target shapes that cannot be dodged and yellow for ones that can be dodged. Light green for player arm and dark green for player target.
While an entity is charging an action/item, put that action/item's sprite as a badge above their sprite on the map.
This allows FreeCast actions like dodge, block, parry, counterspell, etc to have a purpose and timing.
This makes combat slower and more deliberate instead of spamming actions. Shifting to more of a 2d souls-like game
Lower enemy count to make this more deliberate and punishing combat style work.
Give every entity three default core actions. QuickAttack, PowerAttack, and Dodge.
	QuickAttack is an immediate adjacent-target action (so can't be dodged) with low damage. No cooldown besides global. This replaces Punch, defaulting to the R key.
	PowerAttack is a delayed adjacent-target action with a 1 second delay (so can be dodged) with high damage. No cooldown besides global. Default to the Q key.
	Dodge is a FreeCast action to avoid attacks. Its own flat 4 second cooldown (not an increased global cooldown) prevents Dodge from being used as a safer form of movement. Default to the F key. Not all attacks can be dodged; such as auto-targeting attacks like Magic Missile or AOE attacks like explosions. Mark PowerAttack as CanBeDodged (find a better name for this). After dodge is armed (adjacent+self), it can be activated with the same key, click, or directional movement key. Same key or clicking on the player will dodge in-place. Directional movement or clicking an adjacent tile will dodge while moving to that tile (if not occupied. If occupied, the entity will dodge in place). After activating Dodge, any action or item with CanBeDodged will not affect the dodging entity.
Dexterity increases dodge's activation time from 0.5 seconds at dexterity 1 to 1 second at dexterity 300. Check the values used by games like Dark Souls 3, and other games with a dodge mechanic, to validate these activation times.
Block (such as via a shield) will be added later as another FreeCast action with a longer duration but does not fully block damage and effects.
Counterspell will be added later as another FreeCast action that will attempt to cancel delayedAction spells. Determine a way to make this fair for both the source and target.

### Stances and Toggles
Stances: a set of toggle actions that boost one specialty while weakening another. 
Add a visual element to toggle actions/items to indicate when they're toggled on. Compare a rotating inner-fade glow to industry standard.
Stance 1 = Power Stance = Lower charge up times for delay actions in exchange for longer global cooldowns.
Stance 2 = Mage Stance = Improved magic effects at the cost of melee. 
Should toggle be a separate activator type or a different part of an action? Items (torch), spells (buff aura), and direct actions (stances) can all have toggles with various effects that can be manually activated and deactivated by the owning entity.

# Long-Term TODOs

Non-urgent architectural items worth revisiting later. Organized by layer (Engine, Game, Presentation,
Global), each split High/Medium/Low priority. Landed work lives in `IMPLEMENTATION-NOTES.md`, not here
-- cross-referenced by name where a still-open item depends on it.

## Engine

### High Priority

#### Class exceptions: runtime class grants

NPC classes come from spawn rules and fit two fixed class slots. The player's don't: a first class on
floor 3 and a derivative subclass on floor 6, with exceptions --
- *Four Seasons* (Ice/Fire/Earth/Wind Mage): the floor-6 choice may be another element instead of a
  derivative, a third element on floor 9, the fourth automatically on floor 12. Combination spells
  depend on the **set** of classes held at the time, not the order.
- *Former Child Actress*: no permanent subclass on floor 6; a temporary subclass every floor. At the
  end of the floor a seeded roll keeps some of its effects permanently and removes the rest.
- *Oak Fell* (and similar): granted by an achievement, alongside or instead of a lootbox, in addition
  to existing classes.

`ClassSlotsComponent` (2 x `ushort`, permanent) and the sparse `ClassMembershipComponent` Multi pool
(class id, lifetime -- permanent or expires at the end of floor N -- source kind, acquisition order),
read only through `ClassQueries`, are in place and unit-tested. `EntityFactory.Apply(entity, classBlueprint,
grantedBy)` is the runtime grant path: it builds the class blueprint onto a live entity, fills a
slot or adds a membership (`ClassEffects.Grant`) and skips a class already held; the class's actions
then resolve through `EntityActions` and its name follows the entity's (`EntityNaming`). Admin Mode's
"Apply >" menu drives it for testing. Nothing in gameplay calls it yet. Class
content is either **derived** from current membership (shared, never stored per entity; removing a
membership removes it) or an **explicit** per-instance grant carrying a class-grant `ActionSource` and
a lifetime.

What is left, once something grants a class at runtime: the class-grant `ActionSource` kind, the
advancement-rule hook on class definitions, and a combination-rule registry mapping a required class
**set** to derived grants, evaluated on membership change. Former Child Actress's floor end: roll each
effect, turn survivors into explicit permanent grants whose source names the residue, remove the
membership. Dependencies, out of scope here: a floor-end event (see EndOfLevelStairs),
class-selection UI, the advancement rules themselves, the residue roll. Ends with an in-game test and
a `phase-performance-testing` A/B.

### Medium Priority

#### FrameEventBuffer double-buffering + event system cleanup

`FrameEventBuffer<T>` (`Engine/ECS/Systems/`) throws on a second same-cycle `Record` (a safety net, not
a fix). Real fix: double-buffer (swap, not clear) like Bevy's `Events<T>` -- trades same-cycle
visibility for 1-frame latency (check this is OK for `MovementSystem`'s `ContactDamageSystem`/
`StatusEffectAuraSystem` consumers). Do alongside reviewing `EventBus`'s `IBufferedEvent`/
`SubscribeOnce`/`DispatchBuffered` (`Engine/Events/`) for consistency -- a different mechanism (deferred
re-entrant handler vs. high-frequency batching), never compared side by side.

#### Fix module dependencies

`IModule.Dependencies` is a list of `Type`s, but `ModuleSet.Combine` replaces a built-in by `Guid Id`.
A mod that replaces a module (e.g. `Mods.TestFixtures.ReplacementHealthModule`) makes any hard
`typeof(...)` dependency on it fail `Bootstrapper`'s topo-sort, even though the replacement provides the
same components. Because of that, most modules skip real dependencies and use soft `IsRegistered`-guarded
pools instead, each explained in its own comment (`BodyPartEffectsModule`, `HealthModule`, `DeathModule`,
`ActionsModule`, `MovementModule`). `Dependencies` also decides system run order as well as what a module
requires. `ProcessingTierModule` → `MovementModule` exists only so `ProcessingTierSystem` runs after
`MovementSystem`, and the `MovementModule` ↔ `StatusEffectAuraModule` pair can't declare both directions
without a cycle. Fix: resolve dependencies by `Id` (or by provided capability/component) so replacements
satisfy them, and separate "requires" from "runs after" so ordering constraints don't have to be
expressed as module requirements. Then turn the soft dependencies that are really hard back into
declared ones.

#### Investigate moving runtime pool checks to hard dependencies

Follows "Fix module dependencies" above. Game code checks for another module's pool at runtime in about
80 places: `componentManager.IsRegistered<T>()` (~49, across 20 files in `Game/Modules`,
`Game/Bootstrap/GameBootstrapper.cs` and `Game/World/ActionSource.cs`) and
`GetOptionalDirectPool/PackedPool/MultiPool` (~32, `ComponentManagerOptionalPoolExtensions`). Every
optional pool leaves a null path in the code that uses it. Go through each check and sort it:
- **Actually optional:** the module works correctly without the other one (e.g. `HealthModule`
  treating missing `StatModifierComponent` as no modifiers). Keep the check.
- **Only avoiding the replacement or cycle problem:** once dependencies resolve by `Id` or capability,
  declare it in `Dependencies`, fetch the pool normally, and delete the null handling.
- **Only there so minimal test module sets build:** decide whether those tests should include the
  dependency instead. Per the rule that tests don't drive design, a check that exists only for tests
  should become a hard dependency.
Write down the result for each check before changing anything. Some soft checks exist on purpose to
break cycles (`MovementModule`/`DeathModule` → `StatusEffectAuraSourceComponent`) and may have to stay
soft even after the fix.

### Low Priority

#### Partial module replacement

A mod that replaces a built-in module by `Id` currently replaces all of it and must register every
component the built-in did (the replacement contract, see `PLAN-module-dependencies.md`). Let a mod
replace only the systems, only the components, or both. Whatever it doesn't replace comes from the
original module. For example, a mod could swap `SimpleHealthRegenSystem` for its own regen rule and
keep `HealthModule`'s pools and every other system. Open questions: how finely systems can be
replaced (per system or the whole set), how a replaced system keeps its place in the run order, and
how a component replacement with a different merge action is checked against what depends on it.

#### Equipment (Engine)

Slot/equip-unequip mechanics -- move an `InventoryItemStackComponent` stack into a slot, no new storage
primitive. Companion to the Game/Presentation equipment items below.

## Game

### High Priority

#### Merging body plans when an entity gains a second race

Bug: `EntityBodyParts.Update` seeds a body plan (`CreateState`) only when the entity has no
`BodyPartStateComponent` yet. Once one exists it bounds-checks `partId` against the fixed
`BodyPartStateComponent.MaximumParts` (16), not the entity's current `Count`. If a damaged entity
then gains a race (`EntityFactory.Apply`, e.g. Admin Mode "Apply > Human"; `RaceSlotsComponent`
merges it into the next empty slot), the new race's parts are never seeded: they read as 0 health,
and Damage/Heal writes to them go through without any error. Two 11-part races also give 22 parts,
more than `MaximumParts` allows, so `CreateState` throws for an undamaged hybrid and any damaged
entity's parts past index 16 can't be written at all.

A bounds-check fix alone isn't enough. The real gap is that a second race's body plan is just
concatenated onto the first (`TemplatesOf` returns first then second, and a part's id is its index
in that list). Plan the merge before fixing it:

- **Overlapping plans.** Human + Goblin both have a Head, Torso, Arms, Legs and so on. Decide whether
  a hybrid gets one of each overlapping part (whose template wins -- the first race, like appearance?
  the larger maximum health?) or both. A second Head or a third and fourth Arm has to mean something
  to `BodyPartEffectsSystem`'s movement/melee penalties and to whether an entity dies from a vital
  part.
- **Different plans.** Parts only one race has (wings, a tail) get added. Whatever the final count
  is, it must fit `MaximumParts`, or `MaximumParts` needs revisiting against the component size
  (see "Component size audit").
- **Position.** `VerticalPosition` only means something relative to the same race's other parts.
  Concatenating two races mixes two scales, so `BodyPartSelection.PickTopmost/PickBottommost` (and
  anything else using `BodyPartTargetRule`) can pick the wrong part. The merge has to produce one
  consistent ordering, e.g. positions normalized per race, or merged parts placed relative to the
  part they attach to.
- **Part ids.** Ids are indices stored in `BodyPartBurningTimerComponent` and anything else that
  names a part. Gaining a race on a live entity must not shift an existing part's id, or must remap
  every holder.
- **Live state.** Gaining a race while the entity has a state component must seed the added parts at
  full health and keep existing parts' damage, disabled flags and regen lockouts. Losing a race (if
  that ever becomes possible) needs the reverse.
- The merged plan belongs in `BlueprintRegistry.Resolve` / `ResolvedBlueprint` (cached, spawn record +
  applied list), not re-derived per read in `EntityBodyParts.TemplatesOf`.

Cover it with a test: damage a Goblin, apply Human, then check every part's health, the part count,
the topmost/bottommost picks, and that a burning part keeps its id.

#### Real map generation -- neighborhood templates, replacing TestMapBuilder

The sliding window of 1024x1024 neighborhoods is in place: `NeighborhoodStreamer` generates each neighborhood on demand from its own
`NeighborhoodRecord` seed through `TestMapBuilder.GenerateNeighborhood`, which is still a stand-in --
random terrain and flat per-tile population rolls, with the hallway cross, shops and spawn only in
neighborhood (0, 0). Replace it with a generator where each neighborhood is built from a random
combination of four templates:

- **Layout template:** walls, corridors, terrain palette, landmarks.
- **MOB population template:** races and classes, placement rules, and a per-neighborhood population
  budget (replacing the flat 3%/2%/2% rolls that make creature count scale with area).
- **Neighborhood interaction template:** what joins adjacent neighborhoods (aligned corridors,
  faction borders, shared features, raids). Decided per pair of neighbors. Runtime behavior is the
  low-priority "Neighborhood interactions -- Outbound and Inbound" below.
- **Quest line template:** quest hooks placed into the chosen layout and population, possibly
  spanning neighborhoods.

Neighborhood placement is **randomized**: a given kind of neighborhood won't always appear at the
same coordinates, so relationships between neighbors must be generated dynamically rather than read
from a fixed world layout. Population density is defined per population template (dense goblin
warrens, sparse ones elsewhere).

Starting points from the plan's research (section 6): keep a rolling plan one ring outside the loaded
window, and roll each neighborhood's record (template choices plus its own seed) against its
already-assigned neighbors when the window shifts. By the time a neighborhood's tiles are generated,
every edge it shares is decided (the rolling equivalent of CDDA's overmap or Qud's world map).
Interaction templates should be constraints on a single shared edge, since a record is rolled with
only some of its neighbors known. Generation reads only the record's own seed, never a shared
sequence, so contents don't depend on generation order or thread timing. Spelunky's role-based room
templates are a model for composing authored pieces randomly. The record, per-record seeding and
planning a neighborhood as data on a worker already exist (`NeighborhoodRecords`,
`TestMapBuilder.Plan` producing a `NeighborhoodPlan`, which the streamer applies under its per-frame
budget); the new generator produces the same plan. Subsumes the seed
plumbing in "Random map generation v1" below. Needs its own plan.

#### Simplify non-local combat for performance

A Delayed action's windup/telegraph/badge exist for player-visible tactical value -- an off-screen
entity's own windup buys it nothing (nothing is watching it) but still costs the same
`ActionLockComponent`/`PendingDelayedActionComponent`/`DelayedActionSystem` bookkeeping as an
on-screen one. Confirmed expensive at this game's real population scale: a live diagnostics capture
showed over 10,000 entities map-wide simultaneously mid-windup during ordinary NPC-vs-NPC combat
(`DelayedActionSystem` alone cost ~79ms of a 1000ms/sec budget before it was tiered -- see
`IMPLEMENTATION-NOTES.md`'s "Enemy Attack Indicator + follow-up combat/performance work" for that
fix, already landed; tiering cut the per-frame *visit* cost, this entry is about shrinking the
*population* that ever needs a windup in the first place).

For an entity outside Local processing tier (`ProcessingTierLevel.Local` -- `ActionTargetingController
.AllPendingDelayedActionTargets` already treats this as the visibility boundary for its own
telegraph), resolve its actions immediately instead of queuing a Delayed windup at all: skip
`ActionLockGate.Lock`/`PendingDelayedActionComponent` and call `ActionEffectResolver.Apply` right
away, the same as an `Immediate` action. Shrinks `DelayedActionSystem`'s/`ActionLockSystem`'s own
per-frame population directly, without touching on-screen combat's telegraph/timing at all. Open
question: check tier at decision time (`TestCombatBehaviorSystem.TryDecideMeleeAttack`, cheaper --
never queues a windup for an already-non-Local entity) vs. at activation time
(`ActionActivationSystem.TryActivateDelayed`, also covers an entity that starts Local and drifts out
mid-windup). Whether Dodge/Block eligibility should get the same non-Local-entities-skip-it
treatment is a related, separate question worth deciding alongside this.

#### Inventory management rules

Interaction/restriction rules: stacking beyond exact-`ItemDefinition` match, who can pick up what,
item-item interactions. Storage + exact-match merging already exist.

#### Consumable items

Needs the item-instance-divergence design (a consumable's remaining-uses count is per-slot state that
doesn't exist yet) plus an actual "use" action.

#### Torch reveal + light-weakness damage, Scroll Mastery power-scaling

- Scroll of Torch's `StatusEffectType.Light` grant is glow-only -- no `IStatusEffectAuraApplier`
  registered for `Light`. Needs a real applier (fog-of-war reveal, light-weakness damage). Also worth
  reconsidering once fog of war lands: today's grant is per-*entity*; a light source reads more
  naturally anchored to a *location* (see Torch V2 below).
- `ScrollMasteryEffects.MasteryThreshold` (flat 200) and a synthesized spell's placeholder `ManaCost: 0`
  should scale with the effect's power. Blocked on Action Effects gaining a power-scaling concept.

#### Torch V2 -- selectable attachment mode

`AuraSourceGrant` always targets `context.TargetEntityId`. Three modes worth making selectable:
- **Follow the caster** -- already works via a Self-shaped `TargetingSpec`, no code change.
- **Fixed at a location** -- needs a minimal stationary prop entity (Transform + AuraSourceComponent,
  no creature identity, cf. `Lava`), despawned on expiry.
- **Attach to a specific other entity** (companion/pet) -- not expressible today; needs a new
  `TargetShape` or an explicit target-override on the entry.

Design as one shared "attachment mode" concept reusable by any future aura-granting effect.

#### Experience module

XP for kills (`EntityDiedEvent`) and quests (blocked on quest completion existing as a mechanic).
Level-up grants stat boosts/abilities per class. Needs a current/next-threshold Experience component +
HUD bar (candidate for the shared tick-fraction HUD bar item under Presentation). Check whether its
level-up curve can share math with Skills/Spell leveling below rather than three independent copies.

#### Skills

Static bonuses via `StatModifierComponent` + new effects on top of an action -- the motivating case for
the DamageOverride generalization above. Level 0-15 normally, unlockable to 20, never decreases, up to
hundreds per player. Needs a `MultiComponentPool<SkillComponent>`-shaped store (per-entity/per-skill,
not single-instance). See Spell leveling below for the same shape at smaller scale -- share one leveling
primitive. Consumer: Player selection menu's skill-gated detail (Presentation, below).

#### Complex health for every race, or none -- decide before body parts spread further

Goblin and Human (so the player) grant `BodyPartComponent`; Fairy, Ghost and every non-creature
holder use `SimpleHealthComponent`. That split is the order the races happened to be converted in,
not a decision. Both extremes costed against Human's 11 parts (struct sizes from `Unsafe.SizeOf`,
per-system costs from a headless benchmark -- seed 1, frames 600-3600, `EcsContext.Update` at 1.489
ms/frame, 2026-09-17):

**Memory is the whole argument.** A part costs 48 B (32 B struct + 16 B of `MultiComponentPool` dense
side arrays) against `SimpleHealthComponent`'s 16 B (8 B + 8 B packed) -- **528 B vs 16 B per NPC,
33x** -- and it is paid for the whole loaded window (~660k race NPCs: 9 neighborhoods x ~73k, at 3%
ground / 2% underground / 2% flying of 1024²), not just the simulated part of it:

- All Simple: 660k instances, ~18 MB including `WorldSessionBootstrapper`'s 1.467x reserve.
- Today (~21% complex -- goblins are 49% of the ground roll only): 1.53M parts + 522k simple, ~116 MB.
- All Complex: **7.27M parts, ~520 MB** -- roughly +400 MB over today.

Two multipliers on top of that. Dense growth is linear (`_denseGrowthAmount` = 220k), so filling
7.27M takes ~33 `Array.Resize` passes -- ~5.7 GB copied, ending on a ~350 MB LOH array -- during
initial population, before the startup reserve exists to help. And `BodyPartComponent` holds a
`string Name`, so all ~520 MB is traced by every gen-2 GC, where the reference-free
`SimpleHealthComponent[]` is skipped outright. See "Component size audit" and "Gen-1 GC frames during
a window shift" below.

**CPU is the cheap half.** Only Local plus the centre neighborhood simulate
(`SimulatedTierCount = 2`), so the health systems see ~10.8k entity-visits/sec whatever the world
size. Measured per visit: `SimpleHealthRegenSystem` ~135 ns; `ComplexHealthRegenSystem` ~550 ns plus
`BodyPartEffectsSystem` ~400 ns, which has no Simple counterpart -- **~7x per visit**. Extrapolated
to the whole population: all Simple ~0.024 ms/frame, today 0.060, all Complex ~0.17, so +8% of the
simulation budget at the top end. What rides along with it is worth more than the regen systems
themselves: `TestCombatBehaviorSystem.TryDecideSelfHeal` and `MapViewQuery.GetHealthBarFraction` (per
visible entity, every Draw) turn `HealthQueries.TryGetTotals` into an 11-node chain walk;
`ComplexHealthRegenSystem.AccrueWhileFrozen` walks every part and sorts lockouts on tier promotion,
which lands on the window shift, already the spikiest frame; and a neighborhood load becomes 807k
`MultiComponentPool.Add` calls instead of 73k, inside `NeighborhoodStreamer`'s per-frame budget.

All of it scales linearly in part count -- 11 is Human's number, 6 parts halves everything above.

If every race should have body parts, these come first, then measure again:

1. Replace `BodyPartComponent.Name` with a `byte` id into a shared part-template table: 32 B -> 24 B,
   and the dense array becomes reference-free so gen-2 stops tracing 10M+ references.
2. Materialize parts lazily -- Simple until first damage, or on entry to a simulated tier -- so
   memory tracks the ~73k simulated instead of the 660k loaded.
3. Cache an entity's summed current/maximum so `HealthQueries.TryGetTotals` stops being a walk.

Not yet verified as an A/B: the figures above extrapolate one mixed-population benchmark rather than
two runs with every race converted each way. Do that A/B before committing to (2), which is the
expensive one to build.

### Medium Priority

#### Gen-1 GC frames during a window shift

While `NeighborhoodStreamer` populates newly loaded neighborhoods, gen-1 collections occasionally
produce frames of ~17-19 ms (headless Release, seed 12345, 3072², with the 1 ms timer the windowed
game has). Gen-1 frames during a shift run 12-17 ms, varying run to run; the over-budget ones show up
mainly in the first shift of a session, while the population grows to its settled size. Measured by
teleporting the player 70 tiles into the next column of neighborhoods at frames 700 and 1400 of a
headless run (3 loads, then 3 loads plus 3 evictions); see "World scaling" in `IMPLEMENTATION-NOTES.md`.

What's known:
- **Pause cost tracks what population keeps.** Before the 7d allocation cuts, each gen-1 pause was
  8-25 ms and promoted 10-16 MB, nearly everything population had allocated. Sharing wand loot
  definitions and race attack overrides, caching `AbilityScoreType` values and removing a capturing
  closure in `InventoryActions.AddItem` took steady-state population to 469 bytes allocated and ~340
  kept per entity, and shift 2 from 9 frames over budget to 0.
- **Remaining per-creature allocations** (sampled with a `GCAllocationTick` listener): occupant
  `List<int>`s in `MapNeighborhood` (one per occupied cell, new for every loaded neighborhood),
  `EntityStripeSet`/`TieredEntityStripeSet` dictionary growth, `NeighborhoodCells<int>` aura-grid
  dictionaries rehashing up to ~22 MB per neighborhood, and `TimerWheel` slot lists.
- **Per-frame gameplay garbage** in the same window, short-lived but part of every gen-0/gen-1:
  `ActionEffectContext`, `ManhattanCellVisitor` closures, strings, boxed enumerators.
- **Tried and worse:** Server GC, and a 256 MB gen-0 budget (55-120 ms pauses). Pre-sizing every
  stripe-set and aura-grid dictionary didn't change the spike frames.

Options to measure:
- Occupant lists: keep a single occupant inline per cell and spill to a list only for a second one, or
  recycle an unloaded neighborhood's `MapNeighborhood` (arrays and lists) for the next load.
- Stripe sets: an entity-indexed location array shared across a tiered set's tiers, instead of five
  dictionaries per set.
- Aura grids: pre-size a new neighborhood's dictionary from its loaded neighbors' counts, or a dense
  per-neighborhood array where a grid is dense.
- Remove the per-frame allocations above.

Acceptance: no frame over 16.67 ms during either shift of that teleport measurement, other than the
teleport frame itself.

#### Component size audit

Measured with `Unsafe.SizeOf`. Every loaded entity pays for these, and a neighborhood streams in about
73k creatures at a time, so bytes per instance are memory, cache misses and streaming cost at once.
Most of the original list landed with the deferred-build work (2026-09-23): `RaceComponent`/
`ClassComponent` became two-`ushort` slot components, `BodyPartComponent` became race templates plus a
per-entity state component, `ActionInstanceComponent` became per-definition grants, and
`InventoryItemStackComponent` went 56 -> 24 B (interned item id, counter stack id, counter acquisition
sequence) with `MovementComponent` 40 -> 32 B. What is left:

- **`InventoryItemStackComponent`, 24 B, still the largest pool at ~75 MB.** 8 of those bytes are the
  `ItemDefinition? Override` reference, set on a small minority of stacks and scanned by every gen-2
  GC. Moving it to a side table keyed by stack instance id would take the struct to 16 B and drop the
  reference, but that table has to be cleaned up when a stack is removed -- including when an entity
  is destroyed with its stacks still in it -- so it needs a real owner, not a static dictionary.
- **`PendingDelayedActionComponent`, 32 B with references.** A `Guid` action id where a catalog index
  would do, plus a target-tile array per pending action.

Components holding references are scanned by every gen-2 GC as well as being larger, so moving
strings and overrides out is worth more than the byte count alone suggests. Measure pool memory
before and after (the diagnostics memory report lists each pool) and fix the largest total first.

#### Quest NPC simulation tickets

No quest system exists yet beyond the TEMPORARY quest composer demo and the "quest line template"
in "Real map generation" above. Note this before quests land:
Borough is frozen, so a quest NPC that walks out of the middle neighborhood freezes mid-quest. A
courier stops, a fleeing target stands still, a rival party never arrives.

Minecraft's precedent is **chunk tickets**: a ticket holds an area at a chosen load level
regardless of the player's position (portals, spawn chunks, chunk loaders). A quest variant has to
satisfy two constraints: it must not reintroduce the cost frozen Borough removed, and **the NPC
must never walk through a frozen chunk**. An NPC moving through frozen territory can't be
attacked, can't fight, can't be blocked by anything that would move, and nothing around it reacts,
so a live NPC in a frozen world is wrong in every direction.

First, most quest NPCs don't need a ticket. An escort or companion stays near the player, so it is
already Local. Tickets only matter for NPCs acting **independently** of the player.

Three shapes, from cheapest:

1. **Entity-only ticket** (simulate just the NPC): rejected. This is exactly the "walks through a
   frozen chunk" case.
2. **Abstract transit** (recommended default): when an independent quest NPC's route enters frozen
   territory, remove it from the map and move it along the route on the neighborhood-record layer
   ("arrives at X at frame F"). It reappears when its position is simulated again: at F if the
   destination is active by then, otherwise frozen at the destination and caught up on its next
   promotion. It never occupies frozen tiles in transit, and it costs one record timer. Its
   reappearance goes through the smeared transition queue. If its route leaves the loaded window
   entirely, abstract transit is the only option.
3. **Area ticket** (only when the quest needs the NPC to be interceptable live): a small moving
   bubble of radius r around the NPC runs at Neighborhood speed, reusing Local's machinery: O(boundary)
   per step, promotion with catch-up on entry, freezing on exit, all smeared. At r = 32 that's
   ~4,200 tiles, about 0.4% of a neighborhood. Keep r larger than the NPC's own engagement range plus
   a buffer, so it never tries to act across its bubble edge (the same seam rule as the
   Neighborhood/Borough border). **Cap concurrent area tickets** (e.g. 2-4) and give each one an
   expiry. When the cap is hit, extra tickets fall back to abstract transit rather than degrading
   the frame.

Open: the ticket cap and radius (measure the per-bubble cost), whether an area ticket may extend
into the dropped ring when the window shifts (probably not: fall back to transit), and how ticket
cost shows up in the diagnostics engine so it's visible when budgets are tuned.

#### Toggle item activator

`PotionActivator` always consumes a stack per activation -- wrong for a stateful toggle (Toxic Idol's
Poison aura costs a stack to turn *off* too). Needs a new `IActionActivator` (`ToggleItemActivator`)
`ConsumableActivationSystem` recognizes and doesn't consume a stack for. Open questions: does the
effect force-untoggle if the item is dropped/sold/stack empties (mirrors `DeathSystem`'s corpse-aura
cleanup, one layer up)? Is "toggled on" its own tracked state, or implicit in component existence (only
works while a toggle item drives exactly one effect kind)? Toxic Idol migrates to this once it lands.

#### Dexterity scaling ActionLockComponent.StandardLockFrames

Flat per-entity today (Goblin 54, Fairy/Ghost 48, Player 20, +Engineer 10%). Lerp
`ActionLockGate.StandardLockFrames` (1s) at Dex 1 down to 0.25s at Dex 300, off
`AbilityScoreComponent.Total` (same shape as `PotionCooldownEffects.ComputeDurationFrames`). Must
compose with, not replace, the racial baseline -- exact composition (multiply vs. replace) undecided.

#### Spell leveling

Same rules as Skills (level 0-15/20, XP with use, never decreases) -- land after Skills so both share
one leveling primitive. A spell's level would modify its `ActionEffect` magnitude/duration (bigger
heal, cheaper Magic Missile) -- exact "what changes" design still open.

#### Corpse looting rights based on damage dealt

Currently a free-for-all (anyone adjacent can loot). Needs per-entity damage-dealt tracking against a
target + a reset rule: on death (simple, but loses the record before looting starts unless kept
alongside `DeadComponent`) or on a timeout since last hit (avoids crediting an old, unrelated fight).

#### Mobs looting corpses

V1: fill own inventory from a nearby corpse until full (`InventoryCapacity.MaxNonPlayerStackCount`), no
preference. V2: preference by combat style/item rarity, once either concept exists.

#### NPCs use shops

Goblins/Fairies/Ghosts buying and selling at a `Shop`/`PotionShop`/`GeneralShop` autonomously via
`ShopActions.TryBuyFromShop`/`TrySellToShop` (the player-only version already shipped). Needs actual economic decision-making (what to sell for spare Gold, what to buy when low
on supplies) -- blocked on the NPC behavior composition item (this file's own Low Priority section),
which is where that decision would live once it exists.

#### Destroyed items

A "destroyed" item state: displays as "destroyed", modified description, can still be picked up, but
can't be used. Update `ContainerDestructionSystem` (and any future container types) to mark a destroyed container's inventory items destroyed instead of
deleting them outright, once this lands.

#### Add source and target modifier checks for all actions

Only `DirectDamage` runs both an Outgoing (source) and Incoming (target)
`StatModifierMath.GetEffectiveValue` pass -- `DirectHeal`/`DirectManaRestore`/`HotkeyExpansionGrant`/
`StatusEffectGrant`/`ChainedEffect`/`AuraSourceGrant` check neither. Make both checks standard on every
effect entry's `Apply`, even with no real `StatModifierTarget` consumer yet, so a future buff/equipment
source can hook in by granting a modifier alone. Calling-convention change, not a new stat.

### Low Priority

#### Organize blueprints, and replace testing composites with long-term ones

`Game/Blueprints` grew by feature rather than by design, and several of its blueprints exist only to
exercise a mechanic on the test map.

**Organization.**
- The infrastructure (`BlueprintDefinition`, `BlueprintRegistry`, `ResolvedBlueprint`, the appearance
  types, `BlueprintContext`, `BlueprintsModule`) sits in the folder root beside content (`PlayerKit`)
  and build helpers (`DisplayNameCache`, `StartingCurrencyGrant`). Separate infrastructure from content.
- Content folders mix kinds. `Composites/` holds real blueprints (`Player`, `GoblinEngineer`) next to
  test-map fixtures. `Parts/` holds a real trait (`Boss`), occupancy parts (`Tiny`, `Phasing`) and a
  temporary one (`LongDescriptionPart`). `NPCs/Generic/` holds only the test dummy, and
  `NPCs/TemporaryNpcLootGrant` is a helper, not a blueprint. `Objects/` mixes whole entities (shops,
  chest) with pieces never spawned alone (`Shop`, the stock parts, `ShopStock`).
- `TestDummyBlueprint` is the only blueprint named with a `Blueprint` suffix.
- Decide the layout once -- for example by what a definition is (race, class, creature, object,
  trait, piece) or by content area -- and apply it everywhere, `BlueprintsModule.BuiltIns` included.

**Testing composites to replace.** Each exists to show a mechanic on the test map, not as content:
- `TinyGoblin`, `PhasingFairy`, `StationaryFairyEngineer`, `GoblinFairy`, `GoblinEngineerTank`: the
  occupancy, stationary, multi-race and multi-class fixtures `TestMapBuilder.BuildFixtureEntities`
  places in the starting neighborhood.
- `LongDescriptionGoblin` / `LongDescriptionPart` (marked TEMPORARY): a description long enough to test
  word wrap.
- `GoblinForeman` and `Boss`: added to prove composites of composites.
- The test dummy (TEMPORARY in `FloorBuilder`), `TemporaryNpcLootGrant`, and the temporary grants in
  `PlayerKit` (test wand, test potions, permanent test modifiers).

For each, either design the long-term version it stands in for (real hybrid races, real bosses and
elites, real small creatures, real starting kits), or delete it once nothing needs it. Keep a fixture
only where a unit test needs it, and move that one into the test project rather than shipping it in
`BlueprintsModule`. Coordinate with "Real map generation -- neighborhood templates, replacing
TestMapBuilder", which removes the fixtures' placement, and with the Admin Mode "Spawn here >" /
"Apply >" menus, which list every registered blueprint and replace most of the fixtures' reason to be
on the map.

#### Neighborhood interactions -- Outbound and Inbound

Only the middle neighborhood is simulated; the 8 around it (Borough)
are loaded but frozen, nothing may target across the simulated/frozen boundary, and an entity
promoted out of Borough catches up its active timers. So an interaction between two neighborhoods
(the canonical case: goblins raiding fairies) can never be simulated live on both sides at once.
It runs in one of two modes, named by direction relative to the **active** neighborhood:

**Outbound -- initiated by the active neighborhood, resolved abstractly.** Example: the active
goblin neighborhood raids the frozen fairy neighborhood.

1. The goblins' interaction template (or later, faction AI) launches a raid party of real goblin
   entities. They path to a crossing point on the shared edge and walk across it.
2. Each raider freezes on arrival, per the normal Borough rule. Because nothing targets across the
   boundary, there's no half-live skirmish at the border first. The party is recorded on the
   neighborhood-record layer: members, target neighborhood, crossing point, departure frame, and
   return frame (departure + the template's raid duration).
3. At the return frame the raid is **resolved abstractly** from the party's strength against the
   defenders: damage per raider, which raiders died, loot taken. The fairy side takes the matching
   losses on concrete frozen entities (casualties chosen nearest the crossing point become corpses;
   loot is removed from their inventories and containers). Writing to frozen entities is fine, and
   it is smeared across frames like any other bulk change.
4. Survivors are moved back across the border at the crossing point with their damage and loot
   applied (a promotion, so catch-up runs). Dead raiders stay on the far side as corpses.

- **Calibrate so an abstract raid is never better than the same raid fought live.** X4: Foundations'
  out-of-sector combat outperformed in-sector combat, and players learned to look away to win (see
  the plan, section 2). Use the real damage formulas at a pessimistic hit rate, and tune against
  headless Inbound battles of the same composition.
- **The target becomes active mid-raid** (the window shifts): cancel the pending abstract
  result. Raiders thaw where they stand, with catch-up, and the raid continues live as ordinary
  combat.
- **The player's Local bubble reaches the frozen raiders** (standing near the border): same as
  above for the raiders and defenders inside Local. The raid goes live there.
- **The origin freezes while raiders are away:** resolve when the origin next becomes active,
  with the result as of the return frame. It's closed form, so waiting costs nothing.
- **The target is unloaded (Beyond) mid-raid:** resolve immediately and return the survivors
  early.

**Inbound -- initiated by a frozen neighborhood, fully simulated.** Example: a frozen goblin
neighborhood raids the active fairy neighborhood.

1. The frozen neighborhood's record carries its own schedule. That is a timer on the record, not on
   any entity: a handful of records, so it's cheap to keep ticking while every entity is frozen.
2. When it fires, real goblins are chosen from the frozen neighborhood, so population stays
   consistent. They are promoted (catch-up) and placed at crossing points **on the active side of
   the border**, a few per frame so they arrive in waves rather than in one frame.
3. They go through full simulation: combat, looting, everything the active neighborhood normally
   runs.
4. On a template-defined retreat condition (loot carried, casualties taken, or elapsed time) they path
   back across the border and freeze on arrival, per the normal Borough rule.

- **Pursuit stops at the border.** Nothing can target across it, so fleeing raiders are safe once
  they cross. Behavior should not chase into frozen territory, since a pursuer that follows just
  freezes.
- **Stranded raiders** that can't get back (blocked, or cut off by the player) stay in the active
  neighborhood as ordinary hostiles and become its members after a timeout.
- **The origin becomes active mid-raid:** nothing special, both sides are simply live.
- **The target freezes mid-raid** (the player leaves): raiders freeze where they stand, like
  everything else, and resume when it's next active.
- **The origin is unloaded mid-raid:** raiders re-home to the neighborhood they're standing in.
  Deletion follows current position, so they are not deleted with their origin.

Shared notes:

- **Crossing points** come from the layout templates' edge constraints (corridors that line up), so
  the interaction template and layout agree on where a border can be crossed.
- **Cost:** an Outbound raid costs nothing while away (frozen members plus one record timer). An
  Inbound raid adds its raiders to the already-budgeted active neighborhood. Only the placement and
  return steps need smearing.
- **Raid state lives on the neighborhood record,** so it's serialized with the record when Beyond
  neighborhoods start being saved.
- Depends on frozen Borough and catch-up (plan phase 6) and on real map generation above.

#### Borough end-of-day summaries

The four processing tiers each get a distinct job: Local is fully simulated, Neighborhood is
simulated slowly, Borough is **updated by periodic mathematical results**, and Beyond is frozen.
Borough entities are never visited by systems. Instead, at the end of each in-game day, every
Borough neighborhood gets an estimated outcome for what happened there, applied to its frozen
entities and records. Examples: the result of a raid between two Borough neighborhoods, or how many
crawlers died during a boss fight.

- **Estimate from the neighborhood, not by replaying entities:** aggregate strength, population and
  scheduled events on the neighborhood record, resolved in closed form. Apply the results to
  concrete entities (casualties become corpses, loot moves) through `NeighborhoodMembershipIndex`,
  smeared across frames like any other bulk change.
- **Run after a time buffer.** Borough <-> Beyond tier changes drain last after a window shift, so
  each summary waits long enough for every entity's tier to have settled before it reads them.
- **Share the calibration rule of the Outbound raid above:** an estimate is never better than the
  same event fought live.
- **Neighborhoods that change tier mid-day:** decide whether a neighborhood promoted to Neighborhood
  before the day ends gets a partial-day estimate, and whether one demoted to Beyond keeps its
  pending one.
- Depends on "In-game day/time tracking". The Outbound raid's abstract resolution above is one case
  of this; the "Unsimulated-tier time" item decides what Beyond does.

#### AdvancedDodge buff

A buff/upgrade that increases Dodge's movement distance beyond one adjacent tile and extends the
duration of its dodging-immunity window. Follow-up to Combat Overhaul: Dodge (this file's own entry
above).

#### Repair destroyed items

Item-type-specific Repair skills to restore a destroyed item -- blocked on Destroyed items (above) and
the not-yet-existing Skills system (this file's own Skills entry).

#### Item damage and repair

A new per-stack condition/durability concept, distinct from Destroyed items above (a damaged item
stays usable, just worth less and eventually repairable, rather than a binary destroyed/not state).
`ItemDefinition.GoldValue`/`ShopActions.ComputeBuyPrice`/`ComputeSellPrice` would
need a per-stack damage modifier applied on top of the flat catalog value -- necessarily per-stack,
not per-`ItemDefinition`, same split Item weight below already follows for a different field. Repair
likely wants to be the same Repair skill the entry above already wants, generalized to cover
"damaged" as well as "destroyed" rather than two independent mechanics.

#### Trapped containers

Containers can be trapped. Needs a trap-effect concept triggered on
interaction/loot.

#### Trap detection and disarming skills

Skills-system consumer (blocked on Skills, same as Repair destroyed items above) for detecting/
disarming Trapped containers.

#### Show runner race

Randomly selected; affects UI appearance and biases quest/enemy selection.

#### Teleport countdown with a window anchor

**Depends on a teleport visual effect** (a charge-up or dissolve the countdown plays over), which
doesn't exist yet; no countdown without something to watch during it.

A teleport into a Borough neighborhood lands the player in a frozen neighborhood that takes ~9.5 s to
thaw (skeleton builds at `ProcessingTierSystem.DefaultTransitionsPerFrame`, 256 per frame, held until
the evicted neighborhoods' built creatures are destroyed), while loading the new ring and the thaw cost
frame rate. Hide both with a countdown before the move, during which the window already sits on the
destination:

1. **Countdown start:** anchor the window on the destination neighborhood
   (`ProcessingTierResolver.ShiftWindowTo`). The streamer evicts and loads, and the destination
   thaws, while the player is still at the origin. The origin's Local circle stays live because Local
   follows the player, not the window.
2. **Window anchor:** while anchored, `ProcessingTierSystem` must not recompute the centre from the
   player's position (`NextWindowCenter`), or the player's next step during the countdown shifts it
   straight back. Released when the teleport completes or is cancelled; a cancel shifts the window
   back to the player's neighborhood.
3. **Countdown end:** teleport through the shared teleport path. Only the Local square walks remain
   (~10 ms measured in Release).

Open: countdown length (the simulation settles ~568 frames, 9.5 s, after a shift; the
countdown could instead end on `ProcessingTierResolver.Transitions.HasPendingSimulatedChanges` going
false, with a minimum), whether the player can act or move during it, what cancels it (damage, stagger, moving), and whether
it scales with distance. Teleports outside the loaded window are "Long-range teleports" below.

#### Long-range teleports -- pause and reload the map

World streaming relies on the one-neighborhood buffer between the player and Beyond to load
and save neighborhoods asynchronously, since walking 1024+ tiles takes far longer than a load. A
late-game teleport can land outside the loaded 3x3 window, where the buffer gives no protection.
Handle it as a special case: pause the game, unload (later: save) the current window, generate or
load the 3x3 around the destination, then resume. It reuses the window's load/unload path rather
than adding a second one.

#### EndOfLevelStairs (Game)

A one-way teleport from one floor to the next -- never back up, and never between MapLayers
(UnderGround, Ground and Flying are all the same floor). Floors are entirely separate maps: nothing on
one interacts with another except the entities carried through. When the player takes them, the carried
entities are unplaced (`TransformComponent.UnplacedOn`), everything else is destroyed through the normal
`EntityDestroying` cleanup, and the next floor's map is built and the carried entities placed onto it.
First real consumer of `FloorEnteredEvent`. Open: modules capture `IMapQuery` at `Configure`, so either
`World` swaps its `Map` in place or modules are reconfigured per floor; and which MapLayer the stairs sit
on (likely Ground, so flyers land and diggers surface to use them). See the matching Presentation item
for visuals.

#### NPCs avoid EndOfLevelStairs, and are destroyed entering one

NPCs can never use EndOfLevelStairs. NPC movement avoids a staircase tile, but an NPC that ends up on one
anyway -- lured, or pushed there by forced movement (see Entity displacement with damage) -- is instantly
destroyed rather than blocked. Getting a monster onto the stairs is a known crawler tactic from the source
material, so avoidance is a movement preference that forced movement overrides, never a hard block.
Check it where movement resolves (every placement or move onto the tile, at any processing tier), not in
each NPC behavior. Companions and followers are the exception -- see the next item. Open: whether a
multi-tile footprint overlapping the stairs counts as entering; whether destruction leaves a corpse, loot,
or kill credit.

#### Companions and followers travel through EndOfLevelStairs

A standalone feature, larger than the stairs themselves. Unlike every other NPC, companions and followers
can use EndOfLevelStairs. No companion/follower concept exists yet. Needs:
- Temporary NPC storage: an NPC that takes the stairs before or after the player is held off-map with its
  exact state and restored on the next floor. See Entity storage -- suspend an entity from processing
  without per-system checks (Global).
- Timers: how long a stored NPC waits and what happens when that runs out, as `FrameDeadline`s -- and
  against which clock while no floor holds it.
- Unlock conditions: what makes an NPC eligible to follow through the stairs at all.

#### MapLayer interaction (overview)

UnderGround, Ground and Flying are three layers of one floor (floors themselves are separate maps -- see
EndOfLevelStairs (Game)). Today the layers are sealed from each other: nothing changes an entity's Z, and
movement, target shapes, auras and the Local tier all stay on one layer. Wanted: flyers and diggers
(shorthand for UnderGround entities) are privileged -- they reach Ground more easily than Ground reaches
them -- but for playability they mostly come to Ground to interact, with specific exceptions (dropping
items from the air, earthquakes). The player changes layer only through rare dedicated abilities (a
flying race, a burrow spell). How flyers appear to a Ground player is still undecided.

Connected items, in dependency order:
1. Local processing tier across every MapLayer
2. MapLayer reach rules for actions
3. Layer-change actions -- Land, Take Off, Surface, Burrow
4. Auras and terrain effects per MapLayer
5. Corpses across MapLayers
6. Digger detection (Game), then Digger detection (Presentation)

#### Local processing tier across every MapLayer

Part of MapLayer interaction (overview); comes first, since every cross-layer attack depends on it.
`ProcessingTierResolver.ComputeTier` grants Local only on the player's own MapLayer, so a flyer directly
overhead or a digger directly below runs at Neighborhood tier while attacking the player. Local should
cover every layer of the floor. `ProcessingTierSystem.RetierSquare` retiers only the player's Z, and a
player layer change retiers the old and new layers separately -- both change, and the layer-change retier
mostly goes away. At current spawn rates (Ground 3%, UnderGround 2%, Flying 2%) the Local population
roughly doubles, so benchmark before and after (phase-performance-testing skill).

#### MapLayer reach rules for actions

Part of MapLayer interaction (overview). Every action declares the exact MapLayers it can affect from each
caster layer -- not relative "above/below", which would let a digger's ranged attack reach Ground the same
way a Ground ranged attack reaches Flying. Starting proposal:

| Caster / Target | Flying | Ground | UnderGround |
|---|---|---|---|
| Flying | all | tagged only (drop from air, some ranged) | none |
| Ground | ranged only | all | tagged only (bombs) |
| UnderGround | none | tagged only (earthquake) | all |

Touch points: `TargetShapeResolver` (Engine) builds every shape on the origin's Z;
`ActionTargetingController` builds hovered/clicked tiles on the player's Z; `TestCombatBehaviorSystem`
must only pick targets it can reach. An action refused for reach gets clear feedback (disabled cursor),
never a silent no-op. An undetected digger is never a valid target (see Digger detection (Game)).
Related: Add source and target modifier checks for all actions (Medium).

#### Layer-change actions -- Land, Take Off, Surface, Burrow

Part of MapLayer interaction (overview). Land/Take Off (Flying <-> Ground) and Surface/Burrow
(UnderGround <-> Ground) as ordinary actions with an action lock, so the transition is a window where
Ground entities can hit back. Movement never changes Z (`MovementCandidates` keeps it), so these are the
only path. Refused with feedback when any tile of the destination footprint can't be occupied -- reuse
`MovementCandidates.CanOccupyCell` against the destination Z (Blocking occupant, structure,
movement-blocking terrain). `World.MoveEntity` takes a full `Vector3Int`; verify a Z-only move keeps the
occupant index, the per-column occupied-layer mask and `NeighborhoodMembershipIndex` (keyed by Z) in
sync. NPCs get them by race (Fairy and Ghost populate Flying and UnderGround today); the player only
through rare dedicated abilities (a flying race, a burrow spell).

#### Auras and terrain effects per MapLayer

Part of MapLayer interaction (overview). `StatusEffectAuraSystem` spreads every aura on its centre's Z
only, and terrain auras (`TerrainAuraSources`) skip Flying, which has no floor. Decide per effect which
layers it reaches, with the same exact-layers rule as MapLayer reach rules for actions: a Ground fire
aura probably doesn't burn a flyer overhead, while an earthquake is exactly an UnderGround effect reaching
Ground.

#### Corpses across MapLayers

Part of MapLayer interaction (overview).
- A flyer's corpse falls to Ground. `DeathSystem` converts a dead entity to NonBlocking where it stands, so
  the fall is a Z move after that; a NonBlocking corpse can share a tile with a Ground Blocking occupant.
  Open: a wall or lava below, a multi-tile footprint partly over a wall, and whether the fall damages what
  it lands on (see Entity displacement with damage).
- A digger's corpse stays UnderGround, lootable only while the looter is UnderGround. Bug today:
  `MapWindow.IsAdjacentToPlayer` (Loot and Shop) uses `GridDistance.ChebyshevDistance`, which ignores Z,
  so a Ground player standing over an UnderGround corpse can loot it. Needs a same-layer check for every
  corpse and shop, not just diggers.

#### Digger detection (Game)

Part of MapLayer interaction (overview). Diggers are hidden from the player until detected, through
future skills and actions (none exist yet -- see Skills), at two levels: low (something is there) and
high (what it is). Hidden means hidden everywhere, not just undrawn: inspection and selection, tooltips,
targeting (never a valid target), and the per-column occupied-layer mask behind
`MapWindow.DrawLayerBadges`, which today would give a digger away with a `v` badge. Needs a per-digger
detection level against the player, and when it lapses (a deadline, per the `FrameDeadline`
convention). Open: player-only or NPCs too; whether surfacing reveals a digger. See Digger detection
(Presentation).

#### Random map generation v1

`FloorBuilder.CreateMap`/`PopulateFloor` populate a fixed `TestMapBuilder` layout today.
`MathUtility`'s ctor already takes an optional seeded `Random` -- just needs to actually reach it from a
real session (`GameLoop.Initialize` currently constructs an unseeded one). Player-facing needs: a seed
input at floor-start, and a HUD/menu readout of the active seed (Minecraft precedent -- share a seed to
reproduce a layout).

#### Equipment (Game)

Slot/stat rules. Companion to the Engine/Presentation equipment items. Once Complex health exists more
broadly, some slot *counts* (not just which slots exist) should scale with an entity's active,
non-disabled body parts of the matching type (a ring per finger, shrinking if a hand is lost) -- Simple
entities keep a fixed layout.

#### Melee actions should declare which body parts perform them

Follow-up to Equipment + Limb-specific penalties (`IMPLEMENTATION-NOTES.md`). `BodyPartEffectsSystem`
currently scores a generic penalty off every Arm/Hand (correct only because nothing equips to one
specific limb yet). Once Equipment exists, `IActionActivator`/`ActionEffect` should let a melee action
declare which `BodyPartType`(s) perform it (a two-handed weapon needing both Hands; an offhand punch
caring about one arm) -- `BodyPartEffectsSystem` would then key its penalty off the acting part(s), not
a blanket aggregate.

#### Enchantment

Next consumer of `InventoryActions.AddDivergentItem` (already built generic for this): builds a
modified `ItemDefinition` `Override`, same split-into-new-stack primitive as Wand charge depletion. No
design yet for recipe/UI/materials/where performed.
- **Provenance tracking** (why/how a stack diverged): nothing records this today. Once Enchantment is
  real, an `Override`/sidecar field should carry an origin string for tooltips.
- **Acquisition provenance** (separate concept -- where a stack came from: loot, corpse, shop, craft):
  same open question (field vs. sparse component); should record the source's *name*, not id (ids
  recycle; corpses are the one exception, never destroyed).

#### ItemBindingRule for hotkey-bound consumables

Item hotkeys bind to one exact `StackInstanceId` -- once depleted, the slot just goes empty. A future
`ItemBindingRule` would bind to an item id + a preference rule (lowest/highest charges first, plain
batch first), re-resolved each time the current stack runs out.

#### Stats -- consumers

Infra landed (`IMPLEMENTATION-NOTES.md`). Remaining:
- Split hidden ability scores (Luck/Wisdom) into composites of other hidden scores -- needs more hidden
  scores to exist first.
- Wire the concrete "modifies" behaviors: Strength->melee damage (retire hardcoded `PunchDamage`
  consts), Constitution->`MaximumHealth` x10 (regen/potion-cooldown already landed), Dexterity->
  `StandardLockFrames` (own item above), Intelligence->mana (once Mana lands), Charisma->shop/charm,
  Luck->loot/AI.
- Non-player races get their own baseline scores instead of flat 5.
- Level-up modifies Core scores (Hidden excluded). See the matching Presentation stats item.

#### Item weight and carry capacity scaling with Strength

No item has weight; storage is unlimited. Add a carry-capacity limit off `AbilityScoreComponent.Total`
(Strength), gate pickup on it. Depends on the Item weight (Presentation, below) item for the weight
field itself.

#### Scroll and spell durations scaling with Intelligence

Duration-based effects (buffs, DoTs) should scale with caster Intelligence, same shape as
Constitution->potion-cooldown. Needs an `ActionEffect` duration field as a real concept first.

#### Damage types

No concept exists -- every hit is undifferentiated. Starting set: Magic, Blunt, Explosive, Slashing.

#### Level collapse timer

Global per-floor countdown pressure mechanic (not a `CountdownTicker` variant -- those are
per-entity/per-effect). Needs a HUD countdown element below the mana bar.

#### Tomes

Grant a spell outright on consumption (unlike Scrolls' 200-use mastery). Either a new `SpellGrant`
effect entry, or reuse `ScrollMasteryEffects` with a threshold of exactly 1.

#### Burning status effect from touching lava

Damage over time, decreasing, stacking (multiplicatively worse), worse per movement while still in lava.

#### Petrification status effect

Beyond Paralysis: forces `ForceBlockingComponent`-blocking regardless of normal blocking state. Real
map-occupancy problem -- `Map` tracks one Blocking occupant per tile; no resolution policy yet for
turning an already-occupied-adjacent or currently-non-blocking entity into forced-blocking. Needs a
design pass through `World`/`Map` placement, not a drop-in `ParalysisEffects.Apply` extension.

#### NPC behavior composition + generalized "turn claimed" signal

Follow-ups to the temporary `TestCombatBehaviorSystem` stand-in (`IMPLEMENTATION-NOTES.md`):
- Replace the hardcoded if/else chain with composable behaviors (self-heal, engage, flee, wander)
  arbitrated by a per-race-configurable priority/utility system.
- `MovementSystem` currently checks `_pendingAbilityActivations`/`_pendingConsumableActivations`
  directly to know a turn's claimed -- doesn't scale as action types grow. Needs a single shared
  "turn claimed" marker any decision system can set/check generically.

#### User feedback for actions is missing entirely

Casting, cancelling, AOE/melee landing, status effects applied -- no player-visible feedback beyond the
state change itself. No design yet.

#### Corpse decay/destruction and destructible terrain

`DeathSystem` never calls `EntityManager.DestroyEntity` (corpse stays fully populated, non-Blocking, for
future looting). `DestroyEntity` is reserved for a real decay-timer or "loot then destroy" step, or
future destructible terrain (skips `DeadComponent` entirely, just calls `DestroyEntity` on trigger).

#### Self damage buff ability

Example FreeCast/Immediate ability raising the caster's own outgoing damage for a duration.

#### Defensive buff spell -- damage reduction + healing over time

Self-targeted, combining a timed `StatModifierGrant(IncomingDamage, ...)` (fully supported today) with
a periodic self-heal built like Burning/Poison's DoT (`TimerBasedAuraApplier<T>`, healing instead of
damaging). The regen tick can now carry `IncomingHealing`/`OutgoingHealing` through the chain the same
way DirectHeal does (`HealthHeal.ComputeAmount`).

#### FreeCast toggle-aura ability

Item side landed (Toxic Idol, `IMPLEMENTATION-NOTES.md`). Still want the actual FreeCast *ability*
version (usable during an Action Lock) for that specific coverage, and to remove the "costs a stack to
toggle off" quirk (see Toggle item activator above).

#### BodyPartType categorization -- lifting/pickup still open

Movement/melee consumption landed (`IMPLEMENTATION-NOTES.md`). Still open: `InventoryActions` pickup
gating on a disabled Arm/Hand, and carry capacity/lifting (blocked on Strength/carry-capacity infra
above). `BodyPartType.Wing` exists but isn't granted to any race yet.

#### Per-body-part vs whole-entity status effects

`StatusEffectStack`/`StatusEffectAuraApplierRegistry` apply every effect entity-wide today -- correct
for Poison (systemic), wrong for Burning on a Complex entity (a burning leg reads better, and ties to
targeted-damage above: lava burning legs should apply Burning to the legs specifically). Needs a
part-scoped vs. entity-scoped declaration on `StatusEffectGrant`/`IStatusEffectAuraApplier`, and a new
store keyed by (entityId, bodyPartId) for the part-scoped case. Feeds the HealthWindow item
(Presentation).

#### Movement System

`SeekTarget` movement mode.

#### Lootbox delivery, and moving Lootbox out of Achievements

`AchievementModule`'s unlock path describes a `Lootbox` reward in the notification but never calls
`InventoryActions.AddItem` to actually deliver it (now available, unblocked). Separately: `Lootbox`/`LootboxRarity` currently live in and
are named for Achievements, but quests/loot-drops/level-up should be able to award one too -- move into
their own module once a second real awarder exists. Planned in `PLAN-loot-boxes.md`; delete this entry
when its Phase 2 lands.

#### Loot boxes can only be opened in safe rooms

Refuse loot box opening (Activate/double-click, see `PLAN-loot-boxes.md`) unless the player is standing
in a Safe Room, with clear feedback (disabled "Activate" with a reason) rather than a silent no-op.
Blocked on first creating Safe Rooms and zones -- no zone concept exists yet.

#### Lootbox drop tables

Every loot box currently drops a single stack of 1-10 of one item picked uniformly from the whole item
catalog, regardless of type or rarity (the placeholder `RandomSingleStackContents` in `PLAN-loot-boxes.md`).
Replace it with real drop tables: type decides which items can appear (Alchemist -> potions, Weapon ->
weapons, ...), rarity decides their value (e.g. a Gold value budget per rarity), and a box can be either
set contents (specific rewards) or a random pull from its table. Needs higher-value items to exist
before rarities above Gold mean anything -- today every item is worth 1-20 Gold.

#### Advanced boss loot box awards

A boss currently grants at most one box, and only when the player lands the killing blow
(`BossLootboxAwarder`, see `PLAN-loot-boxes.md`). Award boxes per contribution instead -- different
boxes for landing the killing blow, dealing the most damage, starting the fight, etc. -- so one fight
can grant the player several. Needs per-fight damage/participation tracking; shares that need with
"Corpse looting rights based on damage dealt". Also decide how an active quest's own award for killing a
specific boss (a `LootboxReward` with a content override, e.g. an item thematic to that boss and quest)
combines with the boss's own box -- replaces it or adds to it.

#### NPC component

No direct "is this an NPC" marker -- inferred indirectly today (exclude `PlayerEntityId`, or a specific
race check). Raised by `TemporaryNpcLootGrant`, which targets Goblin/Fairy/Ghost individually.

#### In-game day/time tracking

`DeadComponent.DiedAtFrame` only shows a raw frame tick in the corpse summary. A real calendar/clock
would make that (and anything else wanting a timestamp) human-readable. Also unlocks the Crawler TV show
item below (time-gated interactions).

#### Restock shops on the day/night swap

Follow-up to In-game day/time tracking above, which this is blocked on -- no real "day/night" concept
exists yet to swap on. Once one does, reroll a shop's stock (`ShopStock.GrantRandomStock`) on the transition, and reset its own Gold back toward its starting amount so a
shop the player has drained doesn't stay unable to buy anything forever. Today a shop's stock is
rolled once at spawn and never refreshes.

#### Preferred stock for items added to shops

`ShopStockPreferenceComponent`/`EnsurePreferredStockLevel` is
only ever assigned by `ShopStock.GrantRandomStock` at spawn-time stocking. A player selling a shop an
item type it has never stocked before -- via ordinary drag-sell today, or the trade window once it
lands -- silently falls back to `ShopStockPricing.DefaultPreferredStockLevel` (20) regardless of what
the item actually is, rather than a hand-tuned value the way spawn-stocked items get. Give a sold-in
item type a sensible preferred level the first time it lands in a shop that's never carried it (a flat
default scaled off the sold quantity, an item-tag-based table, or similar) instead of always silently
defaulting to 20.

#### CVS (Cosmic Value Shop) general store

A "General Store" shop blueprint themed as a CVS ("Cosmic Value Shop") parody -- larger than average
buy/sell margins vs. a normal `GeneralShop`. First use grants an achievement whose reward is a "CVS
Receipt" item, its description text unusually long and generated from the actual items/currency
traded that session (the joke being real CVS receipts). Also enrolls the player in a newsletter
delivered via floor mail each floor -- needs floor mail as a delivery channel (no mail system exists
yet) and depends on achievement rewards being deliverable (see Lootbox delivery above, mostly landed).

Also add a CVS Rewards currency, worth 10% of a Gold in trades (`ShopActions` pricing math would need
a real conversion-rate concept, not just another flat `CurrencyType` enum value). Blocked on making
`CurrencyRowContent` support more than its current hardcoded Gold/Credits pair -- it needs to become a
dynamic/expandable list before a third currency can show up in it at all.

#### Crawler TV show

Interacting with a television object at specific times of day plays an in-universe "show" -- flavor
content, no mechanical effect. Blocked on In-game day/time tracking above (needs a real clock to gate
on). See the matching Global item for the joke in-show advertisement.

#### Achievement content backlog

15 achievements ship today to prove the pipeline; rest is a deliberate incremental backlog (many
low-value early, tapering to fewer/higher-value by midgame). TODO: give each achievement a pool of
descriptions instead of one fixed string.

Design-target examples (implement once the underlying system lands): start with a cat, find a Borough
Boss, punch a slime, kill an armed enemy bare-handed, kill 20+ non-combatants in one attack, reach level
2, wear magical gear, spell level 3, first corpse loot, store 10 tons of weight.

Implemented-but-not-yet-unlockable, waiting on dependencies: `LonerAchievement`/
`UnarmedCombatAchievement`/`EmptyPocketsAchievement` unlock unconditionally today (no
companion/equipment/start-kit-selection systems exist to gate them properly yet -- revisit each once
its dependency lands). `BigMusclesAchievement`/`UnbreakableAchievement`/`ShanghaiKidAchievement`/
`RevengeOfTheNerdsAchievement`/`KillerQueenAchievement`/`MinMaxerAchievement` react to
`AbilityScoreBaseValueChangedEvent`, which nothing publishes yet (no level-up/permanent-boost system) --
all six also currently reward a placeholder "upgrade choice" with no upgrade-choice system to back it.

#### Tag.Spell can drift out of sync with the actions it describes

Hand-authored, independent of `IActionActivator` -- nothing enforces it, and `SpellCasterAchievement`
trusts it alone. Either drop `Tag.Spell` and key off `action.Activator is SpellActivator` directly, or
keep it as an independent classification but have `SpellActivator`/its registration apply it
automatically so a definition can't forget it.

#### Boundary-aware ProcessingTierSystem recompute

`ProcessingTierSystem` recomputes every movement-capable entity's tier once per its own stripe turn
regardless of whether anything changed. Targeted alternative: a coarse spatial grid (cf. `AuraGrid`) so
a player move only re-tiers entities in the thin band straddling the Local-radius ring. Real structural
addition (new spatial index, insert/remove/move bookkeeping, a genuine correctness surface around
boundary-band width) -- only worth it once profiling actually confirms this as a bottleneck (one past
pass was inconclusive, coincided with unrelated Paralysis load).

#### Entity displacement with damage

`World.MoveEntity`/`PlaceEntityOnMap` no-op when a Blocking destination is occupied -- too blunt for
knockback/forced-push. No such effect exists yet; once one does, needs its own resolution (collision
damage in lieu of moving, or redirect to nearest free cell).

#### Dungeon Anarchist's Cookbook

Rare floor-3 item, many forms (one per specialization) with recipes/hints for that build, encouraging a
different playstyle next run. Depends on floor-specific guaranteed drops (no per-floor loot table yet)
and a way to pick which specialization a run's copy targets. Each form's pages should support
player-added multiline notes (a real second `TextBox.Multiline` consumer, see Text input in
`IMPLEMENTATION-NOTES.md`).

**Meta-progression across runs** (two directions, neither designed): (1) tiny permanent Ability
Score/Skill boosts derived from a just-ended run's build -- Skills should carry over far more rarely
than Ability Scores (a skill is already a bigger power swing). (2) A shared New Game Action Pool each
run banks one action into, offered as a starting choice on a fresh run. Both need a new persistent,
save-file-level meta-progression store distinct from anything in a single `EcsContext`.

## Presentation

### High Priority

#### Global hard minimum/maximum element sizes for user resizing

`UiInputController.ComputeResize`/`ClampResizeToBounds` clamp a drag-resize to `element.MinimumSize`/
`MaximumSize` alone (`ElementLayoutOptions.MinimumSize`/`MaximumSize`, both optional per element) --
`Element.cs`'s own Build defaults an unset `MinimumSize` to `Vector2(0, 0)`, so any element without an
explicit minimum can be dragged all the way down to zero (or effectively zero) width/height. That's the
root cause class behind the TextWindow/StringUtility crash just fixed (a HealthWindow resized to a
degenerate size fed a negative wrap width into word-wrap) -- that fix only patched the one downstream
symptom, not the underlying gap. Add an engine-wide hard minimum and maximum (e.g. a constant pair on
`UiInputController` or a `WindowService`-level config) that every resize clamps against unconditionally,
regardless of whether the element sets its own `MinimumSize`/`MaximumSize`. A per-element optional
min/max should only ever narrow that global range, never escape it -- needs validation (or clamping) at
whichever point an element's own min/max gets set, so a caller can't accidentally configure one outside
the global bounds.

#### Inventory management -- grid cell reorder still open

Read-only view, tabs, drag-onto-hotbar, and click-to-inspect all landed (`IMPLEMENTATION-NOTES.md`).
Still open: dragging one grid cell onto another to reorder -- blocked on Standard widget set below.

#### Stack Controls and Partial Stacks

Supersedes the former "Manual stack splitting and merging" entry -- broader scope. Today every
stack-moving interaction (mouse drag, context menu Give/Take/Sell/Buy, the trade window's Add/Remove)
is all-or-nothing: the whole stack moves or none of it. Needs a single, consistent control scheme --
across both mouse actions (drag gestures, modifier keys) and context-menu options -- covering all
three: move the whole stack, move half (rounded down), and move a player-chosen exact amount (a
quantity-prompt UI, still not built). Whatever scheme is chosen must work identically in *any* item
grid (player inventory, corpse/chest loot, shop, trade window), not be special-cased per window.
Splitting also needs a real primitive: peeling part of a stack off into a new, separate stack instead
of only being able to move a stack as a whole. And the reverse -- combining two stacks of the same
item (respecting divergence: only stacks with equivalent Override/IsDivergent state can merge, per
`InventoryActions.AreEquivalentOverrides`) into one, which today only happens automatically as a
side effect of `AddItem`/`AddItemWithOverride`'s own capacity-driven merging, never player-initiated.
Investigate how other games solve this (Minecraft's right-click-half/right-click-drag-spread and
shift-click quick-transfer; WoW's vendor shift-click quantity popup; Path of Exile's shift-click-drag
quantity slider) before settling on this game's own scheme -- see the industry-standard interface
investigation in this session's transcript for a starting comparison. Blocked on grid drag-to-reorder
(above) and a quantity-prompt UI (needs Context menu / mouse button coverage's remaining scope).

#### Equipped-item comparison

Blocked on Equipment existing (Game + Presentation). See Item Details Comparison in
`IMPLEMENTATION-NOTES.md` for the landed non-equipped half.

#### Advanced sort control -- context-menu of sort options

Today's sort control is a blind click-to-cycle button. Replace with an icon expanding into a context
menu listing every option -- depends on Context menu / mouse button coverage's remaining scope (or some
generalized "popup a list of choices" primitive). Should also cover sorting by a stat (e.g. wand
charges) once per-slot divergence gives items stats worth sorting by -- the option list may need to be
built dynamically per tab, not a fixed list.

#### Search icon that expands into a search bar

Both the Tab search box and the item-name search box are always-visible today. Space-efficient
alternative: a small icon expanding into the box on click (ghost text/debounce unchanged), collapsing
back when empty and unfocused. Pure presentation change.

#### Inventory tab reordering + custom-tag trailing tab

Dynamic per-tag tabs landed (`IMPLEMENTATION-NOTES.md`). Still open: user-reordering the default sort,
and a trailing "+" tab for custom user-created tags.

#### Item weight (definition-only) and race weight ranges

- Weight lives on `ItemDefinition` only (never per-stack) -- same split as Name/Description/Tags. A
  stack's total weight = `definition.Weight * stack.Quantity`, computed on demand.
- Units: pounds. Potions/scrolls default 0.1 lbs today (placeholder for that item class).
- Race weight ranges (separate concept from carry capacity): Goblin 40-70 lbs, Fairy 20-40 lbs (rough
  guesses, no lore anchor). Player and Ghost still need ranges -- Player depends on
  character-customization decisions out of scope here; Ghost's "weightless/ethereal" may be the real
  answer, worth deciding deliberately.

#### Game over screen on player 0 HP

`HealthDamage.Apply`/`DeathSystem`/`DeadComponent` exempt the player from death today since there's no
end-state UI. Build this before lifting that exemption.

#### TextBox context menu wiring, and "Bind To..." sub-menu

Context menu mechanism + AdvancedMapContextMenu landed (`IMPLEMENTATION-NOTES.md`). Still open:
TextBox's Cut/Copy/Paste/Select All (currently keyboard-only) via the same `ContextMenu` mechanism; and
an inventory item's "Bind To..." (hotbar slot picker) -- needs a genuinely new capability, cascading
sub-menus (an option carrying a nested option list, opened east of the clicked row, a second managed
`ContextMenu` instance, and updating the outside-click check to cover both popups). Neither of
AdvancedMapContextMenu's five landed menus needed this.

#### Player stats v1

Persisted view of the player's active stats -- fixed set.

#### Standard widget set

No checkbox, radio button, dropdown, slider, list box, or tree view (`Toggle` covers checkbox --
`IMPLEMENTATION-NOTES.md`). Tabs exist (`TabbedContent`). Inventory/spell hotbar and equipment/stats
windows still want list/grid controls beyond what exists.

#### Tile count and size closer to Dungeon Settlers

Inspired by Dungeon Settlers. Match its on-screen tile size and how many tiles are visible at once
more closely. Today `MapCamera.BaseTileSizePixels` is a fixed 36px at `ZoomLevel.Team` (18px
Neighborhood, 9px Borough, all derived from it), and the visible tile count isn't set directly --
`UpdateTileSizes` derives it from the map window's content size divided by tile size (+2 for partial
edge tiles). So "tile count" is a function of tile size and the map window's layout in
`ShellBootstrapper`, and both may need to change. Start by measuring Dungeon Settlers' visible
columns x rows and tile pixel size at a common resolution, then decide whether to match size, count,
or both. Things that scale with tile size and need a visual check afterward: sprite legibility (pairs
with AI-generated sprites and Per-entity sprite scale), map fonts/badges, `HudMetrics`, and
per-frame cost -- larger tiles means fewer visible tiles and a cheaper `DrawOccupants`; smaller means
the reverse.

#### Walking and action animations

Inspired by Dungeon Settlers. Supersedes the former Medium "Lerp movement animation between tiles"
entry. Two parts:
- **Walking**: entities teleport between tiles on-screen today. Lerp the sprite's rendered position
  across the move's `ActionLockComponent`-driven frame count, plus a walk-cycle frame sequence where
  a sprite has one. Purely visual -- grid position/occupancy still change instantly on the same
  frame as today.
- **Actions**: a short per-action animation (windup during a Delayed action's charge, a strike on
  activation). Could drive off the same elapsed-real-time tracking `MapWindow.TrackChargeElapsedFraction`
  already does for the charge fill, not the stepped `CurrentLockFramesRemaining` (see that method's
  own remarks for why the stepped value never reaches 0).

Needs a frame-sequence concept `SpriteComponent`/`SpriteManifest` don't have yet (one cell per
entity today, chosen once at build). Scope to Local tier -- nothing off-screen should pay for
animation state. Pairs with the AI-generated sprite item (Medium, below), which would be the natural
point to author walk/action frames.

#### Sprites taller than one tile, and two-tile walls (front + top)

Inspired by Dungeon Settlers. Two related wants: character sprites that extend above their own tile,
and walls drawn as a front face on their own tile plus a top face on the tile above. Both need the
map drawn top-down (row-outer), so a lower row's sprite overlaps the row above it rather than the
other way round.

Two things in `MapWindow` stand in the way today:
- `DrawOccupants` walks column-outer, row-inner. Within a column that's already top-down, but column
  c+1's row r-1 draws after column c's row r, so anything wider than one tile (see Per-entity sprite
  scale, Low) gets overdrawn by its upper-right neighbour. Its own remarks say row-major measured no
  performance difference and was only left alone because it would change overlap order for no gain.
  This is that gain.
- Walls are terrain (`Map`'s separate `TerrainLayer` array), rendered into the `MapTileLayerCache`
  texture before any occupant. A wall's top face covering the tile above has to occlude an entity
  standing behind it (north), so wall tops can't live in that cache -- they need to draw in the
  row-ordered occupant pass, after the row above. That cache's remarks explain why terrain was pulled
  out of the per-tile loop in the first place (multi-tile footprints), so this needs a real design,
  not just moving the wall draw.

Tall terrain goes semi-transparent while the player or the inspected entity is behind it: when
either stands on a tile that a wall's top face (or any other tall terrain sprite) draws over, render
that sprite at reduced alpha so the entity stays visible. "Inspected" is
`MapViewState.InspectedEntityId` (Detail/Admin inspection's followed entity, the same one
`DrawFollowedEntityHighlight` tracks), and it should cover every tile of that entity's footprint
(`TransformComponent.Size`), not just its origin tile. The occupant draw path already carries an
alpha multiplier (`TryDrawEntityVisual`'s `alphaMultiplier`, used for Phasing), so this is a
per-tile "is the player or inspected entity under this sprite's overhang" check at draw time, once
tall terrain draws in the occupant pass at all -- at most two entities to check, so no spatial index
needed.

Related: Per-entity sprite scale and Multi-tile sprites (both Low) -- a larger player sprite is the
first real consumer of this.

#### HP and mana numbers on the HUD bars

Inspired by Dungeon Settlers. Draw "15 / 20" in white, centred on `PlayerHealthBarContent`'s and
`PlayerManaBarContent`'s bars. `ResourceBarRenderer.Draw` only takes a fraction today, so either
callers pass current/max through or the text draws separately on top. Health's max is the effective
one (`StatModifierMath.GetEffectiveValue(..., MaximumHealth, ...)`), not the base, and current health
is a float -- round it for display. `PlayerManaBarContent` has no `FontService` yet. Use an
outlined/contrast draw (`ContrastTextRenderer`, or `LabelRenderer.DrawCentered(..., outline: true)`)
so white text stays readable over a bright fill.

#### Show "Self" as the source when an entity is its own source

A `ActionSource` created from the entity the effect landed on renders as that entity's own name
today -- the player's self-granted modifiers read "Player1" in the Ability Score window's line prefix
and hover title, and in `PlayerActivityLog`'s `source=` field, as though something else did it to them.
It should read "Self" instead. There are real self-sourced modifiers to see this on right now:
`Tank`'s MaximumHealth/HealthRegen bonuses and `PlayerKit`'s multiplicative ability-score seeds
all pass `ActionSource.FromEntity(..., player)`.

The rule is per-viewer, not player-global: the source is "Self" when its entity is the same entity the
line is describing. That way inspecting an NPC's own self-buff reads "Self" too, rather than only ever
special-casing the player. Compare with `ActionSource.IsEntity(key)` against an `EntityKey` --
never an entity id, which is recycled the moment an entity is destroyed (`IPlayerQuery.PlayerEntityKey`
is the player's). That means the describe call needs the subject entity's key threaded in, which it
doesn't take today.

Fold the two copies of the logic into one while doing it: `ModifierDisplayFormatting.DescribeSource`
(`Presentation/UI/ModifierDisplayLine.cs`) and `PlayerActivityLog.DescribeSource` are the same
Entity-vs-`ToString()` switch written twice, and `SecondaryInventoryWindow.ResolveKillerName` is a
third partial copy (the corpse "killed by" line -- suicide/self-kill should read "Self" there as well).
`ActionSource.ToString()` stays as-is: it's diagnostics with no subject to be relative to.

### Medium Priority

#### Consolidate Admin Mode features out of the bootstrappers

Admin Mode's pieces are wired one at a time, wherever each happened to need them:
`ShellBootstrapper` hands `MapWindow` three separate admin dependencies (`NeighborhoodStreamer` for
"Regenerate", a `BlueprintAdminCommands` it constructs itself for "Spawn here >"/"Apply >", and
`EntityTeleporter` for "Teleport here"), and `GameLoop` keeps its own title-bar sync
(`SyncAdminModeWindowTitle`). Each new admin tool adds another settable property and another
bootstrapper line.

Collect them into one admin collection (an `AdminTools`/`AdminCommands` object built once from the
world session, holding the commands and anything they need) that the bootstrapper passes as a single
dependency, with the context-menu groups built from it instead of from `MapWindow`'s own checks.
Consider moving the title sync and the F12 toggle (`UiInputController`) alongside it. Out of scope:
the scattered `GlobalState.IsAdminModeOn` reads that change what windows show (the hidden ability
scores, admin inspection); those are display rules, not tools. The admin blueprint menus and
"Teleport here" stay -- this only changes how they're wired.

#### Draw neighborhood borders and the Local radius in Admin Mode

The map gives no sign of where one 1024x1024 neighborhood ends and the next begins, or where the
player's Local bubble ends, so the tier a creature is running at -- and whether a crossing did what it
should -- can only be inferred from behaviour. Under Admin Mode (F12, `GlobalState.IsAdminModeOn`),
draw both over the map, **in two different colours**:

- **Neighborhood borders:** a line on each 1024-tile boundary the viewport crosses, ideally with the
  neighborhood's coordinate (`Neighborhoods.CellOf`) labelled somewhere unobtrusive.
- **The Local radius:** the square of Chebyshev radius `ProcessingTierResolver.LocalRadiusTiles` (80)
  around the player, on the player's own layer. Local overrides the neighborhood tier, so where the
  bubble reaches into another neighborhood *this* line, not the neighborhood border, is the actual
  simulated/frozen seam -- the place a simulated creature stops attacking a frozen one. Found testing
  phase 6b: goblins and fairies fighting 75 tiles away in the neighborhood across the border looked
  wrong until the distance was worked out by hand. Consider a fainter second square at the exit radius
  (`LocalExitRadiusTiles`, 96), since a creature already Local only leaves past that.

Worth extending to the tier itself once the lines exist -- tinting or outlining the player's own
neighborhood against the frozen ring would make a crossing, and phase 6's smeared thaw, directly
visible while testing rather than something to infer.

#### AI-generated sprites with a hovered state

Inspired by Dungeon Settlers. Generate a consistent sprite set with AI tools, replacing today's
mixed assets and glyph fallbacks (Fairy/Ghost/Lava have no sprite -- see `SpriteManifest`). Every
sprite also needs a "hovered" look: a white border around its silhouette. Better generated at draw
time from the sprite's alpha (draw it offset in white on each side, then the sprite on top) than
authored as a second cell per sprite. That keeps the manifest at one cell per sprite and works for
anything added later. Same family of problem as mask-based recoloring (Low, below) -- one base
sprite, many runtime variants. If walk/action frames get authored at the same time (see Walking and
action animations, High), plan the sheet layout for them up front.

#### Vertical status effect list under HP and mana

Inspired by Dungeon Settlers. Supersedes the former Low "Status effect stack count on the player's
status bar" entry. `PlayerStatusEffectsContent` draws one icon per effect type in a horizontal row
under the health bar, with Poison/Burning stack counts (and the potion cooldown's seconds) drawn
*below* each icon. As the number of effect types grows, switch to a vertical list below HP and mana:
one row per effect, icon on the left, number to its right. That frees the number from the
below-the-icon position and gives room for every effect's count, not just Poison/Burning's. The host
window's fixed `Size` (one icon row tall) would need to grow with the active effect count.

#### Party inventory UI -- compare Dungeon Settlers and Elden Ring

Research item. Look at Dungeon Settlers' party inventory UI and Elden Ring's inventory/equipment
screens, and note what's worth adopting for the Inventory window and the Equipment menu (Low,
below). Check it against the still-open Stack Controls and Partial Stacks (High, above), which
already calls for a similar industry comparison.

#### TextDivider label clipping and right-line spacing

Investigate why the bottom of some `TextDivider` label letters (descenders -- g/y/p, etc.) render
clipped -- possibly `DrawContent`'s `textY` centering (`(ContentSize.Y - textSize.Y) / 2f`) against a
`MeasureString`-reported height that doesn't fully account for a descender's real glyph extent, combined
with a tight `ContentSize.Y` (e.g. `HealthWindow.RowHeight`) leaving no slack. Separately: `textEnd`
(`textStart + textSize.X`) is where the right-hand divider line starts immediately, with no gap against
the label -- move it further right (a small fixed or width-fraction-relative pad before `rightEdge >
textEnd`'s line-drawing) so the line doesn't sit flush against the text.

#### Diagonal movement input timing

`PlayerMovementController.HandleInput` only treats a move as diagonal if both keys are down in the
exact same poll -- a few-frame gap between W and D lands as cardinal. Needs a short input-buffering
window before committing to a cardinal move.

#### Targeting tile highlights extend beyond the actual spell/scroll range

`ActionTargetingController.ComputeTargetableTiles` approximates every cursor-directed shape as a
`Burst` scatter (no cursor direction yet at arm time) -- overshoots for Cone/Line, showing tiles as
targetable that the real shape could never hit. Confirmed in-game. Needs a real per-shape reachable-area
computation, at least for Cone/Line.

#### Confirming activation on an empty tile still fires the spell/scroll

`TryConfirmActivationAtTile` only checks the clicked tile is in `TargetableTiles`, never whether the
resolved footprint actually contains an occupant -- clicking empty space still consumes a charge/mana
for no effect. Needs an occupant check, at minimum for `SingleTarget`/`Adjacent`; an AOE shape landing
empty but catching something else in its footprint is a separate case to decide deliberately.

#### Minimap + Fog of War, folded into Neighborhood/Borough zoom

Collapsed minimap (bottom-right); expanding it takes over the zoom-out feature rather than living
alongside it. Shares work with `MapCamera`'s `Neighborhood`/`Borough` zoom levels, which should match
the processing-tier regions (a 1024x1024 neighborhood, and the 3072x3072 3x3 window as Borough --
today's 1000x1000/2000x2000 predate them) -- static structures + boss/landmark
sprites only, no moving entities, snapping to preset regions instead of following the player.
Fog of war is its own item below.

#### Fog of War

Hides what the player can't currently see. Beyond its gameplay value, it's the visual half of
the frozen Borough. Without it, scrolling the camera into Borough shows frozen
creatures, which is Minecraft's well-known visual complaint. RTS games (StarCraft, Age of Empires)
are the standard model.

- **Three states per tile:** *unexplored* (blank), *explored but not visible* (terrain and static
  structures/landmarks, no creatures), and *visible* (everything). Explored stays explored, and only
  visibility is recomputed. (This settles the earlier "re-fog, undecided" note from the minimap
  item, unless play says otherwise.)
- **Visible is always a subset of Local.** A sight radius or line of sight around the player, never
  larger than Local's radius, so nothing visible is ever throttled or frozen. Recomputed on player
  move, and on terrain changes that block sight.
- **Explored-but-not-visible shows current terrain, not a last-seen snapshot,** to start. Flyweight
  terrain (plan section 1) makes that a definition lookup. A snapshot (terrain changed in fog still
  looks unchanged) is the RTS behavior. Revisit if hiding changes matters for gameplay (e.g. a wall
  destroyed out of sight).
- **Storage:** explored is a bitset per chunk per MapLayer, 1024x1024 bits = 128 KB per layer and
  ~384 KB per neighborhood. That's cheap enough to be flat rather than the per-region approximation
  the old note worried about, and it's saved with the chunk. Visible is small and only exists around
  the player.
- **No information leaks through the UI:** hidden creatures can't be hovered, selected, inspected
  (`SelectionWindowContent`, tooltips) or targeted. Targeting outside Local is already refused under
  the plan; fog extends the same refusal to "inside Local but not visible".
- **Rendering:** `MapWindow` skips occupants outside the visible set, and the terrain/background
  caches (`MapTileLayerCache`) draw unexplored tiles as blank and invalidate on reveal. The minimap
  and zoom-out levels read explored only.
- **Admin Mode (F12) bypasses fog.**
- **Light sources:** Scroll of Torch's Light grant is meant to become a fog-of-war reveal (see "Torch
  reveal + light-weakness damage" above). Design the visible set so a light source can add to it.

#### Magic Menu

Spell-equivalent of the inventory menu, mirroring `InventoryWindowController`'s Button+pooled-Window+
`TabbedContent` pattern. "Known spells" isn't a tracked concept -- just a `MultiComponentPool<ActionInstanceComponent>`
query filtered by `Tag.Spell` (same drift risk as Tag.Spell above).

#### Comprehensive control-selection feature

`UiInputController.SetFocus`'s `NextFocusableDescendant` redirect (focusing a window with a `TextBox`
child jumps into the TextBox) was piggybacked on by both the quest composer and the Inventory search
box, causing two confirmed bugs (a resize/move drag on the Inventory window spuriously refocused its
search box) -- fixed narrowly (`HandleMousePress` only resolves focus for a plain click, never
Move/Resize), which also quietly removed the quest composer's "drag title bar to focus" convenience.
Needs a real, explicit design: click-to-focus scoped strictly to a direct hit; or a window-declared
"default control" focused on `Initialize`; or keep the redirect but gate it explicitly and give
Move/Resize an opt-in.

#### Inventory grid item badge clarity

`InventoryItemStackCell` has accumulated several badges (quantity-or-charges number, Merged-Stack "+",
expanded-group border) with no deliberate pass on how they read together. Confirmed real ambiguity: the
bottom-right number silently means "how many I have" vs. "uses left" with zero visual distinction
(`HotbarContent` has the identical ambiguity, same fix should cover both). No redesign specified --
needs a deliberate look once there's room to design against.

#### Button tooltips

No icon/symbol-only button explains itself on hover. Needs the existing `Tooltip` pattern on: every
`Window` title button, the Inventory/Ability Score folder tiles, the Notification/Inventory folder
icons. Mechanical -- reusing an existing pattern in a few more places.

### Low Priority

#### Split Presentation into Presentation + UIEngine projects

Mirrors the Engine->Game split one layer up. Dependency rule: `UIEngine` references only `Engine`;
`Presentation` references `UIEngine`/`Engine`/`Game`; never the reverse. Clearly-`UIEngine` and
clearly-stays-`Presentation` sets are both fairly obvious (generic Element/window framework vs.
game-specific concrete windows). The hard part: `UiInputController` is mostly generic but has
game-specific branches (`HotbarController`/`ActionTargetingController`/drag-payload type-matching) woven
directly into its methods -- needs those pulled behind a `UIEngine`-defined hook `Presentation`
implements, not a straight file move. `ColorPalettes`/`Chrome` need the same judgment call. No
migration plan designed -- purely a scoping note, low priority until UIEngine-shaped reuse becomes real.

#### Abstract element pool factory registration

Every `RegisterFactory<T>` call hand-writes the same `FontService`/`ElementPoolService`/`LabelRenderer`
triple with only a few type-specific extras varying. Worth a helper taking just the extras. Boilerplate,
not error-prone -- low priority.

#### Red X marker over dead entities

`MapWindow.TryDrawEntityVisual` has no `DeadComponent` check -- a corpse looks identical to a
just-motionless living entity. Draw a red X overlay when `DeadComponent` is present.

#### Blood pool under dead entities

A blood pool drawn under a corpse on every tile of its footprint (`TransformComponent.Size`, same
per-tile loop `DrawFollowedEntityHighlight` uses), colored per entity. Nothing carries a blood color
today -- add one per race blueprint (a field or small component, e.g. red for Human/Goblin), and
decide what a Ghost leaves (none, or ectoplasm). Draw it under the corpse in the occupant pass
(`DrawUnderlayOccupants`, before the corpse sprite), not after. Pairs with Red X marker above (both
make a corpse read as dead) and Corpse decay/destruction (Game) -- the pool should go when the
corpse does.

#### Folder glow blink

`Folder.SetGlow` (used by `NotificationCenter`'s unread-glow) is flat on/off. Make it pulse instead --
more noticeable, especially once more things drive glow (Magic Menu, Skills leveling).

#### "Open many" button for stacked achievement notifications

`NotificationCenter`'s per-category unread count (`_unreadByCategory`) only opens one notification at a
time (`OpenNextNotification`). Add a button that instead opens as many non-overlapping achievement
windows at once as will fit on screen (cascade/tile placement, cf. `WindowCascadePlacement`), so a
backlog of unread achievements doesn't have to be worked through one popup at a time.

#### Highlighted-tile visual redesign -- pick one of two directions

`DrawMaskedTileHighlight` draws every highlighted tile as one uniform translucent wash today (an
earlier opaque-border version was deliberately replaced so the sprite stays visible). Two competing
follow-up directions, worth deciding between rather than landing both: (1) add back a thin 100%-opacity
border ring on top of the wash; (2) make the wash fainter and replace the ring with four opaque corner
brackets instead of a full perimeter. No corner-mark geometry worked out for (2) yet.

#### Circle selection under the entity instead of a tile border

Inspired by Dungeon Settlers. `MapWindow.DrawSelectedTileGlow` marks the selected/followed tile with
an interior-fade glow over the whole tile. Replace it with a circle (ellipse) on the ground under the
entity, drawn *before* the entity sprite rather than on top. That needs it inside the occupant pass,
not after it the way highlights draw today. Pairs with the tall-sprite item (High, above): once
sprites extend past their own tile, a tile-shaped highlight stops lining up with the character. May
settle the Highlighted-tile visual redesign above for selection specifically, but not for ability
targeting. There's no circle primitive yet -- `unitRectangle` plus `GlowRenderer` are all the map has.

#### Extract a shared tick-fraction HUD bar element

`PlayerHealthBarContent`/`PlayerManaBarContent` are near-duplicates (same outline+inset-fill+tick-mark
shape, differing only in backing component/palette). Tolerable at two copies -- abstract into one
generic element if a third shows up (e.g. Soul Essence). `MapWindow.DrawHealthBar` is arguably a lighter
third instance already (same fraction math, no ticks, per-any-entity) -- include it in scope if this is
ever picked up.

#### Hotbar insufficient-mana indicator

A small blue fill bar on a hotbar slot for an action/spell whose `ManaCost` exceeds the player's
current `ManaComponent` pool -- a glanceable "you can't afford this right now" without opening a
tooltip. Precedent: `RadialFillRenderer`'s existing cooldown-sweep mask, already drawn on
`HotbarContent`'s ability slots (`ActionLockContent`'s own HUD wheel is the other consumer today) --
this would be a second, independent fill/tint on the same slot, not a replacement, since cooldown and
affordability are two separate reasons a slot can't be used right now and both are worth signaling at
once. Blocked on Mana costs actually being enforced on activation -- this file's own "Mana" entry
(Game, Medium Priority) notes every `ManaCost` is unenforced today ("both free today"); this
indicator has nothing real to check until that lands.

#### Context menu amount picker

`CurrencyRowContent`'s Give/Take (and their "All" variants)
always move a currency's *entire* balance; a currency element dragged onto another entity's grid/row
does the same. Add a textbox popup (reusing `TextBox`, same mechanism `TextBox context menu wiring`
above wants for Cut/Copy/Paste) letting the player specify a partial amount instead, both for the
context menu options and (harder -- needs a way to intercept a drag-drop before it resolves) a
partial-amount drag.

#### Mark items as Sell ("junk"), and a bulk-sell-tab button in shop mode

A per-stack "Sell" marking -- the bulk-sale equivalent of other games' "junk" flag -- likely a new
`InventoryItemStackComponent` field alongside the existing `IsDisabled` one. While a shop is open
(`MapViewState.OpenShopEntityId`), add a button to the player's own inventory tab (`GridControl`/
`InventoryTabContent`) that sells every Sell-marked, currently-eligible item in the *active* tab
through `ShopActions.TrySellToShop` in one action -- any tab, not only a
dedicated "Sell" tab; marking curates what a sweep picks up, it isn't itself a tab requirement.

#### Per-entity sprite scale

`SpriteRenderer.Draw` always stretches to fill the tile footprint exactly -- wrong for character
sprites (confirmed in-game: player needs to render larger, goblins smaller). Needs a per-entity/
per-`SpriteComponent` scale factor applied in `MapWindow.TryDrawEntityVisual`.

#### Multi-tile sprites

No entity's sprite spans more than one tile today -- `TransformComponent.Size` already carries a
footprint (e.g. a corpse/tiny-entity grid already reasons about it), but `MapWindow`'s draw path
always renders one sprite stretched to exactly one tile's own `CurrentTileSize`, never a single
sprite spanning the whole footprint. `Shop`'s own `Sprite = "Shop-1x1"` is a
deliberately-named 1x1 placeholder for this -- a real multi-tile shop sprite (e.g. "Shop-2x2") is
the concrete first implementation once this lands.

#### Player stats v2

Let the player choose which stats to display. Follow-on to Player stats v1.

#### EndOfLevelStairs (Presentation)

Rendering/interaction for EndOfLevelStairs. See the matching Game item.

#### Digger detection (Presentation)

Part of MapLayer interaction (overview, Game); needs Digger detection (Game) first. On the Ground view,
low-level detection draws a "disturbed earth" marker over the digger's footprint, and high-level detection
draws its sprite partially transparent (`TryDrawEntityVisual`'s `alphaMultiplier`, already used for
Phasing). Undetected draws nothing, including no `v` layer badge, and viewing the UnderGround layer
directly (Page Down) must not bypass it outside Admin Mode. Needs a disturbed-earth sprite in
`Content/SpriteManifest.json`.

#### Equipment menu

Side-by-side with inventory, collapsible either direction, click-and-drag equipping. Pauses while open
-- just call `layers.OpenMenuWindow(window)`/`CloseMenuWindow(window)` (see Pause modality,
`IMPLEMENTATION-NOTES.md`), no new modality code needed.

#### Player health bar hover -- per-body-part HP dropdown

Hovering the player's health bar would show a small popup: total % first, then one line per body part
(name + current/max %), reusing the existing delay-gated `HoverPopupWindow` pattern. A `SimpleHealth`
player (today, always) has nothing to show beyond the total line until the player race is ever made
Complex. Lighter-weight than `HealthWindow` (`IMPLEMENTATION-NOTES.md`) -- a glanceable hover, not a
full window. Worth sharing one "format a body part's HP line" helper with `HealthWindow` once both
exist.

#### Text input undo/redo (Ctrl+Z/Ctrl+Y)

Deliberately left out of Text Input Enhanced Features (`IMPLEMENTATION-NOTES.md`) -- needs a real
edit-history design (edit stack or snapshots, coalescing rules, a depth cap), and it's not yet clear
whether the history should live on `TextBox` or a shared primitive a future second editable control
would also want.

#### WrapContent parent sizing collapses when a child resizes itself after attach

Discovered building the quest composer: a `WrapContent` window whose size depends on a child, paired
with a child that resizes *itself* later (not at attach time), collapses both toward `(0,0)`. Root
cause: `Window.Measure` unconditionally overwrites a child's `MaximumSize` with
`_parentWindow.ContentSize - RelativePosition` every pass -- circular for a `WrapContent` parent, whose
own `ContentSize` starts at `(0,0)` and is derived from the same children. Today's workaround (quest
composer stays `Fixed`, explicitly resized off the TextBox's `Resized` event) doesn't generalize. Real
fix likely: a child's own explicitly-authored `MaximumSize` should take precedence over a not-yet-settled
parent `ContentSize` when the parent is itself `WrapContent` mid-resolution. Touches the shared
Measure/Arrange pipeline -- worth a real design pass, not a quick patch.

#### Selectable/copyable read-only text -- move selection out of TextBox into TextWindow

`TextBox`'s selection machinery (hit-testing, double/triple-click, click-drag, Ctrl+A, Ctrl+C) is built
against `TextWindow`'s wrap/display, not anything editing-specific -- none of it needs a caret or
typing. Worth moving selection+copy up onto `TextWindow` itself (gated so a plain `TextWindow` never
shows a caret/accepts input), leaving `TextBox` to extend it with just caret/editing. Investigated
extending the same idea to `Window.TitleText`: no -- title text is a separate, simpler raw-string
mechanism not built on `TextWindow` at all; rebuilding it on shared infra to support copying mostly-short
static labels is a much bigger change for low value.

#### Scroll buttons for narrow content

For narrow scrollable content with no room for a scrollbar (see IMPLEMENTATION-NOTES.md "Scrollbars"), e.g.
TabbedContent's tab strip, which is only `TabHeaderHeight` tall: give up a little content space at each
end for a pair of scroll buttons (◀ ▶ / ▲ ▼) instead. Such an element would use
`ScrollbarVisibility.Hidden` today. Show the buttons only while that axis overflows, disable each one at
its end of the range, and scroll a step per click, repeating while held.

#### Review MapWindow for properties that belong on MapViewState instead

MapWindow has accumulated its own instance fields (camera/zoom state, hotkey bookkeeping, hover
buffers) alongside `MapViewState`, the established home for state other windows/content read. Worth a
pass checking whether any should move, particularly as more Presentation work needs to read that state.

#### Window minimize completeness

Two standing gaps: minimized windows don't hide/show their children (still draw underneath); sibling
windows in a tiled parent don't retile when one minimizes/restores (same class of bug already fixed for
add/remove via `RetileChildrenFrom`, not yet extended to `SetWindowDisplayMode`).

#### Window docking / splitters

No way to resize the boundary between two adjacent panes, or dock a window to the screen/another
window's edge.

#### Window open/close/minimize animation

Everything snaps instantly. Pure polish, lowest priority UI item.

#### Options menu

No settings screen exists -- Escape currently does nothing. Wanted: Escape (global, unconditional, same
as Tab) opens it, and the game pauses while open -- just `OpenMenuWindow`/`CloseMenuWindow` (see Pause
modality, `IMPLEMENTATION-NOTES.md`), no new modality code needed.

#### Keybindings page on the options menu

Needs Options menu (above) to live in, and Standard widget set (needs at least something list-like) --
today's hotkeys are hardcoded in `MapWindow.OnHotkeysAction`/`UiInputController`. Would eventually want
persisted storage for rebinds (see Data storage under Global, which today only covers window geometry).

#### Targeted key-press routing instead of a full-keyboard scan

`RouteKeyPressesToFocusedWindow` calls `KeyboardState.GetPressedKeys()` every frame a window is focused
(confirmed via reflection: FNA has no non-allocating variant) -- allocates every frame for the session.
`HandleKeyPress` has exactly one real consumer (`TextBox`, caring only about Backspace). Let the focused
content declare the small key set it actually wants checked instead of scanning/diffing the whole
keyboard.

#### Chat and speech

Glowing per-NPC speech bubbles (clickable for the full line), separate from a WoW-style configurable
chat log (Loot/Combat/Local Chat/Notifications tabs, user-routable message types). `NotificationCenter`
is the closest precedent but is popup-shaped, not a persistent scrollback -- a different, bigger widget.

#### Visual improvement pass

Dedicated sizing/placement/color pass across Presentation once the HUD stops churning -- today's values
(`HudMetrics`, scattered per-content constants) were each chosen locally.

#### Investigate mask-based recoloring for shared sprites (potions as the case)

Every potion needs a fully-authored sprite even though most differ only by liquid color.
`SpriteManifest`/`SpriteSheetService` have no tinting concept. Worth investigating a mask (grayscale/
alpha region marking recolorable pixels) + a `Color` field `SpriteRenderer` tints per-instance, instead
of a duplicate sprite per color variant. Generalizes to any other "one silhouette, many colors" case
(dyed equipment, faction banners).

## Global

### High Priority

#### Data storage, starting with window locations and sizes

No serialization/save-and-load system exists anywhere. Window layout
(`WindowRelativePosition`/`WindowCurrentSize`/`WindowDisplay`) is the first concrete use case -- every
launch starts from whatever `ShellBootstrapper` hardcodes. Once real per-window saved positions exist, a
manually-dragged window's saved position must always win over `WindowCascadePlacement`'s
always-cascade-and-clamp default (today's default exists *because* saved positions don't yet) -- keyed
per logical window/slot, not globally (3 customized comparison columns keep their spots; a 4th still
falls back to cascade placement).

Treat as the first slice of a general data-storage system (entity/world save state will eventually need
the same serialize-to-disk mechanism) -- but start narrow; window geometry has no cross-entity
references to untangle.

**Modded content must degrade gracefully, not corrupt a save.** Once entity/world state (inventory
items, granted abilities, `IActionActivator`/`ActionEffect` catalog entries) is serialized, a saved `Guid` reference to mod-defined content can go
stale if that mod changes before the save reloads (RimWorld/PoE's well-known failure mode). Fail
hierarchy, decided up front: (1) prefer a mod-supplied replacement/migration, (2) fall back to dropping
just the affected reference while the rest of the save loads, (3) last resort, drop the whole entity if
the missing content is load-bearing for it. Consider letting a mod register its own fallback id per
content id it defines.

#### Save and load Beyond neighborhoods

Today a neighborhood evicted from the window's cache
is deleted: its `NeighborhoodRecord` survives, so returning regenerates the same layout with a fresh
population. Instead, write the neighborhood to disk on unload and read it back on return: the record
plus its contents (entities with their full component sets, and any terrain or structure changes
since generation), so a revisited neighborhood holds the same creatures, corpses and loot.

- **Depends on** "Data storage" above (the serialize-to-disk mechanism) and "Entity storage" below
  (snapshot and rehydrate an entity's exact component set).
- **References:** entities come back with new ids; anything that points at another entity already
  holds its `EntityKey`, which is saved with it (plan decision 17). Crawler numbers are kept, never
  re-minted.
- **Time:** reloading catches up active timers the same way a Borough promotion does; whether more
  than that advances with elapsed time is the open question in "Unsimulated-tier time".
- **Format:** "record plus changes" for neighborhoods the player never touched, a full snapshot
  otherwise. Parsing runs on the streamer's worker, the way generation does (`TestMapBuilder.Plan`; see
  "Asynchronous neighborhood generation" in `IMPLEMENTATION-NOTES.md`).
- **Spawn records** hold a session-local blueprint id: persist the blueprint's Guid instead, and for
  a composite interned at runtime (`BlueprintRegistry.Compose`) its ordered include Guids, so loading can
  re-intern it.
- **Applied blueprints** (`AppliedBlueprintComponent`): persist each entry's blueprint Guid and order
  beside the spawn record -- together they are everything the entity was built from. A crawler's number
  is persisted once assigned; an unbuilt crawler has only its `SpawnFlags.Crawler` flag.

#### CI step for the performance-filtered tests

`Tests.csproj` defaults `VSTestTestCaseFilter` to `TestCategory!=Performance`, so an unfiltered `dotnet
test` no longer runs `AbilityScorePerformanceTests`. Nothing runs them automatically now, and a real
scaling regression -- per-call cost growing with population, which is the only thing those two tests
exist to catch -- would land unnoticed. They need their own step: `dotnet test Tests/Tests.csproj
--filter "TestCategory=Performance"`.

There is no CI anywhere in the repo today (no `.github/workflows`, no pipeline file), so this starts
with standing one up: build the solution, run the ordinary suite, then the performance filter.

- **Separate step, not parallel with the ordinary suite.** Contention between the two is exactly why
  the filter default exists -- the ratio assertions flaked when both ran at once on this machine.
- **If it flakes on a shared runner, raise the ceiling, don't drop the step.**
  `MaxGrantDefaultsScalingRatio`/`MaxExpiryRecomputeScalingRatio` are 20 against a linear ~10; a noisy
  neighbour can push a 10x run past that. The assertion's job is failing quadratic cost, not measuring
  absolute speed, so a looser CI ceiling still does that job.
- Unrelated to the `phase-performance-testing` skill's whole-game per-system benchmark, which needs a
  real run and a fixed seed rather than a test filter.

### Low Priority

#### Debug/event logging with levels

`Game/Diagnostics/PlayerActivityLog.cs` is a narrow, single-purpose EventBus subscriber (Burning
damage/moves to a file), deliberately not a general logging facility. Worth a real design (log
levels, a generic "subscribe any event to a log line" mechanism, configurable sinks) once more than one
thing wants to log. See Entity storage below for a narrower, related need.

#### Entity storage -- suspend an entity from processing without per-system checks

`World.RemoveEntityFromMap` already carries an inline TODO: it zeroes `TransformComponent.Position`
when unregistering from Map's spatial index, losing where the entity was. Broader gap: taking an entity
out of *every* system's processing, not just map occupancy, without each system needing its own check.
`ComponentManager.RemoveAllComponents` already does the mechanical half (drops entityId from every
pool, which is what actually keeps `EntityStripeSet`/`TieredEntityStripeSet` from revisiting it) but
doesn't preserve what was removed -- usable today only for a real despawn, not "store and restore."

Needs: snapshot an entity's full component set into serializable form, remove it the same way
`RemoveAllComponents` does, later rehydrate exactly what it had (not a fresh blueprint instance). Two
open questions: does this ride the general save/load system (Data storage above) or start as a narrower
same-session freeze/thaw first; how do cross-entity references (an equipped item's owner, a pet's
bonded player) stay valid across a storage/restore cycle (same class of problem Data storage's modded-
content section already raises for saves generally). Motivating case: a tamed companion or caged NPC
that can leave and rejoin the active simulation with its exact accumulated state intact.

#### Field and property cleanup

General pass once UI/core systems stop churning -- auto-properties with no logic that could be plain
fields, or the reverse, plus consistency in when a type uses plain fields (see
`WindowGeometryState`/`WindowTitleState`/etc.'s own doc comments) vs. properties. Housekeeping, not a
bug list.

#### Solution-wide code style cleanup

Conventions clarified while building the focus/keyboard-routing system, not retroactively applied
elsewhere: comments explain WHY only when genuinely non-obvious; ternaries on three lines (condition,
`?` branch, `:` branch); one return per method except leading guard clauses.
`UiInputController.cs`/`Window.cs`/`MapWindow.cs` follow these only where actually touched by that work
-- pre-existing code in the same files, and everywhere else, predates them. Related to Field and
property cleanup above -- possibly the same pass.

#### Possible future UI gaps, likely out of scope

Tooltips (mostly landed already elsewhere), localization/IME, and accessibility (screen reader) hooks
are standard in general GUI frameworks, but this is an admin/debug UI over a game world, not a general
application shell. Noted for completeness, not expected soon.

#### DungeonCrawlerWorldAdvertisement joke website + in-show ad

Meta joke, not in-engine content: a standalone `DungeonCrawlerWorldAdvertisement` website that makes fun
of and rickrolls the visitor. The Crawler TV show (Game, above) would air an in-universe ad for it;
navigating to the advertised URL (typed or via an in-show QR code) is the payoff. Two separate
deliverables (the site itself, and the in-show ad segment) -- the site has no dependency on the game
engine at all.

## Accessibility

### Low Priority

#### Read the OS double-click speed instead of a hardcoded window

`InventoryGridContent.DoubleClickWindowFrames` (inventory item double-click-to-activate) is a fixed
constant, not the user's actual configured Windows double-click speed (Control Panel/Settings ->
Mouse, default 500ms) -- unlike WinForms/WPF apps, which read this via
`SystemInformation.DoubleClickTime`. Retrievable without a `System.Windows.Forms` dependency (which
would force retargeting to `net10.0-windows`) via a direct `user32.dll` P/Invoke of
`GetDoubleClickTime()`, callable at startup since it's a system-wide query with no window-handle
dependency. Windows-only -- FNA itself is cross-platform, so this needs an `OperatingSystem.IsWindows()`
guard with today's hardcoded value as the non-Windows fallback. Scoped to the mouse double-click only;
`ActionTargetingController`'s separate keyboard hotbar double-tap window is a different gesture and
wouldn't read from this. Open question if picked up: does the current +25% buffer on top of the base
value still make sense once the base is the user's own real OS setting rather than a fixed guess.

### MEDIUM PRIORITY : Unsimulated-tier time -- research alternatives to freezing

Decided 2026-09-11 as a temporary position: when P2 stops simulating Borough and beyond, entities
there are **frozen** -- their timers stop and resume where they left off when they are next
simulated. That is the cheapest correct option, not
necessarily the right one: from the player's point of view, time does not pass for anything they
walked away from.

**Partly superseded 2026-09-15 by the world-scaling work:** Borough is frozen, but when an entity
is promoted out of it, every effect with an active timer **catches up** to now rather than resuming
where it left off. Interacting state (combat) doesn't catch up. What remains open here is **saved
Beyond neighborhoods**: whether reloading one from disk uses the same catch-up, and whether anything
beyond active timers (regen to full, crop-like growth, population drift) should advance with
elapsed time.

Research how other games handle time for things outside the simulated area before committing to
anything else. Starting points to verify, not settled facts:
- Minecraft: chunks beyond simulation distance are frozen outright -- nothing ticks there.
- Stardew Valley: much off-screen state advances in bulk at the day boundary rather than live.
- Animal Crossing: catches up to the real-time clock on load.
- Dwarf Fortress: off-site world simulated at a coarser, abstracted level ("world activity").
- RimWorld: world-map caravans and settlements simulated abstractly, maps unloaded otherwise.
- Catch-up on load: advance a region by the elapsed time in one bulk step when it comes back into
  range (the approach that runs into the bulk-resolution problem: bulk ticks resolve in isolation,
  so an entity can die to a lump of damage that fine-grained simulation would have offset with regen).

Things to decide from that research: whether time passes at all, whether catch-up is exact, bulk,
or statistical, and whether lethal outcomes may resolve while unobserved. Overlaps with
"Third Pause modality" -- both are "what happens to time where the player isn't". Under the 3x3
neighborhood window,
this item governs saved Beyond neighborhoods (frozen, or caught up on reload), not Borough.

### MEDIUM PRIORITY : Third Pause modality -- per-map pause

Today pause is global: `GameLoop.Update` skips `EcsContext.Update` entirely when
`MapWindow.IsPaused` or menu mode is active, so every map stops together.

Wanted: pause everything on a specific map *without* pausing the sub-map the player is currently
on. The player explores a sub-map at full speed while the map they came from is frozen rather than
running unobserved.

Notes for whoever picks this up:

- This is a third state, not a boolean. The existing two are "everything runs" and "nothing runs";
  the new one is "this map runs, those maps are frozen", which means pause stops being a property
  of the game loop and becomes a property of a map.
- It overlaps heavily with the tier rework and with P2's
  spawn-in-and-wait direction: a frozen map and a Beyond-tier region are close to the same idea
  expressed at different granularity, and it would be a shame to build two mechanisms for it. The
  tier system already carries "this entity is on a different MapLayer, therefore Beyond".
- Interacts with the off-map player reference point: while the player is on a sub-map, other maps'
  entities are tiered against the player's last on-map position. If those maps are frozen anyway,
  the reference point matters less -- but a frozen map still needs a defined tier state for when it
  unfreezes.
- Decide what "frozen" means precisely: no `ISystem.Update` visits at all (Minecraft's simulation
  distance model), or visits that are skipped per-entity. The former is cheaper and easier to
  reason about.
- Clocks: the timer wheel uses one simulation clock, passed explicitly to everything that reads
  it, so this is a wiring change. A frozen map needs its own clock (or its deadlines parked the
  same way unsimulated-tier entities are); decide which when the second map exists.
