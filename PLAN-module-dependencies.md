# Module dependencies

(Pre-implementation. Replaces two TODO.md entries, "Fix module dependencies" and "Investigate moving
runtime pool checks to hard dependencies". Both are deleted when the last phase lands.)

## Context

`IModule.Dependencies` (`Engine/Modules/IModule.cs`) is `IReadOnlyList<Type>`. `Bootstrapper.TopologicalSort`
(`Engine/Bootstrap/Bootstrapper.cs`) keys modules by `GetType()`, walks each module's `Dependencies`
depth-first in list order, and throws when a dependency is missing or when there is a cycle. That one
list decides two separate things:
- **What a module requires.** A missing dependency throws, and `GameBootstrapper.DryRunValidateMods`
  drops the mod.
- **When its systems run.** `RegisterAllSystems` walks the sorted list, and `SystemManager` runs systems
  in registration order. Every component is registered before any system, so the order in which
  components are registered doesn't matter.

`ModuleSet.Combine` replaces a built-in by `Guid Id`, but the replacement is a different type, so a
`typeof(...)` dependency on the replaced module no longer resolves. To avoid that, modules skip real
dependencies. Code outside modules does the same. In total about 110 places look up another module's
pool at runtime, through `IsRegistered<T>()` or `GetOptional*Pool<T>()`, and each one leaves a null path
behind.

### Why nearly every check can become hard

Mods can only **add** modules or **replace** them by Id (`ModuleSet.Combine`). They can't remove one.
So in the real game every built-in module's Id is always present, and a built-in pool can be missing in
only two cases:
1. **A test that builds a minimal module set or a bare `ComponentManager`.** Per "tests don't drive
   design", that isn't a reason to keep a null path.
2. **A replacement module that doesn't register what it replaced.** Nothing rules this out today. Phase 3
   adds a rule that it must.

With that rule in place, a built-in reading another built-in's pool can always fetch it hard, whether
the reader is a module, a shared helper, the composition layer or Presentation. No check anywhere reads
a pool that only a mod adds, so the audit classifies **every** check as hard, and none stay optional.
The per-site record is in the Phase 5–7 tables.

Two checks turned out to be ownership mistakes rather than dependencies, and are fixed rather than
declared (Phase 4 and Phase 5):
- **Health reads `BodyPartBurningTimerComponent`**, so that `BodyPartSelection.PickLowestPercentage`
  (regen and targeted heals) skips a part that is on fire. The type is declared in Health's namespace,
  but `BurningModule` registers the pool, so Health depends on a status effect module. Poison shows the
  right shape: it keeps its own entity-scoped `PoisonTimerComponent`, acts only through Health's API
  (`HealthDamage.Apply` on an Internal part), and Health never learns that poison exists. The burning
  check is also almost entirely redundant. Health already has a generic per-part gate
  (`BodyPartStateComponent.RegenLockedUntilFrame`, read by the same `PickLowestPercentage`), and every
  burn tick (1 s) calls `BodyPartDamageEffects.ResetRegenLockout`, which pushes that gate 10 s out. The
  only time the burning check does anything is the gap between ignition and the first tick.
- **Movement reads `PendingActionActivationComponent` and `PendingConsumableActivationComponent`**
  (`MovementSystem`, marked `//TEMPORARY`), so that an entity with an activation queued this frame
  doesn't also take a step. The player never needs this check: `PlayerInputBuffer` already clears
  `NextMapPosition` whenever it queues an activation, and `MovementSystem` only steps when
  `NextMapPosition` is set. Only the NPC systems (`TestCombatBehaviorSystem`, `TestDummyAttackSystem`)
  queue activations without clearing it.

### Two bugs found while planning

1. **Built-in Ids collide.** Six pairs share an `Id`: Achievement/Inventory (`…010`), Crawler/CoreItems
   (`…011`), StatModifiers/BodyPartEffects (`…012`), Currency/ProcessingTier (`…016`),
   CoreActions/Containers (`…017`), NpcBehavior/Shop (`…018`). `Combine` replaces the first match, so a
   mod meant to replace `InventoryModule` would replace `AchievementModule` instead. Free Ids in the
   same series: `…002`, `…00d`–`…00f`, `…019`–`…01f`.
2. **The dry run doesn't actually replace.** `DryRunValidateMods` builds
   `new List<IModule>(builtInModules) { mod }`, so a replacement runs next to the module it replaces. A
   real replacement that registers the same components would fail with "already registered" every
   time. `ReplacementHealthModule` only passes because it registers nothing.

## Design (decided)

```csharp
IReadOnlyList<Guid> Requires => [];   // must be present; says nothing about order
IReadOnlyList<Guid> RunsAfter => [];  // ordering only; ignored if the target is absent
IReadOnlyList<Guid> RunsBefore => []; // ordering only; ignored if the target is absent
```

- **References.** Every built-in declares `public static readonly Guid ModuleId = new("…");` and
  `public Guid Id => ModuleId;`. Dependents refer to it as `Requires => [HealthModule.ModuleId]`. Mods
  do the same.
- **Requires does not imply RunsAfter.** `Requires` only checks presence. It is validated before
  sorting, and every missing requirement is reported in one exception that names both modules. Mutual
  `Requires` is legal (Death ↔ StatusEffectAura in Phase 5), since presence has no order.
- **Sort.** A stable topological sort over `RunsAfter` plus the reverse of `RunsBefore`. Ties keep input
  order, so `builtInModules` stays the default order and a replacement takes the replaced module's
  position. A cycle throws with its full path. Modules are keyed by `Id`. `Guid.Empty` is keyed per
  instance and can never be a target. A duplicate non-empty Id throws, and the existing check for the
  same type twice stays.
- **The replacement contract (Phase 3).** A module's registered component types, with their pool kind,
  are its contract. When a mod replaces a built-in, the dry run requires it to register every component
  type the built-in registered, as the same pool kind. It may register more, and it may register no
  systems at all ("replace behavior, keep the data"). This is what makes the always-present argument
  above hold for code outside modules.
- **Requires must be complete (Phase 5).** A test builds each built-in module together with only its
  `Requires` closure and checks that `RegisterSystems` doesn't throw. A cross-module pool fetch with no
  matching `Requires` entry fails that test.

Unchanged: `SystemManager.RegisterFirst` (`SpawnMoves`), the rule that components register before
systems, `ModuleLoader`, and `Combine`'s replacement rule. The `FrameEventBuffer` double-buffering TODO
doesn't interact with this. The Movement → consumer `RunsAfter` edges stay declared either way.

## Phases

Stop after each phase for in-game testing and a go-ahead. Except for Phase 4, which changes NPC
behavior on purpose, every phase should leave the real game's behavior unchanged. Only tests' module
sets change.

### Phase 1: Fix the Ids and pin the current order

- Give the second module of each colliding pair a fresh Id. No save or mod data references these.
  `ReplacementHealthModule` uses Health's `…003`, which is unique.
- Pull the list local in `GameBootstrapper.Build` out into `BuiltInModules()`.
- Test: every built-in Id is non-empty and unique.
- Test, **pinned system run order**: build the real built-in set, attach an `IFrameCostRecorder` that
  records `item` names in call order, run one `SystemManager.Update`, and assert the full ordered list
  of system type names. This uses the existing public hook, so no test-only accessor is needed. Every
  later phase must leave this test unchanged.
- Audit and write down whether any `RegisterSystems` relies on another module's `RegisterSystems`
  having already run (e.g. `ProcessingTierResolver.Wire`, `LocalTierRoster.Wire`). If one does, it
  becomes a `RunsAfter` edge in Phase 2.

In-game check: normal startup, and the Admin Mode spawn/apply menus.

### Phase 2: The mechanism

- `IModule`: replace `Dependencies` with the three lists and rewrite its remarks. Add `ModuleId`
  statics to the built-ins.
- Translate each existing `Dependencies` entry to its real meaning:

  | Module | Requires | Ordering |
  |---|---|---|
  | Movement | Core | — |
  | ProcessingTier | Movement | RunsAfter Movement |
  | ContactDamage | Movement | RunsAfter Movement |
  | StatusEffectAura | StatusEffects | RunsAfter Movement |
  | Burning, Poison, Paralysis | StatusEffects | — |
  | AbilityScores | StatModifiers | — |
  | Inventory | Actions | — |
  | Containers | Inventory | — |
  | Shop | Inventory, Currency | — |
  | NpcBehavior | — | RunsBefore Movement (replaces the list-position comment) |

- `Bootstrapper`: validate requirements, then run the Id-keyed stable sort with the cycle path in the
  error and the duplicate-Id check.
- `DryRunValidateMods`: build the trial set with `ModuleSet.Combine` (fixes bug 2).
- `BootstrapperTests`, one test each:
  - A requirement is satisfied by a same-Id replacement of a different type.
  - A missing requirement throws and names both modules.
  - `Requires` alone imposes no order.
  - `RunsAfter` and `RunsBefore` order modules, and are ignored when the target is absent.
  - A cycle throws and shows its path.
  - Duplicate non-empty Ids throw.
  - Two `Guid.Empty` modules coexist.

  The existing circular and missing-dependency tests move over to Ids.

In-game check: movement; lava and aura damage; crossing a neighborhood edge (ProcessingTier runs after
Movement); an NPC fight (NpcBehavior runs before Movement). A headless `--seed` run before and after
(`phase-performance-testing`) should report the same per-system rows in the same order.

### Phase 3: The replacement contract and the test helper

- `DryRunValidateMods`: when a mod's Id matches a built-in, run the built-in's `RegisterComponents` and
  the mod's `RegisterComponents` into two throwaway `ComponentManager`s. Compare their
  `(Type, pool kind)` sets. If anything is missing, the mod fails and the `ModuleFailure` names each
  missing component. `ComponentManager` may need a read-only enumeration of registered types and their
  kinds. Diagnostics' pool memory report could use one too.
- `ReplacementHealthModule`: a real replacement. It references Game with `Private="false"` (the
  `ExampleMod` pattern), registers Health's components and no systems. The replacement test then
  observes that `SimpleHealthRegenSystem` is absent, using the pinned-order recorder, instead of relying
  on the component being unregistered. Add a second fixture that registers nothing, and assert that it
  fails its dry run with the missing components named.
- A test-side helper: `BuiltInTestComponents.RegisterAll(ComponentManager)`. It runs every
  `BuiltInModules()` entry's `RegisterComponents`. If a module's `RegisterComponents` turns out to need
  `Configure` state (check `AbilityScoresModule`, which may subscribe there), the helper configures
  through a throwaway `GameModuleContext`. About 68 test files build a bare `ComponentManager` and
  register pools by hand. Phases 5–7 move whichever ones break onto this helper instead of adding the
  newly required pools one at a time.

In-game check: startup with `Mods.ExampleMod` and `Mods.TestFixtures` in `Mods/`. Only the fixtures
meant to fail should show up in the failure list.

### Phase 4: A queued activation clears the step (Movement stops reading activations)

This phase comes before the module conversions, so Phase 5 never declares Movement → Actions or
Movement → Inventory. Unlike the other phases, this one changes behavior (for NPCs only).

- Apply the player's existing rule everywhere: **whatever queues an activation clears
  `NextMapPosition` in the same write.** `TestCombatBehaviorSystem`, both at the consumable queue
  (`PendingConsumableActivationComponent`) and at the action queue, and `TestDummyAttackSystem` clear it
  through `MovementComponent`, the same way `PlayerInputBuffer.SetNextMapPosition(…, null)` does.
- `MovementSystem`: delete the `_pendingActionActivations`/`_pendingConsumableActivations` fields,
  constructor parameters and the `//TEMPORARY` gate. `MovementModule` stops fetching both pools.
- NpcBehavior keeps `RunsBefore Movement`. If it ran after Movement, Movement would already have taken
  this frame's step before the clear happened. Rewrite the reason in `TestCombatBehaviorSystem`'s doc
  comment: it currently says Movement *checks* the pending request, and after this the behavior system
  *clears* the step.
- Check whether `TestDummyAttackSystem`'s dummies hold a `MovementComponent` at all. If they don't, the
  clear there is a no-op `TryUpdate` and can be left out.
- Tests: one each for NPC combat and the dummy. An entity that has `NextMapPosition` set and queues an
  activation doesn't move that frame, and the activation still resolves. Existing `MovementSystem`
  tests that built the gate's pools drop them.

In-game check: NPC fights. An NPC that stops to attack or drink a potion doesn't slide a tile at the
same moment, and it resumes wandering afterwards. The player's move-then-attack and attack-then-move
input buffering behaves the same as before.

### Phase 5: Modules

Every `IsRegistered`/`GetOptional*` check in a module's `RegisterSystems` becomes a hard `Get*Pool`.
The owning module is added to `Requires`, along with any cross-module pool the module already fetched
hard without declaring it (e.g. `ProcessingTierComponent`, `ActionLockComponent`). The early-return
guards go away, system constructors take non-null pools, and the null handling inside those systems is
deleted.

| Module | Checks removed | Requires after |
|---|---|---|
| Actions | early return on `SimpleHealth`; optional StatModifier, Dead, Mana, AbilityScores, AuraSource, BodyPartState, MeleeDisabled, ProcessingTier | Core, Health, StatModifiers, Death, Mana, AbilityScores, StatusEffectAura, BodyPartEffects, ProcessingTier, Race, Blueprints |
| BodyPartEffects | early return on `BodyPartState`; optional StatModifier | Health, StatModifiers, ProcessingTier, Race |
| Burning | early return on `SimpleHealth`; StatModifier, BodyPartState, Dead | StatusEffects, Health, StatModifiers, Death, Race |
| ContactDamage | early return on `SimpleHealth`; StatModifier, Dead, BodyPartState | Movement, Health, StatModifiers, Death, Race |
| Death | AuraSource | Core, StatusEffectAura |
| Health | StatModifier, Dead, AbilityScores, BodyPartBurningTimer (deleted, not converted; see below) | StatModifiers, Death, AbilityScores, ProcessingTier, Race |
| Inventory | early return on `SimpleHealth`; StatModifier, Dead, Mana, HotkeyExpansionUnlock, AbilityScores, AuraSource, ItemHotkeyBinding (own), BodyPartState, ProcessingTier | Actions, Health, StatModifiers, Death, Mana, AbilityScores, StatusEffectAura, ProcessingTier, Race |
| Mana | StatModifier, Dead, AbilityScores | StatModifiers, Death, AbilityScores, ProcessingTier |
| Movement | Dead, AuraSource, StatModifier, MovementDisabled (the activation pools are already gone, Phase 4) | Core, Death, StatusEffectAura, StatModifiers, BodyPartEffects, ProcessingTier |
| NpcBehavior | 5-pool early return; Dead | Core, Health, Inventory, Actions, Death, Movement, ProcessingTier, Race |
| Paralysis | early return on `ActionLock` | StatusEffects, Core |
| Poison | early return on `SimpleHealth`; StatModifier, BodyPartState | StatusEffects, Health, StatModifiers, Race |
| StatusEffectAura | Dead | StatusEffects, Death, ProcessingTier, Core |

(The exact lists get confirmed by the Requires-closure test, not by this table. `Race` appears wherever
`EntityBodyParts` is built, and `Blueprints` wherever `EntityActions` is built. Both are covered in
Phase 6.)

- **Health stops reading Burning's timers.** The rule, which Poison already follows: a status effect
  acts on health only through Health's API (`HealthDamage`, `BodyPartDamageEffects`), and Health never
  reads a status effect's components. Health keeps its own generic gate
  (`BodyPartStateComponent.RegenLockedUntilFrame`), and effects write it through that API.
  - `BurningAuraApplier.ApplyBodyPartScopedStack`: on ignition (the 0-to-1 transition that adds the
    `BodyPartBurningTimerComponent`), call `BodyPartDamageEffects.ResetRegenLockout`. This closes the
    only gap the burning check covered. After this change, a part is regen-locked from ignition through
    10 s after its last tick, which matches what players see today.
  - Delete `BodyPartSelection.IsCurrentlyBurning`, and remove the `bodyPartBurningTimers` parameter
    everywhere it's threaded through: `PickLowestPercentage`, `ComplexHealthHeal.ApplyToSinglePart` and
    `ResolvePartId`, `HealthHeal.Apply`, `ComplexHealthRegenSystem`, and every caller that passes it.
    Also delete `HealthModule`'s fetch of the pool.
  - Move `BodyPartBurningTimerComponent` from `Game.Modules.Health.Components` to
    `Game.Modules.Burning.Components`, next to `BurningTimerComponent`. Burning keeps registering it.
    Remove its doc remark about "must sit on the Health side", and remove the mention of it in
    `EntityBodyParts`' `PartId` doc.
  - Health's `Requires` gains nothing from Burning. Burning already requires Health.
  - Tests: remove the burning-exclusion cases from the `BodyPartSelection` and regen tests. Add a
    Burning test showing that a part ignited this frame is regen-locked before its first tick, and is
    skipped by `PickLowestPercentage`.
- Add the **Requires-closure test** (see Design).
- Rewrite every comment whose stated reason was the replacement or cycle problem: Movement's "would be
  circular", BodyPartEffects', Death's, Health's, and the "minimal test module set" reasoning in
  Health, Mana and elsewhere.

In-game check: potions and scrolls, burning (a part stops regenerating the moment it catches fire and
starts again 10 s after the fire goes out, and a healing potion skips it meanwhile), poison, paralysis, body-part damage limiting movement and melee, mana regen, NPC combat, and
a shop purchase.

### Phase 6: Shared helpers and the composition layer

These aren't modules. They're safe to make hard for one of two reasons: every module that calls them
now `Requires` the owner (Phase 5), or they run on the full game set, where the Phase 3 contract
applies.

| Site | Check | Becomes |
|---|---|---|
| `EntityBodyParts.For` | RaceSlots | hard |
| `EntityActions.For` | SpawnRecord, AppliedBlueprint | hard |
| `EntityNaming` (4) | SpawnRecord, AppliedBlueprint | hard |
| `EntityFactory` (7) | ProcessingTier, SpawnRecord, ActionLock, Crawler, RaceSlots, ClassSlots, Mana | pools hard; the data checks (`definition.Race is not null`, `ManaCost > 0`) stay |
| `MaximumHealthShift` | BodyPartState, StatModifier | hard |
| `AbilityScoreEffects` (2) | AbilityScores, StatModifier | hard |
| `PoisonEffects` | StatModifier | hard |
| `BurningAuraApplier` | BodyPartState, ContactDamageExposure | hard. Burning also `Requires` ContactDamage. |
| `StatusEffectImmunity` | StatusEffectImmunity | hard |
| `CurrencyActions` (2) | Currency | pool hard; the source ≠ destination check stays |
| `InventoryActions` | MaxStackSize | hard |
| `ShopMarginPricing` | AbilityScores | hard. Shop also `Requires` AbilityScores. |
| `ActionSource` | DisplayText, Crawler | hard |
| `MapViewQuery` (11) | BodyPartState, StatModifier, Dead, InventoryStacks, Looted, Container, Shop, ActionLock, PendingDelayedAction, Dodging, SpawnRecord | hard |
| `GameBootstrapper` (4) | Movement/PendingDelayedAction (teleporter), AuraSource (destruction), ProcessingTier (simulation scope) | hard |

In-game check: spawn and apply from Admin Mode, inspection of an unsimulated creature (the
`SpawnRecordRebuilder` staging world), teleport, container loot, and a shop and trade round trip.

### Phase 7: Presentation

The same always-present argument, since Presentation only ever sees the full game set.
`UiInputController` (Shop), `AbilityScoreModifierFormatter`, `AbilityScoreWindow`, `CurrencyRowContent`
(2), `InspectionWindowContent` (2), `InventoryGridContent` (3), `PlayerHealthBarContent`,
`PlayerManaBarContent`, `HealthWindow` (4) and `TradeWindow` (2) all become hard fetches, and the
null-pool branches in their draw and format paths go away. The roughly 16 Presentation test files that
build a bare `ComponentManager` move onto the Phase 3 helper as they break.

In-game check, per `UI changes` in CLAUDE.md: the health window (buffs/debuffs, burning body parts,
immunities, potion cooldown), health and mana bars, ability scores window, inventory in shop mode,
trade window, currency row, and inspection.

### Phase 8: Docs and cleanup

- Delete `ComponentManagerOptionalPoolExtensions` (Engine) if nothing still calls it. `IsRegistered`
  stays, since the Phase 3 contract comparison and tests use it.
- CLAUDE.md:
  - The ECS bullet ("topo-sorts by `Dependencies`") and the Modding bullet describe the three lists and
    say that requiring doesn't imply order.
  - The Modding bullet also states the replacement contract.
  - Add the rule for new code: a built-in pool is always fetched hard, and a cross-module fetch in a
    module needs a `Requires` entry (the closure test enforces this).
  - Add the rule that a status effect acts on health only through Health's API, and Health never
    reads a status effect's components.
- `FrameEventBuffer.Record`'s exception text says "Dependencies ordering". Change it to "`RunsAfter`
  ordering".
- IMPLEMENTATION-NOTES.md: the decisions, the audit's outcome (every check was hard, and why), and the
  rule from Phase 4 that whatever queues an activation clears `NextMapPosition`.
- TODO.md: delete both entries.
- `phase-performance-testing` A/B against the Phase 1 baseline. Removing the null branches from hot
  systems should be neutral or slightly faster. Treat a regression as a finding.
- Delete this plan.

## Decided

- Modules are referenced by a static `ModuleId`.
- `Requires` does not imply `RunsAfter`.
- The pool-check audit is part of this plan.
- A replacement module replaces every component: it registers at least every component type the
  built-in did, as the same pool kind. Replacing only a module's systems or only its components is the
  Low Priority TODO item "Partial module replacement".
- Health never depends on a status effect module. Effects act through Health's API, as Poison already
  does. Burning locks a part out at ignition, and Health drops its burning check (Phase 5).
- Movement stops reading activation pools, because whatever queues an activation clears the step
  instead (Phase 4).
