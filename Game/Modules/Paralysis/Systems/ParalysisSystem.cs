using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Paralysis.Components;

namespace Game.Modules.Paralysis.Systems;

/// <summary>
/// Ticks Paralysis's own countdown down to 0 and removes the timer once it expires. Does not
/// touch ActionLockComponent: ParalysisEffects.Apply already locked it to the same
/// DurationFrames at grant time, and ActionLockSystem decrements it at the same real-frame rate
/// independently, so both expire in lockstep without this system re-asserting anything. Also
/// does not touch SimpleHealthComponent -- Paralysis has no damage component at all, unlike
/// Burning/Poison, proving a status effect can apply to entities without hit points.
/// </summary>
public sealed class ParalysisSystem : ISystem
{
    public byte StripeCount => 1;

    private readonly PackedComponentPool<ParalysisTimerComponent> _timers;
    private readonly List<int> _pendingTimerRemovals = [];

    // Cached once instead of passing the Tick method group at the CountdownTicker.Tick call
    // site every Update -- see ContactDamageSystem's own field for why this matters (an
    // instance method group conversion allocates a fresh delegate every evaluation).
    private readonly Func<int, ParalysisTimerComponent, bool> _tick;

    public ParalysisSystem(PackedComponentPool<ParalysisTimerComponent> timers)
    {
        _timers = timers;
        _tick = Tick;
    }

    public void Update(EngineTime time, byte stripeIndex) =>
        CountdownTicker.Tick(_timers, _timers.EntityIds, _pendingTimerRemovals, _tick);

    /// <summary>Always returns true (remove) -- see CountdownTicker.Tick's own doc comment for the contract. There's no repeating action to re-arm for, unlike Burning/Poison.</summary>
    private bool Tick(int entityId, ParalysisTimerComponent timer) => true;
}
