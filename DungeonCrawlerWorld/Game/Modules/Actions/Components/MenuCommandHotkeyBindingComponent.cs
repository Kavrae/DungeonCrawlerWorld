using Engine.ECS.Properties;

namespace Game.Modules.Actions.Components;

/// <summary>One hotkey slot bound to a menu command, named by the command's Guid.</summary>
/// <remarks>
/// A menu command opens a window or switches a mode; what each Guid does is Presentation's, and Game never reads it, so
/// a Guid no command claims is a slot that does nothing. Sibling to ActionHotkeyBindingComponent and
/// ItemHotkeyBindingComponent: a slot holds at most one of the three (HotkeyBindingCommands.ClearSlot).
/// </remarks>
[DescribedComponent]
public partial struct MenuCommandHotkeyBindingComponent(HotkeySlot slot, Guid menuCommandId) : IHotkeySlotBinding<Guid>
{
    [ReadOnlyProperty]
    public HotkeySlot Slot { get; init; } = slot;
    [PropertyMeaning(GamePropertyMeanings.MenuCommandId)]
    public Guid MenuCommandId { get; set; } = menuCommandId;

    readonly Guid IHotkeySlotBinding<Guid>.BoundId => MenuCommandId;
}
