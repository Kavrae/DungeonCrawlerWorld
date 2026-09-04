using Engine.Math;
using FontStashSharp;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>
/// Persistent single-item details pane -- shows whatever item ItemDetailsWindowController last
/// selected, updating in place rather than opening a second window when a different item is
/// clicked (see that controller's own doc comment). Close-only (no minimize), the same shape as
/// InventoryManagementWindow/SecondaryInventoryWindow, not InspectionWindow (minimize-only).
///
/// Sections, top to bottom, a divider (BuildDivider, a single TextDivider row -- plain when its
/// label is omitted, mirroring InspectionWindowContent.BuildSpacer; labeled for the two sections
/// that have one) before every one after the first: sprite/glyph + name (no header); Effects
/// (labeled divider, then one line per ActionEffect entry, or "None"); Activation (labeled
/// divider, omitted entirely when Activator is null -- a shape-preview grid plus
/// Targeting/Timing/per-activator-type text lines, all sourced
/// from ItemComparisonStatExtraction so single-item rendering and Item Details Comparison's own
/// per-line coloring can never drift out of sync about what lines exist); full
/// Description (no header, omitted when blank -- same convention InspectionWindowContent.
/// BuildDescriptionRow already uses); GoldValue (no header, currency-gold icon + "{n}G", same
/// icon-beside-text shape the name row uses); Tags, comma-separated (no header). Every top-level child
/// tiles vertically via the host window's own ChildElementTileMode.Vertical (see
/// ItemDetailsWindowController.Open), the same convention InspectionWindowContent uses, rather
/// than manual Y math -- only the name row's own icon-beside-text layout needs an explicit
/// RelativePosition, within its own single non-tiled child Window.
///
/// DisplayMode.WrapContent (set by ItemDetailsWindowController.Open, not this class), not Fixed
/// -- this window's own height needs to track content, which varies a lot per item, and a
/// Fixed-mode window whose height shrinks between two Configure calls re-measures its children
/// against its own already-small content size instead of a stable outer budget on every
/// subsequent rebuild, silently clamping a later child's height to 0 once an earlier one's real
/// content pushed it far enough down the column -- confirmed by reproduction (Tags rendering on
/// top of Description). WrapContent's own Measure path always threads its MaximumSize through to
/// children unchanged instead, sidestepping that shrink-feedback loop entirely, the same
/// mechanism Tooltip already relies on for its own auto-height content. Since ContentSize is
/// therefore an *output* of Measure here, not a stable input, every row is built against an
/// explicit contentWidth passed into Configure (the player's own Inventory window's ContentSize.X)
/// instead of reading this window's own ContentSize.X the way InspectionWindowContent's Fixed-mode
/// host safely can.
/// </summary>
public sealed class ItemDetailsWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    LabelRenderer labelRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ActionCatalog actionCatalog)
    : Window(fontService, elementPoolService, labelRenderer)
{
    private const float IconSize = 32f;
    private const float RowHeight = 18f;
    private const float RowTextGap = 6f;

    /// <summary>Soft per-widget height cap for the target shape preview grid -- independent of this window's own overall MaximumSize/scroll safety net, specifically so one large-AreaSize Burst can't single-handedly blow out the window's height; TargetShapePreviewGeometry.ComputeCellSize shrinks cells to fit within this instead.</summary>
    private const float TargetShapePreviewMaxHeight = 160f;

    /// <summary>BuildTargetingRow divides its own width into this many equal zones -- text (left), the target shape grid (centered in the middle one), and an empty zone (right, reserved for now).</summary>
    private const float TargetingRowZoneCount = 3f;

    /// <summary>A generous, effectively-unlimited per-row height cap -- see InspectionWindowContent.UnboundedChildHeight's own doc comment for why this is needed: without it, a row tiled past the host window's own one-screen-tall content size gets silently clamped to nothing.</summary>
    private const float UnboundedChildHeight = 10000f;

    private static readonly Color BodyTextColor = Color.White;

    /// <summary>Item Details Comparison's own per-line coloring -- Better doubles as the "this item has a stat at least one other compared item doesn't" exclusive marker too (see ResolveLineColor's own doc comment), so the same color means "advantage" either way.</summary>
    private static readonly Color BetterColor = Color.LightGreen;

    /// <summary>LightCoral, not IndianRed -- confirmed live that IndianRed's (205,92,92) read too dark/muted against the near-black panel background, hard to distinguish from BodyTextColor at a glance. LightCoral pairs naturally with BetterColor's own "Light" prefix and is meaningfully brighter (perceived luminance ~162 vs. ~126).</summary>
    private static readonly Color WorseColor = Color.LightCoral;

    /// <summary>TextWindow.ContentFont defaults to DefaultFontSize only the first time a pooled instance is ever truly constructed -- a settable property, not reset by Build/Configure, so a recycled instance keeps whatever size its *previous* consumer last set. Cached once and assigned explicitly wherever a TextWindow row is built (BuildNameRow's own _nameFont included) so no row's size ever depends on what the pool handed back -- see HealthWindow._bodyFont's own doc comment for the same reasoning. Without this, a TextWindow instance the pool last handed to BuildNameRow (set to _nameFont) could come back for an ordinary Effects/Activation/Description/Tags row still carrying the doubled name size.</summary>
    private readonly SpriteFontBase _bodyFont = fontService.GetFont(FontChrome.DefaultFontSize);

    /// <summary>See _bodyFont's own doc comment -- BuildNameRow's own doubled-size counterpart.</summary>
    private readonly SpriteFontBase _nameFont = fontService.GetFont(FontChrome.ItemDetailsNameFontSize);

    private ItemDefinition? _definition;
    private float _contentWidth;
    private IReadOnlyList<ItemDefinition> _comparedAgainst = [];
    private readonly List<IReadOnlyList<ItemComparisonStat>> _otherItemsStats = [];
    private bool _childrenReady;
    private int _currentEntityId;
    private Guid _currentStackInstanceId;

    /// <summary>Settable late-bound callback for this window's own "Compare" title button, if it has one -- see this class's own Initialize override for why only some instances do. Wired by whichever controller creates this instance (ItemDetailsWindowController for the anchor pane; ItemComparisonController deliberately never sets this for its own comparison columns) *before* calling Initialize.</summary>
    public Action<int, Guid>? OnCompareRequested { get; set; }

    /// <summary>
    /// Safe to call both before the first Initialize (a brand-new window -- the real build is
    /// deferred to OnChildrenInitialized) and afterward, on an already-open window now showing a
    /// different item (rebuilds immediately in that case). contentWidth is the target width every
    /// row is built against -- see this class's own doc comment for why it can't just be read
    /// from this window's own ContentSize the way a Fixed-mode host safely could. comparedAgainst
    /// is empty for plain single-item viewing; Item Details Comparison passes every *other*
    /// compared item's own definition here (see ResolveLineColor) so this item's own lines color
    /// symmetrically against them. entityId/stackInstanceId are this exact stack's own identity --
    /// not used for rendering at all, only so the Compare title button (if this instance has one)
    /// can invoke OnCompareRequested with the item currently shown, not a stale one captured at
    /// construction time.
    /// </summary>
    public void Configure(int entityId, Guid stackInstanceId, ItemDefinition definition, float contentWidth, IReadOnlyList<ItemDefinition> comparedAgainst = null!)
    {
        _currentEntityId = entityId;
        _currentStackInstanceId = stackInstanceId;
        _definition = definition;
        _contentWidth = contentWidth;
        _comparedAgainst = comparedAgainst ?? [];

        if (_childrenReady)
        {
            Rebuild();
        }
    }

    /// <summary>
    /// The Compare title button is opt-in per instance, not universal -- attached only when
    /// OnCompareRequested is already set by the time this runs (the anchor pane's own controller
    /// sets it before calling Initialize; Item Details Comparison's own columns never do), rather
    /// than always attaching it and leaving it a dead click on columns. Deliberately added here,
    /// not in OnChildrenInitialized -- Window.Initialize's own CloseBehavior/MinimizeRestoreBehavior
    /// attachment (see its own doc comment) doesn't run until *after* base.Initialize() (which is
    /// what actually fires OnChildrenInitialized, from deep inside Element.Initialize) returns, so
    /// attaching Compare there ran before Close ever existed -- appending (the default insertIndex)
    /// only inserts to the left of whatever's *already* attached, so Compare ended up added first
    /// and read as the rightmost button, past Close's own right edge. Calling AddTitleButton here,
    /// after base.Initialize() has fully returned (Close/Minimize included), keeps every window's
    /// title buttons in the same left-to-right order: unique -> minimize/restore -> close.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        if (OnCompareRequested is not null)
        {
            var compareButton = BuildTitleButton(this, "↔");
            compareButton.Clicked += _ => OnCompareRequested?.Invoke(_currentEntityId, _currentStackInstanceId);
            AddTitleButton(compareButton);
        }
    }

    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();
        _childrenReady = true;

        Rebuild();
    }

    private void Rebuild()
    {
        ElementPoolService.CloseAllChildren(this);

        if (_definition is null)
        {
            return;
        }

        _otherItemsStats.Clear();
        foreach (var other in _comparedAgainst)
        {
            _otherItemsStats.Add(ItemComparisonStatExtraction.Extract(other, actionCatalog));
        }

        // _contentWidth is the target OUTER width this window should end up matching (see this
        // class's own doc comment) -- since this window is WrapContent (grows to fit its rows plus
        // ContentPadding on both edges, not the reverse), rows themselves must be sized to
        // _contentWidth minus that padding, or the window would end up ContentPadding*2 wider than
        // the caller actually asked for.
        var width = _contentWidth - ContentPadding.X * 2;

        BuildNameRow(_definition, width);
        BuildDivider(width, "Effects", 0.125f);
        BuildEffectsSection(_definition, width);

        if (_definition.Activator is { } activator)
        {
            BuildDivider(width, "Activation", 0.125f);
            BuildActivationSection(activator, width);
        }

        if (!string.IsNullOrWhiteSpace(_definition.Description))
        {
            BuildDivider(width);
            BuildWrappingTextLine(width, _definition.Description);
        }

        BuildDivider(width);
        BuildValueRow(_definition.GoldValue, width);

        BuildDivider(width);
        BuildWrappingTextLine(width, string.Join(", ", _definition.Tags));
    }

    /// <summary>Item's base Gold worth (see ItemDefinition.GoldValue's own doc comment) -- same icon+text row shape BuildNameRow uses, but with a fixed RowHeight text line (no doubled name font) and the Currency-Gold sprite/glyph instead of the item's own.</summary>
    private void BuildValueRow(int value, float width)
    {
        var rowContentHeight = System.Math.Max(IconSize, RowHeight);
        var rowHeight = rowContentHeight + ContentPadding.Y * 2;
        var row = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, rowHeight), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        AddChild(row);

        var icon = ElementPoolService.CreateElement<ItemIconElement>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, (rowContentHeight - IconSize) / 2f), Size = new Vector2(IconSize, IconSize), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        icon.Configure("Currency-Gold", "G", Color.Gold, new Vector2(IconSize, IconSize));
        row.AddChild(icon);

        var textX = IconSize + RowTextGap;
        var availableWidth = width - ContentPadding.X * 2;
        var valueLine = ElementPoolService.CreateElement<TextWindow>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(textX, (rowContentHeight - RowHeight) / 2f), Size = new Vector2(System.Math.Max(0f, availableWidth - textX), RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = $"{value}G", TextColor = BodyTextColor },
        });
        valueLine.ContentFont = _bodyFont; // See _bodyFont's own doc comment.
        row.AddChild(valueLine);
    }

    private void BuildNameRow(ItemDefinition definition, float width)
    {
        // Row content height is the taller of the icon and the (now doubled) name text -- no
        // longer just IconSize, since _nameFont's own line height can exceed it. row's outer
        // width is a hard constraint (must equal width exactly, matching every other top-level
        // row so this window's own WrapContent measurement stays consistent); its outer height is
        // not similarly constrained, so it grows by ContentPadding on top and bottom instead --
        // see HealthWindow.AddBarRow's own doc comment for the same reasoning.
        var nameLineHeight = _nameFont.LineHeight;
        var rowContentHeight = System.Math.Max(IconSize, nameLineHeight);
        var rowHeight = rowContentHeight + ContentPadding.Y * 2;
        var row = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, rowHeight), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        AddChild(row);

        var icon = ElementPoolService.CreateElement<ItemIconElement>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, (rowContentHeight - IconSize) / 2f), Size = new Vector2(IconSize, IconSize), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        icon.Configure(definition.SpriteName, definition.Glyph, definition.GlyphColor, new Vector2(IconSize, IconSize));
        row.AddChild(icon);

        // row's own content width is width - ContentPadding*2 (see the width comment above), not
        // the outer width -- width is a hard constraint, so the text shrinks to fit instead.
        var availableWidth = width - ContentPadding.X * 2;
        var textX = IconSize + RowTextGap;
        var nameLine = ElementPoolService.CreateElement<TextWindow>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(textX, (rowContentHeight - nameLineHeight) / 2f), Size = new Vector2(System.Math.Max(0f, availableWidth - textX), nameLineHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = definition.Name, TextColor = BodyTextColor },
        });
        nameLine.ContentFont = _nameFont; // Must match the font Size/nameLineHeight above were computed for -- see _nameFont's own doc comment for why this can't be left to TextWindow's own default.
        row.AddChild(nameLine);
    }

    private void BuildEffectsSection(ItemDefinition definition, float width)
    {
        var stats = ItemComparisonStatExtraction.ExtractEffectStats(definition);
        foreach (var stat in stats)
        {
            BuildFixedTextLine(width, stat.DisplayText, ResolveLineColor(stat));
        }

        if (stats.Count == 0)
        {
            BuildFixedTextLine(width, "None", BodyTextColor);
        }
    }

    private void BuildActivationSection(IActionActivator activator, float width)
    {
        BuildTargetingRow(activator, width);
    }

    /// <summary>
    /// Three equal-width zones: targeting shape/range caption plus every Timing/per-activator-type
    /// line, stacked in the left zone; the target shape preview grid (see
    /// TargetShapePreviewGeometry/TargetShapePreviewElement for the shape math/rendering) centered
    /// within the middle zone; the right zone deliberately left empty for now. Vertically centered
    /// against the taller of the text column/grid. Both are sized entirely up front (line count *
    /// RowHeight for the text column; columns/rows/cellSize from the same TargetingSpec for the
    /// grid, capped to the middle zone's own width) before this row -- and everything inside it --
    /// is created, so none of it risks the Fixed-vs-WrapContent shrink-feedback pitfall the window
    /// itself hit earlier.
    /// </summary>
    private void BuildTargetingRow(IActionActivator activator, float width)
    {
        var stats = ItemComparisonStatExtraction.ExtractActivatorStats(activator, actionCatalog);

        // row's own content width is width - ContentPadding*2 (width is a hard constraint,
        // matching every other top-level row), so the three zones divide that, not the outer width.
        var availableWidth = width - ContentPadding.X * 2;
        var zoneWidth = availableWidth / TargetingRowZoneCount;

        var offsets = TargetShapePreviewGeometry.ComputeOffsets(activator.Targeting);
        var (minX, minY, columns, rows) = TargetShapePreviewGeometry.ComputeBounds(offsets);
        var cellSize = TargetShapePreviewGeometry.ComputeCellSize(columns, rows, zoneWidth, TargetShapePreviewMaxHeight);
        var gridWidth = columns * cellSize;
        var gridHeight = rows * cellSize;

        var textColumnHeight = stats.Count * RowHeight;
        var rowHeight = System.Math.Max(textColumnHeight, gridHeight);

        // row's outer height is not externally constrained (unlike its width), so it grows by
        // ContentPadding on top and bottom instead -- rowHeight itself still means row's own
        // content height throughout the rest of this method (text/grid vertical centering).
        var row = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, rowHeight + ContentPadding.Y * 2), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        AddChild(row);

        for (var i = 0; i < stats.Count; i++)
        {
            var line = ElementPoolService.CreateElement<TextWindow>(row, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, i * RowHeight), Size = new Vector2(zoneWidth, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = Color.Transparent },
                Text = new TextOptions { Text = stats[i].DisplayText, TextColor = ResolveLineColor(stats[i]) },
            });
            line.ContentFont = _bodyFont; // See _bodyFont's own doc comment.
            row.AddChild(line);
        }

        var gridX = zoneWidth + (zoneWidth - gridWidth) / 2f;
        var preview = ElementPoolService.CreateElement<TargetShapePreviewElement>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(gridX, (rowHeight - gridHeight) / 2f), Size = new Vector2(gridWidth, gridHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        preview.Configure(offsets, minX, minY, cellSize, ComputeShapeHighlight(activator.Targeting, offsets));
        row.AddChild(preview);
    }

    /// <summary>
    /// Null (plain grid, no highlight) unless every compared item -- anchor included -- shares
    /// this exact same Targeting.Shape ("do not compare different shapes for color-coding," even
    /// though eligibility only gates on matching Activator type, not Shape). When shapes do match,
    /// a tile highlights green if it's present in this item's own offsets but absent from at
    /// least one other compared item's own offsets -- the same "green if not every other compared
    /// item has it" rule ResolveLineColor already applies per stat line, just at tile granularity
    /// instead. No red case exists here -- a tile absent from this item's own grid was never drawn
    /// in the first place, so there is nothing to color.
    /// </summary>
    private HashSet<Point>? ComputeShapeHighlight(TargetingSpec targeting, IReadOnlyList<Point> offsets)
    {
        if (_comparedAgainst.Count == 0)
        {
            return null;
        }

        var otherOffsetSets = new List<HashSet<Point>>(_comparedAgainst.Count);
        foreach (var other in _comparedAgainst)
        {
            if (other.Activator is not { } otherActivator || otherActivator.Targeting.Shape != targeting.Shape)
            {
                return null;
            }

            otherOffsetSets.Add(new HashSet<Point>(TargetShapePreviewGeometry.ComputeOffsets(otherActivator.Targeting)));
        }

        var highlighted = new HashSet<Point>();
        foreach (var offset in offsets)
        {
            foreach (var otherOffsets in otherOffsetSets)
            {
                if (!otherOffsets.Contains(offset))
                {
                    highlighted.Add(offset);
                    break;
                }
            }
        }

        return highlighted;
    }

    /// <summary>
    /// Normal (no comparison active, or this exact Key isn't shared/rankable). Better/Green when
    /// at least one other compared item lacks a line with this same Key at all -- an advantage/
    /// exclusive marker, the whole line colored rather than just a "name" token (this codebase's
    /// TextWindow has one TextColor for its entire string, no per-substring styling -- see the
    /// new "Rich inline text formatting" TODO item logged alongside this feature). Otherwise, if
    /// every other compared item *does* have this Key and every one of them (plus this line
    /// itself) has a ComparableValue, magnitude-colored via ItemComparisonHighlighting -- ties
    /// (or a non-numeric Key shared by all, e.g. Shape, or two Scrolls casting different named
    /// spells) stay Normal.
    /// </summary>
    private Color ResolveLineColor(ItemComparisonStat stat)
    {
        if (_comparedAgainst.Count == 0)
        {
            return BodyTextColor;
        }

        var otherValues = new List<double>();
        foreach (var otherStats in _otherItemsStats)
        {
            var found = false;
            foreach (var otherStat in otherStats)
            {
                if (otherStat.Key != stat.Key)
                {
                    continue;
                }

                found = true;
                if (otherStat.ComparableValue is { } otherValue)
                {
                    otherValues.Add(otherValue);
                }

                break;
            }

            if (!found)
            {
                return BetterColor;
            }
        }

        if (stat.ComparableValue is not { } ownValue)
        {
            return BodyTextColor;
        }

        otherValues.Add(ownValue);
        var highlight = ItemComparisonHighlighting.ComputeHighlight(ownValue, otherValues, stat.HigherIsBetter);
        return highlight switch
        {
            ComparisonHighlight.Better => BetterColor,
            ComparisonHighlight.Worse => WorseColor,
            _ => BodyTextColor,
        };
    }

    private void BuildFixedTextLine(float width, string text, Color color)
    {
        var line = ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = color },
        });
        line.ContentFont = _bodyFont; // See _bodyFont's own doc comment.
        AddChild(line);
    }

    /// <summary>Description/Tags -- length varies per item, so these wrap and auto-size rather than clipping at a fixed RowHeight (mirrors InspectionWindowContent.BuildDescriptionRow).</summary>
    private void BuildWrappingTextLine(float width, string text)
    {
        var line = ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = BodyTextColor },
        });
        line.ContentFont = _bodyFont; // See _bodyFont's own doc comment.
        AddChild(line);
    }

    /// <summary>Section-opening divider -- a single TextDivider row, always 95% width, in WindowPalette.HeaderTextColor. Plain (mirroring InspectionWindowContent.BuildSpacer) when label/textPosition are left at their defaults; labeled, with the line broken at textPosition, when a section has its own label (Effects/Activation).</summary>
    private void BuildDivider(float width, string label = "", float textPosition = 0f)
    {
        var divider = ElementPoolService.CreateElement<TextDivider>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        AddChild(divider);
        divider.Configure(label, WindowPalette.HeaderTextColor, 0.95f, textPosition);
    }
}
