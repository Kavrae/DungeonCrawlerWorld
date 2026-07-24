using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>Shared sizing/margin constants for top-level HUD elements (notification summary bar, player health bar, ...) so they stay visually consistent without one depending on the other's internals.</summary>
public static class HudMetrics
{
    public static readonly Vector2 Margin = new(30, 30);
    public static readonly Vector2 EntrySize = new(65, 21);
}
