using Engine.Diagnostics;
using System.Diagnostics;

namespace Engine.ECS.Systems;

/// <summary> Runs every registered system once per frame, passing each its rotating stripe index. </summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SystemManager
{
    private readonly List<(ISystem System, byte CurrentStripe)> _systems = [];
    private readonly List<IFrameScoped> _frameScopedBuffers = [];

    /// <summary>Opt-in per-system wall-clock cost tracking, keyed by each system's GetType().Name -- see PhaseProfiler's own doc comment. Null (the default) skips the Stopwatch calls entirely, so this costs nothing unless a caller (e.g. GameLoop, tracking down a gameplay demo's actual frame cost) wires one in.</summary>
    public PhaseProfiler? Profiler { get; set; }

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
        for (var i = 0; i < _systems.Count; i++)
        {
            var (system, stripeIndex) = _systems[i];

            if (Profiler is { } profiler)
            {
                var start = Stopwatch.GetTimestamp();
                system.Update(time, stripeIndex);
                profiler.Record(system.GetType().Name, Stopwatch.GetElapsedTime(start));
            }
            else
            {
                system.Update(time, stripeIndex);
            }

            _systems[i] = (system, (byte)((stripeIndex + 1) % system.StripeCount));
        }

        foreach (var buffer in _frameScopedBuffers)
        {
            buffer.ClearFrame();
        }
    }
}