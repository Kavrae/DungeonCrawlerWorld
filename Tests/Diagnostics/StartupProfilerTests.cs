using System.Diagnostics;
using Engine.Diagnostics;
using Engine.ECS.Systems;

namespace Tests.Diagnostics;

[TestClass]
public sealed class StartupProfilerTests
{
    private static void RecordScope(StartupProfiler profiler, DiagnosticScope scope, Action? inside = null)
    {
        profiler.ScopeStarted(in scope);
        inside?.Invoke();
        profiler.ScopeEnded(in scope, TimeSpan.FromMilliseconds(2));
    }

    [TestMethod]
    public void Scope_Ended_RecordsOneEntryWithItsNameAndDuration()
    {
        var profiler = new StartupProfiler();

        RecordScope(profiler, new DiagnosticScope("World Build"));

        Assert.HasCount(1, profiler.Phases);
        Assert.AreEqual(new PhaseRecord("World Build", 2, 0), profiler.Phases[0]);
    }

    [TestMethod]
    public void Scopes_OneAfterAnother_RecordEachAtDepthZero_InStartOrder()
    {
        var profiler = new StartupProfiler();

        RecordScope(profiler, new DiagnosticScope("First"));
        RecordScope(profiler, new DiagnosticScope("Second"));

        CollectionAssert.AreEqual(new[] { ("First", 0), ("Second", 0) }, profiler.Phases.Select(phase => (phase.Name, phase.Depth)).ToArray());
    }

    [TestMethod]
    public void NestedScopes_RecordTheParentFirst_WithEachChildOneDeeper()
    {
        var profiler = new StartupProfiler();

        RecordScope(profiler, new DiagnosticScope("Module Load"), inside: () =>
        {
            RecordScope(profiler, new DiagnosticScope("Configure"), inside: () => RecordScope(profiler, new DiagnosticScope("Configure", "HealthModule")));
            RecordScope(profiler, new DiagnosticScope("RegisterBehavior"));
        });

        CollectionAssert.AreEqual(
            new[] { ("Module Load", 0), ("Configure", 1), ("Configure:HealthModule", 2), ("RegisterBehavior", 1) },
            profiler.Phases.Select(phase => (phase.Name, phase.Depth)).ToArray());
    }

    [TestMethod]
    public void NewProfiler_IsNotYetStable()
    {
        var profiler = new StartupProfiler();

        Assert.IsFalse(profiler.IsStable);
        Assert.IsNull(profiler.TimeToStable);
    }

    private static void TickWindows(StartupProfiler profiler, params double[] typicalMillisecondsByWindow)
    {
        foreach (var typicalMilliseconds in typicalMillisecondsByWindow)
        {
            for (var frameIndex = 0; frameIndex < StartupProfiler.WindowFrameCount; frameIndex++)
            {
                var spike = frameIndex % 20 == 7 ? 10 * typicalMilliseconds : 0;
                profiler.Tick(TimeSpan.FromMilliseconds(typicalMilliseconds + spike + frameIndex % 3 * 0.01));
            }
        }
    }

    [TestMethod]
    public void SteadyFrames_WithRegularSpikes_SettleOnceEnoughWindowsAgree()
    {
        var profiler = new StartupProfiler();

        TickWindows(profiler, [.. Enumerable.Repeat(2.0, StartupProfiler.RequiredAgreeingWindows - 1)]);
        Assert.IsFalse(profiler.IsStable);

        TickWindows(profiler, 2.0);

        Assert.IsTrue(profiler.IsStable);
        Assert.IsNotNull(profiler.TimeToStable);
    }

    [TestMethod]
    public void FramesStillGettingCheaper_NeverSettle()
    {
        var profiler = new StartupProfiler();

        TickWindows(profiler, 10, 8, 6, 4, 3, 2);

        Assert.IsFalse(profiler.IsStable);
    }

    [TestMethod]
    public void WarmupThenSteady_SettlesAtTheFirstSteadyWindow_NotWhenItWasConfirmed()
    {
        var profiler = new StartupProfiler();
        var constructed = Stopwatch.GetTimestamp();
        TickWindows(profiler, 9, 6);
        var steadyBegins = Stopwatch.GetElapsedTime(constructed);

        for (var windowIndex = 0; windowIndex < StartupProfiler.RequiredAgreeingWindows; windowIndex++)
        {
            TickWindows(profiler, 2.0);
            Thread.Sleep(100);
        }

        Assert.IsTrue(profiler.IsStable);
        Assert.IsGreaterThanOrEqualTo(steadyBegins, profiler.TimeToStable!.Value);
        Assert.IsLessThan(steadyBegins + TimeSpan.FromMilliseconds(200), profiler.TimeToStable.Value);
    }

    [TestMethod]
    public void WriteReport_Twice_RewritesTheSameFile()
    {
        var profiler = new StartupProfiler();
        RecordScope(profiler, new DiagnosticScope("World Build"));
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"StartupProfilerTests-{Guid.NewGuid():N}");
        try
        {
            profiler.WriteReport(outputDirectory);
            TickWindows(profiler, [.. Enumerable.Repeat(2.0, StartupProfiler.RequiredAgreeingWindows)]);
            Thread.Sleep(TimeSpan.FromSeconds(1.1));
            profiler.WriteReport(outputDirectory);

            var file = Directory.GetFiles(outputDirectory, "startup-*.json").Single();
            StringAssert.Contains(File.ReadAllText(file), "\"IsStable\": true");
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void WriteReport_WritesOneTimestampedJsonFileWithPhasesAndDepths()
    {
        var profiler = new StartupProfiler();
        RecordScope(profiler, new DiagnosticScope("World Build"), inside: () => RecordScope(profiler, new DiagnosticScope("Terrain")));

        var outputDirectory = Path.Combine(Path.GetTempPath(), $"StartupProfilerTests-{Guid.NewGuid():N}");
        try
        {
            profiler.WriteReport(outputDirectory);

            var files = Directory.GetFiles(outputDirectory, "startup-*.json");
            Assert.HasCount(1, files);

            var content = File.ReadAllText(files[0]);
            StringAssert.Contains(content, "World Build");
            StringAssert.Contains(content, "\"Depth\": 1");
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

    [TestMethod]
    public void DiagnosticScope_ToString_JoinsNameAndDetail()
    {
        Assert.AreEqual("Configure:HealthModule", new DiagnosticScope("Configure", "HealthModule").ToString());
        Assert.AreEqual("Heap Compaction", new DiagnosticScope("Heap Compaction").ToString());
    }

    [TestMethod]
    [DoNotParallelize]
    public void EngineHooksScope_WithNoListener_IsANoOp()
    {
        using (EngineHooks.DiagnosticScope("Unheard"))
        {
        }

        Assert.IsNull(EngineHooks.DiagnosticScopes.Listener);
    }

    [TestMethod]
    [DoNotParallelize]
    public void EcsBuilder_EmitsEachStage_WithEveryModulesPhaseNestedInside_InStageOrder()
    {
        var profiler = new StartupProfiler();
        using (EngineHooks.DiagnosticScopes.Subscribe(profiler))
        {
            BuiltInTestModules.BuildModules([]);
        }

        var stages = profiler.Phases.Where(phase => phase.Depth == 0).Select(phase => phase.Name).ToArray();
        CollectionAssert.AreEqual(new[] { "DeclareModules", "RegisterComponents", "Configure", "ResolveBlueprints", "RegisterBehavior" }, stages);

        var registerComponentsIndex = profiler.Phases.ToList().FindIndex(phase => phase.Name == "RegisterComponents");
        var modulePhases = profiler.Phases.Skip(registerComponentsIndex + 1).TakeWhile(phase => phase.Depth == 1).ToList();
        Assert.IsNotEmpty(modulePhases);
        Assert.IsTrue(modulePhases.All(phase => phase.Name.StartsWith("RegisterComponents:", StringComparison.Ordinal)), string.Join(", ", modulePhases));
    }

    [TestMethod]
    [DoNotParallelize]
    public void DiagnosticsEngine_WithStartup_ListensToScopes_UntilTheFirstSimulationFrame_ThenWritesThem()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"StartupProfilerTests-{Guid.NewGuid():N}");
        try
        {
            using (var engine = new DiagnosticsEngine(DiagnosticsFeatures.Startup, writesPeriodicReports: false, outputDirectory: outputDirectory))
            {
                engine.Start();
                Assert.IsNotNull(EngineHooks.DiagnosticScopes.Listener);
                using (EngineHooks.DiagnosticScope("World Session Setup"))
                {
                }

                new SystemManager().Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: 1));

                Assert.IsNull(EngineHooks.DiagnosticScopes.Listener);
            }

            var report = DiagnosticsReportReader.ReadStartupReport(File.ReadAllBytes(Directory.GetFiles(outputDirectory, "startup-*.json").Single()));
            Assert.IsFalse(report.IsStable);
            Assert.AreEqual("World Session Setup", report.Phases.Single().Name);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }
}
