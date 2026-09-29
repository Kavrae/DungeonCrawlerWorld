using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Bootstrap;
using Game.World;

namespace Tests.ECS.Context;

[TestClass]
[DoNotParallelize]
public sealed class EcsContextSessionTests
{
    private sealed class SessionRecorder : ISimulationSessionListener
    {
        public List<(string Hook, EcsContext Session)> Calls { get; } = [];

        public void SessionStarted(EcsContext session) => Calls.Add(("started", session));

        public void SessionEnding(EcsContext session) => Calls.Add(("ending", session));
    }

    private static EcsContext CreateContext()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        return new EcsContext(new EntityManager(componentManager, initialCapacity: 10), componentManager, new SystemManager(), new EventBus());
    }

    [TestMethod]
    public void BeginSession_ThenDispose_EmitsStartedThenEnding_ForThatContext()
    {
        var recorder = new SessionRecorder();
        using var sessionSubscription = EngineHooks.Sessions.Subscribe(recorder);
        var context = CreateContext();

        context.BeginSession();
        context.Dispose();

        CollectionAssert.AreEqual(new[] { ("started", context), ("ending", context) }, recorder.Calls);
    }

    [TestMethod]
    public void BeginSession_Twice_Throws()
    {
        using var context = CreateContext();
        context.BeginSession();

        Assert.ThrowsExactly<InvalidOperationException>(context.BeginSession);
    }

    [TestMethod]
    public void BeginSession_AfterDispose_Throws()
    {
        var context = CreateContext();
        context.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(context.BeginSession);
    }

    [TestMethod]
    public void Dispose_Twice_EmitsEndingOnce()
    {
        var recorder = new SessionRecorder();
        using var sessionSubscription = EngineHooks.Sessions.Subscribe(recorder);
        var context = CreateContext();
        context.BeginSession();

        context.Dispose();
        context.Dispose();

        Assert.AreEqual(1, recorder.Calls.Count(call => call.Hook == "ending"));
    }

    private sealed class RejectingSessionListener : ISimulationSessionListener
    {
        public int EndingCount { get; private set; }

        public void SessionStarted(EcsContext session) => throw new InvalidOperationException("rejected");

        public void SessionEnding(EcsContext session) => EndingCount++;
    }

    [TestMethod]
    public void Dispose_AfterAListenerRejectedTheStart_EmitsNoEnding()
    {
        var listener = new RejectingSessionListener();
        using var sessionSubscription = EngineHooks.Sessions.Subscribe(listener);
        var context = CreateContext();
        Assert.ThrowsExactly<InvalidOperationException>(context.BeginSession);

        context.Dispose();

        Assert.AreEqual(0, listener.EndingCount);
    }

    [TestMethod]
    public void Dispose_WithoutASession_EmitsNothing()
    {
        var recorder = new SessionRecorder();
        using var sessionSubscription = EngineHooks.Sessions.Subscribe(recorder);

        CreateContext().Dispose();

        Assert.IsEmpty(recorder.Calls);
    }

    [TestMethod]
    public void AGameBuild_BeginsNoSession()
    {
        var recorder = new SessionRecorder();
        using var sessionSubscription = EngineHooks.Sessions.Subscribe(recorder);

        using var ecsContext = GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(40, 40, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50).EcsContext;

        Assert.IsEmpty(recorder.Calls);
    }
}
