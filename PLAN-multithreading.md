# Multithreading: change detection, structural changes and parallel work as one design

Replaces the TODO.md entries "Research multithreading" (Engine, High), "Change detection" (Medium), "Deferred
structural changes" (Medium) and "Parallel system execution" (Low), removed from TODO.md on 2026-11-12 when its
decisions were settled; what is built and what is deliberately not built lives here and, as phases land, in
IMPLEMENTATION-NOTES.

**Status:** decisions 1-9 settled 2026-11-12. Nothing is built or measured yet. Phase 0, steps 2 and 3, can run at any
time; everything else waits on `PLAN-npc-behavior-composition.md` (see "Order against NPC Behavior Composition").

## What the research found

### 1. The simulation is 6% of the frame

Headless Release A/B, 2026-11-11 (`Log/phase-benchmarks/ab-20261111-134909.json`: seed 1, map 3072, frames 600-3600,
5 runs a side, current side):

| Row | ms/frame |
|---|---|
| `EcsContext.Update (all systems)` | 1.049 |
| `TestCombatBehaviorSystem` | 0.356 |
| `AuraSystem` | 0.281 |
| `MovementSystem` | 0.094 |
| `ActionActivationSystem` | 0.060 |
| `DelayedActionSystem` | 0.058 |
| `BodyPartBurningSystem` | 0.047 |
| `TerrainContactSystem` | 0.033 |
| every other system | under 0.025 each |

The budget at 60 frames a second is 16.67 ms. Worst single frames in the same runs were 7-8.5 ms: the spikes are
several times the steady cost, and nothing yet says what they are made of (streaming, population, promotions, GC).

What follows from the table:
- **Running systems beside each other pays nothing.** Only two systems are over the ~50 µs a job has to be worth, and
  the best case for running those two at once is saving the smaller one: 0.28 ms, 1.7% of a frame. Every other pair is
  below the cost of scheduling it.
- **Splitting one system's entity loop is the only shape that can pay**, and only for the two at the top. An ideal 4x
  on `TestCombatBehaviorSystem` saves 0.27 ms.
- **Neither is worth building today.** What is worth doing today is making sure the code that will one day need it is
  shaped for it (the NPC behavior plan already does), and deciding the design so nothing built meanwhile blocks it.

### 2. One model covers every candidate: read in parallel, write in order

Every place threading has been considered in this codebase came out the same shape, each on its own:
- NPC decisions (`PLAN-npc-behavior-composition.md`, "Shaped for workers, not threaded"): a parallel decide that only
  reads, a serial `Act` in entity order.
- Aura scans (TODO.md, AuraSystem "further"): pure reads of the map and the source, added into `AuraField` on the main
  thread in a fixed order.
- Neighborhood planning (landed): a worker builds a detached `NeighborhoodPlan`, the main thread applies it.
- Write scopes (IMPLEMENTATION-NOTES "Component writes"): a worker's write is a recorded `unmanaged` updater the main
  thread applies in a fixed order.

So the design is that shape, made an engine facility: **a system may fork a read-only section across workers; each
worker fills a record buffer of its own; the main thread applies the records in entity order.** Inside the forked
section nothing is written, added, removed, spawned, destroyed or published. Outside it, everything is exactly as it
is now.

### 3. Under that model the three TODO items stop depending on each other

The TODO tied them together because parallel *writes* break synchronous `ComponentChanged`, and parallel *systems*
can't make structural changes. With every write on the main thread:

| Concern | Under "read in parallel, write in order" |
|---|---|
| `ComponentChanged`/`ComponentRemoving` fire inside the write | Unchanged. Every write is still on the main thread, so timer wheels, relationships and `ToggleItemHolderSync` keep immediate, ordered delivery. No per-thread change queues, no value-carrying change lists |
| Spawn, destroy, add, remove | Unchanged. They happen in the serial apply, at once. No command queue, no reserved ids, no sync points, and none of deferral's cost (about 2x per structural change, applied late) |
| `EventBus`, `FrameEventBuffer` | Unchanged. Published and recorded only from the serial apply |
| Write scopes | Unchanged: still main-thread only, which is now the rule rather than a limitation |
| Seeds and benchmarks | Records are applied in entity order whatever worker finished first; rolls are derived per entity (`SeedMix`), never drawn in the forked section |

What each item becomes:
- **Change detection** is a single-threaded feature with its own value (a question across entities, added versus
  changed, removals). It is no longer a prerequisite of anything. Phase 2.
- **Deferred structural changes** as a general command queue is **not built**. The hazards it listed stay handled where
  they are (`SpawnMoves`, `FrameEventBuffer.Record`'s throw, eviction before promotion). A worker never needs an id for
  something that doesn't exist yet: it records what it chose, and the apply half spawns.
- **Parallel system execution** as a scheduler over declared access is **not built**. What survives of it is the
  ambiguity report (Phase 1), which is useful single-threaded, and the in-system fork (Phase 3).

### 4. What is not thread-safe today, and what the model needs of it

| Thing | Today | Needed for a read-only fork |
|---|---|---|
| Pool reads (`GetReadonly`, `TryGet`, spans, dense walks) | Plain array and page reads | Safe as they are while nothing writes. Nothing to change |
| `EntityPages` | Pages claimed on first holder, freed with the last | Reads only; a claim is a write and can't happen in the fork |
| Debug `IEntityAccessGuard` (`SkeletonAccessGuard.OnAccess`) | Reads `SystemManager.IsUpdating` and `CreatureSkeletons.IsSkeleton` | Both are reads; safe while the fork holds. It throws from a worker: the fork has to carry a worker's exception back to the main thread |
| Debug open-write-scope count (`_openWriteScopes`) | Plain field | Never touched in the fork (no scopes there) |
| `Map`/`World` reads (`GetOccupantEntityIdSpanAt`, `BlocksSight`, cells) | Plain reads; `Map.Unbounded`'s 5x5 lookup re-centres on a window shift | Safe: a shift happens in the streamer, at the frame start, never during a fork |
| `Engine.Tiles` scratch (`TargetShapeResolver`, `PathTracer`, `DigitalSight`, `SightBlockerCache`) | Already `[ThreadStatic]` | Nothing to change. `SightBlockerCache`'s validity across threads is checked in Phase 3 |
| `TargetResolution`'s buffers, a system's own scratch (`_adjacentTilesBuffer`) | One instance | One per worker, owned by the system |
| `RandomStreams.Get(key)` sequences | One sequence per key, drawn in order | Never drawn in the fork. A forked section derives: `SeedMix` of `RandomStreams.SeedFor(key)`, the entity's key and the frame |
| Lazy content (`ContentCatalogLoader`, `Catalog<T>.Get` decoding on first ask; `ActionCatalog.GetOrCreateDerived`; `AuraCatalog.Register`) | A read that can write | The largest hidden hazard: a catalog read from a worker may decode and register. Either the fork only reads what is already loaded (a Debug check that throws on a miss from a worker), or the loader's first-ask path is made safe. Phase 3 decides after measuring how often a decide step can miss. The NPC plan's behaviors ask only what the entity already holds, whose blueprint declared it, so misses should be rare there; `GetOrCreateDerived` (a race's override of an action) is the first-ask write still to check |
| `SimulationClock`, `EngineTime` | Advanced before any system runs | Read-only during the frame |
| `EngineHooks.FrameCosts` (`FrameCostOpenMeasurements`) | Main-thread nesting stack | Workers emit nothing. The fork's whole wall time lands in the system's own row; a second row, recorded by the main thread, splits fork from apply |
| Gauges | Read at frame end | Unaffected |
| Static counters (`InventoryItemStackComponent.NextStackInstanceId`) | `Interlocked` already | Thread-safe but **order-dependent**: an id handed out in the fork would depend on timing. Only ever called from the apply |

## Order against NPC Behavior Composition

`PLAN-npc-behavior-composition.md` goes first. NPC decisions are the expected first forked system, and that plan
replaces `TestCombatBehaviorSystem` with `NpcDecisionSystem` and adds work to every decision (sight checks,
considerations, more behaviors), so today's 0.36 ms says little about what a fork would be judged against.

| This plan | Against the NPC plan |
|---|---|
| Phase 0, steps 2-3 (worst frames, fork-join cost) | Independent; any time |
| Phase 0, steps 1, 4, 5 (per-system figures, triggers) | After NPC Phase 5, on the new system |
| Phase 1 (ambiguity report) | Independent, but after NPC Phase 4, which changes the system list and would churn the baseline |
| Phase 2 (change ticks) | Unrelated; waits for a consumer |
| Phases 3-4 (forked sections, first forked system) | After NPC Phase 5, and only on a trigger |

What the NPC plan builds so that a fork later changes nothing (settled there 2026-11-12):
- **Two passes over each bucket, single-threaded, from its Phase 4**: every due creature chooses, then every choice
  acts in entity order. This is the order a fork produces, so the single-threaded world and the forked world are the
  same world. Without it, creature B would choose after creature A acted, and forking would change every seeded
  world.
- **`BehaviorArbiter.Choose` and `Act` as separate entry points** (its Phase 2), so a pick can be recorded without
  acting.
- **What `Evaluate` finds travels as an `unmanaged` value** (`NpcDecisionFindings`: entity keys, stack instance ids,
  tiles) inside the system's `NpcDecisionRecord`, which is this plan's `TRecord` unchanged.
- **`LootNearby.Act` re-checks its target** and idles when a neighbour took it: looting is the only `Act` that changes
  the world at once, every other one queues an intent.
- Already there: rolls derived per decision, scratch and the arbiter's candidate buffer owned by the system (one per
  worker later), behaviors reading what the entity holds rather than a catalog.

## Decisions (settled 2026-11-12)

1. **The model: read in parallel, write in order.** No parallel writes anywhere, so no thread-safe pools, change
   queues or command buffers. The alternative (Bevy/DOTS style: declared access, parallel systems,
   deferred commands, polled change ticks replacing callbacks) rewrites every observer and every system's signature to
   speed up a simulation that is 6% of the frame, and gives up immediate, ordered change delivery that timers,
   relationships and lit items are built on.
2. **No system-against-system scheduler.** Systems keep running one after another, with the trigger to revisit in
   decision 8.
3. **No general command queue for structural changes.** The "FrameEventBuffer double-buffering"
   TODO entry stays where it is, on its own merits; this plan no longer pairs with it.
4. **Change detection is built on its own, opt-in per pool** (Phase 2), **and only when a reader wants it.** Today's
   readers ("did this one entity change", by version) are served. The first reader that needs "which entities changed
   since frame N" is perception or a Presentation cache that doesn't exist yet. The design stays in this plan; Phase 2
   is built with its first consumer, not before.
5. **The ambiguity report learns what a system touches by recording it in Debug** (Phase 1), not from declarations:
   - *(a) Recorded in Debug* (chosen): every pool already calls `IEntityAccessGuard` on access in Debug. A second
     recorder notes (system, component type, read or write) while `SystemManager` runs each system; a test builds the
     built-ins, simulates a few hundred frames and reports. Nothing to declare, nothing to keep in sync, and mods are
     covered by running them. Its weakness: it sees only what ran, so a rare path is missed.
   - *(b) Declared per system* (not chosen): complete and checkable before running, and what a scheduler would need. About 35
     systems to annotate and a Debug check that each stays inside its declaration. Worth its upkeep only if decision 2
     is ever reversed.
6. **Workers: a small engine-owned group of dedicated threads**, not `Parallel.For` or the thread pool.
   `Parallel.For` allocates per call and shares the pool with content indexing, neighborhood planning and report
   writing, so a frame's fork could wait behind a plan. Dedicated threads, parked on a semaphore, woken with a struct
   job description, allocate nothing per frame. Count: processors minus two, at least one, at most six; a
   `SettingKey<int>` (the engine's first declared setting) overrides it, and 0 means "run the fork inline on the main
   thread" -- which is also how every test runs unless it asks otherwise.
7. **A fork is skipped when it is too small to pay**: under a per-system entity count (measured in
   Phase 0, expected a few hundred) the same code runs inline on the main thread. Same records, same order, same
   result -- which is what makes worker count never change a seeded world.
8. **Triggers.** Phase 3 onward is built only when one of these is measured in the benchmark's headless run on the
   target scenario, not before. The figures are starting points, not measurements; Phase 0 may move them:
   - one system steadily over **1.0 ms/frame** whose work is mostly per-entity reads (today's largest: 0.36), or
   - `EcsContext.Update` steadily over **4 ms/frame** (a quarter of the frame; today 1.05), or
   - Phase 0 shows worst frames dominated by something a fork would shorten.
   The scheduler of decision 2 is reconsidered only if, after in-system forks, three or more systems each over 0.5 ms
   remain that share no written pool.
9. **Simulation on its own thread, apart from drawing** (the classic split) is out of scope. It is a
   different change (Presentation reads `GameViews` between frames and would need a snapshot or a lock), it buys
   nothing while update plus draw fit the frame, and nothing in this plan blocks it later.

## The design

### Forked sections (`Engine.ECS.Parallel`)

- **`WorkerGroup`** (one per `EcsContext`, created by `EcsBuilder`, disposed with the context; Trial and Staging
  builds get the inline one): the dedicated threads of decision 6. One operation: run a job over `[0, chunkCount)`,
  the main thread taking chunks too, returning when all are done. A worker's exception is captured and rethrown on the
  main thread, lowest chunk first, so a failure is the same failure whatever the timing.
- **`IForkedEntityWork<TRecord>`** (`unmanaged` struct implementing it, as updaters and matchers are; `TRecord :
  unmanaged`):
  - `void Read(int entityId, ref ForkedWorkerContext worker, ref RecordWriter<TRecord> records)` -- the forked half.
    Reads anything; writes only `records` and the worker's own scratch.
  - The applying system then walks the records in order and does everything else itself, with the calls it uses today
    (write scopes, updaters, `Merge`, events, spawns). There is no generic "apply" interface: the apply half is
    ordinary system code.
- **`ForkedEntityLoop.Run(workers, entityIds, ref work, records)`**: splits the bucket's `ReadOnlySpan<int>` into
  contiguous chunks (a stripe bucket is already runs of neighbouring ids, 16 to a block, so a contiguous chunk keeps
  each worker on its own pages), gives each chunk its own record list, and returns the lists **in chunk order**.
  Walking them front to back is entity order. Record lists are pooled by the loop's owner and grow only to their
  high-water mark.
- **Chunking is fixed by count, not by who is free**: chunk boundaries depend only on the bucket's length and a
  constant chunk size, so the record order never depends on the worker count. Work-stealing over those chunks is fine;
  the order they are *read back* in is what is fixed.
- **`ForkedWorkerContext`**: the worker's index, and a slot for the system's per-worker scratch (its
  `TargetResolution` buffers, adjacency buffers), allocated once per worker at registration.
- **Rolls**: `ForkedRolls.For(streamSeed, entityKey, frame)` returns a `SeededRandom` by value, seeded with `SeedMix`.
  The NPC behavior plan's Phase 4 already switches decisions to exactly this.

### Enforcement (Debug only, nothing in Release)

- **`ComponentManager.IsForkOpen`**, set by `ForkedEntityLoop` around the fork. While it is set, every pool write
  path, `Add`/`Remove`/`Clear`/resize, `EntityManager.CreateEntity`/`DestroyEntity`, `EventBus.Publish`,
  `FrameEventBuffer.Record`, `World` placement and `RandomStreams.Get(...)`'s draw throw, naming the component or event
  and the system. These are the same `[Conditional("DEBUG")]` seams the open-write-scope check uses.
- **A fork can't nest and can't be opened off the main thread.**
- **Architecture test**: a type implementing `IForkedEntityWork<>` holds no `EventBus`, `EntityManager`,
  `EntityFactory`, `World` (only `IMapQuery`), `GameCommands` or mutable pool field -- read-only pool interfaces
  (`IReadOnlyComponentPool<T>`, `IReadOnlyMultiComponentPool<T>`) only. That catches most mistakes at build time and
  leaves the Debug check for what slips through a helper. It covers the work struct's own fields, not a context the
  work builds: `NpcDecisionContext` is one type for `Evaluate` and `Act` and holds `IntentWriter`, so for NPC
  decisions the guard is the context's own Debug refusal to write while choosing, plus `IsForkOpen`.
- **Determinism test**: the headless benchmark's world fingerprint (`HeadlessBenchmark.Fingerprint`) is equal with 0,
  1, 2 and 6 workers on the same seed. This is the test that matters most; it runs in the unfiltered suite on a small
  bounded map.

### Change ticks (Phase 2, built with its first consumer)

- **Opt-in per pool at registration** (`ComponentChangeTracking.Tracked`, beside `ComponentChangeObservation`, fixed
  for the build; a module asks for another module's pool the way `ObserveChanges<T>()` does).
- **Stored per dense slot**: `AddedFrame` and `ChangedFrame` (`uint`, the frame from `SimulationClock`), stamped at
  the pool's one exit (`MarkChanged`) and in `Add`. The existing version counter stays: it answers "did anything write
  since I looked" between frames, where the frame number can't (Presentation writes through commands between frames).
- **Per-page dirty frame** (`EntityPages`, one `uint` a page): the newest `ChangedFrame` in the page, so "changed
  since N" skips whole pages. That is what makes the question across entities cheaper than visiting them all.
- **Removals**: a per-pool list of (entity id, frame), kept two frames, read by "removed since N".
- **Queries**: `CopyAddedSince(frame, buffer)`, `CopyChangedSince(frame, buffer)`, `CopyRemovedSince(frame, buffer)`;
  a reader remembers the frame it last ran.
- **Write only when different**: `TrySetIfDifferent(id, value)` for `IEquatable<T>` components, which skips the
  write, the stamp and the notification. For a system that rewrites the same value each visit.
- **Not for**: timers, relationships, lit items, aura sources -- anything that needs every write in order keeps
  `ComponentChanged`. `HealthWindow` keeps comparing what it shows.
- **Cost to measure before turning any hot pool on**: two `uint` writes a write on tracked pools only.

### The ambiguity report (Phase 1)

- **`SystemAccessRecorder`** (Debug, Engine): while installed, notes per system the component types it read and wrote
  (hooked where `IEntityAccessGuard.OnAccess` is called, plus the write exits) and the event types it published.
  Handlers run inside the publishing system and count as that system's access.
- **The report**: every pair of systems where one writes a component the other reads or writes, with whether a
  `RunsAfter`/`RunsBefore` chain (or the frame-start lane) fixes their order or only registration order does. The
  second kind is an *ambiguity*: a place where reordering modules silently changes results.
- **Where it lives**: a test in `Tests` writes it to `Log/diagnostics/system-access.md` and fails only on a pair that
  is new since a checked-in baseline list -- so today's accidents are listed once and decided one by one (declare an
  order, or record that the order doesn't matter and why), and a new one is caught when it appears.
  `BuiltInModulesTests`' pinned run order stays as it is.
- It also gives, for free, the table a future scheduler would need and the list of which systems are candidates for a
  fork (mostly reads, few written types).

## Phases

Each phase ends with its checks; stop after each for review.

### Phase 0 -- Measure (no production code)

Steps 2 and 3 can run at any time. Steps 1, 4 and 5 run after `PLAN-npc-behavior-composition.md` Phase 5, on
`NpcDecisionSystem`.

1. Run the `phase-performance-testing` skill headless on seed 1, frames 600-3600, and windowed over the same range.
   Record the top ten rows, and update and draw totals, in this plan.
2. Break down the worst frames: with `--diagnostics=frame,gauges`, list the ten slowest frames of a run that crosses
   several window shifts, and for each the systems and gauges (`Frame/AllocatedBytes`, GC counts, streamer work) that
   account for it.
3. Fork-join cost on this machine: a `TestCategory=Performance` test that runs an empty job across N dedicated
   threads 100,000 times and reports the median and 99th percentile, for N = 1, 2, 4, 6. (A prototype `WorkerGroup` in
   the test project is enough; it is not shipped by this phase.)
4. Per-entity read cost of the two heaviest systems (entities visited a frame, ns each) from the existing rows and
   gauges, giving the break-even bucket size for decision 7.
5. Write the results into "What the research found" and state, against decision 8's triggers, how far each system is
   from one.

**Check:**
- The plan holds a dated table for steps 1-4, with the benchmark files named.
- Each trigger in decision 8 has a current figure beside it.
- No file outside this plan, `Tests` (the performance test) and `Log/` changed.

### Phase 1 -- The ambiguity report

After `PLAN-npc-behavior-composition.md` Phase 4, so the baseline is taken over the systems that will stay.

1. `SystemAccessRecorder` in Engine (Debug only), installed by a test, never by a game session.
2. The report test and its baseline; `Log/diagnostics/system-access.md`.
3. Go through the first report with the user: for each ambiguity, add the `RunsAfter`/`RunsBefore` that was always
   meant, or baseline it with a reason.

**Check:**
- `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~SystemAccess"` passes and writes the report.
- Swap two built-in modules whose systems the report lists as ambiguous: the report still lists the pair; swap two it
  lists as ordered: the build's sort puts them back.
- Add a throwaway system that writes `TransformComponent` with no ordering: the test fails naming it and every system
  it conflicts with.
- A Release build pays nothing for the recorder: the headless A/B against the saved baseline is within run-to-run
  spread, with the same fingerprint.

### Phase 2 -- Change ticks (only with a consumer; see decision 4)

1. `ComponentChangeTracking`, the stamps, the page dirty frame, the removal list, the three queries,
   `TrySetIfDifferent`, for Packed and Multi pools (Direct: stamps and queries without pages).
2. The consumer that asked for it, moved onto the queries.
3. A/B with the skill on the pool(s) turned on.

**Check:**
- Unit tests: added, changed and removed since a frame, across a removal's swap-back, across a recycled entity id,
  and at the frame the reader last ran (inclusive or exclusive, stated once and tested).
- An untracked pool's write path compiles to what it does today (A/B within spread, same fingerprint).
- In game: the consumer's behaviour is what it was, visibly.

### Phase 3 -- Forked sections (only on a trigger; see decision 8)

1. `WorkerGroup`, `ForkedEntityLoop`, `RecordWriter<TRecord>`, `ForkedWorkerContext`, `ForkedRolls`, the worker-count
   setting, created by `EcsBuilder` and carried on `EcsContext`.
2. Debug enforcement (`IsForkOpen` checks at every seam listed above) and the architecture test.
3. Decide lazy content: measure how often a decide step would miss the catalog; then either the Debug miss check
   plus prefetch, or a safe first-ask path.
4. The frame-cost split (fork and apply rows under the system's own).
5. The determinism test across worker counts.

**Check:**
- Unit tests: records come back in entity order for every worker count and bucket length (0, 1, one chunk, many);
  a worker's exception surfaces on the main thread, lowest chunk first; the inline group gives identical records.
- Each forbidden call from inside a fork (a write scope, `Add`, `Publish`, `CreateEntity`, a stream draw) throws in
  Debug, naming what was called.
- Headless A/B with no system using a fork yet: within spread, same fingerprint (the facility costs nothing unused).

### Phase 4 -- The first forked system

The system that met the trigger. Expected to be `NpcDecisionSystem`, which by then already runs each bucket as a
choose pass and an act pass: the choose pass becomes the `Read` (one arbiter and one scratch set per worker), its
`NpcDecisionRecord` is the record, and the act pass is untouched. Aura scans are the other candidate and are on hold
with the rest of aura performance.

A system that still acts as it reads (anything other than NPC decisions) changes its seeded world when it is forked,
because each entity then reads the state before any of them wrote. It moves to two passes single-threaded first, as
its own step with its own A/B and in-game check, and is forked only after.

**Check:**
- World fingerprint equal with 0, 1, 2 and 6 workers, and equal to the two-pass build before the fork (same seed).
- Headless A/B against the saved baseline: the system's row and `EcsContext.Update`, 5 runs a side; worst-frame
  figures no worse.
- Windowed, in game: NPCs wander, engage, flee and loot as before; F3 shows the fork and apply rows; setting workers
  to 0 on the command line changes nothing visible.
- Debug build, ten minutes of play across several window shifts: no fork check throws.

## Not built, and why

- **Parallel systems by declared access** (decision 2): nothing to gain at today's costs; see the table.
- **A structural command queue with reserved ids** (decision 3): its only driver was parallel systems; alone it is a
  measured regression and a frame of latency for input, deaths and dodge windows.
- **Per-thread change queues, value-carrying change lists**: only needed if workers write. They don't.
- **A thread-safe `EventBus` and `FrameEventBuffer`**: only the apply half publishes.
- **Threading Movement**: two entities claiming one tile are settled by order.
- **Threading exposure ticks, DoTs and effect application**: an effect writes health, the ledger and the bus, and
  each is tens of microseconds a frame.

## Bookkeeping

- Done 2026-11-12: the four entries left TODO.md, and what referred to them (`FrameEventBuffer double-buffering`,
  "Component lifecycle hooks and observers", AuraSystem "further", the NPC decide note,
  `PLAN-npc-behavior-composition.md`, IMPLEMENTATION-NOTES "Component writes") points here.
- IMPLEMENTATION-NOTES gains a "Threading model" section as phases land: the rule (read in parallel, write in order),
  the triggers, and Phase 0's figures.
- CLAUDE.md's ECS section gains the rule in one bullet once Phase 3 exists; until then nothing there changes.
