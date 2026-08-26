# Targeted body part damage: Magic Missile -> Head, lava -> Feet-or-bottommost

(Design plan, saved for implementation per this repo's convention -- see `PLAN-body-parts.md` for
the Complex-health machinery this builds on. Item 1 of the current body-parts follow-up work, per
TODO.md's "Targeted body part damage and multi-part effects" entry. The user's own framing: prove
the mechanism with two concrete cases, and design a general "mark/determine body parts in a
specific position" concept reusable by item 3 (lava's Burning application) too.)

## Context

Today, any damage source resolving against a Complex entity picks one body part uniformly at
random -- `ComplexHealthDamage.Apply` always calls `BodyPartSelection.PickRandom`, with no way for
a caller (a spell, a hazard) to express a preference. This plan adds that, proven by two concrete
cases:
- **Magic Missile** targets the Head specifically.
- **Lava's contact damage** targets a Foot specifically -- but no current race (Goblin, Human) has
  a `BodyPartType.Foot` part, only `Leg`. So this case *requires* a real fallback: "whatever touches
  the ground" when no Foot exists, which the user's own phrasing already anticipates.

The user also explicitly flagged that whatever "position" concept answers "what's the bottommost
part" needs to be reusable for item 3 (lava's Burning application uses the same targeting rule) --
so this plan builds one shared, general mechanism, not two bespoke hacks.

## Design

### A general "position" concept: `VerticalPosition` on `BodyPartComponent`/`BodyPartTemplate`

A new `byte VerticalPosition` field -- **higher value = higher up the body** (a Head sits at the
top, a Foot at the bottom; "topmost" = the max value among an entity's own parts, "bottommost" =
the min). A plain numeric field, not a named-bucket enum (`Top`/`Middle`/`Bottom`), because:
- It supports arbitrary anatomy without predicting every bucket name up front -- a future
  multi-segment or non-humanoid race (per `TODO.md`'s own Fairy-wings/cat-four-legs precedent) can
  slot its own parts in at whatever relative heights make sense, without a bucket boundary forcing
  an awkward choice.
- "Topmost"/"bottommost" only ever need a min/max comparison among one entity's *own* parts --
  there's no need to compare across entities or against a fixed universal scale, so the actual
  numeric values only ever need to be internally consistent per race, not globally meaningful.

Race blueprints assign this per part when building their `BodyPartTemplate[]` list -- e.g. Goblin/
Human's Head highest, Torso/Arms next, Legs lowest. Scoped to one axis (vertical) only, per the
user's own two concrete asks -- a future "front/back" or "left/right" axis would need its own
separate field, not a premature multi-axis generalization now.

Both `BodyPartTemplate` (`Game/Modules/Health/BodyPartTemplate.cs`) and `BodyPartComponent`
(`Game/Modules/Health/Components/BodyPartComponent.cs`) gain this new `byte VerticalPosition` field.
`ComplexHealthEffects.GrantBodyParts` (`Game/Modules/Health/ComplexHealthEffects.cs`) threads
`part.VerticalPosition` through into the `BodyPartComponent` it constructs, same as every other
template field already does.

### `BodyPartType` gains `Foot`

Minimal addition -- just what this plan needs. `Hand` (also eventually planned per `TODO.md`'s
BodyPartType categorization item) is not added here since nothing yet consumes it.

### `BodyPartSelection` gains three new primitives, plus one composed one

```csharp
public static int PickTopmost(MultiComponentPool<BodyPartComponent> bodyParts, int entityId);
public static int PickBottommost(MultiComponentPool<BodyPartComponent> bodyParts, int entityId);
public static int PickByType(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, BodyPartType type);
```
Each a simple linear walk (mirrors `PickLowestPercentage`'s existing shape), returning -1 if
`entityId` owns no parts (`PickByType` also returns -1 if none match the requested type -- not an
error, the expected "no Foot on this race" case). `PickTopmost`/`PickBottommost` share a small
private helper parameterized by comparison direction, rather than duplicating the walk twice.

```csharp
public enum BodyPartFallback { Random, Topmost, Bottommost }

public readonly record struct BodyPartTargetRule(BodyPartType PreferredType, BodyPartFallback Fallback);

public static int PickByTypeWithFallback(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, BodyPartTargetRule rule, MathUtility mathUtility)
{
    var typeMatch = PickByType(bodyParts, entityId, rule.PreferredType);
    if (typeMatch != -1) return typeMatch;

    return rule.Fallback switch
    {
        BodyPartFallback.Topmost => PickTopmost(bodyParts, entityId),
        BodyPartFallback.Bottommost => PickBottommost(bodyParts, entityId),
        _ => PickRandom(bodyParts, entityId, mathUtility),
    };
}
```
`BodyPartTargetRule` is the one shared, reusable "how do I pick a target part" type -- deliberately
public and general enough for item 3's Burning-application code to call the exact same
`PickByTypeWithFallback` once that lands, not something Health-module-private.

### Threading a `BodyPartTargetRule?` through the damage chokepoint

`HealthDamage.Apply`/`ComplexHealthDamage.Apply` both gain one new optional trailing parameter,
`BodyPartTargetRule? targetRule = null` (every existing call site keeps compiling unchanged, same
"optional, defaults to today's behavior" pattern every other trailing param on this method already
uses). `ComplexHealthDamage.Apply` dispatches: `targetRule is { } rule ? BodyPartSelection.
PickByTypeWithFallback(bodyParts, entityId, rule, mathUtility) : BodyPartSelection.PickRandom(...)`
-- `null` preserves today's pure-random behavior exactly.

### Magic Missile -> Head

`DirectDamage` gains one new optional field: `BodyPartType? TargetBodyPartType = null`. In `Apply`,
builds `new BodyPartTargetRule(type, BodyPartFallback.Random)` when set (falls back to random if
the type genuinely isn't present -- no current race lacks a Head, but a spell shouldn't just fizzle
against a hypothetical future one that does) and passes it into `HealthDamage.Apply`.
`MagicMissileAction.Build` sets `TargetBodyPartType: BodyPartType.Head` on its `DirectDamage` entry.
No other `DirectDamage` user is affected -- the field defaults to `null`, today's random behavior.

### Lava -> Foot, falling back to bottommost

`DamageOnContactComponent` (the hazard definition) gains one new optional field: `BodyPartType?
PreferredTargetType = null`. `ContactDamageSystem`'s two `HealthDamage.Apply` call sites build
`new BodyPartTargetRule(type, BodyPartFallback.Bottommost)` when the hazard has one set -- fallback
is hardcoded to `Bottommost` at the `ContactDamageSystem` level (not a second per-hazard knob),
since "damages whatever touches the ground" is true of ground-contact hazards generically, not a
choice each individual hazard should need to restate. Lava's own definition sets
`PreferredTargetType: BodyPartType.Foot`.

## Addendum: lava generalized from "prefer Foot" to plain bottom-up

Landed as described below, then simplified by explicit follow-up request: lava no longer names
`BodyPartType.Foot` at all -- `DamageOnContactComponent.PreferredTargetType` stays `null` for Lava,
and `BodyPartTargetRule.PreferredType` became nullable (`BodyPartType?`) so a rule can mean "no type
preference, go straight to Fallback." `ContactDamageSystem` now *always* builds a rule with
`BodyPartFallback.Bottommost` for every hazard (previously only when `PreferredTargetType` was set),
so lava's damage is generic "whatever's lowest" rather than "Foot, or bottommost if no Foot." A
future hazard that *does* want to prefer a specific type can still set `PreferredTargetType` --
the mechanism is unchanged, only Lava's own choice not to use it.

## Decisions (confirmed)

- **Goblin and Human both split `Leg`->`Leg`+`Foot` and `Arm`->`Arm`+`Hand`** -- not just for this
  plan's own Foot-fallback proof, but because Equipment (`TODO.md`'s own Equipment item) will
  eventually key slot counts off active typed parts (a ring per Hand, a boot per Foot), so both
  splits are worth landing together now rather than Foot alone today and Hand later as a second,
  separate rebalance. `BodyPartType` gains `Hand` alongside `Foot`.
- **HP redistribution**, same race totals as before (Goblin 200, Human 250), round numbers, Arm/Leg
  keeping the larger share of their own original HP:
  - Goblin: Head 30 (Vital), Torso 60 (Vital), Arm 15 x2, Hand 5 x2, Leg 25 x2, Foot 10 x2 (sums to
    30+60+30+10+50+20 = 200).
  - Human: Head 40 (Vital), Torso 80 (Vital), Arm 20 x2, Hand 5 x2, Leg 30 x2, Foot 10 x2 (sums to
    40+80+40+10+60+20 = 250).
- **`VerticalPosition` scheme** (higher = higher up): Head 5, Torso 4, Arm 3, Hand 2, Leg 1, Foot 0 --
  same ordinal scheme for both races, since both are bipedal humanoids with the same relative part
  ordering.
- Both races' 6-part lists become 10-part lists (Head, Torso, Left/Right Arm, Left/Right Hand,
  Left/Right Leg, Left/Right Foot).

## Test plan

- `Tests/Modules/Health/BodyPartSelectionTests.cs`: `PickTopmost`/`PickBottommost` pick the correct
  part across mixed `VerticalPosition` values, return -1 for no parts; `PickByType` finds a match or
  returns -1; `PickByTypeWithFallback` prefers an exact type match, falls back correctly per
  `BodyPartFallback` value when no match exists, and (for `Random` fallback) still returns a valid
  index across seeded rolls.
- `Tests/Modules/Health/ComplexHealthDamageTests.cs`: a hit with a `BodyPartTargetRule` set lands on
  the preferred type when present; falls back correctly when absent.
- `Tests/Modules/Actions/ActionEffectTests.cs`: `DirectDamage` with `TargetBodyPartType` set lands on
  that type against a Complex target.
- `Tests/Modules/ContactDamage/ContactDamageSystemTests.cs`: a hazard with `PreferredTargetType` set
  lands on that type when present, bottommost when absent.
- Full `dotnet build`/`dotnet test`, matching the existing pre-existing-failure baseline.

## Execution phases

1. Implementation (single phase -- small enough not to split, no in-game-visible change until a
   spell/hazard actually sets a preference, which Magic Missile/Lava both do as part of this same
   pass). Verify: `dotnet build`, `dotnet test`, then manual in-game test -- cast Magic Missile at a
   Complex entity (Goblin/Human) repeatedly, confirm it always lands on Head (watch the Head's own
   bar in the Health window/hover popup deplete, others untouched); stand in lava, confirm the same
   for whichever part is now the target (Foot if that question above says yes, otherwise confirm
   it's consistently the bottommost part -- Leg today).
