using Engine.ECS.Components.Stores;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Health;

/// <summary>Complex-health counterpart to a healing potion/scroll's DirectHeal -- heals every body part entityId owns at once, not one at a time.</summary>
/// <remarks>
/// Applies Fraction uniformly to each part's own modifier-effective MaximumHealth, clamped there
/// rather than the raw stored field -- the same StatModifierTarget.MaximumHealth chain
/// ComplexHealthDamage/ComplexHealthRegenSystem already apply per-part, so a part can actually
/// reach its true (buffed) cap through a heal the same way it already can through regen or a hit
/// landing short of killing it. Applied independently per part rather than concentrating on the
/// single most-wounded part the way passive regen (BodyPartSelection.PickLowestPercentage) does.
/// Clears IsDisabled the instant a part's CurrentHealth ticks back above 0. Never checks
/// RegenLockoutFramesRemaining -- that lockout only ever gates passive regen, never an active heal.
/// </remarks>
public static class ComplexHealthHeal
{
    public static void ApplyFractionToAllParts(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, float fraction, MultiComponentPool<StatModifierComponent>? statModifiers = null)
    {
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            bodyParts.UpdateByDenseIndex(denseIndex, (fraction, statModifiers, entityId), static (ref BodyPartComponent part, (float Fraction, MultiComponentPool<StatModifierComponent>? StatModifiers, int EntityId) state) =>
            {
                var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(state.StatModifiers, state.EntityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
                part.CurrentHealth = MathHelper.Clamp(part.CurrentHealth + effectiveMaximumHealth * state.Fraction, 0f, effectiveMaximumHealth);
                if (part.CurrentHealth > 0)
                {
                    part.IsDisabled = false;
                }
            });
        }
    }
}
