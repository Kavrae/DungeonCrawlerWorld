using Engine.ECS.Systems;
using Engine.Math;
using Game.Bootstrap;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.World;

namespace Tests.Bootstrap;

/// <summary>The key table systems record sources with is the one the EntityManager issues into, wired by the real bootstrapper.</summary>
[TestClass]
public sealed class EntityKeyWiringTests
{
    [TestMethod]
    public void AnActionsDamageSource_HoldsTheKeyTheEntityManagerIssuedItsCaster()
    {
        var map = new Map(new Vector3Int(40, 40, 3));
        var result = GameBootstrapper.Build(ValidatedMods.None, map, new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50);
        var world = result.World;
        var ecs = result.EcsContext;
        result.ProcessingTierResolver.SetReferencePosition(new Vector3Int(10, 10, (int)MapLayer.Ground));

        var casterId = PlaceAt(world, ecs, result, new Vector3Int(10, 10, (int)MapLayer.Ground));
        world.PlayerEntityId = casterId;
        var targetPosition = new Vector3Int(11, 10, (int)MapLayer.Ground);
        var targetId = PlaceAt(world, ecs, result, targetPosition);
        ecs.ComponentManager.Merge(targetId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        ecs.ComponentManager.Merge(casterId, new ActionLockComponent(standardLockFrames: 30, currentLockTotalFrames: 0, unlockedAtFrame: 0));
        ecs.ComponentManager.Merge(casterId, new ActionInstanceComponent(QuickAttackAction.Id, overrideDefinition: null));
        ecs.ComponentManager.Merge(casterId, new PendingActionActivationComponent(QuickAttackAction.Id, [targetPosition]));
        ActionSource? damageSource = null;
        ecs.EventBus.Subscribe<EntityDamagedEvent>(damaged => damageSource = damaged.EntityId == targetId ? damaged.Source : damageSource);

        ecs.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: 1));

        Assert.IsNotNull(damageSource, "Precondition: the attack landed.");
        Assert.IsFalse(damageSource.Value.Key.IsNone);
        Assert.AreEqual(ecs.EntityManager.Keys.GetKey(casterId), damageSource.Value.Key);
    }

    private static int PlaceAt(Game.World.World world, Engine.ECS.Context.EcsContext ecs, GameBootstrapResult result, Vector3Int position)
    {
        var entityId = result.ProcessingTierResolver.CreateEntityAt(ecs.EntityManager, position);
        ecs.ComponentManager.Merge(entityId, new TransformComponent(TransformComponent.UnplacedOn(MapLayer.Ground), new Vector2Byte(1, 1)));
        world.PlaceEntityOnMap(entityId, position, ref ecs.ComponentManager.GetDirectPool<TransformComponent>().Get(entityId));
        return entityId;
    }
}
