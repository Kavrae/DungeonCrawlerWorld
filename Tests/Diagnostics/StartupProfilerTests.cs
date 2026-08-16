using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class StartupProfilerTests
{
    // Tick()'s stability detection depends on real wall-clock gaps between calls, which isn't
    // something to assert on in a fast unit test (same reasoning EventBusTests gives for not
    // asserting on FrameBudgetTracker's real-time-driven Snapshot/TopEntries) -- these cover
    // Phase()'s functional recording and WriteReport()'s output shape instead.

    [TestMethod]
    public void Phase_Disposed_RecordsOneEntryWithGivenName()
    {
        var profiler = new StartupProfiler();

        using (profiler.Phase("World Build"))
        {
        }

        Assert.HasCount(1, profiler.Phases);
        Assert.AreEqual("World Build", profiler.Phases[0].Name);
        Assert.IsGreaterThanOrEqualTo(0, profiler.Phases[0].Milliseconds);
    }

    [TestMethod]
    public void Phase_MultipleCalls_RecordsEachInCallOrder()
    {
        var profiler = new StartupProfiler();

        using (profiler.Phase("First"))
        {
        }

        using (profiler.Phase("Second"))
        {
        }

        Assert.HasCount(2, profiler.Phases);
        Assert.AreEqual("First", profiler.Phases[0].Name);
        Assert.AreEqual("Second", profiler.Phases[1].Name);
    }

    [TestMethod]
    public void NewProfiler_IsNotYetStable()
    {
        var profiler = new StartupProfiler();

        Assert.IsFalse(profiler.IsStable);
        Assert.IsNull(profiler.TimeToStable);
    }

    [TestMethod]
    public void WriteReport_WritesOneTimestampedJsonFileWithPhases()
    {
        var profiler = new StartupProfiler();
        using (profiler.Phase("World Build"))
        {
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), $"StartupProfilerTests-{Guid.NewGuid():N}");
        try
        {
            profiler.WriteReport(outputDirectory);

            var files = Directory.GetFiles(outputDirectory, "startup-*.json");
            Assert.HasCount(1, files);

            var content = File.ReadAllText(files[0]);
            StringAssert.Contains(content, "World Build");
            StringAssert.Contains(content, "\"IsStable\": false");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }
}
