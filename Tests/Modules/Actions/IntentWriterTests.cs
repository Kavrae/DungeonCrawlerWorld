using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;

namespace Tests.Modules.Actions;

/// <summary>The one way a turn is queued: what each write takes away, so no entity steps and acts on one frame.</summary>
[TestClass]
public sealed class IntentWriterTests
{
    private const int EntityId = 3;
    private const int StationaryEntityId = 4;

    private static readonly Guid ActionId = Guid.NewGuid();
    private static readonly Guid OtherActionId = Guid.NewGuid();
    private static readonly Vector3Int Here = new(5, 5, 0);
    private static readonly Vector3Int NextTile = new(6, 5, 0);

    private static (ComponentManager Components, IntentWriter Intents) Setup()
    {
        var components = BuiltInTestComponents.RegisterAll(new ComponentManager(10, 10));
        components.Merge(EntityId, new MovementComponent(null, NextTile));
        return (components, IntentWriter.For(components));
    }

    private static Vector3Int? StepOf(ComponentManager components, int entityId) =>
        components.GetPackedPool<MovementComponent>().GetReadonly(entityId).NextMapPosition;

    private static bool HasPendingAction(ComponentManager components, int entityId) =>
        components.GetPackedPool<PendingActionActivationComponent>().Has(entityId);

    private static bool HasPendingItem(ComponentManager components, int entityId) =>
        components.GetPackedPool<PendingItemActivationComponent>().Has(entityId);

    [TestMethod]
    public void QueuingAnAction_ClearsTheStep_AndWithdrawsAPendingItemActivation()
    {
        var (components, intents) = Setup();
        intents.QueueItemActivation(EntityId, stackInstanceId: 9, default);
        intents.Step(EntityId, NextTile);
        intents.QueueItemActivation(EntityId, stackInstanceId: 9, default);

        intents.QueueAction(EntityId, ActionId, default);

        Assert.IsNull(StepOf(components, EntityId));
        Assert.IsTrue(HasPendingAction(components, EntityId));
        Assert.IsFalse(HasPendingItem(components, EntityId));
    }

    [TestMethod]
    public void QueuingAnItemActivation_ClearsTheStep_WithdrawsAPendingAction_AndKeepsItsSlot()
    {
        var (components, intents) = Setup();
        intents.QueueAction(EntityId, ActionId, default);
        intents.Step(EntityId, NextTile);
        intents.QueueAction(EntityId, ActionId, default);

        intents.QueueItemActivation(EntityId, stackInstanceId: 9, default, HotkeySlot.Base2);

        Assert.IsNull(StepOf(components, EntityId));
        Assert.IsFalse(HasPendingAction(components, EntityId));
        var pending = components.GetPackedPool<PendingItemActivationComponent>().GetReadonly(EntityId);
        Assert.AreEqual(9u, pending.StackInstanceId);
        Assert.AreEqual(HotkeySlot.Base2, pending.ActivatedFromSlot);
    }

    [TestMethod]
    public void AStep_WithdrawsBothPendingActivations()
    {
        var (components, intents) = Setup();
        components.Merge(EntityId, new PendingActionActivationComponent(ActionId, default));
        components.Merge(EntityId, new PendingItemActivationComponent(9, default));

        intents.Step(EntityId, NextTile);

        Assert.AreEqual(NextTile, StepOf(components, EntityId));
        Assert.IsFalse(HasPendingAction(components, EntityId));
        Assert.IsFalse(HasPendingItem(components, EntityId));
    }

    [TestMethod]
    public void ANullStep_StandsTheEntityStill()
    {
        var (components, intents) = Setup();

        intents.Step(EntityId, null);

        Assert.IsNull(StepOf(components, EntityId));
    }

    [TestMethod]
    public void AStepFromAnActivation_SetsTheStep_AndLeavesPendingRequestsAlone()
    {
        var (components, intents) = Setup();
        components.Merge(EntityId, new PendingActionActivationComponent(ActionId, default));

        intents.StepFromActivation(EntityId, Here);

        Assert.AreEqual(Here, StepOf(components, EntityId));
        Assert.IsTrue(HasPendingAction(components, EntityId));
    }

    public void QueuingTwiceInAFrame_KeepsTheNewest()
    {
        var (components, intents) = Setup();

        intents.QueueAction(EntityId, ActionId, default);
        intents.QueueAction(EntityId, OtherActionId, default);

        Assert.AreEqual(OtherActionId, components.GetPackedPool<PendingActionActivationComponent>().GetReadonly(EntityId).ActionId);
    }

    [TestMethod]
    public void AnEntityThatCantMove_QueuesTheSameWay()
    {
        var (components, intents) = Setup();

        intents.QueueAction(StationaryEntityId, ActionId, default);

        Assert.IsTrue(HasPendingAction(components, StationaryEntityId));
        Assert.IsFalse(components.GetPackedPool<MovementComponent>().Has(StationaryEntityId));
    }
}
