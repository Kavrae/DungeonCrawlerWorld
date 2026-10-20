using Engine.Diagnostics;
using System.Diagnostics;

namespace Engine.ECS.Systems;

/// <summary> Runs every registered system once per frame, passing each its rotating stripe index. </summary>
/// <remarks>
/// Systems run in two lanes, in this order: the frame start -- systems that feed the frame rather than react to it,
/// each placed by an explicit order (RegisterAtFrameStart) -- then every other system, in the order registered
/// (Register: modules' systems, in module sort order). Where a system lands never depends on when it was registered.
/// The lanes are flattened into one run order when registration changes, never per frame, and registration closes
/// when the session begins (EcsContext.BeginSession).
/// </remarks>
/// <cleanupVersion>2</cleanupVersion>
/// <param name="clock">The clock Update advances; a clock of its own when null. EcsBuilder passes the one it gave the build's context.</param>
/// <param name="simulatedTierCount">See SimulatedTierCount.</param>
public sealed class SystemManager(SimulationClock? clock = null, int simulatedTierCount = int.MaxValue)
{
    /// <summary>
    /// How many processing tiers, from the lowest index up, are simulated for every
    /// <see cref="ITieredSystem"/>. Tiers at or past this are skipped entirely: their entities are
    /// not visited and their countdowns do not advance. Defaults to every tier. The game supplies
    /// the value (EcsBuildRequest.SimulatedTierCount); this layer never learns what any tier means.
    /// See ITieredSystem's own remarks.
    /// </summary>
    public int SimulatedTierCount { get; } = simulatedTierCount;

    /// <summary>
    /// Advanced to each update's EngineTime.FrameCount before any system runs, so everything that
    /// reads it during or after that frame sees the frame being simulated. See SimulationClock's own remarks.
    /// </summary>
    public SimulationClock Clock { get; } = clock ?? new SimulationClock();

    private readonly SortedList<int, SystemEntry> _frameStartSystems = [];
    private readonly List<SystemEntry> _moduleSystems = [];
    private readonly List<IFrameScoped> _frameScopedBuffers = [];

    private SystemEntry[] _runOrder = [];
    private bool _isRunOrderStale;
    private bool _isRegistrationClosed;

    /// <summary>One registered system and its rotating stripe index.</summary>
    private sealed class SystemEntry(ISystem system)
    {
        public ISystem System { get; } = system;

        public ITieredSystem? TieredSystem { get; } = system as ITieredSystem;

        public byte CurrentStripe;
    }

    /// <summary>Registers a system to run each frame after the frame start's systems and every system registered before it.</summary>
    /// <exception cref="ArgumentException">The system's StripeCount is zero.</exception>
    /// <exception cref="InvalidOperationException">The session already began.</exception>
    public void Register(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        ThrowIfCannotRegister(system);

        _moduleSystems.Add(new SystemEntry(system));
        _isRunOrderStale = true;
    }

    /// <summary>Registers a system to run each frame before every system registered with Register, at order among the frame start's systems (lower runs first).</summary>
    /// <remarks>For a system that feeds the frame rather than reacting to it -- one that records into a FrameEventBuffer every other system reads this frame. The game names its orders in one place, so two systems' places never depend on which registered first.</remarks>
    /// <exception cref="ArgumentException">The system's StripeCount is zero.</exception>
    /// <exception cref="InvalidOperationException">Another system already holds order at the frame start, or the session already began.</exception>
    public void RegisterAtFrameStart(ISystem system, int order)
    {
        ArgumentNullException.ThrowIfNull(system);
        ThrowIfCannotRegister(system);

        if (!_frameStartSystems.TryAdd(order, new SystemEntry(system)))
        {
            throw new InvalidOperationException($"{system.GetType().Name} can't run at frame-start order {order}: {_frameStartSystems[order].System.GetType().Name} already does.");
        }

        _isRunOrderStale = true;
    }

    /// <summary>See FrameEventBuffer/IFrameScoped's own doc comment for why this is cleared here, once per cycle, rather than by its own producer.</summary>
    /// <exception cref="InvalidOperationException">The session already began.</exception>
    public void RegisterFrameScoped(IFrameScoped buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfRegistrationClosed(buffer.GetType().Name);

        _frameScopedBuffers.Add(buffer);
    }

    /// <summary>Fixes the run order and refuses any later registration: what the session simulates is decided before it begins.</summary>
    internal void CloseRegistration()
    {
        RefreshRunOrder();
        _isRegistrationClosed = true;
    }

    private void ThrowIfCannotRegister(ISystem system)
    {
        if (system.StripeCount == 0)
        {
            throw new ArgumentException("StripeCount must be greater than zero.", nameof(system));
        }

        ThrowIfRegistrationClosed(system.GetType().Name);
    }

    private void ThrowIfRegistrationClosed(string registeredName)
    {
        if (_isRegistrationClosed)
        {
            throw new InvalidOperationException($"{registeredName} was registered after the session began; register systems while building the session.");
        }
    }

    private void RefreshRunOrder()
    {
        if (!_isRunOrderStale)
        {
            return;
        }

        _runOrder = [.. _frameStartSystems.Values, .. _moduleSystems];
        _isRunOrderStale = false;
    }

    /// <summary>Runs every registered system once: the frame start's systems by order, then every other system in registration order.</summary>
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

        RefreshRunOrder();
        Clock.Advance(time.FrameCount);
        IsUpdating = true;
        var frameCostRecorder = EngineHooks.FrameCosts.Listener;
        var runOrder = _runOrder;

        try
        {
            for (var i = 0; i < runOrder.Length; i++)
            {
                var entry = runOrder[i];
                var stripeIndex = entry.CurrentStripe;

                if (frameCostRecorder is not null)
                {
                    var start = Stopwatch.GetTimestamp();
                    Run(entry, time, stripeIndex);
                    frameCostRecorder.Record(FrameCostCategory.Update, "SystemManager", entry.System.GetType().Name, Stopwatch.GetElapsedTime(start));
                }
                else
                {
                    Run(entry, time, stripeIndex);
                }

                entry.CurrentStripe = (byte)((stripeIndex + 1) % entry.System.StripeCount);
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
    private void Run(SystemEntry entry, EngineTime time, byte stripeIndex)
    {
        if (entry.TieredSystem is { } tieredSystem)
        {
            TieredSystemRunner.Run(tieredSystem, time, SimulatedTierCount);
        }
        else
        {
            entry.System.Update(time, stripeIndex);
        }
    }
}
