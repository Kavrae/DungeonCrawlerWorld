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

## Addendum: Human absorbed more of PlayerBlueprint than originally scoped

After the plan below landed, `Human` was expanded (by explicit follow-up request) to also grant
Glyph (`@`, white), Sprite ("Player" lookup), Movement (`PlayerControlled`), `ActionLockComponent`
(30-frame), `TransformComponent`, ability scores (the same clustered 2d6 roll `PlayerBlueprint`
always used), and the Punch `ActionInstanceComponent` grant -- everything `PlayerBlueprint.Build`
used to set directly for these concerns now lives in `Human.Build` instead, with `PlayerBlueprint`
calling `_human.Build(...)` once near the top of its own `Build` and keeping only genuinely
player-specific concerns (Crawler identity, display text, starting inventory/wands/hotkeys, the two
mana-costed spell grants, the permanent stat-modifier buffs). This supersedes the "deliberately
minimal compared to Goblin" framing in the Design section below -- `Human` is no longer minimal, it
is now the player's entire starting-kit-minus-inventory. The real consequence: `Human`'s defaults
(PlayerControlled movement, a 30-frame lock, the '@' glyph) are player-shaped, not a neutral race
default -- a future Human NPC composing this race would need to override all of these via a
CompositeBlueprint overrides step, the same way `GoblinEngineerBlueprint` already overrides Goblin's
own `ActionLockComponent`.

## Addendum 2: reversed -- Human is the NPC default, PlayerBlueprint overrides

Reversed by explicit follow-up request: `Human.Build` now grants a generic NPC shape (pink 'h'
glyph, no Sprite, `MovementMode.Random`) matching every other race's own pattern, and no longer
grants a Sprite at all (a future Human NPC renders by its glyph, not silently wearing the player's
sprite -- `MapWindow` prefers Sprite over Glyph when both are present, so leaving the old
unconditional "Player" sprite lookup in `Human` would have made the new glyph invisible on any
non-player Human). `ActionLockComponent`'s 30-frame value stays Human's real default, unchanged --
not reverted to something Goblin-like, per explicit instruction. `PlayerBlueprint` overrides
Glyph/Movement immediately after `_human.Build(...)`, and grants its own Sprite fresh (no longer an
override, since Human doesn't grant one).

**Real gotcha hit while implementing this**: overriding via a second `Merge` call (the first
approach tried) silently failed for `GlyphComponent`/`MovementComponent`, because neither's
registered merge policy is "last write wins":
- `GlyphComponent` (`CoreModule.RegisterComponents`) only Lerps `GlyphColor` 50/50 between existing
  and incoming, and never touches `Glyph` (the string) at all -- a second `Merge` call left the
  glyph character stuck on Human's original value forever, and would have blended the color into a
  pink/white hybrid rather than pure white.
- `MovementComponent` (`MovementModule.RegisterComponents`) takes
  `(MovementMode)Math.Max((byte)existing.MovementMode, (byte)incoming.MovementMode)` -- this
  happened to still work by coincidence (`MovementMode.PlayerControlled = 2` is numerically the
  highest value in the enum), but is not a real override and would silently break if a mode with a
  higher ordinal were ever added.

Fixed by using `componentManager.TryUpdate` instead of `Merge` for both -- a direct field mutation
that bypasses the merge policy entirely, the same pattern `GoblinEngineerBlueprint`'s own overrides
step already uses for Goblin's `ActionLockComponent` (`actionLock.StandardLockFrames = ...` inside a
`TryUpdate` lambda, not a second `Merge`). **Lesson for any future composite-blueprint override**:
check the target component's actual registered merge policy before assuming a second `Merge` call
will "win" -- several components in this codebase have blend/concatenate/max semantics specifically
because they're designed for legitimate multi-source composition (e.g. two buffs both granting
`SimpleHealthComponent`), not for a deliberate override. `TryUpdate` is the safe, explicit choice
whenever the intent is "replace this value outright," regardless of what the component's merge
policy would otherwise do.

## Decisions (confirmed)

- Body part HP split: 40/80/25/25/40/40 as proposed.
- The Goblin self-heal bug is in scope for this pass -- `TestCombatBehaviorSystem.TryDecideSelfHeal`
  gets the same presence-check treatment as `ConsumableActivationSystem.ApplyPotionToTarget`:
  replace its direct `_health.TryGetReadonly(entityId, out var health) || health.CurrentHealth * 2
  >= health.MaximumHealth` check with a `HealthQueries.TryGetTotals`-based one (this one *does* need
  the actual current/maximum values, unlike the potion gate, so it uses `TryGetTotals` rather than a
  presence-only check -- thread a `MultiComponentPool<BodyPartComponent>` into this system/module the
  same optional-pool way every other Health-adjacent system already does). `NpcBehaviorModule.cs`
  needs updating to fetch and pass it through.
- `Human.cs` stays a fully standalone, reusable `IBlueprint` (no player-only coupling) -- nothing
  else uses it yet, but nothing prevents a future Human NPC either.
