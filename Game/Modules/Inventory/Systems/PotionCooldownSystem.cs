using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory.Systems;

/// <summary>
/// Passively counts every PotionCooldownComponent's FramesRemaining down toward 0, removing it
/// entirely once it reaches 0 -- mirrors ActionLockSystem's shape, but StripeCount 1 (not
/// tiered): only entities that have actually consumed a potion carry this component at all, so
/// the population visited is already small regardless of distance from the player.
/// </summary>
public sealed class PotionCooldownSystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<PotionCooldownComponent> _cooldowns;

    // Reused across calls, cleared each Update -- entities whose cooldown expired this frame
    // can't be removed mid-scan (PackedComponentPool.Remove swaps the last entry into the
    // removed slot, corrupting EntityIds' current enumeration -- see CountdownTicker's own doc
    // comment for the same rule), so removal is collected here and applied only after the scan.
    private readonly List<int> _pendingRemovals = [];

    public PotionCooldownSystem(PackedComponentPool<PotionCooldownComponent> cooldowns)
    {
        _cooldowns = cooldowns;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        _pendingRemovals.Clear();

        foreach (var entityId in _cooldowns.EntityIds)
        {
            var cooldown = _cooldowns.GetReadonly(entityId);

            if (cooldown.FramesRemaining <= StripeCountValue)
            {
                _pendingRemovals.Add(entityId);
                continue;
            }

            _cooldowns.TryUpdate(entityId, static (ref PotionCooldownComponent c) =>
            {
                c.FramesRemaining -= StripeCountValue;
            });
        }

        foreach (var entityId in _pendingRemovals)
        {
            _cooldowns.Remove(entityId);
        }
    }
}
