# NPC Behavior Composition

(Phases 1-6 built and checked 2026-11-13: the plan is complete. Its one deferred item, errands, waits on "NPCs use shops". Checked against the
code on 2026-11-11. Replaces TODO.md's "NPC behavior
composition + generalized "turn claimed" signal" entry, which is deleted when Phase 4 lands. Unblocks
"NPCs use shops".)

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
  1. Self-heal: below 50% health and holding a Health Potion. Queued with no blocker check (the
     melee and ranged branches make one), and before the neighbor gate below.
  2. Melee: holds QuickAttack and an adjacent entity has a different race; QuickAttack or
     PowerAttack on a coin flip. If `ActivationQueries.GetBlocker` refuses the one picked (its
     activation effects, a Disabled rule from a null zone, melee disabled by lost arms), the branch
     fails -- the other attack isn't tried.
  3. Ranged: Magic Missile at the player when in range and not adjacent; Target or Ground mode on a
     coin flip (temporary: only at the player, see TODO "Spatial queries for NPC decisions").
  4. Loot a corpse or an item pile on or beside it (`NpcLooting`). A corpse needs loot rights, and
     when every corpse is reserved the NPC tries none for 30 s (`CorpseLootRetryComponent`, written
     during the search). A pile has no rights and no retry: it is taken from when anything fits and
     passed over when nothing does -- found out by trying.
  5. Wander: a coin flip between idle (2 s `WaitUntilFrame`) and a random adjacent step.

  Melee and looting are skipped outright unless something else stands on or beside the creature
  (`HasOtherOccupantOnOrBeside`, one pass over the footprint's ring) -- a cheap gate that made the
  common "nothing near me" decision fast. Ranged aims through `TargetResolution.IsAimable`, which
  checks range and line of sight (`Engine.Tiles.LineOfSight` over `World.BlocksSight`). Every roll
  draws from `NpcBehaviorModule.BehaviorRandomStream` (`GameModuleContext.RandomStreams`).
- It's an `ITieredSystem` with the same `StripeCount` (15) and tier divisors as `MovementSystem`,
  wired to the same `MovementComponent` pool, so an entity is decided and moved on the same frame.
  It was once the largest per-frame cost in the game; tiering and the shared neighbor pass fixed
  that, and it is now the largest system at 0.40-0.42 ms of `EcsContext.Update`'s 1.13-1.15 ms/frame
  (headless Release, seed 1, frames 600-3600, 2026-10-29 and 2026-11-06): about 560 decisions a
  frame, 91% of them ending in a wander. A replacement has to be at least as cheap.
- **Who decides is "holds a `MovementComponent` in `MovementMode.Random`".** Goblin, Fairy, Ghost,
  Human and the Engineer class add one; `PlayerKit` turns the player's to `PlayerControlled`;
  `StationaryPart` removes it, so a stationary creature (`GoblinFairy`, `StationaryFairyEngineer`)
  is in neither system's stripe set and never decides anything -- it never attacks.
- The system reads the Health Potion's activator from the catalog while it is wired
  (`context.Items.Get(BuiltInItemIds.HealthPotion)` in `RegisterBehavior`), a by-id load at build
  time that the new behaviors don't need.
- `NpcBehaviorModule` declares `RunsBefore = [MovementModule.ModuleId]`. The activation systems drain
  `PendingActionActivationComponent`/`PendingItemActivationComponent` later in the same frame.
- **The "turn claimed" half of the TODO entry is out of date.** `MovementSystem` no longer checks the
  pending pools. The rule now is "queuing an activation clears the step": `TestCombatBehaviorSystem`
  calls `ClearStep` after each queue, and `PlayerCommands` sets `NextMapPosition` to null before it
  writes a pending activation. It holds, but by convention in two places, not by structure -- and
  the two already differ: `PlayerCommands` also withdraws the other kind's pending request when it
  queues one, and both when it writes a move (`WithdrawActivationRequests`); the NPC system does
  neither. `PlayerCommands` clears the step for every timing, FreeCast included; the one step an
  activation brings with it (Dodge's destination) is written when its `ActivationUsedEvent` arrives.
- **Component writes** (IMPLEMENTATION-NOTES "Component writes: write scopes and struct updaters"):
  every in-place write is a write scope (`using var write = pool.TryWrite(id)`, main thread only, no
  structural change to that pool while it's open) or an `unmanaged` struct updater (`TryUpdate`), and
  both notify. The NPC code already follows it: the step goes through the shared
  `SetNextMapPositionUpdate` (`MovementComponentUpdates`, also used by `MovementSystem` and
  `PlayerCommands`), and the idle wait is a write scope. Pools are `Unobserved` unless the module that
  registers them says otherwise; none of the pools this plan writes is observed.
- `TestDummyAttackSystem` is a second decision system: a plain `ISystem` (stripe 1) that fires Power
  Attack at its Adjacent ring whenever its action lock clears, whether or not anyone is there. The
  dummy has no race and no `MovementComponent`. Phase 4 replaces the system with behavior grants: the
  dummy is the first stationary creature that fights (see "Who decides").
- Hostility is "has a different primary race" (`IsAttackable`). There are no factions.
- Blueprints are still C# static classes. Actions, items and every aura are content (`Items.jsonl`,
  `Auras.jsonl`, `Actions.jsonl`) with codecs -- no aura is declared in code -- and blueprints become
  content later (the data-driven plan's follow-up "Terrain and blueprints as data"). A blueprint
  declares the content its build reads (`UsesActions`/`UsesItems`).
- **Actions and items run one activation sequence** (`Activations.TryUse`), and an item's blocker
  is asked the way an action's is (`ActivationQueries.GetBlocker`; `ActionStateView.GetItemBlocker`
  for Presentation). A change to how NPCs use one is checked against the other.
- Toggles have landed: Toxic Idol is an `Items.jsonl` record with a `ToggleItem` activator and an
  `AuraSourceGrant` naming a `Auras.jsonl` aura.
- **Content loads by id and is fixed per session.** A game decodes a record the first time its Guid is
  asked for; game code never enumerates `ActionCatalog`/`ItemCatalog.Definitions` (a whole-catalog
  question reads `Summaries`). No definition changes mid-session: overrides take effect when the next
  session loads (IMPLEMENTATION-NOTES; memory "Definitions fixed per session").
- The engine has the pieces the framework builds on: `Catalog<T>` (Guid-keyed, redirects),
  `GameplayTag`/`GameplayTagSet` (declared by modules, parent-aware), `RandomStreams` and `MathUtility`,
  `FrameDeadline`, write scopes and struct updaters, and Packed pools any module can register.

Related TODO items that this plan does not need, but which will improve its behaviors later:
"Grid pathfinding and navigation" (Engage/Flee step one tile and can get cornered), "Field of view
and perception" (line of sight now exists per pair, which this plan uses; a full field of view and
memory of what was seen don't), "Spatial queries for NPC decisions" (where to flee to, ranged targets
other than the player), and `PLAN-multithreading.md` (decisions on workers; see "Shaped for workers,
not threaded" below -- the TODO entry this plan replaces asks for the shape to be built in now, and
that plan's forked sections come after this plan's Phase 5).

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
| `BehaviorCommitment` (16-byte struct: behavior, deadline, target `EntityKey`; stored in a pool the game registers) | Engine | `Engine.Decisions` |
| `NpcDecisionComponent` (who decides: the entity's behavior set and its idle wait) | Game | `Game/Modules/NpcBehavior/Components` |
| `DecisionTrace` (one inspected decision, layer by layer) | Engine | `Engine.Decisions` |
| `NpcDecisionContext` (perception cache, intent methods, drive lookup) | Game | `Game/Modules/NpcBehavior` |
| The behaviors (SelfHeal, EngageMelee, RangedAttack, LootNearby, Flee, WanderStep, Idle) | Game | `Game/Modules/NpcBehavior/Behaviors` |
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

**B. `IntentWriter`, the one chokepoint (decided).** Game, next to `ActionLockGate`:
`QueueAction(entityId, actionId, selection)`, `QueueItemActivation(entityId, stackInstanceId,
selection, activatedFromSlot)`, `Step(entityId, tile)`, `StepFromActivation(entityId, tile)`,
`Idle(entityId, now, frames)`. Queuing an activation writes the pending component, withdraws the
other kind's, *and* clears `NextMapPosition`; `Step` withdraws both pending activations --
`PlayerCommands`' rule today, which NPCs gain. Behaviors never see a pending pool:
`NpcDecisionContext` exposes only these methods, so a behavior structurally can't double-book a
turn.
- **Every timing clears the step, FreeCast included** -- today's rule in both writers, kept. The one
  step that goes with an activation is the one the activation causes (Dodge's destination), written
  by `StepFromActivation` when its `ActivationUsedEvent` arrives: it sets the step and touches no
  pending pool, since it runs inside the activation system's own drain. `PlayerCommands` keeps the
  buffer, the expiry and which step waits on which action; only its four writes move.
- A `null` tile to `Step` is "stand still": it withdraws the activations and clears the step
  (`PlayerCommands.WriteMove` when the tile can't be occupied).
- **Its writes, by the component-write rules:**

  | Method | Writes |
  |---|---|
  | `QueueAction` / `QueueItemActivation` | `Remove` of the other kind's pending component, `Merge` of this one (an add, or the merge action on a second queue in a frame), then `TryUpdate(SetNextMapPositionUpdate(null))` |
  | `Step` | `Remove` of both pending components, then `TryUpdate(SetNextMapPositionUpdate(tile))` |
  | `StepFromActivation` | `TryUpdate(SetNextMapPositionUpdate(tile))` only |
  | `Idle` | A write scope setting the wait (single site, assignment only): `MovementComponent.WaitUntilFrame` in Phase 3, `NpcDecisionComponent.WaitUntilFrame` from Phase 4, when the wait moves to what it gates |

  The step goes through the existing shared updater, so `IntentWriter` adds no updater of its own.
  Removals and merges happen outside any scope, so the Debug structural-change guard never trips.
  No pool becomes observed. An entity with no `MovementComponent` (a stationary creature) queues the
  same way: the step update finds nothing and does nothing.
- **Why the chokepoint matters beyond the step rule:** a write scope is main-thread only, and a write
  recorded on a worker and applied later must be an `unmanaged` updater or an add (IMPLEMENTATION-NOTES,
  "Scopes are main-thread only"). Every NPC intent goes through these methods, and they are only
  ever called from `Act`, which stays on the main thread when choosing moves to workers
  (`PLAN-multithreading.md`: workers only read, the main thread writes in order) -- so neither
  `IntentWriter` nor any behavior changes then.

**C. Owned gameplay tags, as Unreal's GAS does** (`ActivationOwnedTags`/`ActivationBlockedTags`): an
activation adds "turn claimed" to its owner, and anything can check it.
- Con: needs a per-entity tag container, which the engine doesn't have (`GameplayTagSet` is
  immutable per definition). Worth it when Stagger/Dodge rules want tags too, not for this.

The earlier frame stamp (`ActionLockComponent.TurnClaimedAtFrame`) is dropped: with `MovementSystem`
no longer reading pending pools and every queue clearing the step, nothing would read it.
`ActionLockComponent` stays 8 bytes.

### Performance: frame stamp vs. `IntentWriter`

Measured base: `ActionLockComponent` has about 73,000 holders (8 B each, a 4.1 MB pool; benchmark of
2026-10-25, 662,885 living entities). Settled cost (headless Release, seed 1, frames 600-3600,
2026-10-29 and 2026-11-06): `MovementSystem` 0.14 ms/frame (~257 steps a frame) and
`TestCombatBehaviorSystem` 0.40-0.42 ms/frame (~560 decisions a frame).

| Cost | Frame stamp (B of the first version) | `IntentWriter` |
|---|---|---|
| Writes per turn | +1: claim the lock (a pool lookup and write) on top of today's queue and step clear | Same as today: the pending component and the step clear, moved into one method |
| Reads per movement visit | +1 compare if folded into `IsBlocked`'s existing read of the lock; a second pool lookup if not | None |
| Memory | +4 B x ~73,000 = +0.29 MB (the pool +7%) | None |
| Cache | Lock stride 8 -> 12 B: every system that reads the lock (movement, decisions, activations, HUD) touches 50% more cache lines for it | None |
| Work removed | None: the step must still be cleared, or a stale step runs on the first visit after the claim's frame | Two copies of the step-clearing rule become one |

Estimated size: at ~560 decisions a frame, an extra write at 10-30 ns is 0.006-0.017 ms/frame, and
the wider stride adds a few percent to the lock reads inside the two systems above -- together on
the order of 2-4% of their 0.55 ms/frame. **Both are small; the deciding point is that the stamp
only adds (a field, a write, a read) and removes nothing, while `IntentWriter` costs nothing at run
time.** (It adds one `Remove` of an absent pending component per NPC queue -- a page lookup that
misses.) These are estimates; Phase 3's A/B benchmark confirms `IntentWriter` is free.

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
    /// Who the current behavior is committed to. The arbiter sets it from the live commitment before it asks
    /// the committed behavior to continue, clears it before a fresh pick, and stores what Act leaves in it.
    EntityKey CommitmentTarget { get; set; }
}

public interface IBehavior<TContext> where TContext : IDecisionContext, allows ref struct
{
    Guid Id { get; }
    string Name { get; }
    /// What kind of behavior it is (e.g. Behavior.Drive.Caution) -- what modifiers and runtime multipliers target.
    GameplayTagSet Tags { get; }
    /// Rank <= grant.Rank (0 = doesn't apply) and an intrinsic weight >= 0 from the situation alone.
    /// Reads only: it writes nothing outside the context and draws no random number.
    BehaviorEvaluation Evaluate(ref TContext context, BehaviorGrant<TContext> grant);
    /// Writes the intent. Only called on the winner, and the only place a behavior writes or rolls.
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
- A consideration is as read-only as `Evaluate`.

### Shaped for workers, not threaded

The TODO entry this plan replaces asks for the decide step to be built so it can run on worker
threads later, without threading it now (0.4 ms/frame is far below needing it). The framework's
contract gives that shape:

| What blocks a parallel decide today | In this plan |
|---|---|
| Every roll draws from one shared sequence, so drawing in parallel would tie a seeded world to thread timing | **Each decision's rolls are derived, not drawn**: the system reseeds one sequence per decision from `SeedMix` of the behavior stream's seed (`RandomStreams.SeedFor(BehaviorRandomStream)`), the entity's key and the frame. `NextUnitRandom()` and every roll a behavior makes in `Act` (which attack, which mode, which tile) read that sequence. An entity's choice no longer depends on who decided before it |
| Shared scratch (`_adjacentTilesBuffer`, `TargetResolution`'s buffers) | Scratch belongs to the context's owner (the system: one set today, one per worker later). The arbiter's candidate buffer is per arbiter instance for the same reason |
| Acting while deciding (`NpcLooting` moves stacks and writes its retry mid-search) | `Evaluate` and considerations only read. Everything written is written by `Act`, through `IntentWriter` or a Game command (looting), on the winner only |
| Writes from a worker | The arbiter has two entry points, `Choose` (reads, ends at the pick) and `Act` (the winner writes). The system runs them as **two passes over each bucket**: every due entity chooses, then every choice acts, in entity order. A threaded version forks the first pass and changes nothing else |

**Two passes from Phase 4 on, single-threaded** (settled 2026-11-12, with `PLAN-multithreading.md`).
If each creature chose and acted in turn, creature B would choose after creature A acted, and a later
fork -- where the whole bucket chooses against the state before anyone acted -- would change every
seeded world. Choosing the whole bucket first makes the single-threaded order the forked order, so
the fork changes no result and worker count never matters. What it takes:
- **The choice is a record**: an `unmanaged` value per due entity (`NpcDecisionRecord`: the entity
  id, the arbiter's `BehaviorChoice`, the context's findings, the decision's roll sequence as it
  stood after the pick), appended to a list the system owns and reuses. The act pass walks it front
  to back.
- **What `Evaluate` finds for `Act` is a plain value** (`NpcDecisionFindings`, `unmanaged`): entity
  keys, stack instance ids and tiles -- never a dense index or a span, which the acts before it may
  have invalidated. The act pass builds a fresh context for the entity and puts the findings back,
  so `Act` still never searches again.
- **`Act` re-checks what an earlier act this pass could have taken.** Every `Act` but one only
  queues an intent (an activation, a step, an idle wait), which changes nothing another creature's
  choice or act reads. Looting is the one that changes the world at once, so `LootNearby.Act`
  resolves its target again (the key still resolves, it still holds something this creature can
  carry, it isn't reserved) and idles when it doesn't: a creature can lose a visit to a neighbour
  that looted first. Two creatures stepping to one tile are already settled by `MovementSystem`'s
  order.
- **Nothing is written while choosing**: in Debug the context refuses `IntentWriter` and looting
  until the act pass, so an `Evaluate` or a consideration that writes fails where it does so.
- **Cost**: each due entity's data is fetched in two passes instead of one. Phase 4's benchmark
  reports it separately from the framework's own cost.

Deriving the rolls changes which roll each creature gets, so a seeded world plays differently from
Phase 4 on -- which Phase 4 does anyway (one weighted draw replaces the chain's coin flips). Movement
stays serial either way: two entities claiming one tile are settled by order.

### Tag domains: keeping one tag system from meaning everything

Tags already do many jobs here: classifying actions and items (`Action.*`, `Delivery.*`,
`Damage.*`, `Magic`, `Spell`), naming inventory tabs (`Item.*`), conditioning stat modifiers
(`StatModifierComponent.ConditionTag`), deciding what a Disabled rule stops
(`ActivationDisableGrant`'s query), traits (`Trait.Dodgeable`), map areas (`Area.*`), and now
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
3. **Content fields name their domain.** `ContentField.Tag`/`TagSet` take a domain (today they take a
   key and a display name, and nothing reads the kind). The reader fails a tag outside it (file, line
   and column, like every other content failure), so in the Workshop the record is left out and
   listed in the Error List with no Workshop code. The schema carrying the domain is what a tag
   picker will filter by once the Workshop edits content (its tabs are view-only today; editing is
   in TODO.md). Built: `ContentTypeRegistry.Tags` carries the build's registry to the reader; a
   rejected value's `ContentFormatException` names its key and JSON text, and `ContentTable` finds
   that text in the record's source for the column.
4. **A description per tag**, required at declaration, beside the display name
   (`DeclareRoot(root, description, extensible)`, `Declare(tag, description, displayName,
   extensible)`; ExampleMod declares no tags). The Workshop's detail windows show a tags line as
   display names, comma-separated; built, hovering the line shows one row per tag -- its name and
   description -- rather than a tooltip per tag, so the line keeps its layout.
5. **A validator** (`GameplayTagRegistry.Declare`, after every module's `DeclareTags`): every declared
   tag is under an owned root, depth at most 4 (today's deepest is 3: `Item.Consumable.Potion`),
   PascalCase segments. Several modules may still declare the same tag (today's rule), as long as
   each is allowed under its root.

#### Game: today's roots get owners

**Built (2026-11-12): Core owns every activation root, not the modules that read them.** Content
every build decodes names them: the starting area's Anti-Magic and Anti-Literacy shrines are
blueprints, so every build registers their auras, whose `ActivationDisableGrant`s name `Spell` and
`Item.Consumable.*`. With `Spell` owned by Actions or `Item` by Inventory, a build without that
module (the foundation-only builds tests use) would leave those tags undeclared -- the same reason
`CoreModule` already declared every built-in tag. Ownership still decides who may declare under
each root and which roots mods may extend; Area is the one built-in root a module other than Core
owns, because only Areas reads area content.

| Root | Owner | Extensible by other modules | Why |
|---|---|---|---|
| `Action` | Core | No | Classification code reads (`Action.Attack`) |
| `Spell` | Core | No | A leaf: casting a spell or reading a scroll of one, implied by the activator (`ImpliedTags`) and matched by Anti-Magic |
| `Delivery` | Core | No | Melee/Ranged gate body-part effects and Dodge |
| `Targeting` | Core | No | `Targeting.Self` is checked exactly; a technical tag has no children |
| `Trait` | Core | Yes | Dodgeable, Staggering; a mod's own trait is reasonable |
| `Effect` | Core | Yes | `Effect.Healing`; new effect kinds are content |
| `Damage` | Core | Yes | A mod's new damage type is the classic extension |
| `Magic` | Core | No | A leaf, combined with others by design |
| `Stats` | Core | No | `Stats.AbilityScore.*` maps one-to-one to the seven scores in code |
| `Item` | Core | Yes | Each child is an inventory tab (`Item.Toggle` and `Item.Lootbox` included); a mod's new category gets a tab |
| `Area` | Areas | Yes | Area classification is content (`AreaTags`) |
| `Behavior` | NpcBehavior (Phase 4) | `Behavior.Drive` only | A mod's own drive |

#### Game: every tag field gets a domain

| Domain | Accepts | Fields |
|---|---|---|
| **Activation** ("what a use of something is", extensible) | `Action`, `Spell`, `Delivery`, `Targeting`, `Trait`, `Effect`, `Damage`, `Magic`, `Stats`, `Item` | `ActivatableDefinition.Tags` (actions and items, implied tags included), `AuraDefinition.Tags`, `TerrainContact.Tags` -- each becomes the activator tags an effect is applied with -- and every condition on them: `StatModifierGrant.ConditionTag` and `ActivationDisableGrant`'s query (all / any / none). A condition can only name something an activation can carry, so it can never be unmatchable. Built as one domain rather than a separate "Activation condition" domain over the same roots: the two could only ever accept the same tags |
| **Item category** | `Item` | Inventory tabs (`InventoryTagQueries`: a category is a tag below the domain's root, `AcceptsBelowItsRoots`) and shop stock queries -- a shop's query is set by its blueprint's build step, so nothing can read it before the shop is built; `BlueprintTests.EveryShopsAcceptedItems_AreItemCategoryTags` holds it |
| **Ability score** | `Stats.AbilityScore` | `AbilityScoreTagBonus`'s table (`AbilityScoreTagBonus.Tags`, checked by `ContentTagValidation`) |
| **Area** | `Area` (AreasModule's) | `AreaDefinition.Tags` |
| **Behavior** | `Behavior` (Phase 4) | `IBehavior.Tags`, `BehaviorModifier.Tag` |

The domains are identities in `GameTagDomains` (Game.Tags); what each accepts is declared per build
(`DeclareDomain`, `AddToDomain`) and answered by the registry (`Accepts`, `ProblemWith`,
`EnsureAccepts`). Every tag an effect entry names (`IEffectEntry.ReferencedTags`) is a condition, so
all of them are checked against Activation; an entry naming a tag of another role would have to say
which, and none does yet.

- Content codecs name their field's domain, so `Items.jsonl`, `Actions.jsonl`, `Auras.jsonl`, the area
  files and the area palettes are checked on read, in every build and in a by-id game as each record
  is decoded. `ContentTagValidation`, which today only checks that tags are declared, also checks
  domains -- in its per-record forms (`ProblemsOf`, which `ContentChecks` shares), so a definition made
  at runtime with an out-of-domain tag becomes a draft like any other failing one, and for what is
  still declared in C# (terrain contacts, activators' implied tags, blueprints' behavior modifiers).
  An entry reports each tag with its domain through `IEffectEntry.ReferencedTags`, reached through
  `EffectEntries.ForEach` as now.
- Expected fallout: none in today's content, if every tag sits where its role expects. Any record the
  validator rejects is a real mistake, fixed in the content, not by widening a domain.

This keeps tags (as decided) and gives each one a single role: `Behavior.Drive.Caution` can't be put on
an item, `Area.Testing` can't condition a stat modifier, and `Damage.Fire` can't drive a behavior.

### Catalog and resolved sets

- **`BehaviorCatalog<TContext>`** (built: it wraps a `Catalog<IBehavior<TContext>>` rather than
  deriving from it, so `Register` can refuse once frozen without making `Catalog<T>.Register`
  virtual): re-registering a Guid replaces a
  behavior in place (how a mod replaces "Flee"), and redirects work as for any catalog. Registration
  happens in `Configure` only; once the build resolves sets, the catalog is fixed for the session,
  like every definition. It also issues each behavior a session-local `ushort` id, which is what a
  commitment stores. Never persist one: a future world save stores the behavior's Guid. Ids are
  issued when the catalog is frozen (`Freeze(tags, behaviorDomain)`, called by the game before it
  resolves sets), 1 up in registration order, and `Register` after that throws. `Freeze` checks every
  behavior's tags against the domain; `BehaviorSet.Resolve` checks every modifier's tag against the
  same one, and refuses a modifier naming both or neither of a tag and a behavior.
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

The arbiter has two entry points, so a caller can choose for many entities before any of them acts
(see "Shaped for workers, not threaded"):
- `Choose(ref context, set, in commitment, trace)` reads the commitment as a value, evaluates, and
  returns a `BehaviorChoice` (`unmanaged`: the picked entry's place in the set, or none; its rank;
  whether it continues the commitment). It calls no `Act` and writes nothing.
- `Act(ref context, set, in choice, in commitment)` calls the chosen behavior's `Act` and returns a
  `BehaviorDecision`: what it did, and a new commitment if one started.
- `Decide(ref context, set, in commitment, trace)` is `Choose` then `Act`, for a caller with one
  entity (tests, a game with no need for two passes).

It never holds a pool or a `ref` into one -- the engine has no raw mutable `ref` to hand it, and the
caller decides how to write. Its options (`BehaviorArbiterOptions`) are the game's: the cutoff
fraction (0.25). The pseudocode is `Decide`; `Choose` ends where it says "act on it" or
`pick.Behavior.Act`, returning that entry instead.

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
pick = the only candidate, or weighted random over them using context.NextUnitRandom()
pick.Behavior.Act(ref context, pick.Grant)
decision.NewCommitment = pick.Grant.CommitFrames > 0 ? (pick.SessionId, FrameDeadline.After(now, CommitFrames)) : none
```

- A grant's rank is a **ceiling**: `Evaluate` may return it or lower, never higher -- the arbiter
  throws, naming the behavior, when one does. That's what makes the early exit valid.
- **Weighted random, not argmax**, with a cutoff so a 2% option never wins a roll. A lone candidate
  draws nothing, so a creature with one thing to do doesn't advance the stream.
- Considerations and the runtime multiplier are only computed once a candidate's intrinsic weight is
  above 0, so a creature with nothing adjacent never pays for them.
- **Allocation-free**: the candidate buffer belongs to the arbiter instance (one per system today,
  one per worker if decisions are ever threaded).
- **Commitment** is a stored deadline, not a countdown: `BehaviorCommitment { ushort BehaviorSessionId;
  uint CommittedUntilFrame; EntityKey Target; }`, 16 bytes, a mutable struct with auto-properties (it's
  rewritten in place each time a new commitment starts; IMPLEMENTATION-NOTES "Component shape").
  **The engine tracks who a commitment is to**: `Target` is whatever the winning behavior's `Act` left
  in `context.CommitmentTarget` (a key, never an id -- it outlives the frame; default for a commitment
  to nothing in particular). While the commitment is live the arbiter hands it back through the
  context, so the behavior continues against the same entity rather than choosing again. The engine
  never resolves the key: a behavior whose target is gone returns weight 0 and the commitment ends
  early, as any other. Flee is the first user (it keeps running from the hostile it ran from); errands
  ("NPCs use shops") are the next. The game registers a
  Packed pool of it, `Unobserved` (nothing reacts to a commitment changing; the arbiter reads it on the
  next decision). The system writes it only when `Decide` returns a new one, as a find-or-add: a
  `TryWrite` scope assigns both fields and returns when found, and the `Add` comes after the block.
  It's never removed -- an expired deadline is just stale, so there's no churn and no expiry system
  (the CLAUDE.md "nothing happens, it just stops gating" timer shape). Only entities that have ever
  committed hold one.
- **`DecisionTrace`** (optional, null in normal play): records each evaluated entry's rank, intrinsic
  weight, resolved weight, considerations product, runtime multiplier, final weight, whether the cutoff
  dropped it, and the pick. The game passes one only for the entity being inspected. Built: its
  `Path` (Nothing, FreshPick, Interrupt, Continued) and one outcome per entry (DidNotApply, Vetoed,
  Outranked, BelowCutoff, NotDrawn, Chosen) are what the Admin Mode decision block shows.

### Why commitment is in the engine

Re-deciding after every act is right for attacks, but wrong for Flee: with a 55/45 fight-or-flight
split, a creature re-rolled each step shuffles back and forth beside its attacker. A momentum bonus
still re-rolls every step. A deadline with interrupts from higher ranks is how RimWorld jobs and Qud
goals behave, and nothing about it is game-specific, so it belongs with the arbiter.

## Part 3 -- the game's pieces

### Decision cadence (unchanged)

`NpcDecisionSystem` keeps today's gates, cheapest first, before the arbiter is ever called: dead,
the idle wait, `ActionLockGate.IsBlocked`, mid-move (only for an entity that can move). A locked visit costs a few
pool reads. Every act locks the entity again -- an attack for its action's lock (about 1 s), a step for
the movement lock (1 s, x√2 diagonal), a potion 1 s, idle 2 s -- **so a free NPC decides about once a
second, once per act, not on every striped visit.** Visits come every 15 frames at Local and every 120
at Neighborhood, so an NPC reacts up to 14 (Local) or 119 (Neighborhood) frames after its lock
clears. Unchanged by this plan; waking each entity on its unlock frame would bypass tiering.

### Who decides: `NpcDecisionComponent`

Stationary creatures are meant to fight, so the system can't stay wired to the `MovementComponent`
pool. Its population becomes a pool of its own:

```csharp
public struct NpcDecisionComponent
{
    public ushort BehaviorSetId { get; set; }   // session-local, EntityBehaviors' interned set
    public uint WaitUntilFrame { get; set; }    // the idle wait; 0 = not waiting
}
```

Packed, `Unobserved`, 8 bytes. `EntityBuilder` adds it when an entity is built from a blueprint whose
resolved behavior set isn't empty (beside its mana grant, the same kind of definition-driven add),
and `EntityFactory.Apply` rewrites `BehaviorSetId` when an applied part changes the set. An unbuilt
skeleton doesn't hold it and isn't simulated anyway.

**What adding stationary creatures to the stripe set means:**

| Concern | Outcome |
|---|---|
| Does `MovementSystem` pick them up? | No. It stays wired to `MovementComponent`, which a stationary creature doesn't hold. Nothing in Movement changes |
| Is a mover still decided and moved on the same frame? | Yes. A stripe is a function of the entity id and the stripe count (`EntityStripeSet.StripeOf`), and a tier bucket of the entity's tier -- not of which pool drives the set. Same `StripeCount` (15) and divisors, so the two sets bucket a mover identically; a test pins it |
| Cadence for a stationary creature | The same as anyone's: every 15 frames at Local, 120 at Neighborhood, frozen beyond. The test dummy loses its stripe-1 "the frame the lock clears" and swings up to 14 frames later |
| Behaviors that step (WanderStep, Flee) | They evaluate to 0 for an entity that can't move (`context.CanMove`: it holds a `MovementComponent`). Otherwise a stationary creature would pick WanderStep, write a step nobody takes, set no lock, and pick it again next visit -- or commit to a Flee it can't act on instead of fighting |
| The idle wait | It lived on `MovementComponent`, which a stationary creature lacks, so `Idle` would gate nothing. It moves to `NpcDecisionComponent.WaitUntilFrame`, read by the decision gate and written by `IntentWriter.Idle`. `MovementComponent.WaitUntilFrame` stays as Movement's own blocked-step backoff only |
| The player | Holds the component like any creature of its race (the player is not always Human, and not every Human is the player), and is skipped by entity id (`IPlayerQuery`), one compare |
| `EntityBehaviors`' per-decision lookup | Gone: the set id is on the component, so a decision no longer reads the spawn record and the applied list |
| Memory | 8 B x ~73,000 built creatures = 0.6 MB, plus its page index; ids are already spawned like-with-like |
| `LocalTierRoster` | Unchanged: it is scoped to movers on purpose |

`StationaryPart` keeps removing the `MovementComponent`; `GoblinFairy` and `StationaryFairyEngineer`
then fight in place with their races' grants (melee when something is adjacent, Magic Missile from
where they stand, a potion or Heal when hurt).

**The test dummy on behaviors.** `TestDummyAttackSystem` and `TestDummyComponent` are deleted.
`TestDummyBlueprint` gains a race of its own (a `RaceFacet` with no body plan: hostility is "a
different race", and a raceless entity neither attacks nor is attacked) and one grant, `EngageMelee`.
Its Power Attack keeps the 3 s idle cooldown. What changes for Dodge practice: it winds up only when
a hostile stands beside it, not at empty air; other creatures beside it are hostile to it and it to
them (it regenerates); and holding a race makes it deferrable, so one spawned in an unsimulated tier
is a skeleton until promoted, like any creature.

### `NpcDecisionContext`

A `ref struct` implementing `IDecisionContext`: the entity id, `now`, its transform, the pools and
queries, `IntentWriter`, plus **lazily cached perception**, each computed on first ask:
- **Anyone on or beside me** -- today's `HasOtherOccupantOnOrBeside` gate, asked first by every
  behavior that needs a neighbor (EngageMelee, LootCorpse, and Flee's adjacent case), so the common
  "nothing near me" decision stays as cheap as it is now.
- Health fraction, adjacent tiles, hostiles adjacent, hostiles within 3, allies within R (keyed by
  radius), so Engage and Flee share one scan.
- **Hostiles within 3 count only those in sight** (`LineOfSight.IsVisibleFromFootprint` over
  `World.BlocksSight`, the rule targeting already uses), so a goblin doesn't flee from an enemy behind a
  wall. A few short lines per decision, paid only when a hostile is near. Allies are counted without
  sight: a pack is heard as much as seen.

All reads go through `GetReadonly`/`TryGetReadonly` and `GetOccupantEntityIdSpanAt`; the context writes
nothing itself -- every write is `IntentWriter`'s, or looting's in `LootNearby.Act`. What `Evaluate`
finds for `Act` (the corpse or pile, the potion's stack, the hostile to step away from) is kept on
the context as one `unmanaged` value, `NpcDecisionFindings` (entity keys, stack instance ids, tiles),
which the system carries from the choose pass to the act pass, so `Act` never searches again. `IsHostileTo`/`IsAllyOf` hold today's race rule
(simulated, not dead, a different primary race) in one place for factions later. Behaviors ask the
entity what it has (`EntityActions`, its inventory stacks), never a whole catalog, so by-id content
loading holds: a behavior finds content by tag among what the entity already holds -- whose
blueprint therefore already declares it (`UsesActions`/`UsesItems`, its action grants), so granting
a behavior adds no declaration. `context.CanMove` is whether it holds a `MovementComponent`. An item's targeting comes from the stack's effective item
(`InventoryQueries.TryResolveEffectiveItem`), not from a catalog lookup made while the module is
wired.

- `NextUnitRandom()` and `Rolls` (what a behavior's `Act` picks with) read the decision's own
  sequence, reseeded by the system for each decision from the behavior stream's seed
  (`RandomStreams.SeedFor(NpcBehaviorModule.BehaviorRandomStream)`), the entity's key and `now` --
  see "Shaped for workers, not threaded". A seeded run stays deterministic, no other module's rolls
  shift, and no creature's choice depends on another's.
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
| `SelfHeal` | Caution | 1 if health < `Parameter` and it holds a usable self-heal -- an item or an action tagged `Effect.Healing` and `Targeting.Self` -- else 0 | Queue the first usable self-heal by priority: stronger spell, weaker spell, stronger consumable, weaker consumable |
| `EngageMelee` | Aggression | 1 if a hostile is adjacent and it holds a usable `Delivery.Melee`-tagged action, else 0 | Queue one usable melee action, picked uniformly (QuickAttack/PowerAttack: parity) |
| `RangedAttack` | Aggression | 1 if the player is hostile and not adjacent, and it holds a usable `Delivery.Ranged`-tagged `Action.Attack` the player is aimable with (`TargetResolution.IsAimable`: in range and in sight) | Queue one, picked uniformly, Target or Ground mode on a coin flip (today's rule, until spatial queries) |
| `LootNearby` | none | 1 if `NpcLooting` finds a corpse it may loot (its retry deadline respected) or an item pile holding something it can carry | Loot it |
| `Flee` | Caution | (`Parameter` + missing health fraction) x hostiles within 3; 0 with none or when it can't move | Step to the adjacent tile farthest from the hostile it is fleeing (the nearest when it starts, kept as the commitment's target) |
| `WanderStep` | none | 1; 0 when it can't move | Random adjacent step; idle 2 s when no tile is free (today's fallback) |
| `Idle` | none | 1 | Wait 2 s |

- **Usable** = the entity holds it, nothing blocks it, and it is ready: `ActivationQueries.GetBlocker`
  (activation effects it can't pay -- no mana for Heal --, a Disabled rule, melee disabled) and
  `FramesUntilReady == 0` (its cooldown; the lock is already a gate), each with its item form. A
  blocker alone isn't enough: cooldown is a timer, not a blocker, and a behavior that queued an
  action on cooldown (the test dummy's Power Attack for 3 s after each swing) would spend every
  visit on a request the activation system drops.

- **Adding a behavior** = one class and one registration line, then a grant on whichever creatures
  should have it. Neither the arbiter nor `NpcDecisionSystem` changes.
- `WanderStep` and `Idle` at equal weight reproduce today's coin flip.
- **No behavior names content by Guid: what a creature does is its grants, and what it does it
  with is what it holds, found by tag.** Deliberate differences from the chain:
  - `SelfHeal` uses any self-heal the creature holds: today that is the Health Potion, the Scroll
    of Healing (both in NPC starting loot) and the Heal spell (20%, 2 mana; granted to Goblins and
    Fairies in Phase 4). `Targeting.Self` keeps out a heal meant for someone else. It asks "usable",
    which today's potion branch doesn't: in the Anti-Literacy zone a creature drinks or casts rather
    than reads, in Anti-Magic it drinks, and out of mana it doesn't try to cast.
    **It picks by priority, not at random: renewable before non-renewable, stronger before weaker**
    -- stronger spell, weaker spell, stronger consumable, weaker consumable -- because mana comes
    back and a potion doesn't. A spell is an action the creature holds; a consumable is an item whose
    use spends a unit or a charge (a Scroll of Healing is a consumable, though what it casts is a
    spell). Strength is the health its `DirectHeal` entries would restore to this creature (percent
    of its maximum plus flat), read from the definition; equal strengths keep the order the creature
    holds them in. The first usable one in that order is used, so nothing is rolled. Today: Heal
    (20%) while it has the mana, then the Health Potion or the Scroll of Healing (50% each).
  - `EngageMelee` attacks with any `Delivery.Melee` action (parent-aware, so Quick Attack's
    `Delivery.Melee.Unarmed` counts), and picks among the usable ones -- today a blocked coin flip
    skips melee for that visit even when the other attack would go.
  - `RangedAttack` casts any ranged attack action, not Magic Missile by name. Still only at the
    player.
- The inventory and action scans behind "holds a usable ..." run only once the cheap condition
  passed (hurt enough; a hostile adjacent; the player in the same neighborhood and not adjacent), and
  what they find is kept on the context for `Act`.
- `LootNearby` splits `NpcLooting.TryLootNearby` into a find (`Evaluate`, read-only, which keeps
  what it found on the context) and a loot (`Act`). Two things the search does today move out of it:
  - The retry deadline (`CorpseLootRetryComponent`, written when every corpse found was reserved)
    is noted on the context and written by `NpcDecisionSystem` after `Decide`, with its existing
    `Merge`.
  - "Can it carry anything from this pile" is found out today by trying. `Evaluate` needs it as a
    read: the test `InventoryActions.LootEveryStack` makes per stack, exposed as a query. If that
    read costs more than the benchmark allows, the fallback is to accept any non-empty pile and let
    `Act` set the same 30 s retry when nothing fit.
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
- **Applied parts**: `EntityBehaviors` (mirroring `EntityActions`) gives the spawn blueprint's set
  for an entity with no `AppliedBlueprintComponent`s; otherwise the set of
  `BlueprintRegistry.Compose(spawn, applied parts in order)`, which interns and caches the
  composition, so a Boss applied mid-fight is resolved once per distinct combination. `Apply` never
  builds a part twice, so Boss applied twice multiplies once. It is asked when an entity is built and
  when a part is applied, and the answer is kept as `NpcDecisionComponent.BehaviorSetId` -- never
  per decision.
- An entity decides if it holds an `NpcDecisionComponent` and isn't the player (see "Who decides").
  This replaces the `MovementMode.Random` gate, `MovementMode`'s only reader, so **`MovementMode` is
  removed** in Phase 4 (the enum, `MovementComponent`'s field and constructor argument, the merge
  action's max, `PlayerKit`'s write). A movement goal, when pathfinding lands, is a behavior's
  commitment and its target, not a mode.
- **Every race gets the same decisions, with its own weights.** One shared list
  (`StandardBehaviors.Grants`, the table below) that each race's definition takes with its weights,
  parameters and considerations -- Human included, treated as any other race. A behavior a creature
  has nothing to do with costs it only a failed "holds a usable ..." check, reached rarely.

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
3. Replacing a behavior or a definition in a mod or a save. Never mid-session: definitions are fixed
   for the session, so a replacement takes effect when the next session loads.

## Starting configurations

Ranks keep today's order: heal, then fight-or-flight, then loot, then wander.

| Behavior (rank) | Goblin | Fairy | Ghost | Human |
|---|---|---|---|---|
| SelfHeal (4), parameter 0.5 | 1 | 1 | 1 | 1 |
| EngageMelee (3) | 1, x `NearbyAlliesFactor(8, 0.25, 8, Inverse: false)` | 1 | 1 | 1 |
| RangedAttack (3) | 1 (holds no ranged attack today) | 1 | 1 (none today) | 1 (none today) |
| Flee (3), parameter 0.1, commit 3 s | **3**, x `NearbyAlliesFactor(8, 0.5, 8, Inverse: true)` | 1 | 1 | 1 |
| LootNearby (2) | 1 | 1 | 1 | 1 |
| WanderStep (1) | 1 | 1 | 1 | 1 |
| Idle (1) | 1 | 1 | 1 | 1 |

Every race has every decision; only the weights differ (to start with). EngageMelee and RangedAttack
can't both apply (adjacent vs. not adjacent). Fairy, Ghost and Human keep neutral weights until their
personalities are picked in-game. Human is a race like the others: the player is skipped by entity
id, not by race, since the player won't always be Human and not every Human is the player. The
Engineer class needs no grants: its race brings them. Phase 4 grants everything except Flee and the
considerations.

The test dummy is not a race in this sense: its only grant is EngageMelee (3), weight 1.

**Heal for Goblins and Fairies** (Phase 4): both races grant the Heal spell (`ActionGrant`), so
`SelfHeal` has a spell to cast as well as potions and scrolls to use.
- Goblin: a `ManaComponent(2, 2)` in its build -- exactly one cast, then mana regeneration earns the
  next. Written by the race so the automatic grant (sized from Intelligence, 5) doesn't replace it.
- Fairy: 15 -> 17 mana, so it can cast everything it could before and Heal (2) once.

**Goblins: cowardly alone, brave in numbers.** One adjacent hostile, nothing to heal with. Flee =
(0.1 + missing health) x 1 hostile x 3, then the multipliers:

| Goblin | Health | Allies within 8 | Engage | Flee | Result |
|---|---|---|---|---|---|
| Alone | 100% | 0 | 1.0 | 0.1 x 3 = 0.3 | Fights 77%, flees 23% |
| Alone | 80% | 0 | 1.0 | 0.3 x 3 = 0.9 | Fights 53%, flees 47% |
| Alone | 50% | 0 | 1.0 | 0.6 x 3 = 1.8 | Flees 64% |
| Pack | 50% | 4 | 1.0 x 2 = 2.0 | 1.8 / 3 = 0.6 | Fights 77% |
| Pack | 50% | 8 | 1.0 x 3 = 3.0 | 1.8 / 5 = 0.36 | Fights (Flee is under the cutoff) |
| Boss, alone | 50% | 0 | 1.0 x 2 = 2.0 | 1.8 x 0.25 = 0.45 | Fights (Flee is under the cutoff) |
| Alone, with a potion, a scroll or mana for Heal | 40% | any | -- | -- | SelfHeal wins at rank 4; rank 3 isn't evaluated |

**With the player's Dread Idol lit (Caution x4, Phase 6):**

| Goblin | Health | Allies within 8 | Engage | Flee | Result |
|---|---|---|---|---|---|
| Alone | 100% | 0 | 1.0 | 0.3 x 4 = 1.2 | Flees 55% |
| Pack | 100% | 4 | 2.0 | 0.3 x 4 / 3 = 0.4 | Fights (a pack's courage beats fear while unhurt) |
| Pack | 50% | 4 | 2.0 | 1.8 x 4 / 3 = 2.4 | Flees 55% |
| Boss, alone | 50% | 0 | 2.0 | 1.8 x 0.25 x 4 = 1.8 | Fights 53% |

A goblin that decides to flee keeps fleeing from the same hostile for up to 3 s, or until nothing
hostile is within 3 tiles, rather than re-rolling after each step. A rank-4 SelfHeal still interrupts it. The numbers are starting
points for in-game tuning.

## Fear: an aura, not a status effect

The earlier plan built a Fear status-effect module (timer component, wheel, applier, display). The
effects work since makes that unnecessary: CLAUDE.md's rule is that "a buff or protection that holds
while an entity is in an area is ... a `StatModifierGrant` with `Stacking: RefreshFromSameSource`".

- **Dread Aura** (`Auras.jsonl`, `BuiltInAuraIds.DreadAura`, its own Guid): one
  `StatModifierGrant(BehaviorCaution, Multiplicative, +3 (x4), Debuff, 2 s, RefreshFromAnySource)`,
  falloff `None` and `Flat` like the null zones -- a stat modifier's magnitude is never scaled by the
  aura's power anyway, so the table above holds at any distance. The aura ticks once a second, so
  the modifier stays while a creature is in reach and lapses up to 2 s after it leaves. One modifier
  per creature however many ticks, by the stacking rule.
- **`StatModifierGrant` gains `FixedDuration`** (default false, so no existing record changes), as
  `ActivationDisableGrant` has: a grant that holds only by being applied again must not be
  shortened below its interval. Without it the 2 s is scaled by the creature's
  `IncomingDebuffDuration`, and anything under x0.5 would make fear flicker between ticks -- the
  yo-yo other games let players abuse (afraid, brave, afraid, each second). The Dread Aura sets it. (Holy Ground's contact buff has the same gap with `IncomingBuffDuration` and
  can set it too.)
- **Dread Idol** (`Items.jsonl`, `BuiltInItemIds.DreadIdol`): a toggle item like Toxic Idol -- a
  `ToggleItem` activator holding an `AuraSourceGrant` of the Dread Aura, no upkeep. Added to the
  player in `PlayerKit` (quantity 1, beside the Toxic Idol, and to its `UsesItems`).
- Carried (turned on in Target mode), the aura leaves the player alone, as every carried source
  does. Turned on in Ground mode the idol is put down and lit where it lies, like any toggle item,
  and then reaches the player like anyone else: the player gains the Caution modifier, which shows
  in the HealthWindow and does nothing (the player has no behaviors). No placer exclusion is added.
- The HealthWindow lists it with the other stat modifiers; the two new targets need display names
  wherever `StatModifierTarget` is named for display.
- Both records are written to the master manifest and canonicalised with `ContentManifestExporter`;
  the Workshop's Items and Auras tabs show them, which is the quickest check that they parse.

## Workshop and diagnostics

- **No new Workshop tab now** (today's are Items, Actions, Auras and Map, all view-only). Blueprints
  aren't content, so grants and modifiers are C# data.
  `ContentSession` builds every module, so a broken grant fails it the same way it fails the game.
  When blueprints become content, `BehaviorGrant`/`BehaviorModifier` and the consideration family get
  codecs (a consideration is a polymorphic family, written with `$`), and a Creatures tab can show a
  blueprint's resolved set. Listed as a follow-up.
- **Benchmarks**: the `TestCombatBehaviorSystem` row becomes `NpcDecisionSystem`. The skill and the
  Debug tab's Compare match rows by name, so Phase 4's A/B compares those two rows by hand, and a
  fresh baseline is saved after it. The world fingerprint changes at Phase 4 (different rolls), so
  compare cost per decision as well as per frame.
- **Gauges** (group `NpcBehavior`, registered where the system is built, counters the system keeps):
  `Decisions` and `CommitmentsStarted`, both `Cumulative`, so the F3 window and the Debug tab show
  decisions per frame beside the system's cost.
- `BuiltInModulesTests` pins system order; the rename is a deliberate change to it.
- **Inspection (Admin Mode only)**: the inspection window shows the inspected NPC's current decision
  information in a block of its own **between the description and the component dump**
  (`InspectionWindowContent`: after `BuildSubjectBlock`, before `BuildAdminDump`), and nothing
  outside Admin Mode. It reads `GameViews.NpcDecisions` (a view, so the block stays off the store
  even though the dump below it is exempt), which sets the inspected entity -- the only one the
  system passes a `DecisionTrace` for -- and returns:
  - what it is doing now: the last decision's behavior and how long ago, or why it isn't deciding
    (dead, idle until, action locked until, mid-move);
  - its commitment, if live: the behavior, the frames left, and the target's name;
  - its behavior set: each grant's rank, resolved weight (base x blueprint modifiers) and parameter;
  - the last decision layer by layer: each evaluated candidate's rank, intrinsic weight,
    considerations, runtime multiplier and final weight, whether the cutoff dropped it, and the pick.

  An entity with no `NpcDecisionComponent` (the player aside, a shop, a chest) shows no block; an
  unbuilt skeleton shows its blueprint's behavior set under the existing "Unsimulated" label and no
  decision. The text is built when the trace or the commitment changes, not per frame (a
  `KeyedCache` keyed on the trace's frame and whole seconds). The block arrives with Phase 4, so it
  is the test tool for Phases 4-6: Phase 5 adds the commitment lines, Phase 6 the runtime multiplier.

## Phases

Stop after each phase for in-game testing. Each ends with what to check.

### Phase 1 -- tag discipline, applied to every existing tag role
1. Engine: `DeclareRoot` with ownership and extensibility, `GameplayTagDomain` and `DeclareDomain`,
   required descriptions, the validator (owned root, depth 4, PascalCase), and a domain on
   `ContentField.Tag`/`TagSet` enforced by the reader.
2. Game: give each root its owner (the roots table: Core for the activation roots, Areas for
   `Area`, whose tags moved to `AreaTags`) and every existing tag a description; declare the
   Activation, Item category, Ability score and Area domains; name the domain
   on every tag field's codec (`ActivatableDefinition`'s and the aura's `t`, the area's `tg`,
   `StatModifierGrant`'s `ct`, `ActivationDisableGrant`'s `all`/`any`/`none`); add domain checks to
   `ContentTagValidation`'s per-record forms and to what is still C#-declared; `InventoryTagQueries`,
   `AbilityScoreTagBonus` and the shop stock query read their domains. ExampleMod and the test
   fixtures' tag declarations gain descriptions and, where they add under a built-in root, rely on
   it being extensible (`Damage`, `Item`).
3. Workshop: a tag's description is its tooltip wherever a detail window lists tags; domain
   violations appear in the Error List (through the session's content failures, no new code).
4. Tests: a module declaring under another module's closed root fails the build, naming both; under
   an extensible one it succeeds; a replacement module inherits its roots; an undescribed tag, a
   fifth level, or a non-PascalCase segment fails; each domain accepts its roots and rejects the
   others (an `Area` tag on an item, a `Stats` tag as an area tag, an `Item` tag in a condition
   outside the Activation roots); a content record with an out-of-domain tag fails with its file, line
   and column; the whole built-in content passes unchanged.
5. In-game check: the game and the Workshop both start with no new failures; inventory tabs, potion
   shop stock, ability-score bonuses and tag-conditioned modifiers (Holy Ground, body-part melee
   penalties) behave as before; the Anti-Magic and Anti-Literacy zones in the starting area
   still refuse exactly what they refused. In the Workshop, hovering a tag in an item's, an
   action's, an aura's or an area's details shows its description. Put an `Area` tag on an item in
   a scratch copy of `Items.jsonl`: the Workshop leaves the record out and lists it in the Error
   List with its file, line and column, and the game refuses to start, naming the same place.

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
   weight 0, ends at the deadline, a stale commitment to a behavior the set no longer holds is ignored,
   the target `Act` set is stored and handed back on each continue and cleared before a fresh pick;
   `Choose` calls no `Act` (the fake behaviors count their calls) and `Choose` then `Act` gives what
   `Decide` gives, for a fresh pick, an interrupt and a continued commitment;
   a lone candidate draws nothing; `Evaluate` is never given a chance to roll (the fake context
   throws on a draw before the pick); the catalog refuses `Register` after `Freeze` and issues ids in
   registration order; no allocation per `Decide`.
3. In-game check: nothing changes. Goblins behave exactly as before.

### Phase 3 -- one way to queue a turn
1. `IntentWriter` (`QueueAction`, `QueueItemActivation`, `Step`, `StepFromActivation`, `Idle`).
   Built over its three pools (`IntentWriter.For(componentManager)`, as `EntityActions.For`) rather
   than as a `GameModuleContext` member: the pending item pool is Inventory's, which a build without
   Inventory lacks, and the writer holds nothing else, so an instance per user costs nothing.
   `PlayerCommands` makes its own from the pools it is already given. `PlayerCommands`, `TestCombatBehaviorSystem` and
   `TestDummyAttackSystem` write through it and no longer hold a pending pool for writing
   (`PlayerCommands` still reads them in `Flush`). `ClearStep`, `SetIdle`, `WithdrawActivationRequests`'
   removals and `PlayerCommands`' own step writes go. IMPLEMENTATION-NOTES' "A queued activation
   clears the step" names `IntentWriter`.
2. Tests: queuing an activation clears the step, whatever its timing; queuing an action withdraws a
   pending item activation and the reverse; a step withdraws both; a null step clears the step;
   `StepFromActivation` leaves pending requests alone; idle sets the wait; queuing twice in a frame
   keeps the newest (the pending pools' merge action); an entity with no `MovementComponent` queues
   without a step to clear; the existing input-buffer, Dodge and windup tests pass unchanged;
   `BuiltInModulesTests` still finds no newly observed pool.
3. Benchmark A/B (headless, fixed seed): the world fingerprint and every row unchanged within the
   run-to-run spread. Built: the fingerprint was identical in two A/Bs against the last commit
   (`6C2F46F7517E414C`, seed 1, frames 600-3600). Costs were inconclusive on a loaded machine (78%
   CPU from other work): the first A/B moved every row ~15-20% together, the untouched
   `AuraSystem` and `TerrainContactSystem` included; the second measured +1.5% overall with the
   baseline's own spread at 39%. Re-measure on a quiet machine if a cost question comes up.
4. In-game check: walking and attacking feel the same; hold a direction and tap an attack -- the
   attack lands and the walk resumes; a Dodge still steps, and a Dodge on cooldown doesn't; drink a
   potion mid-walk and the step stops; goblins still attack and drink potions without stepping on
   the same frame; the test dummy still swings.

### Phase 4 -- the game on the framework
1. `NpcDecisionContext`, `GameModuleContext.NpcBehaviors`, the `Behavior` root (owned by
   NpcBehavior, `Behavior.Drive` extensible) and the Behavior domain, the drive tags (declared, unused
   until Phase 6), the commitment pool (Packed, `Unobserved`, written find-or-add through a scope),
   and `NpcDecisionComponent` (Packed, `Unobserved`; added by `EntityBuilder`, its set id rewritten by
   `Apply`; `IntentWriter.Idle` writes its wait).
2. Behaviors: SelfHeal, EngageMelee, RangedAttack, LootNearby, WanderStep, Idle, each finding what
   it uses by tag and asking "usable" (split `NpcLooting` into a read-only find and a loot; the "can
   it carry anything from this pile" query; the retry written after `Decide`).
3. `BlueprintDefinition.Behaviors`/`BehaviorModifiers`, `ResolvedBlueprint.Behaviors`,
   `EntityBehaviors` with `Compose` for applied parts.
4. `StandardBehaviors.Grants` on Goblin, Fairy, Ghost and Human (the table minus Flee and
   considerations); Heal granted to Goblin (with 2 mana) and Fairy; the test dummy gets a race and
   an EngageMelee grant, and `TestDummyAttackSystem` and `TestDummyComponent` are deleted.
5. `NpcDecisionSystem` replaces `TestCombatBehaviorSystem` (deleted), wired to the
   `NpcDecisionComponent` pool, with the per-decision derived rolls and the `NpcBehavior` gauges; it
   runs each bucket as two passes (every due entity chooses into the system's `NpcDecisionRecord`
   list, then each record acts in order), and in Debug the context refuses a write while choosing. The
   module no longer reads the Health Potion at wiring. `MovementMode` is removed.
   `GameViews.NpcDecisions` and the Admin Mode decision block in the inspection window, between the
   description and the components (what it is doing, its behavior set, the last decision's trace).
   IMPLEMENTATION-NOTES' stand-in entry is rewritten; the TODO.md entry is deleted, what it says
   about worker threads being covered by `PLAN-multithreading.md`; TODO's "Resolve highest performance
   costs" and CLAUDE.md name the new system.
6. Tests: **parity per situation, not per roll sequence.** Rolls are derived per decision and drawn
   in a different pattern (one weighted draw instead of a coin flip, none for a lone candidate), so a
   seeded run won't repeat the old chain roll for roll. Instead, scenario tests captured from the old
   chain before deletion: for each situation (low health with only a potion, an adjacent hostile, the
   player in range and in sight, the player in range behind a wall, a lootable corpse beside it, a
   reserved corpse beside it, an item pile beside it, nothing near), the same decision, or the same
   choices at the same rates over many seeded trials (QuickAttack/PowerAttack, Target/Ground,
   idle/step). Also: a creature's choice is the same whatever order the bucket is visited in and
   whoever chose before it; no creature acts until every due creature in the bucket has chosen; two
   creatures that both choose the same corpse or pile: the first loots, the second idles and writes
   nothing else, and a corpse the first only partly emptied is still looted by the second; an
   `Evaluate` that queues an intent throws in Debug; SelfHeal uses the stronger spell before the weaker, any spell before any
   consumable, and the stronger consumable before the weaker, rolling nothing; it skips the scroll in
   Anti-Literacy, the scroll and the spell in Anti-Magic, and the spell without mana, and ignores a
   healing item not tagged `Targeting.Self`; a Goblin's 2 mana pays for one Heal, and a Fairy's 17
   for three Magic Missiles and one; the decision view reports nothing for an entity that isn't the
   inspected one, and the last trace, set and gate reason for the one that is; a creature in the
   Anti-Magic zone doesn't queue Magic Missile and still wanders; an action on cooldown is never
   queued; a reserved corpse sets the 30 s retry once; a Human NPC decides and a non-Human player
   doesn't; a stationary creature fights, heals and idles but never queues a step, and
   `MovementSystem` never visits it; a mover lands in the same stripe and tier bucket in both
   systems; the idle wait gates a stationary creature; the test dummy winds up Power Attack only with
   a hostile beside it and waits out its cooldown; `Apply` rewrites the set id once; a mod
   registering a new behavior and replacing a built-in by Guid; a blueprint
   naming an unknown behavior fails `ResolveAll`; the "nothing near me" gate means a lone creature's
   decision never evaluates EngageMelee or LootNearby past the gate; `BlueprintSkeletonTests` and
   `SpawnRecordRebuilderTests` still pass (grants are on the definition, nothing new per entity).
7. Benchmark A/B (headless, fixed seed): `NpcDecisionSystem` against `TestCombatBehaviorSystem`'s
   row, per frame and per decision, with the two passes' cost told apart from the framework's (one
   run acting straight after each choice, for the comparison only); then save the new baseline.
8. In-game check: goblins wander, fight adjacent enemies with both attacks and loot corpses; below
   half health a goblin drinks a potion, reads a Scroll of Healing or casts Heal (once, then waits
   for mana); drop an item beside a goblin and it picks it up; a Fairy casts Magic Missile at the
   player from range, and not through a wall; lure one into the Anti-Magic zone and it stops casting
   and, hurt, drinks instead; stand beside the test dummy and it winds up Power Attack, dodge it,
   step away and it stops swinging; "Spawn here > GoblinFairy" (stationary) beside the player and it
   attacks without ever moving; F3 shows `NpcBehavior/Decisions`. A hurt goblin with mana casts Heal
   first and only then drinks or reads. Press F12 and inspect a goblin: between its description and
   its components the window shows what it last decided, why it isn't deciding right now (idle,
   locked) and its grants with ranks and weights, updating as it acts; turn Admin Mode off and the
   block is gone; inspect a shop and there is none.

#### Phase 4 as built (2026-11-13)

Where the build differs from the steps above, and what it measured:
- **A set's id is its blueprint's id.** `NpcBehaviorSets` resolves one set per blueprint and keeps it by the blueprint's
  session id, so `NpcDecisionComponent.BehaviorSetId` is that id; an entity with applied parts decides with the set of
  `BlueprintRegistry.Compose(spawn, applied...)`, resolved once per distinct combination.
- **The component is added by a build step.** `EntityBuilder` runs `IEntityBuildStep`s after a build and after each
  `Apply` (`EntityFactory.AddBuildStep`; the rebuilder's builder copies the session's), and `NpcDecisionBuildStep` adds or
  rewrites `NpcDecisionComponent` there -- an empty set holds none, so chests and shops are never visited.
- **Idle is the context's, not `IntentWriter`'s:** it writes `NpcDecisionComponent.WaitUntilFrame`. `MovementComponent`
  lost `WaitUntilFrame` with `MovementMode`.
- **`LocalTierRoster` follows deciders** (`NpcDecisionComponent`), not movers: a stationary fighter winds up too, and the
  map telegraphs only the Local windups the roster names (found in the in-game check: the dummy's Power Attack had no
  shape). An open entity inspection follows Admin Mode (`MapViewState.EntityInspectionMode`).
- **Admin inspection block:** `NpcDecisionView` (`GameViews.NpcDecisions`) and Presentation's `NpcDecisionText` -- the
  gate, the last decision with its trace, any commitment, the grants with rank, weight and parameter -- between the
  description and the components, Admin Mode only; inspecting names the one entity the system traces.
- **Cost** (Release, headless, seed 1, A/B against HEAD `b24568ed` on a quiet machine): `NpcDecisionSystem` 0.44
  ms/frame against `TestCombatBehaviorSystem`'s 0.31 (+0.13, +43%); `EcsContext.Update` 1.02 against 0.87 (the worlds
  differ, so only roughly comparable). A one-pass variant read 0.42-0.49, so the two passes cost ~2% (noise). Per
  decision in Debug: EngageMelee ~1.0 µs, RangedAttack 0.7, SelfHeal 0.4, LootNearby 0.2, the rest the arbiter's. About
  560 decisions a frame. Reported, not chased.
- **Tests** cover the situations above that a whole session shows (`NpcDecisionSystemTests`, `FairyMagicMissileTests`,
  `NpcLootingTests`) and the framework's own (`Tests/Decisions`); the full parity-by-rates sweep was not written.

### Phase 5 -- modifiers, considerations, Flee, commitment
1. Boss's `BehaviorModifiers` (Aggression x2, Caution x0.25).
2. `NearbyAlliesFactor` and the context's cached ally/hostile counts.
3. `Flee`, with its commitment and the commitment's target (the hostile it runs from), granted to
   every race at its weight.
4. The goblin configuration above.
5. Tests: Boss through an include (Goblin Foreman) and through `Apply` (once only); a modifier on a
   missing behavior does nothing; `NearbyAlliesFactor` counts (self and dead excluded, radius edge,
   cap, other layer ignored); a hostile behind a wall isn't counted for Flee; the goblin table's rows as
   decision tests; a committed Flee interrupted by SelfHeal; a committed Flee keeps running from its
   target when a second hostile comes nearer, and ends when the target dies or is destroyed (its key
   no longer resolves); a stationary creature never flees; a new commitment is added the first time
   and rewritten in place after (one pool entry per entity, never removed).
6. Benchmark a goblin-heavy fight (the ally scan is the new cost).
7. In-game check (Admin Mode "Spawn here >"): a lone goblin hit to half health usually runs and keeps
   running for a few seconds; a pack of five holds; "Apply > Boss" to a fleeing goblin and it turns
   back to fight; a hurt goblin with an enemy on the far side of a wall doesn't run from it.


#### Phase 5 as built (2026-11-13)

- **A fresh Flee starts only with a hostile beside it** (`HasHostileAdjacent`), as the choice against EngageMelee; once
  committed it keeps running while its target lives and any hostile is in sight within 3. As first written, a hostile
  merely in sight 2-3 tiles away made Flee the only rank-3 candidate, which always wins -- every creature would run
  from anything it saw, and with no approach behavior nothing would ever close to melee. The table above is the
  adjacent case and holds as written.
- **Cornered doesn't flee:** Flee applies only when a step takes it farther from what it runs from (the step is found in
  `Evaluate` and carried in `NpcDecisionFindings.FleeStep`), so a cornered creature fights.
- Perception on the context: `HostilesInSight` (radius `HostileSightRadius` = 3 from its footprint, line of sight over
  `GameModuleContext.SightBlockers`, a creature larger than a tile counted once) with `NearestHostileInSight`, and
  `AlliesWithin(radius)` (`NpcDecisionServices.IsAllyOf`, beside `IsHostileTo`), each read at most once a decision.
- Grants: `StandardBehaviors` holds each grant and `Grants` (Flee added, weight 1); `GoblinGrants` is the goblin row
  (Flee x3 with pack fear, EngageMelee with pack courage). Boss carries the two modifiers.
- **Cost:** the machine was loaded for both runs (AuraSystem 0.58 and 0.91 ms against its usual ~0.26), so no absolute
  figure; relative to AuraSystem the decision system is no dearer than in Phase 4 (1.5x it, against 1.7x then). The
  in-sight scan runs only for a creature with a hostile beside it or a live Flee commitment.
- Tests: `NpcFleeTests` (Boss by include and by Apply, ally counting, sight, the goblin table's rows sampled over 400
  seeded decisions, stationary and two-tiles-away never fleeing, the commitment's target, its end on the target's
  destruction, SelfHeal's interrupt).
### Phase 6 -- runtime drives, Dread Idol, inspection
1. `StatModifierTarget.BehaviorAggression`/`BehaviorCaution` (appended), display names, and
   `NpcDrives`' table behind `RuntimeWeightMultiplier`; `StatModifierGrant.FixedDuration` with its
   codec key.
2. Dread Aura and Dread Idol records, their built-in id constants, `PlayerKit` (and its `UsesItems`).
3. The decision block's trace gains the runtime multiplier per candidate (Phase 5 added its
   commitment lines: behavior, frames left, target).
4. Tests: a timed Caution modifier raises Flee's weight and then expires; the lit, carried idol puts
   one refreshed modifier on goblins in reach and none on the player; put down, it reaches the player
   too; unlit, it lapses within 2 s; a creature with a x0.25 `IncomingDebuffDuration` keeps the
   modifier between ticks (`FixedDuration`); the Dread table's rows as decision tests.
5. In-game check: light the idol in Target mode beside a lone, unhurt goblin (it usually runs),
   beside an unhurt pack (it holds), and beside a Boss at half health (about even); inspect a goblin
   and see the Caution x4 in its trace and in its HealthWindow debuffs; put the idol down in Ground
   mode and walk away -- goblins near it stay afraid, and the player's HealthWindow shows the modifier
   while standing in it.

#### Phase 6 as built (2026-11-13)

- `NpcDrives.RuntimeTargets` maps each drive to its stat target; `NpcDecisionContext.RuntimeWeightMultiplier`
  multiplies the effective value (base 1, floored at 0) of every drive a behavior serves. Read only for a candidate
  that already has weight. The trace and the decision block already carried the runtime multiplier from Phase 4, so
  step 3 needed no change.
- `StatModifierGrant.FixedDuration` (`fd`) skips every duration scaling, scroll scaling included, and declares no
  duration modifiers. The Dread aura (`BuiltInAuraIds.DreadAura`) is `BehaviorCaution` x4 (magnitude 3), 120 frames,
  `RefreshFromAnySource` (one modifier whichever idol is credited, so two idols never add), `Flat`, falloff `None`; the Dread Idol (`BuiltInItemIds.DreadIdol`) holds it at size 4, a
  Delayed toggle like the Toxic Idol. Both records canonicalise unchanged through `ContentManifestExporter`.
- **Bug found and fixed:** the decision's health fraction and SelfHeal's heal sizing read the stored maximum, not the
  one with MaximumHealth modifiers. A Boss (health x2, current raised with it) read about 160% healthy, so it never
  fled and healed itself only far too late. Both now read the modified maximum.
- Goblin vital parts were raised before this phase (Head 90, Torso 100, Internal 90; total 390), so no hit short of a
  crit kills a goblin from full health -- the Flee check needed goblins to live through half health.
- **Cost:** headless Release, 3 runs: NpcDecisionSystem 0.68 ms against AuraSystem 0.35 (1.9x, against 1.5x in Phase
  5). Not comparable: tougher goblins make a different world (fights last longer, so more creatures have a hostile
  beside them and run the sight scan). Reported, not chased.
- Tests: `NpcDreadTests` (a timed Caution modifier raises Flee and expires; the carried idol holds one modifier on
  goblins in reach and none on the player; put down it reaches the player; put out, fear lapses within 2 s;
  `FixedDuration` keeps it through x0.25 debuff duration; the Dread table's rows; an unhurt Boss reads full health).

### Deferred -- errands
When "NPCs use shops" starts, a committed behavior remembers its shop through the commitment's
target, which the engine already tracks (Flee uses it from Phase 5). Commitment gives the rest:
persistence across acts, interrupts from higher ranks, and ending when its behavior says it's done. The spatial-queries TODO's claimable spots and blackboard
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
  a write and a read while removing nothing; see "Performance"). Every timing clears the step; NPCs
  gain the player's rule that queuing one kind of activation withdraws the other.
- Built for a later parallel decide, not threaded: `Evaluate` and considerations only read, `Act` is
  the only writer, and each decision's rolls are derived from the entity's key and the frame
  (confirmed 2026-11-11; from the TODO entry's "design the decide step to run on worker threads").
- Choosing and acting are two passes over each bucket from Phase 4 on, single-threaded: the arbiter
  has `Choose` and `Act`, what `Evaluate` finds travels as an `unmanaged` value, and `LootNearby.Act`
  re-checks its target and idles when a neighbour took it. A creature can lose a visit that way; in
  exchange a later fork (`PLAN-multithreading.md`) changes no seeded world (2026-11-12).
- What a creature does is its grants, and what it does it with is found by tag among what it holds
  (confirmed 2026-11-11): no behavior names an action or item by Guid.
- Stationary creatures fight. Who decides is `NpcDecisionComponent` (the behavior set id and the
  idle wait), not the movement pool; `MovementSystem` is untouched and a mover is still decided and
  moved on the same frame. The test dummy is the first: a race of its own and an EngageMelee grant
  replace `TestDummyAttackSystem` and `TestDummyComponent` (2026-11-11).
- Every race gets the same decisions with its own weights, Human included; the player is skipped by
  entity id, never by race (2026-11-11).
- `SelfHeal` uses any item or action tagged `Effect.Healing` (and `Targeting.Self`); Goblins and
  Fairies get the Heal spell, Goblins with mana for exactly one cast and Fairies 17 so they can cast
  each of theirs once more (2026-11-11). It picks by priority -- stronger spell, weaker spell,
  stronger consumable, weaker consumable -- because renewable resources are spent before
  non-renewable ones.
- Other races attacking the test dummy (it has a race now) is accepted until factions exist.
- In Admin Mode the inspection window shows the NPC's current decision information between the
  description and the components, from Phase 4 on (2026-11-11).
- The engine tracks a commitment's target: `BehaviorCommitment` holds an `EntityKey` (2026-11-11).
- Deciding and stepping stay two systems (2026-11-11); `MovementMode` is removed (2026-11-11).
- Fear is the Dread Aura's `StatModifierGrant` on Caution, not a status-effect module; the Dread Idol
  is a toggle item in the player's starting kit. The grant has a fixed duration, so no modifier can
  make fear yo-yo between ticks (2026-11-11).
- No Admin Mode weight tuner, and no new Workshop tab until blueprints are content.

## Open questions

The five from the 2026-11-11 review are settled (see Decisions). Deciding and stepping stay two
systems because the player's steps go through `MovementSystem` too, the saving is about half of
0.14 ms/frame, and a serial step after a (later) parallel decide is the shape the worker design
wants; revisit with "Grid pathfinding and navigation". Nothing is open.
