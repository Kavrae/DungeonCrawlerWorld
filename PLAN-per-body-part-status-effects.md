# Per-body-part status effects: Burning localizes to a body part, Poison always hits Internal

(Design plan, saved for implementation per this repo's convention -- see `PLAN-body-parts.md` for
the Complex-health machinery this builds on, and `PLAN-targeted-body-part-damage.md` for the
`BodyPartTargetRule` mechanism this reuses. Item 3 of the current body-parts follow-up work, per
`TODO.md`'s "Per-body-part vs whole-entity status effects" entry. Substantially revised from its
first draft after a round of concrete follow-up decisions -- this version supersedes that one.)

## Context

Status effects are entity-scoped only today: `StatusEffectStack`/`BurningTimerComponent`/
`PoisonTimerComponent`/`ParalysisTimerComponent` are all keyed by entity id alone. There's no way
for "this specific leg is burning" to exist as real state. This plan gives Burning a real,
independent per-body-part existence for Complex entities -- **without removing its existing
entity-scoped behavior**, which stays fully valid and continues to be used -- and gives Poison a
fixed, always-the-same-part target (a new `Internal` body part) rather than a flexible one.

## Naming: "Entity-scoped" vs "Body-part-scoped," not "Simple/Complex"

Explicitly answering the naming question: **"Simple/Complex" describes an entity's own health
architecture (does it have one pool or several parts) -- it's the wrong axis for a status effect's
own application, because a single Complex entity can have *both* an entity-scoped Burning stack and
a body-part-scoped one active at the same time** (e.g. a general fire spell burning the whole
entity, while a specific foot is *also* independently on fire from standing in lava). The real
distinction is per-application, not per-entity-type. This plan uses **"entity-scoped"** and
**"body-part-scoped"** as the vocabulary, and names new types with the existing `BodyPart` prefix
this codebase already uses (`BodyPartComponent`, `BodyPartSelection`, `BodyPartTargetRule`) rather
than inventing a new qualifier:
- Today's `BurningSystem`/`BurningTimerComponent`/`StatusEffectStack` are **unchanged, not renamed**
  -- they're still exactly what they were, entity-scoped, and (per the decision below) that
  continues to be a real, independently useful mode, not something superseded. This mirrors
  `PLAN-body-parts.md`'s own precedent: `HealthDamage`/`HealthHeal` kept their plain names even
  after a Complex path landed, because they remained genuinely still-used dispatch points, not
  legacy stand-ins.
- New: **`BodyPartBurningTimerComponent`** (`MultiComponentPool`, one instance per currently-burning
  part) and **`BodyPartStatusEffectStack`** (`MultiComponentPool`, mirrors `StatusEffectStack` plus
  a `PartId`). No new system name needed beyond `BodyPartBurningSystem`.

## Investigated: two blocking technical facts (unchanged from the first draft)

**1. `MultiComponentPool` dense indices are not stable identifiers** -- `RemoveDenseIndexInternal`
swaps the last entry into a freed slot on every removal, silently relocating a *different* entity's
still-live component. A per-part store needs a real stable identity, not a dense index. Fixed by a
new `BodyPartComponent.PartId` (`byte`, assigned once by `ComplexHealthEffects.GrantBodyParts` in
template order, permanent for the entity's lifetime).

**2. `StatusEffectAuraSystem`'s own contract (`IStatusEffectAuraApplier.ApplyStack(componentManager,
entityId, source)`) is already sufficient** -- it doesn't need to change. A part-aware applier
implementation dispatches internally; the registry/grid/exposure machinery around it stays entirely
untouched.

## Design

### `BodyPartComponent.PartId` and `BodyPartType.Internal`

`PartId` as described above. A new `BodyPartType.Internal` value, and **every Complex race grants an
`Internal` part, `IsVital: true`** -- representing the entity's internal organs/bloodstream, the
thing Poison always travels to regardless of where it entered. `VerticalPosition` for `Internal`
matches its race's own `Torso` value (co-located, not independently rankable on the vertical axis).

**Rebalance** (HP taken from each race's own `Torso`, same race totals preserved):
- Goblin (200 total): `Torso` 60 -> 50, new `Internal` 10 (Vital). Full list: Head 30, Torso 50, Arm
  15x2, Hand 5x2, Leg 25x2, Foot 10x2, Internal 10 -- 30+50+30+10+50+20+10 = 200.
- Human (250 total): `Torso` 80 -> 65, new `Internal` 15 (Vital). Full list: Head 40, Torso 65, Arm
  20x2, Hand 5x2, Leg 30x2, Foot 10x2, Internal 15 -- 40+65+40+10+60+20+15 = 250.

An entity now has *three* ways to die instead of two -- Head, Torso, or Internal reaching 0 (any
Vital part at 0 is already the existing death rule, unchanged; Internal just adds a third one).

### Poison always targets Internal -- no new storage needed at all

This turned out simpler than Burning once `Internal` exists as a guaranteed part: Poison doesn't
need body-part-*scoped* storage the way Burning does (its target never varies, never coexists across
multiple different parts) -- it just needs its existing entity-scoped damage tick to *aim* at
Internal, reusing `PLAN-targeted-body-part-damage.md`'s mechanism directly, the same way Magic
Missile aims at Head. One-line change: `PoisonSystem.Tick`'s existing `HealthDamage.Apply(...)` call
gains `targetRule: new BodyPartTargetRule(BodyPartType.Internal, BodyPartFallback.Random)` (Random
fallback purely defensive, in case a future Complex race is ever authored without granting
`Internal` -- every race this plan touches always grants one, so it should never actually fire).
`PoisonTimerComponent`/`StatusEffectStack` -- entirely unchanged.

### Burning: entity-scoped and body-part-scoped coexist as two independent mechanisms

**Entity-scoped (unchanged)**: today's `BurningTimerComponent`/`BurningSystem`/`StatusEffectStack`
path, exactly as it works today -- including for a Complex entity. `HealthDamage.Apply`'s existing
Simple/Complex dispatch already sends a Complex entity's entity-scoped Burning damage to a random
body part each tick (no `targetRule` -- `BodyPartSelection.PickRandom`), which is correct and
unchanged; this plan doesn't touch `BurningSystem`/`BurningTimerComponent` at all.

**Body-part-scoped (new)**: `BodyPartBurningTimerComponent(byte partId, byte stackCount, ushort
framesUntilNextTick)` + `BodyPartStatusEffectStack`, both `MultiComponentPool`-based so one entity
can have several parts independently burning at once, each with its own countdown. New
`BodyPartBurningSystem` ticks this pool the same `CountdownTicker`-shaped decrement-or-fire loop
`BurningSystem` already uses, but walking a `MultiComponentPool` (several due entries per entity
possible in one visit, not just one). **Damages the exact part its timer names** -- via a new
`BodyPartSelection.FindByPartId(bodyParts, entityId, partId)` lookup (mirrors `PickByType`'s linear-
walk shape, matching `PartId` instead of `Type`), not a fresh `BodyPartTargetRule` resolution each
tick -- the confirmed answer to the first draft's open question.

### Which mode does a grant use? Reusing `DamageOnContactComponent` as the signal

`BurningModule.Configure`'s registered `TimerBasedAuraApplier<BurningTimerComponent>` is replaced by
a new applier that dispatches per grant: if `source` traces to an entity with a
`DamageOnContactComponent` (i.e. a ground hazard -- lava today), grant a
`BodyPartBurningTimerComponent`+`BodyPartStatusEffectStack` on a part resolved via
`BodyPartTargetRule(hazard.PreferredTargetType, BodyPartFallback.Bottommost)` -- the literal same
rule lava's own contact damage already uses (this is the actual "same rules as plan 1" reuse).
Otherwise (no hazard behind the source -- a future non-hazard Burning grant, e.g. a fire spell),
grant the existing entity-scoped `BurningTimerComponent` exactly as today. No new field needed on
`StatusEffectAuraSourceComponent`/anywhere else -- the hazard's own existing `DamageOnContactComponent`
is reused as the discriminator, not a new one invented. `StatusEffectGrant` (the direct spell/scroll
path) needs no change -- it already dispatches purely through `IStatusEffectAuraApplier`, so it
automatically inherits whichever mode the registered applier picks.

### Shared per-part damage-application helper (avoids duplicating the lockout-reset logic)

`ComplexHealthDamage.Apply`'s per-part clamp-and-disable logic (clamp to effective max, set
`IsDisabled`+reset `RegenLockoutFramesRemaining` to a fresh 10 seconds the instant `CurrentHealth`
hits 0) needs to be reused verbatim by `BodyPartBurningSystem`'s own tick, not re-implemented --
extracted into a small shared static helper (e.g. `BodyPartDamageEffects.ApplyToPart(bodyParts,
denseIndex, statModifiers, entityId, amount) -> bool disabledThisHit`), called by both
`ComplexHealthDamage.Apply` (after it resolves *which* part via `BodyPartSelection`) and
`BodyPartBurningSystem.Tick` (which already knows its part via `FindByPartId`). This is what makes
the regen-lockout behavior below correct automatically, for both damage sources, without a second
place to keep in sync.

**Confirms/extends the existing lockout reset**: the clamp logic already re-sets
`RegenLockoutFramesRemaining` to a fresh 10 seconds on *every* hit that leaves `CurrentHealth` at 0
(not only the first transition into 0 -- the check is `if (CurrentHealth == 0)`, re-evaluated
every call, so a second hit against an already-0 part already re-arms the lockout today). Routing
`BodyPartBurningSystem`'s DoT tick through the same shared helper means a burning, already-disabled
part keeps its lockout freshly extended every burn tick too, for free, with no separate logic.

### Regen must also skip a currently-burning part outright, independent of the lockout timer

**A body part with an active `BodyPartBurningTimerComponent` entry must never be selected for
passive regen, even if its numeric lockout has already counted down to 0** -- "on fire" is its own
exclusion condition, not just a longer lockout. `BodyPartSelection.PickLowestPercentage` gains a new
optional parameter, `MultiComponentPool<BodyPartBurningTimerComponent>? bodyPartBurningTimers =
null`, and skips any candidate part with a matching `PartId` entry in that pool (a short linear walk
of the entity's own -- typically very small -- burning-parts chain per candidate, checked the same
way the existing lockout check already short-circuits). `ComplexHealthRegenSystem` threads this
through as a new optional constructor dependency, same optional-pool pattern every other Health
system already uses.

## Confirmed: `PickRandom` (and friends) now prefer an alive part

`BodyPartSelection.PickRandom` didn't actually skip disabled parts before this plan (it picked any
part uniformly, including an already-0 one -- harmless, since the clamp keeps it at 0, but not
actually excluded). Fixed: `PickRandom` now counts non-disabled parts first; if any exist, picks
uniformly among only those; if *every* part is disabled (the entity should already be dead via the
Vital-part-at-0 rule, but this is the defensive fallback for the same-frame edge case where death
processing hasn't run yet), falls back to today's original uniform-among-everyone behavior rather
than returning -1. `PickByTypeWithFallback`'s own `Random` fallback branch calls `PickRandom`
internally, so it inherits this for free -- no separate change there.

**Extended for consistency, not separately requested**: `PickByType` and `PickTopmost`/
`PickBottommost` get the same "prefer alive, fall back to anyone if all disabled" treatment. Without
this, "prefer Foot" would keep re-selecting the *same* already-destroyed Foot forever once it hits 0
if a second, still-alive Foot exists, and `Bottommost` would keep aiming at an already-dead lowest
part instead of handing off to the next-lowest alive one -- both read as the same underlying bug
`PickRandom`'s own fix addresses, just via a different selection path. Flagging since it wasn't
explicitly asked for the non-`PickRandom` methods, but leaving them inconsistent felt like a
half-fix.

## Test plan

- `Tests/Modules/Health/BodyPartSelectionTests.cs`: `FindByPartId` (match/no-match); `PickLowestPercentage` skips a part with an active `BodyPartBurningTimerComponent` entry even when its own lockout is 0.
- `Tests/Modules/Health/BodyPartDamageEffectsTests.cs` (new, or folded into an existing file): the shared clamp-and-disable helper resets the lockout on every 0-landing hit, not just the first.
- `Tests/Modules/Burning/BodyPartBurningSystemTests.cs` (new): a body-part-scoped burn ticks, damages only its own named part (via `FindByPartId`, not re-resolved), expires correctly; two different parts burn independently and concurrently.
- Applier-dispatch test: granting Burning via a source with `DamageOnContactComponent.PreferredTargetType` set lands body-part-scoped on that type (falling back to Bottommost); via a source with no hazard at all, lands entity-scoped (today's `BurningTimerComponent`, unchanged).
- `Tests/Modules/Poison/PoisonSystemTests.cs`: a Complex target's Poison damage always lands on `Internal`.
- `Tests/Blueprints/GoblinTests.cs`/`HumanTests.cs`/`BlueprintTests.cs`: updated part lists/sums (11 parts, new totals).
- `Tests/Presentation/HealthWindowTests.cs`: a body-part-scoped burn shows under its own part's section (glyph + name + remaining duration -- same format the entity-scoped section already uses); the entity-scoped section still shows Poison/Paralysis/entity-scoped Burning.
- Full `dotnet build`/`dotnet test`, matching the existing pre-existing-failure baseline.

## Execution phases (stop for manual in-game testing after each)

1. **Data model, no live behavior change.** `PartId`, `BodyPartType.Internal` + Goblin/Human
   rebalance, `BodyPartBurningTimerComponent`/`BodyPartStatusEffectStack`,
   `BodyPartSelection.FindByPartId`, the shared `BodyPartDamageEffects` helper (wired into
   `ComplexHealthDamage.Apply`, behavior-preserving). Verify: build/test only.
2. **Poison -> Internal, and the dispatching Burning applier + `BodyPartBurningSystem`.** Verify:
   build/test, then manual in-game test -- poison a Complex entity, confirm damage always lands on
   Internal (Health window); stand a Complex entity in lava, confirm Burning now localizes to one
   part rather than scattering; confirm a non-hazard Burning source (if one exists to test with)
   still applies entity-scoped, random-part-per-tick, unchanged.
3. **Regen exclusion for burning parts + `HealthWindow` per-part status display.** Verify:
   build/test, then manual in-game test -- confirm a burning part never regens while on fire even
   after its lockout would otherwise have expired, confirm it resumes regenerating once the fire
   goes out; confirm the Health window shows the correct part's own Burning line (glyph + name +
   duration) and the top-level section still shows Poison/Paralysis correctly.
