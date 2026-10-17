using Microsoft.Xna.Framework;

namespace Presentation.UI.ColorPalettes;

/// <summary>The colour each resource's cost is drawn in on a hotbar slot, so a mana cost and a health cost side by side can't be mistaken for each other: the mana bar's full colour and the health bar's empty one.</summary>
internal static class CostBadgePalette
{
    public static readonly Color Mana = ManaBarPalette.FractionColor(1f);

    public static readonly Color Health = HealthBarPalette.FractionColor(0f);
}
