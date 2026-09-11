# Processing Tier Rework

Design for moving `ProcessingTierSystem` from a periodic distance scan over movers to
tier-on-create plus event-driven transitions over every positioned entity.

## Why

`ProcessingTierSystem`'s membership is `MovementComponent` (70,266 entities). Seventeen tiered
consumers drive off thirteen different pools, most of which stationary entities populate. Anything
in one of those pools without a `MovementComponent` resolves to Beyond at
`TieredEntityStripeSet.OnMemberAdded` and **stays Beyond permanently** -- nothing will ever tier it.
Measured: `ProcessingTierComponent` = 70,267, exactly `MovementComponent` + the player.

Live examples today: ~59,325 `Lava` entities (aura sources, no `MovementComponent`), plus shops and
treasure chests carrying `SimpleHealthComponent`/`ActionLockComponent`. A shop three tiles from the
player is throttled identically to lava 900 tiles away.

## The coupling that makes this safe

**Switching membership to `TransformComponent` alone would be catastrophic.** That is 2,082,027
entities against 70,266 today -- a ~30x increase on a system that already costs ~6 ms/sec, so a
naive swap lands near 180 ms/sec, more than the entire simulation currently costs.

It is only affordable because the periodic scan goes away at the same time. The two changes are one
change.

## Is the movement check cheaper than the distance check?

Yes, decisively, and the reason is structural rather than a close call:

**Neighborhood, Borough and Beyond are fixed grid cells, not player-relative.**
`ProcessingTierSystem` classifies by `SameCell(position, playerPosition, 1000)` and
`SameCell(..., 2000)` -- absolute grid cells the player happens to occupy. An entity's
Neighborhood/Borough/Beyond classification therefore changes **only when the player crosses a
1000- or 2000-tile cell boundary**, which on the current 1000x1000 map is never, and on a Borough
map is rare. When it does happen it is a wholesale reclassification, but it is an event, not a
per-frame concern.

**Only Local is continuous.** As the player moves one tile, the set of entities within Chebyshev 80
changes by a single boundary column or row. With `LocalExitBufferTiles = 16` hysteresis, that is
the column entering at `px + 80` and the column leaving at `px - 96`.

**The map is already spatially indexed.** `Map` holds creature occupancy per `(x, y, MapLayer)` and
a non-Blocking index (`GetBlockingEntityId` / `GetNonBlockingEntityIdsAt`), so walking a boundary
column is a direct array read, not a search.

So the per-player-step cost is O(boundary), roughly 2 x 192 tile probes for an axis-aligned step,
against O(population) for the scan it replaces. An entity's own move is O(1) -- recompute that one
entity. There is no case where the scan wins.

This matches the spatial-hash area-of-interest pattern used for MMO interest management, including
the grace range (add at radius R, remove at radius R + buffer) that
`LocalRadiusTiles`/`LocalExitBufferTiles` already implements.

## Feedback on the proposed spawn sequence

Proposed:

1. Pause the game
2. Spawn the terrain
3. Spawn the player at a random non-terrain tile
4. Fix the tiers of the terrain entities
5. Spawn the NPCs with correct tiers

This is sound, and the ordering instinct is right -- player position before anything that needs to
be tiered relative to it. Three suggested changes:

### 1. Split "choose the player's position" from "create the player entity"

Phase 4 exists only because terrain is created before the player's position is known. But the
constraint driving phase 3's placement ("don't spawn inside terrain") needs the *map layout*, not
the player *entity*. Decide the position from the layout, then create terrain already tiered:

1. Pause
2. Generate the map layout as data (which tiles will be floor/wall), no entities yet
3. Choose the player's spawn position from that layout
4. Create terrain entities, each tiered on creation against the now-known player position
5. Create the player entity at the chosen position, pinned Local
6. Create NPCs, each tiered on creation

**Phase 4 of the original disappears entirely.** No fix-up pass, and no window in which entities
exist untiered.

### 2. Prefer tier-on-create over a correct sequence

The stated goal is "to prevent a repeat of a previously repeated bug". A sequence that must be
followed correctly is exactly the kind of thing that regresses; the bug already recurred four times
in this family. If every entity receives its tier as part of creation -- as the first component,
with the rest deriving from it -- then ordering stops being load-bearing for correctness. It only
governs the player-position constraint, which is a single explicit dependency rather than an
invariant spread across every call site.

Concretely: an entity that gains a `TransformComponent` should gain a `ProcessingTierComponent` in
the same breath. Then no consumer can ever observe an untiered positioned entity, and
`TieredEntityStripeSet.OnMemberAdded`'s fail-open-to-Beyond default becomes genuinely unreachable
for positioned entities rather than the silent norm it is today.

### 3. Do not let the pause carry correctness weight

Pausing during construction is reasonable hygiene. But with tier-on-create there is no window of
untiered entities to protect, so nothing should *depend* on the pause. If it does, the sequence has
a latent ordering bug that a future change to the pause behaviour will expose. Keep the pause;
don't rely on it.

### Industry alignment

The revised order matches how streamed and generated worlds normally do it: pick the world spawn
point, then generate and activate the region around it, rather than generating first and
retrofitting. Minecraft's world spawn plus its spawn-chunk region is the familiar example, and its
simulation distance -- entities outside the radius receive no ticks at all -- is close to where the
P2 direction below is heading.

## Open question: the player's tier reference point

"The player should be Local always, even in a sub-map" is straightforward for the player itself --
its distance from itself is 0, so `ComputeTier` can only answer Local, and it can be pinned once at
spawn and never re-examined.

The reference point every **other** entity's tier is computed from, while the player is off-map or
in a sub-map, is **the player's last on-map position**. A player removed from the map is returned
to that same position, so the last-known position is not a stale guess -- it is where the player
will actually be when they come back, which makes tiers computed against it correct on return
rather than merely plausible while away.

Consequences to hold onto:

- `ProcessingTierSystem` needs to retain that position rather than reading it live. Today
  `Update` bails when the player has no `TransformComponent`; instead it should fall back to the
  retained value, and only bail before the player has *ever* been placed.
- The retained position updates on every player move, so it is one `Vector3Int` field, written on
  the same event that already drives the Local boundary walk.
- Entities on the map the player left keep meaningful tiers while away, so nothing has to be
  re-tiered wholesale on return -- only whatever moved.
- See `TODO.md`'s per-map Pause modality item: if the departed map is frozen outright, its tiers
  simply stay as they were, which the retained-position model already gives for free.

## Status: IMPLEMENTED

All seven steps below have landed. `dotnet test`: 1,635 of 1,636 pass; the one failure is the
pre-existing wall-clock `GrantDefaults_OneHundredThousandEntities_WithinBaselineTolerance`, which
touches none of this (it registers only StatModifiers and AbilityScores), passes in isolation every
time at ~133 ms, and fails under full-suite parallel load against a 180 ms threshold.

### Where the implementation deviated from the outline, and why

**Step 1's hook point was wrong.** The outline said to assign the tier at
`World.PlaceEntityOnMap`. That is too late: `TestMapBuilder` runs `blueprint.Build(...)` -- which
adds every component, including every tiered-pool one -- *before* it places the entity, so every
tiered consumer has already run `OnMemberAdded` against an untiered entity by the time placement
happens. The tier has to exist before the blueprint runs. What landed instead:

- **`ProcessingTierResolver.CreateEntityAt(entityManager, plannedPosition)`** -- the resolver
  creates the entity *itself* and writes its tier as the first component, silently. Owning creation
  is deliberate: a silent write on an entity already in tiered pools would leave them all holding it
  at the fail-open Beyond default, and nothing could rescue it later (EnsureTiered compares against
  the stored tier, which the silent write already set). Owning creation makes that misuse
  unrepresentable rather than documented. Used by `TestMapBuilder`'s three bulk paths (race
  entities, terrain, layer-explicit walls) -- effectively all ~2.6M entities.
- **`World.EntityPlaced` -> `ProcessingTierResolver.EnsureTiered`** -- the catch-all, wired in
  `GameBootstrapper`. Any placement through `World` gets a correct tier whether or not its caller
  asked, raising `TierChanged` only when the tier is new-and-not-Beyond or different. Covers the
  `PlaceAt` sites whose Z comes from the blueprint (fixtures, shops, Ground walls) and any spawn path
  that does not exist yet. Tolerates an unwired resolver rather than throwing, since it runs on every
  placement in the game.

The invariant from step 1 ("every blueprint adds `TransformComponent` before any tiered-pool
component") therefore **stopped mattering** -- tier-first happens before the blueprint runs at all.
It is replaced by an end-to-end test (`FloorBuilderTests.PopulateFloor_WithTierResolver_...`)
asserting that every entity the map indexes is born correctly tiered and that no terrain entity was
tiered by event. That test was mutation-checked: disabling tier-first makes it fail on exactly the
terrain assertion.

**The spawn sequence was not restructured into phases.** The goal of the phased sequence was
correct tiers at creation without a fix-up pass. That is achieved by setting the tier reference
position to `FloorBuilder.PlayerSpawnOrigin(world)` *before* `PopulateFloor`, rather than by
reordering population:

- Terrain and NPCs are born tiered against the spawn origin; no fix-up pass exists.
- The player lands on the nearest free cell to that origin, possibly a few tiles away.
  `ProcessingTierSystem`'s first update treats the difference as an ordinary player move and walks
  the Local edges, so it reconciles itself.
- Reordering `TestMapBuilder`'s interleaved per-tile loop would have changed its RNG consumption,
  and so the map every existing seed produces. Not worth it for no correctness gain.
- The phased structure is still the right shape for the planned maze-like map generator, which
  will naturally produce layout-as-data first. Revisit then.

**Entity moves are buffered, not retiered at the move.** Retiering synchronously inside
`MovementSystem` would raise `TierChanged` mid-iteration and swap-remove from the very bucket list
`MovementSystem` is walking as a span. So `ProcessingTierSystem` drains the shared move buffer
after `MovementSystem` runs -- which is why `ProcessingTierModule` now declares a dependency on
`MovementModule`, making the required ordering a declaration rather than a coincidence of the
module list.

**`LocalTierRoster` needed a change the outline did not anticipate.** It learned Local membership
only from `TierChanged`, on the grounds that a new entity had no tier yet. Under tier-first, a mover
born Local never raises `TierChanged`, so the roster (and the enemy-telegraph path that reads it)
would silently have seen nobody. It now also admits on the driving pool's `EntityAdded`, and filters
`TierChanged` to movers -- tiering covers terrain now, and the roster exists to be the small side.

**Every tiered system implements `ITieredSystem` -- 18 of them, not ~15.** Including the two that
used `GetDueEntities` without owning a countdown (`DelayedActionSystem`, `BodyPartEffectsSystem`),
so no tiered system calls `GetDueEntities` any more and the P2 policy applies to all of them.
`TestCombatBehaviorSystem`'s helper lost its `framesPerVisit` parameter: it owns no countdown now
that `MovementSystem` owns `FramesToWait`.

**Found and fixed along the way:** a stale class doc on `StatModifierExpirySystem` still described
Defect E as "the same fidelity tradeoff every ProcessingTier consumer accepts" -- i.e. as intended.
Corrected. Three regen systems kept a dead `_processingTiers` field from the per-entity lookup that
was removed earlier. Removed.

### Measured effect -- partly unresolved

Two agreeing-ish measurement windows each side, warmup discarded:

| | Before (avg of 2) | After (avg of 2) |
|---|---:|---:|
| `ProcessingTierSystem` | 6.18 | **1.98** |
| `EcsContext.Update` | 136.08 | 175.41 |

`ProcessingTierSystem` fell 68% -- the scan this rework targets is gone. The total rose 29%, and
that rise is **not attributed**. It has a combat-intensity signature (every combat system up
together, `ComplexHealthRegenSystem` down), the rework legitimately changes *when* entities are
processed (promotions are immediate rather than lagging up to four seconds), and the within-run
swing is as large as the delta (`ActionLockSystem` read 18.96, 9.19, 19.79 across three consecutive
windows of one run). A clean A/B is not available: the pre-rework state was uncommitted working
tree. Treat the total as unmeasured until the harness samples long enough to beat the variance.

## Implementation outline

1. **`ProcessingTierComponent` on creation.** Hook wherever an entity gains a
   `TransformComponent` -- `World.PlaceEntityOnMap` is the natural chokepoint since it already owns
   placement -- and assign the tier there, before any other component is added.
   - **Invariant this depends on, unverified:** every blueprint must add `TransformComponent`
     before any tiered-pool component, or the tiered consumer's `OnMemberAdded` runs before the
     tier exists. `Lava` does (Transform is added before `DamageOnContact` and the aura source).
     The rest have not been checked. Assert it -- a test that builds every blueprint and records
     component-add order is cheap -- rather than assuming it.
2. **Pin the player once. DONE.** `ProcessingTierSystem.PinPlayerLocalOnce` sets a flag on the
   first Update after the player has a position and never re-examines it, so a player who leaves
   the map stays Local. The retained last-known on-map position (see the open question above) is
   also implemented, with tests for both.
3. **Membership -> `TransformComponent`.** `ProcessingTierSystem` wires to the transform pool.
4. **Delete the periodic scan.** Replace with two event handlers:
   - entity moved -> recompute that entity's tier, O(1)
   - player moved -> walk the Local boundary slabs via `Map`'s position index, and on a
     1000-/2000-cell crossing, reclassify wholesale
   - **Hysteresis means two slabs, not one.** Promotion happens crossing radius 80
     (`LocalRadiusTiles`), demotion crossing radius 96 (`+ LocalExitBufferTiles`). The promote
     candidates are tiles newly inside radius 80 around the new position; the demote candidates
     are tiles newly outside radius 96 around the old one. The symmetric difference of a single
     box pair is *wrong*: an entity at distance 85 can be inside both radius-96 boxes and still
     cross 80 as the player approaches, and that shortcut would never promote it.
   - Z change, teleport, or any move larger than the slab width -> full rebuild of the Local set
     rather than slab walks.
5. **Propagate on change.** `ProcessingTierEvents.TierChanged` already exists and every
   `TieredEntityStripeSet` already subscribes, so consumers follow automatically once the tier
   moves. This is the "all components follow the tier" requirement -- it is already wired, and
   needs verifying rather than building.
6. **Move the tier loop into `SystemManager`** -- see the section below. Land alongside steps 3-5
   so each system is touched once, not twice.
7. **Tests** for: tier assigned at creation, boundary crossing in both directions with hysteresis
   (including the distance-85 case above), a stationary entity (lava, a shop) receiving a correct
   non-Beyond tier -- the case that is broken today -- and the blueprint component-order
   invariant from step 1.

## Moving the tier loop into `SystemManager`

Today about 15 tiered systems each repeat the same loop:

```csharp
for (var tierIndex = 0; tierIndex < _tieredStripeSet.TierCount; tierIndex++)
{
    var framesPerVisit = _tieredStripeSet.GetTierFramesPerVisit(tierIndex);
    foreach (var entityId in _tieredStripeSet.GetTierBucket(tierIndex, time.FrameCount))
    {
        // ... work scaled by framesPerVisit
    }
}
```

Proposed: `SystemManager` owns that loop and hands each tiered system its bucket and
`framesPerVisit`.

### Why

1. **It removes the defect family's easiest failure path.** Five defects found this session were a
   tiered system either decrementing by a constant rather than `framesPerVisit`, or walking
   `GetDueEntities`, which cannot expose `framesPerVisit` at all. With the value in the method
   signature and `GetDueEntities` gone from tiered systems, the right thing becomes the default
   path. It does not make the wrong thing *impossible* -- a system can still ignore a parameter.
   Closing it fully means centralising the decrement as well, which is the P2/P5 `CountdownTicker`
   work.
2. **It is where P2's policy wants to live.** P2's direction is that only Local and Neighborhood
   are simulated. With the loop in `SystemManager` that is one line -- iterate tiers 0-1. With the
   loop spread across systems it is ~18 edits, each a chance for one system to be missed. This is
   the strongest argument, and why this should land *before* P2 rather than after.
3. **It deletes ~15 copies of the loop**, plus `SystemManager`'s per-system `stripeIndex`
   bookkeeping, which tiered systems already ignore because they key off `EngineTime.FrameCount`.

### Shape

```csharp
public interface ITieredSystem
{
    TieredEntityStripeSet Tiers { get; }

    /// Once per frame, before any bucket -- for work that is not per-entity.
    void BeginFrame(EngineTime time) { }

    /// Once per simulated tier per frame.
    void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit);
}
```

- **Bucket granularity, not per-entity.** One interface call per tier per system per frame -- four
  per system, ~80 per frame total -- and each system keeps its tight inner loop. A per-entity
  callback would add a virtual call per entity, which is measurable on the 70k-entity systems.
- **`BeginFrame` exists for systems with per-frame work outside the tier loop.** The canonical case
  is draining a `FrameEventBuffer` once per frame.
- **The tier policy is injected; the loop is not game knowledge.** Walking `TierCount` is generic,
  and `TieredEntityStripeSet` already lives in `Engine`, so the loop moves down cleanly. *Which*
  tiers to simulate is game knowledge, and `CLAUDE.md` keeps `Engine` free of it -- so `Game`
  supplies the policy at bootstrap (a highest-simulated-tier index, or a predicate). `Engine` never
  learns what "Neighborhood" means.
- **`SystemManager` profiling is unchanged** -- it still times each system as a unit, summing its
  `BeginFrame` and bucket calls.

### Systems that stay plain `ISystem`

- **`StatusEffectAuraSystem`** runs *two* tiered passes: exposures ticked per tier with
  `framesPerVisit`, then a source-resync pass via `GetDueEntities` (correctly -- the resync owns no
  countdown). One stripe set per system cannot express that. Keep it plain and running its own
  loops rather than bending the interface around one system.
- **`ProcessingTierSystem`** becomes event-driven under this plan and does not loop over tiers at
  all.
- The deliberately untiered systems from the scheduling audit (`DodgeExpirySystem`, the two
  activation systems, `DeathSystem`, `ContainerDestructionSystem`, `AchievementPollingSystem`).

### Cost

Mostly test churn: about 20 systems' tests call `system.Update(time, 0)` directly. Rather than
routing every test through a `SystemManager`, add one small shared helper that runs the same tier
loop, used by both `SystemManager` and the tests. One implementation of the loop, so the two
cannot drift -- the same class of drift that let `RunFullCycle` in `PoisonSystemTests` silently
stay correct only while `StripeCount` was 1.

Performance is expected to be neutral.

## Related, deliberately out of scope

**P2 -- Borough and Beyond simulation.** Direction: only Local and Neighborhood are simulated;
Borough and beyond are spawn-in-and-wait until they reach a closer tier. Once the tier loop lives
in `SystemManager` (above), the "which tiers are simulated" half of this becomes a single policy
value injected from `Game`. That is close to
Minecraft's simulation distance (no ticks at all outside the radius) and is a different question
from *cadence*, which is all the tier system currently expresses. It needs its own plan, including
the "living world" research (Dwarf Fortress and its competitors) and the bulk-DoT ordering problem
already filed in `TODO.md`.
