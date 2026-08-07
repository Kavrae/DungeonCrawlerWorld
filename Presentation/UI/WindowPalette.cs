using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Shared chrome-color defaults so windows read as one consistent UI instead of each hardcoding
/// its own Title/Header/Body/Border colors -- every consumer below still takes its own
/// ElementChromeOptions/ElementContentOptions/TextOptions color first and only falls back to
/// these, so an individual window can still override any of them.
/// </summary>
internal static class WindowPalette
{
    /// <summary>Window title bar background, unfocused.</summary>
    public static readonly Color TitleColor = Color.LightBlue;

    /// <summary>Window title bar background while focused.</summary>
    public static readonly Color TitleFocusedColor = Color.Gold;

    /// <summary>Window title bar text.</summary>
    public static readonly Color TitleTextColor = Color.Black;

    /// <summary>Generic Element header background -- Folder's icon backdrop uses this directly; Window specializes its own Title* colors above instead of this, but both default to the same value (see FolderOptions' own doc comment: "Folder's header reads as the same kind of chrome" as a Window's title bar).</summary>
    public static readonly Color HeaderColor = Color.LightBlue;

    /// <summary>Reserved for a future Element subclass that draws header text generically -- nothing does today (Folder's header draws an icon, not text).</summary>
    public static readonly Color HeaderTextColor = Color.Black;

    /// <summary>Dark panel look shared by the management-style windows (Inventory, Ability Scores) -- an explicit opt-in override of BodyColor below, not every window's default.</summary>
    public static readonly Color PanelBackgroundColor = new(45, 45, 45);

    /// <summary>Light content areas set against PanelBackgroundColor's dark chrome -- the Inventory folder's own tiles and AbilityScoreWindow's columns both use this, so a management-style panel's inner content reads consistently wherever it appears.</summary>
    public static readonly Color PanelContentColor = Color.LightGray;

    /// <summary>Default content-area background for any Element/Window.</summary>
    public static readonly Color BodyColor = Color.White;

    /// <summary>Default content text color.</summary>
    public static readonly Color BodyTextColor = Color.Black;

    /// <summary>Default flat border color. Outset/Inset's raised/pressed bevel look (see BorderRenderer) is a separate two-color light/dark shading effect, not a single overridable color, so it stays independent of this.</summary>
    public static readonly Color BorderColor = Color.Black;
}
