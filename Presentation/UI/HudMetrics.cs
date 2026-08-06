using Engine.Utilities;
using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>Shared sizing/margin/timing constants for top-level HUD elements (notification summary bar, player health bar, ...) so they stay visually consistent without one depending on the other's internals.</summary>
public static class HudMetrics
{
    public static readonly Vector2 Margin = new(30, 30);
    public static readonly Vector2 EntrySize = new(65, 21);

    /// <summary>Standard hover-tooltip wait time before a hover-triggered popup (e.g. the Armed
    /// Hotkey Summary, and future hover-triggered UI) appears -- shared so every such element feels
    /// consistent. 0.4s, expressed in frames via GameTiming.FramesForSeconds so it stays testable
    /// via repeated synthetic Update() calls the same way AbilityTargetingController.
    /// DoubleTapWindowFrames already is.</summary>
    public static readonly int HoverTooltipDelayFrames = GameTiming.FramesForSeconds(0.4f);
}
