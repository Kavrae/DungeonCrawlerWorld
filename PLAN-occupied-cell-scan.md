# Plan: scan occupied cells through the column masks

From TODO "Resolve highest performance costs", "NpcDecisionSystem, further": the shared nearby-occupant pass reads nine
tiles a wanderer through the occupant index, and one bit per cell could cut it.

Status: **complete** (2026-11-24). Phases 1, 2, 4, 5 and 6 done; Phase 3 measured and not kept; Phase 7 found no bug.
The results are in IMPLEMENTATION-NOTES "Occupied-cell scans".

**Results need not match earlier builds.** Where it simplifies the logic, this plan may change entity order, tie breakers
and decision paths. A seed must still decide everything within one build.

## The finding that shapes this plan

**The map already holds one occupancy bit per cell.** `MapNeighborhood.OccupiedLayerMaskByColumn` has a byte per column,
and bit N is set exactly when the cell on MapLayer N has an occupant. Its bytes are row-major, X fastest, so a row of a
scan is a contiguous run of bytes.

So this plan adds **no new data layer**: no memory and no extra write on any move. A separate `NeighborhoodBits` would
repeat the same fact, and its reads also write its last-neighborhood cache, which `PLAN-multithreading.md`'s read-only
worker sections could not share.

What a scan pays today, per tile: an interface call, a neighborhood lookup, then the mask read. What it pays after: one
interface call per scan, one neighborhood lookup per neighborhood segment, and a byte test per tile. An occupant span is
built only for a tile that has one.

## The scan

```csharp
public interface IOccupiedCellQuery
{
    OccupiedCellScan GetOccupiedCellsIn(int minX, int minY, int maxXExclusive, int maxYExclusive, int mapLayer);
}

public readonly ref struct OccupiedCell
{
    public Vector3Int Position { get; }
    public ReadOnlySpan<int> Occupants { get; }
}

public ref struct OccupiedCellScan
{
    public OccupiedCellScan GetEnumerator();
    public bool MoveNext();
    public OccupiedCell Current { get; }
}
```

- `Map` implements the scan and `World` implements `IOccupiedCellQuery` over it. `GameModuleContext.OccupiedCells` is the
  build's `World`, complete from construction like `MapQuery`.
- It is a separate interface so `IMapQuery` and its ten test doubles do not gain a member they would have to fake.
- A caller reads it with `foreach`, allocating nothing.

The rules it must keep:

1. **Order is fixed: Y ascending, then X ascending.** Every caller gets this one order, so a tie is decided the same way
   on every run of a build. No caller needs to keep the order it had before.
2. **It reads the same cells the per-tile loop would.** Negative coordinates, a row crossing a neighborhood border, an
   unloaded neighborhood (skipped), a neighborhood cut short by a bounded map, and a layer off the map (empty).
3. **It reads only.** It writes no cache, so a worker can use it later. The current `Occupants` span follows the rule of
   `GetOccupantEntityIdSpanAt`, and nothing may change occupants while a scan is open.

## What moves to it, and what doesn't

| Scan | Decision |
|---|---|
| `NpcDecisionContext.ScanForOtherOccupant` | Moves (Phase 2) |
| `NpcDecisionContext.CountHostilesInSight` | Moves (Phase 2) |
| `NpcDecisionContext.CountAllies` | Moves (Phase 2) |
| `NpcLooting.TryFind` | Moves (Phase 2) |
| `AuraSystem.StartExposuresNear` | Moves (Phase 3): one row of the diamond at a time |
| `AuraSystem.ReEvaluateExposuresNear` | Moves (Phase 3): one row of the diamond at a time |
| `NeighborhoodStreamer.RestoreStraddlingFootprints` | Moves (Phase 4): 8,208 cells a layer, 24,624 per load |
| `NpcDecisionContext.ScanForHostileAdjacent` | Moves (Phase 2): one rectangle, leaving out occupied cells inside its footprint |
| `ProcessingTierSystem.RetierRectangle` | Moves (Phase 5): changes from X then Y to Y then X, on purpose |
| `TargetShapeResolver.AppendAdjacent` | Stays: previews and effects need the actual tile list |

Also out of scope: folding `BlockingEntityIds` into the mask, 32x32 occupant chunks, and SIMD row scanning.

## Measuring

Every phase uses the `phase-performance-testing` skill, headless A/B, **Release**, seed 1, frames 600-3600, map 3072.

- Save the baseline before any code changes:
  `Invoke-FrameBenchmark.ps1 -SaveBaseline -BaselineName pre-occupied-scan -Configuration Release`
- Compare after each phase:
  `Invoke-FrameBenchmark.ps1 -Compare -BaselineName pre-occupied-scan -Configuration Release`

A phase is kept when all of these hold:

- **Runs of the same build share one fingerprint.** A mismatch within a build is a bug.
- **A fingerprint that differs from the baseline is allowed**, but the A/B is then comparing two different worlds. For
  that phase, time the changed code with a temporary Stopwatch probe rather than trusting the A/B's rows.
- The player stands still in these runs, so Phase 5's order change is invisible to them and it has its own walk test.
- `NpcDecisionSystem` (Phase 2) is faster, with its median outside the baseline's min-max range. It need not reach the
  script's 10% `IMPROVED` flag, since small engine gains are worth keeping.
- No other system is flagged `REGRESSION`.

If Phase 2 shows no gain outside the baseline's spread, stop, remove the scan, and record the result in the TODO entry so
it is not tried again.

## Phase 1 -- the scan

1. Save the baseline above.
2. Add `IOccupiedCellQuery`, `OccupiedCell` and `OccupiedCellScan`, the scan in `Map`, `World`'s implementation and
   `GameModuleContext.OccupiedCells`. No caller changes yet.
3. Tests in `Tests/GameTests/World`:
   - An equivalence test: on a seeded map with single and shared cells and multi-tile entities, the scan yields exactly
     the cells, occupants and order of the per-tile loop, over rectangles placed across every case in rule 2.
   - A scan allocates nothing.
   - After add, shared-cell, sole-move and remove sequences, the scan still agrees with `GetOccupantEntityIdSpanAt`.

Check: `dotnet test Tests/Tests.csproj` passes, and the A/B shows the same fingerprint with no regression. There is
nothing to see in game for this phase.

## Phase 2 -- NPC scans

1. Move the five NPC scans in the table, each keeping its own filter and early exit.
2. `ScanForHostileAdjacent` scans the rectangle one tile beyond its footprint and skips any occupied cell inside the
   footprint, because a Tiny or Phasing hostile sharing it is not adjacent today. Its answer is yes or no, so order does
   not matter. `NpcDecisionServices.AdjacentTiles` is then unused and goes. `HasHostileAdjacent` stops
   asking `HasOtherOccupantNear` first, since that gate would now read the same rectangle twice.
3. `NpcDecisionServices` and `NpcLooting` take `context.OccupiedCells`.
4. Add a test that a hostile on the ring counts as adjacent at 1x1 and 3x3, and one sharing the footprint does not.
5. Run `dotnet test Tests/Tests.csproj --filter "TestCategory=Determinism"`, then the full suite, then the A/B.

In-game checks (`--seed=1`):

- Stand beside a goblin. Expect it to engage you in melee.
- Bring a hostile within 3 tiles with a wall between you. Expect it not to flee from what it cannot see.
- Remove the wall or step into its sight. Expect a creature that should flee to run from you.
- Kill a creature beside another that loots. Expect it to loot the corpse, and to leave one reserved for you alone.
- Fight a Huge (3x3) creature. Expect it to notice you on every side of its footprint.
- Fight across a neighborhood border. Expect the same behavior on both sides.
- Stand a Tiny or Phasing hostile on a creature's own tile. Expect it not to be treated as adjacent until it steps off.

## Phase 3 -- aura exposure scans

1. `StartExposuresNear` and `ReEvaluateExposuresNear` read each row of the diamond as a one-row rectangle, Y ascending
   then X ascending, as they do now. `IsWithinReachOf` stays and is asked only of occupied cells.
2. Both bodies change exposures and never occupants, so they keep the scan's read-only rule.
3. Add a test that a moving source, with and without an overlap to skip, starts and ends the same exposures as the
   per-tile walk, including Tiny and Phasing occupants and a reach crossing a neighborhood border.

The seeded benchmark is mostly lava exposure ticks, so `AuraSystem`'s row may barely move. Besides the A/B's fingerprint
and regression checks, time the two walks with a temporary Stopwatch probe at reach 1, 3 and 8. Keep the change only if
the walks are no slower at any of them, and remove the probe afterwards.

In-game checks:

- Toggle a carried aura on beside a creature. Expect its exposure to start within a second.
- Walk the aura away until the creature is out of reach. Expect the effect to stop.
- Stand a Tiny or Phasing creature in a placed aura. Expect it to be affected.
- Carry an aura across a neighborhood border with a creature on the far side. Expect it to be reached.

**Result (2026-11-24): not kept; the per-tile walks stay.** Timed in Release with tiered JIT off, with the old and new
walks alternating in one process (11 runs each, medians, µs per one-step move of a source over a map 12% occupied):

| Walk | Reach 1 | Reach 3 | Reach 8 |
|---|---|---|---|
| Per tile, glow-only aura | 0.10 | 0.24-0.28 | 0.82-0.86 |
| A row scan per diamond row | 0.21-0.22 | 0.50 | 1.67-1.71 |
| One scan over the bounding square | 0.13-0.14 | 0.35-0.37 | 1.72-1.73 |
| Per tile, burning aura | 0.14-0.19 | 0.35-0.47 | 1.05-1.10 |
| A row scan per diamond row | 0.26-0.27 | 0.63-0.67 | 1.96-1.97 |
| One scan over the bounding square | 0.18-0.23 | 0.48-0.57 | 1.98-1.99 |

The per-tile walk rules out nearly every tile by arithmetic before reading the map: after a one-step move only the
thin band where the old and new diamonds differ is read. A scan reads every column mask in its rectangle and pays its
setup per row, so it does more map work, not less. The scan pays only where every tile of a rectangle is read, as in
Phase 2's NPC scans.

Timed with tiered JIT left on, the same runs swung up to 7x between identical builds, as methods were recompiled
mid-run. A short probe needs `DOTNET_TieredCompilation=0`, or a warm-up long enough to finish tiering.

## Phase 4 -- the streamer's border scan

1. Move `RestoreStraddlingFootprints` to the scan as four rectangles per layer: top, bottom, left and right. This replaces
   the loop that jumps across the neighborhood's interior.
2. Run the streamer tests, the full suite and the A/B. Read the worst frame as well as the average.

In-game checks:

- Stand a 3x3 creature across a neighborhood border, walk far enough that one side unloads, and come back. Expect it to
  occupy every tile of its footprint and to block movement on both sides.
- Walk across several neighborhood borders. Expect no new hitch.

## Phase 5 -- tier updates

This phase is separate because the headless benchmark cannot see it, so it needs its own checks.

1. `RetierRectangle` scans Y then X and collects every occupant into `_occupantIdsScratch`, then retiers the collected
   ids after the scan closes. Tier handlers can build creatures and apply effects, so none may run during a scan.
2. `RetierTile` goes. Its separate Blocking read is redundant, since the occupant list already holds that entity. A
   multi-tile entity may be collected more than once, which is harmless because `Retier` is idempotent.
3. `ProcessingTierSystem` takes `context.OccupiedCells`, and its test double follows.

What this changes, by decision:

- Which entities change tier does not change.
- When several cross a tier edge on one step, their order does: NPC order within a stripe bucket (and so who wins a
  contested tile), which of two promoted creatures gets which crawler number, and the order resume effects start.
- The same seed and inputs still give the same result in the new build, but not the old build's result once the player
  moves. Saves hold definitions rather than simulation state, and there are no replays, so nothing needs migrating.

The headless benchmark cannot cover this phase because its player stands still, so:

- Add a scripted player walk to `Tests/Determinism`: orthogonal steps, diagonal steps, a MapLayer change and a
  neighborhood crossing. After every step, each entity's tier must equal a fresh `ComputeTier` that accounts for
  hysteresis, and two runs must produce the same trace.
- Time `OnReferenceMoved` with a temporary Stopwatch probe on the same four moves, before and after. Keep the change
  only if none is slower, and remove the probe afterwards.

In-game checks:

- Walk in a straight line for 200 tiles, then diagonally. Expect creatures ahead to start moving before they come into
  view, with no hitch.
- Change MapLayer. Expect creatures on the new layer nearby to act, with no longer a pause than before.
- Cross a neighborhood border and turn back. Expect frozen creatures to wake and no creature to be stuck or duplicated.
- Walk into a crowd of unbuilt crawlers. Expect each to show its own crawler number.

## Phase 6 -- wrap-up

- IMPLEMENTATION-NOTES: the scan, its rules, and the measured results.
- CLAUDE.md, "World & Map": a rectangle of occupants is read with `GetOccupiedCellsIn`.
- TODO: update "NpcDecisionSystem, further" with what is left, removing the part this finished.

## Phase 7 -- sight and ally range fix

`CountHostilesInSight` and `CountAllies` end their rectangle at `position + size + radius`, which is exclusive, so they
reach `radius` tiles left and up but only `radius - 1` right and down. Both promise "within radius tiles of its
footprint". This is the one phase here meant to change behavior, and it is separate so the earlier phases stay pure
performance changes.

1. End both rectangles at `position + size + radius + 1`, in X and Y.
2. Add tests at 1x1 and 3x3, for hostiles in sight and for allies: an entity exactly `radius` tiles away counts on all
   four sides and at all four corners, and one at `radius + 1` does not.
3. Run `dotnet test Tests/Tests.csproj --filter "TestCategory=Determinism"`, then the full suite, then the A/B.

The wider scan changes who flees and how packs weigh their allies, so the fingerprint is expected to differ from the
baseline and the A/B then compares two different worlds. Runs of the same build must still share one fingerprint. Read
the A/B for anything flagged `REGRESSION`, but don't treat its rows as a like-for-like cost. Any existing test that
relied on the short side is updated to the promised range.

In-game checks (`--seed=1`):

- Stand exactly 3 tiles to the right of a creature that should flee, in its sight. Expect it to run.
- Repeat from 3 tiles below it, then from the left and above. Expect the same on every side.
- Stand 4 tiles away on each side. Expect it not to flee from you.
- Fight a goblin with an ally at the edge of its pack radius on the right or bottom. Expect it to fight as boldly as with
  the same ally on the left or top.

Then add the fix to IMPLEMENTATION-NOTES beside the scan's entry.

**Result (2026-11-24): no bug; nothing changed.** The premise was wrong. A footprint covers `position` to
`position + size - 1`, so an exclusive end of `position + size + radius` already reaches exactly `radius` tiles right
and down, as the start of `position - radius` does left and up. Widening the end by one made both ranges `radius + 1`
on the right and bottom, and the new tests failed there. The code is as it was, so NPC behavior and the world
fingerprint are unchanged, and the in-game checks above aren't needed. The tests stay
(`NpcPerceptionRangeTests`: hostiles in sight and allies, 1x1 and 3x3, every side and corner at the radius and one
tile past it).

## Later, only if measured

A separate bitset (64 tiles a word) only pays over a byte per column on **wide** scans, around 64 tiles or more. No
frequent scan is that wide today. Reconsider it when a real one appears, such as field of view or NPC spatial queries,
and a profile shows the row walk costing time.
