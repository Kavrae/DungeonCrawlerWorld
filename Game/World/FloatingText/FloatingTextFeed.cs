using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffects;

namespace Game.World;

/// <summary>The one publisher of FloatingTextEvent, limited to entities in the Local processing tier.</summary>
/// <remarks>
/// The Local tier is the visibility filter: it covers every tile the map shows around the player at the zoom levels
/// that draw floating text, and keeps high-frequency publishers bounded to the Local population. Created with
/// GameModuleContext, before any pool exists, so GameBootstrapper wires it once the pools do; nothing may publish
/// through it before then.
/// </remarks>
public sealed class FloatingTextFeed
{
    private EventBus _eventBus = null!;
    private IReadOnlyComponentPool<ProcessingTierComponent> _processingTiers = null!;
    private IReadOnlyComponentPool<TransformComponent> _transforms = null!;

    public void Wire(EventBus eventBus, IReadOnlyComponentPool<ProcessingTierComponent> processingTiers, IReadOnlyComponentPool<TransformComponent> transforms)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(processingTiers);
        ArgumentNullException.ThrowIfNull(transforms);

        _eventBus = eventBus;
        _processingTiers = processingTiers;
        _transforms = transforms;
    }

    /// <summary>Whether a floating text published for entityId would be shown.</summary>
    /// <remarks>For a publisher that has work to do before it knows the amount, so it can skip that work for an entity nobody sees.</remarks>
    public bool IsShownFor(int entityId) =>
        _processingTiers.TryGetReadonly(entityId, out var processingTier)
        && processingTier.Tier == ProcessingTierLevel.Local;

    public void Publish(int entityId, FloatingTextKind kind, ushort amount, StatusEffectType effectType = default, FloatingTextFlags flags = FloatingTextFlags.None)
    {
        if (!IsShownFor(entityId) || !_transforms.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        _eventBus.Publish(new FloatingTextEvent(entityId, kind, amount, transform.Position, transform.Size, effectType, flags));
    }
}
