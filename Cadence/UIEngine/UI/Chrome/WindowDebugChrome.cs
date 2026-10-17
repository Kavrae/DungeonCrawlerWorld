namespace UIEngine.UI.Chrome;

/// <summary>What windows draw for a developer looking at the layout: a host turns it on with its own debug mode.</summary>
public static class WindowDebugChrome
{
    /// <summary>Whether every window's title shows its position and size after its text.</summary>
    public static bool ShowsGeometryInTitles { get; set; }
}
