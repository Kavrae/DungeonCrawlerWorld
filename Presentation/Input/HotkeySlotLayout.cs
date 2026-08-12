using Game.Modules.Actions;
using Microsoft.Xna.Framework.Input;

namespace Presentation.Input;

/// <summary>Which of the FF14-style hotbar's three groups a slot belongs to -- see HotkeySlotLayout's own doc comment for the groups' physical-key/growth rules.</summary>
public enum HotkeyCategory
{
    Base,
    DefaultAttack,
    Expansion,
}

/// <summary>
/// A single slot's full physical-input and grid-position identity. Row/Column/Page are local to
/// the slot's own Category (e.g. Expansion's Row runs 0-1 and Page runs 0-1, Base's Row/Page are
/// always 0) -- HotbarContent (Presentation) is what turns these into actual on-screen pixel
/// positions, this record only carries the layout data itself. Page distinguishes Expansion's two
/// 2x5 blocks (see HotkeySlotLayout's own doc comment) -- irrelevant (always 0) for Base/
/// DefaultAttack.
/// </summary>
public readonly record struct HotkeySlotLayoutEntry(HotkeySlot Slot, HotkeyCategory Category, Keys Key, bool RequiresShift, int Page, int Row, int Column);

/// <summary>
/// Default slot-to-physical-key mapping and category/grid layout -- deliberately separate from
/// the HotkeySlot enum itself (Game layer, no FNA/XNA dependency) so slot identity, physical key,
/// and display layout stay three independently-changeable concerns. Three fixed categories (see
/// HotkeyCategory): Base (Base1-3, always exactly 3, Q/E/R) and DefaultAttack (always exactly 1,
/// F) never grow or shrink; Expansion (Slot1-20) is the one that grows, as two 2x5 pages placed
/// side by side (page 1 on the left, page 2 to its right -- not stacked below) -- page 0 row 0 is
/// 1/2/3/4/5, page 0 row 1 is Z/X/C/V/B, page 1 row 0 is Shift+1..5, page 1 row 1 is
/// Shift+Z/X/C/V/B. How many of the 20 Expansion slots are actually unlocked for a given entity
/// lives on HotkeyExpansionUnlockComponent, not here -- this table is the static default layout
/// for all 24 slots regardless of how many any one entity currently has unlocked.
/// </summary>
public static class HotkeySlotLayout
{
    private static readonly Keys[] ExpansionRowKeys = [Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5];
    private static readonly Keys[] ExpansionRowTwoKeys = [Keys.Z, Keys.X, Keys.C, Keys.V, Keys.B];

    public static readonly IReadOnlyList<HotkeySlotLayoutEntry> Entries = BuildEntries();

    private static readonly IReadOnlyDictionary<HotkeySlot, HotkeySlotLayoutEntry> EntryBySlot =
        Entries.ToDictionary(entry => entry.Slot);

    private static List<HotkeySlotLayoutEntry> BuildEntries()
    {
        var entries = new List<HotkeySlotLayoutEntry>
        {
            new(HotkeySlot.Base1, HotkeyCategory.Base, Keys.Q, RequiresShift: false, Page: 0, Row: 0, Column: 0),
            new(HotkeySlot.Base2, HotkeyCategory.Base, Keys.E, RequiresShift: false, Page: 0, Row: 0, Column: 1),
            new(HotkeySlot.Base3, HotkeyCategory.Base, Keys.R, RequiresShift: false, Page: 0, Row: 0, Column: 2),
            new(HotkeySlot.DefaultAttack, HotkeyCategory.DefaultAttack, Keys.F, RequiresShift: false, Page: 0, Row: 0, Column: 0),
        };

        AddExpansionRow(entries, HotkeySlot.Slot1, ExpansionRowKeys, requiresShift: false, page: 0, row: 0);
        AddExpansionRow(entries, HotkeySlot.Slot6, ExpansionRowTwoKeys, requiresShift: false, page: 0, row: 1);
        AddExpansionRow(entries, HotkeySlot.Slot11, ExpansionRowKeys, requiresShift: true, page: 1, row: 0);
        AddExpansionRow(entries, HotkeySlot.Slot16, ExpansionRowTwoKeys, requiresShift: true, page: 1, row: 1);

        return entries;
    }

    /// <summary>Adds 5 consecutive HotkeySlot values (firstSlot..firstSlot+4) as one Expansion page's row -- relies on HotkeySlot's declaration order (Slot1..Slot20 contiguous) matching this row-of-5 layout.</summary>
    private static void AddExpansionRow(List<HotkeySlotLayoutEntry> entries, HotkeySlot firstSlot, Keys[] rowKeys, bool requiresShift, int page, int row)
    {
        for (var column = 0; column < rowKeys.Length; column++)
        {
            var slot = (HotkeySlot)((int)firstSlot + column);
            entries.Add(new HotkeySlotLayoutEntry(slot, HotkeyCategory.Expansion, rowKeys[column], requiresShift, page, row, column));
        }
    }

    public static HotkeySlotLayoutEntry GetEntry(HotkeySlot slot) => EntryBySlot[slot];

    /// <summary>Display label for a slot's bind key -- e.g. "Q", "1", "↑1" for a Shift-page Expansion slot. Digit keys (D1-D5) print as their digit, not "D1".</summary>
    public static string GetKeyLabel(HotkeySlot slot)
    {
        var entry = EntryBySlot[slot];
        var keyLabel = KeyDisplayName(entry.Key);
        return entry.RequiresShift ? $"↑{keyLabel}" : keyLabel;
    }

    private static string KeyDisplayName(Keys key) => key switch
    {
        Keys.D1 => "1",
        Keys.D2 => "2",
        Keys.D3 => "3",
        Keys.D4 => "4",
        Keys.D5 => "5",
        _ => key.ToString(),
    };
}
