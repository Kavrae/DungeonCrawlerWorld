namespace Game.Modules.Abilities;

/// <summary>
/// Generic numbered hotkey slots -- named by number, not by physical key, so slots never need
/// renaming once rebinding becomes player-configurable (see Presentation's HotkeySlotLayout for
/// the current default slot-to-physical-key mapping and visual grouping). 10 is the starting
/// count; more slots are an expected future addition, so nothing should hardcode "10" where it
/// can instead read HotkeySlotLayout's own slot list.
/// </summary>
public enum HotkeySlot
{
    Slot1,
    Slot2,
    Slot3,
    Slot4,
    Slot5,
    Slot6,
    Slot7,
    Slot8,
    Slot9,
    Slot10,
}

/// <summary>
/// Shared shape for anything a hotkey slot can be bound to -- ActionHotkeyBindingComponent
/// (Game.Modules.Abilities.Components) and ItemHotkeyBindingComponent
/// (Game.Modules.Inventory.Components) both implement this rather than sharing a base class:
/// every component pool in this ECS is constrained "where T : struct" (see
/// PackedComponentPool/MultiComponentPool/DirectComponentPool), so C#'s no-struct-inheritance
/// rule rules out a real base type -- an interface is the actual available equivalent. A slot
/// binds to at most one of {action, item} at a time; whatever writes a new binding for a slot is
/// responsible for clearing the other kind's entry for that same slot first.
/// </summary>
public interface IHotkeySlotBinding
{
    HotkeySlot Slot { get; }
}
