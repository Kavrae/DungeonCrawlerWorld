# Dexterity-scaled action lock

(Pre-implementation. Replaces TODO.md's "Dexterity scaling ActionLockComponent.StandardLockFrames"
entry, which is deleted when Phase 2 lands.)

## Context

An entity's standard action lock is how long it waits after most actions and after every step. It is
its main speed stat. Today it is a stored per-entity number, and each race sets it by hand.

What exists today:
- `ActionLockComponent(StandardLockFrames, CurrentLockTotalFrames, UnlockedAtFrame)` is a Packed pool
  registered in `CoreModule`. The merge policy averages `StandardLockFrames`.
- Race builds set the number: Human 30, Goblin 54, Fairy 48, Ghost 48, and `TestDummyBlueprint` 48.
  `PlayerKit` doesn't override it, so the player uses Human's 30.
- `Engineer.Build` multiplies it by 0.9 when an `ActionLockComponent` already exists. Otherwise (no
  race) it merges one seeded with `ActionLockGate.StandardLockFrames` (60, 1s). `GoblinEngineer.Build`
  multiplies it by 0.9 again.
- It is read in two places:
  - `ActionLockGate.Lock(..., framesToWait: null)` falls back to `actionLock.StandardLockFrames`. Every
    `ActionTiming.ActionLockFrames` is null except `PowerAttackAction`'s windup, so this covers
    `ActionActivationSystem` (Immediate and Delayed), `ConsumableActivationSystem` (potion, scroll and
    wand) and `PlayerActionGate.Lock` (the map's Inspect).
  - `MovementSystem.TryMoveToNextMapPosition` reads it as the base value for
    `StatModifierTarget.MovementLockFrames` (`BodyPartEffectsSystem`'s leg/foot debuff), then applies
    the diagonal multiplier.
- Ability scores: every race grants all seven. Human rolls 2d5 (range 2-10), Goblin, Fairy and Ghost
  get a flat 5, and TestDummy gets 5. Totals are precomputed eagerly (`AbilityScoresComponent`), so a
  read is one packed lookup. Other stats already derive from a score on read through
  `AbilityScoreMath.Lerp` (range 1-300): `PotionCooldownEffects.ComputeDurationFrames` (Constitution)
  and `DodgeEffects.ComputeWindowFrames` (Dexterity). Each falls back to its Dex-1 / Con-1 value when
  the entity has no score.
- `StatModifierMath` totals are `(base + additiveSum) * (1 + multiplicativeSum)`: multiplicative
  magnitudes on one target **add**, they don't compound.

## Decisions (confirmed)

- **Dexterity replaces the racial lock; the two don't combine.** There is no stored per-entity
  standard lock any more. A race's speed will come from its Dex, once races get their own score
  ranges (TODO "Stats -- consumers": "Non-player races get their own baseline scores").
- **Curve:** linear, **30 frames at Dex total 1, down to 15 frames at Dex total 300**, so a
  maximum-Dex entity acts twice as often as a minimum-Dex one.
- **Speed that isn't Dexterity goes through a new `StatModifierTarget.ActionLockFrames`**, a
  multiplier on the Dex-derived lock. Engineer and Goblin Engineer each grant -10% through it, in place
  of their 0.9 multiplier. A Dex bonus would change size as more Dex-scaled effects are added (Dodge
  already is one); a lock modifier stays exactly what it says. The same target makes an entity
  **slower** with a positive magnitude, so no race is capped at the Dex-1 baseline of 30 frames.
- **No race gets a lock modifier, and no race's ability-score grant changes.** Human keeps its 2d5
  roll, and Goblin, Fairy, Ghost and TestDummy keep their flat 5. Every race runs at about 30 frames
  until the future race score-range work sets race speed through Dex.

## Design

### New modifier target: `StatModifierTarget.ActionLockFrames`

- Declared next to `MovementLockFrames`. Nothing depends on the enum's numeric values (every
  conversion is a `switch`), so inserting it there is safe.
- It scales the entity's **standard** lock only: every lock that uses the resolver below, movement
  included. Explicit durations aren't touched: `ActionTiming.ActionLockFrames` (Power Attack's windup),
  Paralysis and the spawn stagger stay fixed whoever they land on.
- `MovementLockFrames` keeps its meaning (movement only) and layers on top:
  `step lock = resolved standard lock × (1 + movement multipliers)`, then the diagonal multiplier.
- Polarity follows the effect on the entity: a negative magnitude (shorter lock) is a `Buff`, a
  positive one (longer lock) a `Debuff`.

### The resolver: `StandardActionLockFrames`

A static class in `Game/Modules/AbilityScores/`. It sits there rather than in Actions because both
Movement and Actions read it; it depends on ability scores and stat modifiers (AbilityScores already
depends on StatModifiers).

- `public const ushort MaximumLockFrames = 30;` -- the Dex-derived lock at Dex total 1, and the base
  for an entity with no Dexterity score.
- `public const ushort MinimumLockFrames = 15;` -- the Dex-derived lock at Dex total 300.
- `public static float ComputeFromDexterity(ushort dexterityTotal)`: `AbilityScoreMath.Lerp(total,
  MaximumLockFrames, MinimumLockFrames)`. It returns a float because rounding happens once, after
  modifiers.
- `public static ushort ResolveForEntity(PackedComponentPool<AbilityScoresComponent>? abilityScores, MultiComponentPool<StatModifierComponent>? statModifiers, int entityId)`:
  1. Dex total through `AbilityScoreQueries.TryGetComponent`, or `MaximumLockFrames` when the pool or
     the score is absent.
  2. `StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.ActionLockFrames, dexLock)`.
  3. **Rounded to nearest**, clamped to `[1, ushort.MaxValue]`. With truncation, every Dex above 1
     would drop straight to 29, a jump no Dex change caused. With rounding, one frame drops every
     ~20 Dex: Dex 1-10 gives 30, and 300 gives 15. The floor of 1 stops a stack of speed modifiers
     from reaching a zero-frame standard lock.
  Both pools are nullable, matching today's optional-pool convention; `PLAN-module-dependencies.md`
  will harden them along with every other lookup.

Derived on read, not cached on the component. The result now changes whenever the Dex total or an
`ActionLockFrames` modifier changes: the five ability-score write paths, modifier grant, modifier
expiry, and the build order of both. A cached value would need a sync hook at each of them. A read is
two lookups plus a lerp, and it only happens when an entity locks: once per action or step, never per
frame.

### `ActionLockComponent` loses `StandardLockFrames`

- The struct becomes `ActionLockComponent(ushort currentLockTotalFrames, uint unlockedAtFrame)`.
  `CoreModule`'s merge drops its averaging line, and `ToString` drops the field. The struct stays 8
  bytes because of padding, so this is about correctness, not memory.
- `ActionLockGate.StandardLockFrames` (60) is deleted. Its only non-test user was Engineer's no-race
  branch; `StandardActionLockFrames.MaximumLockFrames` replaces it everywhere, tests included.
- `ActionLockGate.Lock`'s `framesToWait` becomes a required `ushort`. With the null fallback gone,
  every lock states its own duration, and the gate stays free of ability scores (Core doesn't depend
  on AbilityScores or StatModifiers). Callers that relied on null now pass
  `timing.ActionLockFrames ?? StandardActionLockFrames.ResolveForEntity(_abilityScores, _statModifiers, entityId)`:
  - `ActionActivationSystem.TryActivateImmediate` / `TryActivateDelayed` (already hold both pools).
  - `ConsumableActivationSystem`'s potion, scroll and wand branches. It already holds `_abilityScores`;
    it gets the stat-modifier pool if it doesn't already hold one. One local
    `ResolveLockFrames(ActionTiming, entityId)` helper per system saves repeating the expression five
    times.
  - `PlayerActionGate` takes both pools as new constructor parameters (`ElementFactoryRegistry` passes
    them) and locks for the resolved frames.
- `MovementSystem` takes the ability-scores pool as a new optional constructor parameter; it already
  holds `statModifiers`. `MovementModule` fetches it with `GetOptionalPackedPool`, the same way it
  fetches `statModifiers`. It does **not** get a hard `Dependencies` entry: adding one would change the
  topological order that every system registers in, which is out of scope here. The step lock becomes
  `GetEffectiveValue(MovementLockFrames, ResolveForEntity(...))`, then the diagonal multiplier as
  today.
- Doc comments updated to point at the resolver: `ActionTiming` ("use the acting entity's own
  StandardLockFrames"), `StatModifierTarget.MovementLockFrames`, and `ActionLockComponent`'s "primary
  lever for speed" remark.

Deadlines are unaffected by tiers: the lock is a deadline, so no `framesPerVisit` scaling applies.
Skeletons don't lock, since only built entities act, so the `SkeletonAccessGuard` never sees these
reads on an unbuilt entity.

### Blueprints

- Human, Goblin, Fairy, Ghost and TestDummy still merge an `ActionLockComponent`, because the lock's
  presence is what makes an entity able to act, just without a frame count.
- **Engineer:** `Build` grants a permanent modifier through `StatModifierEffects.Apply`:
  `ActionLockFrames`, `Multiplicative`, `Buff`, `canModify: false`, magnitude `-0.1f`,
  `FrameDeadline.Never`, `ActionSource.Admin`. These are the arguments `BodyPartEffectsSystem` uses for
  its permanent modifiers. The no-race branch still merges a `MovementComponent` and an
  `ActionLockComponent`. It no longer checks whether a lock exists: the modifier is granted either
  way. Build order doesn't matter, since the resolver reads the modifier at lock time. The class
  summary ("act 10% more often") stays true.
- **GoblinEngineer:** its `Build` grants a second modifier of the same shape.
- **Stacking changes slightly.** Multiplicative magnitudes add, so Engineer + Goblin Engineer is
  1 - 0.1 - 0.1 = **0.8**, not today's 0.9 × 0.9 = 0.81. It is off by less than a frame at these lock
  lengths, and it matches how every other multiplicative modifier in the game stacks.
- `Apply` already refuses to build a part twice, so a second Engineer can't stack the modifier. The
  existing `EntityFactoryTests` cover that, reworked to count `ActionLockFrames` modifiers.

### Presentation: showing the modifier

`HealthWindow`'s buff/debuff list has named formats per target. `ActionLockFrames` gets one: a signed
percentage like `MaximumHealth`'s ("-10% Action Lock" / "+80% Action Lock"). It shows only if the
player gains one, through Admin "Apply > Engineer" today. `FormatMovementPenalty`'s literal-"x" format
is left as it is.

### Behaviour change to expect in play

Until races get their own score ranges, every creature has Dex 2-10, which gives
**30 frames**:
- Player: unchanged. Human was already 30.
- Goblins: 54 → 30, so they move and attack almost twice as often. Fairy and Ghost: 48 → 30.
- Goblin Engineer: 43 → 24.

This fits the direction of TODO's "much more frequent enemy movement", but it is a visible difficulty
change, and an accepted one: faster creatures are handled by the future race score-range work.

## Phases

### Phase 1 -- target, resolver, component change, blueprints, tests

1. Add `StatModifierTarget.ActionLockFrames` and `StandardActionLockFrames`, with unit tests:
   - Dex 1 → 30, Dex 300 → 15, Dex 0 and above 300 clamp, Dex 10 → 30 (rounding).
   - Missing pool or score → 30.
   - A -10% modifier on Dex 1 → 27; +80% → 54.
   - Two -10% modifiers → 24 (they add).
   - A -100% modifier floors at 1.
2. Remove `StandardLockFrames` from `ActionLockComponent`, its `CoreModule` merge and `ToString`;
   delete `ActionLockGate.StandardLockFrames`; make `Lock`'s frames required.
3. Update the callers: `ActionActivationSystem`, `ConsumableActivationSystem`, `MovementSystem` (+
   `MovementModule` wiring), `PlayerActionGate` (+ `ElementFactoryRegistry`).
4. Blueprints: drop the frame counts from the five race/dummy builds; switch Engineer and
   GoblinEngineer to the `ActionLockFrames` modifier.
5. `HealthWindow`: the `ActionLockFrames` format.
6. Tests:
   - About 30 test files construct `ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, ...)`;
     drop the argument. This is mechanical.
   - `ActionLockGateTests.Lock_NoFramesGiven_UsesTheEntitysOwnStandardLockFrames` is replaced by
     caller-level tests:
     - An Immediate action with no `ActionLockFrames` locks for the caster's resolved frames, and an
       `ActionLockFrames` modifier changes that.
     - An explicit `ActionLockFrames` still wins, and ignores the modifier.
     - A move's lock follows Dex and `ActionLockFrames`, with `MovementLockFrames` layered on top.
   - `BlueprintTests` (Engineer with and without a race, Goblin + Engineer), `SpawnRecordRebuilderTests`
     (Goblin + Engineer) and `EntityFactoryTests` (Engineer applied once) assert on the resolved lock
     and the modifier count instead of the removed field.
   - A `HealthWindow` formatting test next to the existing per-target ones.
7. `dotnet build` and `dotnet test`.

**Stop for in-game testing:**
- The player's step and attack rhythm is unchanged, and goblins are noticeably quicker.
- Admin "Apply > Engineer" on the player shortens their step to 27 frames and lists "-10% Action
  Lock" in the Health window.
- Admin inspection no longer shows `StandardLockFrames`.

### Phase 2 -- verification and bookkeeping

1. Headless A/B benchmark (`phase-performance-testing`) against the pre-change build, to confirm
   `MovementSystem` doesn't regress from the extra lookups per move. The expectation is noise-level;
   goblins moving more often shows up as more moves, not as a higher cost per move.
2. TODO.md:
   - Delete this entry.
   - "Faster player movement, much more frequent enemy movement": replace the "Human's
     `StandardLockFrames` (30)" and "Player 20" notes. Player speed is now Dex or `ActionLockFrames`;
     `MovementLockFrames` is still the movement-only lever.
   - "Stats -- consumers": remove the Dexterity->`StandardLockFrames` item; add to "Non-player races
     get their own baseline scores" that race Dex ranges now set race speed, with `ActionLockFrames`
     for anything outside the Dex curve.
3. IMPLEMENTATION-NOTES.md: a short "Dexterity action lock" section covering:
   - The curve and its endpoints.
   - `ActionLockFrames` versus `MovementLockFrames` versus explicit durations.
   - Derived on read rather than cached (and why).
   - Required `Lock` frames.
   - Multiplicative modifiers adding rather than compounding.
