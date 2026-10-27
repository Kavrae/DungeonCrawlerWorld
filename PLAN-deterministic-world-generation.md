# Deterministic world generation, independent of player actions

(TODO.md "Improved deterministic random -- world generation independent of player actions" (Game, High). Planned
2026-10-27. Three phases, each ending in a stop for testing. Phase 1 alone fixes what the TODO's title names; Phase 2
is the "one stream per purpose" bullet; Phase 3 is the guard and the paperwork.)

## Context

What exists today, checked in the code:
- **One shared sequence.** `WorldSessionBootstrapper.Build` makes one `MathUtility(new SeededRandom(randomSeed))` and
  hands it to `NeighborhoodRecords`, `GameBootstrapper.Build` -> `GameBuildPass` -> `GameModuleContext.MathUtility`,
  and `FloorBuilder.CreatePlayer` (the player's and the test dummy's spawn seeds, `mathUtility.NextSeed()`).
- **Who draws from it** (every gameplay roll in the game):
  - `EffectServices.MathUtility` (built lazily in `GameModuleContext` from the same instance): `DirectDamage`'s flat
    roll and crit, `ChainedEffect`'s proc, `BodyPartTargeting`, and through them `HealthDamage`/`HealthHeal` ->
    `ComplexHealthDamage`/`ComplexHealthHeal` -> `BodyPartSelection.PickRandom`.
  - `BurningSystem` and `PoisonSystem` (`context.MathUtility`, for the body part a tick lands on through `HealthDamage`).
  - `TestCombatBehaviorSystem` (`context.MathUtility`): attack pick, mode pick, wander coin and
    `MovementCandidates.TryPickRandomAdjacentPosition`. Nothing in Movement itself rolls.
- **Neighborhood seeds are drawn from it.** `NeighborhoodRecords.GetOrCreate` assigns `mathUtility.Next(int.MinValue,
  int.MaxValue)` the first time a coordinate is asked for. The starting window is assigned first
  (`TestMapBuilder.AssignRecords`, before any gameplay), so it is already stable per seed -- which is what
  `FloorBuilder.StartingNeighborhoodRecords` and the Workshop's `NeighborhoodPreviewBuild` rely on. **Every neighborhood
  first reached after that** (the streamer's loads after a `WindowShifted`) gets whatever value the shared sequence is
  at: it depends on every combat roll, wander and body-part pick so far, and on the order the player reached
  coordinates.
- **Everything below the record is already pure.** `TestMapBuilder.Plan(map, record, populationSeed)` reads only the
  record's seed and the population seed: layout from `record.Seed`, shrines from `record.Seed ^ HealingShrineSeedSalt`,
  fixed areas from `AreaSeeds.ForFixedPlacement(record.Seed, placementId)`, area cells and placements from
  `AreaSeeds.ForCell`/`ForPlacement`, unique individuals from `AreaSeeds.ForUnique(areaId, placementId)` (no seed at
  all), cell looks from `CellSeeds` (layout seed and position), and each creature's seed from the population's own
  sequence. The population seed is `NeighborhoodRecord.Mix(Seed, PopulationCount)`: the visit index.
- **Sequences already kept apart, each salted by hand:** the crawler numbers (`UniqueNumberAllocator`,
  `randomSeed << 32 | CrawlerNumberSalt` in `WorldSessionBootstrapper`), the factory's runtime spawn seeds
  (`EntityFactory._runtimeSeeds`, `runtimeSpawnSeed = (uint)randomSeed`), the loot box opener (`runtimeSpawnSeed ^
  LootboxRollSalt`), and each entity's own rolls (`EntityBuilder` reseeds one `SeededRandom` from the spawn record's
  seed; `Apply` mixes in `blueprint.Definition.Id.GetHashCode()`).
- **Three mixers.** `NeighborhoodRecord.Mix` (32-bit murmur finalizer), `CellSeeds.Mix` (32-bit, three rounds, the
  terrain image's hot path), `AreaSeeds.Mix` (SplitMix64 finalizer, plus FNV-1a for strings and a Guid fold).
- **Builds that aren't a session** (mod trials, the staging world, the Workshop's content session and area previews,
  `ContentManifestExporter`) each pass `new MathUtility(new SeededRandom())`.
- **Presentation's own** `FloatingTextController` uses `new Random()` for text jitter: display only, never simulation,
  out of scope.
- **Unique placements** (`UniqueEntityRoster`: one alive, never again once killed) depend on what the player did by
  design. That is game state, not generation: the plan is the same, only which entry spawns differs.

## Design

### Derive, don't draw
- **A neighborhood's seed is a function of (session seed, floor, cell X, cell Y)**, computed by a new
  `Game.World.NeighborhoodSeeds.For(worldSeed, floorNumber, cellX, cellY)`: `SeedMix` over a fixed salt, the floor,
  and the packed coordinate, narrowed to `int` (the record's `Seed` stays an `int`; `Map.CreateLayout` and every
  consumer take one). `NeighborhoodRecords` takes the world seed and floor instead of a `MathUtility` and computes the
  seed in `GetOrCreate`; the record is still created lazily (it carries the visit count), but its seed no longer
  depends on when.
- **One mixer in the engine.** `Engine.Math.SeedMix`: `Mix(ulong, ulong)` (SplitMix64's finalizer, today's
  `AreaSeeds.Mix`), `Of(Guid)`, `Of(string)` (FNV-1a), `Of(int)` (no sign extension) and `Pack(int, int)` for a pair. `AreaSeeds` keeps
  its salts and methods and delegates to it; `NeighborhoodRecord.Mix` goes. `CellSeeds` stays as it is: it is the
  terrain image's per-cell cost, already pure, and changing it would only reshuffle art variants.
- **Session setup seeds are derived too.** The player's spawn seed and the test dummy's become
  `SeedMix.Mix(sessionSeed, PlayerSpawnSalt)` / `TestDummySpawnSalt`, so nothing at startup draws from a sequence.
- **`EntityBuilder.Apply`** mixes the applied blueprint's Guid through `SeedMix.Of(Guid)` instead of
  `Guid.GetHashCode()` -- deterministic today, but it is the one seed in Game built from a `GetHashCode`, against the
  rule `AreaSeeds` states.
- **`FloorBuilder.StartingNeighborhoodRecords(seed)`** stays (the Workshop preview and its tests use it) but is now a
  plain derivation, and its remarks lose the "drawn before anything else" caveat. It could take any coordinate now;
  widening the Map tab's preview past the starting window is not part of this plan.

### One stream per purpose
- **Engine: `Engine.Math.RandomStreams`** (session seed in; `Get(RandomStreamKey key)` returns that stream's
  `MathUtility`, created on first ask and cached) and **`RandomStreamKey(Guid Owner, string Name)`**, the same shape as
  `SettingKey<T>`: a stream's seed is `SeedMix.Mix(SeedMix.Mix(sessionSeed, SeedMix.Of(Owner)), SeedMix.Of(Name))`.
  Owned by a module's Id, so a mod's streams never shift a built-in's, and a replacement module takes over the stream
  of the module it replaces. Streams are created in `Configure`/`RegisterBehavior` only (the build is single-threaded);
  `Get` after `BeginSession` is allowed but the streams themselves are main-thread only, like the shared one today.
- **`GameModuleContext.MathUtility` is replaced by `GameModuleContext.RandomStreams`.** Each consumer asks for its own:
  - `EffectServices` -- one stream, `EffectServices.RandomStream` (owner Core's `ModuleId`, name "Effects"): every
    effect entry's rolls and the body-part picks they lead to.
  - `BurningModule`, `PoisonModule` -- one each ("Ticks"), for their tick's body-part pick.
  - `NpcBehaviorModule` -- one ("Behavior"), handed to `TestCombatBehaviorSystem` (and on to `MovementCandidates`).
  - The factory's runtime spawn seeds, the loot box opener and the crawler numbers move onto the same derivation
    (`RandomStreams.SeedFor(key)` for the two that hold a `SeededRandom`/allocator of their own), so every salt lives
    beside the thing it seeds and none is hand-XORed in a bootstrapper.
- **The session passes a seed, not a sequence.** `GameBootstrapper.Build`/`GameBuildPass.Run`/`BuildModules` take
  `ulong sessionSeed` in place of `MathUtility mathUtility` and `runtimeSpawnSeed`; the non-session builds pass 0 (the
  Workshop's area preview keeps its preview crawler seed). `WorldSessionBootstrapper` holds no `MathUtility` at all.
- **`NeighborhoodRecords` and `TestMapBuilder` never see `RandomStreams`**: generation reads only derived seeds, which
  is what keeps a worker-thread `Plan` safe and gameplay out of it.

### Population
**Keep the visit index.** A neighborhood's population seed stays `Mix(record seed, visit index)`: the index is the
player's doing (a return brings new creatures, by design) but it is a count, not a time or a roll -- two runs that
visit a coordinate the same number of times get the same creatures there, whatever happened in between. A reload
from the streamer's cache doesn't count a visit (it repopulates nothing), and a load cancelled before it starts
counts none (already so). It moves from `NeighborhoodRecord.Mix` onto `SeedMix` with its own salt, so a population
seed can never equal a layout seed.

### What "Real map generation" must keep
That TODO's starting point -- "roll each neighborhood's record against its already-assigned neighbors when the window
shifts" -- is order-dependent as written. The rule this plan adds: **a neighborhood's choices may read its neighbours'
derived values, never which neighbour was decided first.** A shared edge is decided from the pair's own seed
(`SeedMix` of both coordinates, smaller first), and a template is picked from the neighborhood's seed among those
compatible with its four edges. Phase 3 rewrites that TODO paragraph to say so.

## Phases

### Phase 1 -- derived neighborhood and setup seeds
1. `Engine.Math.SeedMix` (+ `Tests/Math/SeedMixTests`: fixed outputs for fixed inputs, so a change to the mixer is a
   deliberate one). `AreaSeeds` delegates; values unchanged.
2. `Game.World.NeighborhoodSeeds.For(worldSeed, floorNumber, cellX, cellY)`; `NeighborhoodRecords(int worldSeed, int
   floorNumber)`; `NeighborhoodRecord`'s population seed on `SeedMix` with a salt; `NeighborhoodRecord.Mix` removed.
3. `WorldSessionBootstrapper` builds records from the seed and floor; `FloorBuilder.CreatePlayer`/`SpawnTestDummy` take
   the session seed and derive; `StartingNeighborhoodRecords` simplified.
4. `EntityBuilder.Apply` through `SeedMix.Of(Guid)`.
5. Tests: `NeighborhoodRecordsTests` -- the same seed for a coordinate whatever order coordinates are asked for, and
   different for neighbouring cells, floors and session seeds; `StartingNeighborhoodRecordsTests` updated (values
   change, the properties hold).

**Test after Phase 1**
- Run `--seed=1`, walk east until the window shifts and a new neighborhood loads; note its layout near its west edge.
  Restart on `--seed=1`, fight a few goblins first, then take a different route to the same place: identical layout,
  same shrines.
- Same start, same walk, but visit that neighborhood twice (leave past the cache and come back): the creatures change,
  the walls and shrines don't.
- Workshop Map tab, Neighborhood preview on a seed: still opens and matches the game's starting area on that seed.

### Phase 2 -- one stream per purpose
1. `Engine.Math.RandomStreams` and `RandomStreamKey` (+ tests: same key same sequence, different owner or name
   independent, a stream's values don't move when another stream is drawn from).
2. `GameModuleContext.RandomStreams` replaces `MathUtility`; `EffectServices`, `BurningModule`, `PoisonModule`,
   `NpcBehaviorModule` ask for their keys.
3. `GameBootstrapper.Build`/`GameBuildPass` take `sessionSeed`; the factory, loot box opener and crawler numbers seed
   from it through their keys; the hand salts in `WorldSessionBootstrapper` and `GameModuleContext` go.
4. Every non-session build (`ModValidation`, staging, `ContentSession`, `ContentManifestExporter`, test helpers
   `BuiltInTestModules`/`TestSystems`/`TestHealth`/`TestActionEffects`) passes a seed; tests that stub rolls pass their
   stub as the stream (the `EffectServices`/system constructors still take a `MathUtility`).

**Test after Phase 2**
- Play a few minutes on a fixed seed: combat, crits, burning and poison ticks, NPC wandering and attacks all still vary
  and work as before.
- Open several loot boxes on a seed, restart, open them in the same order: same contents (unchanged behaviour).
- Two crawlers' numbers on a seed are the same as each other run (unchanged behaviour, new derivation).
- `dotnet test Tests/Tests.csproj` and `Workshop.Tests` pass.

### Phase 3 -- guard, baselines, paperwork
1. **End-to-end guard** (`Tests/Floors/WorldGenerationIndependenceTests`): two sessions on one seed over a bounded
   map; in one, draw a few thousand values from every gameplay stream and spawn and kill creatures through the factory
   at runtime; then request the same set of neighborhood plans in a different order in each. Plans compare equal:
   layout cells (`Map.CopyCells` over the loaded neighborhood, or the plan's `NeighborhoodLayout`), the spawn list
   (blueprint, position, seed) and the area instances. A second case: the same coordinate on two floors differs.
2. **Architecture guard:** `NeighborhoodRecords`, `TestMapBuilder` and `AreaStamp` take no `RandomStreams` (a
   reflection test over their constructors and fields), so generation can't start drawing from gameplay later.
3. Re-record benchmark baselines (`phase-performance-testing`): seeded worlds change once in Phase 1 and the headless
   fingerprint changes; note it in the saved run.
4. Docs: CLAUDE.md (World & Map: neighborhood seeds are derived; a new bullet under Engine for `SeedMix`/
   `RandomStreams`: "derive, don't draw; a gameplay roll draws from its module's stream"), IMPLEMENTATION-NOTES (a
   "Deterministic generation" section with the decisions below), `NeighborhoodRecords`'/`RandomSeed`'s remarks, the
   "Real map generation" TODO's starting point, and remove this TODO entry.

**Test after Phase 3**
- `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~WorldGenerationIndependence"` passes; comment out the
  derivation (draw from a stream again) and it fails.
- A headless benchmark on seed 1 prints the same fingerprint twice in a row.

## Decisions to settle in review
1. **Population keeps the visit index** (above), rather than a seed the player can't steer at all (which would make
   every return bring the same creatures). Recommended: keep.
2. **Streams keyed by owner Guid + name** (mod-safe, replacement takes over), rather than a fixed enum of purposes in
   Game. Recommended: Guid + name.
3. **Granularity:** one Effects stream for every effect entry (damage, crit, proc, body part), not one per entry type.
   Finer splits buy little -- one more hit shifts the next roll either way -- and cost a stream per entry kind.
4. **`CellSeeds` stays on its own 32-bit mixer** (hot path, already pure), so terrain art variants don't reshuffle.
5. **`MathUtility` keeps both halves** (static clamps and the instance roll wrapper). Splitting the roll wrapper into
   its own type is a rename across ~100 test files and isn't needed for this; leave it unless you want it.
6. **The "Real map generation" rule** (choices read neighbours' derived values, never assignment order) goes into
   that TODO now, before its own plan is written.
