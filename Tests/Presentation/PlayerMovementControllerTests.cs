using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Microsoft.Xna.Framework.Input;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class PlayerMovementControllerTests
{
    private const int PlayerEntityId = 1;
    private const int LockEndsAtFrame = 60;
    private static readonly Vector3Int PlayerPosition = new(5, 5, 0);
    private static readonly Vector3Int North = new(5, 4, 0);
    private static readonly Vector3Int East = new(6, 5, 0);

    private static readonly HashSet<Keys> NoClaimedKeys = [];

    private sealed class Harness
    {
        public required PlayerMovementController Controller { get; init; }
        public required SimulationClock Clock { get; init; }
        public required ComponentManager ComponentManager { get; init; }

        private KeyboardState _previous;

        public Vector3Int? NextMapPosition => ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(PlayerEntityId).NextMapPosition;

        public void Frame(long frame, params Keys[] held)
        {
            Clock.Advance(frame);
            var current = new KeyboardState(held);
            Controller.HandleInput(current, _previous, NoClaimedKeys);
            _previous = current;
        }

        public void SetNextMapPosition(Vector3Int? next) =>
            ComponentManager.GetPackedPool<MovementComponent>().TryUpdate(PlayerEntityId, next, static (ref MovementComponent m, Vector3Int? n) => m.NextMapPosition = n);
    }

    private static Harness Build(Vector3Int? playerPosition = null, bool locked = true)
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)));
        world.PlayerEntityId = PlayerEntityId;

        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));

        TestTransforms.Set(componentManager, PlayerEntityId, new TransformComponent(playerPosition ?? PlayerPosition, new Vector2Byte(1, 1)));
        componentManager.Merge(PlayerEntityId, new MovementComponent(MovementMode.PlayerControlled, null, null));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, unlockedAtFrame: locked ? LockEndsAtFrame : 0u));

        var clock = new SimulationClock();
        var playerCommands = new PlayerCommands(
            world,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            clock,
            TestActionStateViews.EntityActions(componentManager),
            new Engine.Events.EventBus());

        return new Harness { Controller = new PlayerMovementController(playerCommands), Clock = clock, ComponentManager = componentManager };
    }

    [TestMethod]
    public void FreshPress_WhileUnlocked_WritesStepTheSameFrame()
    {
        var harness = Build(locked: false);

        harness.Frame(10, Keys.D);

        Assert.AreEqual(East, harness.NextMapPosition);
    }

    [TestMethod]
    public void FreshPress_WhileLocked_WritesNothingUntilTheLockClears()
    {
        var harness = Build();

        harness.Frame(55, Keys.D);
        Assert.IsNull(harness.NextMapPosition);

        harness.Frame(LockEndsAtFrame);
        Assert.AreEqual(East, harness.NextMapPosition);
    }

    [TestMethod]
    public void RedirectDuringTheLock_ResolvesToTheNewestDirection()
    {
        var harness = Build();

        harness.Frame(50, Keys.W);
        harness.Frame(51);
        harness.Frame(55, Keys.D);
        harness.Frame(56);
        harness.Frame(LockEndsAtFrame);

        Assert.AreEqual(East, harness.NextMapPosition);
    }

    [TestMethod]
    public void Tap_ExpiresAfterTheBufferWindow()
    {
        var harness = Build();
        var pressFrame = LockEndsAtFrame - PlayerCommands.ExpiryFrames;

        harness.Frame(pressFrame, Keys.D);
        harness.Frame(pressFrame + 1);
        harness.Frame(LockEndsAtFrame);

        Assert.IsNull(harness.NextMapPosition);
    }

    [TestMethod]
    public void Tap_StillLiveOnTheLastFrameOfTheWindow_Flushes()
    {
        var harness = Build();
        var pressFrame = LockEndsAtFrame - PlayerCommands.ExpiryFrames + 1;

        harness.Frame(pressFrame, Keys.D);
        harness.Frame(pressFrame + 1);
        harness.Frame(LockEndsAtFrame);

        Assert.AreEqual(East, harness.NextMapPosition);
    }

    [TestMethod]
    public void HeldKey_StepsOnTheFrameTheLockClears_EvenAfterItsPressExpired()
    {
        var harness = Build();

        harness.Frame(10, Keys.W);
        harness.Frame(30, Keys.W);
        harness.Frame(LockEndsAtFrame - 1, Keys.W);
        Assert.IsNull(harness.NextMapPosition);

        harness.Frame(LockEndsAtFrame, Keys.W);
        Assert.AreEqual(North, harness.NextMapPosition);
    }

    [TestMethod]
    public void HeldKey_DoesNotOverwriteATapStillWaitingForMovementSystem()
    {
        var harness = Build();

        harness.Frame(50, Keys.W);
        harness.Frame(55, Keys.W, Keys.D);
        harness.Frame(56, Keys.W);
        harness.Frame(LockEndsAtFrame, Keys.W);
        harness.Frame(LockEndsAtFrame + 1, Keys.W);

        Assert.AreEqual(new Vector3Int(6, 4, 0), harness.NextMapPosition);
    }

    [TestMethod]
    public void BlockedNewestDirection_ClearsTheOlderStepInsteadOfFallingBack()
    {
        var edge = new Vector3Int(19, 5, 0);
        var harness = Build(playerPosition: edge, locked: false);
        harness.SetNextMapPosition(new Vector3Int(19, 4, 0));

        harness.Frame(10, Keys.D);

        Assert.IsNull(harness.NextMapPosition);
    }

    [TestMethod]
    public void OpposingFreshPress_CancelsTheBufferedStep()
    {
        var harness = Build();

        harness.Frame(50, Keys.D);
        harness.Frame(52, Keys.D, Keys.A);
        harness.Frame(LockEndsAtFrame);

        Assert.IsNull(harness.NextMapPosition);
    }
}
