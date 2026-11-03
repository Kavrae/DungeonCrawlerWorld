using Game.World;

namespace Game.Modules.Health.Components;

/// <summary>How much health one source entity has taken off the holder since the holder's damage ledger last reset.</summary>
/// <remarks>One instance per source entity, in a Multi pool; written only through DamageLedger, which adds each hit to TotalDamageDealt in place.</remarks>
public struct DamageContributionComponent(ActionSource source, float totalDamageDealt, uint firstHitFrame)
{
    /// <summary>The damaging entity as it was at its first hit: its stable key, which outlives its unload, and the name it had.</summary>
    public ActionSource Source { get; } = source;

    /// <summary>Health actually removed: after the holder's IncomingDamage modifiers, never past what it had left.</summary>
    public float TotalDamageDealt { get; set; } = totalDamageDealt;

    /// <summary>The frame this source first damaged the holder since the last reset; the earlier one wins a tie.</summary>
    public uint FirstHitFrame { get; } = firstHitFrame;

    public override readonly string ToString() => $"Source : {Source}\nTotalDamageDealt : {TotalDamageDealt:0.##}\nFirstHitFrame : {FirstHitFrame}";
}
