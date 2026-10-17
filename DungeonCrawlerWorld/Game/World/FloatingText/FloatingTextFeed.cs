using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffects;

namespace Game.World;

/// <summary>The one publisher of FloatingTextEvent, limited to entities in the Local processing tier.</summary>
/// <remarks>
/// The Local tier is the visibility filter: it covers every tile the map shows around the player at the zoom levels
/// that draw floating text, and keeps high-frequency publishers bounded to the Local population.
/// </remarks>
public sealed class FloatingTextFeed(EventBus eventBus, IReadOnlyComponentPool<ProcessingTierComponent> processingTiers, IReadOnlyComponentPool<TransformComponent> transforms)
{
    /// <summary>Whether a floating text published for entityId would be shown.</summary>
    /// <remarks>For a publisher that has work to do before it knows the amount, so it can skip that work for an entity nobody sees.</remarks>
    public bool IsShownFor(int entityId) =>
        processingTiers.TryGetReadonly(entityId, out var processingTier)
        && processingTier.Tier == ProcessingTierLevel.Local;

    public void Publish(int entityId, FloatingTextKind kind, ushort amount, StatusEffectType effectType = default, FloatingTextFlags flags = FloatingTextFlags.None)
    {
        if (!IsShownFor(entityId) || !transforms.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        eventBus.Publish(new FloatingTextEvent(entityId, kind, amount, transform.Position, transform.Size, effectType, flags));
    }
}
