using Engine.ECS.Components;
using Game.World;

namespace Game.Modules.StatusEffects;

/// <summary>Publishes the floating text for one attempt to grant stacks of a status effect: the stacks that landed, or "Immune" when none did because of an immunity.</summary>
/// <remarks>A grant that landed nothing for another reason (the effect's own stack cap) shows nothing.</remarks>
public static class StatusEffectGrantFloatingText
{
    /// <param name="stacksLanded">What IStatusEffectApplier.ApplyStacks returned.</param>
    /// <param name="announcesRefusal">False to leave "Immune" unshown: this refusal was already reported.</param>
    public static void Publish(FloatingTextFeed floatingTextFeed, ComponentManager componentManager, StatusEffectType effectType, int entityId, int stacksLanded, bool announcesRefusal = true)
    {
        if (!floatingTextFeed.IsShownFor(entityId))
        {
            return;
        }

        if (stacksLanded > 0)
        {
            floatingTextFeed.Publish(entityId, FloatingTextKind.StatusEffectStacksAdded, (ushort)System.Math.Min(stacksLanded, ushort.MaxValue), effectType);
        }
        else if (announcesRefusal && StatusEffectImmunity.HasImmunity(componentManager, entityId, effectType))
        {
            floatingTextFeed.Publish(entityId, FloatingTextKind.Immune, 0, effectType);
        }
    }
}
