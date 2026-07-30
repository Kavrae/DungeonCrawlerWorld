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
