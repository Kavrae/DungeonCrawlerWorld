namespace Game.Modules.Actions.Effects;

/// <summary>Permanently unlocks Slots more Expansion hotkey slots for the target. No-op when context.HotkeyExpansionUnlocks isn't wired, or the target has no HotkeyExpansionUnlockComponent at all (see HotkeyExpansionEffects.Grant's own doc comment).</summary>
public sealed record HotkeySlotGrantEntry(short Slots) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (Slots <= 0 || context.HotkeyExpansionUnlocks is null)
        {
            return;
        }

        HotkeyExpansionEffects.Grant(context.HotkeyExpansionUnlocks, context.TargetEntityId, Slots);
    }
}
