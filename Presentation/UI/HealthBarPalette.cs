using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>Health-bar color logic shared by MapWindow's per-tile bars and PlayerHealthBarContent's HUD bar, so both render identically.</summary>
internal static class HealthBarPalette
{
    public static readonly Color OutlineColor = Color.Black;

    /// <summary>Green at full health, yellow at half, red at empty -- two linear segments (100%-50% and 50%-0%) rather than one 100%-0% lerp, so the midpoint lands exactly on yellow instead of a muddy green-red blend.</summary>
    public static Color FractionColor(float healthFraction) =>
        healthFraction >= 0.5f
            ? Color.Lerp(Color.Yellow, Color.Green, (healthFraction - 0.5f) / 0.5f)
            : Color.Lerp(Color.Red, Color.Yellow, healthFraction / 0.5f);
}
