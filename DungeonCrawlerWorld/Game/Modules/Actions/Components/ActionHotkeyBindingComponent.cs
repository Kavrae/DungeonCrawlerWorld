using Engine.ECS.Properties;

namespace Game.Modules.Actions.Components;

/// <summary>
/// One bound hotkey slot
/// </summary>
/// <remarks>
/// One ActionHotkeyBindingComponent per bound hotkey per entity.
/// </remarks>
[DescribedComponent]
public partial struct ActionHotkeyBindingComponent(HotkeySlot slot, Guid actionId) : IHotkeySlotBinding<Guid>
{
    [ReadOnlyProperty]
    public HotkeySlot Slot { get; init; } = slot;
    [PropertyMeaning(GamePropertyMeanings.ActionId)]
    public Guid ActionId { get; set; } = actionId;

    readonly Guid IHotkeySlotBinding<Guid>.BoundId => ActionId;
}
