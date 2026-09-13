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

    /// <summary>Runs frames 1..lastFrame the way GameLoop does -- BeginSimulationFrame, then one Record for that frame's Update.</summary>
    private static void RunFrames(FrameRangeBenchmark benchmark, long lastFrame)
    {
        for (var frame = 1L; frame <= lastFrame; frame++)
        {
            benchmark.BeginSimulationFrame(frame);
            RecordSystem(benchmark);
        }
    }

    [TestMethod]
    public void Record_OnlyCountsFramesInsideTheRange()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(10, 15));

        RunFrames(benchmark, 30);

        // Frames 10..14 -- five frames, one millisecond each. Frame 15 begins the close.
        Assert.AreEqual(5, SystemTotal(benchmark), 1e-9);
    }

    [TestMethod]
    public void BeginSimulationFrame_OpensAtStartAndCompletesAtEnd()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(3, 5));

        benchmark.BeginSimulationFrame(2);
        Assert.IsFalse(benchmark.IsRecording);

        benchmark.BeginSimulationFrame(3);
        Assert.IsTrue(benchmark.IsRecording);

        benchmark.BeginSimulationFrame(4);
        Assert.IsTrue(benchmark.IsRecording);
        Assert.IsFalse(benchmark.IsComplete);

        benchmark.BeginSimulationFrame(5);
        Assert.IsFalse(benchmark.IsRecording);
        Assert.IsTrue(benchmark.IsComplete);
    }

    /// <summary>Draws and shell updates between simulation frames land in the open window -- only the frame boundary, not the call's category, decides.</summary>
    [TestMethod]
    public void Record_CountsDrawsBetweenFramesInsideTheRange()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(1, 2));

        benchmark.BeginSimulationFrame(1);
        benchmark.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", OneMillisecond);
        benchmark.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", OneMillisecond);
        benchmark.BeginSimulationFrame(2);
        benchmark.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", OneMillisecond);

        Assert.AreEqual(2, benchmark.GetTotalMilliseconds(FrameCostCategory.Draw, "GameLoop", "Shell.Draw"), 1e-9);
    }

    /// <summary>Once complete it stays complete: frame numbers coming round again (a new session) must not reopen it and blend a second workload in.</summary>
    [TestMethod]
    public void BeginSimulationFrame_AfterComplete_NeverReopens()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(1, 2));
        RunFrames(benchmark, 3);

        benchmark.BeginSimulationFrame(1);
        RecordSystem(benchmark);

        Assert.IsTrue(benchmark.IsComplete);
        Assert.AreEqual(1, SystemTotal(benchmark), 1e-9);
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
    public void DiagnosticsEngine_WithFrameBudgetAndBenchmark_RecorderFeedsTheBenchmark()
    {
        var engine = new DiagnosticsEngine(DiagnosticsFeatures.FrameBudget, randomSeed: 1, new BenchmarkFrameRange(1, 1000));

        Assert.IsNotNull(engine.FrameCostRecorder);
        Assert.IsNotInstanceOfType<FrameBudgetTracker>(engine.FrameCostRecorder);
        Assert.IsNotInstanceOfType<FrameRangeBenchmark>(engine.FrameCostRecorder);
    }

    [TestMethod]
    public void DiagnosticsEngine_BenchmarkOnly_StillHasARecorder()
    {
        var engine = new DiagnosticsEngine(DiagnosticsFeatures.None, randomSeed: 1, new BenchmarkFrameRange(1, 1000));

        Assert.IsInstanceOfType<FrameRangeBenchmark>(engine.FrameCostRecorder);
    }

    [TestMethod]
    public void DiagnosticsEngine_Neither_HasNoRecorder()
    {
        Assert.IsNull(new DiagnosticsEngine(DiagnosticsFeatures.None).FrameCostRecorder);
    }
}
