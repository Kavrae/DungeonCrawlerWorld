# Aura scaling -- strongest wins, pulses for moving sources, one tick per target

Plans the work the aura stress test of 2026-11-26 called for, and replaces TODO.md "Resolve highest performance costs"'
"AuraSystem, further" bullet (its "hybrid" idea is Decision 2). The hold on aura-specific performance work
(memory: aura perf on hold) ends with this plan: the stress test is the aura content it was waiting for.

Written 2026-11-26 from the stress test, two prototypes and the user's answers in the same session.

**Status:** complete -- Phases 0-5 built and checked, 2026-11-27. Decisions 1-10 confirmed 2026-11-26; Decision 4's tier-change gap turned out not to arise (Phase 3), and Decision 8's terrain index wasn't needed (Phase 2).

**The measure:** the stress run -- every built creature carrying a random small (reach 3), medium (8) and large (15)
aura of 14 effects -- must stay inside the 16.7 ms frame with room for everything else. Headless Release, seed 1,
frames 600-3600, map 3072:

| | Today | Prototype (pulses + one tick per target) | This plan's target |
|---|---|---|---|
| 100% of creatures, headless | 23.5 ms/frame | 9.7 | <= 10 |
| 50%, headless | 14.4 | 6.0 | <= 6.5 |
| 100%, windowed | 21.7 + draw, behind real time | 10.5 + 0.3 draw (no moving glow), real time | real time, draw <= 1 ms |
| 0% (today's game), headless | 1.24 | 1.23 | no worse than today |
| Aura totals memory at 100% | 650 MB | 111 MB | <= 111 MB |
| Moved sources waiting to be re-placed | ~70,000 (auras ~2.4 min behind) | 0 | 0 |

## What the stress test found

- **Cost follows exposures, not sources.** About 0.03 ms per 1,000 (creature, aura) exposures, each its own
  once-a-second timer. The same 224 k sources with half the reach cost a third as much.
- **Inside each tick, `PowerReaching` scans every entity source of the aura** in a 3x3 block of 32-tile chunks, to
  subtract the creature's own sources and name the credited one. With 217 k moving sources that is ~9 ms a frame.
- **Non-Local moving sources are re-placed 8 a frame.** At 100%, ~70 k waited, so off the Local tier an aura stayed
  ~2.4 minutes behind its carrier. Raising the cap with today's re-placement cost would not fit a frame.
- **The per-cell totals are dense per aura id:** 650 MB at 100%, barely smaller at half reach. The number of distinct
  auras moving across the map drives it.
- **`StartExposures` asks every aura in the field** on each move onto covered ground: ~11 us a move at 100%.
- **Allocation** rose from 26 KB to 530 KB a frame, about 44 bytes per exposure tick; with one tick per target it fell
  to 185 KB.

What each prototype bought at 100% (interleaved runs, two each, agreeing within 0.3%):

| | ms/frame | AuraSystem | Allocated per frame |
|---|---|---|---|
| Field + a timer per (creature, aura) -- today | 23.5 | 21.3 | 530 KB |
| Pulses + a timer per (creature, aura) | 21.3 | 19.2 | 533 KB |
| Field + one tick per creature | 18.1 | 16.0 | 186 KB |
| Pulses + one tick per creature | 9.7 | 8.0 | 185 KB |

The two only pay together: a per-creature tick still asks the field for each aura, which is the expensive scan while
moving sources are in the field, and pulses take them out. With both, about 3.2 ms is pulses and 4.6 ms is ticks, most
of it the effects themselves.

## What the code says today

- `AuraField` holds every source: terrain cells (its own) and entity sources (`AuraSystem` puts them in, and
  `AuraSourceIndex` keeps them by 32x32 chunk for credit). Totals are sums: sources of one Guid add.
- `AuraExposureComponent` (Multi pool) is one per (entity, aura), each a keyed timer on a `MultiTimerWheel`.
- A moving source is re-placed on the move if Local, else queued and re-placed 8 a frame (`ResyncQueuedSources`).
- A field source that moves for any reason (a teleported shrine: `EntityTeleporter` records an `EntityMovedEvent`) and
  a terrain change (`AuraField.OnTerrainChanged` -> `TerrainAuraAdded`/`Removed`) both re-place and re-expose
  correctly today. Checked 2026-11-26.
- Presentation reads only the field: the glow (`AuraView`, blended by `AuraGlowBlend`) and a tile's auras on
  inspection (`AuraView.CopyAurasAt`).

## Decisions

1. **Strongest wins.** Confirmed by the user 2026-11-26. For one entity and one aura Guid, the strongest single source's
   value there applies; sources never add. It holds for stacking debuffs too: they top up to a maximum, so anything
   after the strongest would be wasted. Consequences:
   - Two Toxic Idols held together are as strong as one. Two lit units in one stack likewise.
   - Lava: a tile near several lava cells gets the nearest one's value, not their sum, so lava fields burn less deep
     inside. Lava's description ("the nearer the lava, the hotter it burns") still holds.
   - The rule is fixed, not a per-aura option: nothing needs adding today, and a per-aura policy would bring back
     every cost of sums. An aura that should grow with its sources is a different aura per source.

2. **Two ways an aura reaches, chosen by whether the carrier can move.**
   - **Field:** terrain and every entity source whose entity has no `MovementComponent` (shrines, idols on the floor,
     item piles, anchors). Written once when placed; read in O(1).
   - **Pulse:** every source on an entity with a `MovementComponent` (creatures and the player, and what they carry: a
     lit idol, a toggle's aura). Each source scans its own reach on a schedule.

   The choice is made when the source is placed. It is re-made only if the entity gains or loses movement, which takes
   the source out and puts it back the other way. Whether the entity is moving right now never decides it: a
   creature standing still is still a pulse source, and a teleported shrine is still a field source.

   `AurasModule` then `Requires` Movement (it already `RunsAfter` it).

3. **A field source that moves re-places at once, at every tier.** Field sources move a few times a session (a
   teleport, a terrain change, a pile picked up), so the 8-a-frame queue and the Local/non-Local split are deleted.

4. **Pulse cadence.**
   - Local: every 60 frames. Neighborhood: every 240. Borough and Beyond are frozen and don't pulse (resuming
     re-stamps the schedule, as exposures skip owed ticks today).
   - The first pulse is staggered by entity id across the source's **own** period. Staggering a 240-frame source over
     60 frames bunched pulses into a quarter of each cycle: 4x the mean pulses in a frame, a 50 ms worst frame at 50%.
     Staggered over its own period, the peak is ~2x the mean and the worst frame was 21 ms.
   - A pulse writes a record that lasts the period plus one frame, so the target's once-a-second tick applies the aura
     every second however slowly its source pulses. A Neighborhood source is as strong as a Local one.
   - **When a source's tier changes, it pulses at once** and keeps the new period. Otherwise a source dropping from
     Local to Neighborhood leaves its 61-frame records to run out before its first 240-frame pulse, and targets lose
     the aura for up to 3 s. Tier changes are rare, so this costs nothing measurable.

5. **One tick per target.** An entity exposed to any aura holds one `AuraExposureTickComponent` (Packed, an
   `IScheduledTimer` on a `PackedTimerWheel`, once a second, staggered by entity id). Its per-aura
   `AuraExposureComponent`s become records with no timer:
   - the pulse record: strongest power, its credit, its expiry;
   - `Refused`, so a refusal is announced once per stay.

   A tick copies the entity's records, applies each aura still reaching at the stronger of the field's value and the
   live pulse record, drops the records that no longer reach, then re-arms. With no records left it removes itself.
   An aura newly entered applies at the entity's next tick (up to a second), as a new exposure's staggered first tick
   does today.

6. **Accepted, not fixed: the turn-off delay and the handover gap.** The user accepts both where a fix costs
   performance (2026-11-26). Both apply to pulse sources only; field sources stay instant.
   - **Turn-off delay:** a pulse source's records outlive it by up to one period (1 s Local, 4 s Neighborhood) after a
     toggle goes off, the carrier dies or walks away. A fix would scan the reach on every removal (the cost pulses
     remove) or keep a back-reference per record.
   - **Handover gap:** while the strongest source's record is live, a weaker one's pulse is ignored. If the strongest
     leaves, the aura can lapse for up to one period until the weaker pulses again. A fix keeps a second-strongest slot
     per record: 8 more bytes and a compare on every pulse write.

7. **Credit.** A pulse record holds its source's credit as of that pulse (`AuraSystem.AttributionOf`, worked out once
   per pulse, not per target). In the field the credited source is whichever gives the maximum; terrain wins only when
   it gives more than every entity source (today an entity source wins a tie with terrain, and still does). With only
   stationary entity sources left in `AuraSourceIndex`, its credit scan stays small.

8. **The field holds maximums, and a removal recomputes.** A per-cell maximum can't be subtracted, so removing,
   moving or changing a field source recomputes each cell of its old diamond from the sources still reaching it:
   - stationary entity sources from `AuraSourceIndex`;
   - terrain from a new per-chunk index of each aura's radiating terrain cells.

   These are rare events (Decision 3). A neighborhood unloading drops its chunks whole and recomputes only the band its
   sources reached into loaded neighbors. Totals become `ushort`, the type of a source's power, which halves their
   memory. `AnyAuraReaches` and `Version` keep their meanings.

9. **Pulse auras are drawn and inspected from their sources.**
   - **Drawing:** `AuraView` adds the pulse sources whose reach touches the view, found each frame by scanning
     carriers in the view rectangle grown by the largest pulse reach. Each cell takes the field's value or a pulse's,
     whichever is strongest. The glow image is rebuilt only when that set of (source, position) or the field's
     `Version` changes.
   - **Inspection:** a tile's auras (`CopyAurasAt`) use the same query, so what is inspected is what applies.
   - TODO "Aura glow clarity" (look and lights) is separate and unaffected.

10. **The stress test stays as a benchmark, out of master.** Its 42 definitions are built in code when the setting is on
    (the catalog takes a code-built definition; no content Guid can clash with them), and the records leave
    `Auras.jsonl`. The settings stay: `AurasModule.StressAuraCreaturePercent` and `StressAuraSizePercent`. The
    prototype's `StressAuraPulseMovingSources` and `StressAuraTickPerTarget` settings, its gauges and its probes go.
    The benchmark script gets `-ExtraArguments`, so an A/B can run under stress.

## The design

```
moves ──► field only (terrain + stationary sources) ──► StartExposures: records for auras the cell's field holds
pulses (moving sources, staggered, per tier) ──► scan reach ──► record on target: max(power), credit, expiry = period+1
per-target tick (1 s) ──► for each record: power = max(field(cell), live pulse record) ──► apply / drop ──► re-arm
```

## Phases

Each phase ends with the stress benchmark (0%, 50%, 100%, headless Release, A/B against the phase before) and the
in-game checks, and stops for the user's go-ahead.

### Phase 0 -- The stress harness, permanent; baselines

Built 2026-11-26, awaiting the check. As built:
- `AuraStressTest` (Game, Auras; public so the tests reach it) builds its 42 definitions in code (`GuidOf`, `AuraCount`)
  and `AurasModule` declares its two settings and wires it. `Auras.jsonl` is back to its 12 records.
- Gauges `Auras/Sources` and `Auras/Exposures` (each pool's own count), registered beside the totals gauges.
- `Invoke-FrameBenchmark.ps1 -ExtraArguments "..."` (split on spaces, saved as `extraArguments`, a previous run must
  match it); SKILL.md says how to run the stress benchmark.
- The prototype is reverted. Its files are kept in `Log/aura-scaling-prototype` (gitignored) as the
  reference for Phases 1 and 3.
- Baseline build saved: `Log/phase-benchmarks/baseline-build-release-pre-aura-scaling`. Headless Release, two runs each:
  0% 1.16 ms/frame (fingerprint `D19176A3FAED7E0F`, the world before this plan), 50% 13.2, 100% 23.1 (AuraSystem 20.9,
  worst frame 37-42 ms).

1. Build the 42 stress definitions in code. Remove them from `Auras.jsonl`. Keep the two stress settings and their
   build step.
2. Keep gauges for sources, exposures and dead creatures (as `Auras/Sources`, `Auras/Exposures`), not the prototype's
   static counters.
3. Give `Invoke-FrameBenchmark.ps1` an `-ExtraArguments` parameter, recorded with the run so results under stress are
   never compared with results without it.
4. Revert the prototype's `AuraSystem`, exposure-component and test changes. They stay in this session's history as the
   reference.
5. Save baselines: `-BaselineName pre-aura-scaling` at 0%, 50% and 100%.

**Tests:** `ContentCodecTests` and master-content tests no longer see the stress records; a build with
`StressAuraCreaturePercent=100` registers all 42 and gives a built creature three sources.

**Check:** run the script with `-ExtraArguments "--setting=AurasModule.StressAuraCreaturePercent=100"`. Expect about
23.5 ms/frame, as recorded above.

### Phase 1 -- One tick per target (Decision 5)

Built 2026-11-26, awaiting the check. As built:
- `AuraExposureComponent(AuraId)` holds `AuraId` and `Refused` only, found by `AuraExposureMatcher`. The pulse record's
  fields come in Phase 3, where they are first read, not here: nothing would read them until then.
- `AuraExposureTickComponent` (Packed, `IScheduledTimer`) on a `PackedTimerWheel` in `AuraSystem`, handed in by
  `AurasModule`. `AuraSystem.Tick` copies the entity's exposures, applies each (`ApplyExposure`), removes the ones whose
  aura no longer reaches, and re-arms, or removes itself with the last exposure. A corpse loses all of them.
- Tests: four new in `AuraSystemTests` (three auras on one tick, leaving one then all, one aura refused while another
  lands, a corpse); the existing tests that planted a timed exposure plant a record and a tick (`PlantExposure`).
  4,895 game and 404 Workshop tests pass. The determinism tests compare runs with each other, so nothing was
  re-recorded. Benchmark fingerprints moved: seed 1 without stress `D19176A3FAED7E0F` -> `4280ED4F46274369`, at 100%
  stress `11BD3FA0BBA9BFC2` -> `EFBAEE4D53073852` (auras apply in a different order within a frame).
- A/B against `pre-aura-scaling`, headless Release: 100% stress 23.11 -> 17.85 ms/frame (-22.8%; AuraSystem 20.91 ->
  15.73), allocation 186 KB a frame (530 before). Without stress 1.163 -> 1.189 (+2.2%), AuraSystem 0.298 -> 0.310
  (+4.2%, under the 10% threshold, A's own spread 3.2%). Probably the tick component
  costing a little where nearly every exposed entity stands in one aura (today's lava); not measured further.

1. `AuraExposureTickComponent` and its wheel. `AuraExposureComponent` loses its timer (`IKeyedScheduledTimer`,
   `NextTickFrame`) and gains the pulse record's fields, unused until Phase 3.
2. The per-target tick, the resume skip, and the exposure starts that ensure the tick exists.
3. Determinism: fingerprints move (auras apply in a different order within a frame). Re-record what `Tests/Determinism`
   pins, and name every moved fingerprint in IMPLEMENTATION-NOTES.

**Tests:**
- An entity in three auras holds one tick component and three records. Each aura applies once a second.
- Leaving one aura drops only its record. Leaving all of them removes the tick component at the next tick.
- An immunity refuses one aura: it is announced once while the entity stays, and the others still apply.
- A frozen entity's tick skips the seconds it was frozen. A corpse loses its records and its tick.
- The existing aura, toggle and pile suites pass unchanged (the prototype passed all 4,886 tests).

**Check:**
- Stand in lava next to a Healing Shrine: expect a burn and a heal each second, as before.
- Stress A/B at 100%: expect ~18 ms/frame (from 23.5) and about 185 KB allocated a frame.

**Found in the check (2026-11-26), and fixed with it: auras and terrain contact read only a large entity's origin tile.**
A 3x3 player with its origin at (16,11) and lava under its far corner, (18,13), took no burn, and the Healing Shrine at
(19,11) healed it at the strength 3 tiles away instead of 1. Both systems did this before this plan. Now:
- `AuraSystem` measures every read over the footprint, at the strongest tile (`PowerReachingFootprint`, and
  `AnyAuraReachesFootprint` before asking per aura): starting an exposure, the tick and the re-check after a source
  leaves. A 1x1 entity still reads one tile.
- `TerrainContactSystem` applies each distinct contact terrain under the footprint once, on stepping on and at its own
  repeat: `TerrainContactExposureComponent` is a Multi pool keyed by terrain (it was Packed, one per entity). A repeat
  that finds its terrain gone takes up the repeating terrains now under the footprint, so a repeat still reads the
  terrain where the entity stands.
- Tests: two in `AuraSystemTests` (a 3x3 entity exposed by its far corner at that corner's power, and losing it when no
  tile is in reach), three in `TerrainContactSystemTests` (a corner on a hazard burns and repeats, two tiles of one
 Workshop
  tests pass. A's own run-to-run spread was 13.8% this sitting (baseline 24.2 ms/frame at 100%, 23.1 earlier).
- A/B with the fix, 100% stress: 24.18 -> 19.80 ms/frame (-18.1%; Phase 1 alone was -22.8%). The footprint costs ~1-2 ms
  there while moving sources are in the field: a large entity's exposure reads 9 totals and asks 1-2 tiles in full
  (`PowerReachingFootprint` asks the tile with the largest total first and skips any whose total can't win), more
  exposures land (733 k against 720 k), and `DeathSystem`/`AuraSourceRemovedEvent` rose 0.27 -> 0.33 (a re-check
  visits a large occupant from each of its tiles). `TerrainContactSystem` 0.051 -> 0.070 (keyed Multi pool). Phase 3
  takes the full asks away for moving sources. Without stress: 1.220 -> 1.234 (spread 16%, no flags).
- Checked in-game 2026-11-26 by the user: the 3x3 player burns from the lava under its corner.

### Phase 2 -- Strongest wins in the field (Decisions 1, 7, 8)

1. `AuraTotals` holds per-cell maximums (`ushort`). Adding a source raises cells. Removing, moving or a terrain change
   recomputes the old diamond from the remaining sources (`AuraSourceIndex`, plus the new per-chunk terrain-aura index).
2. Neighborhood unload: drop its chunks, recompute the band its sources reached into loaded neighbors.
3. Credit by maximum (Decision 7). "A source never affects the entity carrying it" stays: for an entity that is itself
   a stationary source of the aura (a shrine standing in its own aura), `PowerReaching` takes the maximum over the other
   sources, from `AuraSourceIndex` and the terrain index. Such entities are few, so this read stays rare.
4. Docs: CLAUDE.md's Auras section ("Overlapping sources of one aura add", "their powers add"), the
   `AuraField`/`AuraSourceComponent` remarks, and IMPLEMENTATION-NOTES.

**Tests:**
- Two overlapping sources of one aura: each cell holds the larger value. Removing the stronger restores the weaker's
  values exactly; removing the weaker changes nothing.
- A terrain cell changed to and from lava next to another lava cell gives the same values as a fresh build.
- Unloading a neighborhood leaves its neighbors' cells as a build of the remaining terrain would.
- `TwoLitUnitsInOneStack...` and `MergingTwoLitStacks...` assert the strongest, not the sum.
- Credit: an entity source equal to the terrain's value is credited; a stronger terrain value is credited to the
  terrain.

**Check:**
- Drop two lit Toxic Idols on one tile: expect the glow and the poison no stronger than one idol.
- Walk into a lava field: expect burn top-ups matching the nearest lava, not deeper ones inside.
- Admin "Regenerate" a neighborhood beside a lava field: expect the glow across the border to match before and after.
- Stress A/B: expect 0% no worse, and the totals memory down by about half at 0%.

Built 2026-11-26, awaiting the check. As built:
- `AuraTotals` holds `ushort` maximums (`Raise`, `Clear`, `ClearArea`); `AuraGrid` raises a source's reach, collects the
  cells a source holds (`CollectHeldCells`) and raises given cells from a source (`RaiseCells`). `AuraField.RemoveSource`
  clears the held cells and raises them again from the sources still reaching: `AuraSourceIndex.CollectIn` and the
  terrain read from the map around them. **No per-chunk terrain index was needed** (the plan's Decision 8 asked for
  one): reading the map around a removal is cheap enough for terrain changes and stationary sources.
- A None source's move still writes only the cells that changed sides (the overlap keeps its value).
- Unloading: nothing happens per `TerrainUnloadingEvent` row; on `NeighborhoodUnloadedEvent`, once the map no longer
  holds the neighborhood, it and a band one largest reach wide are cleared and raised again from every source still
  there (`NeighborhoodBits.ClearNeighborhood` for its coverage). A source outside still writes its reach into it.
- Credit by maximum: an entity source wins a tie with terrain, and terrain's value is read from the map only where
  the entity's own source is the strongest at the cell (`AuraField.PowerReaching`).
- Tests: `AuraTotalsTests` rewritten for raise and clear (plus `ClearArea`), two new `AuraFieldTests` (removing the
  stronger or weaker of two overlapping sources; lava changed to and from beside more lava, against a fresh build).
  Changed to strongest wins: two lit units or two merged stacks (one idol's strength), two shrines (heal 13, not 26),
  two overlapping Burning sources (8, not 10), a moved dual source's Poison (8, not 9). The unload tests publish
  `NeighborhoodUnloadedEvent` after the map unloads, as the streamer does. 4,903 game and 404 Workshop tests pass.
- A/B against `pre-aura-scaling`, headless Release. Without stress: 1.22 -> 1.29 ms/frame; AuraSystem 0.32 -> 0.37
  (+14%, flagged). Probed: power reads cost the same per tick as before; the rise is ticks (+4.5%, exposures 23.5 k ->
  24.5 k) and applying effects (+13%) -- gameplay: lava at its strongest cell's value, not the sum, burns creatures
  less deep in a field, so more stay alive and exposed. Worst frame 11-22 ms -> 7 ms. Aura totals 110 -> 56 MB. At
  100% stress: 22.96 -> 19.73 (-14%), the same as Phase 1 with the footprint fix.

### Phase 3 -- Pulses for moving sources (Decisions 2, 3, 4, 6)

Built 2026-11-26, awaiting the check. As built:
- `AuraPulseSources` (in `GameModuleContext.AuraPulses`): which sources pulse (`IsPulseCarrier`: a `MovementComponent`, not
  an anchor), the largest pulse reach, the counters for the gauges `Auras/Pulses` and `Auras/PulseRecords`, and the tile
  queries -- `PowerAt` and `CollectAurasAt`, from the carriers around a tile. **Moved here from Phase 4:** `AuraView.PowerAt`
  and `CopyAurasAt` read field and pulses now, so inspection and the carrier tests read what is really there. Only the
  glow is left for Phase 4. Pools are fetched on first use, so a build without the Auras module never asks for them.
- `AuraSystem`: classification on a source being added and on every move (an entity that gains movement lifts its
  sources out of the field; one that loses it goes back in at its next due pulse). The pulse schedule is a deadline per
  carrier on a 512-frame ring, entries whose deadline moved on passed over, and never scheduled on or before a frame
  whose pulses already ran. The deferred re-placement queue is gone. `AurasModule` requires Movement.
- **Decision 4, the tier-change gap, does not happen here.** A pulse takes its period when it runs and writes a record
  lasting that period plus one, and the next pulse is scheduled with the same period, so a record always lasts until
  the next pulse, whichever way the tier changed. (The prototype set expiry by the new tier but kept the old schedule.)
  The pulse at once on a tier change is kept for the other direction: a carrier rising to Local is fast at once, not
  after the rest of a 4 s period.
- **A bug found by the stress gauges and fixed:** a first pulse is staggered up to 1.5 periods out (360 frames), past the
  256-frame ring the prototype also used, so ~40% of carriers fell off the schedule for good. Exposures at 100% stress
  were 504 k against a field-only run's 766 k; with the 512-frame ring, 756 k.
- Tests: `AuraPulseTests` (7, full builds): a carried source is not in the field, reaches a target within a second and
  never its carrier; a Neighborhood carrier heals every second; turned off, it stops within a period; Local to
  Neighborhood misses no second; Neighborhood to Local reaches a new target within two seconds (fails without the pulse at
  once); 50 carriers added at once each pulse every period (fails on a 256-frame ring); a teleported shrine moves its
  field reach on the frame. The queue tests are gone; one is now that a non-Local field source moving exposes on the
  move. Tile reads in the carrier tests go through `AuraView` or field-and-pulse. 4,906 game and 404 Workshop tests pass.
  Not written: the "1,000 sources pulse no more than 2x the mean" test -- too few fit a test map for a stable ratio; the
  stress gauge measures it instead (peak 334 against a mean of 304, 1.1x).
- A/B against `pre-aura-scaling`, headless Release (the baseline's spread was 15% this sitting): 100% stress 23.97 ->
  9.78 ms/frame (-59%; AuraSystem 21.70 -> 8.03), worst frame 44-137 -> 22-39 ms; allocation 187 KB a frame; aura totals
  56 MB. Without stress: 1.114 -> 1.186 (+6.5%), AuraSystem 0.279 -> 0.328 (+18%; Phase 2 was +14% from gameplay),
  worst frame 10-12 -> 6 ms.

1. The choice when a source is placed (Decision 2), re-made when the entity gains or loses `MovementComponent`.
2. The pulse ring:
   - staggered over the source's own period;
   - Local 60 frames, Neighborhood 240;
   - an immediate pulse on `ProcessingTierEvents.TierChanged`;
   - no pulses while frozen;
   - a pulse scans the reach with `IOccupiedCellQuery`, counting each target once, never the carrier, at the distance to
     the nearest tile of the target's footprint (the footprint rule found in Phase 1's check, below Phase 1).
3. Records: strongest-wins writes with expiry = period + 1. The tick takes the stronger of field and live record.
4. Delete the deferred re-placement queue (Decision 3). A field source's move re-places at once.
5. Gauges: pulses per frame, records written per frame.

**Tests:**
- A carried aura reaches a creature in range within a second, and never its carrier.
- A Neighborhood-tier carrier pulsing every 240 frames still applies its effect to a target every second.
- After a toggle goes off, the target's record lasts no more than one period, then the aura stops.
- A carrier dropping from Local to Neighborhood pulses on that frame, and its targets miss no second.
- 1,000 sources added in one frame pulse no more than 2x the mean in any frame across a full 240-frame cycle.
- A teleported shrine's field reach moves within the frame, and the creatures it left lose the aura at their next
  tick. A terrain change still re-exposes at once.
- A lit idol dropped into a pile becomes a field source; picked up, a pulse source. Its power is the same both ways.

**Check:**
- Carry a lit Toxic Idol past goblins: expect them poisoned within a second, and the poison stopping within a second of
  putting it out.
- Read Torch at a goblin: expect the light to follow it. The glow comes in Phase 4, so expect the light by its effect
  only.
- Teleport a Healing Shrine (Admin): expect healing at the new place and none at the old.
- Stress A/B at 100%: expect <= 10 ms/frame, totals <= 111 MB, and the pulse gauge peaking at no more than 2x its mean.

### Phase 4 -- Glow and inspection for pulse auras (Decision 9)

Built 2026-11-27, awaiting the check. As built:
- The tile reads (`PowerAt`, `CopyAurasAt`) came in Phase 3. `AuraView.AnyAuraReaches` now counts pulse sources too.
- Glow: `AuraView.CopyPulseSourcesReaching` lists the pulse sources reaching an area (`PulseSourceReach`: aura, centre,
  power, size, falloff) in one occupied-cell scan. `MapRenderer.Draw` collects them for the view and margin each frame and
  folds them into the glow texture's version (`GlowVersionOf`), so the image is rebuilt only when a visible carried aura
  moved or changed. On a rebuild `AuraGlowBlend.BeginPass` lays them into a grid per aura over the area, the strongest
  per cell, and a cell takes the field's value or the grid's, whichever is stronger; the field is read with
  `AnyFieldAuraReaches`/`FieldPowerAt`. **This also removed a Phase 3 cost:** the glow pass read `AuraView.PowerAt` per tile
  per aura, which since Phase 3 scanned the carriers around each tile.
- The inspector caches a tile's auras by `AuraView.VersionAt` (the field's version and the pulse sources reaching that
  tile), so a carrier walking past refreshes it.
- The Workshop's area preview builds no pulse sources and reads the field only, as planned.
- Tests: two in `MapGlowTexelsTests` (a pulse source glows exactly as a field source of the same power and reach; field
  and pulse glow at the stronger), two in `AuraPulseTests` (a tile in a carried aura's reach reports its power and credits
  the carrier; the sources reaching an area are those near it, and a tile's version moves when a carried aura comes into
  reach). 4,910 game and 404 Workshop tests pass.
- Windowed Release, seed 1, frames 600-3600: 100% stress keeps real time (50.0 s for 50 s) at 11.7 ms update and 0.70 ms
  draw a frame (Phase 3, without moving glow: 10.5 + 0.3; before the plan 21.7, behind real time). Without stress 1.89 +
  0.29, as before.

1. `AuraView`'s pulse sources in view, the strongest-per-cell blend with the field, and the glow image key (Decision 9).
2. `CopyAurasAt` and every other tile read through the same query.
3. The Workshop's area preview reads the field only: no pulse sources exist there.

**Tests:**
- `AuraView` at a tile in a carried Lantern's reach reports its power. A tile both in the field and in a pulse reach
  reports the stronger.
- The glow key changes when a visible pulse source moves and doesn't when an off-screen one does.

**Check:**
- Carry a Lantern: expect the glow to follow you every step.
- Inspect a tile next to a goblin carrying Toxic Aura (stress 100%): expect it listed with its holder credited.
- Windowed stress at 100%: expect real time and draw <= 1 ms a frame.

### Phase 5 -- What's left, and the wrap-up

Built 2026-11-27. As built:
- **Allocation:** the steady-state frame allocation was the timer wheels: each of 4,096 slots grew a list the first time
  its frame came round, so every benchmark range (under 68 s) allocated a fresh list a frame for every wheel; what read as
  "allocated in effects" was the burn and poison wheels being scheduled from inside effects. `TimerWheel` now hands a
  drained slot's list to the next slot that needs one (`_spareSlotLists`): 27 -> 3 KB a frame without stress, 187 -> 8.5
  KB at 100%, no gen-0 collection in the range without stress, worst frame 6-9 ms where it was 10-16 (A/B with and
  without, same build otherwise; averages unchanged).
- **Per-chunk aura list for `StartExposures`:** not built. Starting exposures costs 0.07 ms a frame without stress and
  0.2 ms at 100%, now that only stationary auras are in the field.
- **Pulse scans on workers:** not built. ~3.9 ms a frame at 100% stress (the largest aura cost left), ~0 without; it waits
  for `PLAN-multithreading.md`'s fork-join, and TODO.md's "AuraSystem, further" says so.
- **Final table** (headless Release, A/B against `pre-aura-scaling`; the machine read ~35% slower than on 2026-11-26, so
  the relative figures hold): 100% stress -55.5% (27.9 -> 12.4; 23.5 -> 9.8 on 2026-11-26), 50% -52.7%, without stress
  +7.2% (AuraSystem +17%, gameplay -- Phase 2). Worst frames: 53-67 -> 22 ms at 100% (one run had a 184 ms frame), 36-40
  -> 18-23 at 50%, 11-13 -> 6-8 without. Windowed at 100%: real time, 11.7 ms update, 0.70 ms draw (Phase 4).
- Docs: CLAUDE.md's Auras section (as each phase landed), IMPLEMENTATION-NOTES "Aura scaling", TODO.md's "Aura
  scaling" entry deleted and "AuraSystem, further" reduced to what is left.

1. **Measure, then decide** each of these (A/B, Release):
   - the 185 KB a frame still allocated at 100% (an allocation trace of one stress run);
   - a per-chunk list of the field's auras for `StartExposures` (TODO's preferred fix), now that only stationary auras
     are in the field;
   - moving the pulse scans to worker threads (`PLAN-multithreading.md`: pure reads, records written in a fixed order
     on the main thread) if pulses stay the largest part.
2. Docs: CLAUDE.md Auras section (field vs pulse, strongest wins, the accepted delay and gap), IMPLEMENTATION-NOTES
   "Aura scaling", TODO.md ("AuraSystem, further" bullet replaced by a pointer here, then deleted).
3. Memory: the plan as landed.

**Check:** the full stress table, run again and recorded in IMPLEMENTATION-NOTES.

## Not built by this plan

From the review of auras against the item and action changes. Each is filed in TODO.md when this plan is confirmed:
- **Spell scaling of aura size.** Intelligence scales a spell's area, but an aura a spell grants doesn't grow.
- **Spell tags on aura effects.** An aura placed by a spell applies with only the aura's own tags, so Anti-Magic doesn't
  touch it and a Magic-conditioned modifier doesn't apply to it. This needs a decision.
- **Placement in an aura's effects.** An aura tick applies every entry to each entity. Entries placed once
  (`AuraSourceGrant`) or per tile (`TerrainChange`) mean nothing there, and nothing refuses them in aura content.
- **Inline aura definitions.** An action or item would define its aura in place, with a Guid derived from its content.
  A standalone editor would remain for shared auras (terrain, shrines, blueprints). This belongs to
  TODO "Workshop: edit Auras".

## Order against other plans

- **Before "Workshop: edit Auras".** Decision 1 changes what an aura's power means when sources overlap.
- **Independent of "Buffs and debuffs from the same source don't stack"**, which is about modifiers. Aura stat grants
  already refresh (`RefreshFromAnySource`).
- **Independent of "Aura glow clarity"**, which changes the look. Phase 4 only feeds it pulse sources.

## Bookkeeping

- TODO.md's "Aura scaling" entry is deleted (2026-11-27); the "Not built" items stay filed (2026-11-26).
- The prototype code (2026-11-26) is kept in `Log/aura-scaling-prototype`, the reference for Phases 1 and 3.
