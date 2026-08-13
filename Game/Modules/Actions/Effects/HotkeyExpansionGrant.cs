namespace Game.Modules.Actions.Effects;

/// <summary>Permanently unlocks more Expansion hotkey slots for the target. No-op when context.HotkeyExpansionUnlocks isn't wired, or the target has no HotkeyExpansionUnlockComponent at all (see HotkeyExpansion.Apply's own doc comment).</summary>
public sealed record HotkeyExpansionGrant(short Slots) : IActionEffectEntry
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
