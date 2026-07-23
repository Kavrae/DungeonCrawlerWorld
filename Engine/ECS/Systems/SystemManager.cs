namespace Engine.ECS.Systems;

/// <summary>
/// Runs every registered system once per frame, passing each its rotating stripe index.
/// Rather than gating a system on a firing period (skipping most frames, then doing its
/// whole population's work in one lump), every system's Update runs every frame and
/// processes only Count/StripeCount of its own population per call -- entity striping (see
/// EntityStripeSet) -- so per-frame cost stays bounded even as population grows, instead of
/// spiking to O(population) on the one frame in N a periodic system would have fired.
/// SystemManager's only scheduling job is owning and advancing each system's stripe cursor,
/// centralized here since the increment-and-wrap logic is identical for every system.
/// </summary>
public sealed class SystemManager
{
    private readonly List<(ISystem System, byte CurrentStripe)> _systems = [];

    public void Register(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (system.StripeCount == 0)
        {
            throw new ArgumentException("StripeCount must be greater than zero.", nameof(system));
        }

        _systems.Add((system, 0));
    }

    public void Update(EngineTime time)
    {
        for (var i = 0; i < _systems.Count; i++)
        {
            var (system, stripeIndex) = _systems[i];

            system.Update(time, stripeIndex);

            _systems[i] = (system, (byte)((stripeIndex + 1) % system.StripeCount));
        }
    }
}