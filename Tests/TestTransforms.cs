using Engine.ECS.Components;
using Game.Modules.Core.Components;

namespace Tests;

/// <summary>Writes an entity's transform outright, adding it if the entity has none.</summary>
internal static class TestTransforms
{
    public static void Set(ComponentManager componentManager, int entityId, TransformComponent transform)
    {
        var transforms = componentManager.GetDirectPool<TransformComponent>();
        if (transforms.Has(entityId))
        {
            transforms.Get(entityId) = transform;
        }
        else
        {
            transforms.Add(entityId, transform);
        }
    }
}
