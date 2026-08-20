using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Microsoft.Xna.Framework;
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
    Tooltip hoverPopup) : IElementContent
{
    public static readonly Vector2 CellSize = new(24, 24);
    private const float CellGap = 1f;

    /// <summary>Popup sits just to the right of whatever's hovered, vertically centered against it -- see PopupPositioning.GetPosition(East).</summary>
    private static readonly Vector2 PopupGap = new(1, 1);

    private readonly MultiComponentPool<InventoryItemStackComponent> _stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
    private readonly List<InventoryItemStackComponent> _reusableStacks = [];
    private readonly List<(InventoryItemStackComponent Stack, ItemDefinition Definition)> _reusableVisibleEntries = [];
    private readonly Dictionary<Guid, List<int>> _reusableGroupIndices = [];
    private readonly List<CellEntry> _reusableCellEntries = [];
    private readonly List<InventoryItemStackCell> _cells = [];

    private readonly VersionWatcher _versionWatcher = new();

    private Window _hostWindow = null!;

    private InventoryItemStackCell? _hoveredCell;
    private int _hoveredFrames;

    private InventorySortOrder _sortOrder = InventorySortOrder.NameAscending;
    private string _nameFilter = string.Empty;
    private bool _hideDisabled;
    private bool _groupDivergedStacks = true;

    /// <summary>Which single ItemDefinitionId (if any) is currently shown expanded -- ungrouped into its individual member cells -- instead of merged into one badged cell. Cleared by any filter/sort/grouping-toggle change and by Deactivate (tab switch); set/cleared by OnCellClicked.</summary>
    private Guid? _expandedItemDefinitionId;

    /// <summary>
    /// One rendered cell's resolved display data, computed once per RebuildCells after grouping --
    /// decoupled from "how many underlying stacks" since a merged cell has no single one. Quantity
    /// is what's actually shown (a group's summed total, or one stack's own); SortQuantity is what
    /// sort order compares against, and is deliberately the *group's* total for every member of a
    /// currently-expanded group (not each member's own Quantity) so they sort to the same key and
    /// land contiguously in the final layout regardless of active SortOrder -- see RebuildCells'
    /// own border-drawing pass, which assumes exactly that adjacency. ChargeText is null except
    /// for a cell resolving to exactly one stack (StackInstanceId set, never a Merged Stack) whose
    /// effective item's Activator is a WandActivator -- see BuildCellEntries' own doc comment for
    /// why charges take priority over Quantity in that one case, replacing it rather than showing
    /// alongside it.
    /// </summary>
    private readonly record struct CellEntry(ItemDefinition Definition, Guid? StackInstanceId, ushort Quantity, ushort SortQuantity, string? ChargeText, bool IsDisabled, bool IsDivergent, bool MergedStackBadgeVisible);

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
            _expandedItemDefinitionId = null;
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
            _expandedItemDefinitionId = null;
            RebuildIfInitialized();
        }
    }

    /// <summary>False (the default) shows disabled stacks the same as always -- grayed via InventoryItemStackCell's own icon tint (its background is transparent, so the tint is the only disabled cue). True hides them entirely instead.</summary>
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
            _expandedItemDefinitionId = null;
            RebuildIfInitialized();
        }
    }

    /// <summary>
    /// True (the default) merges every stack sharing an ItemDefinitionId into one cell with a
    /// summed quantity -- a "+" badge marks a merge containing at least one divergent stack (see
    /// InventoryItemStackCell's own doc comment); clicking a badged cell expands just that one item
    /// id into its individual stacks (see OnCellClicked). False disables merging entirely, for
    /// every item -- one cell per stack, the grid's original always-per-stack behavior.
    /// </summary>
    public bool GroupDivergedStacks
    {
        get => _groupDivergedStacks;
        set
        {
            if (_groupDivergedStacks == value)
            {
                return;
            }

            _groupDivergedStacks = value;
            _expandedItemDefinitionId = null;
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

        ItemDefinition? definition = null;
        var isSingleStack = candidate.StackInstanceId is not null;
        if (candidate.StackInstanceId is { } stackInstanceId)
        {
            if (InventoryQueries.TryFindByStackInstanceId(_stacks, entityId, stackInstanceId, out var stack))
            {
                InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out definition);
            }
        }
        else
        {
            // A merged cell (see CellEntry's own doc comment) has no single stack to resolve --
            // fall back to the plain catalog definition, the same "default" shape every member
            // shares the ItemDefinitionId of.
            itemCatalog.TryGet(candidate.ItemDefinitionId, out definition);
        }

        if (definition is null)
        {
            return;
        }

        var summary = definition.Summary;
        if (definition.Activator is { } activator)
        {
            summary = $"{summary}\nTarget: {activator.Targeting.Shape}";

            // Charges only makes sense for one specific physical stack -- a merged cell could be
            // averaging over several different charge counts, so it's suppressed there entirely
            // rather than showing a misleading single number.
            if (isSingleStack && activator is WandActivator wandActivator)
            {
                summary += $"\nCharges: {wandActivator.Charges}/{wandActivator.MaxCharges}";
            }
        }

        hoverPopup.ShowNear(candidate.Rectangle, PopupAnchor.East, PopupGap, summary, definition.Name);
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

    public void DrawContent(GameTime gameTime)
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
        _expandedItemDefinitionId = null;
        hoverPopup.Hide();
    }

    private void OnHostWindowResized(Element _) => RebuildCells();

    /// <summary>Clicking a badged (merged) cell expands its item id; clicking anything else while an id is expanded collapses it back -- including one of that group's own now-visible member cells, the same single "click toggles" rule applying uniformly rather than special-casing a click inside the expanded block.</summary>
    private void OnCellClicked(Element element)
    {
        if (element is not InventoryItemStackCell cell)
        {
            return;
        }

        if (cell.MergedStackBadgeVisible)
        {
            _expandedItemDefinitionId = cell.ItemDefinitionId;
        }
        else if (_expandedItemDefinitionId is not null)
        {
            _expandedItemDefinitionId = null;
        }
        else
        {
            return;
        }

        RebuildCells();
    }

    private void RebuildCells()
    {
        elementPoolService.CloseAllChildren(_hostWindow);
        _cells.Clear();

        InventoryQueries.CopyStacksForEntity(_stacks, entityId, _reusableStacks);

        _reusableVisibleEntries.Clear();
        foreach (var stack in _reusableStacks)
        {
            if (!InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition))
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

        BuildCellEntries();
        SortCellEntries();
        VisibleItemCount = _reusableCellEntries.Count;

        var columns = ComputeColumnCount();

        for (var i = 0; i < _reusableCellEntries.Count; i++)
        {
            var entry = _reusableCellEntries[i];

            var column = i % columns;
            var row = i / columns;
            var position = new Vector2(column * (CellSize.X + CellGap), row * (CellSize.Y + CellGap));
            var isExpandedMember = _expandedItemDefinitionId is not null && entry.Definition.Id == _expandedItemDefinitionId && !entry.MergedStackBadgeVisible;

            var cell = elementPoolService.CreateElement<InventoryItemStackCell>(_hostWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                // An expanded group's member cells skip their own normal border entirely -- the
                // group border (below) replaces it, drawn only on the group's outer perimeter, so
                // two adjacent members share a clean, unbroken join with no line between them.
                Layout = new ElementLayoutOptions { RelativePosition = position, Size = CellSize, DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = !isExpandedMember, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = Color.Transparent },
            });
            cell.Configure(entry.Definition.Id, entry.StackInstanceId, entry.Definition.SpriteName, entry.Definition.Glyph, entry.Definition.GlyphColor, entry.Quantity, entry.ChargeText, entry.IsDisabled, entry.IsDivergent, entry.MergedStackBadgeVisible, CellSize);
            cell.Clicked += OnCellClicked;
            _hostWindow.AddChild(cell);
            _cells.Add(cell);

            if (isExpandedMember)
            {
                // column == 0/columns - 1 guard the row-wrap case explicitly -- i-1/i+1 are still
                // valid indices across a row boundary (the previous row's last cell, or the next
                // row's first), but never a real visual left/right neighbor of this cell. i-columns/
                // i+columns need no equivalent guard: IsExpandedMemberAt's own bounds check already
                // returns false once that index runs off the top/bottom of the whole list.
                var top = !IsExpandedMemberAt(i - columns);
                var bottom = !IsExpandedMemberAt(i + columns);
                var left = column == 0 || !IsExpandedMemberAt(i - 1);
                var right = column == columns - 1 || !IsExpandedMemberAt(i + 1);
                cell.SetGroupBorderEdges(top, bottom, left, right);
            }
        }
    }

    private bool IsExpandedMemberAt(int index) =>
        index >= 0 && index < _reusableCellEntries.Count &&
        _reusableCellEntries[index].Definition.Id == _expandedItemDefinitionId &&
        !_reusableCellEntries[index].MergedStackBadgeVisible;

    private int ComputeColumnCount() =>
        System.Math.Max(1, (int)((_hostWindow.ContentSize.X + CellGap) / (CellSize.X + CellGap)));

    /// <summary>
    /// Groups _reusableVisibleEntries by ItemDefinitionId when GroupDivergedStacks is on --
    /// a group of exactly one stack (the overwhelmingly common case: every non-divergent item
    /// still only ever has one stack per id, per InventoryActions.AddItem's own merge behavior)
    /// renders unchanged; a larger group either merges into one badged cell (summed Quantity,
    /// first member's display data) or, if it's the currently-expanded id, renders as its
    /// individual member cells instead -- see CellEntry's own doc comment for SortQuantity's role
    /// in keeping an expanded group's members contiguous afterward.
    /// </summary>
    private void BuildCellEntries()
    {
        _reusableCellEntries.Clear();

        if (!_groupDivergedStacks)
        {
            foreach (var (stack, definition) in _reusableVisibleEntries)
            {
                _reusableCellEntries.Add(new CellEntry(definition, stack.StackInstanceId, stack.Quantity, stack.Quantity, ComputeChargeText(definition), stack.IsDisabled, stack.IsDivergent, MergedStackBadgeVisible: false));
            }

            return;
        }

        _reusableGroupIndices.Clear();
        for (var i = 0; i < _reusableVisibleEntries.Count; i++)
        {
            var itemId = _reusableVisibleEntries[i].Definition.Id;
            if (!_reusableGroupIndices.TryGetValue(itemId, out var indices))
            {
                indices = [];
                _reusableGroupIndices[itemId] = indices;
            }

            indices.Add(i);
        }

        foreach (var (itemId, indices) in _reusableGroupIndices)
        {
            if (indices.Count == 1)
            {
                var (stack, definition) = _reusableVisibleEntries[indices[0]];
                _reusableCellEntries.Add(new CellEntry(definition, stack.StackInstanceId, stack.Quantity, stack.Quantity, ComputeChargeText(definition), stack.IsDisabled, stack.IsDivergent, MergedStackBadgeVisible: false));
                continue;
            }

            var totalQuantity = 0;
            var anyDivergent = false;
            var allDisabled = true;
            foreach (var index in indices)
            {
                var memberStack = _reusableVisibleEntries[index].Stack;
                totalQuantity += memberStack.Quantity;
                anyDivergent |= memberStack.IsDivergent;
                allDisabled &= memberStack.IsDisabled;
            }

            var groupTotal = (ushort)totalQuantity;

            if (itemId == _expandedItemDefinitionId)
            {
                foreach (var index in indices)
                {
                    var (stack, definition) = _reusableVisibleEntries[index];
                    _reusableCellEntries.Add(new CellEntry(definition, stack.StackInstanceId, stack.Quantity, groupTotal, ComputeChargeText(definition), stack.IsDisabled, stack.IsDivergent, MergedStackBadgeVisible: false));
                }

                continue;
            }

            // A Merged Stack cell always shows its summed Quantity, never charges -- its members
            // can each carry a different charge count, so there is no single number to show (see
            // CellEntry's own doc comment).
            var first = _reusableVisibleEntries[indices[0]];
            _reusableCellEntries.Add(new CellEntry(first.Definition, StackInstanceId: null, groupTotal, groupTotal, ChargeText: null, allDisabled, IsDivergent: false, MergedStackBadgeVisible: anyDivergent));
        }
    }

    /// <summary>"{Charges}/{MaxCharges}" for a WandActivator item, else null -- the one case where a stack's remaining-uses count isn't its Quantity (see CellEntry's own doc comment for why the two never show together).</summary>
    private static string? ComputeChargeText(ItemDefinition definition) =>
        definition.Activator is WandActivator wandActivator ? $"{wandActivator.Charges}/{wandActivator.MaxCharges}" : null;

    /// <summary>Sorts by SortQuantity, not each entry's own Quantity, so an expanded group's members (all sharing the same SortQuantity -- the group's total) land contiguously regardless of active SortOrder; ties (including two entries with genuinely equal Quantity/Name) always break by ItemDefinitionId, both for determinism and so a coincidental tie with an unrelated item can never split an expanded group apart.</summary>
    private void SortCellEntries()
    {
        switch (_sortOrder)
        {
            case InventorySortOrder.NameDescending:
                _reusableCellEntries.Sort(static (a, b) => CompareWithTieBreak(string.CompareOrdinal(b.Definition.Name, a.Definition.Name), a, b));
                break;
            case InventorySortOrder.QuantityDescending:
                _reusableCellEntries.Sort(static (a, b) => CompareWithTieBreak(b.SortQuantity.CompareTo(a.SortQuantity), a, b));
                break;
            case InventorySortOrder.QuantityAscending:
                _reusableCellEntries.Sort(static (a, b) => CompareWithTieBreak(a.SortQuantity.CompareTo(b.SortQuantity), a, b));
                break;
            default:
                _reusableCellEntries.Sort(static (a, b) => CompareWithTieBreak(string.CompareOrdinal(a.Definition.Name, b.Definition.Name), a, b));
                break;
        }
    }

    private static int CompareWithTieBreak(int primaryComparison, CellEntry a, CellEntry b) =>
        primaryComparison != 0 ? primaryComparison : a.Definition.Id.CompareTo(b.Definition.Id);
}
