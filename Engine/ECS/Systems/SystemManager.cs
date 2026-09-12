using Engine.Diagnostics;
using System.Diagnostics;

namespace Engine.ECS.Systems;

/// <summary> Runs every registered system once per frame, passing each its rotating stripe index. </summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SystemManager
{
    private readonly List<(ISystem System, byte CurrentStripe)> _systems = [];
    private readonly List<IFrameScoped> _frameScopedBuffers = [];

    /// <summary>Opt-in per-system wall-clock cost tracking, recorded under FrameCostCategory.Update, group "SystemManager", item = each system's GetType().Name -- see FrameBudgetTracker's own doc comment. Null (the default) skips the Stopwatch calls entirely, so this costs nothing unless a caller (e.g. GameLoop, tracking down a gameplay demo's actual frame cost) wires one in.</summary>
    public IFrameCostRecorder? Profiler { get; set; }

    /// <summary>
    /// How many processing tiers, from the lowest index up, are simulated for every
    /// <see cref="ITieredSystem"/>. Tiers at or past this are skipped entirely: their entities are
    /// not visited and their countdowns do not advance. Defaults to every tier. The game supplies
    /// the value; this layer never learns what any tier means. See ITieredSystem's own remarks.
    /// </summary>
    public int SimulatedTierCount { get; set; } = int.MaxValue;

    /// <summary>
    /// Advanced to each update's EngineTime.FrameCount before any system runs, so everything that
    /// reads it during or after that frame sees the frame being simulated. The game replaces this
    /// default with the instance its modules were configured against (GameBootstrapper) -- the same
    /// injected-policy shape as SimulatedTierCount. See SimulationClock's own remarks.
    /// </summary>
    public SimulationClock Clock { get; set; } = new();

    /// <summary>Register a system to be updated each frame.</summary>
    /// <param name="system">The system to register.</param>
    /// <exception cref="ArgumentException">Thrown when the system's StripeCount is zero.</exception>
    public void Register(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (system.StripeCount == 0)
        {
            throw new ArgumentException("StripeCount must be greater than zero.", nameof(system));
        }

        _systems.Add((system, 0));
    }

    /// <summary>See FrameEventBuffer/IFrameScoped's own doc comment for why this is cleared here, once per cycle, rather than by its own producer.</summary>
    public void RegisterFrameScoped(IFrameScoped buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        _frameScopedBuffers.Add(buffer);
    }

    public void Update(EngineTime time)
    {
        Clock.Advance(time.FrameCount);

        for (var i = 0; i < _systems.Count; i++)
        {
            var (system, stripeIndex) = _systems[i];

            if (Profiler is { } profiler)
            {
                var start = Stopwatch.GetTimestamp();
                Run(system, time, stripeIndex);
                profiler.Record(FrameCostCategory.Update, "SystemManager", system.GetType().Name, Stopwatch.GetElapsedTime(start));
            }
            else
            {
                Run(system, time, stripeIndex);
            }

            _systems[i] = (system, (byte)((stripeIndex + 1) % system.StripeCount));
        }

        foreach (var buffer in _frameScopedBuffers)
        {
            buffer.ClearFrame();
        }
    }

    /// <summary>A tiered system runs through TieredSystemRunner with this manager's tier policy -- deliberately not through its own Update, which runs every tier. A plain system gets its rotating stripe index, which a tiered system has no use for since it keys off EngineTime.FrameCount.</summary>
    private void Run(ISystem system, EngineTime time, byte stripeIndex)
    {
        if (system is ITieredSystem tiered)
        {
            TieredSystemRunner.Run(tiered, time, SimulatedTierCount);
        }
        else
        {
            system.Update(time, stripeIndex);
        }
    }
}