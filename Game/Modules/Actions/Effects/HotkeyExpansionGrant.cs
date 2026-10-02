using Game.Effects;

namespace Game.Modules.Actions.Effects;

/// <summary>Permanently unlocks more Expansion hotkey slots for the target.</summary>
/// <remarks>No-op when the target has no HotkeyExpansionUnlockComponent at all (see HotkeyExpansion.Apply's own doc comment).</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed record HotkeyExpansionGrant(byte Slots) : IEffectEntry
{
    public EffectOutcome Apply(in EffectContext context)
    {
        if (Slots <= 0 || !context.Services.HotkeyExpansionUnlocks.Has(context.TargetEntityId))
        {
            return EffectOutcome.NoEffect;
        }

        HotkeyExpansion.Apply(context.Services.HotkeyExpansionUnlocks, context.TargetEntityId, Slots);
        return EffectOutcome.Applied;
    }
}
