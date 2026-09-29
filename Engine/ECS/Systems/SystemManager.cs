using Engine.Diagnostics;
using System.Diagnostics;

namespace Engine.ECS.Systems;

/// <summary> Runs every registered system once per frame, passing each its rotating stripe index. </summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SystemManager
{
    private readonly List<(ISystem System, byte CurrentStripe)> _systems = [];
    private readonly List<IFrameScoped> _frameScopedBuffers = [];

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
    /// default with the instance its modules were configured against (GameBuildPass) -- the same
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

    /// <summary>Registers a system to be updated each frame before every system registered so far.</summary>
    /// <remarks>For a system that feeds the frame rather than reacting to it -- one that records into a FrameEventBuffer every other system reads this frame.</remarks>
    public void RegisterFirst(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (system.StripeCount == 0)
        {
            throw new ArgumentException("StripeCount must be greater than zero.", nameof(system));
        }

        _systems.Insert(0, (system, 0));
    }

    /// <summary>See FrameEventBuffer/IFrameScoped's own doc comment for why this is cleared here, once per cycle, rather than by its own producer.</summary>
    public void RegisterFrameScoped(IFrameScoped buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        _frameScopedBuffers.Add(buffer);
    }

    /// <summary>Runs every registered system once, in registration order.</summary>
    /// <remarks>
    /// The frame boundary for every host: EngineHooks.SimulationFrames hears the frame start before the
    /// clock advances and its end after the frame-scoped buffers clear. While EngineHooks.FrameCosts has a
    /// listener, each system's wall-clock cost is recorded under FrameCostCategory.Update, group
    /// "SystemManager", item = the system's type name. That is a branch on the listener rather than an
    /// EngineHooks.FrameCost wrapper, because it runs for every system every frame: with nothing listening
    /// each system runs as a bare call, with no try/finally and no arguments evaluated.
    /// </remarks>
    public void Update(EngineTime time)
    {
        var simulationFrameListener = EngineHooks.SimulationFrames.Listener;
        var frameStartTimestamp = 0L;
        if (simulationFrameListener is not null)
        {
            simulationFrameListener.SimulationFrameStarting(time.FrameCount);
            frameStartTimestamp = Stopwatch.GetTimestamp();
        }

        Clock.Advance(time.FrameCount);
        IsUpdating = true;
        var frameCostRecorder = EngineHooks.FrameCosts.Listener;

        try
        {
            for (var i = 0; i < _systems.Count; i++)
            {
                var (system, stripeIndex) = _systems[i];

                if (frameCostRecorder is not null)
                {
                    var start = Stopwatch.GetTimestamp();
                    Run(system, time, stripeIndex);
                    frameCostRecorder.Record(FrameCostCategory.Update, "SystemManager", system.GetType().Name, Stopwatch.GetElapsedTime(start));
                }
                else
                {
                    Run(system, time, stripeIndex);
                }

                _systems[i] = (system, (byte)((stripeIndex + 1) % system.StripeCount));
            }
        }
        finally
        {
            IsUpdating = false;
        }

        foreach (var buffer in _frameScopedBuffers)
        {
            buffer.ClearFrame();
        }

        simulationFrameListener?.SimulationFrameEnded(time.FrameCount, Stopwatch.GetElapsedTime(frameStartTimestamp));
    }

    /// <summary>True while Update is running systems -- the simulation's own reads and writes, as opposed to presentation or input between frames.</summary>
    public bool IsUpdating { get; private set; }

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