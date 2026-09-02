namespace Presentation.UI.Chrome;

/// <summary>HotbarContent's own internal layout constants -- see HudChrome's own doc comment for
/// why these are plain mutable fields rather than readonly. HotbarContent's actual live
/// Size/MaximumSize stay on HotbarContent itself: both depend on how many Expansion slots are
/// currently unlocked (changes during play), so they're genuinely dynamic and can't be
/// pre-calculated the way a startup HUD window's position/size can.</summary>
public static class HotbarChrome
{
    public static int BaseSlotCount = 3;
    public static int ExpansionColumnsPerRow = 5;
    public static int MaxExpansionRows = 2;
    public static int MaxExpansionPages = 2;
    public static int SlotsPerExpansionPage = ExpansionColumnsPerRow * MaxExpansionRows;

    public static float SlotGap = 1f;
    public static float GroupGap = 10f;
    public static float ExpansionPageGap = GroupGap;
}
