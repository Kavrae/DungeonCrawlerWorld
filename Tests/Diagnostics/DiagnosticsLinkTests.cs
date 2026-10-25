using System.Net;
using System.Net.Sockets;
using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints.Composites;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests.Diagnostics;

[TestClass]
public sealed class DiagnosticsLinkTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private static EcsContext CreateContext()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        return new EcsContext(new EntityManager(componentManager, initialCapacity: 10), componentManager, new SystemManager(), new EventBus());
    }

    private static void RunFrames(EcsContext context, int firstFrame, int lastFrame)
    {
        for (var frame = firstFrame; frame <= lastFrame; frame++)
        {
            context.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: frame));
            EngineHooks.HostFrames.Listener?.HostUpdated(simulated: true);
            EngineHooks.HostFrames.Listener?.HostDrew();
        }
    }

    private static void WaitUntil(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting until {description}.");
            }

            Thread.Sleep(5);
        }
    }

    private static List<(DiagnosticsFrameRecord Record, double[] GaugeValues)> FramesOf(LoopbackDiagnosticsReceiver receiver)
    {
        var frames = new List<(DiagnosticsFrameRecord, double[])>();
        foreach (var (type, payload) in receiver.Messages)
        {
            if (type != DiagnosticsLinkMessageType.Frame)
            {
                continue;
            }

            var gaugeValues = new double[16];
            var gaugeCount = DiagnosticsFrameRecord.Read(payload, out var record, gaugeValues);
            frames.Add((record, gaugeValues[..gaugeCount]));
        }

        return frames;
    }

    private static IPEndPoint UnusedLoopbackEndPoint()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endPoint = (IPEndPoint)listener.LocalEndpoint;
        listener.Stop();
        return endPoint;
    }

    [TestMethod]
    public void EveryMessageType_ArrivesAsSent()
    {
        using var receiver = new LoopbackDiagnosticsReceiver();
        var errors = new StringWriter();
        using var link = new DiagnosticsLink(receiver.EndPoint, DiagnosticsFeatures.FrameBudget | DiagnosticsFeatures.Gauges, randomSeed: 42, errors);
        link.Start();

        var hello = DiagnosticsLinkProtocol.ReadJson<DiagnosticsLinkHello>(receiver.WaitFor(DiagnosticsLinkMessageType.Hello));
        Assert.AreEqual(DiagnosticsLinkProtocol.Version, hello.ProtocolVersion);
        Assert.AreEqual(Environment.ProcessId, hello.ProcessId);
        Assert.AreEqual(42, hello.RandomSeed);
        Assert.AreEqual((DiagnosticsFeatures.FrameBudget | DiagnosticsFeatures.Gauges).ToString(), hello.Features);

        var loads = 0.0;
        var tracker = new GaugeTracker([new Gauge("Streamer", "QueuedBytes", GaugeKind.Level, static () => 2048), new Gauge("Streamer", "Loads", GaugeKind.Cumulative, () => loads)]);
        link.SessionStarted(tracker.Gauges);
        loads = 3;
        tracker.Sample();
        link.RecordFrame(7, TimeSpan.FromMilliseconds(2.5), tracker);
        link.HostUpdated(simulated: false);
        link.SendPeriodicReport("{\"Features\":\"Gauges\"}");
        link.SendStartupReport("{\"IsStable\":true}");
        link.SendBenchmarkCompleted(new DiagnosticsLinkBenchmarkCompletion(10, 20, "benchmark.json", null, "gauges.json"));
        link.SessionEnded();

        receiver.WaitFor(DiagnosticsLinkMessageType.SessionEnded);
        var sessionStart = DiagnosticsLinkProtocol.ReadJson<DiagnosticsLinkSessionStart>(receiver.WaitFor(DiagnosticsLinkMessageType.SessionStarted));
        Assert.AreEqual(new DiagnosticsLinkGaugeDescriptor("Streamer", "QueuedBytes", GaugeKind.Level, GaugeUnit.Bytes), sessionStart.Gauges[0]);
        Assert.AreEqual(new DiagnosticsLinkGaugeDescriptor("Streamer", "Loads", GaugeKind.Cumulative, GaugeUnit.Count), sessionStart.Gauges[1]);

        var frames = FramesOf(receiver);
        Assert.HasCount(2, frames);
        Assert.AreEqual(7, frames[0].Record.Frame);
        Assert.AreEqual(2.5, frames[0].Record.UpdateMilliseconds);
        Assert.IsFalse(frames[0].Record.IsPaused);
        CollectionAssert.AreEqual(new[] { 2048.0, 3.0 }, frames[0].GaugeValues);
        Assert.AreEqual(7, frames[1].Record.Frame);
        Assert.IsTrue(frames[1].Record.IsPaused);

        Assert.AreEqual("{\"Features\":\"Gauges\"}", DiagnosticsLinkProtocol.ReadText(receiver.WaitFor(DiagnosticsLinkMessageType.PeriodicReport)));
        Assert.AreEqual("{\"IsStable\":true}", DiagnosticsLinkProtocol.ReadText(receiver.WaitFor(DiagnosticsLinkMessageType.StartupReport)));
        Assert.AreEqual(
            new DiagnosticsLinkBenchmarkCompletion(10, 20, "benchmark.json", null, "gauges.json"),
            DiagnosticsLinkProtocol.ReadJson<DiagnosticsLinkBenchmarkCompletion>(receiver.WaitFor(DiagnosticsLinkMessageType.BenchmarkCompleted)));
        Assert.AreEqual(string.Empty, errors.ToString());
    }

    [TestMethod]
    public void AListenerThatIsGone_ClosesTheLinkWithOneErrorLine_AndRecordingCarriesOn()
    {
        var errors = new StringWriter();
        using var link = new DiagnosticsLink(UnusedLoopbackEndPoint(), DiagnosticsFeatures.Gauges, randomSeed: 1, errors);
        link.Start();

        WaitUntil(() => link.IsClosed, "the link closed");
        link.SessionStarted([]);
        for (var frame = 1; frame <= 1_000; frame++)
        {
            link.RecordFrame(frame, TimeSpan.FromMilliseconds(1), gauges: null);
            link.HostUpdated(simulated: frame % 2 == 0);
        }

        var lines = errors.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(1, lines);
        StringAssert.Contains(lines[0], "diagnostics link");
    }

    [TestMethod]
    public void TheIdeClosingMidRun_ClosesTheLinkWithOneErrorLine()
    {
        var receiver = new LoopbackDiagnosticsReceiver();
        var errors = new StringWriter();
        using var link = new DiagnosticsLink(receiver.EndPoint, DiagnosticsFeatures.Gauges, randomSeed: 1, errors);
        link.Start();
        receiver.WaitFor(DiagnosticsLinkMessageType.Hello);
        link.SessionStarted([]);

        receiver.Dispose();
        var frame = 0;
        WaitUntil(() =>
        {
            link.RecordFrame(++frame, TimeSpan.FromMilliseconds(1), gauges: null);
            return link.IsClosed;
        }, "the link closed");

        Assert.HasCount(1, errors.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [TestMethod]
    [DoNotParallelize]
    public void WithTheLinkOn_ASimulationFrameAllocatesNothing()
    {
        using var receiver = new LoopbackDiagnosticsReceiver();
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.Gauges, randomSeed: 1, writesPeriodicReports: false, linkEndPoint: receiver.EndPoint);
        engine.Start();
        using var session = CreateContext();
        var reading = 0.0;
        session.Gauges.Register("Test", "Value", GaugeKind.Level, () => reading++);
        session.BeginSession();
        receiver.WaitFor(DiagnosticsLinkMessageType.SessionStarted);
        RunFrames(session, 1, 200);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        RunFrames(session, 201, 5_200);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.AreEqual(0, allocated);
        receiver.WaitFor(DiagnosticsLinkMessageType.Frame);
        Assert.IsFalse(engine.IsLinkClosed);
    }

    [TestMethod]
    [DoNotParallelize]
    public void ASeededRun_EndsOnTheSameWorld_WithTheLinkOnOrOff()
    {
        var withoutLink = RunSeededWorld(linkEndPoint: null);

        using var receiver = new LoopbackDiagnosticsReceiver();
        var withLink = RunSeededWorld(receiver.EndPoint);

        Assert.AreEqual(withoutLink, withLink);
        Assert.IsGreaterThan(0, FramesOf(receiver).Count);
    }

    private static string RunSeededWorld(IPEndPoint? linkEndPoint)
    {
        using var engine = linkEndPoint is null ? null : new DiagnosticsEngine(DiagnosticsFeatures.FrameBudget | DiagnosticsFeatures.Gauges, randomSeed: 5, writesPeriodicReports: false, linkEndPoint: linkEndPoint);
        engine?.Start();

        var map = new Map(new Vector3Int(40, 40, 3));
        var session = GameBootstrapper.Build(ValidatedMods.None, map, new MathUtility(new SeededRandom((ulong)5)), initialEntityCapacity: 1_000, initialComponentCapacity: 100, runtimeSpawnSeed: 5);
        using var ecsContext = session.EcsContext;
        var factory = session.Internals.Factory;
        for (var index = 0; index < 6; index++)
        {
            factory.Spawn(Goblin.Id, 5 + (index * 4), 10);
            factory.Spawn(Fairy.Id, 5 + (index * 4), 14);
            factory.Spawn(GoblinEngineer.Id, 5 + (index * 4), 18);
        }

        ecsContext.BeginSession();
        RunFrames(ecsContext, 1, 600);
        return Fingerprint(ecsContext);
    }

    private static string Fingerprint(EcsContext ecsContext)
    {
        var parts = new List<string> { ecsContext.EntityManager.LivingEntityCount.ToString() };
        var transforms = ecsContext.ComponentManager.GetDirectPool<TransformComponent>();
        for (var entityId = 0; entityId < transforms.Capacity; entityId++)
        {
            if (transforms.TryGetReadonly(entityId, out var transform))
            {
                parts.Add($"{entityId}:{transform.Position.X},{transform.Position.Y},{transform.Position.Z}");
            }
        }

        foreach (var pool in ecsContext.ComponentManager.AllPools.OfType<IMemoryReportingComponentPool>().OrderBy(static pool => pool.ComponentType.FullName, StringComparer.Ordinal))
        {
            parts.Add($"{pool.ComponentType.Name}={pool.Count}");
        }

        return string.Join(";", parts);
    }
}
