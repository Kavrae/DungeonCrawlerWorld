using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.ProcessingTier;

/// <summary>
/// Shared across every module's Configure call within one GameModuleContext -- same shape and
/// reasoning as StatusEffectAuraApplierRegistry/MovedEntities on GameModuleContext itself: a
/// module subscribing to TierChanged doesn't need ProcessingTierModule to have run its own
/// Configure/RegisterBehavior first, since subscribing to an event only needs the event's
/// owning object to exist, not for anything to have fired yet.
/// </summary>
public sealed class ProcessingTierEvents
{
    /// <summary>Raised for every tier change before TierChanged, with the new tier already written -- for work every TierChanged handler must find done, like building a creature skeleton promoted into a simulated tier.</summary>
    public event Action<int, ProcessingTierLevel>? TierChanging;

    public event Action<int, ProcessingTierLevel>? TierChanged;

    internal void RaiseTierChanged(int entityId, ProcessingTierLevel tier)
    {
        TierChanging?.Invoke(entityId, tier);
        TierChanged?.Invoke(entityId, tier);
    }
}
