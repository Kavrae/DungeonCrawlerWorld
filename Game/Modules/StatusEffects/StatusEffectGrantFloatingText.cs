using Engine.ECS.Components;
using Game.World;

namespace Game.Modules.StatusEffects;

/// <summary>Publishes the floating text for one attempt to grant stacks of a status effect: the stacks that landed, or "Immune" when none did because of an immunity.</summary>
/// <remarks>
/// Measured as the stack count after the grant minus the count before it, so a stack that didn't land for any reason
/// (immunity, the effect's own stack cap) is never shown. A grant that landed nothing for another reason shows nothing.
/// </remarks>
public static class StatusEffectGrantFloatingText
{
    public static void Publish(FloatingTextFeed floatingTextFeed, ComponentManager componentManager, IStatusEffectAuraApplier applier, int entityId, int stackCountBefore)
    {
        if (!floatingTextFeed.IsShownFor(entityId))
        {
            return;
        }

        var stacksAdded = applier.GetCurrentStackCount(componentManager, entityId) - stackCountBefore;
        if (stacksAdded > 0)
        {
            floatingTextFeed.Publish(entityId, FloatingTextKind.StatusEffectStacksAdded, (ushort)System.Math.Min(stacksAdded, ushort.MaxValue), applier.EffectType);
        }
        else if (StatusEffectImmunity.HasImmunity(componentManager, entityId, applier.EffectType))
        {
            floatingTextFeed.Publish(entityId, FloatingTextKind.Immune, 0, applier.EffectType);
        }
    }
}
