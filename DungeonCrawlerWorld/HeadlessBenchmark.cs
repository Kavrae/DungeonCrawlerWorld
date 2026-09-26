using System.Diagnostics;
using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;

namespace DungeonCrawlerWorld;

/// <summary>Runs the simulation with no window, no Presentation and no frame pacing, for FrameRangeBenchmark -- launched by "--headless".</summary>
/// <remarks>
/// Builds exactly the world GameLoop builds (same WorldSessionBootstrapper, same constants, same
/// seed), then calls EcsContext.Update back to back until the benchmark range closes, and exits.
///
/// Why it exists, beside the windowed benchmark:
/// - Speed. The windowed game is paced to 60 frames per second, so 3000 frames takes 50 seconds
///   however little work each frame does. Here it takes as long as the work itself -- a few
///   seconds -- which makes several repeats per measurement affordable.
/// - Fewer confounders. No Draw between frames evicting simulation data from cache, no GPU driver
///   threads, no idle time for power management to downclock the CPU in.
///
/// It does hold the same 1 ms Windows timer resolution SDL3 sets for the windowed game (see
/// WindowsTimerResolution), so short runtime waits cost what they cost the player.
///
/// What it does not measure: anything in Presentation (MapWindow alone is ~1.3 ms/frame), and the
/// in-game cost of a system with Draw running between frames, which is higher. Use it to compare
/// simulation changes against each other; use the windowed run for what the player actually pays.
///
/// The simulation is the same one: Game never reads EngineTime's wall-clock fields, only
/// FrameCount, and the shell adds no simulation input when nobody touches it. The player simply
/// stands still, as in a windowed benchmark nobody plays.
///
/// Prints a fingerprint of the final world state. Two runs of the same build and seed must print
/// the same one -- the phase-performance-testing script checks that, which is also the only
/// end-to-end test that the seed really does make the simulation deterministic.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal static class HeadlessBenchmark
{
    private const int FramesPerSecond = 60;

    /// <param name="diagnosticsFeatures">Memory adds a PoolMemoryReport over the same frame range; the other features have nothing to report headless.</param>
    public static int Run(int randomSeed, BenchmarkFrameRange frameRange, int? mapSizeOverride = null, DiagnosticsFeatures diagnosticsFeatures = DiagnosticsFeatures.None)
    {
        using var timerResolution = WindowsTimerResolution.Request(milliseconds: 1);
        var diagnostics = new DiagnosticsEngine(diagnosticsFeatures & DiagnosticsFeatures.Memory, randomSeed, frameRange);
        var modsDirectory = Path.Combine(AppContext.BaseDirectory, "Mods");

        // Its own file, so benchmark runs never append to the real Log/player-activity.log.
        var activityLogPath = Path.Combine(GameLoop.FindProjectRoot(), "Log", "diagnostics", $"headless-activity-{Environment.ProcessId}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(activityLogPath)!);

        var session = WorldSessionBootstrapper.Build(
            GameLoop.FloorNumber,
            modsDirectory,
            GameLoop.InitialEntityCapacity,
            GameLoop.InitialComponentCapacity,
            GameLoop.MinCrawlerNumber,
            GameLoop.CrawlerNumberBits,
            activityLogPath,
            diagnostics,
            randomSeed,
            mapSizeOverride);

        try
        {
            var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);

            // Mirrors GameLoop.Update's simulation half, in the same order: frame number first,
            // then the benchmark window, then the update itself.
            for (var frame = 1; ; frame++)
            {
                session.PlayerActivityLog.BeginFrame(frame, DateTime.Now);
                diagnostics.BeginSimulationFrame(frame);
                if (diagnostics.IsBenchmarkComplete)
                {
                    break;
                }

                var start = Stopwatch.GetTimestamp();
                session.EcsContext.Update(new EngineTime(frameDuration * frame, frameDuration, IsRunningSlowly: false, frame));
                diagnostics.RecordSimulationTick("GameLoop", "EcsContext.Update (all systems)", Stopwatch.GetElapsedTime(start));
            }

            Console.WriteLine($"[Headless] Fingerprint {Fingerprint(session)}");
            return 0;
        }
        finally
        {
            session.PlayerActivityLog.Dispose();
            File.Delete(activityLogPath);
        }
    }

    /// <summary>Holds the process's Windows timer resolution at a given period until disposed, the way SDL3 does for the windowed game.</summary>
    /// <remarks>
    /// Windows wakes a sleeping or timed-waiting thread on its timer tick, 15.6 ms unless the process asks
    /// for finer. The runtime waits briefly in places the simulation reaches -- measured as the end of a
    /// background gen-2 collection -- so without this a headless run shows 15 ms frames the player never
    /// gets. A no-op off Windows.
    /// </remarks>
    private sealed class WindowsTimerResolution : IDisposable
    {
        private readonly uint _milliseconds;

        private WindowsTimerResolution(uint milliseconds) => _milliseconds = milliseconds;

        public static WindowsTimerResolution? Request(uint milliseconds) =>
            OperatingSystem.IsWindows() && TimeBeginPeriod(milliseconds) == 0 ? new WindowsTimerResolution(milliseconds) : null;

        public void Dispose() => TimeEndPeriod(_milliseconds);

        [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint milliseconds);

        [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint milliseconds);
    }

    /// <summary>
    /// A hash of the final world state: living entity count, every pool's size, every positioned
    /// entity's position, and every simple-health entity's health. Cheap relative to a run (one
    /// pass over each), and sensitive to any divergence that matters -- one extra step, hit, death
    /// or spawn changes it.
    /// </summary>
    private static string Fingerprint(WorldSessionContext session)
    {
        var componentManager = session.EcsContext.ComponentManager;
        var hash = new StableHash();

        hash.Add(session.EcsContext.EntityManager.LivingEntityCount);

        foreach (var pool in componentManager.AllPools
            .OfType<IMemoryReportingComponentPool>()
            .OrderBy(static pool => pool.ComponentType.FullName, StringComparer.Ordinal))
        {
            hash.Add(pool.ComponentType.FullName ?? pool.ComponentType.Name);
            hash.Add(pool.Count);
        }

        var transforms = componentManager.GetDirectPool<TransformComponent>();
        for (var entityId = 0; entityId < transforms.Capacity; entityId++)
        {
            if (transforms.TryGetReadonly(entityId, out var transform))
            {
                hash.Add(entityId);
                hash.Add(transform.Position.X);
                hash.Add(transform.Position.Y);
                hash.Add(transform.Position.Z);
            }
        }

        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        for (var denseIndex = 0; denseIndex < health.Count; denseIndex++)
        {
            hash.Add(health.GetEntityIdByDenseIndex(denseIndex));
            hash.Add(BitConverter.SingleToInt32Bits(health.GetReadonlyByDenseIndex(denseIndex).CurrentHealth));
        }

        return hash.Value.ToString("X16");
    }

    /// <summary>
    /// 64-bit FNV-1a. Not System.HashCode or string.GetHashCode: both are seeded randomly per
    /// process, so two identical worlds would fingerprint differently -- exactly the comparison
    /// this exists for.
    /// </summary>
    private struct StableHash()
    {
        private const ulong Prime = 1099511628211;

        public ulong Value { get; private set; } = 14695981039346656037;

        public void Add(int value)
        {
            for (var shift = 0; shift < 32; shift += 8)
            {
                Value = (Value ^ (byte)(value >> shift)) * Prime;
            }
        }

        public void Add(string value)
        {
            foreach (var character in value)
            {
                Add(character);
            }
        }
    }
}
