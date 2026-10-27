# NPC Behavior Composition

(Pre-implementation. Replaces TODO.md's "NPC behavior composition + generalized "turn claimed" signal"
entry, which is deleted when Phase 4 lands. Unblocks "NPCs use shops".)

**The split:** the engine (`Cadence/Engine`) owns the behavior *framework* -- what a behavior,
a grant, a modifier and a consideration are, how a set of grants is resolved, how one is chosen
(dual utility), and how a choice is kept (commitment). It knows nothing about goblins, health,
potions or the map. The game (`DungeonCrawlerWorld/Game`) provides the *pieces* -- the behaviors
themselves, the considerations, the drives and what feeds them, the perception they read -- and
*assigns* them to creatures through blueprints.

## Context

What exists today:
- `TestCombatBehaviorSystem` (`Game/Modules/NpcBehavior/Systems/`) is a hardcoded chain for every
  `MovementMode.Random` entity. The first branch that fires wins:
  1. Self-heal: below 50% health and holding a Health Potion.
  2. Melee: an adjacent entity of a different race; QuickAttack or PowerAttack on a coin flip,
     skipped if `ActivationQueries.GetBlocker` refuses it.
  3. Ranged: Magic Missile at the player when in range and not adjacent; Target or Ground mode on a
     coin flip (temporary: only at the player, see TODO "Spatial queries for NPC decisions").
  4. Loot a corpse on or beside it (`NpcCorpseLooting`, with its 30 s `CorpseLootRetryComponent`).
  5. Wander: a coin flip between idle (2 s `WaitUntilFrame`) and a random adjacent step.
- It's an `ITieredSystem` with the same `StripeCount` (15) and tier divisors as `MovementSystem`,
  wired to the same `MovementComponent` pool, so an entity is decided and moved on the same frame.
  It was once the largest per-frame cost in the game, and tiering fixed that. A replacement has to
  be at least as cheap.
- `NpcBehaviorModule` declares `RunsBefore = [MovementModule.ModuleId]`. The activation systems drain
  `PendingActionActivationComponent`/`PendingItemActivationComponent` later in the same frame.
- **The "turn claimed" half of the TODO entry is out of date.** `MovementSystem` no longer checks the
  pending pools. The rule now is "queuing an activation clears the step": `TestCombatBehaviorSystem`
  calls `ClearStep` after each queue, and `PlayerCommands` sets `NextMapPosition` to null before it
  writes a pending activation. It holds, but by convention in two places, not by structure.
- `TestDummyAttackSystem` is a second decision system: a plain `ISystem` (stripe 1) that fires Power
  Attack at its Adjacent ring whenever its action lock clears. It stays separate (a decision below).
- Hostility is "has a different primary race" (`IsAttackable`). There are no factions.
- Blueprints are still C# static classes. Actions, items and auras are content (`Items.jsonl`,
  `Auras.jsonl`, `Actions.jsonl`) with codecs, and blueprints become content later (the data-driven
  plan's follow-up "Terrain and blueprints as data").
- Toggles have landed: Toxic Idol is an `Items.jsonl` record with a `ToggleItem` activator and an
  `AuraSourceGrant` naming a `Auras.jsonl` aura.
- The engine has the pieces the framework builds on: `Catalog<T>` (Guid-keyed, redirects),
  `GameplayTag`/`GameplayTagSet` (declared by modules, parent-aware), `MathUtility`/`SeededRandom`,
  `FrameDeadline`, and Packed pools any module can register.

Related TODO items that this plan does not need, but which will improve its behaviors later:
"Grid pathfinding and navigation" (Engage/Flee step one tile and can get cornered), "Field of view
and perception" (behaviors count hostiles through walls), and "Spatial queries for NPC decisions"
(where to flee to, ranged targets other than the player).

## Research: how other games do it

| Game / technique | Arbitration | Per-type configuration | Multi-turn intent | Fit here |
|---|---|---|---|---|
| **NetHack** (`monmove.c`, `muse.c`) | Fixed code chain: `find_defensive` -> `find_offensive` -> `find_misc` -> move | Monster flags gate branches | None | What `TestCombatBehaviorSystem` is today |
| **DCSS / Brogue** | A behavior state (wander/seek/flee) plus per-ability conditions | Data flags per monster type (`fleesNearDeath`, `MAINTAINS_DISTANCE`) | The state persists | Data-driven per-race flags over a small fixed set of behaviors |
| **RimWorld** ThinkTree | `ThinkNode_Priority` takes the first child that issues a job; `ThinkNode_PrioritySorter` sorts by computed priority; a "constant" tree interrupts | `ThinkTreeDef` XML per race/kind; mods insert subtrees by tag | A `Job` runs over many ticks until done or interrupted | Per-race configuration and "one current job, interruptible" |
| **Caves of Qud** `Brain` | A goal stack; when empty, AI parts (`AISelfPreservation`, `AIShootAndScoot`, ...) push goals | AI parts attached in blueprint XML | The goal stack | Closest to "compose behaviors on the blueprint"; per-creature goal objects are too costly at our scale |
| **Utility AI / IAUS** (Dave Mark, GDC 2013/2015) | Every behavior scores from multiplied considerations; highest wins | Weights and curves per archetype | None | Personalities as weights; no early exit |
| **Dual utility** (Kevin Dill, *Game AI Pro*) | Rank (priority bucket) + weight; highest non-empty rank wins, weighted pick inside it | Ranks and weights as data | None | Early exit of a priority chain plus trade-offs where they matter |
| **Unreal** (TODO.md's reference) | Behavior Trees + Blackboard, StateTree; GAS gates abilities by owned/blocked tags; Mass LOD processors | Assets | StateTree states | GAS tags as a "turn claimed" model; Mass LOD confirms processing tiers |
| **Behavior trees / GOAP** (Halo 2 / F.E.A.R.) | Tree traversal / A* planning | Assets | Running nodes / plans | Overkill now; GOAP planning is out of the question at our entity counts |

The recurring pattern: **stateless behavior logic + per-type data selecting and weighting it + the
decision writing an intent that separate systems execute.** That pattern is also the split: the
selection machinery is generic, the logic and the data are game content.

## Engine and game: who owns what

| Piece | Owner | Where |
|---|---|---|
| `IBehavior<TContext>`, `BehaviorEvaluation` | Engine | `Cadence/Engine/Decisions` |
| `BehaviorGrant<TContext>`, `BehaviorModifier`, `IBehaviorConsideration<TContext>` | Engine | `Engine.Decisions` |
| `IDecisionContext` (what the arbiter needs from any context) | Engine | `Engine.Decisions` |
| `BehaviorCatalog<TContext>` (Guid catalog + session-local `ushort` ids) | Engine | `Engine.Decisions` |
| `BehaviorSet<TContext>` (resolve grants + modifiers, sort by rank ceiling) | Engine | `Engine.Decisions` |
| `BehaviorArbiter<TContext>` (dual utility, cutoff, weighted pick, commitment) | Engine | `Engine.Decisions` |
| `BehaviorCommitment` (8-byte struct, stored in a pool the game registers) | Engine | `Engine.Decisions` |
| `DecisionTrace` (one inspected decision, layer by layer) | Engine | `Engine.Decisions` |
| `NpcDecisionContext` (perception cache, intent methods, drive lookup) | Game | `Game/Modules/NpcBehavior` |
| The behaviors (SelfHeal, EngageMelee, RangedAttack, LootCorpse, Flee, WanderStep, Idle) | Game | `Game/Modules/NpcBehavior/Behaviors` |
| The considerations (`NearbyAlliesFactor`) | Game | `Game/Modules/NpcBehavior/Considerations` |
| Drive tags (`Behavior.Drive.Aggression`, `.Caution`) and their stat-modifier targets | Game | `NpcBehaviorModule.DeclareTags`, `StatModifierTarget` |
| Assigning grants and modifiers to creatures | Game | `BlueprintDefinition.Behaviors`/`BehaviorModifiers` |
| `NpcDecisionSystem` (tiered, gates, builds the context, calls the arbiter) | Game | `Game/Modules/NpcBehavior/Systems` |
| `IntentWriter` (the one way to queue a turn) | Game | `Game/Modules/Actions` |
| Dread Aura, Dread Idol | Game content | `Auras.jsonl`, `Items.jsonl` |

**Why the system stays in Game.** The arbiter is generic, but *when* an entity may decide (dead,
idle backoff, action lock, mid-move) and *who* decides (tiered over the movement pool, not the
player) are this game's rules. The engine offers the decision; the game decides when to ask.

**Naming.** The engine's module phase is already called `RegisterBehavior` (systems and handlers),
so the framework lives in `Engine.Decisions`, and behaviors are registered into a catalog in
`Configure` -- never in a phase of their own -- so "behavior" never means two things in one call.

`EngineRootArchitectureTests` already fails any engine reference to the game; the engine's tests
(below) use a fake context, so they prove the framework stands without the game.

## Part 1 -- one way to queue a turn

### Options

**A. Keep the convention.** Every writer clears the step itself (today's two places).
- Con: a third writer that forgets lets a creature step and act in one frame.

**B. `IntentWriter`, the one chokepoint (recommended).** Game, next to `ActionLockGate`:
`QueueAction(entityId, actionId, selection)`, `QueueItemActivation(entityId, stackInstanceId,
selection, activatedFromSlot)`, `Step(entityId, tile)`, `Idle(entityId, now, frames)`. Queuing an
activation writes the pending component *and* clears `NextMapPosition`; `Step` withdraws a pending
activation, as `PlayerCommands` does today. Behaviors never see a pending pool: `NpcDecisionContext`
exposes only these methods, so a behavior structurally can't double-book a turn.
- FreeCast actions don't clear the step: they don't wait for the lock, and a FreeCast plus a step
  on the same frame is legitimate (today's `PlayerCommands` rule, moved here).

**C. Owned gameplay tags, as Unreal's GAS does** (`ActivationOwnedTags`/`ActivationBlockedTags`): an
activation adds "turn claimed" to its owner, and anything can check it.
- Con: needs a per-entity tag container, which the engine doesn't have (`GameplayTagSet` is
  immutable per definition). Worth it when Stagger/Dodge rules want tags too, not for this.

The earlier frame stamp (`ActionLockComponent.TurnClaimedAtFrame`) is dropped: with `MovementSystem`
no longer reading pending pools and every queue clearing the step, nothing would read it.
`ActionLockComponent` stays 8 bytes.

### Performance: frame stamp vs. `IntentWriter`

Measured base (benchmark of 2026-10-25, seed 8153770, frames 1-600, 662,885 living entities):
`ActionLockComponent` has 73,355 holders (8 B each, a 4.1 MB pool); `MovementSystem` costs
0.98 ms/frame and `TestCombatBehaviorSystem` 2.96 ms/frame.

| Cost | Frame stamp (B of the first version) | `IntentWriter` |
|---|---|---|
| Writes per turn | +1: claim the lock (a pool lookup and write) on top of today's queue and step clear | Same as today: the pending component and the step clear, moved into one method |
| Reads per movement visit | +1 compare if folded into `IsBlocked`'s existing read of the lock; a second pool lookup if not | None |
| Memory | +4 B x 73,355 = +0.29 MB (the pool +7%) | None |
| Cache | Lock stride 8 -> 12 B: every system that reads the lock (movement, decisions, activations, HUD) touches 50% more cache lines for it | None |
| Work removed | None: the step must still be cleared, or a stale step runs on the first visit after the claim's frame | Two copies of the step-clearing rule become one |

Estimated size: about 73k built creatures each take a turn every 1-2 s, so roughly 600-1,200 turns a
frame at most. An extra write at 10-30 ns is 0.01-0.04 ms/frame, and the wider stride adds a few
percent to the lock reads inside the two systems above -- together on the order of 1% of their
4 ms/frame. **Both are small; the deciding point is that the stamp only adds (a field, a write, a
read) and removes nothing, while `IntentWriter` costs nothing at run time.** These are estimates;
Phase 3's A/B benchmark confirms `IntentWriter` is free.

## Part 2 -- the engine framework (`Engine.Decisions`)

### Behaviors, grants, modifiers, considerations

```csharp
namespace Engine.Decisions;

/// What the arbiter needs from any decision context. A game's context implements it (a ref struct is allowed).
public interface IDecisionContext
{
    long Now { get; }
    /// Uniform in [0, 1), from the caller's seeded sequence -- the arbiter never owns randomness.
    float NextUnitRandom();
    /// The runtime multiplier for a behavior carrying these tags; 1 when nothing applies.
    float RuntimeWeightMultiplier(GameplayTagSet behaviorTags);
}

public interface IBehavior<TContext> where TContext : IDecisionContext, allows ref struct
{
    Guid Id { get; }
    string Name { get; }
    /// What kind of behavior it is (e.g. Behavior.Drive.Caution) -- what modifiers and runtime multipliers target.
    GameplayTagSet Tags { get; }
    /// Rank <= grant.Rank (0 = doesn't apply) and an intrinsic weight >= 0 from the situation alone.
    BehaviorEvaluation Evaluate(ref TContext context, BehaviorGrant<TContext> grant);
    /// Writes the intent. Only called on the winner.
    void Act(ref TContext context, BehaviorGrant<TContext> grant);
}

public readonly record struct BehaviorEvaluation(byte Rank, float Weight);

public sealed record BehaviorGrant<TContext>(
    Guid BehaviorId,
    byte Rank,
    float Weight = 1f,
    float Parameter = 0f,
    ushort CommitFrames = 0,
    IReadOnlyList<IBehaviorConsideration<TContext>>? Considerations = null)
    where TContext : IDecisionContext, allows ref struct;

/// Multiplies the weight of every behavior carrying Tag (parent-aware), or of one behavior by id.
public sealed record BehaviorModifier(float WeightMultiplier, GameplayTag? Tag = null, Guid? BehaviorId = null);

public interface IBehaviorConsideration<TContext> where TContext : IDecisionContext, allows ref struct
{
    /// A multiplier >= 0. Returning 0 vetoes the candidate.
    float Evaluate(ref TContext context);
}
```

- **`Parameter`** is one generic number each behavior interprets (SelfHeal: the health threshold;
  Flee: the baseline panic). The engine never reads it.
- **Tags instead of an enum of drives.** The earlier plan's `BehaviorDrive` enum couldn't be extended
  by a mod. With `GameplayTag`, a module declares its drive tags in `DeclareTags` like any other tag,
  a mod's "Cower" behavior tagged `Behavior.Drive.Caution` is affected by everything that affects
  caution, and a mod can declare a new drive. A modifier targeting `Behavior.Drive` reaches every
  drive (parent-aware `Has`). See "Tag domains" for how drive tags are kept from mixing with the
  others.
- Grants, modifiers and considerations are immutable and shared by every creature that holds them.
  No per-entity memory.

### Tag domains: keeping one tag system from meaning everything

Tags already do many jobs here: classifying actions and items (`Action.*`, `Delivery.*`,
`Damage.*`, `Magic`), naming inventory tabs (`Item.*`), conditioning stat modifiers
(`StatModifierComponent.ConditionTag`), traits (`Trait.Dodgeable`), map areas (`Area.*`), and now
drives. The risk is real: a tag picked for one purpose silently matching a query written for another,
and roots growing without an owner.

**How other engines handle it:**

| Engine | Mechanism | Lesson |
|---|---|---|
| **Unreal GameplayTags** | One global hierarchical dictionary, but every property says which subtree it accepts: `meta = (Categories = "Damage")` filters the editor's picker (Blueprint-enforced, not C++). Tags come from per-plugin sources (`Config/Tags/*.ini`, data tables, native C++ declarations). *Restricted tags* list an owner, and say whether others may add children | The role of a tag comes from the field that holds it, and a root has an owner |
| **Unreal GAS** | The same tag means different things by container: `AbilityTags` (what it is), `ActivationOwnedTags` (what it grants while active), `ActivationBlockedTags` (what stops it) | A role is the container, not the tag; one tag can sit in several roles without confusion |
| **Large Unreal projects** (Jonas Reich, "Managing Gameplay Tag Complexity in ARPGs") | Root tags declared in code and owned by one plugin or system; whole trees declared natively (no repeated strings); a description on every tag; an `AllowContentChildTags` flag on the few tags designers may extend; *typed tags* that accept only one subtree, checked at compile time; a validator for naming, depth and per-root rules once there are more than a dozen tags; reuse a tag only for the *same* meaning, never a different one | Ownership, typed fields and a validator, not fewer tags |
| **Unity** | Built-in tags are a flat list with one tag per GameObject -- widely considered too limited, so projects use components or ScriptableObject "tags" instead | Without structure, people route around tags entirely |
| **Godot** | Groups: free-form strings, many per node, no hierarchy | Easy and unchecked; collisions are the project's problem |
| **Bevy / Unity DOTS** | Marker (zero-size) components: the type is the tag | Type safety by giving each role its own type |

**What this plan takes from them, applied to every tag role in Phase 1:**

#### Engine (`Engine.Tags`)

1. **Root ownership.** `GameplayTagDeclarations.DeclareRoot(root, description, extensible)`: the
   declaring module owns the root and everything under it. Another module (a mod included) may declare
   under it only where the root, or a tag below it, is marked extensible (Jonas Reich's
   `AllowContentChildTags`). Anything else fails the build, naming both modules. A replacement module
   inherits the replaced module's roots, as it inherits its settings.
2. **Domains.** `GameplayTagDomain` = a name plus the roots (or subtrees) it accepts, with
   `Accepts(tag)` and `EnsureAccepts(set, usedBy)`. A domain is declared by the module that owns it
   (`DeclareDomain`); an extensible domain lets other modules add a root to it (a mod's own damage
   family). The registry holds them; every tag-holding field names one.
3. **Content fields name their domain.** `ContentField.Tag`/`TagSet` take a domain. The reader fails a
   tag outside it (file, line and column, like every other content failure); the Workshop's generated
   inspector offers only that domain's tags in its picker, and lists violations in the Error List.
4. **A description per tag**, required at declaration, beside the display name. The Workshop shows it
   in the picker's tooltip.
5. **A validator** (`GameplayTagRegistry`, at the end of `DeclareTags`): every declared tag is under an
   owned root, depth at most 4 (today's deepest is 3: `Item.Consumable.Potion`), PascalCase segments.

#### Game: today's roots get owners

| Root | Owner (today: all Core) | Extensible by other modules | Why |
|---|---|---|---|
| `Action` | Actions | No | Classification code reads (`Action.Spell`, `Action.Attack`) |
| `Delivery` | Actions | No | Melee/Ranged gate body-part effects and Dodge |
| `Targeting` | Actions | No | `Targeting.Self` is checked exactly; a technical tag has no children |
| `Trait` | Actions | Yes | Dodgeable, Staggering; a mod's own trait is reasonable |
| `Effect` | Core (Effects) | Yes | `Effect.Healing`; new effect kinds are content |
| `Damage` | Health | Yes | A mod's new damage type is the classic extension |
| `Magic` | Core | No | A leaf, combined with others by design |
| `Stats` | AbilityScores | No | `Stats.AbilityScore.*` maps one-to-one to the seven scores in code |
| `Item` | Inventory | Yes | Each child is an inventory tab; a mod's new category gets a tab |
| `Area` | Areas | Yes | Area classification is content |
| `Behavior` | NpcBehavior | `Behavior.Drive` only | A mod's own drive |

#### Game: every tag field gets a domain

| Domain | Accepts | Fields |
|---|---|---|
| **Activation** ("what a use of something is") | `Action`, `Delivery`, `Targeting`, `Trait`, `Effect`, `Damage`, `Magic`, `Stats`, `Item` | `ActivatableDefinition.Tags` (actions and items), `IActionActivator.ImpliedTags`, `AuraDefinition.Tags`, `TerrainContact.Tags` -- each becomes the activator tags an effect is applied with |
| **Activation condition** | the Activation domain | `StatModifierGrant.ConditionTag` / `StatModifierComponent.ConditionTag`: a condition can only name something an activation can carry, so it can never be unmatchable |
| **Item category** | `Item` | Shop stock queries (`PotionShopStock.AcceptedItems`), inventory tabs (`InventoryTagQueries`, which filters by `GameTags.Item` today and reads the domain instead) |
| **Ability score** | `Stats.AbilityScore` | `AbilityScoreTagBonus`'s table |
| **Area** | `Area` | `AreaDefinition.Tags` |
| **Behavior** | `Behavior` | `IBehavior.Tags`, `BehaviorModifier.Tag` |

- Content codecs name their field's domain, so `Items.jsonl`, `Actions.jsonl`, `Auras.jsonl`, the area
  files and the area palettes are checked on read. `ContentTagValidation`, which today only checks that
  tags are declared, also checks each C#-declared definition's fields against their domains
  (blueprints' behavior grants, code-declared auras, terrain contacts), through
  `EffectContent.ForEachEntry` and `IEffectEntry.ReferencedTags` as it does now -- an entry reports
  each tag with its domain.
- Expected fallout: none in today's content, if every tag sits where its role expects. Any record the
  validator rejects is a real mistake, fixed in the content, not by widening a domain.

This keeps tags (as decided) and gives each one a single role: `Behavior.Drive.Caution` can't be put on
an item, `Area.Testing` can't condition a stat modifier, and `Damage.Fire` can't drive a behavior.

### Catalog and resolved sets

- **`BehaviorCatalog<TContext> : Catalog<IBehavior<TContext>>`**: re-registering a Guid replaces a
  behavior in place (how a mod replaces "Flee"), and redirects work as for any catalog. It also issues
  each behavior a session-local `ushort` id, which is what a commitment stores. Never persist one.
- **`BehaviorSet<TContext>.Resolve(catalog, grantLists, modifiers)`** turns the grant lists of a
  composition (in order) and every modifier into one immutable set:
  1. Merge: a later grant of the same behavior id replaces an earlier one; `Rank = 0` removes it.
  2. Fold modifiers: each multiplies the weight of every grant it targets. Multiplication commutes, so
     order doesn't matter; a modifier aimed at a behavior the set doesn't hold does nothing.
  3. Sort by rank ceiling, descending, and keep each entry's behavior instance and session id.
  4. An unknown behavior id throws, naming it -- the game's `ResolveAll` turns that into a failed dry
     run for a broken mod.
- The caller caches sets. The engine doesn't know what a blueprint is.

### The arbiter

`BehaviorArbiter<TContext>.Decide(ref context, set, in commitment, trace)` returns what it did and the
commitment to store. Its options (`BehaviorArbiterOptions`) are the game's: the cutoff fraction (0.25).

```
if commitment is live (deadline not reached, and set still holds that behavior):
    // 1. Interrupts: only grants ranked above the commitment are evaluated.
    run the loop below over grants with ceiling > committed.Rank
    if it produced a winner: act on it; done
    // 2. Continue: ask the committed behavior whether it still applies.
    if committed.Evaluate(...).Weight > 0: committed.Act(...); done   // no re-roll against rivals
    // 3. It ended early, so fall through to the full loop.

winnerRank = 0; candidates.Clear()
foreach entry in set (sorted by rank ceiling, descending):
    if entry.Rank < winnerRank: break                                  // early exit
    (rank, intrinsic) = entry.Behavior.Evaluate(ref context, entry.Grant)
    if intrinsic <= 0 or rank < winnerRank: continue
    weight = intrinsic * entry.ResolvedWeight                          // base x blueprint modifiers
           * Product(entry.Grant.Considerations, ref context)          // situational
           * context.RuntimeWeightMultiplier(entry.Behavior.Tags)      // runtime
    if weight <= 0: continue
    if rank > winnerRank: winnerRank = rank; candidates.Clear()
    candidates.Add(entry, weight)
drop candidates with weight < cutoff x best
pick = weighted random over candidates using context.NextUnitRandom()
pick.Behavior.Act(ref context, pick.Grant)
commitment = pick.Grant.CommitFrames > 0 ? (pick.SessionId, FrameDeadline.After(now, CommitFrames)) : unchanged
```

- A grant's rank is a **ceiling**: `Evaluate` may return it or lower, never higher. That's what makes
  the early exit valid.
- **Weighted random, not argmax**, with a cutoff so a 2% option never wins a roll.
- Considerations and the runtime multiplier are only computed once a candidate's intrinsic weight is
  above 0, so a creature with nothing adjacent never pays for them.
- **Allocation-free**: the candidate buffer belongs to the arbiter instance (one per system; the
  simulation is single-threaded).
- **Commitment** is a stored deadline, not a countdown: `BehaviorCommitment { ushort BehaviorSessionId;
  uint CommittedUntilFrame; }`, 8 bytes. The game registers a Packed pool of it; the system writes it
  only when `Decide` changes it, and never removes it -- an expired deadline is just stale, so there's
  no churn and no expiry system (the CLAUDE.md "nothing happens, it just stops gating" timer shape).
  Only entities that have ever committed hold one.
- **`DecisionTrace`** (optional, null in normal play): records each evaluated entry's rank, intrinsic
  weight, resolved weight, considerations product, runtime multiplier, final weight, whether the cutoff
  dropped it, and the pick. The game passes one only for the entity being inspected.

### Why commitment is in the engine

Re-deciding after every act is right for attacks, but wrong for Flee: with a 55/45 fight-or-flight
split, a creature re-rolled each step shuffles back and forth beside its attacker. A momentum bonus
still re-rolls every step. A deadline with interrupts from higher ranks is how RimWorld jobs and Qud
goals behave, and nothing about it is game-specific, so it belongs with the arbiter.

## Part 3 -- the game's pieces

### Decision cadence (unchanged)

`NpcDecisionSystem` keeps today's gates, cheapest first, before the arbiter is ever called: dead,
`MovementComponent.IsWaiting(now)`, `ActionLockGate.IsBlocked`, mid-move. A locked visit costs a few
pool reads. Every act locks the entity again -- an attack for its action's lock (about 1 s), a step for
the movement lock (1 s, x√2 diagonal), a potion 1 s, idle 2 s -- **so a free NPC decides about once a
second, once per act, not on every striped visit.** Visits come every 15 frames at Local and every 120
at Neighborhood, so an NPC reacts up to 14 (Local) or 119 (Neighborhood) frames after its lock
clears. Unchanged by this plan; waking each entity on its unlock frame would bypass tiering.

### `NpcDecisionContext`

A `ref struct` implementing `IDecisionContext`: the entity id, `now`, its transform, the pools and
queries, `IntentWriter`, plus **lazily cached perception** -- health fraction, adjacent tiles, hostiles
adjacent, hostiles within 3, allies within R (keyed by radius) -- each computed on first ask, so Engage
and Flee share one scan. `IsHostileTo`/`IsAllyOf` hold today's race rule in one place for factions later.

- `NextUnitRandom()` draws from the session's `MathUtility`, as today's coin flips do, so seeded runs
  stay deterministic.
- `RuntimeWeightMultiplier(tags)` multiplies, for each drive tag the behavior has, the entity's
  stat modifiers on that drive's target: `StatModifierMath.GetEffectiveValue(modifiers, entityId,
  target, 1f)`. The tag-to-target table is the game's (`NpcDrives`): `Behavior.Drive.Aggression` ->
  `StatModifierTarget.BehaviorAggression`, `Behavior.Drive.Caution` -> `BehaviorCaution` (both appended to
  the enum, never inserted). A drive tag with no target (a mod's own drive) still works with blueprint
  modifiers; it just has no runtime source until a target exists.

### The behaviors

`NpcDecisionContext` is the `TContext` for every game behavior, so a game behavior is an
`IBehavior<NpcDecisionContext>`. Registered into `GameModuleContext.NpcBehaviors` (a
`BehaviorCatalog<NpcDecisionContext>` created with the context) in `NpcBehaviorModule.Configure`; a
mod registers its own in its `Configure`.

| Behavior | Tags | Intrinsic weight | Act |
|---|---|---|---|
| `SelfHeal` | Caution | 1 if health < `Parameter` and holds a Health Potion, else 0 | Queue the potion on itself |
| `EngageMelee` | Aggression | 1 if a hostile is adjacent and a `Melee`-tagged action isn't blocked, else 0 | Queue one unblocked melee action, picked uniformly (QuickAttack/PowerAttack: parity) |
| `RangedAttack` | Aggression | 1 if the player is hostile, in range, not adjacent, and Magic Missile isn't blocked | Queue it, Target or Ground mode on a coin flip (today's rule, until spatial queries) |
| `LootCorpse` | none | 1 if `NpcCorpseLooting` finds a corpse it may loot (its retry deadline respected) | Loot it |
| `Flee` | Caution | (`Parameter` + missing health fraction) x hostiles within 3; 0 with none | Step to the adjacent tile farthest from the nearest hostile |
| `WanderStep` | none | 1 | Random adjacent step |
| `Idle` | none | 1 | Wait 2 s |

- **Adding a behavior** = one class and one registration line, then a grant on whichever creatures
  should have it. Neither the arbiter nor `NpcDecisionSystem` changes.
- `WanderStep` and `Idle` at equal weight reproduce today's coin flip.
- `LootCorpse` loots in its `Evaluate`-confirmed `Act`: `NpcCorpseLooting.TryLootNearbyCorpse` splits
  into a find (Evaluate, which caches the corpse on the context) and a loot (Act).
- Flee's `Parameter` is a baseline panic: without it, Flee's weight is 0 at full health and no
  multiplier (Fear included) could make an unhurt creature run.

### Considerations

`NearbyAlliesFactor(byte Radius, float PerAlly, byte MaxAllies, bool Inverse)`: `1 + PerAlly x n`, or
`1 / (1 + PerAlly x n)` when inverse, where `n` = living, simulated allies within `Radius` (Chebyshev,
same layer), self excluded, capped at `MaxAllies`. Radius 8 is 289 tile reads through
`GetOccupantEntityIdSpanAt`, paid only once a candidate already has weight (a hostile is adjacent), and
cached on the context. If the benchmark objects, the fallback is a per-chunk count of creatures per race
kept by `World`'s placement and moves. **Adding a consideration** = one class.

### Assigning: blueprints

- `BlueprintDefinition.Behaviors : IReadOnlyList<NpcBehaviorGrant>` and
  `BlueprintDefinition.BehaviorModifiers : IReadOnlyList<BehaviorModifier>` (`NpcBehaviorGrant` is a
  global alias for `BehaviorGrant<NpcDecisionContext>`). Any definition may carry either: a race, a
  class, a trait, a composite.
- `BlueprintRegistry.Resolve` hands the include chain's grant lists and modifiers to
  `BehaviorSet.Resolve` and caches the set on `ResolvedBlueprint.Behaviors`. `ResolveAll` validates
  them with everything else.
- **Applied parts**: `EntityBehaviors` (mirroring `EntityActions`) returns the spawn blueprint's set
  for an entity with no `AppliedBlueprintComponent`s; otherwise the set of
  `BlueprintRegistry.Compose(spawn, applied parts in order)`, which interns and caches the
  composition, so a Boss applied mid-fight is resolved once per distinct combination, not per
  decision. `Apply` never builds a part twice, so Boss applied twice multiplies once.
- An entity decides if its set isn't empty and it isn't the player. This replaces the
  `MovementMode.Random` gate.

```csharp
// Game/Blueprints/Parts/Boss.cs
public static readonly BehaviorModifier[] BehaviorModifiers =
[
    new(WeightMultiplier: 2f, Tag: NpcDrives.Aggression),
    new(WeightMultiplier: 0.25f, Tag: NpcDrives.Caution),
];
```

### Runtime adjustment, three routes

1. `EntityFactory.Apply` a part with `BehaviorModifiers` (a curse, a promotion). Permanent.
2. Stat modifiers on a drive target, from any effect: a `StatModifierGrant` in an aura, an item, an
   action. Timed or permanent, stacking by the grant's own rule. The behavior framework knows nothing
   of them.
3. Re-registering a behavior or a definition (how mods replace them).

## Starting configurations

Ranks keep today's order: heal, then fight-or-flight, then loot, then wander.

| Behavior (rank) | Goblin | Fairy | Ghost |
|---|---|---|---|
| SelfHeal (4), parameter 0.5 | 1 | 1 | 1 |
| EngageMelee (3) | 1, x `NearbyAlliesFactor(8, 0.25, 8, Inverse: false)` | 1 | 1 |
| RangedAttack (3) | -- (no Magic Missile) | 1 | -- |
| Flee (3), parameter 0.1, commit 3 s | **3**, x `NearbyAlliesFactor(8, 0.5, 8, Inverse: true)` | 1 | 1 |
| LootCorpse (2) | 1 | 1 | 1 |
| WanderStep (1) | 1 | 1 | 1 |
| Idle (1) | 1 | 1 | 1 |

EngageMelee and RangedAttack can't both apply (adjacent vs. not adjacent), so parity holds where Flee
isn't granted. Fairy and Ghost keep neutral defaults until their personalities are picked in-game.
Phase 4 grants everything except Flee and the considerations, to match today exactly.

**Goblins: cowardly alone, brave in numbers.** One adjacent hostile, no potion. Flee =
(0.1 + missing health) x 1 hostile x 3, then the multipliers:

| Goblin | Health | Allies within 8 | Engage | Flee | Result |
|---|---|---|---|---|---|
| Alone | 100% | 0 | 1.0 | 0.1 x 3 = 0.3 | Fights 77%, flees 23% |
| Alone | 80% | 0 | 1.0 | 0.3 x 3 = 0.9 | Fights 53%, flees 47% |
| Alone | 50% | 0 | 1.0 | 0.6 x 3 = 1.8 | Flees 64% |
| Pack | 50% | 4 | 1.0 x 2 = 2.0 | 1.8 / 3 = 0.6 | Fights 77% |
| Pack | 50% | 8 | 1.0 x 3 = 3.0 | 1.8 / 5 = 0.36 | Fights (Flee is under the cutoff) |
| Boss, alone | 50% | 0 | 1.0 x 2 = 2.0 | 1.8 x 0.25 = 0.45 | Fights (Flee is under the cutoff) |
| Alone, with a potion | 40% | any | -- | -- | SelfHeal wins at rank 4; rank 3 isn't evaluated |

**With the player's Dread Idol lit (Caution x4, Phase 6):**

| Goblin | Health | Allies within 8 | Engage | Flee | Result |
|---|---|---|---|---|---|
| Alone | 100% | 0 | 1.0 | 0.3 x 4 = 1.2 | Flees 55% |
| Pack | 100% | 4 | 2.0 | 0.3 x 4 / 3 = 0.4 | Fights (a pack's courage beats fear while unhurt) |
| Pack | 50% | 4 | 2.0 | 1.8 x 4 / 3 = 2.4 | Flees 55% |
| Boss, alone | 50% | 0 | 2.0 | 1.8 x 0.25 x 4 = 1.8 | Fights 53% |

A goblin that decides to flee keeps fleeing for up to 3 s, or until nothing hostile is within 3 tiles,
rather than re-rolling after each step. A rank-4 SelfHeal still interrupts it. The numbers are starting
points for in-game tuning.

## Fear: an aura, not a status effect

The earlier plan built a Fear status-effect module (timer component, wheel, applier, display). The
effects work since makes that unnecessary: CLAUDE.md's rule is that "a buff or protection that holds
while an entity is in an area is ... a `StatModifierGrant` with `Stacking: RefreshFromSameSource`".

- **Dread Aura** (`Auras.jsonl`, `BuiltInAuraIds.DreadAura`, its own Guid): one
  `StatModifierGrant(BehaviorCaution, Multiplicative, +3 (x4), 2 s, RefreshFromSameSource)`, `Flat`
  magnitude so the table above holds at any distance. The aura ticks once a second, so the modifier
  stays while a creature is in reach and lapses 2 s after it leaves. One modifier per creature however
  many ticks, by the stacking rule.
- **Dread Idol** (`Items.jsonl`, `BuiltInItemIds.DreadIdol`): a toggle item like Toxic Idol -- a
  `ToggleItem` activator (Delayed turn-on) holding an `AuraSourceGrant` of the Dread Aura. Added to the
  player in `PlayerKit` (quantity 1, beside the Toxic Idol).
- The player carries the source, so the aura leaves the player alone (only carried sources do), and
  the player has no behaviors anyway.
- The HealthWindow lists it with the other stat modifiers; the two new targets need display names
  wherever `StatModifierTarget` is named for display.
- Both records are written to the master manifest and canonicalised with `ContentManifestExporter`;
  the Workshop's Items and Auras tabs show them, which is the quickest check that they parse.

## Workshop and diagnostics

- **No new Workshop tab now.** Blueprints aren't content, so grants and modifiers are C# data.
  `ContentSession` builds every module, so a broken grant fails it the same way it fails the game.
  When blueprints become content, `BehaviorGrant`/`BehaviorModifier` and the consideration family get
  codecs (a consideration is a polymorphic family, written with `$`), and a Creatures tab can show a
  blueprint's resolved set. Listed as a follow-up.
- **Benchmarks**: the `TestCombatBehaviorSystem` row becomes `NpcDecisionSystem`. The skill and the
  Debug tab's Compare match rows by name, so Phase 4's A/B compares those two rows by hand.
- `BuiltInModulesTests` pins system order; the rename is a deliberate change to it.
- **Inspection**: `GameViews.NpcDecisions` (a view, so Presentation stays off the store) sets the
  inspected entity and returns its last `DecisionTrace`, its resolved set and its commitment.

## Phases

Stop after each phase for in-game testing. Each ends with what to check.

### Phase 1 -- tag discipline, applied to every existing tag role
1. Engine: `DeclareRoot` with ownership and extensibility, `GameplayTagDomain` and `DeclareDomain`,
   required descriptions, the validator (owned root, depth 4, PascalCase), and a domain on
   `ContentField.Tag`/`TagSet` enforced by the reader.
2. Game: move each root to its owner (the roots table) and give every existing tag a description;
   declare the Activation, Activation condition, Item category, Ability score and Area domains; name
   the domain on every tag field's codec; extend `ContentTagValidation` to C#-declared definitions;
   `InventoryTagQueries`, `AbilityScoreTagBonus` and the shop stock query read their domains.
3. Workshop: the inspector's tag picker offers only the field's domain, with each tag's description;
   domain violations appear in the Error List.
4. Tests: a module declaring under another module's closed root fails the build, naming both; under
   an extensible one it succeeds; a replacement module inherits its roots; an undescribed tag, a
   fifth level, or a non-PascalCase segment fails; each domain accepts its roots and rejects the
   others (an `Area` tag on an item, a `Stats` tag as an area tag, an `Item` tag in a condition
   outside the Activation roots); a content record with an out-of-domain tag fails with its file, line
   and column; the whole built-in content passes unchanged.
5. In-game check: the game and the Workshop both start with no new failures; inventory tabs, potion
   shop stock, ability-score bonuses and tag-conditioned modifiers (Holy Ground, body-part melee
   penalties) behave as before. In the Workshop, an item's tag picker lists only Activation tags and
   shows each one's description; an area's lists only `Area` tags.

### Phase 2 -- engine decision framework
1. `Cadence/Engine/Decisions`: `IDecisionContext`, `IBehavior<TContext>`, `BehaviorEvaluation`,
   `BehaviorGrant<TContext>`, `BehaviorModifier`, `IBehaviorConsideration<TContext>`,
   `BehaviorCatalog<TContext>`, `BehaviorSet<TContext>`, `BehaviorArbiter<TContext>` with
   `BehaviorArbiterOptions`, `BehaviorCommitment`, `DecisionTrace`. Behavior and modifier tags are
   checked against a domain the caller names when a set resolves (the game's Behavior domain from
   Phase 4; a test domain here), using Phase 1's `GameplayTagDomain`.
2. Tests (`Tests/Decisions`, a fake context and fake behaviors, no game types): merge (later grant
   replaces, rank 0 removes), modifiers by tag (parent-aware) and by id, unknown id throws, a tag
   outside the behavior domain throws, sort by ceiling; arbiter: ceiling respected, early exit skips
   lower ranks (a lower behavior's `Evaluate` is never called), cutoff, weighted pick distribution over
   a seeded sequence, considerations and runtime multiplier skipped at intrinsic 0, a veto (0) drops a
   candidate; commitment: no re-roll against same-rank rivals, a higher rank interrupts, ends early at
   weight 0, ends at the deadline, a stale commitment to a behavior the set no longer holds is ignored;
   no allocation per `Decide`.
3. In-game check: nothing changes. Goblins behave exactly as before.

### Phase 3 -- one way to queue a turn
1. `IntentWriter` (`QueueAction`, `QueueItemActivation`, `Step`, `Idle`); `PlayerCommands`,
   `TestCombatBehaviorSystem` and `TestDummyAttackSystem` write through it. `ClearStep` and
   `PlayerCommands`' own step clearing go.
2. Tests: queuing an activation clears the step; a FreeCast doesn't; a step withdraws a pending
   activation; the existing input-buffer, Dodge and windup tests pass unchanged.
3. In-game check: walking and attacking feel the same; a Dodge still steps; goblins still attack and
   drink potions without stepping on the same frame.

### Phase 4 -- the game on the framework, at parity
1. `NpcDecisionContext`, `GameModuleContext.NpcBehaviors`, the `Behavior` root (owned by
   NpcBehavior, `Behavior.Drive` extensible) and the Behavior domain, the drive tags (declared, unused
   until Phase 6), the commitment pool.
2. Behaviors: SelfHeal, EngageMelee, RangedAttack, LootCorpse, WanderStep, Idle (split
   `NpcCorpseLooting` into find and loot).
3. `BlueprintDefinition.Behaviors`/`BehaviorModifiers`, `ResolvedBlueprint.Behaviors`,
   `EntityBehaviors` with `Compose` for applied parts.
4. Grants on Goblin, Fairy and Ghost (the table minus Flee and considerations).
5. `NpcDecisionSystem` replaces `TestCombatBehaviorSystem` (deleted); IMPLEMENTATION-NOTES' stand-in
   entry is rewritten; the TODO.md entry is deleted.
6. Tests: a seeded parity test against the old chain, captured before deletion; a mod registering a
   new behavior and replacing a built-in by Guid; a blueprint naming an unknown behavior fails
   `ResolveAll`.
7. Benchmark A/B (headless, fixed seed): `NpcDecisionSystem` against `TestCombatBehaviorSystem`'s row.
8. In-game check: goblins wander, fight adjacent enemies with both attacks, drink potions below half
   health and loot corpses; a Fairy casts Magic Missile at the player from range.

### Phase 5 -- modifiers, considerations, Flee, commitment
1. Boss's `BehaviorModifiers` (Aggression x2, Caution x0.25).
2. `NearbyAlliesFactor` and the context's cached ally/hostile counts.
3. `Flee`, with its commitment.
4. The goblin configuration above.
5. Tests: Boss through an include (Goblin Foreman) and through `Apply` (once only); a modifier on a
   missing behavior does nothing; `NearbyAlliesFactor` counts (self and dead excluded, radius edge,
   cap, other layer ignored); the goblin table's rows as decision tests; a committed Flee interrupted
   by SelfHeal.
6. Benchmark a goblin-heavy fight (the ally scan is the new cost).
7. In-game check (Admin Mode "Spawn here >"): a lone goblin hit to half health usually runs and keeps
   running for a few seconds; a pack of five holds; "Apply > Boss" to a fleeing goblin and it turns
   back to fight.

### Phase 6 -- runtime drives, Dread Idol, inspection
1. `StatModifierTarget.BehaviorAggression`/`BehaviorCaution` (appended), display names, and
   `NpcDrives`' table behind `RuntimeWeightMultiplier`.
2. Dread Aura and Dread Idol records, their built-in id constants, `PlayerKit`.
3. `GameViews.NpcDecisions` and the inspection readout (resolved set, commitment and deadline, last
   decision's candidates layer by layer).
4. Tests: a timed Caution modifier raises Flee's weight and then expires; the lit idol puts one
   refreshed modifier on goblins in reach and none on the player; unlit, it lapses after 2 s; the
   Dread table's rows as decision tests.
5. In-game check: light the idol beside a lone, unhurt goblin (it usually runs), beside an unhurt pack
   (it holds), and beside a Boss at half health (about even); inspect a goblin and see the Caution x4
   in its trace.

### Deferred -- errands
When "NPCs use shops" starts: add an `EntityKey Target` to `BehaviorCommitment` (or a game-side
companion component, if the engine struct should stay key-free), so a committed behavior remembers
its shop. Commitment already gives the rest: persistence across acts, interrupts from higher ranks,
and ending when its behavior says it's done. The spatial-queries TODO's claimable spots and blackboard
build on this.

## Decisions

- **The engine owns the framework (`Engine.Decisions`); the game provides and assigns the pieces.**
- Dual utility: rank ceiling, weighted random, 0.25 cutoff (the game's option, not an engine constant).
- Drives are gameplay tags, so mods can extend them; blueprint modifiers target a tag or a behavior id.
  Boss: Aggression x2, Caution x0.25. Tags are kept to their roles by root ownership and typed
  fields (`GameplayTagDomain`), after Unreal's `Categories`, restricted tags and typed tags.
- Phase 1 applies tag discipline to every existing tag role (owners for today's roots, a domain on
  every tag field, descriptions, the validator), before any behavior code.
- Goblins: Flee weight 3 with a 0.1 baseline panic, pack courage and pack fear from other goblins
  within 8 tiles.
- Commitment is part of the arbiter: Flee commits for 3 s, interruptible only by higher ranks.
- One way to queue a turn: `IntentWriter`. No frame stamp on the action lock (it would add a field,
  a write and a read while removing nothing; see "Performance").
- `TestDummyAttackSystem` stays its own system (a special case), writing through `IntentWriter`.
- Fear is the Dread Aura's `StatModifierGrant` on Caution, not a status-effect module; the Dread Idol
  is a toggle item in the player's starting kit.
- No Admin Mode weight tuner, and no new Workshop tab until blueprints are content.

## Open questions

1. **Commitment target**: when errands arrive, should the engine's `BehaviorCommitment` gain an
   `EntityKey` (engine already has `EntityKey`), or should the target live in a game-side component
   beside it? Recommend the engine struct, since "commit to X" is generic -- but it can wait until
   errands start.
