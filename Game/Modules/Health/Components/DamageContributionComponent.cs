using Game.World;

namespace Game.Modules.Health.Components;

/// <summary>How much health one source entity has taken off the holder since the holder's damage ledger last reset.</summary>
/// <remarks>One instance per source entity, in a Multi pool; written only through DamageLedger.</remarks>
/// <param name="Source">The damaging entity as it was at its first hit: its stable key, which outlives its unload, and the name it had.</param>
/// <param name="TotalDamageDealt">Health actually removed: after the holder's IncomingDamage modifiers, never past what it had left.</param>
/// <param name="FirstHitFrame">The frame this source first damaged the holder since the last reset; the earlier one wins a tie.</param>
public readonly record struct DamageContributionComponent(ActionSource Source, float TotalDamageDealt, uint FirstHitFrame)
{
    public override string ToString() => $"Source : {Source}\nTotalDamageDealt : {TotalDamageDealt:0.##}\nFirstHitFrame : {FirstHitFrame}";
}
