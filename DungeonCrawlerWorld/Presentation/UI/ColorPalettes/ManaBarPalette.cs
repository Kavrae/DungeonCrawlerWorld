using Microsoft.Xna.Framework;

namespace Presentation.UI.ColorPalettes;

/// <summary>Mana-bar color logic, mirroring HealthBarPalette's shape exactly -- shared by any future per-tile mana bar and PlayerManaBarContent's HUD bar. Sky blue at full mana, fading to dark blue at empty -- a single lerp (not HealthBarPalette's two-segment yellow midpoint), since only two named colors were specified for mana.</summary>
internal static class ManaBarPalette
{
    public static readonly Color OutlineColor = Color.Black;

    public static Color FractionColor(float manaFraction) => Color.Lerp(Color.DarkBlue, Color.SkyBlue, manaFraction);
}
