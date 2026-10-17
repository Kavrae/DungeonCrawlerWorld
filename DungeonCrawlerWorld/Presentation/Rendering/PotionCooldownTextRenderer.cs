using Microsoft.Xna.Framework;

namespace Presentation.Rendering;

/// <summary>Shared colors for the potion-cooldown countdown text -- PlayerStatusEffectsContent (the symbol + green number) and HotbarContent (the same green number, no symbol, above each potion slot) both draw the identical number the identical way.</summary>
public static class PotionCooldownPalette
{
    public static readonly Color TextColor = Color.LightGreen;
    public static readonly Color ShadowColor = Color.Black;
}
