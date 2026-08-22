using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>
/// Persistent single-item details pane -- shows whatever item ItemDetailsWindowController last
/// selected, updating in place rather than opening a second window when a different item is
/// clicked (see that controller's own doc comment). Close-only (no minimize), the same shape as
/// InventoryManagementWindow/CorpseInventoryWindow, not InspectionWindow (minimize-only).
///
/// Sections, top to bottom, a divider (BuildDivider, mirroring InspectionWindowContent.BuildSpacer)
/// before every one after the first: sprite/glyph + name (no header); Effects (header, one line
/// per ActionEffect entry via ActionEffectFormatting.FormatEntry, or "None"); Activation (header,
/// omitted entirely when Activator is null -- Targeting/Timing plus one or two hand-picked fields
/// per concrete activator type, mirroring what InventoryGridContent.UpdateHover already
/// hand-picks for WandActivator); full
/// Description (no header, omitted when blank -- same convention InspectionWindowContent.
/// BuildDescriptionRow already uses); Tags, comma-separated (no header). Every top-level child
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
    GlyphRenderer glyphRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ActionCatalog actionCatalog)
    : Window(fontService, elementPoolService, glyphRenderer)
{
    public static readonly Color BackgroundColor = WindowPalette.PanelBackgroundColor;

    private const float IconSize = 32f;
    private const float RowHeight = 18f;
    private const float RowTextGap = 6f;
    private const float BlockPadding = 12f;
    private const float SeparatorHeight = 1f;

    /// <summary>Soft per-widget height cap for the target shape preview grid -- independent of this window's own overall MaximumSize/scroll safety net, specifically so one large-AreaSize Burst can't single-handedly blow out the window's height; TargetShapePreviewGeometry.ComputeCellSize shrinks cells to fit within this instead.</summary>
    private const float TargetShapePreviewMaxHeight = 160f;

    /// <summary>BuildTargetingRow divides its own width into this many equal zones -- text (left), the target shape grid (centered in the middle one), and an empty zone (right, reserved for now).</summary>
    private const float TargetingRowZoneCount = 3f;

    /// <summary>A generous, effectively-unlimited per-row height cap -- see InspectionWindowContent.UnboundedChildHeight's own doc comment for why this is needed: without it, a row tiled past the host window's own one-screen-tall content size gets silently clamped to nothing.</summary>
    private const float UnboundedChildHeight = 10000f;

    private static readonly Color HeaderTextColor = WindowPalette.TitleColor;
    private static readonly Color BodyTextColor = Color.White;
    private static readonly Color SeparatorColor = WindowPalette.PanelContentColor;

    private ItemDefinition? _definition;
    private float _contentWidth;
    private bool _childrenReady;

    /// <summary>
    /// Safe to call both before the first Initialize (a brand-new window -- the real build is
    /// deferred to OnChildrenInitialized) and afterward, on an already-open window now showing a
    /// different item (rebuilds immediately in that case). contentWidth is the target width every
    /// row is built against -- see this class's own doc comment for why it can't just be read
    /// from this window's own ContentSize the way a Fixed-mode host safely could.
    /// </summary>
    public void Configure(ItemDefinition definition, float contentWidth)
    {
        _definition = definition;
        _contentWidth = contentWidth;

        if (_childrenReady)
        {
            Rebuild();
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

        var width = _contentWidth;

        BuildNameRow(_definition, width);
        BuildDivider(width);
        BuildEffectsSection(_definition, width);

        if (_definition.Activator is { } activator)
        {
            BuildDivider(width);
            BuildActivationSection(activator, width);
        }

        if (!string.IsNullOrWhiteSpace(_definition.Description))
        {
            BuildDivider(width);
            BuildWrappingTextLine(width, _definition.Description);
        }

        BuildDivider(width);
        BuildWrappingTextLine(width, string.Join(", ", _definition.Tags));
    }

    private void BuildNameRow(ItemDefinition definition, float width)
    {
        var row = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, IconSize), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        AddChild(row);

        var icon = ElementPoolService.CreateElement<ItemIconElement>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(IconSize, IconSize), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        icon.Configure(definition.SpriteName, definition.Glyph, definition.GlyphColor, new Vector2(IconSize, IconSize));
        row.AddChild(icon);

        var textX = IconSize + RowTextGap;
        var nameLine = ElementPoolService.CreateElement<TextWindow>(row, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(textX, (IconSize - RowHeight) / 2f), Size = new Vector2(System.Math.Max(0f, width - textX), RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = definition.Name, TextColor = BodyTextColor },
        });
        row.AddChild(nameLine);
    }

    private void BuildEffectsSection(ItemDefinition definition, float width)
    {
        BuildFixedTextLine(width, "Effects", HeaderTextColor);

        var any = false;
        foreach (var effect in definition.Effects)
        {
            foreach (var entry in effect.Entries)
            {
                BuildFixedTextLine(width, ActionEffectFormatting.FormatEntry(entry), BodyTextColor);
                any = true;
            }
        }

        if (!any)
        {
            BuildFixedTextLine(width, "None", BodyTextColor);
        }
    }

    private void BuildActivationSection(IActionActivator activator, float width)
    {
        BuildFixedTextLine(width, "Activation", HeaderTextColor);
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
        var textLines = new List<string> { $"{activator.Targeting.Shape} (Range {activator.Targeting.Range})" };
        textLines.AddRange(ActionActivatorFormatting.BuildLines(activator, actionCatalog));

        var zoneWidth = width / TargetingRowZoneCount;

        var offsets = TargetShapePreviewGeometry.ComputeOffsets(activator.Targeting);
        var (minX, minY, columns, rows) = TargetShapePreviewGeometry.ComputeBounds(offsets);
        var cellSize = TargetShapePreviewGeometry.ComputeCellSize(columns, rows, zoneWidth, TargetShapePreviewMaxHeight);
        var gridWidth = columns * cellSize;
        var gridHeight = rows * cellSize;

        var textColumnHeight = textLines.Count * RowHeight;
        var rowHeight = System.Math.Max(textColumnHeight, gridHeight);

        var row = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, rowHeight), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        AddChild(row);

        for (var i = 0; i < textLines.Count; i++)
        {
            var line = ElementPoolService.CreateElement<TextWindow>(row, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, i * RowHeight), Size = new Vector2(zoneWidth, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = Color.Transparent },
                Text = new TextOptions { Text = textLines[i], TextColor = BodyTextColor },
            });
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
        preview.Configure(offsets, minX, minY, cellSize);
        row.AddChild(preview);
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
        AddChild(line);
    }

    /// <summary>Padding between one section and the next, with a 1px divider (SeparatorBar's own 75%-width centering) vertically centered within it -- mirrors InspectionWindowContent.BuildSpacer, but SeparatorColor is light (PanelContentColor) rather than Black, since this window's own background is the dark PanelBackgroundColor rather than the default white body.</summary>
    private void BuildDivider(float width)
    {
        var spacer = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, BlockPadding), MaximumSize = new Vector2(width, UnboundedChildHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        AddChild(spacer);

        var separator = ElementPoolService.CreateElement<SeparatorBar>(spacer, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, (BlockPadding - SeparatorHeight) / 2f), Size = new Vector2(width, SeparatorHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        separator.Configure(SeparatorColor);
        spacer.AddChild(separator);
    }
}
