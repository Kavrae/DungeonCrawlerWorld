namespace Game.Modules.Abilities.Components;

/// <summary>
/// How many of the 20 Expansion hotkey slots (HotkeySlot.Slot1-Slot20) an entity has unlocked --
/// unlocking is rare and permanent (see HotkeyExpansionEffects.Grant, the only writer), so this
/// only ever grows, up to MaxUnlockedSlots. PlayerBlueprint defaults this to 10, matching the
/// slot count every entity already effectively had before Expansion grew past 10 -- nobody loses
/// access by this component's introduction. Presentation reads this to decide how many Expansion
/// rows to actually draw (row reveal is 5-slots-per-row, see HotkeySlotLayout), not to gate
/// activation directly -- an already-bound-but-since-relocked slot can't happen today since
/// unlocking never shrinks, so no separate enforcement exists yet.
/// </summary>
public struct HotkeyExpansionUnlockComponent(short unlockedSlotCount)
{
    public short UnlockedSlotCount { get; set; } = unlockedSlotCount;

    public override readonly string ToString() => $"UnlockedSlotCount : {UnlockedSlotCount}";
}
