# Body Parts: Simple/Complex health split, per-part HP, and regen overhaul

(Design plan, saved for implementation per this repo's design-doc convention -- see
`PLAN-action-effect-activator.md`/`PLAN-corpse-inventory-looting.md` for the same shape used
pre-implementation. Addresses `TODO.md`'s "Body parts" item under Game, Low Priority.)

## Context

Every entity with health today shares one pool: `HealthComponent`/`HealthRegenSystem`/
`HealthDamage`/`HealthHeal` (`Game/Modules/Health/`), a `PackedComponentPool<HealthComponent>`
current/maximum float pair, regenerated per-tick by Constitution and consumed by every damage/heal
call site (melee, `ActionEffectResolver`, `ContactDamageSystem`, potions, DoT ticks). `TODO.md`'s
"Body parts" item asks for a second, opt-in path -- halfway between Fallout's per-limb HP and
Dwarf Fortress's full simulation -- where a complex entity (a crawler, a boss) instead tracks
several independent body parts, each with its own HP, Vital flag, and disabled state, while every
existing entity keeps today's single-pool behavior unchanged.

This plan makes that real: a rename of the existing pool/system to make the "Simple" path an
explicit choice rather than an unqualified default, a new `BodyPartComponent`
(`MultiComponentPool`, several instances per entity) for the "Complex" path, a shared
`HealthQueries` query so every consumer of "current/max HP" works against either path without
knowing which one it's looking at, and new Complex-path counterparts for damage, heal, and regen --
`ComplexHealthDamage` mirrors `HealthDamage`'s shape closely (one part takes the hit); a healing
potion/scroll's `ComplexHealthHeal` deliberately does not mirror `HealthHeal`'s single-target shape
-- it heals every part at once, by the source's stated percentage, per direct instruction (see the
heal design below).

Deliberately narrow: no race gets a genuinely tuned body-part list in this pass (see Phase 3
below for the one proof-case race), no UI shows per-part detail yet (see the `HealthWindow`
follow-up), no targeting UI lets an attack choose which part to hit (random, per `TODO.md`'s own
"attacks hit a random body part (for now)" note), and `BodyPartType` lands as a real field with
only the minimal member set this plan itself needs (Head/Torso/Arm/Leg -- exact final set is the
`BodyPartType categorization` follow-up's job, not this plan's).

## Design

### Rename: `HealthComponent`/`HealthRegenSystem` -> `SimpleHealthComponent`/`SimpleHealthRegenSystem`

Mechanical, no behavior change. `HealthComponent.cs` -> `SimpleHealthComponent.cs`,
`HealthRegenSystem.cs` -> `SimpleHealthRegenSystem.cs`, every call site across
`Game/Modules/Health/`, every race blueprint (`PlayerBlueprint`/`Goblin`/`Fairy`/`Ghost`), every
consumer (`HealthDamage`/`HealthHeal`/`HealthBarElement`/`PlayerHealthBarContent`/
`MapWindow.DrawHealthBar`/`InspectionWindowContent`), and every test (`HealthComponentTests`,
`HealthRegenSystemTests`, `HealthModuleTests`, `HealthDamageTests`, `HealthHealTests`,
`Mods.TestFixtures/ReplacementHealthModule.cs`). `HealthDamage`/`HealthHeal`/`HealthModule`
themselves keep their names -- `HealthDamage` becomes a Simple/Complex-dispatching facade (see
below) and `HealthModule` now registers both paths, so "Health" is still the right name for
either; `HealthHeal` stays a plain Simple-only helper under its existing name too, just no longer
the only thing "healing" can mean once `DirectHeal` gains its own Complex path directly (see
below) -- only the single-pool component/system that used to be the only implementation need the
qualifier now that a second one exists.

### New data model (`Game/Modules/Health/`)

**`BodyPartType`** (new enum, `Components/BodyPartType.cs`) -- minimal set for this pass:
`Head`, `Torso`, `Arm`, `Leg`. Each race's own body-part list (Phase 3) picks freely from these;
nothing in this plan assumes a fixed count or a specific subset per race (see the design note
already in `TODO.md`: a Fairy's Wing parts, a cat-shaped race's zero Arm parts). Expanding this
enum (`Wing`, `Hand`, `Foot`, ...) is additive and doesn't touch this plan's own systems -- the
BodyPartType followup is what actually wires gameplay *behavior* per type, not this plan.

**`BodyPartComponent`** (new, `Components/BodyPartComponent.cs`):
```csharp
public struct BodyPartComponent(string name, BodyPartType type, float currentHealth, float maximumHealth, bool isVital)
{
    public string Name { get; set; } = name;
    public BodyPartType Type { get; set; } = type;
    public float CurrentHealth { get; set; } = currentHealth;
    public float MaximumHealth { get; set; } = maximumHealth;
    public bool IsVital { get; set; } = isVital;
    public bool IsDisabled { get; set; }

    /// <summary>Frames remaining before ComplexHealthRegenSystem may select this part again after
    /// it was disabled -- the yo-yo-prevention lockout. Decremented directly by
    /// ComplexHealthRegenSystem's own per-visit walk, not CountdownTicker: CountdownTicker is
    /// PackedComponentPool-only (see StatModifierExpirySystem's own doc comment for the same
    /// "not reusable here, this pool is Multi" reasoning), and a per-part field updated in place
    /// via UpdateByDenseIndex needs no separate ticking system regardless.</summary>
    public ushort RegenLockoutFramesRemaining { get; set; }
}
```
Registered via `componentManager.RegisterMultiPool<BodyPartComponent>()` -- no merge action (Multi
pools never merge; `Add` always appends a new instance, which is exactly right here: two sources
granting a Goblin's own "Arm" part would be a bug to catch via testing, not silently average
together the way `SimpleHealthComponent`'s `MaximumHealth`/`CurrentHealth` merge today).

An entity's health kind is never a separate marker component -- it's whichever pool actually has
entries for that `entityId`: `simpleHealth.Has(entityId)` or `bodyParts.Has(entityId)`. Mirrors
`NonBlockingComponent.Kind` folding its own exemption-kind flag into the one component that grants
the exemption rather than a second component that could drift out of sync (CLAUDE.md's World &
Map note). An entity is never expected to carry both, and nothing in this plan enforces that
mutual exclusion at the type level -- a blueprint bug that grants both is a testing/authoring
error, not a runtime-guarded state.

### `BodyPartSelection` (new static class, `Game/Modules/Health/BodyPartSelection.cs`)

Two selection rules, shared by every system/helper below rather than each re-walking the chain its
own way:

```csharp
public static class BodyPartSelection
{
    /// <summary>Picks one of entityId's body parts uniformly at random -- the "attacks hit a
    /// random body part (for now)" placeholder TODO.md's Body parts item names, until the
    /// Targeted body part damage follow-up adds real selection rules. Two-pass walk (count, then
    /// walk to the Nth) since MultiComponentPool exposes no direct "the Nth instance for this
    /// entity" accessor. Returns -1 if entityId owns no BodyPartComponent at all.</summary>
    public static int PickRandom(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, MathUtility mathUtility)
    {
        var count = bodyParts.CountForEntity(entityId);
        if (count == 0) return -1;

        var targetOrdinal = mathUtility.Next(0, count);
        var ordinal = 0;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex), ordinal++)
        {
            if (ordinal == targetOrdinal) return denseIndex;
        }

        return -1; // unreachable given count > 0, guarded for completeness
    }

    /// <summary>Picks entityId's body part with the lowest CurrentHealth/MaximumHealth fraction,
    /// skipping any part still inside its post-disable RegenLockoutFramesRemaining window --
    /// the yo-yo-prevention case that lockout exists for. Its only caller is
    /// ComplexHealthRegenSystem's own passive-regen tick; an active heal (potion/scroll) never
    /// goes through this method at all -- see ComplexHealthHeal.ApplyFractionToAllParts below,
    /// which heals every part at once rather than picking one, so there is no "should this ignore
    /// the lockout" question for the heal path to begin with. Returns -1 if entityId owns no
    /// BodyPartComponent, or every part is either at full health or currently locked out.</summary>
    public static int PickLowestPercentage(MultiComponentPool<BodyPartComponent> bodyParts, int entityId)
    {
        var bestDenseIndex = -1;
        var bestFraction = float.MaxValue;

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            if (part.RegenLockoutFramesRemaining > 0) continue;

            var fraction = part.MaximumHealth > 0 ? part.CurrentHealth / part.MaximumHealth : 1f;
            if (fraction >= 1f) continue; // already full, nothing to gain by selecting it

            if (fraction < bestFraction)
            {
                bestFraction = fraction;
                bestDenseIndex = denseIndex;
            }
        }

        return bestDenseIndex;
    }
}
```

### `HealthQueries` (new static class, `Game/Modules/Health/HealthQueries.cs`)

The one shared chokepoint every "what's this entity's current/max HP" consumer goes through --
the same reasoning that already drove `IMapQuery.IsBlocking` to be the single Blocking/NonBlocking
decision point, applied here to Simple-vs-Complex:

```csharp
public static class HealthQueries
{
    public static bool TryGetTotals(
        PackedComponentPool<SimpleHealthComponent> simpleHealth,
        MultiComponentPool<BodyPartComponent> bodyParts,
        int entityId,
        out float current,
        out float maximum)
    {
        if (simpleHealth.TryGetReadonly(entityId, out var simple))
        {
            current = simple.CurrentHealth;
            maximum = simple.MaximumHealth;
            return true;
        }

        if (!bodyParts.Has(entityId))
        {
            current = 0f;
            maximum = 0f;
            return false;
        }

        current = 0f;
        maximum = 0f;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            current += part.CurrentHealth;
            maximum += part.MaximumHealth;
        }

        return true;
    }
}
```
Deliberately does not fold in `StatModifierMath`'s `MaximumHealth` modifier -- callers that need
the modifier-effective maximum (`HealthBarElement`, `MapWindow.DrawHealthBar`) apply
`StatModifierMath.GetEffectiveValue` to the returned `maximum` themselves, same as they already do
today against `SimpleHealthComponent.MaximumHealth` directly; `HealthQueries` only owns the
Simple-vs-Complex sum, not the modifier chain on top of it.

### `HealthDamage` becomes a dispatching facade; `HealthHeal` does not

Damage and heal diverge here, deliberately, because their real call sites don't look the same.
Damage has several non-`ActionEffect` callers that each own their own pools and call
`HealthDamage.Apply` directly with no `ActionEffectContext` to fork through --
`ContactDamageSystem`, `PoisonSystem`/`BurningSystem`'s DoT ticks -- so `HealthDamage.Apply`
itself has to be the one shared Simple/Complex chokepoint every one of them can lean on unchanged.
Heal has exactly one caller today, `DirectHeal.Apply` (`Game/Modules/Actions/Effects/
DirectHeal.cs`) -- already holds a real `ActionEffectContext` with everything it needs to fork
itself, so there's no second call site forcing a shared facade into existence the way damage has.

**`HealthDamage.Apply`** gains two new optional trailing parameters --
`MultiComponentPool<BodyPartComponent>? bodyParts = null` and `MathUtility? mathUtility = null` --
so every existing call site keeps compiling unchanged (the same "optional, defaults to today's
behavior" pattern `statModifiers`/`deadEntities` already use). If `simpleHealth.Has(entityId)`,
run today's logic unchanged (renamed pool only). Else if `bodyParts?.Has(entityId) == true`,
delegate to a new internal `ComplexHealthDamage.Apply` (below) -- `mathUtility` becomes required
at that point (throws if null, mirroring how `MovementModule.Configure` throws on a still-null
`EntityMoveSync` per CLAUDE.md's Game module pattern: a caller that reaches a Complex entity
without ever wiring `mathUtility` is a real construction bug, not a state worth degrading
gracefully from). Neither branch taken (no `SimpleHealthComponent`, no `BodyPartComponent`) is
today's existing no-op -- "an immortal entity a status effect still applied to."

**`ComplexHealthDamage.Apply`** (new, `Game/Modules/Health/ComplexHealthDamage.cs`):
1. `BodyPartSelection.PickRandom` selects one part (see `TODO.md`'s own "exactly one part per hit
   as the simplest version" decision -- the multi-part case is explicitly the Targeted body part
   damage follow-up's job).
2. Apply the same `IncomingDamage`-then-clamp-against-effective-`MaximumHealth` modifier chain
   `HealthDamage.Apply` already runs, scoped to that one part's own `CurrentHealth`/
   `MaximumHealth` via `UpdateByDenseIndex`.
3. If the part's `CurrentHealth` lands at 0: set `IsDisabled = true` and
   `RegenLockoutFramesRemaining = 10 * GameTiming.FramesPerSecond` in the same update.
4. If the part `IsVital` and now at 0, and the entity isn't already known dead (`deadEntities?.Has
   (entityId) != true`, the same gate `SimpleHealthRegenSystem` already uses to skip corpses --
   threaded in as a new optional parameter here too), publish `EntityDiedEvent` -- the Complex
   equivalent of `HealthDamage.Apply`'s `wasAlive && updatedHealth.CurrentHealth == 0` check, just
   keyed off one Vital part instead of a summed total (see `TODO.md`'s own note: a Complex
   entity's *total* can still read well above 0 the instant its last Vital part hits 0).
5. `EntityDamagedEvent` publishing (the player-involved display/logging event) is unchanged in
   spirit -- built from `HealthQueries.TryGetTotals`'s post-hit sum, not the single hit part, so
   the HUD-facing event still reports the entity's real current/max total either way.

**`HealthHeal.Apply`** is untouched beyond the rename -- still Simple-only, still a flat `short
amount` against one pool. It gains no new parameters and no Complex branch, because its one real
caller forks itself instead (see below).

### `DirectHeal` heals every body part by its stated percentage, not one

Healing potions and scrolls -- every current and future `DirectHeal(Fraction)` entry -- heal a
Complex target by applying `Fraction` to *each* body part's own `MaximumHealth` independently, not
by picking the single most-wounded part the way passive regen does. A 50% heal potion used on a
Goblin with a nearly-dead Arm and an undamaged Torso restores both by 50% of their own max, same as
it would restore a Simple entity's one pool by 50% of its max -- the fraction is uniform across
every part, mirroring how `DirectHeal`'s own doc comment already states the fraction is "computed
per target, not per caster" for a multi-target splash; this is the same idea one level down, per
part instead of per target.

`DirectHeal.Apply` becomes the fork point (not `HealthHeal.Apply`, per the asymmetry above):
```csharp
public sealed record DirectHeal(float Fraction) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (Fraction <= 0)
        {
            return;
        }

        if (context.Health.TryGetReadonly(context.TargetEntityId, out var targetHealth))
        {
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(context.StatModifiers, context.TargetEntityId, StatModifierTarget.MaximumHealth, targetHealth.MaximumHealth);
            HealthHeal.Apply(context.Health, context.TargetEntityId, (short)(Fraction * effectiveMaximumHealth), context.StatModifiers);
            return;
        }

        if (context.BodyParts?.Has(context.TargetEntityId) == true)
        {
            ComplexHealthHeal.ApplyFractionToAllParts(context.BodyParts, context.TargetEntityId, Fraction);
        }
    }
}
```
`ActionEffectContext` (`Game/Modules/Actions/ActionEffectContext.cs`) gains one new optional
field, `MultiComponentPool<BodyPartComponent>? BodyParts = null`, alongside its existing `Health`
field -- threaded in wherever the context is built (`ActionEffectResolver`,
`ConsumableActivationSystem`), the same way every other feature-gated pool on that record already
is.

**`ComplexHealthHeal.ApplyFractionToAllParts`** (new, `Game/Modules/Health/ComplexHealthHeal.cs`)
is the entire class -- no per-part-selection method, since nothing selects a single part to heal
anymore:
```csharp
public static class ComplexHealthHeal
{
    /// <summary>Heals every body part entityId owns by the same Fraction of that part's own
    /// MaximumHealth -- what a healing potion/scroll's DirectHeal does against a Complex target,
    /// applying uniformly rather than concentrating on the single most-wounded part the way
    /// passive regen (BodyPartSelection.PickLowestPercentage) does. Clears IsDisabled the instant
    /// a part's CurrentHealth ticks back above 0 -- the lockout only ever gates passive regen,
    /// never an active heal, so this never checks RegenLockoutFramesRemaining at all.</summary>
    public static void ApplyFractionToAllParts(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, float fraction)
    {
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            bodyParts.UpdateByDenseIndex(denseIndex, fraction, static (ref BodyPartComponent part, float f) =>
            {
                part.CurrentHealth = MathHelper.Clamp(part.CurrentHealth + part.MaximumHealth * f, 0f, part.MaximumHealth);
                if (part.CurrentHealth > 0)
                {
                    part.IsDisabled = false;
                }
            });
        }
    }
}
```

### `ComplexHealthRegenSystem` (new, `Game/Modules/Health/Systems/ComplexHealthRegenSystem.cs`)

Mirrors `SimpleHealthRegenSystem`'s shape and constructor exactly (`ISystem`, `StripeCount =
GameTiming.FramesPerSecond`, same `TieredEntityStripeSet` via `ProcessingTierWiring.CreateAndWire`
-- `MultiComponentPool<T>` already implements `IEntityMembershipPool`, the same interface
`ProcessingTierWiring.CreateAndWire`'s `drivingPool` parameter expects, so wiring it against
`componentManager.GetMultiPool<BodyPartComponent>()` needs no changes to `ProcessingTierWiring`
itself), same Constitution-scaled `AbilityScoreMath.Lerp`/`StatModifierMath.GetEffectiveValues`
per-tick amount computation. The two differences:

1. **Target selection**: `BodyPartSelection.PickLowestPercentage(bodyParts, entityId)` instead of
   unconditionally updating the entity's one pool. `-1` (nothing eligible -- every part full or
   locked out) skips the entity for this tick, same as `SimpleHealthRegenSystem`'s existing
   `effectiveRegen == 0f` skip.
2. **Lockout decrement**: on every visit to a due entity (regardless of whether a part was
   selected for healing this tick), walk the entity's own chain once and decrement any nonzero
   `RegenLockoutFramesRemaining` by `framesPerVisit` (clamped at 0), via `UpdateByDenseIndex` --
   the same "mutate in place while walking, no removal mid-walk" safety `StatModifierExpirySystem`
   already relies on for its own chain walk. `IsDisabled` clears the instant the selected part's
   `CurrentHealth` first ticks above 0 (checked the same update call that applies the regen
   amount).

The shared per-tick-amount computation (Constitution lerp, `StatModifierMath` calls,
`ProcessingTier`-scaled `secondsPerVisit`) is worth factoring out into one static helper both
`SimpleHealthRegenSystem` and `ComplexHealthRegenSystem` call, rather than copy-pasted a second
time -- e.g. `HealthRegenMath.ComputeTickAmount(constitutionTotal, framesPerVisit)`.

`HealthModule.RegisterSystems` registers both `SimpleHealthRegenSystem` and
`ComplexHealthRegenSystem` side by side, unconditionally -- two independent `TieredEntityStripeSet`
instances, each empty (and therefore free) until some entity actually joins their respective
driving pool, the same "every system runs every frame, does nothing if its own pop is empty" shape
`ManaRegenSystem` already coexists with `SimpleHealthRegenSystem` under today's `HealthRegenSystem`
naming.

### Race-defined body part templates (Phase 3's one proof case)

Each race blueprint that wants `ComplexHealth` specifies its own body part list -- name/
`BodyPartType`/min-max HP range/`IsVital` -- rolled at Build time via a new
`ComplexHealthEffects.GrantBodyParts` helper (`Game/Modules/Health/`, mirroring the shape
`AbilityScoreEffects.GrantDefaults`/`StatModifierEffects.Apply` already establish for a
blueprint-called static helper):

```csharp
public static class ComplexHealthEffects
{
    public static void GrantBodyParts(ComponentManager componentManager, int entityId, MathUtility mathUtility, IReadOnlyList<BodyPartTemplate> parts)
    {
        foreach (var part in parts)
        {
            var startingHealth = mathUtility.Next(part.MinimumHealth, part.MaximumHealth + 1);
            componentManager.GetMultiPool<BodyPartComponent>().Add(entityId, new BodyPartComponent(part.Name, part.Type, startingHealth, part.MaximumHealth, part.IsVital));
        }
    }
}

public readonly record struct BodyPartTemplate(string Name, BodyPartType Type, ushort MinimumHealth, ushort MaximumHealth, bool IsVital);
```
A race calling this instead of `componentManager.Merge(entityId, new SimpleHealthComponent(...))`
becomes Complex; every other race keeps calling the Simple constructor unchanged. Each race's own
list is fully independent -- no shared humanoid template, no assumption every Complex race has the
same part count or kind (see `TODO.md`'s own Fairy-wings/cat-four-legs example).

**Phase 3 proof case only**: give exactly one existing race a real (if not final-balance) body
part list to exercise the pipeline end-to-end. Goblin is the natural pick -- already takes
`MathUtility` by constructor, already has a tuned `MaximumHealth` (200) to redistribute across
parts. A plausible first split, not a balance-final one: Head 30 (Vital), Torso 60 (Vital), Arm x2
20 each, Leg x2 35 each (sums to 200, matching today's flat total so the swap itself doesn't
silently rebalance Goblin's overall toughness). Player/Fairy/Ghost stay on `SimpleHealthComponent`
-- this plan does not migrate every race, only proves the mechanism works on one.

### Presentation touches (mechanical once `HealthQueries` exists)

`HealthBarElement`/`PlayerHealthBarContent`/`MapWindow.DrawHealthBar`/`InspectionWindowContent`
(everywhere that reads `SimpleHealthComponent`/old `HealthComponent` directly for a current/max
pair) switch to `HealthQueries.TryGetTotals`, gaining a `MultiComponentPool<BodyPartComponent>`
constructor dependency alongside their existing `PackedComponentPool<SimpleHealthComponent>` one.
No visual change for a Simple entity (Player, most NPCs); a Complex entity's bar now reflects the
real summed total instead of reading nothing (today, a `BodyPartComponent`-only entity has no
`HealthComponent` at all, so `HealthBarElement`'s existing `TryGetReadonly` would simply fail and
draw nothing -- confirming this wiring is required, not optional, the moment Goblin becomes
Complex in Phase 3).

### Module registration (`HealthModule.cs`)

`RegisterComponents` adds `componentManager.RegisterMultiPool<BodyPartComponent>()` alongside the
existing (renamed) `SimpleHealthComponent` packed-pool registration. `Configure` additionally
captures `context.MathUtility` (already on `GameModuleContext`, unused by this module today) for
threading into `HealthDamage`/`HealthHeal` call sites that need it, the same way
`AbilityActivationSystem`/`ConsumableActivationSystem` already take `MathUtility` per
`PLAN-action-effect-activator.md`. `RegisterSystems` constructs and registers
`ComplexHealthRegenSystem` alongside the renamed `SimpleHealthRegenSystem`, with the same optional-
pool guard pattern (`statModifiers`/`deadEntities`/`abilityScores`) the existing registration
already uses.

## Open decisions this plan makes explicitly (called out since `TODO.md`'s own item left them open)

- **`HealthDamage` is a dispatching facade; `HealthHeal` is not.** Damage has several non-
  `ActionEffect` callers (`ContactDamageSystem`, DoT ticks) that need one shared chokepoint; heal
  has exactly one caller (`DirectHeal`) that already holds enough context to fork itself, so no
  second facade was worth building for a single call site -- matching `IMapQuery.IsBlocking`'s
  "one chokepoint" precedent where there's genuinely more than one caller to unify, and not
  applying that same shape where there isn't.
- **Healing potions and scrolls heal every body part by the stated percentage, not one.**
  `DirectHeal`'s existing per-target fraction semantics extend one level down to per-part on a
  Complex target -- a 50% heal restores every part by 50% of its own max simultaneously, via
  `ComplexHealthHeal.ApplyFractionToAllParts`. Only passive regen (`ComplexHealthRegenSystem`)
  concentrates on a single most-wounded part; an active heal source never does.
- **The 10s lockout is a plain field on `BodyPartComponent`**, decremented directly by
  `ComplexHealthRegenSystem`'s own chain walk, not `CountdownTicker` -- that helper is
  `PackedComponentPool`-only, and `StatModifierExpirySystem`'s own doc comment already establishes
  why it doesn't extend to a `MultiComponentPool` shape.
- **The lockout only gates passive regen, never an active heal** -- a potion or Heal spell can
  always target every disabled part immediately; the lockout exists solely to stop competing
  damage/regen *ticks* from flickering a part's state, and `ComplexHealthHeal` never reads it at
  all (not "reads it and ignores it" -- there's no lockout check in that code path to begin with).
- **Exactly one random part per hit** for this pass, per `TODO.md`'s own stated simplification --
  multi-part/targeted selection is explicitly the next follow-up's scope, not this plan's. This
  applies to damage only; heal was never single-part to begin with (see above).

## Not in scope -- follow-up TODO items this plan unblocks

All six already logged (or being logged alongside this plan) under `TODO.md`'s Game/Presentation
sections, each explicitly blocked on this plan landing first -- this list is the canonical one;
`TODO.md`'s own "Body parts" item deliberately points here instead of re-enumerating it, so update
it here first if the set ever changes:

1. **Targeted body part damage and multi-part effects** (Game) -- real per-part targeting and
   multi-part resolution (lava->legs, Fireball->all parts), replacing this plan's
   `BodyPartSelection.PickRandom` placeholder.
2. **BodyPartType categorization and gameplay effects** (Game) -- the consumption pass over
   `BodyPartComponent.IsDisabled`/`Type` this plan introduces but never reads: movement gated by
   disabled Legs, melee/lifting gated by disabled Arms, equipment slot counts keyed by active
   typed parts.
3. **Per-body-part vs whole-entity status effects** (Game) -- letting Burning/similar effects
   localize to the specific part a hit landed on, once the Targeted damage follow-up above can
   name which part that was.
4. **HealthWindow -- per-body-part health and status display** (Presentation) -- the real UI this
   plan has no substitute for; today's health bar only ever shows the derived total, never which
   part is critical or disabled.
5. **Limb-specific gameplay penalties beyond disable** (Game) -- graduated penalty curves
   (a wounded-but-not-disabled Leg slowing movement, not just a fully disabled one blocking it),
   layered on top of the BodyPartType categorization follow-up's binary gates once those exist.
6. **Player health bar hover -- per-body-part HP dropdown** (Presentation) -- a lightweight,
   delay-gated hover popup on `PlayerHealthBarContent` listing total % first, then each part's own
   name and HP%; a glanceable companion to `HealthWindow` above, not a replacement for it (no
   Vital/disabled state, no status effects, no click-to-open).

## Test plan

New unit tests (`Tests/Modules/Health/`), seeded `MathUtility` throughout:
- `BodyPartSelectionTests`: `PickRandom` lands on a valid dense index across repeated seeded
  rolls, returns -1 for an entity with none; `PickLowestPercentage` picks the correct part across
  mixed fractions, skips a locked-out part, returns -1 when every part is full or locked out.
- `HealthQueriesTests`: sums correctly across N `BodyPartComponent` instances; falls through to
  `SimpleHealthComponent` when present; returns false for an entity with neither.
- `ComplexHealthDamageTests`: damage lands on exactly one part; a hit dropping a non-Vital part to
  0 sets `IsDisabled`+lockout; a hit dropping a Vital part to 0 publishes `EntityDiedEvent` exactly
  once (not on a subsequent hit to an already-dead entity); `IncomingDamage`/effective-maximum
  modifiers apply the same as `HealthDamageTests` already covers for the Simple path.
- `ComplexHealthHealTests` (`ApplyFractionToAllParts`): every part rises by `Fraction` of its own
  `MaximumHealth`, including an already-full part (clamped, no overflow) and a locked-out part
  (heals it anyway -- the lockout is never consulted here); clears `IsDisabled` on any part healed
  back above 0; a mixed-damage entity (one part at 20%, another at 90%) ends up with each part
  raised by the same fraction, not converged toward the same value.
- `DirectHealTests`: a Complex target routes through `ComplexHealthHeal.ApplyFractionToAllParts`
  (every part rises) instead of `HealthHeal.Apply`; a Simple target's existing coverage is
  unchanged.
- `ComplexHealthRegenSystemTests`: mirrors `HealthRegenSystemTests`' existing Constitution-scaling/
  dead-entity-skip coverage, plus: selects the lowest-percentage eligible part each tick; a locked-
  out part's own countdown still decrements even on a tick where it isn't selected; a part exits
  lockout and becomes selectable again after enough ticks.

Existing tests: `HealthComponentTests`/`HealthRegenSystemTests`/`HealthModuleTests`/
`HealthDamageTests`/`HealthHealTests` all renamed (class/type references only) and pass unchanged
-- this plan's rename is byte-for-byte behavior-preserving for the Simple path. `BlueprintTests`
gains coverage for Goblin's new Complex body-part list (part count, names/types, Vital flags, HP
sums to the same 200 total `Fairy`/`Player` still use as a flat `SimpleHealthComponent` max).

## Execution phases (stop for manual in-game testing after each, per established working style)

1. **Rename + additive data model.** `HealthComponent`->`SimpleHealthComponent`,
   `HealthRegenSystem`->`SimpleHealthRegenSystem`, every call site/test updated. Add
   `BodyPartType`/`BodyPartComponent`/`BodyPartSelection`/`HealthQueries`, register the new
   `MultiComponentPool<BodyPartComponent>` in `HealthModule`. No race grants a body part yet.
   Verify: `dotnet build`, `dotnet test` (renamed tests green, new `BodyPartSelectionTests`/
   `HealthQueriesTests` green), then a quick in-game smoke test confirming the player's own health
   bar/regen still behaves identically (it's still 100% Simple-path).
2. **Damage/heal/regen machinery**, fully wired but still unreachable in-game. `HealthDamage`
   becomes a dispatching facade; `ComplexHealthDamage`/`ComplexHealthHeal` land; `DirectHeal` gains
   its own Complex fork and `ActionEffectContext` gains `BodyParts`; `ComplexHealthRegenSystem`
   registers in `HealthModule`. Verify: `dotnet build`, `dotnet test` only
   (`ComplexHealthDamageTests`/`ComplexHealthHealTests`/`DirectHealTests`/
   `ComplexHealthRegenSystemTests` green) -- no in-game verification possible yet, since no entity
   owns a `BodyPartComponent`.
3. **Goblin becomes the Complex proof case**, plus Presentation wiring.
   `ComplexHealthEffects.GrantBodyParts` called from `Goblin.Build` with the Head/Torso/Arm x2/
   Leg x2 split above; `HealthBarElement`/`PlayerHealthBarContent`/`MapWindow.DrawHealthBar`/
   `InspectionWindowContent` switch to `HealthQueries.TryGetTotals`. Verify: `dotnet build`,
   `dotnet test`, then manually fight a Goblin in-game -- confirm its health bar drains/regens
   sensibly (watch for the bar advancing in visibly uneven per-part increments rather than a
   smooth Simple-style trickle), confirm it still dies and leaves a lootable corpse once enough
   damage lands, confirm a partially-damaged Goblin left alone regenerates one part at a time. Also
   damage a Goblin below half health and let `TestCombatBehaviorSystem`'s existing
   below-half-health-with-a-potion self-heal trigger -- confirm the resulting jump on its health
   bar reads as every part rising together (a bigger, uniform step) rather than the smaller,
   single-part steps passive regen produces, the concrete in-game confirmation that
   `ComplexHealthHeal.ApplyFractionToAllParts` is actually what a potion exercises.
4. **Cleanup.** Grep for any leftover `HealthComponent`/`HealthRegenSystem` references the rename
   missed (including `Mods.TestFixtures/ReplacementHealthModule.cs`, a real mod-pattern consumer
   of the old names). Update `TODO.md`: mark the Body parts item's design phase as landed, and add
   the fifth follow-up ("Limb-specific gameplay penalties beyond disable") alongside the four
   already logged.
