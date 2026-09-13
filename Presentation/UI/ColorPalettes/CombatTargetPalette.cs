using Microsoft.Xna.Framework;

namespace Presentation.UI.ColorPalettes;

/// <summary>
/// MapWindow's targeting/telegraph highlight colors
/// </summary>
internal static class CombatTargetPalette
{
    public static readonly Color PlayerArmColor = Color.LightGreen;
    public static readonly Color PlayerTargetColor = Color.DarkGreen;
    public static readonly Color EnemyUndodgeableColor = Color.Red;
    public static readonly Color EnemyDodgeableColor = Color.Yellow;
}
