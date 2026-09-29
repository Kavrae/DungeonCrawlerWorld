using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;

namespace Tests.Diagnostics;

[TestClass]
[DoNotParallelize]
public sealed class DiagnosticsEngineSessionTests
{
    private static EcsContext CreateContext()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        return new EcsContext(new EntityManager(componentManager, initialCapacity: 10), componentManager, new SystemManager(), new EventBus());
    }

    [TestMethod]
    [DataRow(DiagnosticsFeatures.Memory)]
    [DataRow(DiagnosticsFeatures.LeakDetection)]
    public void Start_WithAPoolMeasuringFeature_ListensToSessions(DiagnosticsFeatures features)
    {
        using var engine = new DiagnosticsEngine(features);
        engine.Start();

        Assert.AreSame(engine, EngineHooks.Sessions.Listener);
    }

    [TestMethod]
    public void Start_WithoutAPoolMeasuringFeature_DoesNotListenToSessions()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.FrameBudget | DiagnosticsFeatures.Startup);
        engine.Start();

        Assert.IsNull(EngineHooks.Sessions.Listener);
    }

    [TestMethod]
    public void ASecondSession_AfterTheFirstEnds_Starts()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.Memory | DiagnosticsFeatures.LeakDetection);
        engine.Start();
        var firstSession = CreateContext();
        firstSession.BeginSession();
        firstSession.Dispose();

        using var secondSession = CreateContext();
        secondSession.BeginSession();
    }

    [TestMethod]
    public void ASecondSession_WhileTheFirstIsActive_Throws()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.Memory);
        engine.Start();
        using var firstSession = CreateContext();
        firstSession.BeginSession();

        using var secondSession = CreateContext();

        Assert.ThrowsExactly<InvalidOperationException>(secondSession.BeginSession);
    }
}
