using Microsoft.Xna.Framework;

namespace Presentation.UI.ColorPalettes;

/// <summary>
/// MapWindow's targeting/telegraph highlight colors -- Combat Overhaul: Dodge (TODO.md): light
/// green for the player's own reachable/armed tiles, dark green for the player's own confirmed/
/// hovered target or in-flight Delayed windup, red for an enemy's telegraphed action that cannot
/// be dodged, yellow for one that can. Shared by every Delayed action's telegraph, not just Dodge's
/// own targeting -- see MapWindow.DrawTargetingHighlights.
/// </summary>
internal static class CombatTargetPalette
{
    public static readonly Color PlayerArmColor = Color.LightGreen;
    public static readonly Color PlayerTargetColor = Color.DarkGreen;
    public static readonly Color EnemyUndodgeableColor = Color.Red;
    public static readonly Color EnemyDodgeableColor = Color.Yellow;
}
