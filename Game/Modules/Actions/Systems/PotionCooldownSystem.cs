using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Activators;

namespace Game.Modules.Actions.Systems;

/// <summary>
/// Passively counts every PotionCooldownComponent's FramesRemaining down toward 0, removing it
/// entirely once it reaches 0 -- mirrors ActionLockSystem's shape, but StripeCount 1 (not
/// tiered): only entities that have actually consumed a potion carry this component at all, so
/// the population visited is already small regardless of distance from the player. Drives the
/// decrement/remove loop through the shared CountdownTicker (see PotionCooldownComponent's own
/// ITickCountdown bridge) rather than hand-rolling it -- the same utility BurningSystem/
/// PoisonSystem/ParalysisSystem/ContactDamageSystem already share; onTick always returns true
/// since PotionCooldownComponent carries no other cleanup on expiry, the same "no re-arm" shape
/// TorchMarkExpirySystem uses.
/// </summary>
public sealed class PotionCooldownSystem : ISystem
{
    public byte StripeCount => 1;

    private readonly PackedComponentPool<PotionCooldownComponent> _cooldowns;
    private readonly List<int> _pendingRemovals = [];
    private readonly Func<int, PotionCooldownComponent, bool> _tick;

    public PotionCooldownSystem(PackedComponentPool<PotionCooldownComponent> cooldowns)
    {
        _cooldowns = cooldowns;
        _tick = static (_, _) => true;
    }

    public void Update(EngineTime time, byte stripeIndex) =>
        CountdownTicker.Tick(_cooldowns, _cooldowns.EntityIds, _pendingRemovals, _tick);
}
