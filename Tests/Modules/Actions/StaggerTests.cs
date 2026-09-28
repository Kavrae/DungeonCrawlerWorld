using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Movement;
using Game.Modules.ProcessingTier;
using Game.World;

namespace Tests.Modules.Actions;

/// <summary>Drives ActionsModule's own EntityStaggeredEvent subscription, wired by Bootstrapper.Build the same way the game wires it.</summary>
[TestClass]
public sealed class StaggerTests
{
    private const uint LockedUntilFrame = 60;

    private static (EcsContext EcsContext, EventBus EventBus, int TargetEntityId) Build()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var context = new GameModuleContext(world, new MathUtility(), new EventBus()) { PlayerQuery = world, EntityMoveSync = new WorldEventSync(world) };

        var ecsContext = BuiltInTestModules.Build(context, 10, 10);

        var targetEntityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.ComponentManager.Merge(targetEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 60, unlockedAtFrame: LockedUntilFrame));
        return (ecsContext, context.EventBus, targetEntityId);
    }

    [TestMethod]
    public void Staggered_DuringAWindup_CancelsTheWindupAndKeepsTheLock()
    {
        var (ecsContext, eventBus, targetEntityId) = Build();
        var componentManager = ecsContext.ComponentManager;
        componentManager.Merge(targetEntityId, new PendingDelayedActionComponent(Guid.NewGuid(), [new Vector3Int(2, 2, 0)], LockedUntilFrame));

        eventBus.Publish(new EntityStaggeredEvent(targetEntityId, ActionSource.Admin));

        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(targetEntityId));
        Assert.AreEqual(LockedUntilFrame, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(targetEntityId).UnlockedAtFrame, "The time the windup already cost is lost, not given back.");
    }

    [TestMethod]
    public void Staggered_WithNoWindup_LeavesTheLockAlone()
    {
        var (ecsContext, eventBus, targetEntityId) = Build();

        eventBus.Publish(new EntityStaggeredEvent(targetEntityId, ActionSource.Admin));

        Assert.AreEqual(LockedUntilFrame, ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().GetReadonly(targetEntityId).UnlockedAtFrame);
    }
}
