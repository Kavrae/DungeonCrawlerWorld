using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Activators;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.Actions.Systems;

/// <summary>
/// Passively counts every PotionCooldownComponent's FramesRemaining down toward 0, removing it
/// entirely once it reaches 0 -- mirrors ActionLockSystem's shape. Drives the decrement/remove
/// loop through the shared CountdownTicker (see PotionCooldownComponent's own ITickCountdown
/// bridge) rather than hand-rolling it -- the same utility BurningSystem/PoisonSystem/
/// ParalysisSystem/ContactDamageSystem already share; onTick always returns true since
/// PotionCooldownComponent carries no other cleanup on expiry, the same "no re-arm" shape
/// TorchMarkExpirySystem uses.
/// </summary>
/// <remarks>
/// Was StripeCount 1 and untiered, on the stated grounds that "only entities that have actually
/// consumed a potion carry this component at all, so the population visited is already small
/// regardless of distance from the player." Measurement contradicted that: a diagnostics memory
/// capture showed 9,110 live PotionCooldownComponents, and this system -- scanning all of them
/// every single frame -- was one of the largest costs in the simulation despite doing nothing but
/// decrementing integers. Tiered now, like every other countdown system.
/// </remarks>
public sealed class PotionCooldownSystem : ITieredSystem
{
    private const byte StripeCountValue = 10;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PotionCooldownComponent> _cooldowns;
    private readonly TieredEntityStripeSet _tieredStripeSet;
    private readonly List<int> _pendingRemovals = [];
    private readonly Func<int, PotionCooldownComponent, bool> _tick;

    public PotionCooldownSystem(
        PackedComponentPool<PotionCooldownComponent> cooldowns,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents)
    {
        _cooldowns = cooldowns;
        _tick = static (_, _) => true;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, cooldowns, processingTiers, processingTierEvents);
    }

    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    /// <summary>Advances each due entity's countdown by framesPerVisit -- see ITieredSystem.UpdateBucket.</summary>
    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit) =>
        CountdownTicker.Tick(_cooldowns, entityIds, _pendingRemovals, _tick, framesPerVisit);
}
