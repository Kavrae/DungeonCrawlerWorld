using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;

namespace Presentation.UI.Looting;

/// <summary>
/// The corpse side of looting: a fixed summary (icon, name, killer, death tick) above a plain
/// item grid, capped at InventoryCapacity.MaxNonPlayerStackCount items -- no tabs/sort/filter,
/// see TODO.md's Corpse looting entry. Every position/size below is explicit and deterministic
/// (fixed pixel offsets, a fixed grid column count) rather than derived from ambient ContentSize
/// propagation or ChildElementTileMode auto-tiling -- an earlier version relying on those produced
/// an inconsistent, jumbled layout (confirmed by live testing) once nested inside this window's
/// own OnChildrenInitialized-built structure, a combination (Window-hosted InventoryGridContent
/// nested inside a manually-built custom Window) this codebase hadn't exercised before.
///
/// Sizes itself exactly once, from the corpse's own raw stack count, before building any
/// children at all (see OnChildrenInitialized) -- deliberately not a build-then-shrink-to-fit
/// pass: resizing gridWindow (or this window) *after* InventoryGridContent has already built real
/// cells re-fires its own Resized-driven rebuild reentrantly, mid-Measure, which is what actually
/// broke dragging out of a corpse's grid (confirmed by live testing -- cells rebuilt during that
/// reentrant call never got correctly hit-tested afterward). Also pins MinimumSize to this same
/// starting size (see Element.SetMinimumSize) so the window can never be user-resized smaller
/// than what it opened at.
/// </summary>
public sealed class CorpseInventoryWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    LabelRenderer labelRenderer,
    ComponentManager componentManager,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ItemCatalog itemCatalog,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState)
    : Window(fontService, elementPoolService, labelRenderer)
{
    private static readonly Vector2 IconSize = new(48, 48);
    private const float Padding = 8f;
    private const float SummaryTextWidth = 180f;
    private const float SummaryLineHeight = 18f;
    private const int SummaryLineCount = 3;
    private const float SummaryHeight = SummaryLineHeight * SummaryLineCount > 48 ? SummaryLineHeight * SummaryLineCount : 48f;

    /// <summary>Fixed at 5 columns (the "2x5" shape a 10-item corpse should read as) rather than derived from ambient window width -- see this class's own doc comment for why a dynamic width caused the original layout bug.</summary>
    private const int GridColumns = 5;

    /// <summary>Wide enough for exactly GridColumns columns of InventoryGridContent.CellSize, assuming its own private CellGap (1px, duplicated here -- see InventoryGridContent.cs) -- comfortably mid-range of the width band that computes to exactly GridColumns, not right at its edge.</summary>
    private const float GridWidth = GridColumns * (24f + 1f) + 10f;

    private readonly DirectComponentPool<DisplayTextComponent> _displayTextPool = componentManager.GetDirectPool<DisplayTextComponent>();
    private readonly PackedComponentPool<DeadComponent> _deadPool = componentManager.GetPackedPool<DeadComponent>();

    private int _entityId;
    private Tooltip _hoverPopup = null!;
    private Action<int, Guid> _onItemSelected = static (_, _) => { };
    private Action<int, Guid> _onCompareRequested = static (_, _) => { };

    /// <summary>Must be called after CreateElement but before Initialize -- same contract InventoryManagementWindow/AbilityScoreWindow's own Configure follow.</summary>
    public void Configure(int entityId, Tooltip hoverPopup, Action<int, Guid> onItemSelected, Action<int, Guid> onCompareRequested)
    {
        _entityId = entityId;
        _hoverPopup = hoverPopup;
        _onItemSelected = onItemSelected;
        _onCompareRequested = onCompareRequested;
    }

    /// <summary>See AbilityScoreWindow's own doc comment for why building children here, not in Configure -- ContentSize isn't real until after MeasureAndArrange.</summary>
    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();

        var gridHeight = ComputeGridHeight();
        var finalSize = ComputeOuterSize(gridHeight);
        SetMinimumSize(finalSize);
        SetSize(finalSize);

        BuildSummary();
        BuildGrid(gridHeight);
    }

    /// <summary>Always at least MinimumRows (a 2x5 grid) worth of height, regardless of how few items the corpse actually starts with -- items can be dragged in later (see InventoryActions.TryTransferStack), and this window's size is set once, up front, never revisited (see this class's own doc comment), so a corpse that opened with room for only 1 row would otherwise force the grid to scroll the moment a second row's worth of items arrived.</summary>
    private const int MinimumGridRows = 2;

    /// <summary>
    /// Rows needed for the corpse's raw InventoryItemStackComponent count -- not
    /// InventoryGridContent's own (possibly smaller) VisibleItemCount, since that depends on its
    /// default same-item stack merging and isn't known until the grid actually builds cells,
    /// which must happen only once, after this window's final size is already set (see this
    /// class's own doc comment). Raw stack count is never smaller than the merged cell count, so
    /// this is always tall enough -- worst case a little extra empty space at the bottom of the
    /// grid on a corpse carrying several divergent stacks of the same item, never a cramped grid.
    /// </summary>
    private float ComputeGridHeight()
    {
        var stackCount = componentManager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(_entityId);
        var rows = System.Math.Max(MinimumGridRows, (int)System.Math.Ceiling(stackCount / (double)GridColumns));
        return rows * (InventoryGridContent.CellSize.Y + 1f);
    }

    /// <summary>
    /// contentWidth/contentHeight are this window's own padded content area, not its outer bounds
    /// -- the top/left/right/bottom EDGE margin is the generic ContentPadding's job now (this
    /// Window has children), already folded into outerInsets below (see ChildContentPadding's own
    /// doc comment: gated on CanContainChildren alone, so this reads correctly even before any
    /// child has actually been added yet, which is exactly when this runs -- see OnChildrenInitialized).
    /// Only the one remaining INTERNAL gap (summary-to-grid) is still this method's own Padding.
    /// </summary>
    private Vector2 ComputeOuterSize(float gridHeight)
    {
        var contentWidth = System.Math.Max(IconSize.X + Padding + SummaryTextWidth, GridWidth);
        var contentHeight = SummaryHeight + Padding + gridHeight;

        var outerInsets = CurrentSize - ContentSize;
        return new Vector2(contentWidth, contentHeight) + outerInsets;
    }

    private void BuildSummary()
    {
        var icon = ElementPoolService.CreateElement<EntityIconElement>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = IconSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        icon.Configure(_entityId, IconSize);
        AddChild(icon);

        // Padding here is the icon-to-text gap, an internal gap distinct from the (now automatic)
        // left edge margin -- see this class's own doc comment on ComputeOuterSize.
        var textX = Padding + IconSize.X;
        AddSummaryLine(textX, 0, ResolveName(_entityId));
        AddSummaryLine(textX, 1, $"Slain by: {ResolveKillerName()}");
        AddSummaryLine(textX, 2, $"Died at tick: {ResolveDiedAtFrame()}");
    }

    private void AddSummaryLine(float x, int lineIndex, string text)
    {
        var line = ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, lineIndex * SummaryLineHeight), Size = new Vector2(SummaryTextWidth, SummaryLineHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = Color.White },
        });
        AddChild(line);
    }

    /// <summary>Built at its final height from the start (see ComputeGridHeight) -- never resized afterward, so InventoryGridContent's cells are built exactly once, non-reentrantly.</summary>
    private void BuildGrid(float gridHeight)
    {
        var gridWindow = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                // Padding here is the summary-to-grid gap, an internal gap distinct from the (now
                // automatic) top/left edge margin -- see this class's own doc comment on ComputeOuterSize.
                RelativePosition = new Vector2(0, SummaryHeight + Padding),
                Size = new Vector2(GridWidth, gridHeight),
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });

        // getSecondaryTargetEntityId always returns this corpse's own _entityId -- this window
        // *is* the currently-open secondary target for as long as it exists (see
        // SecondaryInventoryWindowController.OpenLoot, which never has more than one open at
        // once), so its own grid's Give/Take menu only ever needs to offer "Take," never query
        // anything external (contrast InventoryManagementWindow's own callback, which has to ask
        // whether a secondary window is open at all).
        gridWindow.SetContent(new InventoryGridContent(world, componentManager, itemCatalog, ElementPoolService, FontService, LabelRenderer, spriteSheetService, spriteRenderer, contextMenuController, _entityId, filterTag: null, _hoverPopup, () => _entityId, mapViewState, _onItemSelected, _onCompareRequested));
        AddChild(gridWindow); // Initializes gridWindow, which in turn Initializes (and builds the cells of) its InventoryGridContent -- see Window.OnChildrenInitialized/AddChild's own doc comment on why Initialize is never called explicitly here.
    }

    private string ResolveName(int entityId) =>
        _displayTextPool.TryGetReadonly(entityId, out var displayText) ? displayText.Name : "Unknown";

    private string ResolveKillerName()
    {
        if (!_deadPool.Has(_entityId) || _deadPool.GetReadonly(_entityId).KilledByEntityId is not { } killerEntityId)
        {
            return "Unknown";
        }

        return ResolveName(killerEntityId);
    }

    private long ResolveDiedAtFrame() => _deadPool.Has(_entityId) ? _deadPool.GetReadonly(_entityId).DiedAtFrame : 0;
}
