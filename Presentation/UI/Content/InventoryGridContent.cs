using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// Grid of item-stack icons for one entity's inventory, optionally filtered to stacks whose item
/// carries filterTag (null shows every stack -- the "All" tab) -- one InventoryItemStackCell per
/// stack, no empty filler cells, wraps to hostWindow's width and scrolls vertically without limit
/// (the host tab body window already has CanUserScrollVertical -- see TabbedContent). Sorted
/// alphabetically by item name by default (SortOrder), further narrowed by NameFilter (a
/// case-insensitive Name.Contains match) and HideDisabled -- all three are settable properties
/// driven by GridControl via InventoryTabContent, this class has no idea either of those exist.
/// Rebuilds (destroy-all, recreate-all -- cheap, since this only fires on an actual inventory
/// mutation or a property change, not every frame) whenever the pool's per-entity version
/// changes, or SortOrder/NameFilter/HideDisabled actually changes. Also self-polls
/// Mouse.GetState() every Update (see UpdateHover), the same idiom AbilityScoreWindow uses for
/// its own hover popup -- kept self-contained here rather than routed through UiInputController
/// since nothing else needs to know about it.
/// </summary>
public sealed class InventoryGridContent(
    ComponentManager componentManager,
    ItemCatalog itemCatalog,
    ElementPoolService elementPoolService,
    FontService fontService,
    GlyphRenderer glyphRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    int entityId,
    Tag? filterTag,
    HoverPopupWindow hoverPopup) : IElementContent
{
    public static readonly Vector2 CellSize = new(24, 24);
    private const float CellGap = 1f;

    private static readonly Color DisabledCellColor = Color.Gray;

    /// <summary>Popup sits just to the right of whatever's hovered, vertically centered against it -- see PopupPositioning.GetPosition(East).</summary>
    private static readonly Vector2 PopupGap = new(1, 1);

    private readonly MultiComponentPool<InventoryItemStackComponent> _stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
    private readonly List<InventoryItemStackComponent> _reusableStacks = [];
    private readonly List<(InventoryItemStackComponent Stack, ItemDefinition Definition)> _reusableVisibleEntries = [];
    private readonly List<InventoryItemStackCell> _cells = [];

    private readonly VersionWatcher _versionWatcher = new();

    private Window _hostWindow = null!;

    private InventoryItemStackCell? _hoveredCell;
    private int _hoveredFrames;

    private InventorySortOrder _sortOrder = InventorySortOrder.NameAscending;
    private string _nameFilter = string.Empty;
    private bool _hideDisabled;

    /// <summary>Defaults to NameAscending, reproducing this class's original always-alphabetical behavior exactly. Setting to the same value is a no-op -- doesn't force a rebuild.</summary>
    public InventorySortOrder SortOrder
    {
        get => _sortOrder;
        set
        {
            if (_sortOrder == value)
            {
                return;
            }

            _sortOrder = value;
            RebuildIfInitialized();
        }
    }

    /// <summary>Case-insensitive contains-match against each visible item's Name. Empty (the default) matches everything.</summary>
    public string NameFilter
    {
        get => _nameFilter;
        set
        {
            value ??= string.Empty;
            if (_nameFilter == value)
            {
                return;
            }

            _nameFilter = value;
            RebuildIfInitialized();
        }
    }

    /// <summary>False (the default) shows disabled stacks the same as always -- grayed via DisabledCellColor, per this class's existing behavior. True hides them entirely instead.</summary>
    public bool HideDisabled
    {
        get => _hideDisabled;
        set
        {
            if (_hideDisabled == value)
            {
                return;
            }

            _hideDisabled = value;
            RebuildIfInitialized();
        }
    }

    /// <summary>How many cells the last rebuild actually produced, after tag/name/disabled filtering -- GridControl's item-count display reads this (see InventoryTabContent).</summary>
    public int VisibleItemCount { get; private set; }

    /// <summary>Property setters above can fire before Initialize (e.g. InventoryTabContent wiring GridControl's events right after CreateElement) -- RebuildCells needs _hostWindow, so skip until Initialize's own unconditional rebuild has run at least once.</summary>
    private void RebuildIfInitialized()
    {
        if (_hostWindow is not null)
        {
            RebuildCells();
        }
    }

    /// <summary>
    /// Rebuilds unconditionally, not gated behind the version watcher -- TabbedContent reuses the
    /// same InventoryGridContent instance across repeated Deactivate/Initialize cycles as the
    /// player switches tabs back and forth (see SwitchTab), and Deactivate always fully tears
    /// down _cells/_hostWindow's children. Gating this on _versionWatcher.HasChanged would skip
    /// the rebuild on every re-Initialize after the first, since the underlying stack version
    /// hasn't actually changed just from switching tabs -- confirmed bug: a tab's grid was empty
    /// on every selection after its first. Primes the watcher afterward so Update's own check
    /// doesn't immediately redo the same rebuild this frame.
    /// </summary>
    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        hostWindow.Resized += OnHostWindowResized;

        RebuildCells();
        _versionWatcher.HasChanged(_stacks.GetEntityVersion(entityId));
    }

    public void Update(GameTime gameTime)
    {
        if (_versionWatcher.HasChanged(_stacks.GetEntityVersion(entityId)))
        {
            RebuildCells();
        }

        UpdateHover(Mouse.GetState());
    }

    /// <summary>
    /// Header highlight is immediate (instant visual feedback); the popup itself is delay-gated
    /// against the same shared HudMetrics.HoverTooltipDelayFrames AbilityScoreWindow/
    /// HotbarController use -- but hides immediately on candidate change/loss (no delay on
    /// hiding, only on showing, same convention MapViewState.HoverSlot uses).
    /// </summary>
    private void UpdateHover(MouseState mouseState)
    {
        var mousePosition = new Point(mouseState.X, mouseState.Y);
        var candidate = FindHoverCandidate(mousePosition);

        foreach (var cell in _cells)
        {
            cell.IsHovered = ReferenceEquals(cell, candidate);
        }

        if (candidate == _hoveredCell)
        {
            _hoveredFrames++;
        }
        else
        {
            _hoveredCell = candidate;
            _hoveredFrames = candidate is null ? 0 : 1;
        }

        if (candidate is null || _hoveredFrames < HudMetrics.HoverTooltipDelayFrames)
        {
            hoverPopup.Hide();
            return;
        }

        if (itemCatalog.TryGet(candidate.ItemDefinitionId, out var definition))
        {
            hoverPopup.ShowNear(candidate.Rectangle, PopupAnchor.East, PopupGap, definition.Summary, definition.Name);
        }
    }

    private InventoryItemStackCell? FindHoverCandidate(Point mousePosition)
    {
        foreach (var cell in _cells)
        {
            if (cell.Rectangle.Contains(mousePosition))
            {
                return cell;
            }
        }

        return null;
    }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        // Nothing to draw directly -- every stack is its own child InventoryItemStackCell,
        // which Window already draws as part of its own child-element loop.
    }

    /// <summary>Removes every cell -- called by TabbedContent when this tab is switched away from. Also hides the hover popup -- a no-op today (there's only one tab), but keeps this correct if a second tab is ever added.</summary>
    public void Deactivate()
    {
        elementPoolService.CloseAllChildren(_hostWindow);
        _cells.Clear();
        _hoveredCell = null;
        _hoveredFrames = 0;
        hoverPopup.Hide();
    }

    private void OnHostWindowResized(Element _) => RebuildCells();

    private void RebuildCells()
    {
        elementPoolService.CloseAllChildren(_hostWindow);
        _cells.Clear();

        InventoryQueries.CopyStacksForEntity(_stacks, entityId, _reusableStacks);

        _reusableVisibleEntries.Clear();
        foreach (var stack in _reusableStacks)
        {
            if (!itemCatalog.TryGet(stack.ItemDefinitionId, out var definition))
            {
                continue;
            }

            if (filterTag is { } tag && !definition.Tags.Contains(tag))
            {
                continue;
            }

            if (_hideDisabled && stack.IsDisabled)
            {
                continue;
            }

            if (_nameFilter.Length > 0 && !definition.Name.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _reusableVisibleEntries.Add((stack, definition));
        }

        SortVisibleEntries();
        VisibleItemCount = _reusableVisibleEntries.Count;

        var columns = ComputeColumnCount();

        for (var i = 0; i < _reusableVisibleEntries.Count; i++)
        {
            var (stack, definition) = _reusableVisibleEntries[i];

            var column = i % columns;
            var row = i / columns;
            var position = new Vector2(column * (CellSize.X + CellGap), row * (CellSize.Y + CellGap));

            var cell = elementPoolService.CreateElement<InventoryItemStackCell>(_hostWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = position, Size = CellSize, DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = stack.IsDisabled ? DisabledCellColor : Color.White },
            });
            cell.Configure(stack.ItemDefinitionId, definition.SpriteName, definition.Glyph, definition.GlyphColor, stack.Quantity, stack.IsDisabled, CellSize);
            _hostWindow.AddChild(cell);
            _cells.Add(cell);
        }
    }

    private int ComputeColumnCount() =>
        System.Math.Max(1, (int)((_hostWindow.ContentSize.X + CellGap) / (CellSize.X + CellGap)));

    private void SortVisibleEntries()
    {
        switch (_sortOrder)
        {
            case InventorySortOrder.NameDescending:
                _reusableVisibleEntries.Sort(static (a, b) => string.CompareOrdinal(b.Definition.Name, a.Definition.Name));
                break;
            case InventorySortOrder.QuantityDescending:
                _reusableVisibleEntries.Sort(static (a, b) => b.Stack.Quantity.CompareTo(a.Stack.Quantity));
                break;
            case InventorySortOrder.QuantityAscending:
                _reusableVisibleEntries.Sort(static (a, b) => a.Stack.Quantity.CompareTo(b.Stack.Quantity));
                break;
            default:
                _reusableVisibleEntries.Sort(static (a, b) => string.CompareOrdinal(a.Definition.Name, b.Definition.Name));
                break;
        }
    }
}
