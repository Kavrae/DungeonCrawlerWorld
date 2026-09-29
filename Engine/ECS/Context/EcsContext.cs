using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;

namespace Engine.ECS.Context;

/// <summary>Composition root bundling the three ECS managers plus the shared EventBus for a running game.</summary>
/// <remarks>
/// Every build produces one, including builds that are never simulated (a mod's trial build, a staging
/// rebuild). The host marks the one it simulates with BeginSession, and ends it by disposing it; only
/// those two emit EngineHooks.Sessions.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EcsContext(EntityManager entityManager, ComponentManager componentManager, SystemManager systemManager, EventBus eventBus) : IDisposable
{
    private bool _isSessionStarted;
    private bool _isDisposed;

    public EntityManager EntityManager { get; } = entityManager;
    public ComponentManager ComponentManager { get; } = componentManager;
    public SystemManager SystemManager { get; } = systemManager;
    public EventBus EventBus { get; } = eventBus;

    /// <summary>The named values diagnostics samples from this context once its session begins; register them while building it.</summary>
    public GaugeRegistry Gauges { get; } = CreateEngineGauges(entityManager, componentManager);

    /// <summary>The game's split of living entities for diagnostics that compare a pool against the entities that can hold it; null compares every pool against every living entity.</summary>
    /// <exception cref="InvalidOperationException">Set after the session began.</exception>
    public EntityPopulationPolicy? EntityPopulations
    {
        get;
        set
        {
            if (_isSessionStarted)
            {
                throw new InvalidOperationException("EntityPopulations was set after the session began; set it while building the session.");
            }

            field = value;
        }
    }

    private static GaugeRegistry CreateEngineGauges(EntityManager entityManager, ComponentManager componentManager)
    {
        var gauges = new GaugeRegistry();
        gauges.Register("Entities", "Living", GaugeKind.Level, () => entityManager.LivingEntityCount);
        gauges.Register("Entities", "Capacity", GaugeKind.Level, () => entityManager.Capacity);
        gauges.Register("Pools", "Components", GaugeKind.Level, () => SumPools(componentManager, static pool => pool.Count));
        gauges.Register("Pools", "EstimatedBytes", GaugeKind.Level, () => SumPools(componentManager, static pool => pool.EstimatedBytes));
        return gauges;
    }

    private static double SumPools(ComponentManager componentManager, Func<IMemoryReportingComponentPool, double> measure)
    {
        var total = 0d;
        foreach (var pool in componentManager.AllPools)
        {
            if (pool is IMemoryReportingComponentPool memoryReportingPool)
            {
                total += measure(memoryReportingPool);
            }
        }

        return total;
    }

    /// <summary>Updates the ECS context via the system manager.</summary>
    /// <param name="time">The current engine time.</param>
    public void Update(EngineTime time) => SystemManager.Update(time);

    /// <summary>Marks this context as the simulated session, once the host has finished building it, and freezes its gauges.</summary>
    /// <exception cref="InvalidOperationException">The session already began, or this context was disposed.</exception>
    public void BeginSession()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isSessionStarted)
        {
            throw new InvalidOperationException("This EcsContext's session already began.");
        }

        Gauges.Freeze();
        EngineHooks.Sessions.Listener?.SessionStarted(this);
        _isSessionStarted = true;
    }

    /// <summary>Ends the session, if it began, while every pool is still intact, then drops its gauges.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_isSessionStarted)
        {
            EngineHooks.Sessions.Listener?.SessionEnding(this);
        }

        Gauges.Clear();
    }
}
