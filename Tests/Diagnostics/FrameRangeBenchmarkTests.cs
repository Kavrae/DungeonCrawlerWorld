using System.Text.Json;
using Engine.Diagnostics;
using Engine.ECS.Systems;

namespace Tests.Diagnostics;

[TestClass]
public sealed class FrameRangeBenchmarkTests
{
    private static readonly TimeSpan OneMillisecond = TimeSpan.FromMilliseconds(1);
    private static readonly FrameCostRowId TestSystem = Row(FrameCostCategory.Update, "SystemManager", "TestSystem");
    private static readonly FrameCostRowId ShellDraw = Row(FrameCostCategory.Draw, "GameLoop", "Shell.Draw");
    private static readonly DiagnosticsRunConditions TestConditions = new("FrameBudget", IsHeadless: true, HasLink: false, BuildConfiguration: "Release");

    private static FrameCostRowId Row(FrameCostCategory category, string groupName, string itemName) => FrameCostRowIds.Of(category, groupName, itemName);

    private static void RecordSystem(FrameRangeBenchmark benchmark) => benchmark.Record(TestSystem, OneMillisecond);

    private static double SystemTotal(FrameRangeBenchmark benchmark) =>
        benchmark.GetTotalMilliseconds(FrameCostCategory.Update, "SystemManager", "TestSystem");

    /// <summary>Runs frames 1..lastFrame the way SystemManager.Update drives it -- frame start, one Record for that frame's Update, frame end.</summary>
    private static void RunFrames(FrameRangeBenchmark benchmark, long lastFrame)
    {
        for (var frame = 1L; frame <= lastFrame; frame++)
        {
            benchmark.SimulationFrameStarting(frame);
            RecordSystem(benchmark);
            benchmark.SimulationFrameEnded(frame);
        }
    }

    /// <summary>Its rows start with room for 16 and grow; a row added past that must land in the grown storage.</summary>
    [TestMethod]
    public void Record_ManyDistinctItems_KeepsEveryOnesTotal()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(1, 10));
        benchmark.SimulationFrameStarting(1);

        for (var itemIndex = 0; itemIndex < 100; itemIndex++)
        {
            benchmark.Record(Row(FrameCostCategory.Update, "SystemManager", $"System{itemIndex}"), TimeSpan.FromMilliseconds(itemIndex));
        }

        Assert.AreEqual(99, benchmark.GetTotalMilliseconds(FrameCostCategory.Update, "SystemManager", "System99"), 1e-9);
        Assert.AreEqual(17, benchmark.GetTotalMilliseconds(FrameCostCategory.Update, "SystemManager", "System17"), 1e-9);
    }

    [TestMethod]
    public void Record_OnlyCountsFramesInsideTheRange()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(10, 15));

        RunFrames(benchmark, 30);

        // Frames 10..14 -- five frames, one millisecond each. Frame 14 ending closes it.
        Assert.AreEqual(5, SystemTotal(benchmark), 1e-9);
    }

    /// <summary>A headless run updates until the benchmark completes; that must be exactly the frames before EndFrame, or its fingerprint stops matching earlier builds'.</summary>
    [TestMethod]
    public void UpdatingUntilComplete_RunsEveryFrameBeforeEndFrame_AndNoMore()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(10, 14));
        var lastFrameRun = 0L;

        for (var frame = 1L; !benchmark.IsComplete; frame++)
        {
            benchmark.SimulationFrameStarting(frame);
            benchmark.SimulationFrameEnded(frame);
            lastFrameRun = frame;
        }

        Assert.AreEqual(13, lastFrameRun);
    }

    [TestMethod]
    public void Frames_OpenAsTheFirstStarts_AndCompleteAsTheLastEnds()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(3, 5));

        benchmark.SimulationFrameStarting(2);
        benchmark.SimulationFrameEnded(2);
        Assert.IsFalse(benchmark.IsRecording);

        benchmark.SimulationFrameStarting(3);
        Assert.IsTrue(benchmark.IsRecording);
        benchmark.SimulationFrameEnded(3);
        Assert.IsTrue(benchmark.IsRecording);

        benchmark.SimulationFrameStarting(4);
        Assert.IsFalse(benchmark.IsComplete);

        benchmark.SimulationFrameEnded(4);
        Assert.IsFalse(benchmark.IsRecording);
        Assert.IsTrue(benchmark.IsComplete);
    }

    /// <summary>Draws and shell updates between simulation frames land in the open window -- only the frame boundary, not the call's category, decides.</summary>
    [TestMethod]
    public void Record_CountsDrawsBetweenFramesInsideTheRange()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(1, 3));

        benchmark.SimulationFrameStarting(1);
        benchmark.SimulationFrameEnded(1);
        benchmark.Record(ShellDraw, OneMillisecond);
        benchmark.Record(ShellDraw, OneMillisecond);
        benchmark.SimulationFrameStarting(2);
        benchmark.SimulationFrameEnded(2);
        benchmark.Record(ShellDraw, OneMillisecond);

        Assert.AreEqual(2, benchmark.GetTotalMilliseconds(FrameCostCategory.Draw, "GameLoop", "Shell.Draw"), 1e-9);
    }

    /// <summary>Once complete it stays complete: frame numbers coming round again (a new session) must not reopen it and blend a second workload in.</summary>
    [TestMethod]
    public void AfterComplete_NeverReopens()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(1, 2));
        RunFrames(benchmark, 3);

        benchmark.SimulationFrameStarting(1);
        RecordSystem(benchmark);
        benchmark.SimulationFrameEnded(1);

        Assert.IsTrue(benchmark.IsComplete);
        Assert.AreEqual(1, SystemTotal(benchmark), 1e-9);
    }

    [TestMethod]
    public void WriteReport_WritesTheWorstSingleRecordingInsideTheRange()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(10, 14));
        for (var frame = 1L; frame <= 20; frame++)
        {
            benchmark.SimulationFrameStarting(frame);
            var elapsed = frame switch { 5 => 9.0, 12 => 5.0, _ => 1.0 };
            benchmark.Record(TestSystem, TimeSpan.FromMilliseconds(elapsed));
            benchmark.SimulationFrameEnded(frame);
        }

        var directory = Path.Combine(Path.GetTempPath(), $"{nameof(FrameRangeBenchmarkTests)}-{Guid.NewGuid():N}");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(benchmark.WriteReport(directory, randomSeed: 7, TestConditions)));
            var item = document.RootElement.GetProperty("Update").GetProperty("SystemManager")[0];
            Assert.AreEqual(5, item.GetProperty("WorstMilliseconds").GetDouble(), 1e-9);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void WriteReport_LeavesOutAnEntryRecordedOnlyBeforeTheRange()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(10, 14));
        for (var frame = 1L; frame <= 20; frame++)
        {
            benchmark.SimulationFrameStarting(frame);
            if (frame < 10)
            {
                benchmark.Record(Row(FrameCostCategory.Update, "EventBus", "EarlyEvent"), TimeSpan.FromMilliseconds(1));
            }

            benchmark.Record(TestSystem, TimeSpan.FromMilliseconds(1));
            benchmark.SimulationFrameEnded(frame);
        }

        var directory = Path.Combine(Path.GetTempPath(), $"{nameof(FrameRangeBenchmarkTests)}-{Guid.NewGuid():N}");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(benchmark.WriteReport(directory, randomSeed: 7, TestConditions)));
            var update = document.RootElement.GetProperty("Update");
            Assert.IsFalse(update.TryGetProperty("EventBus", out _));
            Assert.IsTrue(update.TryGetProperty("SystemManager", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void WriteReport_WritesSeedRangeAndPerFrameCost()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(10, 14));
        RunFrames(benchmark, 20);

        var directory = Path.Combine(Path.GetTempPath(), $"{nameof(FrameRangeBenchmarkTests)}-{Guid.NewGuid():N}");
        try
        {
            var path = benchmark.WriteReport(directory, randomSeed: 7, TestConditions);

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.AreEqual(7, root.GetProperty("RandomSeed").GetInt32());
            Assert.AreEqual(Environment.ProcessId, root.GetProperty("ProcessId").GetInt32());
            Assert.AreEqual(10, root.GetProperty("StartFrame").GetInt64());
            Assert.AreEqual(14, root.GetProperty("EndFrame").GetInt64());
            Assert.AreEqual(4, root.GetProperty("FrameCount").GetInt64());

            var item = root.GetProperty("Update").GetProperty("SystemManager")[0];
            Assert.AreEqual("TestSystem", item.GetProperty("Name").GetString());
            Assert.AreEqual(4, item.GetProperty("TotalMilliseconds").GetDouble(), 1e-9);
            Assert.AreEqual(1, item.GetProperty("MillisecondsPerFrame").GetDouble(), 1e-9);
            Assert.AreEqual(4, item.GetProperty("RecordCount").GetInt32());

            Assert.AreEqual(TestConditions, DiagnosticsReportReader.ReadBenchmarkReport(File.ReadAllBytes(path)).Conditions);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Parse_ReadsStartAndEnd()
    {
        var range = BenchmarkFrameRange.Parse(["--seed=1", "--benchmark-frames=600-3600"]);

        Assert.IsNotNull(range);
        Assert.AreEqual(600, range.Value.StartFrame);
        Assert.AreEqual(3600, range.Value.EndFrame);
        Assert.AreEqual(3000, range.Value.FrameCount);
    }

    [TestMethod]
    [DataRow("--benchmark-frames=3600-600")]
    [DataRow("--benchmark-frames=600-600")]
    [DataRow("--benchmark-frames=600")]
    [DataRow("--benchmark-frames=a-b")]
    [DataRow("--benchmark-frames=-5-10")]
    public void Parse_MalformedValue_IsNoBenchmark(string argument)
    {
        Assert.IsNull(BenchmarkFrameRange.Parse([argument]));
    }

    [TestMethod]
    public void Parse_Absent_IsNoBenchmark()
    {
        Assert.IsNull(BenchmarkFrameRange.Parse(["--diagnostics=frame"]));
    }

    /// <summary>With both FrameBudget and a benchmark on, one Record reaches both -- the same instrumentation feeds latest.json and the benchmark.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void DiagnosticsEngine_WithFrameBudgetAndBenchmark_RecorderFeedsTheBenchmark()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.FrameBudget, randomSeed: 1, new BenchmarkFrameRange(1, 1000));
        engine.Start();

        Assert.IsNotNull(EngineHooks.FrameCosts.Listener);
        Assert.IsNotInstanceOfType<FrameBudgetTracker>(EngineHooks.FrameCosts.Listener);
        Assert.IsNotInstanceOfType<FrameRangeBenchmark>(EngineHooks.FrameCosts.Listener);
    }

    [TestMethod]
    [DoNotParallelize]
    public void DiagnosticsEngine_BenchmarkOnly_StillHasARecorder()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.None, randomSeed: 1, new BenchmarkFrameRange(1, 1000));
        engine.Start();

        Assert.IsInstanceOfType<FrameRangeBenchmark>(EngineHooks.FrameCosts.Listener);
    }

    [TestMethod]
    [DoNotParallelize]
    public void DiagnosticsEngine_Neither_HasNoRecorder()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.None);
        engine.Start();

        Assert.IsNull(EngineHooks.FrameCosts.Listener);
        Assert.IsNull(EngineHooks.SimulationFrames.Listener);
    }

    [TestMethod]
    [DoNotParallelize]
    public void DiagnosticsEngine_BenchmarkOnly_ListensToSimulationFrames()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.None, randomSeed: 1, new BenchmarkFrameRange(1, 1000));
        engine.Start();

        Assert.AreSame(engine, EngineHooks.SimulationFrames.Listener);
    }

    [TestMethod]
    [DoNotParallelize]
    public void DiagnosticsEngine_AnyFeature_ListensToSimulationFrames()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.LeakDetection);
        engine.Start();

        Assert.AreSame(engine, EngineHooks.SimulationFrames.Listener);
    }

    [TestMethod]
    public void ARowNestedInItself_AddsOnlyTheOutermostMeasurementsTime_AndCountsBoth()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(1, 2));
        var eventRow = Row(FrameCostCategory.Update, "EventBus", "SelfPublishingEvent");
        benchmark.SimulationFrameStarting(1);

        benchmark.Begin(eventRow);
        benchmark.Begin(eventRow);
        benchmark.Record(eventRow, TimeSpan.FromMilliseconds(1));
        benchmark.Record(eventRow, TimeSpan.FromMilliseconds(3));

        Assert.AreEqual(3, benchmark.GetTotalMilliseconds(FrameCostCategory.Update, "EventBus", "SelfPublishingEvent"), 1e-9);
        Assert.AreEqual(2, benchmark.GetRecordCount(FrameCostCategory.Update, "EventBus", "SelfPublishingEvent"));
    }

    [TestMethod]
    public void AMeasurementOpenedBeforeTheRange_StillNestsWhatTheRangeRecordsInsideIt()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(1, 2));
        var outerRow = Row(FrameCostCategory.Update, "GameLoop", "OpenedBeforeTheRange");

        benchmark.Begin(outerRow);
        benchmark.Begin(outerRow);
        benchmark.SimulationFrameStarting(1);
        benchmark.Record(outerRow, TimeSpan.FromMilliseconds(1));
        benchmark.Record(outerRow, TimeSpan.FromMilliseconds(2));

        Assert.AreEqual(2, benchmark.GetTotalMilliseconds(FrameCostCategory.Update, "GameLoop", "OpenedBeforeTheRange"), 1e-9);
    }

    private static void RunEngineFrames(SystemManager systemManager, long firstFrame, long lastFrame)
    {
        for (var frame = firstFrame; frame <= lastFrame; frame++)
        {
            systemManager.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, frame));
        }
    }

    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), $"{nameof(FrameRangeBenchmarkTests)}-{Guid.NewGuid():N}");

    [TestMethod]
    [DoNotParallelize]
    [DataRow(DiagnosticsFeatures.None, false)]
    [DataRow(DiagnosticsFeatures.FrameBudget, true)]
    public void DiagnosticsEngine_OnceTheRangeCloses_TheBenchmarkStopsListening(DiagnosticsFeatures features, bool trackerListensOn)
    {
        var directory = TemporaryDirectory();
        try
        {
            using var engine = new DiagnosticsEngine(features, randomSeed: 1, new BenchmarkFrameRange(2, 4), writesPeriodicReports: false, outputDirectory: directory);
            engine.Start();
            var systemManager = new SystemManager();

            RunEngineFrames(systemManager, 1, 2);
            Assert.IsNotNull(EngineHooks.FrameCosts.Listener);

            RunEngineFrames(systemManager, 3, 3);
            Assert.IsTrue(engine.IsBenchmarkComplete);
            if (trackerListensOn)
            {
                Assert.IsInstanceOfType<FrameBudgetTracker>(EngineHooks.FrameCosts.Listener);
            }
            else
            {
                Assert.IsNull(EngineHooks.FrameCosts.Listener);
            }

            RunEngineFrames(systemManager, 5, 6);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void DiagnosticsEngine_WritesNoPeriodicReportWhileTheRangeRecords_AndResumesAfter()
    {
        var directory = TemporaryDirectory();
        try
        {
            using (var engine = new DiagnosticsEngine(DiagnosticsFeatures.FrameBudget, randomSeed: 1, new BenchmarkFrameRange(1, 5), outputDirectory: directory))
            {
                engine.Start();
                var systemManager = new SystemManager();

                RunEngineFrames(systemManager, 1, 3);
                Assert.IsFalse(File.Exists(Path.Combine(directory, "latest.json")));

                RunEngineFrames(systemManager, 4, 4);
            }

            Assert.IsTrue(File.Exists(Path.Combine(directory, "latest.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void DiagnosticsEngine_StampsTheBenchmarkReportWithItsRunConditions()
    {
        var directory = TemporaryDirectory();
        try
        {
            using (var engine = new DiagnosticsEngine(DiagnosticsFeatures.Gauges, randomSeed: 1, new BenchmarkFrameRange(1, 2), writesPeriodicReports: false, isHeadless: true, outputDirectory: directory))
            {
                engine.Start();
                RunEngineFrames(new SystemManager(), 1, 1);
            }

            var report = DiagnosticsReportReader.ReadBenchmarkReport(File.ReadAllBytes(Directory.GetFiles(directory, "benchmark-*.json").Single()));
            Assert.AreEqual(new DiagnosticsRunConditions(nameof(DiagnosticsFeatures.Gauges), IsHeadless: true, HasLink: false, DiagnosticsRunConditions.CurrentBuildConfiguration), report.Conditions);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void RunConditions_NameEveryWayTwoRunsDiffer()
    {
        var windowedDebug = new DiagnosticsRunConditions("FrameBudget", IsHeadless: false, HasLink: true, "Debug");

        Assert.IsEmpty(TestConditions.DifferencesFrom(TestConditions));
        CollectionAssert.AreEqual(
            new[] { "build Release vs Debug", "headless vs windowed", "unlinked vs linked" },
            TestConditions.DifferencesFrom(windowedDebug).ToArray());
    }

    [TestMethod]
    [DoNotParallelize]
    public void DiagnosticsEngine_Dispose_ClearsEveryChannelItSubscribed()
    {
        var engine = new DiagnosticsEngine(DiagnosticsFeatures.FrameBudget);
        engine.Start();

        engine.Dispose();

        Assert.IsNull(EngineHooks.FrameCosts.Listener);
        Assert.IsNull(EngineHooks.SimulationFrames.Listener);
    }
}
