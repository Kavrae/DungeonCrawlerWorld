using Engine.ECS.Context;
using Engine.Math;
using Game.Bootstrap;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Bootstrap;

/// <summary>Destroying an entity through EntityManager, wired by the real bootstrapper: everything Game keeps about it outside the pools lets go before its id can be reused.</summary>
[TestClass]
public sealed class EntityDestructionTests
{
    private static (Game.World.World World, EcsContext Ecs, ProcessingTierResolver Resolver) Build()
    {
        var map = new Map(new Vector3Int(40, 40, 3));
        var result = GameBootstrapper.Build(ValidatedMods.None, map, new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50);
        var world = result.World;
        result.Internals.ProcessingTierResolver.SetReferencePosition(new Vector3Int(1, 1, (int)MapLayer.Ground));
        return (world, result.EcsContext, result.Internals.ProcessingTierResolver);
    }

    [TestMethod]
    public void DestroyEntity_ClearsItsFootprint_ItsTierMembership_AndItsAuraSources()
    {
        var (world, ecs, resolver) = Build();
        var position = new Vector3Int(10, 10, (int)MapLayer.Ground);
        var entityId = resolver.CreateEntityAt(ecs.EntityManager, position);
        ecs.ComponentManager.Merge(entityId, new TransformComponent(TransformComponent.UnplacedOn(MapLayer.Ground), new Vector2Byte(2, 2)));
        world.PlaceEntityOnMap(entityId, position, ref ecs.ComponentManager.GetDirectPool<TransformComponent>().Get(entityId));
        var sources = ecs.ComponentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        AuraSourceEffects.Toggle(sources, ecs.EventBus, entityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange);
        var removedSources = 0;
        ecs.EventBus.Subscribe<AuraSourceRemovedEvent>(removed => removedSources += removed.EntityId == entityId ? 1 : 0);
        Assert.AreEqual(entityId, world.GetEntityIdAt(new Vector3Int(11, 11, (int)MapLayer.Ground)), "Precondition: placed.");

        ecs.EntityManager.DestroyEntity(entityId);

        Assert.AreEqual(-1, world.GetEntityIdAt(position));
        Assert.AreEqual(-1, world.GetEntityIdAt(new Vector3Int(11, 11, (int)MapLayer.Ground)));
        Assert.IsFalse(resolver.Membership.IsIndexedAt(entityId, position));
        Assert.AreEqual(1, removedSources);
    }

    [TestMethod]
    public void EntityDestroying_IsRaisedWhileTheComponentsCanStillBeRead()
    {
        var (_, ecs, _) = Build();
        var entityId = ecs.EntityManager.CreateEntity();
        ecs.ComponentManager.Merge(entityId, new DisplayTextComponent("Goblin", "A goblin."));
        string? nameSeen = null;
        ecs.EntityManager.EntityDestroying += destroyed => nameSeen = ecs.ComponentManager.GetPackedPool<DisplayTextComponent>().GetReadonly(destroyed).Name;

        ecs.EntityManager.DestroyEntity(entityId);

        Assert.AreEqual("Goblin", nameSeen);
    }
}
