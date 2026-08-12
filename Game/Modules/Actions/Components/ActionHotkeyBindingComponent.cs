namespace Game.Modules.Actions.Components;

/// <summary>
/// One bound hotkey slot
/// </summary>
/// <remarks>
/// One ActionHotkeyBindingComponent per bound hotkey per entity.
/// </remarks>
public struct ActionHotkeyBindingComponent(HotkeySlot slot, Guid actionId) : IHotkeySlotBinding
{
    public HotkeySlot Slot { get; } = slot;
    public Guid ActionId { get; set; } = actionId;

    public override readonly string ToString() => $"Slot : {Slot}\nActionId : {ActionId}";
}
