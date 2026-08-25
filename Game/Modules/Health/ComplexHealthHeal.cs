using Engine.ECS.Components.Stores;
using Game.Modules.Health.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Health;

/// <summary>Complex-health counterpart to a healing potion/scroll's DirectHeal -- heals every body part entityId owns at once, not one at a time.</summary>
/// <remarks>
/// Applies Fraction uniformly to each part's own MaximumHealth, clamped, rather than
/// concentrating on the single most-wounded part the way passive regen
/// (BodyPartSelection.PickLowestPercentage) does. Clears IsDisabled the instant a part's
/// CurrentHealth ticks back above 0. Never checks RegenLockoutFramesRemaining -- that lockout
/// only ever gates passive regen, never an active heal.
/// </remarks>
public static class ComplexHealthHeal
{
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
