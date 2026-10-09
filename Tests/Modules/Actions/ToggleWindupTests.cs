using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Composites;
using Game.Bootstrap;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Mana.Components;
using Game.Spawning;
using Game.World;

namespace Tests.Modules.Actions;

/// <summary>Delayed toggles share the action windup: they go on only when it resolves, and every way a windup ends early turns nothing on.</summary>
[TestClass]
public sealed class ToggleWindupTests
{
    private static readonly Vector3Int PlayerPosition = new(10, 10, (int)MapLayer.Ground);
    private static readonly Vector3Int OtherPosition = new(30, 30, (int)MapLayer.Ground);

    private sealed class Harness
    {
        public required GameSession Session { get; init; }
        public required int PlayerEntityId { get; init; }

        public long CurrentFrame { get; private set; }

        public EcsContext Ecs => Session.EcsContext;
        public ComponentManager Components => Session.EcsContext.ComponentManager;
        public Engine.ECS.Components.Stores.PackedComponentPool<PendingWindupComponent> Windups => Components.GetPackedPool<PendingWindupComponent>();

        public void Frame(int count = 1)
        {
            for (var index = 0; index < count; index++)
            {
                CurrentFrame++;
                Ecs.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: CurrentFrame));
            }
        }

        public void FrameUntil(Func<bool> condition, int maximumFrames = 300)
        {
            for (var index = 0; index < maximumFrames && !condition(); index++)
            {
                Frame();
            }

            Assert.IsTrue(condition(), $"Not reached within {maximumFrames} frames.");
        }

        public bool IsPlayerLocked => ActionLockGate.IsBlocked(Components.GetPackedPool<ActionLockComponent>(), PlayerEntityId, CurrentFrame);

        public void WaitUntilFree() => FrameUntil(() => !IsPlayerLocked);

        public List<InventoryItemStackComponent> IdolStacks(int entityId)
        {
            var stacks = new List<InventoryItemStackComponent>();
            InventoryQueries.CopyStacksForEntity(Components.GetMultiPool<InventoryItemStackComponent>(), entityId, stacks);
            return stacks.Where(stack => stack.ItemDefinitionId == ToxicIdol.Id).ToList();
        }

        public int LitCount(int entityId) =>
            IdolStacks(entityId).Where(stack => stack.Override?.Activator is ToggleItemActivator { IsToggledOn: true }).Sum(stack => stack.Quantity);

        public uint IdolStackId => IdolStacks(PlayerEntityId).Single().StackInstanceId;

        public void QueueItem(uint stackInstanceId) =>
            Components.Merge(PlayerEntityId, new PendingItemActivationComponent(stackInstanceId, TestSelections.At(PlayerPosition)));

        public void QueueAction(Guid actionId) =>
            Components.Merge(PlayerEntityId, new PendingActionActivationComponent(actionId, TestSelections.At(PlayerPosition)));

        public void RunPastAWindup() => Frame(StandardActionLockFrames.MaximumLockFrames + 5);

        public float Mana => Components.GetPackedPool<ManaComponent>().GetReadonly(PlayerEntityId).CurrentMana;

        public bool IsToggleActionOn(Guid actionId) => Session.Views.ActionStateView.IsActionToggledOn(PlayerEntityId, actionId);
    }

    private static Harness Build()
    {
        var map = new Map(new Vector3Int(40, 40, 3));
        var session = GameBootstrapper.Build(ValidatedMods.None, map, new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50);
        session.Internals.ProcessingTierResolver.SetReferencePosition(PlayerPosition);

        var playerEntityId = session.EcsContext.EntityManager.CreateEntity();
        session.Internals.ProcessingTierResolver.PinLocalAndNotify(playerEntityId);
        session.Internals.Factory.Spawn(SpawnRequest.At(session.Catalogs.Definitions.GetId(Player.Id), PlayerPosition) with { Seed = 1, ReservedEntityId = playerEntityId });
        session.World.PlayerEntityId = playerEntityId;

        var harness = new Harness { Session = session, PlayerEntityId = playerEntityId };
        harness.WaitUntilFree();
        return harness;
    }

    [TestMethod]
    public void DelayedIdol_WindsUpAsAnItemWindup_AndIsLitWhenItResolves()
    {
        var harness = Build();
        var stackId = harness.IdolStackId;

        harness.QueueItem(stackId);
        harness.Frame();

        Assert.AreEqual(0, harness.LitCount(harness.PlayerEntityId));
        Assert.IsTrue(harness.IsPlayerLocked);
        var windup = harness.Windups.GetReadonly(harness.PlayerEntityId);
        Assert.AreEqual(ActivatableReference.ItemStack(stackId), windup.Activatable);

        harness.FrameUntil(() => harness.LitCount(harness.PlayerEntityId) == 1);

        Assert.IsFalse(harness.Windups.Has(harness.PlayerEntityId));
        Assert.IsFalse(harness.IsPlayerLocked);
    }

    [TestMethod]
    public void DelayedIdol_IsTurnedOffAtOnce()
    {
        var harness = Build();
        harness.QueueItem(harness.IdolStackId);
        harness.FrameUntil(() => harness.LitCount(harness.PlayerEntityId) == 1);
        var litStackId = harness.IdolStacks(harness.PlayerEntityId).Single().StackInstanceId;

        harness.QueueItem(litStackId);
        harness.Frame();

        Assert.AreEqual(0, harness.LitCount(harness.PlayerEntityId));
        Assert.IsFalse(harness.Windups.Has(harness.PlayerEntityId));
    }

    [TestMethod]
    public void DelayedIdol_ItsWindupShowsTheChargeTelegraph()
    {
        var harness = Build();
        var mapView = harness.Session.Views.MapView;

        harness.QueueItem(harness.IdolStackId);
        harness.Frame(5);

        Assert.IsTrue(mapView.TryGetChargingAction(harness.PlayerEntityId, out var charging));
        Assert.AreEqual(ToxicIdol.Build().Glyph, charging.Glyph);
        Assert.IsGreaterThan(0f, mapView.GetChargeFraction(harness.PlayerEntityId));
    }

    [TestMethod]
    public void CancellingTheWindup_LightsNothing_AndGivesTheTimeBack()
    {
        var harness = Build();
        harness.QueueItem(harness.IdolStackId);
        harness.Frame(3);

        Assert.IsTrue(harness.Session.Commands.PlayerCommands.TryCancelWindup(harness.CurrentFrame));
        Assert.IsFalse(harness.IsPlayerLocked);

        harness.RunPastAWindup();
        Assert.AreEqual(0, harness.LitCount(harness.PlayerEntityId));
    }

    [TestMethod]
    public void DyingMidWindup_LightsNothing()
    {
        var harness = Build();
        harness.QueueItem(harness.IdolStackId);
        harness.Frame(3);

        harness.Ecs.EventBus.Publish(new EntityDiedEvent(harness.PlayerEntityId, TestSources.Entity(harness.PlayerEntityId)));
        harness.RunPastAWindup();

        Assert.IsTrue(harness.Components.GetPackedPool<DeadComponent>().Has(harness.PlayerEntityId));
        Assert.AreEqual(0, harness.LitCount(harness.PlayerEntityId));
    }

    [TestMethod]
    public void TradingTheIdolAwayMidWindup_LightsNothingOnEitherHolder()
    {
        var harness = Build();
        var receiver = harness.Session.Internals.ProcessingTierResolver.CreateEntityAt(harness.Ecs.EntityManager, OtherPosition);
        var stackId = harness.IdolStackId;
        harness.QueueItem(stackId);
        harness.Frame(3);

        Assert.IsTrue(harness.Session.Commands.InventoryCommands.TryTransferStack(harness.PlayerEntityId, receiver, stackId));
        harness.RunPastAWindup();

        Assert.AreEqual(0, harness.LitCount(harness.PlayerEntityId));
        Assert.AreEqual(0, harness.LitCount(receiver));
        Assert.AreEqual(1, harness.IdolStacks(receiver).Sum(stack => stack.Quantity));
    }

    [TestMethod]
    public void DodgingDuringTheWindup_CancelsIt_AndLightsNothing()
    {
        var harness = Build();
        harness.QueueItem(harness.IdolStackId);
        harness.Frame(3);

        harness.QueueAction(DodgeAction.Id);
        harness.Frame();

        Assert.IsFalse(harness.Windups.Has(harness.PlayerEntityId));
        Assert.IsFalse(harness.IsPlayerLocked);

        harness.RunPastAWindup();
        Assert.AreEqual(0, harness.LitCount(harness.PlayerEntityId));
    }

    [TestMethod]
    public void DelayedToggleAction_GoesOnOnlyWhenItsWindupResolves_AndPaysItsActivationEffectsWhenTheWindupStarts()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var delayedAura = ToxicAuraAction.Build() with
        {
            Activator = new DirectAction(new TargetingSpec(TargetShape.Self, Range: 0), new ActionTiming(ActionTimingCategory.Delayed)),
            Toggle = ToggleSpec.HoldsEffectsOnly,
            ActivationEffects = [new Effect([new ManaDrain(2)])],
        };
        ActionGrantEffects.Grant(harness.Components, harness.Session.Catalogs.ActionCatalog, player, ToxicAuraAction.Id, overrideDefinition: delayedAura);
        harness.Components.GetPackedPool<ManaComponent>().TryUpdate(player, static (ref ManaComponent mana) =>
        {
            mana.MaximumMana = 100;
            mana.CurrentMana = 50;
        });

        harness.QueueAction(ToxicAuraAction.Id);
        harness.Frame();
        var manaAtWindupStart = harness.Mana;

        Assert.IsFalse(harness.IsToggleActionOn(ToxicAuraAction.Id));
        Assert.AreEqual(ActivatableReference.Action(ToxicAuraAction.Id), harness.Windups.GetReadonly(player).Activatable);
        Assert.IsLessThan(49f, manaAtWindupStart, "Paid when the windup starts.");

        harness.FrameUntil(() => harness.IsToggleActionOn(ToxicAuraAction.Id));
        Assert.IsGreaterThanOrEqualTo(manaAtWindupStart, harness.Mana, "Nothing more is taken when it goes on.");

        harness.Frame(60);
        harness.QueueAction(ToxicAuraAction.Id);
        harness.Frame();
        Assert.IsFalse(harness.IsToggleActionOn(ToxicAuraAction.Id), "Turning off is at once.");
        Assert.IsFalse(harness.Windups.Has(player));
    }

    [TestMethod]
    public void DelayedToggleAction_CancelledMidWindup_StaysOff_AndKeepsWhatItPaid()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var delayedAura = ToxicAuraAction.Build() with
        {
            Activator = new DirectAction(new TargetingSpec(TargetShape.Self, Range: 0), new ActionTiming(ActionTimingCategory.Delayed)),
            Toggle = ToggleSpec.HoldsEffectsOnly,
            ActivationEffects = [new Effect([new ManaDrain(10)])],
        };
        ActionGrantEffects.Grant(harness.Components, harness.Session.Catalogs.ActionCatalog, player, ToxicAuraAction.Id, overrideDefinition: delayedAura);
        harness.Components.GetPackedPool<ManaComponent>().TryUpdate(player, static (ref ManaComponent mana) =>
        {
            mana.MaximumMana = 100;
            mana.CurrentMana = 50;
        });

        harness.QueueAction(ToxicAuraAction.Id);
        harness.Frame();
        Assert.IsTrue(harness.Session.Commands.PlayerCommands.TryCancelWindup(harness.CurrentFrame));
        var manaAfterCancel = harness.Mana;

        harness.RunPastAWindup();

        Assert.IsFalse(harness.IsToggleActionOn(ToxicAuraAction.Id));
        Assert.IsLessThan(41f, manaAfterCancel, "The activation effects are not given back.");
    }
}
