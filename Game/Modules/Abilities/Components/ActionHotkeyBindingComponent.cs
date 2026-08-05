namespace Game.Modules.Abilities.Components;

/// <summary>
/// One bound hotkey slot -- an entity's full set of bindings is "however many of these it has"
/// (MultiComponentPool, the same pattern as AbilityInstanceComponent/RaceComponent), not a
/// single component holding a list. An unbound HotkeySlot simply has no instance for it -- the
/// Hotbar UI renders every HotkeySlot value regardless of whether a binding exists, so "no
/// instance for this slot" is itself the empty/unbound state, not a separate flag.
///
/// Named "Action" (not "Ability") to match ActionLockComponent/ActionTimingCategory's existing
/// umbrella term for activatable things, now that ItemHotkeyBindingComponent
/// (Game.Modules.Inventory.Components) is a second, sibling kind of hotkey binding -- see
/// IHotkeySlotBinding (Game.Modules.Abilities.HotkeySlot.cs) for what the two share.
/// </summary>
public struct ActionHotkeyBindingComponent(HotkeySlot slot, Guid abilityId) : IHotkeySlotBinding
{
    public HotkeySlot Slot { get; } = slot;
    public Guid AbilityId { get; set; } = abilityId;
}
