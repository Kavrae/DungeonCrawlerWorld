using Engine.Tags;
using FontStashSharp;
using Game.Effects;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;
using UIEngine.Fonts;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI.Details;

/// <summary>The sections a details window shows for anything activatable -- an item or an action: name row, effects, cost, activation, description and tags.</summary>
/// <remarks>
/// Each Build method adds rows to the host window, which tiles its children vertically and is WrapContent (see
/// ItemDetailsWindow's doc comment for why), so every row is built against an explicit width. Every TextWindow row is
/// given its font explicitly: a pooled TextWindow keeps whatever font its previous user set (see HealthWindow._bodyFont).
/// Lines that Item Details Comparison colours take their colour from the caller; a window that compares nothing passes
/// BodyTextColor.
/// </remarks>
public sealed class ActivatableDetailsSections(ElementPoolService elementPoolService, FontService fontService, EffectTextNames names)
{
    public const float IconSize = 32f;
    public const float RowHeight = 18f;
    public const float RowTextGap = 6f;

    /// <summary>A wrapping text line's height cap: taller than any screen, so a long line grows with its column, which scrolls, rather than stopping at the screen's height (an element's default cap).</summary>
    public const float MaximumTextRowHeight = 10000f;

    /// <summary>Soft per-widget height cap for the target shape preview grid -- independent of the window's own overall MaximumSize/scroll safety net, so a long range or a large-AreaSize Circle can't single-handedly blow out the window's height (the one-screen rule); TargetShapePreviewGeometry.ComputeCellSize shrinks cells to fit within this instead. The aura reach's cap, so the two grids read at one size.</summary>
    private const float TargetShapePreviewMaxHeight = AuraReachPreviewMaxHeight;

    /// <summary>Height cap for an aura's reach diamond, kept small so an aura section adds little height (the one-screen rule).</summary>
    private const float AuraReachPreviewMaxHeight = 96f;

    /// <summary>How far toward the aura's glow a reach cell is shaded at the edge (weakest) and at the source (strongest).</summary>
    private const float ReachMinimumShade = 0.15f;

    private const float ReachMaximumShade = 0.75f;

    /// <summary>An activation preview's cell it can be aimed at but doesn't land on: the cell colour, halfway to the panel behind it.</summary>
    private static readonly Color ReachOnlyCellFill = Color.Lerp(WindowPalette.PanelContentColor, WindowPalette.PanelBackgroundColor, 0.5f);

    private const string AuraSwatchGlyph = "●";

    /// <summary>BuildActivationSection divides its own width into this many equal zones -- text (left), the target shape grid (centered in the middle one), and an empty zone (right, reserved for now).</summary>
    private const float TargetingRowZoneCount = 3f;

    public static readonly Color BodyTextColor = Color.White;

    /// <summary>A change to the user's advantage, and Item Details Comparison's better value.</summary>
    public static readonly Color BuffColor = Color.LightGreen;

    /// <summary>A change against the user, and Item Details Comparison's worse value. LightCoral, not IndianRed: IndianRed read too dark against the panel, hard to tell from BodyTextColor.</summary>
    public static readonly Color DebuffColor = Color.LightCoral;

    /// <summary>Room a TextWindow keeps around its text, added to the measured width of a line followed by a suffix.</summary>
    private const float LinePadding = 8f;

    /// <summary>Space between a line's text and its suffix.</summary>
    private const float SuffixGap = 2f;

    public SpriteFontBase BodyFont { get; } = fontService.GetFont(FontChrome.DefaultFontSize);

    private readonly SpriteFontBase _nameFont = fontService.GetFont(GameFontChrome.ItemDetailsNameFontSize);

    /// <summary>The sprite or glyph beside the name in the doubled name font; the row, for a caller that puts buttons in the reservedRightWidth it leaves.</summary>
    public Window BuildNameRow(Window host, ActivatableDefinition definition, float width, float reservedRightWidth = 0f) =>
        BuildNameRow(host, definition.SpriteName, definition.Glyph, definition.GlyphColor, definition.Name, width, reservedRightWidth);

    /// <summary>The sprite or glyph beside name in the doubled name font, for something that isn't an action or item (an aura).</summary>
    public Window BuildNameRow(Window host, string? spriteName, string glyph, Color glyphColor, string name, float width, float reservedRightWidth = 0f)
    {
        // Row content height is the taller of the icon and the (doubled) name text -- not just IconSize, since
        // _nameFont's own line height can exceed it. The row's outer width is a hard constraint (must equal width
        // exactly, matching every other top-level row so the host's WrapContent measurement stays consistent); its
        // outer height is not, so it grows by ContentPadding on top and bottom instead -- see HealthWindow.AddBarRow.
        var nameLineHeight = _nameFont.LineHeight;
        var rowContentHeight = System.Math.Max(IconSize, nameLineHeight);
        var row = CreateRowWindow(host, width, rowContentHeight + host.ContentPadding.Y * 2);

        AddIcon(row, spriteName, glyph, glyphColor, rowContentHeight);

        // A row has no side padding (CreateRowWindow), so its content is the whole width -- a hard constraint, so the
        // text shrinks to fit instead.
        var availableWidth = width;
        var textX = IconSize + RowTextGap;
        var nameLine = elementPoolService.CreateElement<TextWindow>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(textX, (rowContentHeight - nameLineHeight) / 2f), Size = new Vector2(System.Math.Max(0f, availableWidth - textX - reservedRightWidth), nameLineHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = name, TextColor = BodyTextColor },
        });
        nameLine.ContentFont = _nameFont;
        row.AddChild(nameLine);
        return row;
    }

    /// <summary>Height of a button on the name row.</summary>
    private const float NameRowButtonHeight = 20f;

    /// <summary>Space around a name-row button's label, and between two buttons.</summary>
    private const float NameRowButtonGap = 6f;

    /// <summary>The name row with buttons right-aligned at its top, left to right, the name shortened to leave them room -- Item Details' and Action Details' Compare, Stop Comparison and mode buttons.</summary>
    public Window BuildNameRowWithButtons(Window host, string? spriteName, string glyph, Color glyphColor, string name, float width, IReadOnlyList<(string Label, Action OnClicked)> buttons)
    {
        var buttonWidths = new float[buttons.Count];
        var buttonsWidth = 0f;
        for (var index = 0; index < buttons.Count; index++)
        {
            buttonWidths[index] = BodyFont.MeasureString(buttons[index].Label).X + NameRowButtonGap * 2;
            buttonsWidth += buttonWidths[index] + NameRowButtonGap;
        }

        var row = BuildNameRow(host, spriteName, glyph, glyphColor, name, width, reservedRightWidth: buttonsWidth);

        var buttonX = width;
        for (var index = buttons.Count - 1; index >= 0; index--)
        {
            buttonX -= buttonWidths[index];
            var (label, onClicked) = buttons[index];
            var button = elementPoolService.CreateElement<Button>(row, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(buttonX, 0), Size = new Vector2(buttonWidths[index], NameRowButtonHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true },
                Content = new ElementContentOptions { ContentColor = WindowPalette.ControlBackground },
                Text = new TextOptions { TextColor = WindowPalette.ControlLabelTextColor },
            });
            button.SetText(label);
            button.Clicked += _ => onClicked();
            row.AddChild(button);
            buttonX -= NameRowButtonGap;
        }

        return row;
    }

    /// <summary>An icon beside one line of body text, as tall as the icon -- the name row's shape without the doubled font (Item Details' gold value).</summary>
    public void BuildIconTextRow(Window host, string? spriteName, string glyph, Color glyphColor, string text, float width)
    {
        var rowContentHeight = System.Math.Max(IconSize, RowHeight);
        var row = CreateRowWindow(host, width, rowContentHeight + host.ContentPadding.Y * 2);

        AddIcon(row, spriteName, glyph, glyphColor, rowContentHeight);

        var textX = IconSize + RowTextGap;
        var availableWidth = width;
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
    /// <summary>"Effects" (or "While on" for a toggle), one line per effect entry, each aura grant followed by its aura's section (BuildAuraSection), or "None".</summary>
    /// <param name="preview">The definition as the player would use it; null shows its own amounts.</param>
    /// <param name="auraPreviews">An aura's effects at a strength (Game.Views.ActivationPreviews.PreviewAura); null shows them per point of strength, as content gives them.</param>
    public void BuildEffectsSection(Window host, ActivatableDefinition definition, DetailsMode mode, float width, Func<ItemComparisonStat, LineStyle> lineStyle, ActivatablePreview? preview = null, AuraEffectPreviews? auraPreviews = null) =>
        BuildEffectsSection(host, definition.Effects, definition.Toggle is null ? "Effects" : "While on", mode, width, lineStyle, preview?.Entries, auraPreviews, showsAuraSections: true);

    /// <summary>heading, then one line per entry of effects -- with showsAuraSections, each aura grant followed by its aura's section -- or "None".</summary>
    /// <param name="previews">Each entry's amounts as used; null shows the entries' own.</param>
    public void BuildEffectsSection(Window host, IReadOnlyList<Effect> effects, string heading, DetailsMode mode, float width, Func<ItemComparisonStat, LineStyle> lineStyle, IReadOnlyDictionary<IEffectEntry, EffectAmountPreview>? previews = null, AuraEffectPreviews? auraPreviews = null, bool showsAuraSections = false)
    {
        BuildDivider(host, width, heading, 0.125f);

        var anyLine = false;
        foreach (var effect in effects)
        {
            foreach (var entry in effect.Entries)
            {
                foreach (var stat in ItemComparisonStatExtraction.ExtractEntryStats(entry, names, mode, previews))
                {
                    BuildStatRow(host, width, stat, lineStyle(stat));
                    anyLine = true;
                }

                if (showsAuraSections && entry is AuraSourceGrant grant)
                {
                    BuildAuraSection(host, grant, previews?.GetValueOrDefault(grant), mode, width, auraPreviews);
                }
            }
        }

        if (!anyLine)
        {
            BuildFixedTextLine(host, width, "None", BodyTextColor);
        }
    }

    /// <summary>
    /// What an aura grant's aura does, under the line that grants it: a heading with its glow, its effects each second at
    /// the grant's strength, and its reach as a diamond of the grant's size, shaded by its strength. Advanced adds the
    /// strength at the source and the edge, the Strength lines (AuraDetailsWindow.StrengthLines), the effects at the edge
    /// too where they differ, and the aura's description.
    /// </summary>
    /// <remarks>
    /// Strength and size are the grant's as used (grantAmounts). The effects' lines carry no change in parentheses: an
    /// aura's amounts are given per point of strength, so the "change" would only restate it; their tooltips still show
    /// the breakdown. A glow-only aura (no effects) shows its heading and reach alone.
    /// </remarks>
    public void BuildAuraSection(Window host, AuraSourceGrant grant, EffectAmountPreview? grantAmounts, DetailsMode mode, float width, AuraEffectPreviews? auraPreviews)
    {
        var aura = grant.Aura;
        var power = (int)MathF.Round(grantAmounts?.ValueOr(EffectAmount.AuraPower, grant.Power) ?? grant.Power);
        var size = (int)MathF.Round(grantAmounts?.ValueOr(EffectAmount.AuraSize, grant.Size) ?? grant.Size);
        var edgePower = aura.Falloff.ValueAt(power, size, size);
        var effectsDifferAtEdge = mode == DetailsMode.Advanced && aura.Magnitude == AuraMagnitude.Power && edgePower != power && auraPreviews is not null;

        BuildAuraHeading(host, aura, width);

        var lines = new List<(ItemComparisonStat Stat, LineStyle Style)>();
        void AddPlain(string key, string text) => lines.Add((new ItemComparisonStat(key, text, null, HigherIsBetter: true), new LineStyle(BodyTextColor)));

        if (mode == DetailsMode.Advanced)
        {
            AddPlain("aura:strength", edgePower == power ? $"Strength {power} to the edge" : $"Strength {power} at the source, {edgePower} at the edge");
            foreach (var strengthLine in AuraDetailsWindow.StrengthLines(aura))
            {
                AddPlain("aura:strengthRule", strengthLine);
            }
        }

        if (aura.HasEffects)
        {
            AddPlain("aura:eachSecond", "Each second:");
            AddAuraEffectLines(lines, aura, auraPreviews?.Invoke(aura, power), mode, effectsDifferAtEdge ? " at the source" : string.Empty);
            if (effectsDifferAtEdge)
            {
                AddAuraEffectLines(lines, aura, auraPreviews!.Invoke(aura, edgePower), DetailsMode.Simple, " at the edge");
            }
        }

        BuildLinesBesideReach(host, width, lines, aura, power, size);

        if (mode == DetailsMode.Advanced && !string.IsNullOrWhiteSpace(aura.Description))
        {
            BuildWrappingTextLine(host, width, aura.Description);
        }
    }

    private void AddAuraEffectLines(List<(ItemComparisonStat Stat, LineStyle Style)> lines, AuraDefinition aura, IReadOnlyDictionary<IEffectEntry, EffectAmountPreview>? previews, DetailsMode mode, string suffix)
    {
        foreach (var stat in ItemComparisonStatExtraction.ExtractEffectStats(aura.Effects, names, mode, previews))
        {
            var isQualifier = stat.Key.EndsWith($":{EffectFormatting.LockedDuration}", StringComparison.Ordinal);
            var shown = stat with { DisplayText = isQualifier ? stat.DisplayText : stat.DisplayText + suffix, ChangeText = null };
            lines.Add((shown, new LineStyle(BodyTextColor)));
        }
    }

    /// <summary>A row with the aura's glow swatch and "Poison aura".</summary>
    private void BuildAuraHeading(Window host, AuraDefinition aura, float width)
    {
        var row = CreateRowWindow(host, width, RowHeight);
        row.ContentPadding = Vector2.Zero;
        AddText(row, Vector2.Zero, RowHeight, AuraSwatchGlyph, aura.GlowColor, tooltip: null, showsTooltipIndicator: false);
        AddText(row, new Vector2(RowHeight, 0), width - RowHeight, $"{aura.Name} aura", GameWindowPalette.HeaderTextColor, tooltip: null, showsTooltipIndicator: false);
    }

    /// <summary>lines in a column on the left, and the aura's reach to their right: a diamond of size, each cell shaded by the aura's strength there.</summary>
    private void BuildLinesBesideReach(Window host, float width, IReadOnlyList<(ItemComparisonStat Stat, LineStyle Style)> lines, AuraDefinition aura, int power, int size)
    {
        var availableWidth = width;
        var offsets = TargetShapePreviewGeometry.ComputeDiamondOffsets(size);
        var (minX, minY, columns, rows) = TargetShapePreviewGeometry.ComputeBounds(offsets);
        var cellSize = TargetShapePreviewGeometry.ComputeCellSize(columns, rows, availableWidth / TargetingRowZoneCount, AuraReachPreviewMaxHeight);
        var gridWidth = columns * cellSize;
        var gridHeight = rows * cellSize;
        var rowHeight = System.Math.Max(lines.Count * RowHeight, gridHeight);
        var textWidth = System.Math.Max(0f, availableWidth - gridWidth - RowTextGap);

        var row = CreateRowWindow(host, width, rowHeight + host.ContentPadding.Y * 2);
        for (var index = 0; index < lines.Count; index++)
        {
            AddStatLine(row, new Vector2(0, index * RowHeight), textWidth, lines[index].Stat, lines[index].Style);
        }

        var reach = elementPoolService.CreateElement<TargetShapePreviewElement>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(MathF.Floor(availableWidth - gridWidth), MathF.Floor((rowHeight - gridHeight) / 2f)), Size = new Vector2(gridWidth, gridHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        reach.Configure(offsets, minX, minY, cellSize, cellFill: offset => ReachCellFill(aura, power, size, offset));
        row.AddChild(reach);
    }

    /// <summary>A reach cell's fill: the panel's cell colour toward the aura's glow, the more the stronger the aura is there.</summary>
    private static Color ReachCellFill(AuraDefinition aura, int power, int size, Point offset)
    {
        var distance = System.Math.Abs(offset.X) + System.Math.Abs(offset.Y);
        var strength = power <= 0 ? 0f : aura.Falloff.ValueAt(power, size, distance) / (float)power;
        return Color.Lerp(WindowPalette.PanelContentColor, aura.GlowColor, ReachMinimumShade + (ReachMaximumShade - ReachMinimumShade) * strength);
    }

    private void BuildStatRow(Window host, float width, ItemComparisonStat stat, LineStyle style)
    {
        var row = CreateRowWindow(host, width, RowHeight);
        row.ContentPadding = Vector2.Zero;
        AddStatLine(row, Vector2.Zero, width, stat, style);
    }

    /// <summary>"Cost" and one line per cost entry (ItemComparisonStatExtraction.ExtractCostStats), coloured by lineStyle against what it is compared with -- nothing at all when using it costs nothing.</summary>
    public void BuildCostSection(Window host, ActivatableDefinition definition, float width, Func<ItemComparisonStat, LineStyle> lineStyle, ActivatablePreview? preview = null)
    {
        var stats = ItemComparisonStatExtraction.ExtractCostStats(definition, names, preview);
        if (stats.Count == 0)
        {
            return;
        }

        BuildDivider(host, width, "Cost", 0.125f);
        foreach (var stat in stats)
        {
            BuildStatRow(host, width, stat, lineStyle(stat));
        }
    }

    /// <summary>
    /// "Activation", then three equal-width zones: the targeting shape/range caption plus every Timing/per-activator-type
    /// line, stacked in the left zone; the target shape preview grid (see TargetShapePreviewGeometry/
    /// TargetShapePreviewElement) centered within the middle zone -- every tile it can be aimed at dimmed
    /// (ComputeReach), and the tiles it lands on aimed at the edge of that range in full (ComputeOffsets); the right zone
    /// left empty for now. Vertically centered
    /// against the taller of the text column/grid. Both are sized entirely up front, before the row is created, so none
    /// of it risks the Fixed-vs-WrapContent shrink-feedback pitfall the window itself hit earlier.
    /// </summary>
    /// <param name="shapeHighlight">The grid tiles Item Details Comparison highlights, given the spec and its tiles; null highlights none.</param>
    /// <param name="preview">The activation as the player would use it: the grid and the range lines show its targeting (a scroll's scaled by Intelligence). Null shows the activator's own.</param>
    /// <param name="mode">Which lines show (ItemComparisonStatExtraction.ExtractActivatorStats); the grid shows in both.</param>
    /// <param name="isToggle">A toggle's reach is its aura's (BuildAuraSection), so it gets no grid and no Shape, Range or Area Size line; with nothing else to show, no section at all.</param>
    public void BuildActivationSection(Window host, IActionActivator activator, DetailsMode mode, float width, Func<ItemComparisonStat, LineStyle> lineStyle, Func<TargetingSpec, IReadOnlyList<Point>, IReadOnlySet<Point>?>? shapeHighlight = null, ActivatablePreview? preview = null, bool isToggle = false)
    {
        var stats = ItemComparisonStatExtraction.ExtractActivatorStats(activator, preview, mode, names, showsTargeting: !isToggle);
        if (isToggle)
        {
            if (stats.Count > 0)
            {
                BuildDivider(host, width, "Activation", 0.125f);
                foreach (var stat in stats)
                {
                    BuildStatRow(host, width, stat, lineStyle(stat));
                }
            }

            return;
        }

        BuildDivider(host, width, "Activation", 0.125f);
        var targeting = preview?.Targeting ?? activator.Targeting;

        // A row has no side padding (CreateRowWindow), so the three zones divide its whole width.
        var zoneWidth = width / TargetingRowZoneCount;

        var offsets = TargetShapePreviewGeometry.ComputeOffsets(targeting);
        var shapeCells = new HashSet<Point>(offsets);
        var cells = new List<Point>(offsets);
        foreach (var reachable in TargetShapePreviewGeometry.ComputeReach(targeting))
        {
            if (shapeCells.Add(reachable))
            {
                cells.Add(reachable);
            }
        }

        var (minX, minY, columns, rows) = TargetShapePreviewGeometry.ComputeBounds(cells);
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
            AddStatLine(row, new Vector2(0, i * RowHeight), zoneWidth, stats[i], lineStyle(stats[i]));
        }

        var gridX = MathF.Floor(zoneWidth + (zoneWidth - gridWidth) / 2f);
        var shapePreview = elementPoolService.CreateElement<TargetShapePreviewElement>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(gridX, MathF.Floor((rowHeight - gridHeight) / 2f)), Size = new Vector2(gridWidth, gridHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        var landsOn = new HashSet<Point>(offsets);
        shapePreview.Configure(cells, minX, minY, cellSize, shapeHighlight?.Invoke(targeting, offsets), cellFill: cell => landsOn.Contains(cell) ? WindowPalette.PanelContentColor : ReachOnlyCellFill);
        row.AddChild(shapePreview);
    }

    /// <summary>"Resources", then what using it spends that it holds itself -- a wand's charges, later ammunition -- or nothing at all when it holds none.</summary>
    public void BuildResourcesSection(Window host, IActionActivator activator, float width, Func<ItemComparisonStat, LineStyle> lineStyle)
    {
        if (ItemComparisonStatExtraction.ExtractResourceStats(activator) is not { Count: > 0 } stats)
        {
            return;
        }

        BuildDivider(host, width, "Resources", 0.125f);
        foreach (var stat in stats)
        {
            var row = CreateRowWindow(host, width, RowHeight);
            row.ContentPadding = Vector2.Zero;
            AddStatLine(row, Vector2.Zero, width, stat, lineStyle(stat));
        }
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

    /// <summary>A plain divider and the tags' display names, comma-separated, each tag's description in the line's tooltip.</summary>
    public void BuildTagsSection(Window host, ActivatableDefinition definition, float width) => BuildTagsSection(host, definition.Tags, width);

    /// <summary>A plain divider and tags' display names, comma-separated, each tag's description in the line's tooltip.</summary>
    /// <param name="withDivider">False for a caller that put its own divider and lines above the tags (Item Details' restrictions).</param>
    public void BuildTagsSection(Window host, GameplayTagSet tags, float width, bool withDivider = true)
    {
        if (withDivider)
        {
            BuildDivider(host, width);
        }

        var line = BuildWrappingTextLine(host, width, TagDisplayText(tags));
        line.TooltipText = TagDescriptionText(tags);
        line.ShowsTooltipIndicator = true;
    }

    /// <summary>stat's line at position in parent, at most width wide, in style: its text, then style's suffix in its own colour; stat's tooltip over both.</summary>
    /// <remarks>A TextWindow draws in one colour, so the suffix is its own element, placed after the text's measured width.</remarks>
    public void AddStatLine(Window parent, Vector2 position, float width, ItemComparisonStat stat, LineStyle style)
    {
        if (style.Suffix is not { } suffix)
        {
            AddText(parent, position, width, stat.DisplayText, style.TextColor, stat.TooltipText, showsTooltipIndicator: true);
            return;
        }

        var textWidth = System.MathF.Min(width, BodyFont.MeasureString(stat.DisplayText).X + LinePadding);
        AddText(parent, position, textWidth, stat.DisplayText, style.TextColor, stat.TooltipText, showsTooltipIndicator: false);
        AddText(parent, position + new Vector2(textWidth + SuffixGap, 0), System.MathF.Max(0f, width - textWidth - SuffixGap), suffix, style.SuffixColor, stat.TooltipText, showsTooltipIndicator: true);
    }

    /// <param name="showsTooltipIndicator">Whether this element marks the line's tooltip: the line's last element, so a line split for its colours marks it once, at its right.</param>
    private void AddText(Window parent, Vector2 position, float width, string text, Color color, string? tooltip, bool showsTooltipIndicator)
    {
        var line = elementPoolService.CreateElement<TextWindow>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = new Vector2(width, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = color },
        });
        line.ContentFont = BodyFont;
        line.TooltipText = tooltip;
        line.ShowsTooltipIndicator = showsTooltipIndicator;
        parent.AddChild(line);
    }

    public TextWindow BuildFixedTextLine(Window host, float width, string text, Color color)
    {
        var line = elementPoolService.CreateElement<TextWindow>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = color },
        });
        line.ContentFont = BodyFont;
        host.AddChild(line);
        return line;
    }

    /// <summary>Description/Tags -- length varies, so these wrap and auto-size rather than clipping at a fixed RowHeight (mirrors InspectionWindowContent.BuildDescriptionRow).</summary>
    public TextWindow BuildWrappingTextLine(Window host, float width, string text)
    {
        var line = elementPoolService.CreateElement<TextWindow>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(width, MaximumTextRowHeight), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = BodyTextColor },
        });
        line.ContentFont = BodyFont;
        host.AddChild(line);
        return line;
    }

    /// <summary>Section-opening divider -- a single TextDivider row, always 95% width, in GameWindowPalette.HeaderTextColor. Plain (mirroring InspectionWindowContent.BuildSpacer) when label/textPosition are left at their defaults; labeled, with the line broken at textPosition, when a section has its own label.</summary>
    public void BuildDivider(Window host, float width, string label = "", float textPosition = 0f)
    {
        var divider = elementPoolService.CreateElement<TextDivider>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        host.AddChild(divider);
        divider.Configure(label, GameWindowPalette.HeaderTextColor, 0.95f, textPosition);
    }

    /// <summary>tags' display names, comma-separated.</summary>
    public string TagDisplayText(GameplayTagSet tags)
    {
        var displayNames = new List<string>(tags.Count);
        foreach (var tag in tags)
        {
            displayNames.Add(names.DisplayNameOf(tag));
        }

        return string.Join(", ", displayNames);
    }

    /// <summary>One line per tag: its display name and its description; null for no tags.</summary>
    public string? TagDescriptionText(GameplayTagSet tags)
    {
        if (tags.IsEmpty)
        {
            return null;
        }

        var lines = new List<string>(tags.Count);
        foreach (var tag in tags)
        {
            lines.Add($"{names.DisplayNameOf(tag)}: {names.DescriptionOf(tag)}");
        }

        return string.Join('\n', lines);
    }

    /// <summary>A row of width, the host's own vertical padding above and below its content and none at its sides, so everything in it starts at the host's left edge as a line added straight to the host does.</summary>
    private Window CreateRowWindow(Window host, float width, float rowHeight)
    {
        var row = elementPoolService.CreateElement<Window>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, rowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        row.ContentPadding = new Vector2(0f, host.ContentPadding.Y);
        host.AddChild(row);
        return row;
    }

    private void AddIcon(Window row, string? spriteName, string glyph, Color glyphColor, float rowContentHeight)
    {
        var icon = elementPoolService.CreateElement<IconElement>(row, new ElementOptions
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
