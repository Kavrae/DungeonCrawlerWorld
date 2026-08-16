namespace Game.Modules.Actions.Effects;

/// <summary>Permanently unlocks more Expansion hotkey slots for the target.</summary>
/// <remarks>No-op when context.HotkeyExpansionUnlocks isn't wired, or the target has no HotkeyExpansionUnlockComponent at all (see HotkeyExpansion.Apply's own doc comment).</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed record HotkeyExpansionGrant(byte Slots) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (Slots <= 0 || context.HotkeyExpansionUnlocks is null)
        {
            return;
        }

        HotkeyExpansion.Apply(context.HotkeyExpansionUnlocks, context.TargetEntityId, Slots);
    }
}
