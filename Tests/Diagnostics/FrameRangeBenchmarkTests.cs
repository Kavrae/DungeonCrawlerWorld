using System.Text.Json;
using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class FrameRangeBenchmarkTests
{
    private static readonly TimeSpan OneMillisecond = TimeSpan.FromMilliseconds(1);

    private static void RecordSystem(FrameRangeBenchmark benchmark) =>
        benchmark.Record(FrameCostCategory.Update, "SystemManager", "TestSystem", OneMillisecond);

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
        benchmark.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", OneMillisecond);
        benchmark.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", OneMillisecond);
        benchmark.SimulationFrameStarting(2);
        benchmark.SimulationFrameEnded(2);
        benchmark.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", OneMillisecond);

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
            benchmark.Record(FrameCostCategory.Update, "SystemManager", "TestSystem", TimeSpan.FromMilliseconds(elapsed));
            benchmark.SimulationFrameEnded(frame);
        }

        var directory = Path.Combine(Path.GetTempPath(), $"{nameof(FrameRangeBenchmarkTests)}-{Guid.NewGuid():N}");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(benchmark.WriteReport(directory, randomSeed: 7)));
            var item = document.RootElement.GetProperty("Update").GetProperty("SystemManager")[0];
            Assert.AreEqual(5, item.GetProperty("WorstMilliseconds").GetDouble(), 1e-9);
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
            var path = benchmark.WriteReport(directory, randomSeed: 7);

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
