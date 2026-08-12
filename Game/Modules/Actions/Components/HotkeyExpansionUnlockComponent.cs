namespace Game.Modules.Actions.Components;

/// <summary>
/// How many of the 20 Expansion hotkey slots (HotkeySlot.Slot1-Slot20) an entity has unlocked
/// </summary>
public struct HotkeyExpansionUnlockComponent(short unlockedSlotCount)
{
    public short UnlockedSlotCount { get; set; } = unlockedSlotCount;

    public override readonly string ToString() => $"UnlockedSlotCount : {UnlockedSlotCount}";
}
