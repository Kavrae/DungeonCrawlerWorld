# NPC Behavior Composition + Turn Claimed Signal

(Pre-implementation. Replaces TODO.md's "NPC behavior composition + generalized "turn claimed" signal"
entry, which is deleted when Phase 2 lands. Unblocks "NPCs use shops" and "Mobs looting corpses".)

## Context

What exists today:
- `TestCombatBehaviorSystem` (`Game/Modules/NpcBehavior/Systems/`) is a hardcoded chain for every
  `MovementMode.Random` entity: self-heal (below 50% health, holds a Health Potion), then melee (an
  adjacent entity of a different race; QuickAttack or PowerAttack on a coin flip), then wander (coin
  flip between idle and a random adjacent step). The first branch that fires wins.
- It is an `ITieredSystem` with the same `StripeCount` (15) and tier divisors as `MovementSystem`,
  wired to the same `MovementComponent` pool, so an entity is decided and moved on the same frame.
  It has been the largest per-frame cost in the game before, and the tiering fixed that. Any
  replacement has to be at least as cheap.
- `NpcBehaviorModule` is registered before `MovementModule`, so the decision runs before
  `MovementSystem`. `ActionsModule` and `InventoryModule` are registered after it, so
  `ActionActivationSystem` and `ItemActivationSystem` drain the queued requests later in the
  same frame.
- A decision is written as one of three requests: `PendingActionActivationComponent`,
  `PendingItemActivationComponent`, or `MovementComponent.NextMapPosition`/`WaitUntilFrame`.
- `MovementSystem.UpdateEntity` skips an entity that has either pending activation component
  (`//TEMPORARY replace with a more generic mechanics`). That's the check the TODO entry is about:
  every new request type would need another pool injected and another `Has` here.
- The check looks redundant today. `TestCombatBehaviorSystem` only decides when `NextMapPosition`
  is null or already reached, and `PlayerCommands` clears `NextMapPosition` when it writes an
  action or consumable, so nothing currently has both a step and an activation queued. It holds by
  convention, not by structure. Phase 1 confirms this with a test before removing anything.
- `TestDummyAttackSystem` is a second decision system: a plain `ISystem` (stripe 1) that fires
  Power Attack at its Adjacent ring whenever its action lock clears.
- Hostility is "has a race different from mine" (`IsAttackable`). There are no factions and no NPC
  marker (TODO.md "NPC component").
- Race definitions already carry per-race data that's read through the spawn record and never
  stored per entity: `Actions` (merged across includes, a later grant replacing an earlier one),
  `Appearance`, `Race`. `EntityActions` resolves an entity's actions from its own instances, then
  its applied parts, then its spawn blueprint.

## Research: how other games do it

| Game / technique | Arbitration | Per-type configuration | Multi-turn intent | Fit here |
|---|---|---|---|---|
| **NetHack** (`monmove.c`, `muse.c`) | Fixed code chain: `find_defensive` (heal/escape items) -> `find_offensive` -> `find_misc` -> move | Monster flags (`M1_*`, `M2_*`) gate branches | None; re-decided each turn | This is what `TestCombatBehaviorSystem` is today |
| **DCSS / Brogue** | A behavior state (`BEH_WANDER`/`SEEK`/`FLEE`, Brogue's `MONSTER_WANDERING`/`TRACKING_SCENT`/`FLEEING`) plus per-ability conditions | Data flags per monster type (`fleesNearDeath`, `MAINTAINS_DISTANCE`, `MA_AVOID_CORRIDORS`), spell slots with their own use chance | The state persists across turns | Good precedent for data-driven per-race flags on top of a small fixed set of behaviors |
| **RimWorld** ThinkTree | Tree of nodes; `ThinkNode_Priority` takes the first child that issues a job, `ThinkNode_PrioritySorter` sorts children by a computed priority; a separate "constant" tree can interrupt | `ThinkTreeDef` XML per race/kind (humanlike, animal, mechanoid), mods insert subtrees by tag | A `Job` runs its `JobDriver` toils over many ticks until it ends or is interrupted | Per-race trees and "one current job" are both relevant. The full tree is more machinery than 4 behaviors need |
| **Caves of Qud** `Brain` | A goal stack (`GoalHandler`s). When the stack is empty the creature is "bored", and AI parts (`AISelfPreservation`, `AIShootAndScoot`, `AITryKeepDistance`, `AIFlocks`, ...) react by pushing goals. Items advertise their own uses to the AI through events | AI parts are attached to a creature in its blueprint XML, the same way as any other part | The goal stack *is* the multi-turn intent (flee for N turns, go to X) | Closest to "compose behaviors from parts on the blueprint". Its goal objects are per-creature allocations, which our GC budget can't afford at 660k entities |
| **Utility AI / IAUS** (Dave Mark and Mike Lewis, GDC AI Summit 2013/2015) | Every behavior scores 0..1 from "considerations" (input -> response curve), multiplied together; the highest score wins | Data-driven weights and curves per archetype | None inherently | Makes aggressive vs. cowardly a question of weights instead of code. Scores every behavior every time: no early exit |
| **Dual utility** (Kevin Dill, *Game AI Pro*) | Each behavior returns a **rank** (a priority bucket) and a **weight**. The highest non-empty rank wins, and a weighted/argmax pick is made inside it | Ranks and weights as data | None inherently | Keeps the cheap "first bucket that fires wins" early exit of a priority chain, plus utility-style trade-offs where they matter |
| **Behavior trees** (Halo 2, most engines) / **GOAP** (F.E.A.R.) | Tree traversal / A* over world-state actions | Tree or action-set assets | Running nodes / plans | Overkill for the current behavior count. A GOAP planning pass per decision is out of the question at our entity counts |
| **ECS utility frameworks** (e.g. Utility Intelligence for Unity DOTS) | Utility scoring run as a batch job per decision, with the chosen decision's "action tasks" writing components | Decision assets | Components written by tasks | Confirms the ECS-idiomatic split: the decision writes intent components, and separate systems execute them. We already do that |

The pattern that recurs across all of these: **stateless behavior logic + per-type data selecting and
weighting it + the decision writing an intent that separate systems execute.** They differ in
arbitration (first-that-fires vs. score) and in whether an intent persists across turns.

## Part 1 -- the "turn claimed" signal

### Options

**A. A marker component (`TurnClaimedComponent`).** A decision system adds it, `MovementSystem`
checks `Has`, and something clears it at the end of the frame.
- Con: an add plus a remove per decision per entity (pool churn on the hottest path in the game), a
  clearing system with its own ordering constraint, and a new pool.

**B. A frame stamp on the action lock: `ActionLockComponent.TurnClaimedAtFrame` (`uint`).**
`ActionLockGate.ClaimTurn(pool, id, now)` writes `now`, and `ActionLockGate.IsTurnClaimed(pool, id, now)`
compares it. `MovementSystem` gates on `IsBlocked(...) || IsTurnClaimed(...)`.
- This is the Timers rule's "nothing happens, it just stops gating" shape: a plain `uint` and a
  comparison at the reader. No system, no clearing, and nothing to do per entity when it goes stale.
- It sits on the component every decider already reads to answer "may this entity act?", through
  the one class that owns that question (`ActionLockGate`).
- Cost: `ActionLockComponent` goes from 8 to 12 bytes (Packed pool, every creature).
- It can't be folded into `UnlockedAtFrame` (for example "lock until now + 1"): that would make
  `ActionActivationSystem`'s own `IsBlocked` check reject the very activation that claimed the turn,
  later in the same frame.

**C. One intent slot per entity (`EntityIntentComponent`, a union of Move / Action / Consumable)**
that replaces the three request channels, like `PlayerCommands`'s single slot or RimWorld's
single current `Job`.
- Pro: double-booking a turn becomes structurally impossible.
- Con: rewrites `ActionActivationSystem`, `ItemActivationSystem`, `PlayerCommands`,
  Dodge's step-on-activation and every test that queues a request. FreeCast has to coexist with a
  move, so it isn't really one slot anyway.

**D. Rely on ordering and delete the check** (it is redundant today).
- Con: the guarantee is again "every writer happens to clear the other channels", which is what the
  TODO entry says doesn't scale.

### Recommendation: B, written through one chokepoint

Add `IntentWriter` (Game, next to `ActionLockGate`), the only way to queue a turn-taking request:
`QueueAction(entityId, actionId, targetTiles, now)`, `QueueItemActivation(entityId, stackInstanceId,
targetTiles, now)`. Each writes the pending component *and* claims the turn, so no caller can forget
to claim (the same reasoning as the timer wheels observing `ComponentChanged` so no caller has to
remember to schedule).
- Callers: `TestCombatBehaviorSystem` (later `NpcDecisionSystem`), `TestDummyAttackSystem` and
  `PlayerCommands`, which are the only three writers of the pending components today.
- FreeCast actions don't claim the turn: they don't wait for the lock, and a FreeCast plus a step on
  the same frame is legitimate. `IntentWriter.QueueAction` reads the action's timing category to
  decide.
- A move doesn't claim: it *is* the thing being gated.
- `MovementSystem` loses its two optional pending-pool parameters.


## Part 2 -- behavior composition

### Arbitration options

**1. Per-race ordered priority list** (NetHack, DCSS, RimWorld's `ThinkNode_Priority`). Each race
lists behavior ids in order, and the first that fires wins.
- Pro: cheapest (early exit, the same cost profile as today), deterministic, easy to read.
- Con: no trade-offs. "Cowardly" can only mean "Flee is listed higher", not "flees sooner".

**2. Pure utility scoring** (IAUS). Every behavior scores 0..1, with per-race weights, and the
highest score wins.
- Pro: personalities are just weights, and trade-offs come for free.
- Con: every behavior is evaluated on every visit (no early exit), on the system that was once the
  most expensive in the game. Unrelated behaviors must be calibrated against each other on one
  shared scale.

**3. Dual utility: rank + weight** (Kevin Dill). Ranks separate categories that must never compete.
Weights only decide between behaviors in the same rank.
- Pro: keeps option 1's early exit and readability, and adds utility only where two behaviors
  really compete (fight or flight).
- Con: slightly more machinery than option 1.

**4. Goal stack** (Caves of Qud). Behaviors push goals that persist across turns.
- Pro: natural fit for multi-step errands (walk to a shop, buy, return).
- Con: per-creature goal objects are the kind of allocation the Scale notes warn about. None of the
  current behaviors need memory across turns.

### Decision: option 3 (dual utility); a small activity slot later if needed

## How a decision is made

Each grant's `Rank` is a **ceiling**: a behavior's `Evaluate` may return that rank or lower (0 = not
applicable), never higher. Grants are pre-sorted by that ceiling at resolve time, which is what makes
the early exit valid. Once a candidate exists at rank R, no grant whose ceiling is below R can win,
so it isn't evaluated at all.

```
winnerRank = 0; candidates.Clear()
(see "Commitment" below for what happens first when the entity is committed)
foreach grant in grantsSortedByRankCeilingDescending:
    if grant.Rank < winnerRank: break                        // early exit
    (rank, intrinsic) = behavior.Evaluate(ref context, grant)
    if intrinsic <= 0 or rank < winnerRank: continue
    weight = intrinsic * grant.Weight                         // base x blueprint modifiers (cached)
           * Product(grant.Considerations, ref context)       // situational (pack courage)
           * DriveMultiplier(behavior.Drive, entity)          // runtime (stat modifiers)
    if weight <= 0: continue
    if rank > winnerRank: winnerRank = rank; candidates.Clear()
    candidates.Add(grant, weight)
drop candidates with weight < CutoffFraction (0.25) x best
pick = WeightedRandom(candidates, mathUtility)                // seeded, so deterministic
pick.Behavior.Act(ref context, pick.Grant)
```

- **Weighted random, not argmax.** A behavior with twice the weight is twice as likely. That gives
  individuals variety, and it replaces today's hardcoded coin flips with data (see Wander below).
- **The cutoff** (0.25 of the best weight in the winning rank, a constant for now) stops a 2% option
  from ever winning a roll.
- Everything multiplies, so each layer is independent and order doesn't matter. Considerations and
  the drive lookup are only computed for a candidate whose intrinsic weight is already above 0, so a
  goblin with nothing adjacent never counts its allies.

## Decision cadence: how often an entity decides

An entity only runs the arbitration loop when it's actually free to act. Today's gates stay, in
cheapest-first order, ahead of any behavior evaluation:

1. Dead -> skip.
2. `MovementComponent.IsWaiting(now)` (the idle backoff) -> skip.
3. `ActionLockGate.IsBlocked` -> skip.
4. `ActionLockGate.IsTurnClaimed` (Phase 1) -> skip.
5. Mid-move (`NextMapPosition` not yet reached) -> skip.
6. Otherwise: evaluate.

So yes, the action lock already gates the decision. A locked visit costs a handful of pool reads
and returns; it never touches a behavior. Every act then locks the entity again:

| Act | Locked until the next decision for |
|---|---|
| Attack (QuickAttack / PowerAttack) | the action's own `ActionLockFrames` (about 1 s for the standard lock) |
| Step (wander, flee) | the movement lock: `StandardLockFrames` (60 = 1 s), x√2 for a diagonal |
| Drink a potion | the standard lock (1 s) |
| Idle | `WaitUntilFrame`, 120 frames (2 s) |

**The result is one decision per act, about once a second for a free NPC.** The loop isn't
re-evaluated every striped frame.

Visits come every `StripeCount` (15) x tier divisor frames: every 15 frames (0.25 s) at Local, and
every 120 frames (2 s) at Neighborhood. Borough and Beyond aren't simulated. One consequence exists
today and this plan doesn't change it: an NPC acts on its first visit *after* its lock clears, so it
reacts up to 14 frames late at Local and up to 119 at Neighborhood. Skipping locked entities
entirely (waking each one on its unlock frame with a timer wheel) would remove that latency and the
locked-visit reads, but it would bypass tiering. It's not worth it while a locked visit is this
cheap.

## Commitment: behaviors that last

Re-deciding after every act is right for attacks, but wrong for Flee. Flee acts one step at a time,
so it would be re-rolled after every step. With weighted random, a 55/45 fight-or-flight split
re-rolled each second is a goblin that shuffles back and forth next to its attacker. Three options:

| Option | How | Problem |
|---|---|---|
| Re-evaluate every act (today) | Nothing to add | Dithering, as above |
| Momentum bonus | The previous behavior's weight x1.5 on the next roll | Still re-rolls every step, just with loaded dice. No notion of time |
| **Commitment deadline + interrupts** (recommended) | The winner stays chosen until a deadline, or until it reports it's done, or a higher rank interrupts | One small component |

This is also what the reference games do: a RimWorld `Job` runs until it ends or a higher-priority
think node interrupts it, and a Qud goal stays on the stack until it pops.

**Data.** `NpcBehaviorGrant.CommitFrames` (`ushort`, 0 = re-decide after every act, the default).
Flee's goblin grant uses 3 s. Engage, SelfHeal, WanderStep and Idle stay at 0 (Idle already persists
through `WaitUntilFrame`).

**State.** `NpcCommitmentComponent { ushort BehaviorId; uint CommittedUntilFrame; }`, 8 bytes in a
Packed pool. `BehaviorId` is the catalog's session-local id (like `BlueprintRegistry`'s `ushort`
ids), not the Guid. It's written with `Merge` when a behavior with `CommitFrames > 0` wins, and never
removed: an expired deadline is simply stale, so there's no churn and no expiry system. That's the
same "nothing happens, it just stops gating" timer shape as the action lock. Only entities that have
ever committed hold one.

**The loop, when committed** (deadline not reached, and the entity still has that behavior):

```
committed = the grant for commitment.BehaviorId
// 1. Interrupts: only grants ranked above the commitment are evaluated.
run the normal loop over grants with ceiling > committed.Rank
if it produced a winner: act on it (it may start its own commitment); done
// 2. Continue: ask the committed behavior whether it still applies.
(rank, intrinsic) = committed.Behavior.Evaluate(ref context, committed.Grant)
if intrinsic > 0: committed.Behavior.Act(...); done          // no re-roll against same-rank rivals
// 3. It's done early (Flee: no hostile within 3 tiles), so fall through to the full loop.
```

- **Interrupts**: a fleeing goblin that drops below the SelfHeal threshold with a potion drinks it,
  because rank 3 is above Flee's rank 2. Nothing at Flee's own rank or below can interrupt, which is
  the point.
- **Ending early**: Flee's `Evaluate` returns 0 once no hostile is within 3 tiles, so the goblin
  stops running when it's safe instead of running for the full 3 s.
- **Cheaper, not dearer**: a committed entity evaluates only the higher ranks plus one behavior.
- Deadline-based, so it's correct at every tier. A Neighborhood-tier goblin visited every 2 s just
  gets fewer flee steps in its 3 s.
- It also covers the old "persistent activities" idea: the Deferred section below now only adds a
  target to this same component.

## Design

### Four layers of weight

| Layer | Where it lives | When it's computed | Example |
|---|---|---|---|
| **Base grant** | `BlueprintDefinition.Behaviors` on a race (or any definition) | Resolve time, cached | Goblin: Flee weight 3 |
| **Blueprint modifiers** | `BlueprintDefinition.BehaviorModifiers` on any definition | Resolve time, cached (plus applied parts) | Boss: Aggression x2, Caution x0.25 |
| **Considerations** | On the grant, as shared immutable instances | Per decision, lazily | Goblin: pack courage within 8 tiles |
| **Drives (runtime)** | `StatModifierComponent`s on the entity | Per decision, only if the entity has any | A Fear effect: Caution x3 for 10 s |

### Behaviors

```csharp
public interface INpcBehavior
{
    Guid Id { get; }
    string Name { get; }
    /// Which drive scales this behavior, or null for none (Wander).
    BehaviorDrive? Drive { get; }
    /// Rank <= grant.Rank (0 = doesn't apply) and an intrinsic weight >= 0 from the situation alone.
    BehaviorEvaluation Evaluate(ref NpcDecisionContext context, in NpcBehaviorGrant grant);
    /// Writes the intent (IntentWriter / MovementComponent). Only called on the winner.
    void Act(ref NpcDecisionContext context, in NpcBehaviorGrant grant);
}
```

- Stateless singletons, registered by Guid in `NpcBehaviorCatalog` (session-local; re-registering a
  Guid replaces it, like `ActionCatalog`). The catalog also issues each behavior a session-local
  `ushort` id, which is what `NpcCommitmentComponent` stores. A module adds its behaviors in a
  `RegisterBehaviors(NpcBehaviorCatalog)` step, so a mod can add "Kite" or replace "Flee".
- **Adding a new behavior type** = one class implementing `INpcBehavior`, one line registering it,
  and a grant on whichever races should have it. Nothing in `NpcDecisionSystem` changes.
- Built-ins:

| Behavior | Drive | Intrinsic weight | Act |
|---|---|---|---|
| `SelfHeal` | Caution | 1 if health < `grant.Threshold` and holds a Health Potion, else 0 | Queue the potion |
| `EngageMelee` | Aggression | 1 if a hostile is adjacent, else 0 | Queue one of the entity's `Tag.Melee` actions, picked uniformly (QuickAttack/PowerAttack today, so parity holds) |
| `Flee` | Caution | (`grant.Threshold` + missing health fraction) x hostiles within 3 tiles; 0 with no hostile | Step to the adjacent tile that maximizes distance from the nearest hostile |
| `WanderStep` | none | 1 | Random adjacent step (today's move branch) |
| `Idle` | none | 1 | Wait `FramesToWaitIfNoOptions` (today's idle branch) |

`WanderStep` and `Idle` at the same rank and weight reproduce today's 50/50 coin flip; a lazier race
gets a higher `Idle` weight.

Flee's `Threshold` is a baseline panic. Without it, Flee's weight would be 0 at full health, and no
multiplier (a Fear effect included) could ever make an unhurt creature run.

### Grants: per-race configuration

```csharp
public sealed record NpcBehaviorGrant(
    Guid BehaviorId,
    byte Rank,
    float Weight = 1f,
    float Threshold = 0f,
    ushort CommitFrames = 0,
    IReadOnlyList<IBehaviorConsideration>? Considerations = null);
```

- `BlueprintDefinition.Behaviors : IReadOnlyList<NpcBehaviorGrant>`. Resolved with `Actions`' rule: a
  later grant of the same behavior id replaces an earlier one. `Rank = 0` removes it.
- `Threshold` is a generic parameter each behavior interprets (SelfHeal: the health fraction; Flee:
  the baseline panic).
- `CommitFrames` is how long the behavior stays chosen once it wins (see Commitment); 0 means
  re-decide after every act.
- Grants are declared once per race as `static readonly` data and shared by every creature, the way
  `Goblin.QuickAttackOverride` is. No per-entity memory.
- **Configuring a race** = one `Behaviors` array on its definition.

### Blueprint modifiers: how a trait changes behaviors it doesn't own

```csharp
public sealed record NpcBehaviorModifier(float WeightMultiplier, BehaviorDrive? Drive = null, Guid? BehaviorId = null);
```

- `BlueprintDefinition.BehaviorModifiers : IReadOnlyList<NpcBehaviorModifier>` on any definition
  (trait, class, composite). It targets either every behavior with a given drive, or one behavior by id.
- Targeting a drive is what makes Boss work without knowing the behavior list: `Aggression x2`
  boosts `EngageMelee` and any future fighting behavior (a mod's "Charge"), and `Caution x0.25` damps
  `Flee` and `SelfHeal`.
- Resolve applies every modifier from the whole include chain to the merged grants and caches the
  result. Multiplication commutes, so include order doesn't matter. A modifier aimed at a behavior the
  entity doesn't have does nothing (Boss on a shopkeeper).
- Runtime `Apply` of a trait works through the applied list, mirroring `EntityActions`: an entity with
  no applied parts reads its spawn blueprint's cached grants; one with applied parts folds their grants
  and modifiers in on the fly (few grants, rare entities). Since `Apply` never builds a part twice,
  applying Boss twice multiplies once.

```csharp
// Game/Blueprints/Parts/Boss.cs
public static readonly NpcBehaviorModifier[] BehaviorModifiers =
[
    new(WeightMultiplier: 2f, Drive: BehaviorDrive.Aggression),
    new(WeightMultiplier: 0.25f, Drive: BehaviorDrive.Caution),
];
```

### Considerations: situational factors

```csharp
public interface IBehaviorConsideration
{
    /// A multiplier >= 0. Returning 0 vetoes the candidate.
    float Evaluate(ref NpcDecisionContext context);
}
```

- Immutable parameter objects, shared across every creature of a race. **Adding a new consideration**
  = one class.
- First one: `NearbyAlliesFactor(byte Radius, float PerAlly, byte MaxAllies, bool Inverse)`.
  `Inverse = false` gives `1 + PerAlly x n`; `Inverse = true` gives `1 / (1 + PerAlly x n)`. `n` =
  living, simulated allies within `Radius` (Chebyshev, same layer), excluding self, capped at `MaxAllies`.
- "Ally" goes through the context's `IsAllyOf(candidate)`, the other half of the `IsHostileTo` seam.
  Today it means "same primary race", so a Goblin Foreman or Goblin Engineer counts as a goblin.
- **Cost**: radius 8 is 17 x 17 = 289 tile lookups. It's only paid when a candidate already has
  intrinsic weight above 0 (a hostile is adjacent, so a real fight is on), and the count is cached on
  the context per radius, so Engage and Flee share one scan. If the benchmark objects, the fallback is
  a per-neighborhood-chunk count of creatures per race, maintained by `World`'s placement and moves.

### Drives: runtime adjustment

```csharp
public enum BehaviorDrive : byte { Aggression, Caution }
```

- Each drive maps to a new `StatModifierTarget` (`BehaviorAggression`, `BehaviorCaution`).
  `DriveMultiplier` is `StatModifierMath.GetEffectiveValue(statModifiers, entityId, target, 1f)`.
- So **runtime per-entity adjustment reuses the whole stat-modifier machinery**: timed expiry,
  sources, stacking, and any `StatModifierGrant` on an action, potion or aura. A Fear aura, a Rage
  potion, a taunt, or a morale break when the leader dies are all just stat modifiers. Nothing in the
  behavior system knows about them, and nothing is added for entities without modifiers.
- Drives rather than per-behavior-id runtime modifiers: a Fear effect shouldn't need to list every
  cautious behavior, and a mod's new "Cower" behavior tagged `Caution` is affected by Fear
  automatically. (`StatModifierTarget` is an enum, so a mod can use the existing drives but not add
  one. That's acceptable until a mod needs its own.)
- Three runtime routes, from most to least permanent:
  1. `EntityFactory.Apply` a trait with `BehaviorModifiers` (a curse, a promotion). Permanent.
  2. Stat modifiers on a drive (effects, items, auras). Timed or permanent, stackable.
  3. Re-registering a definition (how mods replace a race). Invalidates the resolve cache for every
     entity of it.

### `NpcDecisionContext` and `NpcDecisionSystem`

- `NpcDecisionContext` is a `ref struct` holding the entity id, `now`, the transform, pools and
  queries, plus **lazily cached perception**: health fraction, adjacent tiles, hostiles adjacent,
  hostiles within 3, allies within R (keyed by radius). Each is computed on first ask.
- `NpcDecisionSystem` replaces `TestCombatBehaviorSystem`: the same `ITieredSystem` shape, stripe
  count and wiring, and the same gates (dead, waiting, action lock, turn already claimed, mid-move).
  An entity decides if its resolved behavior set is non-empty and it isn't the player. This replaces
  the `MovementMode.Random` gate; `MovementMode` stays a movement-only concern.

## Starting configurations

| Behavior (rank) | Goblin | Fairy | Ghost |
|---|---|---|---|
| SelfHeal (3), threshold 0.5 | 1 | 1 | 1 |
| EngageMelee (2) | 1, x pack courage `NearbyAlliesFactor(8, 0.25, 8, Inverse: false)` | 1 | 1 |
| Flee (2), threshold 0.1, commit 3 s | **3**, x pack fear `NearbyAlliesFactor(8, 0.5, 8, Inverse: true)` | 1 | 1 |
| WanderStep (1) | 1 | 1 | 1 |
| Idle (1) | 1 | 1 | 1 |

Fairy and Ghost keep neutral defaults until you pick their personalities in-game.

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
| Alone, with a potion | 40% | any | -- | -- | SelfHeal wins at rank 3; rank 2 isn't evaluated |

**With the player's Dread Idol lit (Fear: Caution x4, see Phase 4):**

| Goblin | Health | Allies within 8 | Engage | Flee | Result |
|---|---|---|---|---|---|
| Alone | 100% | 0 | 1.0 | 0.3 x 4 = 1.2 | Flees 55% |
| Pack | 100% | 4 | 2.0 | 0.3 x 4 / 3 = 0.4 | Fights (a pack's courage beats fear while unhurt) |
| Pack | 50% | 4 | 2.0 | 1.8 x 4 / 3 = 2.4 | Flees 55% |
| Boss, alone | 50% | 0 | 2.0 | 1.8 x 0.25 x 4 = 1.8 | Fights 53% |

A goblin that decides to flee keeps fleeing for up to 3 s (or until nothing hostile is within 3
tiles) rather than re-rolling after each step. The exact numbers are starting points for in-game
tuning.

## Phases

Stop after each phase for in-game testing.

### Phase 1 -- turn claimed signal
1. Add a test first: an NPC with a queued activation and a stale `NextMapPosition` doesn't move, to
   pin down today's behavior before changing how it's guaranteed.
2. `ActionLockComponent.TurnClaimedAtFrame`, `ActionLockGate.ClaimTurn`/`IsTurnClaimed`.
3. `IntentWriter.QueueAction`/`QueueItemActivation`. Route `TestCombatBehaviorSystem`,
   `TestDummyAttackSystem` and `PlayerCommands` through it.
4. `MovementSystem`: replace the pending-pool check with `IsTurnClaimed`, and drop the two
   constructor parameters and the `//TEMPORARY` comment.
5. Tests: claim/expiry by frame, FreeCast doesn't claim, player activation claims, and existing
   Dodge/input-buffer tests still pass. A/B benchmark `MovementSystem` (the component grows by 4 bytes).

### Phase 2 -- behavior framework, at parity
1. `INpcBehavior`, `BehaviorEvaluation`, `NpcBehaviorGrant`, `NpcDecisionContext`,
   `NpcBehaviorCatalog` (owned by `NpcBehaviorModule`, exposed on `GameModuleContext` like `Actions`).
2. `BlueprintDefinition.Behaviors`: resolve, merge, sort by rank ceiling, and cache on
   `ResolvedBlueprint`. `EntityBehaviors` handles applied parts. `ResolveAll` validates behavior ids,
   so a broken mod fails its dry run.
3. The arbitration loop (rank ceiling, early exit, cutoff, seeded weighted random).
4. Port SelfHeal, EngageMelee, WanderStep and Idle. Grant them on Goblin, Fairy and Ghost at parity
   weights (no Flee, no considerations yet).
5. `NpcDecisionSystem` replaces `TestCombatBehaviorSystem` (deleted). The IMPLEMENTATION-NOTES
   "temporary stand-in" entry gets rewritten.
6. Tests: arbitration (ceiling respected, early exit skips lower ranks, cutoff, weighted pick
   distribution over a seeded run), merge rules (a later grant replaces, rank 0 removes), a seeded
   parity test against the old chain captured before deletion, and a mod adding and replacing a
   behavior by Guid.
7. Benchmark A/B: `NpcDecisionSystem` must not be slower than `TestCombatBehaviorSystem`.
8. Delete the TODO.md entry.

### Phase 3 -- modifiers, considerations, Flee, goblin personality
1. `NpcBehaviorModifier` + `BlueprintDefinition.BehaviorModifiers`, folded at resolve and for applied
   parts. `BehaviorDrive` on behaviors. Boss gets Aggression x2, Caution x0.25.
2. `IBehaviorConsideration`, `NearbyAlliesFactor`, and the context's cached ally/hostile counts.
3. `FleeBehavior`.
4. Commitment: `NpcBehaviorGrant.CommitFrames`, `NpcCommitmentComponent`, and the committed path of
   the loop (interrupts from higher ranks, continue, end early).
5. Goblin configuration as in the table above.
6. Tests: Boss modifier through include (Goblin Foreman) and through `Apply` (applied once only), a
   modifier on a missing behavior is a no-op, `NearbyAlliesFactor` counts (self excluded, dead
   excluded, radius edge, cap, other layer ignored), and the goblin table's scenarios as decision
   tests. Commitment: a committed Flee isn't re-rolled against Engage, a rank-3 SelfHeal interrupts
   it, it ends early with no hostile in range, it ends at the deadline, and a stale commitment to a
   behavior the entity no longer has (an applied part changed its grants) is ignored.
7. Benchmark in a goblin-heavy fight (the ally scan is the new cost).
8. In-game: spawn goblin packs and lone goblins with Admin Mode "Spawn here >", and "Apply > Boss" to
   one mid-fight.

### Phase 4 -- runtime drives, Fear, Dread Idol, inspection

**Toggle items have landed** (aura stage 2: `ToggleItemActivator`, `ToggleSpec`, `ToggleItemActions.TryToggle`,
`ToggleItemHolderSync` -- see IMPLEMENTATION-NOTES.md "Toggles: items and actions"), so the Dread Idol is a
toggle item like Toxic Idol. Its aura needs a Guid of its own: the build refuses two different aura
definitions sharing one.

1. `StatModifierTarget.BehaviorAggression`/`BehaviorCaution` and `DriveMultiplier` in the loop.
2. **Fear status effect** (`Game/Modules/Fear/`, modelled on Paralysis):
   - `StatusEffectType.Fear`, and a `FearAuraApplier : IStatusEffectAuraApplier` registered in
     `FearModule.Configure`, so an aura can grant it.
   - Not stacking. Applying it while active pushes the expiry to the later of the two (Paralysis's and
     Poison's rule). Duration 2 s, so it lasts a moment after leaving the aura's reach and lapses
     after that. The aura system re-applies it every tick while the creature is in range.
   - Effect: one `StatModifierComponent` on `BehaviorCaution`, multiplicative, x4 (magnitude +3),
     added when Fear starts and removed when its `FearTimerComponent` (an `IScheduledTimer` driving a
     `PackedTimerWheel`) fires. So there's one modifier per feared creature however many times the
     aura refreshes it, not one per tick.
   - Respects `StatusEffectImmunity` like every other effect. An `IStatusEffectDisplay` shows it in
     the HealthWindow's buffs/debuffs.
   - It does nothing to the player (no behaviors), and the aura system already excludes a source from
     its own aura.
3. **Dread Idol** (`Game/Modules/Inventory/Definitions/DreadIdol.cs`): a toggle item built exactly
   like the toggle-items plan's Toxic Idol, with
   `AuraSourceGrant(StatusEffectType.Fear, strength, <color>)`. Registered in `CoreItemsModule` and
   added to the player's starting kit in `PlayerKit` (quantity 1, next to the Toxic Idol).
4. Admin inspection readout: the entity's resolved grants (after blueprint modifiers), its current
   commitment and deadline, and its last decision's candidates with each weight layer, so "why did it
   do that?" is answerable in-game. Kept on a debug-only side table, not a component.
5. Tests: a timed Caution modifier raises Flee's weight, then expires. Fear: applied by the aura,
   refreshed rather than stacked (one modifier however many ticks), removed on expiry, immunity
   respected. The idol toggled on applies Fear to goblins in range and toggled off stops it. The
   Fear table's scenarios as decision tests.
6. In-game: light the idol near a lone goblin (it runs), near a pack (it holds while unhurt), and
   near a Boss.

### Deferred -- errands (the Qud idea, done cheaply)
Only when "NPCs use shops" or "Mobs looting corpses" starts: add an `EntityKey Target` to
`NpcCommitmentComponent`, so a committed behavior remembers what its errand is about (the shop, the
corpse). Commitment already gives the rest: the errand persists across acts, higher ranks can
interrupt it, and it ends when its behavior reports it's done. One slot rather than a stack, and a
struct rather than objects. The target is held by `EntityKey` per the id-recycling rule.

## Decisions

- Dual utility (rank ceiling + weighted random with a 0.25 cutoff).
- Blueprints modify behaviors through `BehaviorModifiers`, by drive or by behavior id. Boss:
  Aggression x2, Caution x0.25.
- Goblins: Flee weight 3, with pack courage and pack fear from other goblins within 8 tiles.
- `TestDummyAttackSystem` stays its own system, as a special case, and is only routed through
  `IntentWriter`.
- Runtime drives ship with an in-game source: the Fear status effect and the Dread Idol toggle item
  in the player's starting kit.
- No Admin Mode weight tuner.
- Decisions are gated by the action lock (one decision per act). Behaviors can commit for a set time
  (Flee: 3 s), interruptible only by higher ranks.
- Goblin Flee keeps a 0.1 baseline panic: an unhurt lone goblin flees 23% of the time it's engaged,
  and Fear can move an unhurt goblin.
