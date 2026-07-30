namespace Game.Modules.Abilities.Components;

/// <summary>
/// One bound hotkey slot -- an entity's full set of bindings is "however many of these it has"
/// (MultiComponentPool, the same pattern as AbilityInstanceComponent/RaceComponent), not a
/// single component holding a list. An unbound HotkeySlot simply has no instance for it -- the
/// Hotbar UI renders every HotkeySlot value regardless of whether a binding exists, so "no
/// instance for this slot" is itself the empty/unbound state, not a separate flag.
/// </summary>
public struct HotkeyBindingComponent(HotkeySlot slot, Guid abilityId)
{
    public HotkeySlot Slot { get; } = slot;
    public Guid AbilityId { get; set; } = abilityId;
}
