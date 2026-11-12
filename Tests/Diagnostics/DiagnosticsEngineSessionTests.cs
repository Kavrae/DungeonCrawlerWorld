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
    [DataRow(DiagnosticsFeatures.Gauges)]
    public void Start_WithASessionMeasuringFeature_ListensToSessions(DiagnosticsFeatures features)
    {
        using var engine = new DiagnosticsEngine(features);
        engine.Start();

        Assert.AreSame(engine, EngineHooks.Sessions.Listener);
    }

    [TestMethod]
    public void Start_WithoutASessionMeasuringFeature_DoesNotListenToSessions()
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

    private static void RunFrames(EcsContext context, int frameCount)
    {
        for (var frame = 1; frame <= frameCount; frame++)
        {
            context.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: frame));
        }
    }

    private static int IndexOfGauge(GaugeTracker tracker, string groupName, string gaugeName)
    {
        for (var gaugeIndex = 0; gaugeIndex < tracker.Gauges.Count; gaugeIndex++)
        {
            if (tracker.Gauges[gaugeIndex].GroupName == groupName && tracker.Gauges[gaugeIndex].GaugeName == gaugeName)
            {
                return gaugeIndex;
            }
        }

        return -1;
    }

    [TestMethod]
    public void Gauges_DuringASession_SamplesTheProcessAndSessionGaugesEachFrame()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.Gauges, writesPeriodicReports: false);
        engine.Start();
        using var session = CreateContext();
        var queueDepth = 0d;
        session.Gauges.Register("Test", "QueueDepth", GaugeKind.Level, () => queueDepth);
        session.BeginSession();

        queueDepth = 3;
        RunFrames(session, 1);

        var tracker = engine.Gauges!;
        Assert.AreEqual(3d, tracker.GetLatestValue(IndexOfGauge(tracker, "Test", "QueueDepth")));
        Assert.IsGreaterThanOrEqualTo(0, IndexOfGauge(tracker, "Process", "Gen1Collections"));
        Assert.IsGreaterThanOrEqualTo(0, IndexOfGauge(tracker, "Entities", "Living"));
    }

    private sealed class AllocatingSystem : ISystem
    {
        public int BytesPerFrame { get; set; }

        public byte[]? LastAllocation { get; private set; }

        public byte StripeCount => 1;

        public void Update(EngineTime time, byte stripeIndex) =>
            LastAllocation = BytesPerFrame > 0 ? new byte[BytesPerFrame] : null;
    }

    [TestMethod]
    public void Gauges_FrameAllocatedBytes_CountsWhatTheFrameAllocates_NotWhatHappensBetweenFrames()
    {
        const int OneMegabyte = 1 << 20;
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.Gauges, writesPeriodicReports: false);
        engine.Start();
        using var session = CreateContext();
        var allocatingSystem = new AllocatingSystem();
        session.SystemManager.Register(allocatingSystem);
        session.BeginSession();
        var tracker = engine.Gauges!;
        var frameAllocatedBytes = IndexOfGauge(tracker, "Frame", "AllocatedBytes");

        allocatingSystem.BytesPerFrame = OneMegabyte;
        RunFrames(session, 2);
        Assert.IsGreaterThanOrEqualTo(OneMegabyte, tracker.GetLatestValue(frameAllocatedBytes));

        allocatingSystem.BytesPerFrame = 0;
        RunFrames(session, 1);
        var betweenFrames = new byte[OneMegabyte];
        RunFrames(session, 1);

        Assert.IsLessThan(1024, tracker.GetLatestValue(frameAllocatedBytes));
        GC.KeepAlive(betweenFrames);
    }

    [TestMethod]
    public void Gauges_AContextThatNeverBeganASession_IsNotSampled()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.Gauges, writesPeriodicReports: false);
        engine.Start();
        using var trialBuild = CreateContext();

        RunFrames(trialBuild, 2);

        Assert.IsNull(engine.Gauges);
    }

    [TestMethod]
    public void Gauges_AfterTheSessionEnds_IsNull()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.Gauges, writesPeriodicReports: false);
        engine.Start();
        var session = CreateContext();
        session.BeginSession();

        session.Dispose();

        Assert.IsNull(engine.Gauges);
    }

    [TestMethod]
    public void Gauges_WithTheFeatureOff_IsNull()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.Memory, writesPeriodicReports: false);
        engine.Start();
        using var session = CreateContext();
        session.BeginSession();

        RunFrames(session, 1);

        Assert.IsNull(engine.Gauges);
    }
}
