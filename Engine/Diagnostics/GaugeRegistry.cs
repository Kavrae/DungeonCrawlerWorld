namespace Engine.Diagnostics;

/// <summary>The gauges registered on one EcsContext, where the values they read live.</summary>
/// <remarks>Frozen when the context begins its session, so the set a sampler takes is final, and cleared when the context is disposed, so no gauge keeps an ended session's objects alive.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class GaugeRegistry
{
    private readonly List<Gauge> _gauges = [];

    /// <summary>Every registered gauge, in registration order.</summary>
    public IReadOnlyList<Gauge> Gauges => _gauges;

    /// <summary>True once the owning context has begun its session.</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>Registers a gauge read by read.</summary>
    /// <exception cref="InvalidOperationException">The registry is frozen, or groupName already has a gauge named gaugeName.</exception>
    public void Register(string groupName, string gaugeName, GaugeKind kind, Func<double> read)
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException($"Gauge {groupName}/{gaugeName} was registered after the session began; register gauges while building the session.");
        }

        foreach (var gauge in _gauges)
        {
            if (gauge.GroupName == groupName && gauge.GaugeName == gaugeName)
            {
                throw new InvalidOperationException($"Gauge {groupName}/{gaugeName} is already registered.");
            }
        }

        _gauges.Add(new Gauge(groupName, gaugeName, kind, read));
    }

    internal void Freeze() => IsFrozen = true;

    internal void Clear() => _gauges.Clear();
}
