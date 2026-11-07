# Component write scope: `ref` writes that still notify

(From TODO.md "TOP PRIORITY: Component write scope -- `ref` writes that still notify". Started 2026-11-05. The current
state, the case against the 2026-11-03 decision this reverses, then phases. Open decisions are at the end.)

## Verdict in one paragraph

Add a `ref struct` write scope that every pool hands out (`Write`, `TryWrite`, `WriteByDenseIndex`, and a Multi pool's
`TryWriteFirst` by matcher). It holds a `ref T` to the component, and its `Dispose` makes the same `MarkChanged` call an
updater's write makes, so every write is still versioned and observed. A new Roslyn analyzer makes a scope that isn't in a
`using` a build error. The public mutable `GetByDenseIndex` and `IncrementVersion*` methods go away. The timer wheels
write their claim marks without notifying on purpose, so they keep that access as an `internal` member of Engine.
Updaters stay for writes that are shared, carry logic, or will one day be recorded on a worker. About half of today's
one-off updaters move onto the scope. The cost should be neutral, and the `phase-performance-testing` skill confirms it
on Movement and Health.

## How it works today

- **In-place writes** take an `unmanaged` `IComponentUpdater<T>` (`TryUpdate`, `UpdateByDenseIndex`, Multi's
  `TryUpdateFirst`). There are 37 updater types (3 in Engine's `ScheduledTimerUpdates`, 34 in Game) and 59 call sites.
- **Whole writes** (`TrySet`/`SetByDenseIndex`) have 3 production callers. All of them write readonly components:
  `SpawnRecordComponent` with `with`, `ProcessingTierComponent`, and `DisplayTextComponent`.
- **The hatch.** `PackedComponentPool`/`MultiComponentPool.GetByDenseIndex` return a mutable `ref T`, and
  `IncrementVersionByDenseIndex` (or `DirectComponentPool.IncrementVersion`) is the call a caller has to remember to make
  afterwards. It has **no production callers left that write a component's value**:
  - `PackedTimerWheel`/`MultiTimerWheel` take the `ref` to claim and release a deadline (`ScheduledTimerMark.TryClaim`/
    `Release`). They must **not** notify: their own `ComponentChanged` handler schedules from that pool, so a
    notifying claim would schedule itself again.
  - 14 test uses: `ComponentChangedTests` (4), the timer wheel tests (6), the pool tests (2) and
    `UiInputControllerTests` (2).
  - *Found in Phase 1:* `DirectComponentPool.Get` was a mutable `ref` too, and it skipped versioning. It had four
    production callers, all writes to `TransformComponent` (`DeathSystem`, `EntityFactory.Spawn`, `EntityTeleporter`,
    and `GameBuildPass`'s destroy handler), plus 11 test uses. Every one of them now writes through a scope.
- **Reference-typed fields.** Nothing writes one in place today. An item's override changes by moving the unit to
  another stack (`ItemActivationSystem` builds `item with { Activator = ... }`). The scope makes that write possible
  without making anyone use it.
- **No analyzer project exists.** `Directory.Build.props` only promotes nullable warnings to errors.

## Reversing "Why not a `ref struct` writer" (IMPLEMENTATION-NOTES, 2026-11-03)

That note gave three reasons. Each one now has an answer:

1. **"It can't be stored or queued."** That is still true, and it is why updaters stay. A scope is for writes made
   at once on the main thread, which today is every write. Writes on a worker are a question for the multithreading
   research TODO. Those writes would be recorded updaters, never scopes. The plan states this rule in
   IMPLEMENTATION-NOTES so the two forms don't drift.
2. **"It holds a `ref` across a resize."** A structural change to the same pool while a scope is open could reallocate
   the dense array or swap another entity's data into the slot. That includes a change made by an observer that
   another write fires. **In Debug, a pool counts its open scopes, and `Add`, `Merge`-as-add, `Remove*`,
   `ReserveDenseCapacity` and `Resize` throw while the count isn't zero.** The check names the component, in the
   style of `SkeletonAccessGuard`. In Release nothing is checked and nothing is paid. The observers a scope's own
   `Dispose` fires run after the scope's last use of the `ref`, so they are safe.
3. **"A block can do anything."** That's accepted for immediate writes. The block is code that the calling system
   could already run. An updater's `unmanaged` purity only matters for a write that gets recorded and replayed later
   (reason 1).

## Phase 1 -- The scope in Engine

**Built 2026-11-06.** The Debug guard covers `Add`, every removal, `Clear`, `ReserveDenseCapacity` and `Resize`. A
Direct pool's `Add` doesn't move any data, so it isn't guarded. Tests use `TestTransforms.PlaceOnMap`/`RemoveFromMap`
in place of `ref pool.Get(id)`. `InternalsVisibleTo` wasn't needed: the wheel tests write through a scope.

- `Cadence/Engine/ECS/Components/Stores`: a scope type per pool kind (see Decision 1) with a `ref T` field, a
  `ref T Value` accessor, a dense index, and `Dispose` → the pool's `MarkChanged` (internal, `AggressiveInlining`).
  - Packed: `Write(entityId)` (throws through `ComponentPoolThrows` when the entity has none), `TryWrite(entityId)`
    (see Decision 2), and `WriteByDenseIndex(denseIndex)`.
  - Multi: `WriteByDenseIndex`, and `TryWriteFirst<TMatcher>(entityId, matcher)` with the same `unmanaged` matcher
    that `TryGetFirst` takes.
  - Direct: `Write`/`TryWrite`. The scope bumps the version, and the pool can't be observed, so it raises no event.
  - The access guard runs when a scope opens, as it does for `TryUpdate`.
- The Debug open-scope count and structural-change check from reason 2.
- Remove the public mutable `GetByDenseIndex` from Packed and Multi (the readonly form stays), plus
  `IncrementVersionByDenseIndex` and Direct's `IncrementVersion`. The timer wheels get an `internal`
  `GetByDenseIndexUnobserved`, named so a reader can see that it skips notification. Tests reach it through
  `InternalsVisibleTo`, if Engine already grants that to Tests; otherwise the tests drive it through the wheel.
- Update the `IComponentUpdater<T>` remarks: the reference-typed-field advice becomes "write it through a scope".
- **Tests** (`Tests/ECS/Components/Stores`): a scope write versions and fires `ComponentChanged` once, after the
  write, with the new value readable; a scope that is absent fires nothing; a Multi `TryWriteFirst` writes only the
  matched instance; in Debug, `Add`/`Remove` on a pool with an open scope throws, and the count recovers after
  `Dispose`, including when the block throws. Move the 14 hatch uses onto scopes, or onto the internal member for the
  wheel tests.

**Test checklist:**
- Run `dotnet build DungeonCrawlerWorld.sln`; nothing outside Engine and Tests should need a change.
- Run `dotnet test Tests/Tests.csproj`, which should pass, including `ComponentChangedTests` and both timer wheel test
  classes.

## Phase 2 -- The analyzer

**Built 2026-11-06.**
- `Engine.Utilities.MustBeDisposedByUsingAttribute` marks the three scopes. The analyzer matches the attribute by
  name in any namespace, so a mod can mark its own types.
- `Cadence/Analyzers` (`MustBeDisposedByUsingAnalyzer`, on Roslyn 4.14.0) is in the solution under Cadence.
  `Directory.Build.props` wires it into every project except itself and `Analyzers.Tests`.
- Its tests are a new root project, `Analyzers.Tests` (`dotnet test Analyzers.Tests/Analyzers.Tests.csproj`). Like
  Workshop.Tests, it isn't in the solution. Each test compiles a sample in memory and runs the analyzer through
  `CompilationWithAnalyzers`, because the analyzer testing package isn't in the local NuGet cache.
- A local disposed by a later `using (local)` is flagged too. The rule is "declared by a using", which keeps it to a
  single shape.

- A new engine-root project, `Cadence/Analyzers` (netstandard2.0, `Microsoft.CodeAnalysis.CSharp`). It holds one rule,
  `CAD0001`: a value of a type marked `[MustBeDisposedByUsing]` (an Engine attribute on every scope type) must be the
  declared variable of a `using` declaration or statement. This makes these errors: a discarded result, a chained
  `pool.Write(id).Value.X = 1`, a plain `var` declaration, and passing a scope as an argument. A `ref struct` already
  can't be stored in a field, so nothing more is needed.
- Wire it into every project in the solution through `Directory.Build.props` (`ProjectReference` with
  `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`), with `CAD0001` as an error. Mods built outside this
  repository are not in scope (Decision 3).
- `EngineRootArchitectureTests` already covers a new project under `Cadence/`.
- **Tests:** a small test project (or a folder in Tests) using `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`. It
  checks each flagged shape and each allowed one: `using var`, a `using (...)` block, and `TryWrite` followed by an
  early return on not-found.

**Test checklist:**
- Add `var w = pool.Write(id);` anywhere in Game. The build should fail with `CAD0001` naming the line. Remove it, and
  the build should pass.
- Run the analyzer tests, which should pass.

## Phase 3 -- Callers onto the scope

**The rule:** an updater stays when it is shared by several call sites, carries logic (a clamp, a check, or a
condition), or is an engine timer helper. A single-site updater that only assigns fields becomes a scope at its call
site, and its struct is deleted. Then read each updater before moving it. The table below goes by name and call-site
count, and is provisional.

| Moves to a scope (one site, assignments only) | Stays an updater |
|---|---|
| `SetTotalUpdate`, `SetReadyAtUpdate`, `CountUsageUpdate`, `RearmExposureUpdate`, `LockUpdate`, `AddContributionUpdate`, `SetLastDamagedUpdate`, `TakeHealthUpdate`, `ShiftedHealthUpdate`, `SetQuantityUpdate`, `SetStackDisabledUpdate`, `SetWaitUntilFrameUpdate`, `SetMovementModeUpdate`, `ClearMovementTargetsUpdate`, `SetPositionUpdate`, `PoisonTickUpdate`, `ContactTickUpdate`, `RestartExposureUpdate` | Shared: `ShiftCurrentHealthUpdate`, `ShiftCurrentManaUpdate`, `SetNextMapPositionUpdate`, `AddQuantityUpdate`, `RemoveQuantityUpdate`, the `AddStacksUpdate`s, the `BurnTickUpdate`s, `SetScoreUpdate`, the `SetExpiryUpdate`s, `RepointUpdate`. Logic: `UnlockSlotsUpdate`, `RemoveClassUpdate`, `ExtendUpdate`, `ExtendExpiryUpdate`, the `EntityBodyParts` body-part updaters (checked against the plan). Engine: `RepeatEveryUpdate<T>`, `SkipOwedPeriodsUpdate<T>`, `SetNextTickUpdate<T>` |

- A moved write keeps its order relative to the reads around it. Anything the old updater read before it was built
  is still read before the scope opens, so the block never calls into another pool of the same type.
- A structural change made in the same method as a moved write goes after the scope's block. The Debug check catches
  any that don't.

**Built 2026-11-06.** 18 updaters moved: the table's list without `TakeHealthUpdate` (it clamps at 0) and
`ShiftedHealthUpdate` (it floors at 1), which stay as logic, plus the two `SetExpiryUpdate`s (DamageLedger's and
StatModifierEffects'), which turned out to be private and single-site, not shared. A find-or-add (cooldown, scroll
mastery, damage contribution, stat modifier refresh, terrain exposure restart) writes inside the scope and returns, and
its `Add` comes after the block. `MovementSystem.TryMoveToNextMapPosition` now closes its transform scope before it
locks, syncs and records the move.

**Test checklist:**
- Run `dotnet test Tests/Tests.csproj` and `dotnet test Workshop.Tests/Workshop.Tests.csproj`, which should pass.
- In game, check each moved feature: walk and run (Movement), take lava damage, then heal at the shrine (health,
  ledger, contact ticks, aura exposures), drink a potion and stack items (quantity), get poisoned (poison tick), use an
  action and watch its cooldown, and toggle a lantern.

## Phase 4 -- Measure and document

**Built 2026-11-06.** The baseline is commit 0de7ca01 (before Phase 1), exported with `git archive` and built in Release
outside the checkout, then saved as `baseline-build-release-pre-write-scope`. Headless Release A/B, 5 runs a side, same
world fingerprint (`ab-20261106-212423.json`): `EcsContext.Update` 1.143 -> 1.131 ms/frame (-1%), Movement -1.4%, Aura
-0.3%, TerrainContact -0.6%, Poison -3.2%, BodyPartBurning +0.4%, ComplexHealthRegen -0.5%. Nothing fell outside A's own
spread. The notes entry is now "Component writes: write scopes and struct updaters". CLAUDE.md lists `Analyzers` and
`Analyzers.Tests`.

- Run the `phase-performance-testing` skill as a headless A/B: the saved baseline from before Phase 1 against the
  result. Look at `MovementSystem`, the health and damage systems, `PoisonSystem`, `TerrainContactSystem`,
  `AuraSystem` and the GameLoop total. Expect them to be neutral. Report a small explained cost rather than chasing it.
- IMPLEMENTATION-NOTES: rewrite "Component updates are unmanaged struct updaters" as two forms with the rule from
  Phase 3. Replace "Why not a `ref struct` writer" with the three answers above, and add the main-thread-only rule for
  scopes.
- CLAUDE.md: update the Pools bullet (scopes, the analyzer, no raw mutable `ref`) and the Commands section if the
  analyzer tests get their own project.
- Delete the TODO entry. In the multithreading research TODO, note that scopes are main-thread only.

**Test checklist:**
- The A/B report should show no system more than its usual noise slower. Any that is gets its own explanation in the
  report.
- `grep -rn "IncrementVersion\|GetByDenseIndex(" --include=*.cs` should find only the readonly form and the internal
  unobserved member.

## Not in scope

- Writes from worker threads, and recording a scope's write for later. Both belong to "Research multithreading".
- Skipping the notification when a scope writes nothing. A scope always notifies, as an updater does.
- Shipping the analyzer to mods built outside this repository.
- Generated property access (the Workshop inspector writing through a scope) has its own TODO. That TODO will use
  this scope.

## Decisions

1. **One scope type per pool kind** (`PackedComponentWrite<T>`, `MultiComponentWrite<T>`, `DirectComponentWrite<T>`),
   each holding its sealed pool so `Dispose` calls `MarkChanged` directly and inlines. **Decided 2026-11-06.** One
   `ComponentWrite<T>` over an interface would pay an indirect call per write; one holding the pieces (value, version,
   delegate) would be as fast but repeat `MarkChanged`, so a pool's rule for what a write records would live in two
   places.
2. **`TryWrite` returns a scope with a `Found` flag** whose `Dispose` does nothing when it is false
   (`using var write = pool.TryWrite(id); if (!write.Found) return;`). *Recommended*, because then the analyzer has a
   single rule: every scope is a `using`. The other option is `bool TryWrite(id, out var write)`, which reads more like
   the rest of the pool API but needs a second analyzer shape (`if (TryWrite(...)) { using (write) ... }`).
   **Decided 2026-11-06.**
3. **`CAD0001` is an error in every project in the repository.** Mods outside it aren't covered until mod packaging
   exists. **Decided 2026-11-06.**
4. **Remove the public mutable `GetByDenseIndex` and the `IncrementVersion*` methods outright**, with no obsolete
   period, since this is the initial development phase. **Decided 2026-11-06.**
5. **Gate:** the TODO says to plan this "once `PLAN-data-driven-actions-and-items.md` and its code reviews are
   complete". **Confirmed 2026-11-06.**
