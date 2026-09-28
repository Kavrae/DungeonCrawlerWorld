using Game.Blueprints;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Game.World;

namespace Tests;

/// <summary>Entity sources for tests that put entities straight into pools, with no EntityManager to issue their keys.</summary>
/// <remarks>Keys are issued in id order from a fresh EntityKeys, so entity id N always has key N + 1 -- the same key an EntityManager gives the Nth entity it creates.</remarks>
internal static class TestSources
{
    public static EntityKey KeyOf(int entityId) => new((ulong)entityId + 1);

    /// <summary>A source for entityId keyed as KeyOf(entityId), with no recorded identity.</summary>
    public static ActionSource Entity(int entityId)
    {
        var entityKeys = new EntityKeys();
        for (var id = 0; id <= entityId; id++)
        {
            entityKeys.Issue(id);
        }

        return ActionSource.FromEntity(BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: entityId + 1, initialComponentCapacity: 1)), entityKeys, entityId, creatures: new BlueprintRegistry());
    }
}
