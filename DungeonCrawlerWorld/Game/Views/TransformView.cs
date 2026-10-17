using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Core.Components;

namespace Game.Views;

/// <summary>Where an entity is and how big it is, whether or not it is on the map.</summary>
public sealed class TransformView(ComponentManager componentManager)
{
    private readonly DirectComponentPool<TransformComponent> _transforms = componentManager.GetDirectPool<TransformComponent>();

    public bool TryGetTransform(int entityId, out TransformComponent transform) => _transforms.TryGetReadonly(entityId, out transform);
}
