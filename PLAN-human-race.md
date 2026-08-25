# Human race, applied to the player

(Design plan, saved for implementation per this repo's convention -- see `PLAN-body-parts.md` for
the Complex-health machinery this builds on. Item 7 of the current body-parts follow-up work.)

## Context

`PLAN-body-parts.md` landed Complex health (`BodyPartComponent`) with exactly one proof-case race,
Goblin. The player today is Simple health only: `PlayerBlueprint.Build` merges a flat
`SimpleHealthComponent(current, MaximumHealth: 100)` directly, with no `RaceComponent` at all --
unlike Goblin/Fairy/Ghost, the player has never had a race identity.

This plan adds a `Human` race (a real, independent `IBlueprint`, mirroring Goblin's shape) carrying
250 HP across the same body-part *types* Goblin uses (Head/Torso/Arm/Leg), and composes it into
`PlayerBlueprint` so the player becomes the second Complex-health entity.

## Design

### `Human` race blueprint (new, `Game/Blueprints/Races/Human.cs`)

Deliberately minimal compared to Goblin -- Goblin's `Build` sets NPC-only concerns (random
display name, `MovementMode.Random`, an NPC-tuned `ActionLockComponent`, a Punch grant, starting
loot, flat default ability scores) that `PlayerBlueprint` already sets its own, player-appropriate
versions of. `Human.Build` grants only what a race is actually responsible for: identity and body
parts.

```csharp
public sealed class Human(MathUtility mathUtility) : IBlueprint
{
    public static readonly Guid RaceId = new("<new guid>");
    private const string RaceName = "Human";
    private const string Description = "Adaptable and unremarkable in any single way -- which is exactly what makes them so widespread.";

    private static readonly BodyPartTemplate[] BodyParts =
    [
        new BodyPartTemplate("Head", BodyPartType.Head, 40, 40, IsVital: true),
        new BodyPartTemplate("Torso", BodyPartType.Torso, 80, 80, IsVital: true),
        new BodyPartTemplate("Left Arm", BodyPartType.Arm, 25, 25, IsVital: false),
        new BodyPartTemplate("Right Arm", BodyPartType.Arm, 25, 25, IsVital: false),
        new BodyPartTemplate("Left Leg", BodyPartType.Leg, 40, 40, IsVital: false),
        new BodyPartTemplate("Right Leg", BodyPartType.Leg, 40, 40, IsVital: false),
    ];

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));
        ComplexHealthEffects.GrantBodyParts(componentManager, entityId, mathUtility, BodyParts);
    }
}
```
40+80+25+25+40+40 = 250. Same proportions as Goblin's split (Head/Torso Vital, Arms/Legs not),
scaled up, with round numbers rather than an exact 1.25x scale of Goblin's odd totals (which would
produce fractional HP values).

### `PlayerBlueprint` composes `Human` in

`PlayerBlueprint` already takes `mathUtility` as a primary-constructor parameter, so `Human` is
constructed from it directly rather than adding a new constructor parameter (no ripple into
`FloorBuilder.cs`'s or any test's `new PlayerBlueprint(...)` call sites):

```csharp
private readonly Human _human = new(mathUtility);
```
(A primary-constructor member that does real construction work with the parameter, not a plain
passthrough -- stays in its natural declaration position, not hoisted above unrelated members, per
this codebase's own primary-constructor convention.)

`Build` calls `_human.Build(componentManager, entityId)` once, in place of today's
`componentManager.Merge(entityId, new SimpleHealthComponent(...))` line -- removing the now-unused
`MaximumHealth` const. Everything else in `PlayerBlueprint.Build` (glyph, movement, action lock,
transform, ability scores, action/item grants, the permanent `StatModifierTarget.MaximumHealth`
+50% buff) stays unchanged; the `MaximumHealth` stat-modifier buff keeps working unmodified since it
applies to whatever `HealthQueries.TryGetTotals` returns as the summed maximum, the same as it
already does for Goblin today.

No other race becomes Complex. Fairy/Ghost/NPC Goblin variants are unaffected.

### Fix required: `ConsumableActivationSystem.ApplyPotionToTarget`'s hard Simple-health gate

Audited every direct `SimpleHealthComponent` pool read outside the Health module itself (not routed
through `HealthDamage`/`HealthHeal`/`HealthQueries`) to find what breaks once the *player* --
not just an NPC -- can be Complex. Found one real, severe gap:

`Game/Modules/Inventory/Systems/ConsumableActivationSystem.cs`'s `ApplyPotionToTarget` --
```csharp
if (_deadEntities?.Has(targetEntityId) == true || !_health.TryGetReadonly(targetEntityId, out _))
{
    return;
}
```
hard-requires a `SimpleHealthComponent` to consider *any* potion effect valid on a target --
health, mana, damage, toxic, hotkey-expansion, all of them, since every potion goes through this
one gate before `ActionEffectSequence.Apply`. A Complex player would fail this check outright,
silently turning every potion in the game into a no-op the instant this plan lands, unless fixed.
(`ApplyScrollToTarget`/`ApplyWandToTarget` already don't hard-require `SimpleHealthComponent` --
this asymmetry is exactly why potions are the one path that breaks.)

Fix: presence-only check across both pools, using the `_bodyParts` field this system already has
(threaded in during `PLAN-body-parts.md`'s Phase 2, currently unused by this method):
```csharp
if (_deadEntities?.Has(targetEntityId) == true || (!_health.Has(targetEntityId) && _bodyParts?.Has(targetEntityId) != true))
{
    return;
}
```
No `HealthQueries.TryGetTotals` call needed here -- this method only ever needed "does the target
have health at all," never the actual current/max values.

### Discovered, out-of-scope-for-this-plan bug: Goblin's AI self-heal is currently dead

While auditing, found that `TestCombatBehaviorSystem.TryDecideSelfHeal`
(`Game/Modules/NpcBehavior/Systems/TestCombatBehaviorSystem.cs`) also reads
`_health.TryGetReadonly(entityId, ...)` directly for its "below half health, drink a potion"
NPC-AI check -- since Goblin became Complex in `PLAN-body-parts.md` Phase 3, this always returns
`false` for a Goblin, so **Goblin's self-heal behavior has been silently dead since Phase 3
landed**. This is unrelated to the player/Human work (Goblin, not the player, is affected), so it's
called out here rather than folded into this plan -- flagging for a decision on whether to fix it
as part of this pass anyway (it's a two-line change, same shape as the `ConsumableActivationSystem`
fix above) or file it separately.

## Test plan

- `Tests/Blueprints/HumanTests.cs` (new, mirroring `Tests/Blueprints/BlueprintTests.cs`'s Goblin
  coverage style): `Build` grants a `RaceComponent` with the right Id/Name, grants exactly 6
  `BodyPartComponent`s matching the template above (names/types/Vital flags/HP-in-range), sums to
  250, and grants no `SimpleHealthComponent` at all.
- `Tests/Blueprints/BlueprintTests.cs`'s existing `PlayerBlueprint_Build_...` test: replace its
  `SimpleHealthComponent` assertion with the same Complex-path body-part walk added for Goblin in
  Phase 3, plus asserting the player now has a `RaceComponent` with `Human.RaceId`.
- `Tests/Modules/Inventory/ConsumableActivationSystemTests.cs`: add a case proving a Complex target
  (no `SimpleHealthComponent`, has `BodyPartComponent`s) is no longer rejected by
  `ApplyPotionToTarget`'s gate -- a potion's effect actually lands (e.g. `DirectHeal` heals its
  parts) instead of silently no-oping.
- Full `dotnet build`/`dotnet test` pass, matching the existing 25-failure pre-existing baseline.

## Execution phases

1. **`Human` race + `PlayerBlueprint` composition + the `ConsumableActivationSystem` fix**, all
   together (small enough not to split, and the fix is load-bearing for the player change to be
   usable at all). Verify: `dotnet build`, `dotnet test`, then manual in-game test -- confirm the
   player's health bar/HUD shows 250-based totals, confirm passive regen advances in uneven
   per-part steps the same way Goblin's does, confirm a Health Potion still actually heals the
   player (the concrete proof the `ConsumableActivationSystem` fix works), confirm Mana/Damage/
   Toxic/Hotkey-Expansion potions still work too (they all shared the same broken gate), confirm
   the player still dies on a Vital part (Head or Torso) reaching 0 even if Arms/Legs are intact.
2. Decide on and (if wanted) fix the Goblin self-heal bug as a small follow-on, or leave it filed
   for later -- your call once you've seen the rest of this plan.

## Open questions for you

- Body part HP split (40/80/25/25/40/40) -- fine as proposed, or do you want different numbers?
- Fix the Goblin self-heal bug now (small, same-shaped fix) or file it separately?
- Any other race ever becomes Human besides the player, or is `Human.cs` player-only for now (it's
  still a fully standalone `IBlueprint`, so nothing stops a future Human NPC, but no such NPC is
  planned here)?
