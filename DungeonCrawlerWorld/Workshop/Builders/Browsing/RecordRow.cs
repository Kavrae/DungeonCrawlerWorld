using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Builders.Browsing;

/// <summary>A symbol on a browser row after the record's name: a glyph (or a sprite, when it names one), its colour, and what it means, shown as its own tooltip.</summary>
public readonly record struct RecordRowMarker(string Glyph, string? SpriteName, Color Color, string Tooltip);

/// <summary>A browser row: the record's icon, its name, and its marker symbols packed against the row's right end.</summary>
/// <remarks>
/// The row is one line laid out by its own tiling: the icon, the name taking what the symbols leave, then the symbols. A
/// name too long for that is cut short with "...", never a symbol, and carries the full name as its tooltip only while it
/// is cut. Each symbol is an element of its own carrying its tooltip. A dimmed row greys its name and blends its symbols
/// toward the same grey, so they still tell apart. A click on any child reaches the row.
/// </remarks>
public static class RecordRow
{
    public const float Height = 24f;

    private const float IconSize = 20f;

    /// <summary>Room at either end of the row.</summary>
    private const float SideInset = 4f;

    /// <summary>Between the icon and the name.</summary>
    private const float NameGap = 7f;

    private const float MarkerSize = 18f;
    private const float MarkerGap = 2f;

    /// <summary>How far a dimmed row's symbol colours move toward the dimmed text colour.</summary>
    private const float DimmedMarkerBlend = 0.6f;

    /// <summary>A row width wide, holding appearance, drawn over rowColor; added to parent by the caller.</summary>
    public static Window Create(ElementPoolService elementPool, Element parent, float width, RecordRowAppearance appearance, Color rowColor)
    {
        var rowSize = new Vector2(width, Height);
        var row = elementPool.CreateElement<Window>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Horizontal, ChildCrossAlignment = ChildCrossAlignment.Center },
            Layout = new ElementLayoutOptions { Size = rowSize, MinimumSize = rowSize, MaximumSize = rowSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = rowColor },
        });
        row.ContentPadding = new Vector2(SideInset, 0);

        AddIcon(elementPool, row, appearance.SpriteName, appearance.Glyph, appearance.GlyphColor, IconSize, NameGap, tooltip: null);

        var name = elementPool.CreateElement<Label>(row, new ElementOptions
        {
            Layout = new ElementLayoutOptions { FillsRemaining = true, SpacingAfter = appearance.Markers.Count == 0 ? 0f : MarkerGap },
            Text = new TextOptions { Text = appearance.Name, TextColor = appearance.IsDimmed ? WindowPalette.DimmedRowTextColor : WindowPalette.BodyTextColor },
        });
        name.ShowsFullTextWhenCut = true;
        row.AddChild(name);

        for (var index = 0; index < appearance.Markers.Count; index++)
        {
            var marker = appearance.Markers[index];
            var color = appearance.IsDimmed ? Color.Lerp(marker.Color, WindowPalette.DimmedRowTextColor, DimmedMarkerBlend) : marker.Color;
            var isLast = index == appearance.Markers.Count - 1;
            AddIcon(elementPool, row, marker.SpriteName, marker.Glyph, color, MarkerSize, isLast ? 0f : MarkerGap, marker.Tooltip);
        }

        return row;
    }

    private static void AddIcon(ElementPoolService elementPool, Window row, string? spriteName, string glyph, Color color, float size, float spacingAfter, string? tooltip)
    {
        var iconSize = new Vector2(size, size);
        var icon = elementPool.CreateElement<IconElement>(row, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = iconSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true, SpacingAfter = spacingAfter },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        icon.Configure(spriteName, glyph, color, iconSize);
        icon.TooltipText = tooltip;
        row.AddChild(icon);
    }
}
