using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Paralysis.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.Paralysis.Systems;

/// <summary>
/// Ticks Paralysis's own countdown down to 0 and removes the timer once it expires. Does not
/// touch ActionLockComponent: ParalysisEffects.Apply already locked it to the same
/// DurationFrames at grant time, and ActionLockSystem decrements it at the same real-frame rate
/// independently, so both expire in lockstep without this system re-asserting anything. Also
/// does not touch SimpleHealthComponent -- Paralysis has no damage component at all, unlike
/// Burning/Poison, proving a status effect can apply to entities without hit points.
/// </summary>
public sealed class ParalysisSystem : ITieredSystem
{
    private const byte StripeCountValue = 15;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<ParalysisTimerComponent> _timers;
    private readonly TieredEntityStripeSet _tieredStripeSet;
    private readonly List<int> _pendingTimerRemovals = [];

    // Cached once instead of passing the Tick method group at the CountdownTicker.Tick call
    // site every Update -- see ContactDamageSystem's own field for why this matters (an
    // instance method group conversion allocates a fresh delegate every evaluation).
    private readonly Func<int, ParalysisTimerComponent, bool> _tick;

    public ParalysisSystem(
        PackedComponentPool<ParalysisTimerComponent> timers,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents)
    {
        _timers = timers;
        _tick = Tick;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, timers, processingTiers, processingTierEvents);
    }

    /// <summary>
    /// Tiered, matching every other CountdownTicker-driven system. Was StripeCount 1 and untiered
    /// -- a full pool scan every frame -- on the reasonable-sounding grounds that paralysis is
    /// rare. PotionCooldownSystem carried the identical justification and turned out to be one of
    /// the largest costs in the simulation once its population grew unnoticed, so that assumption
    /// is not one to rest on. The lockstep with ActionLockComponent this class's own doc comment
    /// describes is unaffected: both count down in real time, this one in StripeCount * divisor
    /// steps and ActionLockSystem in its own, so they still expire together in aggregate even
    /// though the granularity differs.
    /// </summary>
    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    /// <summary>Advances each due entity's countdown by framesPerVisit -- see ITieredSystem.UpdateBucket.</summary>
    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit) =>
        CountdownTicker.Tick(_timers, entityIds, _pendingTimerRemovals, _tick, framesPerVisit);

    /// <summary>Always returns true (remove) -- see CountdownTicker.Tick's own doc comment for the contract. There's no repeating action to re-arm for, unlike Burning/Poison.</summary>
    private bool Tick(int entityId, ParalysisTimerComponent timer) => true;
}
