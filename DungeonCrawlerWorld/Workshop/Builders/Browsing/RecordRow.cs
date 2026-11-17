using Engine.Utilities;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using UIEngine.Fonts;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Builders.Browsing;

/// <summary>A symbol on a browser row after the record's name: a glyph (or a sprite, when it names one), its colour, and what it means, shown as its own tooltip.</summary>
public readonly record struct RecordRowMarker(string Glyph, string? SpriteName, Color Color, string Tooltip);

/// <summary>A browser row: the record's icon, its name, and its marker symbols packed against the row's right end.</summary>
/// <remarks>
/// The row is one line: a name too long for what the symbols leave is cut short with "...", never a symbol. Each symbol is
/// an element of its own carrying its tooltip; the row and the name carry the full name only when it is cut. A dimmed
/// row greys its name and blends its symbols toward the same grey, so they still tell apart. A click on any child reaches
/// the row.
/// </remarks>
public static class RecordRow
{
    public const float Height = 24f;

    private const float IconSize = 20f;
    private const float IconLeft = 4f;
    private const float TextIndent = 28f;

    /// <summary>Room left beside a row's text so a line that just fits is never wrapped.</summary>
    private const float TextSlack = 4f;

    private const float MarkerSize = 18f;
    private const float MarkerGap = 2f;

    /// <summary>Room between the last symbol and the row's right end.</summary>
    private const float MarkersRightMargin = 4f;

    /// <summary>How far a dimmed row's symbol colours move toward the dimmed text colour.</summary>
    private const float DimmedMarkerBlend = 0.6f;

    /// <summary>A row width wide, holding appearance, drawn over rowColor; added to parent by the caller.</summary>
    public static Window Create(ElementPoolService elementPool, FontService fontService, Element parent, float width, RecordRowAppearance appearance, Color rowColor)
    {
        var rowSize = new Vector2(width, Height);
        var row = elementPool.CreateElement<Window>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = rowSize, MinimumSize = rowSize, MaximumSize = rowSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = rowColor },
        });
        row.ContentPadding = Vector2.Zero;

        var iconSize = new Vector2(IconSize, IconSize);
        var icon = elementPool.CreateElement<ItemIconElement>(row, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(IconLeft, (Height - IconSize) / 2), Size = iconSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        icon.Configure(appearance.SpriteName, appearance.Glyph, appearance.GlyphColor, iconSize);
        row.AddChild(icon);

        var markersWidth = MarkersWidth(appearance.Markers.Count);
        var nameSize = new Vector2(Math.Max(width - TextIndent - markersWidth, 1f), Height);
        var name = elementPool.CreateElement<TextWindow>(row, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(TextIndent, 0), Size = nameSize, MinimumSize = nameSize, MaximumSize = nameSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { TextColor = appearance.IsDimmed ? WindowPalette.DimmedRowTextColor : WindowPalette.BodyTextColor },
        });
        var measurer = fontService.GetTextMeasurer(name.ContentFont);
        var nameWidth = nameSize.X - name.ContentPadding.X * 2 - TextSlack;
        var drawnName = StringUtility.TruncateWithEllipsis(measurer, appearance.Name, Math.Max(nameWidth, 1f));
        name.UpdateText(drawnName);
        row.AddChild(name);

        var isCut = !ReferenceEquals(drawnName, appearance.Name);
        row.TooltipText = name.TooltipText = isCut ? appearance.Name : null;

        var markerSize = new Vector2(MarkerSize, MarkerSize);
        var markerLeft = width - markersWidth + MarkerGap;
        foreach (var marker in appearance.Markers)
        {
            var symbol = elementPool.CreateElement<ItemIconElement>(row, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(markerLeft, (Height - MarkerSize) / 2), Size = markerSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            });
            var color = appearance.IsDimmed ? Color.Lerp(marker.Color, WindowPalette.DimmedRowTextColor, DimmedMarkerBlend) : marker.Color;
            symbol.Configure(marker.SpriteName, marker.Glyph, color, markerSize);
            symbol.TooltipText = marker.Tooltip;
            row.AddChild(symbol);
            markerLeft += MarkerSize + MarkerGap;
        }

        return row;
    }

    /// <summary>The width count symbols take at the row's right end, the margin after them included; none for none.</summary>
    private static float MarkersWidth(int count) => count == 0 ? 0f : count * (MarkerSize + MarkerGap) + MarkersRightMargin;
}
