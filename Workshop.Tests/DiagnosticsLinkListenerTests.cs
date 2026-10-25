using System.Text.Json;
using Engine.Diagnostics;
using Workshop.Debug;

namespace Workshop.Tests;

[TestClass]
public sealed class DiagnosticsLinkListenerTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private static void UpdateUntil(DiagnosticsLinkListener listener, Func<LiveDiagnosticsSession, bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (true)
        {
            listener.Update();
            if (listener.Session is { } session && condition(session))
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting until {description}.");
            }

            Thread.Sleep(5);
        }
    }

    private static DiagnosticsLink ConnectedLink(DiagnosticsLinkListener listener, TextWriter? errors = null)
    {
        var link = new DiagnosticsLink(listener.EndPoint, DiagnosticsFeatures.FrameBudget | DiagnosticsFeatures.Gauges, randomSeed: 9, errors ?? TextWriter.Null);
        link.Start();
        return link;
    }

    [TestMethod]
    public void AGameThatConnects_IsConnected_WithItsProcessId()
    {
        using var listener = new DiagnosticsLinkListener();
        listener.Start();
        using var link = ConnectedLink(listener);

        UpdateUntil(listener, session => session.Hello is not null, "the hello arrived");

        var session = listener.Session!;
        Assert.AreEqual(DiagnosticsConnectionState.Connected, session.State);
        Assert.AreEqual(9, session.Hello!.RandomSeed);
        Assert.AreEqual($"Diagnostics: connected (pid {Environment.ProcessId})", session.StatusText);
    }

    [TestMethod]
    public void FramesAndReports_ReachTheSession()
    {
        using var listener = new DiagnosticsLinkListener();
        listener.Start();
        using var link = ConnectedLink(listener);
        var tracker = new GaugeTracker([new Gauge("Streamer", "QueuedBytes", GaugeKind.Level, static () => 512)]);
        link.SessionStarted(tracker.Gauges);
        tracker.Sample();
        link.RecordFrame(1, TimeSpan.FromMilliseconds(4), tracker);
        link.RecordFrame(2, TimeSpan.FromMilliseconds(6), tracker);
        link.HostUpdated(simulated: false);
        link.SendPeriodicReport(JsonSerializer.Serialize(new DiagnosticsReport(DateTime.UtcNow, "Gauges", 9, null, null, [new DiagnosticsReportLeak("HealthComponent", "grew", 0.5)], null)));
        link.SendStartupReport(JsonSerializer.Serialize(new DiagnosticsStartupReport(DateTime.UtcNow, true, 1234, [new PhaseRecord("Build", 20, 0), new PhaseRecord("Configure", 5, 1)])));
        link.SendBenchmarkCompleted(new DiagnosticsLinkBenchmarkCompletion(1, 3, "benchmark.json", null, null));

        UpdateUntil(listener, session => session.LatestBenchmarkCompletion is not null && session.IsPaused && session.StartupReport is not null && session.LatestPeriodicReport is not null, "everything arrived");

        var session = listener.Session!;
        Assert.IsTrue(session.IsSimulating);
        Assert.AreEqual(GaugeUnit.Bytes, session.Gauges[0].Unit);
        Assert.AreEqual(2, session.LatestFrame);
        var history = session.History!;
        Assert.AreEqual(2, history.RecordedFrameCount);
        var updateMilliseconds = new double[2];
        history.CopySeries(0, updateMilliseconds);
        CollectionAssert.AreEqual(new[] { 4.0, 6.0 }, updateMilliseconds);
        Assert.AreEqual(512, history.GetLatestValue(1));
        Assert.AreEqual(1234, session.StartupReport!.TimeToStableMilliseconds);
        Assert.AreEqual(new PhaseRecord("Configure", 5, 1), session.StartupReport.Phases[1]);
        Assert.AreEqual("HealthComponent", session.LatestPeriodicReport!.Leaks![0].Subject);
        Assert.AreEqual(1, session.PeriodicReportVersion);
    }

    [TestMethod]
    public void AGameThatCloses_LeavesItsSessionEnded_WithWhatItSent()
    {
        using var listener = new DiagnosticsLinkListener();
        listener.Start();
        var link = ConnectedLink(listener);
        link.SessionStarted([]);
        link.RecordFrame(1, TimeSpan.FromMilliseconds(2), gauges: null);
        UpdateUntil(listener, session => session.LatestFrame == 1, "the frame arrived");

        link.Dispose();
        UpdateUntil(listener, session => session.State == DiagnosticsConnectionState.Ended, "the session ended");

        Assert.AreEqual("Diagnostics: ended", listener.Session!.StatusText);
        Assert.AreEqual(1, listener.Session.History!.RecordedFrameCount);
    }

    [TestMethod]
    public void ASecondGame_WhileTheFirstIsConnected_IsRefused()
    {
        using var listener = new DiagnosticsLinkListener();
        listener.Start();
        using var first = ConnectedLink(listener);
        UpdateUntil(listener, session => session.Hello is not null, "the first hello arrived");
        var firstSession = listener.Session;

        var errors = new StringWriter();
        using var second = ConnectedLink(listener, errors);
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (!second.IsClosed && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        listener.Update();
        Assert.IsTrue(second.IsClosed);
        Assert.AreSame(firstSession, listener.Session);
        Assert.AreEqual(DiagnosticsConnectionState.Connected, firstSession!.State);
    }

    [TestMethod]
    public void TheNextGame_AfterTheFirstEnded_GetsAFreshSession()
    {
        using var listener = new DiagnosticsLinkListener();
        listener.Start();
        var first = ConnectedLink(listener);
        UpdateUntil(listener, session => session.Hello is not null, "the first hello arrived");
        var firstSession = listener.Session;
        first.Dispose();
        UpdateUntil(listener, session => session.State == DiagnosticsConnectionState.Ended, "the first ended");

        using var second = ConnectedLink(listener);
        UpdateUntil(listener, session => !ReferenceEquals(session, firstSession) && session.Hello is not null, "the second hello arrived");

        Assert.AreEqual(DiagnosticsConnectionState.Connected, listener.Session!.State);
    }

    [TestMethod]
    public void DroppedFrames_ShowOnTheStatusLine()
    {
        var session = new LiveDiagnosticsSession(historyCapacity: 8);
        var payload = new byte[DiagnosticsFrameRecord.ByteCountFor(0)];
        DiagnosticsFrameRecord.Write(new DiagnosticsFrameRecord(1, 1, 60, 60, IsPaused: false, DroppedBefore: 0), [], payload);
        session.Receive(DiagnosticsLinkMessageType.SessionStarted, System.Text.Encoding.UTF8.GetBytes("{\"Gauges\":[]}"));
        session.Receive(DiagnosticsLinkMessageType.Frame, payload);
        DiagnosticsFrameRecord.Write(new DiagnosticsFrameRecord(40, 1, 60, 60, IsPaused: false, DroppedBefore: 38), [], payload);
        session.Receive(DiagnosticsLinkMessageType.Frame, payload);

        session.ApplyPending();

        Assert.AreEqual(38, session.DroppedFrameCount);
        Assert.AreEqual("Diagnostics: connected, dropped 38 frames", session.StatusText);
    }

    [TestMethod]
    public void TheHistory_KeepsItsCapacity_AndReadsOldestFirst()
    {
        var history = new DiagnosticsFrameHistory(gaugeCount: 1, capacity: 4);
        for (var frame = 1; frame <= 6; frame++)
        {
            history.Record(frame, frame * 10, [frame * 100]);
        }

        var frames = new long[4];
        var updateMilliseconds = new double[4];
        var gauge = new double[4];
        history.CopyFrames(frames);
        history.CopySeries(0, updateMilliseconds);
        history.CopySeries(1, gauge);

        Assert.AreEqual(4, history.RecordedFrameCount);
        CollectionAssert.AreEqual(new long[] { 3, 4, 5, 6 }, frames);
        CollectionAssert.AreEqual(new[] { 30.0, 40.0, 50.0, 60.0 }, updateMilliseconds);
        CollectionAssert.AreEqual(new[] { 300.0, 400.0, 500.0, 600.0 }, gauge);
    }

    [TestMethod]
    public void TheLaunchArgument_NamesTheListenersLoopbackPort()
    {
        using var listener = new DiagnosticsLinkListener();
        listener.Start();

        Assert.AreEqual(listener.EndPoint, DiagnosticsLinkArgument.Parse([listener.LaunchArgument]));
        Assert.AreNotEqual(0, listener.EndPoint.Port);
    }
}
