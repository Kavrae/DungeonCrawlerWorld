using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Text-bearing content, independently combinable with any WindowOptions -- replaces
/// TextWindowOptions (a WindowOptions subclass). Any window can carry a Text block without
/// needing its own options subclass, which is what let DebugWindowContent/SelectionWindowContent
/// host TextWindow children without adding new options types for each.
/// </summary>
public sealed class TextOptions
{
    /// <summary>Text to display, formatted by StringUtility before rendering.</summary>
    public string? Text { get; set; }

    public Color? TextColor { get; set; }
}
