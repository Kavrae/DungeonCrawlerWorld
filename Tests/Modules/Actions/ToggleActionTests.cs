using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints.Composites;
using Game.Bootstrap;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Mana.Components;
using Game.Spawning;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI;

namespace Tests.Modules.Actions;

/// <summary>Toggle actions, and toggles with activation and periodic effects, in a real session.</summary>
[TestClass]
public sealed class ToggleActionTests
{
    private const byte AuraStrength = 16;
    private const uint LockedUntilFrame = 100_000;

    private static readonly Vector3Int PlayerPosition = new(10, 10, (int)MapLayer.Ground);
    private static readonly Vector3Int OtherPosition = new(30, 30, (int)MapLayer.Ground);

    private sealed class Harness
    {
        public required GameSession Session { get; init; }
        public required int PlayerEntityId { get; init; }

        public long CurrentFrame { get; private set; }

        public EcsContext Ecs => Session.EcsContext;
        public ComponentManager Components => Session.EcsContext.ComponentManager;
        public Toggles Toggles { get; init; } = null!;

        public void Frame(int count = 1)
        {
            for (var index = 0; index < count; index++)
            {
                CurrentFrame++;
                Ecs.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: CurrentFrame));
            }
        }

        /// <summary>Runs frames until condition holds, failing after maximumFrames.</summary>
        public void FrameUntil(Func<bool> condition, int maximumFrames = 300)
        {
            for (var index = 0; index < maximumFrames && !condition(); index++)
            {
                Frame();
            }

            Assert.IsTrue(condition(), $"Not reached within {maximumFrames} frames.");
        }

        public void QueueToxicAura() =>
            Components.Merge(PlayerEntityId, new PendingActionActivationComponent(ToxicAuraAction.Id, TestSelections.At(PlayerPosition)));

        public void QueueItem(uint stackInstanceId) =>
            Components.Merge(PlayerEntityId, new PendingItemActivationComponent(stackInstanceId, TestSelections.At(PlayerPosition)));

        public bool IsToxicAuraOn => Session.Views.ActionStateView.IsActionToggledOn(PlayerEntityId, ToxicAuraAction.Id);

        public int ActiveToggleCount(int entityId) => Components.GetMultiPool<ActiveToggleComponent>().CountForEntity(entityId);

        public int StrengthAt(Vector3Int position, AuraDefinition aura)
        {
            Assert.IsTrue(Session.Catalogs.Auras.TryGetId(aura.Id, out var auraId));
            return Session.Internals.AuraField.GetTotalPowerAt(position, auraId);
        }

        public float Mana => Components.GetPackedPool<ManaComponent>().GetReadonly(PlayerEntityId).CurrentMana;

        public void SetMana(float mana) =>
            Components.GetPackedPool<ManaComponent>().TryUpdate(PlayerEntityId, mana, static (ref ManaComponent component, float value) =>
            {
                component.MaximumMana = MathF.Max(component.MaximumMana, value);
                component.CurrentMana = value;
            });

        public void SetCurrentMana(float mana) =>
            Components.GetPackedPool<ManaComponent>().TryUpdate(PlayerEntityId, mana, static (ref ManaComponent component, float value) => component.CurrentMana = value);

        public void LockPlayer() =>
            Components.GetPackedPool<ActionLockComponent>().TryUpdate(PlayerEntityId, static (ref ActionLockComponent actionLock) => actionLock.UnlockedAtFrame = LockedUntilFrame);

        public bool IsPlayerLocked => ActionLockGate.IsBlocked(Components.GetPackedPool<ActionLockComponent>(), PlayerEntityId, CurrentFrame);

        public uint LockDeadline => Components.GetPackedPool<ActionLockComponent>().GetReadonly(PlayerEntityId).UnlockedAtFrame;

        public List<InventoryItemStackComponent> IdolStacks(int entityId)
        {
            var stacks = new List<InventoryItemStackComponent>();
            InventoryQueries.CopyStacksForEntity(Components.GetMultiPool<InventoryItemStackComponent>(), entityId, stacks);
            return stacks.Where(stack => stack.ItemDefinitionId == ToxicIdol.Id).ToList();
        }

        public int LitCount(int entityId) =>
            IdolStacks(entityId).Where(stack => stack.Override?.Activator is ToggleItemActivator { IsToggledOn: true }).Sum(stack => stack.Quantity);

        public uint LitStackId(int entityId) =>
            IdolStacks(entityId).Single(stack => stack.Override?.Activator is ToggleItemActivator { IsToggledOn: true }).StackInstanceId;

        public int CreatePlacedEntity(Vector3Int position)
        {
            var entityId = Session.Internals.ProcessingTierResolver.CreateEntityAt(Ecs.EntityManager, position);
            Components.Merge(entityId, new TransformComponent(TransformComponent.UnplacedOn(MapLayer.Ground), new Vector2Byte(1, 1)));
            Session.World.PlaceEntityOnMap(entityId, position, ref Components.GetDirectPool<TransformComponent>().Get(entityId));
            return entityId;
        }

        public void Kill(int entityId)
        {
            Ecs.EventBus.Publish(new EntityDiedEvent(entityId, TestSources.Entity(PlayerEntityId)));
            Frame();
            Assert.IsTrue(Components.GetPackedPool<DeadComponent>().Has(entityId));
        }
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
        harness.SetMana(50);
        return harness;
    }

    /// <summary>A unit of a Toxic Idol variant with periodic effects, added to entityId unlit. FreeCast, so a test about its upkeep isn't also waiting out a windup.</summary>
    private static uint AddIdolWithUpkeep(Harness harness, int entityId, ushort manaPerInterval, ushort intervalFrames, IReadOnlyList<Effect>? activationEffects = null)
    {
        var variant = ToxicIdol.Build() with
        {
            Activator = new ToggleItemActivator(new ActionTiming(ActionTimingCategory.FreeCast)),
            Toggle = new ToggleSpec(new TogglePeriodicEffects([new Effect([new ManaDrain(manaPerInterval)])], intervalFrames)),
            ActivationEffects = activationEffects ?? [],
        };
        return InventoryActions.AddDivergentItem(harness.Components, entityId, variant);
    }

    [TestMethod]
    public void ToxicAura_TurnsOnWhileActionLocked_HoldsItsAura_AndLeavesTheLockAlone()
    {
        var harness = Build();
        harness.LockPlayer();

        harness.QueueToxicAura();
        harness.Frame();

        Assert.IsTrue(harness.IsToxicAuraOn);
        Assert.AreEqual(AuraStrength, harness.StrengthAt(PlayerPosition, ToxicAuraAction.Aura));
        Assert.AreEqual(LockedUntilFrame, harness.LockDeadline);
    }

    [TestMethod]
    public void ToxicAura_CooldownStartsBothWays()
    {
        var harness = Build();
        harness.QueueToxicAura();
        harness.Frame();
        Assert.IsTrue(harness.IsToxicAuraOn);

        harness.QueueToxicAura();
        harness.Frame();
        Assert.IsTrue(harness.IsToxicAuraOn, "Still on its cooldown from turning on.");

        harness.Frame(60);
        harness.QueueToxicAura();
        harness.Frame();
        Assert.IsFalse(harness.IsToxicAuraOn);
        Assert.AreEqual(0, harness.StrengthAt(PlayerPosition, ToxicAuraAction.Aura));

        harness.QueueToxicAura();
        harness.Frame();
        Assert.IsFalse(harness.IsToxicAuraOn, "Still on its cooldown from turning off.");
    }

    [TestMethod]
    public void ToxicAura_DrainsManaWhileOn()
    {
        var harness = Build();
        var withoutAura = Build();
        harness.SetCurrentMana(20);
        withoutAura.SetCurrentMana(20);
        harness.QueueToxicAura();

        harness.Frame(200);
        withoutAura.Frame(200);

        Assert.IsTrue(harness.IsToxicAuraOn);
        Assert.AreEqual(3f, withoutAura.Mana - harness.Mana, 0.01f, "Three whole seconds on, one mana each, against the same player without the aura, neither reaching its maximum.");
    }

    [TestMethod]
    public void ToxicAuraAndALitIdol_AreIndependent_AndEachOffRemovesOnlyItsOwn()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        harness.QueueToxicAura();
        harness.Frame();
        harness.FrameUntil(() => !harness.IsPlayerLocked);
        harness.QueueItem(harness.IdolStacks(player).Single().StackInstanceId);
        harness.FrameUntil(() => harness.LitCount(player) == 1);

        Assert.AreEqual(2, harness.ActiveToggleCount(player));
        Assert.AreEqual(AuraStrength, harness.StrengthAt(PlayerPosition, ToxicAuraAction.Aura));
        Assert.AreEqual(AuraStrength, harness.StrengthAt(PlayerPosition, ToxicIdol.Aura));

        harness.FrameUntil(() => !harness.IsPlayerLocked);
        harness.QueueItem(harness.LitStackId(player));
        harness.Frame();

        Assert.IsTrue(harness.IsToxicAuraOn);
        Assert.AreEqual(AuraStrength, harness.StrengthAt(PlayerPosition, ToxicAuraAction.Aura));
        Assert.AreEqual(0, harness.StrengthAt(PlayerPosition, ToxicIdol.Aura));
    }

    [TestMethod]
    public void HolderDiesWithBothOn_TheActionsToggleAndSourceGo_AndTheIdolsStay()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        harness.QueueToxicAura();
        harness.Frame();
        harness.FrameUntil(() => !harness.IsPlayerLocked);
        harness.QueueItem(harness.IdolStacks(player).Single().StackInstanceId);
        harness.FrameUntil(() => harness.LitCount(player) == 1);

        harness.Kill(player);

        Assert.IsFalse(harness.IsToxicAuraOn);
        Assert.AreEqual(0, harness.StrengthAt(PlayerPosition, ToxicAuraAction.Aura));
        Assert.AreEqual(1, harness.ActiveToggleCount(player));
        Assert.AreEqual(AuraStrength, harness.StrengthAt(PlayerPosition, ToxicIdol.Aura));
        Assert.AreEqual(1, harness.LitCount(player));
    }

    [TestMethod]
    public void LitItemWhoseUpkeepCannotBeMet_GoesOutAfterOneInterval_WithItsUnitBackUnlit()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var stackId = AddIdolWithUpkeep(harness, player, manaPerInterval: 1000, intervalFrames: 30);

        harness.QueueItem(stackId);
        harness.Frame();
        Assert.AreEqual(1, harness.LitCount(player));

        harness.Frame(28);
        Assert.AreEqual(1, harness.LitCount(player), "Lit for the whole first interval.");

        harness.Frame(3);
        Assert.AreEqual(0, harness.LitCount(player));
        Assert.AreEqual(0, harness.ActiveToggleCount(player));
        Assert.AreEqual(0, harness.StrengthAt(PlayerPosition, ToxicIdol.Aura));
        Assert.AreEqual(2, harness.IdolStacks(player).Sum(stack => stack.Quantity), "No unit is lost.");
    }

    [TestMethod]
    public void HolderDies_ALitItemWithPeriodicEffects_GoesOutAtOnce()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var stackId = AddIdolWithUpkeep(harness, player, manaPerInterval: 0, intervalFrames: 6000);
        harness.QueueItem(stackId);
        harness.Frame();
        Assert.AreEqual(1, harness.LitCount(player));

        harness.Kill(player);

        Assert.AreEqual(0, harness.LitCount(player));
        Assert.AreEqual(0, harness.ActiveToggleCount(player));
    }

    [TestMethod]
    public void LitItemWithPeriodicEffects_GivenToACorpse_ArrivesAndGoesOut()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var corpse = harness.CreatePlacedEntity(OtherPosition);
        harness.Components.Merge(corpse, new DeadComponent(TestSources.Entity(player), DiedAtFrame: 0));
        var stackId = AddIdolWithUpkeep(harness, player, manaPerInterval: 0, intervalFrames: 6000);
        harness.QueueItem(stackId);
        harness.Frame();

        Assert.IsTrue(harness.Session.Commands.InventoryCommands.TryTransferStack(player, corpse, harness.LitStackId(player)));
        harness.Frame(2);

        Assert.AreEqual(0, harness.LitCount(corpse));
        Assert.AreEqual(0, harness.ActiveToggleCount(corpse));
        Assert.AreEqual(1, harness.IdolStacks(corpse).Sum(stack => stack.Quantity));
        Assert.AreEqual(0, harness.StrengthAt(OtherPosition, ToxicIdol.Aura));
    }

    [TestMethod]
    public void LitItemWithPeriodicEffects_StagedOnAHolderOffTheMap_IsNotTickedAndStaysLit()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var offMapHolder = harness.Ecs.EntityManager.CreateEntity();
        var stackId = AddIdolWithUpkeep(harness, player, manaPerInterval: 0, intervalFrames: 30);
        harness.QueueItem(stackId);
        harness.Frame();

        Assert.IsTrue(harness.Session.Commands.InventoryCommands.TryTransferStack(player, offMapHolder, harness.LitStackId(player)));
        harness.Frame(100);

        Assert.AreEqual(1, harness.LitCount(offMapHolder), "A holder with no mana pool couldn't pay, but nothing is asked of a holder off the map.");
        Assert.AreEqual(1, harness.ActiveToggleCount(offMapHolder));
    }

    [TestMethod]
    public void ItemActivationEffects_BlockTheTurnOnWhenTheyCannotApply_AndAreAppliedWhenTheyCan()
    {
        var harness = Build();
        var player = harness.PlayerEntityId;
        var unaffordableStackId = AddIdolWithUpkeep(harness, player, manaPerInterval: 0, intervalFrames: 6000, activationEffects: [new Effect([new ManaDrain(1000)])]);

        Assert.AreEqual(ActivationBlocker.NotEnoughMana, harness.Session.Views.ActionStateView.GetItemBlocker(player, unaffordableStackId));

        var manaBefore = harness.Mana;
        harness.QueueItem(unaffordableStackId);
        harness.Frame();
        Assert.AreEqual(0, harness.LitCount(player));
        Assert.IsGreaterThanOrEqualTo(manaBefore, harness.Mana, "Nothing is taken for a turn-on that was blocked.");

        var affordable = Build();
        var affordableStackId = AddIdolWithUpkeep(affordable, affordable.PlayerEntityId, manaPerInterval: 0, intervalFrames: 6000, activationEffects: [new Effect([new ManaDrain(20)])]);
        Assert.AreEqual(ActivationBlocker.None, affordable.Session.Views.ActionStateView.GetItemBlocker(affordable.PlayerEntityId, affordableStackId));

        affordable.QueueItem(affordableStackId);
        affordable.Frame();

        Assert.AreEqual(1, affordable.LitCount(affordable.PlayerEntityId));
        Assert.IsLessThan(31f, affordable.Mana);

        var manaWhileLit = affordable.Mana;
        affordable.QueueItem(affordable.LitStackId(affordable.PlayerEntityId));
        affordable.Frame();
        Assert.AreEqual(0, affordable.LitCount(affordable.PlayerEntityId));
        Assert.IsGreaterThanOrEqualTo(manaWhileLit, affordable.Mana, "Turning off takes nothing.");
    }

    private sealed class RegisteringModule(Action<GameModuleContext> configure) : IGameModule
    {
        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void Configure(GameModuleContext context) => configure(context);

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    [TestMethod]
    public void ToggleActionThatIsNotSelfTargeted_FailsTheBuild()
    {
        var action = new ActionDefinition(Guid.NewGuid(), "Thrown Stance", null, "?", Color.White, [], Effects: [],
            Activator: new DirectAction(new TargetingSpec(TargetShape.SingleTarget, Range: 3), new ActionTiming(ActionTimingCategory.FreeCast)),
            Toggle: ToggleSpec.HoldsEffectsOnly);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInTestModules.BuildModules([new RegisteringModule(context => context.Actions.Register(action))]));

        Assert.Contains("Action 'Thrown Stance'", exception.Message);
        Assert.Contains("Self", exception.Message);
    }
}
