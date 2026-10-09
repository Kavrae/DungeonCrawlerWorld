using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Utilities;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.World;

namespace Game.Modules.Health;

/// <summary>Records how much health each source entity has taken off each victim, and names the one that took the most.</summary>
/// <remarks>
/// Only entity sources are recorded: terrain, aura, Admin and AI damage credits nobody, and neither
/// does an entity damaging itself. A victim's whole ledger resets once it has gone ResetAfterFrames
/// without recorded damage (DamageLedgerExpirySystem), so any hit from anyone keeps every
/// contributor's total. A dead victim records nothing more.
/// </remarks>
public sealed class DamageLedger(
    MultiComponentPool<DamageContributionComponent> contributions,
    PackedComponentPool<DamageLedgerExpiryComponent> expiries,
    PackedComponentPool<DeadComponent> deadEntities,
    EntityKeys entityKeys)
{
    public const int ResetAfterFrames = 30 * GameTiming.FramesPerSecond;

    public static DamageLedger For(ComponentManager componentManager, EntityKeys entityKeys) => new(
        componentManager.GetMultiPool<DamageContributionComponent>(),
        componentManager.GetPackedPool<DamageLedgerExpiryComponent>(),
        componentManager.GetPackedPool<DeadComponent>(),
        entityKeys);

    /// <param name="healthRemoved">Health the hit actually took off the victim, after modifiers and capped at what it had left.</param>
    public void Record(int victimEntityId, ActionSource source, float healthRemoved, long now)
    {
        if (healthRemoved <= 0 || source.Kind != ActionSourceKind.Entity || source.Key.IsNone || IsEntityItself(victimEntityId, source) || deadEntities.Has(victimEntityId))
        {
            return;
        }

        var sourceEntityKey = source.Key;

        var frame = (uint)now;
        RecordLastDamagedFrame(victimEntityId, frame, now);

        var updated = contributions.TryUpdateFirst(victimEntityId, (SourceEntityKey: sourceEntityKey, Amount: healthRemoved),
            static (ref readonly DamageContributionComponent contribution, (EntityKey SourceEntityKey, float Amount) state) => contribution.Source.Key == state.SourceEntityKey,
            static (ref DamageContributionComponent contribution, (EntityKey SourceEntityKey, float Amount) state) =>
                contribution = contribution with { TotalDamageDealt = contribution.TotalDamageDealt + state.Amount });

        if (!updated)
        {
            contributions.Add(victimEntityId, new DamageContributionComponent(source, healthRemoved, frame));
        }
    }

    /// <summary>Whether source is entityId itself.</summary>
    public bool IsEntityItself(int entityId, ActionSource source) =>
        source.Kind == ActionSourceKind.Entity && source.Key == entityKeys.GetKey(entityId);

    /// <summary>The source that has taken the most health off victimEntityId since its ledger last reset; a tie goes to whoever hit first.</summary>
    public bool TryGetTopContributor(int victimEntityId, out EntityKey topContributorEntityKey)
    {
        topContributorEntityKey = EntityKey.None;
        var topTotal = 0f;
        var topFirstHitFrame = uint.MaxValue;

        for (var denseIndex = contributions.GetFirstDenseIndex(victimEntityId); denseIndex != -1; denseIndex = contributions.GetNextDenseIndex(denseIndex))
        {
            ref readonly var contribution = ref contributions.GetReadonlyByDenseIndex(denseIndex);
            var isHigher = contribution.TotalDamageDealt > topTotal ||
                (contribution.TotalDamageDealt == topTotal && contribution.FirstHitFrame < topFirstHitFrame);

            if (isHigher)
            {
                topContributorEntityKey = contribution.Source.Key;
                topTotal = contribution.TotalDamageDealt;
                topFirstHitFrame = contribution.FirstHitFrame;
            }
        }

        return !topContributorEntityKey.IsNone;
    }

    /// <summary>Forgets every contribution to victimEntityId.</summary>
    public void Clear(int victimEntityId)
    {
        contributions.Remove(victimEntityId);
        expiries.Remove(victimEntityId);
    }

    /// <summary>Handles victimEntityId's expiry deadline arriving: re-arms it if the victim was damaged since it was set, otherwise clears the ledger.</summary>
    /// <returns>True when the expiry component should be removed -- see TimerFired.</returns>
    internal bool ExpireOrRearm(int victimEntityId, DamageLedgerExpiryComponent expiry, long now)
    {
        var resetFrame = FrameDeadline.After(expiry.LastDamagedFrame, ResetAfterFrames);
        if (resetFrame > now)
        {
            expiries.TryUpdate(victimEntityId, resetFrame, static (ref DamageLedgerExpiryComponent component, uint deadline) => component.ExpiresAtFrame = deadline);
            return false;
        }

        contributions.Remove(victimEntityId);
        return true;
    }

    /// <remarks>A ledger whose quiet period already ran out but hasn't been cleared yet (its expiry is due later this frame) is cleared here first, so a stale fight never adds to a new one.</remarks>
    private void RecordLastDamagedFrame(int victimEntityId, uint frame, long now)
    {
        if (!expiries.TryGetReadonly(victimEntityId, out var expiry))
        {
            expiries.Add(victimEntityId, new DamageLedgerExpiryComponent(frame, FrameDeadline.After(now, ResetAfterFrames)));
            return;
        }

        if (FrameDeadline.After(expiry.LastDamagedFrame, ResetAfterFrames) <= now)
        {
            contributions.Remove(victimEntityId);
        }

        expiries.TryUpdate(victimEntityId, frame, static (ref DamageLedgerExpiryComponent component, uint lastDamagedFrame) => component.LastDamagedFrame = lastDamagedFrame);
    }
}
