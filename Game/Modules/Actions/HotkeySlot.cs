namespace Game.Modules.Actions;

/// <summary>
/// Named by category+number, not by physical key, so slots never need renaming once rebinding
/// becomes player-configurable (see Presentation's HotkeySlotLayout for the current default
/// slot-to-physical-key mapping, category grouping, and Expansion unlock/row-reveal behavior).
/// Base1-3 and DefaultAttack are fixed, always-available slots. Slot1-Slot20 are the Expansion
/// group -- Slot1-10 is "page 1", Slot11-20 is "page 2" (reached via Shift); how many of the 20
/// are actually unlocked for a given entity is tracked separately by
/// HotkeyExpansionUnlockComponent, not by this enum itself. 20 is the current cap; see
/// HotkeyExpansionUnlockComponent's own doc comment for why.
/// </summary>
public enum HotkeySlot : byte
{
    Base1,
    Base2,
    Base3,
    DefaultAttack,
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
    Slot11,
    Slot12,
    Slot13,
    Slot14,
    Slot15,
    Slot16,
    Slot17,
    Slot18,
    Slot19,
    Slot20,
}

/// <summary>
/// Shared shape for anything a hotkey slot can be bound to -- ActionHotkeyBindingComponent
/// (Game.Modules.Actions.Components) and ItemHotkeyBindingComponent
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
