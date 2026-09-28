using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Composites;
using Game.Bootstrap;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Game.Spawning;
using Game.World;
using Microsoft.Xna.Framework.Input;
using Presentation.UI;
using Game.Modules.AbilityScores.Components;

namespace Tests.Presentation;

/// <summary>A Dodge confirmed while a step's action lock is still counting down, driven through the real input path and the real bootstrapped systems.</summary>
[TestClass]
public sealed class DodgeDuringActionLockTests
{
    private static readonly DirectoryInfo EmptyModsDirectory = Directory.CreateTempSubdirectory();

    private static readonly Vector3Int Start = new(10, 10, (int)MapLayer.Ground);
    private static readonly Vector3Int North = new(10, 9, (int)MapLayer.Ground);
    private static readonly Vector3Int NorthEast = new(11, 9, (int)MapLayer.Ground);

    [ClassCleanup]
    public static void DeleteEmptyModsDirectory() => EmptyModsDirectory.Delete(recursive: true);

    private sealed class Harness
    {
        public required EcsContext Ecs { get; init; }
        public required int PlayerEntityId { get; init; }
        public required PlayerMovementController Movement { get; init; }
        public required ActionTargetingController ActionTargeting { get; init; }

        private long _frame;

        public Vector3Int Position => Ecs.ComponentManager.GetDirectPool<TransformComponent>().GetReadonly(PlayerEntityId).Position;

        public bool IsLocked => ActionLockGate.IsBlocked(Ecs.ComponentManager.GetPackedPool<ActionLockComponent>(), PlayerEntityId, _frame);

        public void Frame(Action? input = null)
        {
            input?.Invoke();
            _frame++;
            Ecs.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: _frame));
        }

        public void WaitUntilFree()
        {
            do
            {
                Frame();
            }
            while (IsLocked);
        }

        public void Move(Keys key) =>
            Movement.HandleInput(new KeyboardState(key), new KeyboardState(), new HashSet<Keys>());

        public void DirectionalDodge(Keys key)
        {
            ActionTargeting.HandleHotkeySlotPress(HotkeySlot.DefaultAttack);
            ActionTargeting.TryClaimDodgeDirectionalKey(new KeyboardState(key), new KeyboardState(), []);
        }
    }

    private static Harness Build()
    {
        var world = new Game.World.World(new Map(new Vector3Int(40, 40, 3)));
        var result = GameBootstrapper.Build(world, new MathUtility(new Random(1)), EmptyModsDirectory.FullName, initialEntityCapacity: 100, initialComponentCapacity: 50);
        var ecs = result.EcsContext;
        var components = ecs.ComponentManager;
        result.ProcessingTierResolver.SetReferencePosition(Start);

        var playerEntityId = ecs.EntityManager.CreateEntity();
        result.ProcessingTierResolver.PinLocalAndNotify(playerEntityId);
        result.Factory.Spawn(SpawnRequest.At(result.Definitions.GetId(Player.Id), Start) with { Seed = 1, ReservedEntityId = playerEntityId });
        world.PlayerEntityId = playerEntityId;

        var clock = ecs.SystemManager.Clock;
        var inputBuffer = new PlayerInputBuffer(
            world,
            components.GetDirectPool<TransformComponent>(),
            components.GetPackedPool<MovementComponent>(),
            components.GetPackedPool<ActionLockComponent>(),
            components.GetPackedPool<PendingActionActivationComponent>(),
            components.GetPackedPool<PendingConsumableActivationComponent>(),
            clock,
            ecs.EventBus);

        var actionTargeting = new ActionTargetingController(
            world,
            new MapViewState(),
            new MapCamera(world),
            new UiLayerStack(),
            result.ActionCatalog,
            result.ItemCatalog,
            components.GetDirectPool<TransformComponent>(),
            components.GetMultiPool<ActionHotkeyBindingComponent>(),
            components.GetMultiPool<ItemHotkeyBindingComponent>(),
            components.GetMultiPool<InventoryItemStackComponent>(),
            components.GetPackedPool<HotkeyExpansionUnlockComponent>(),
            components.GetPackedPool<PendingDelayedActionComponent>(),
            components.GetPackedPool<ActionLockComponent>(),
            inputBuffer,
            components.GetPackedPool<ManaComponent>(),
            components.GetPackedPool<AbilityScoresComponent>(),
            simulationClock: clock);

        return new Harness
        {
            Ecs = ecs,
            PlayerEntityId = playerEntityId,
            Movement = new PlayerMovementController(inputBuffer),
            ActionTargeting = actionTargeting,
        };
    }

    [TestMethod]
    public void MoveThenDirectionalDodge_OnConsecutiveFrames_TakesBothStepsWithoutWaitingForTheLock()
    {
        var harness = Build();
        harness.WaitUntilFree();

        harness.Frame(() => harness.Move(Keys.W));
        Assert.AreEqual(North, harness.Position, "Precondition: the ordinary step was taken.");
        Assert.IsTrue(harness.IsLocked, "Precondition: the ordinary step started the action lock.");

        harness.Frame(() => harness.DirectionalDodge(Keys.D));
        harness.Frame();

        Assert.AreEqual(NorthEast, harness.Position, "The Dodge's step must not wait for the lock the ordinary step started.");
        Assert.IsTrue(harness.Ecs.ComponentManager.GetPackedPool<DodgingComponent>().Has(harness.PlayerEntityId));
    }

    [TestMethod]
    public void DirectionalDodge_StillOnCooldown_DoesNotStepDuringTheLock()
    {
        var harness = Build();
        harness.WaitUntilFree();

        harness.Frame(() => harness.Move(Keys.W));
        harness.Frame(() => harness.DirectionalDodge(Keys.D));
        harness.Frame();
        Assert.AreEqual(NorthEast, harness.Position, "Precondition: the first Dodge stepped.");
        Assert.IsTrue(harness.IsLocked, "Precondition: the Dodge's step started a lock of its own.");

        harness.Frame(() => harness.DirectionalDodge(Keys.D));
        harness.Frame();

        Assert.AreEqual(NorthEast, harness.Position, "Dodge's own cooldown must still stop it being used as movement.");
        Assert.IsTrue(harness.IsLocked);
    }
}
