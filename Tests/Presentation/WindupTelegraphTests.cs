using Engine.ECS.Entities;
using Engine.Math;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;

namespace Tests.Presentation;

/// <summary>The windup telegraph is drawn from TargetResolution each frame, so a Target-mode windup's tiles follow its target.</summary>
[TestClass]
public sealed class WindupTelegraphTests
{
    private const int CasterEntityId = 5;
    private const int TargetEntityId = 6;

    private static readonly Vector3Int PlayerPosition = new(100, 100, 0);
    private static readonly Vector3Int CasterPosition = new(110, 110, 0);
    private static readonly Vector3Int TargetPosition = new(114, 110, 0);
    private static readonly Guid BurstActionId = new("00000000-0000-0000-0000-00000000b057");

    [TestMethod]
    public void AnotherEntitysTargetModeWindup_IsTelegraphedWhereItsTargetIsNow()
    {
        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(BurstActionId, "Test Burst", null, "*", default, [], Effects: [Effect.None],
            Activator: new DirectAction(new TargetingSpec(TargetShape.Burst, Range: 10, AreaSize: 1), new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: 60))));
        var harness = TestMapWindows.Create(300, 300, 1, PlayerPosition, actionCatalog);
        var world = harness.World;
        var components = harness.ComponentManager;
        components.Merge(CasterEntityId, new ActionInstanceComponent(BurstActionId, overrideDefinition: null));
        TestTransforms.Set(components, CasterEntityId, new TransformComponent(CasterPosition, new Vector2Byte(1, 1)));
        var targetTransform = new TransformComponent(TargetPosition, new Vector2Byte(1, 1));
        TestTransforms.Set(components, TargetEntityId, targetTransform);
        world.PlaceEntityOnMap(TargetEntityId, TargetPosition, ref targetTransform);
        var targetKey = world.EntityKeys.Issue(TargetEntityId);
        components.Merge(CasterEntityId, PendingWindupComponent.ForAction(BurstActionId, new TargetSelection(TargetingMode.Target, TargetPosition, targetKey, default, Range: 10, AreaSize: 1), readyAtFrame: 60));

        var moved = new Vector3Int(TargetPosition.X, TargetPosition.Y + 3, 0);
        world.MoveEntity(TargetEntityId, moved, targetTransform);
        TestTransforms.Set(components, TargetEntityId, targetTransform with { Position = moved });

        var telegraph = harness.ActionTargetingController.AllPendingWindupTargets().Single(windup => windup.EntityId == CasterEntityId);

        Assert.HasCount(5, telegraph.TargetTiles);
        CollectionAssert.Contains(telegraph.TargetTiles.ToList(), moved);
        CollectionAssert.DoesNotContain(telegraph.TargetTiles.ToList(), TargetPosition);
    }
}
