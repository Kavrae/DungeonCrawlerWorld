using Game.Modules.Abilities;
using Microsoft.Xna.Framework.Input;

namespace Presentation.Input;

/// <summary>
/// Default slot-to-physical-key mapping and visual grouping -- deliberately separate from the
/// HotkeySlot enum itself (Game layer, no FNA/XNA dependency) so slot identity, physical key,
/// and display grouping stay three independently-changeable concerns. This is the QE / RFV /
/// 12345 layout from the outline; groupings get revisited once more slots are added (see
/// HotkeySlot's own doc comment), kept as-is for now.
/// </summary>
public static class HotkeySlotLayout
{
    public static readonly IReadOnlyDictionary<HotkeySlot, Keys> PhysicalKeyBySlot = new Dictionary<HotkeySlot, Keys>
    {
        [HotkeySlot.Slot1] = Keys.Q,
        [HotkeySlot.Slot2] = Keys.E,
        [HotkeySlot.Slot3] = Keys.R,
        [HotkeySlot.Slot4] = Keys.F,
        [HotkeySlot.Slot5] = Keys.V,
        [HotkeySlot.Slot6] = Keys.D1,
        [HotkeySlot.Slot7] = Keys.D2,
        [HotkeySlot.Slot8] = Keys.D3,
        [HotkeySlot.Slot9] = Keys.D4,
        [HotkeySlot.Slot10] = Keys.D5,
    };

    /// <summary>Visually-grouped clusters for the Hotbar UI (a later Presentation phase) -- QE, then RFV, then 12345.</summary>
    public static readonly IReadOnlyList<IReadOnlyList<HotkeySlot>> VisualGroups =
    [
        [HotkeySlot.Slot1, HotkeySlot.Slot2],
        [HotkeySlot.Slot3, HotkeySlot.Slot4, HotkeySlot.Slot5],
        [HotkeySlot.Slot6, HotkeySlot.Slot7, HotkeySlot.Slot8, HotkeySlot.Slot9, HotkeySlot.Slot10],
    ];
}
