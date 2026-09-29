using Engine.Diagnostics;
using Engine.ECS.Systems;

namespace Tests.Diagnostics;

[TestClass]
public sealed class StartupProfilerTests
{
    // Tick()'s stability detection depends on real wall-clock gaps between calls, which isn't
    // something to assert on in a fast unit test (same reasoning EventBusTests gives for not
    // asserting on FrameBudgetTracker's real-time-driven Snapshot/TopEntries) -- these cover
    // scope recording and WriteReport()'s output shape instead.

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
            RecordScope(profiler, new DiagnosticScope("RegisterSystems"));
        });

        CollectionAssert.AreEqual(
            new[] { ("Module Load", 0), ("Configure", 1), ("Configure:HealthModule", 2), ("RegisterSystems", 1) },
            profiler.Phases.Select(phase => (phase.Name, phase.Depth)).ToArray());
    }

    [TestMethod]
    public void NewProfiler_IsNotYetStable()
    {
        var profiler = new StartupProfiler();

        Assert.IsFalse(profiler.IsStable);
        Assert.IsNull(profiler.TimeToStable);
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
        CollectionAssert.AreEqual(new[] { "RegisterComponents", "Configure", "ResolveBlueprints", "RegisterSystems" }, stages);

        var registerComponentsIndex = profiler.Phases.ToList().FindIndex(phase => phase.Name == "RegisterComponents");
        var modulePhases = profiler.Phases.Skip(registerComponentsIndex + 1).TakeWhile(phase => phase.Depth == 1).ToList();
        Assert.IsNotEmpty(modulePhases);
        Assert.IsTrue(modulePhases.All(phase => phase.Name.StartsWith("RegisterComponents:", StringComparison.Ordinal)), string.Join(", ", modulePhases));
    }

    [TestMethod]
    [DoNotParallelize]
    public void DiagnosticsEngine_WithStartup_ListensToScopes_UntilTheFirstSimulationFrame()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.Startup);
        engine.Start();
        Assert.IsNotNull(EngineHooks.DiagnosticScopes.Listener);

        new SystemManager().Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: 1));

        Assert.IsNull(EngineHooks.DiagnosticScopes.Listener);
    }
}
