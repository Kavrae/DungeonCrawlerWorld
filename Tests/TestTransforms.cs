using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;

namespace Tests;

/// <summary>Writes an entity's transform outright, adding it if the entity has none, and places or removes it through the World.</summary>
internal static class TestTransforms
{
    public static void Set(ComponentManager componentManager, int entityId, TransformComponent transform)
    {
        var transforms = componentManager.GetDirectPool<TransformComponent>();
        if (transforms.Has(entityId))
        {
            using var write = transforms.Write(entityId);
            write.Value = transform;
        }
        else
        {
            transforms.Add(entityId, transform);
        }
    }

    public static void PlaceOnMap(Game.World.World world, ComponentManager componentManager, int entityId, Vector3Int position)
    {
        using var transform = componentManager.GetDirectPool<TransformComponent>().Write(entityId);
        world.PlaceEntityOnMap(entityId, position, ref transform.Value);
    }

    public static void RemoveFromMap(Game.World.World world, ComponentManager componentManager, int entityId)
    {
        using var transform = componentManager.GetDirectPool<TransformComponent>().Write(entityId);
        world.RemoveEntityFromMap(entityId, ref transform.Value);
    }
}
