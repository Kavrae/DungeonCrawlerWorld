using Engine.Tags;
using FontStashSharp;
using Game.Effects;
using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;
using UIEngine.Fonts;
using UIEngine.UI;
using UIEngine.UI.Chrome;

namespace Presentation.UI.Details;

/// <summary>The sections a details window shows for anything activatable -- an item or an action: name row, effects, cost, activation, description and tags.</summary>
/// <remarks>
/// Each Build method adds rows to the host window, which tiles its children vertically and is WrapContent (see
/// ItemDetailsWindow's doc comment for why), so every row is built against an explicit width. Every TextWindow row is
/// given its font explicitly: a pooled TextWindow keeps whatever font its previous user set (see HealthWindow._bodyFont).
/// Lines that Item Details Comparison colours take their colour from the caller; a window that compares nothing passes
/// BodyTextColor.
/// </remarks>
public sealed class ActivatableDetailsSections(ElementPoolService elementPoolService, FontService fontService, ActionCatalog actionCatalog, GameplayTagRegistry gameplayTags)
{
    public const float IconSize = 32f;
    public const float RowHeight = 18f;
    public const float RowTextGap = 6f;

    /// <summary>A generous, effectively-unlimited per-row height cap -- see InspectionWindowContent.UnboundedChildHeight's own doc comment for why this is needed: without it, a row tiled past the host window's own one-screen-tall content size gets silently clamped to nothing.</summary>
    public const float UnboundedChildHeight = 10000f;

    /// <summary>Soft per-widget height cap for the target shape preview grid -- independent of the window's own overall MaximumSize/scroll safety net, so one large-AreaSize Burst can't single-handedly blow out the window's height; TargetShapePreviewGeometry.ComputeCellSize shrinks cells to fit within this instead.</summary>
    private const float TargetShapePreviewMaxHeight = 160f;

    /// <summary>BuildActivationSection divides its own width into this many equal zones -- text (left), the target shape grid (centered in the middle one), and an empty zone (right, reserved for now).</summary>
    private const float TargetingRowZoneCount = 3f;

    public static readonly Color BodyTextColor = Color.White;

    public SpriteFontBase BodyFont { get; } = fontService.GetFont(FontChrome.DefaultFontSize);

    private readonly SpriteFontBase _nameFont = fontService.GetFont(GameFontChrome.ItemDetailsNameFontSize);

    /// <summary>The sprite or glyph beside the name in the doubled name font.</summary>
    public void BuildNameRow(Window host, ActivatableDefinition definition, float width) =>
        BuildNameRow(host, definition.SpriteName, definition.Glyph, definition.GlyphColor, definition.Name, width);

    /// <summary>The sprite or glyph beside name in the doubled name font, for something that isn't an action or item (an aura).</summary>
    public void BuildNameRow(Window host, string? spriteName, string glyph, Color glyphColor, string name, float width)
    {
        // Row content height is the taller of the icon and the (doubled) name text -- not just IconSize, since
        // _nameFont's own line height can exceed it. The row's outer width is a hard constraint (must equal width
        // exactly, matching every other top-level row so the host's WrapContent measurement stays consistent); its
        // outer height is not, so it grows by ContentPadding on top and bottom instead -- see HealthWindow.AddBarRow.
        var nameLineHeight = _nameFont.LineHeight;
        var rowContentHeight = System.Math.Max(IconSize, nameLineHeight);
        var row = CreateRowWindow(host, width, rowContentHeight + host.ContentPadding.Y * 2);

        AddIcon(row, spriteName, glyph, glyphColor, rowContentHeight);

        // The row's own content width is width - ContentPadding*2, not the outer width -- width is a hard
        // constraint, so the text shrinks to fit instead.
        var availableWidth = width - host.ContentPadding.X * 2;
        var textX = IconSize + RowTextGap;
        var nameLine = elementPoolService.CreateElement<TextWindow>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(textX, (rowContentHeight - nameLineHeight) / 2f), Size = new Vector2(System.Math.Max(0f, availableWidth - textX), nameLineHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = name, TextColor = BodyTextColor },
        });
        nameLine.ContentFont = _nameFont;
        row.AddChild(nameLine);
    }

    /// <summary>An icon beside one line of body text, as tall as the icon -- the name row's shape without the doubled font (Item Details' gold value).</summary>
    public void BuildIconTextRow(Window host, string? spriteName, string glyph, Color glyphColor, string text, float width)
    {
        var rowContentHeight = System.Math.Max(IconSize, RowHeight);
        var row = CreateRowWindow(host, width, rowContentHeight + host.ContentPadding.Y * 2);

        AddIcon(row, spriteName, glyph, glyphColor, rowContentHeight);

        var textX = IconSize + RowTextGap;
        var availableWidth = width - host.ContentPadding.X * 2;
        var line = elementPoolService.CreateElement<TextWindow>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(textX, (rowContentHeight - RowHeight) / 2f), Size = new Vector2(System.Math.Max(0f, availableWidth - textX), RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = BodyTextColor },
        });
        line.ContentFont = BodyFont;
        row.AddChild(line);
    }

    /// <summary>"Effects" (or "While on" for a toggle, whose own effects are what it holds while on), then one line per effect entry, or "None".</summary>
    public void BuildEffectsSection(Window host, ActivatableDefinition definition, float width, Func<ItemComparisonStat, Color> lineColor) =>
        BuildEffectsSection(host, definition.Effects, definition.Toggle is null ? "Effects" : "While on", width, lineColor);

    /// <summary>heading, then one line per entry of effects, or "None".</summary>
    public void BuildEffectsSection(Window host, IReadOnlyList<Effect> effects, string heading, float width, Func<ItemComparisonStat, Color> lineColor)
    {
        BuildDivider(host, width, heading, 0.125f);

        var stats = ItemComparisonStatExtraction.ExtractEffectStats(effects, gameplayTags);
        foreach (var stat in stats)
        {
            BuildFixedTextLine(host, width, stat.DisplayText, lineColor(stat));
        }

        if (stats.Count == 0)
        {
            BuildFixedTextLine(host, width, "None", BodyTextColor);
        }
    }

    /// <summary>"Cost" and one line per cost -- nothing at all when using it costs nothing.</summary>
    public void BuildCostSection(Window host, ActivatableDefinition definition, float width)
    {
        if (CostText.Lines(definition, gameplayTags).ToList() is not { Count: > 0 } costLines)
        {
            return;
        }

        BuildDivider(host, width, "Cost", 0.125f);
        foreach (var line in costLines)
        {
            BuildFixedTextLine(host, width, line, BodyTextColor);
        }
    }

    /// <summary>
    /// "Activation", then three equal-width zones: the targeting shape/range caption plus every Timing/per-activator-type
    /// line, stacked in the left zone; the target shape preview grid (see TargetShapePreviewGeometry/
    /// TargetShapePreviewElement) centered within the middle zone; the right zone left empty for now. Vertically centered
    /// against the taller of the text column/grid. Both are sized entirely up front, before the row is created, so none
    /// of it risks the Fixed-vs-WrapContent shrink-feedback pitfall the window itself hit earlier.
    /// </summary>
    /// <param name="shapeHighlight">The grid tiles Item Details Comparison highlights, given the spec and its tiles; null highlights none.</param>
    public void BuildActivationSection(Window host, IActionActivator activator, float width, Func<ItemComparisonStat, Color> lineColor, Func<TargetingSpec, IReadOnlyList<Point>, IReadOnlySet<Point>?>? shapeHighlight = null)
    {
        BuildDivider(host, width, "Activation", 0.125f);

        var stats = ItemComparisonStatExtraction.ExtractActivatorStats(activator, actionCatalog);

        // The row's own content width is width - ContentPadding*2 (width is a hard constraint, matching every other
        // top-level row), so the three zones divide that, not the outer width.
        var availableWidth = width - host.ContentPadding.X * 2;
        var zoneWidth = availableWidth / TargetingRowZoneCount;

        var offsets = TargetShapePreviewGeometry.ComputeOffsets(activator.Targeting);
        var (minX, minY, columns, rows) = TargetShapePreviewGeometry.ComputeBounds(offsets);
        var cellSize = TargetShapePreviewGeometry.ComputeCellSize(columns, rows, zoneWidth, TargetShapePreviewMaxHeight);
        var gridWidth = columns * cellSize;
        var gridHeight = rows * cellSize;

        var textColumnHeight = stats.Count * RowHeight;
        var rowHeight = System.Math.Max(textColumnHeight, gridHeight);

        // The row's outer height is not externally constrained (unlike its width), so it grows by ContentPadding on top
        // and bottom instead -- rowHeight itself still means the row's own content height below.
        var row = CreateRowWindow(host, width, rowHeight + host.ContentPadding.Y * 2);

        for (var i = 0; i < stats.Count; i++)
        {
            var line = elementPoolService.CreateElement<TextWindow>(row, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, i * RowHeight), Size = new Vector2(zoneWidth, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = Color.Transparent },
                Text = new TextOptions { Text = stats[i].DisplayText, TextColor = lineColor(stats[i]) },
            });
            line.ContentFont = BodyFont;
            row.AddChild(line);
        }

        var gridX = zoneWidth + (zoneWidth - gridWidth) / 2f;
        var preview = elementPoolService.CreateElement<TargetShapePreviewElement>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(gridX, (rowHeight - gridHeight) / 2f), Size = new Vector2(gridWidth, gridHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        preview.Configure(offsets, minX, minY, cellSize, shapeHighlight?.Invoke(activator.Targeting, offsets));
        row.AddChild(preview);
    }

    /// <summary>A plain divider and the full description -- nothing when the description is blank.</summary>
    public void BuildDescriptionSection(Window host, ActivatableDefinition definition, float width)
    {
        if (string.IsNullOrWhiteSpace(definition.Description))
        {
            return;
        }

        BuildDivider(host, width);
        BuildWrappingTextLine(host, width, definition.Description);
    }

    /// <summary>A plain divider and the tags' display names, comma-separated.</summary>
    public void BuildTagsSection(Window host, ActivatableDefinition definition, float width) => BuildTagsSection(host, definition.Tags, width);

    /// <summary>A plain divider and tags' display names, comma-separated.</summary>
    public void BuildTagsSection(Window host, GameplayTagSet tags, float width)
    {
        BuildDivider(host, width);
        BuildWrappingTextLine(host, width, FormatTagDisplayNames(tags));
    }

    public void BuildFixedTextLine(Window host, float width, string text, Color color)
    {
        var line = elementPoolService.CreateElement<TextWindow>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = color },
        });
        line.ContentFont = BodyFont;
        host.AddChild(line);
    }

    /// <summary>Description/Tags -- length varies, so these wrap and auto-size rather than clipping at a fixed RowHeight (mirrors InspectionWindowContent.BuildDescriptionRow).</summary>
    public void BuildWrappingTextLine(Window host, float width, string text)
    {
        var line = elementPoolService.CreateElement<TextWindow>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = BodyTextColor },
        });
        line.ContentFont = BodyFont;
        host.AddChild(line);
    }

    /// <summary>Section-opening divider -- a single TextDivider row, always 95% width, in GameWindowPalette.HeaderTextColor. Plain (mirroring InspectionWindowContent.BuildSpacer) when label/textPosition are left at their defaults; labeled, with the line broken at textPosition, when a section has its own label.</summary>
    public void BuildDivider(Window host, float width, string label = "", float textPosition = 0f)
    {
        var divider = elementPoolService.CreateElement<TextDivider>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        host.AddChild(divider);
        divider.Configure(label, GameWindowPalette.HeaderTextColor, 0.95f, textPosition);
    }

    private string FormatTagDisplayNames(GameplayTagSet tags)
    {
        var displayNames = new List<string>(tags.Count);
        foreach (var tag in tags)
        {
            displayNames.Add(gameplayTags.GetDisplayName(tag));
        }

        return string.Join(", ", displayNames);
    }

    private Window CreateRowWindow(Window host, float width, float rowHeight)
    {
        var row = elementPoolService.CreateElement<Window>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, rowHeight), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        host.AddChild(row);
        return row;
    }

    private void AddIcon(Window row, string? spriteName, string glyph, Color glyphColor, float rowContentHeight)
    {
        var icon = elementPoolService.CreateElement<ItemIconElement>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, (rowContentHeight - IconSize) / 2f), Size = new Vector2(IconSize, IconSize), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        icon.Configure(spriteName, glyph, glyphColor, new Vector2(IconSize, IconSize));
        row.AddChild(icon);
    }
}
