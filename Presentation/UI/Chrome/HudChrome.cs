using Engine.Utilities;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;

namespace Presentation.UI.Chrome;

/// <summary>Shared sizing/margin/timing constants for top-level HUD elements (notification summary bar, player health bar, ...) so they stay visually consistent without one depending on the other's internals. Plain mutable fields, not readonly -- a future data-driven theme loader needs to be able to overwrite them at startup.</summary>
public static class HudChrome
{
    public static Vector2 Margin = new(30, 30);
    public static Vector2 EntrySize = new(65, 21);

    /// <summary>Standard hover-tooltip wait time before a hover-triggered popup (e.g. the Armed
    /// Hotkey Summary, and future hover-triggered UI) appears -- shared so every such element feels
    /// consistent. 0.4s, expressed in frames via GameTiming.FramesForSeconds so it stays testable
    /// via repeated synthetic Update() calls the same way ActionTargetingController.
    /// DoubleTapWindowFrames already is.</summary>
    public static int HoverTooltipDelayFrames = GameTiming.FramesForSeconds(0.4f);

    private const float DebugWindowHeight = 24f;
    private const float ActionLockGap = 8f;
    private const float ManaBarGap = 3f;
    private const float InspectionWindowGap = 8f;

    /// <summary>Empty headroom left between InspectionWindow's bottom edge and the hotbar's worst-case (fully expanded) top edge -- no minimap exists yet, this just keeps the corner free for one, per the Inspection V2 request.</summary>
    private const float MinimapReserve = 140f;

    public static Vector2 MapWindowPosition;
    public static Vector2 MapWindowSize;
    public static Vector2 DebugWindowPosition;
    public static Vector2 DebugWindowSize;
    public static Vector2 PlayerHealthBarPosition;
    public static Vector2 PlayerManaBarPosition;
    public static Vector2 ActionLockPosition;
    public static Vector2 PlayerStatusEffectsPosition;
    public static Vector2 InspectionWindowPosition;
    public static Vector2 InspectionWindowSize;
    public static Vector2 QuestTriggerWindowPosition;
    public static Vector2 QuestTriggerWindowSize;

    /// <summary>Resolves every startup HUD window's position/size from the runtime screen size --
    /// called once, as the first line of ShellBootstrapper.Build, before any of those windows are
    /// actually created. A method rather than static field initializers because screenSize isn't
    /// known until Build runs; the fields it writes are plain static state afterward, the same
    /// shape a future save-file loader would need to overwrite specific fields with restored
    /// values after calling this for defaults.</summary>
    public static void ResolveLayout(Vector2 screenSize)
    {
        MapWindowPosition = Vector2.Zero;
        MapWindowSize = new Vector2(screenSize.X, screenSize.Y - DebugWindowHeight);

        DebugWindowPosition = new Vector2(0, MapWindowSize.Y);
        DebugWindowSize = new Vector2(MapWindowSize.X, DebugWindowHeight);

        PlayerHealthBarPosition = new Vector2(screenSize.X - PlayerHealthBarContent.Size.X - Margin.X, Margin.Y);
        PlayerManaBarPosition = new Vector2(screenSize.X - PlayerManaBarContent.Size.X - Margin.X, Margin.Y + PlayerHealthBarContent.Size.Y + ManaBarGap);
        ActionLockPosition = new Vector2(screenSize.X - PlayerHealthBarContent.Size.X - Margin.X - ActionLockContent.Size.X - ActionLockGap, Margin.Y);
        PlayerStatusEffectsPosition = new Vector2(screenSize.X - PlayerHealthBarContent.Size.X - Margin.X, Margin.Y + PlayerHealthBarContent.Size.Y + ManaBarGap + PlayerManaBarContent.Size.Y);

        var inspectionWindowTop = Margin.Y + PlayerHealthBarContent.Size.Y + ManaBarGap + PlayerManaBarContent.Size.Y + PlayerStatusEffectsContent.Size.Y + InspectionWindowGap;
        var hotbarClearanceTop = screenSize.Y - HotbarContent.MaximumSize.Y - Margin.Y * 1.5f;
        var inspectionWindowBottom = hotbarClearanceTop - InspectionWindowGap - MinimapReserve;
        InspectionWindowSize = new Vector2(PlayerHealthBarContent.Size.X, System.Math.Max(0f, inspectionWindowBottom - inspectionWindowTop));
        InspectionWindowPosition = new Vector2(screenSize.X - Margin.X - InspectionWindowSize.X, inspectionWindowTop);

        QuestTriggerWindowPosition = new Vector2(Margin.X, 800);
        QuestTriggerWindowSize = new Vector2(120, 30);
    }
}
