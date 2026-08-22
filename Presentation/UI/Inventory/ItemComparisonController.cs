using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>
/// Owns Item Details Comparison's own arm/add/remove state -- mirrors ActionTargetingController's
/// own arm/disarm shape (MapViewState.ArmedActionId/Disarm/CancelArmedOrPendingAction) but scoped
/// to inventory clicks rather than map targeting. The *anchor* item is always whatever
/// ItemDetailsWindowController's own single pane currently shows -- this controller never owns a
/// duplicate of that state, only the *additional* compared items beside it, each its own
/// independent ItemDetailsWindow instance (no shared comparison table -- see that class's own
/// comparedAgainst-driven coloring). Constructed with a direct reference to
/// ItemDetailsWindowController (built after it, one-directional, no cycle).
/// </summary>
public sealed class ItemComparisonController(
    ElementPoolService elementPoolService,
    ComponentManager componentManager,
    ItemCatalog itemCatalog,
    ActionCatalog actionCatalog,
    InventoryFolderController inventoryFolderController,
    ContextMenuController contextMenuController,
    MapWindow mapWindow,
    MapViewState mapViewState,
    ItemDetailsWindowController itemDetailsWindowController,
    CursorTextContent cursorTextContent)
{
    /// <summary>Shown at the cursor for as long as IsArmed -- see Arm/Disarm/ClearComparison.</summary>
    private const string SelectNextItemMessage = "Select next item...";

    /// <summary>Fixed HUD-style gap between one column and the next -- same spirit as ItemDetailsWindowController.Gap/InventoryFolderController.Gap.</summary>
    private const float Gap = 12f;

    private readonly MultiComponentPool<InventoryItemStackComponent> _stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

    /// <summary>The additional compared items, anchor excluded -- index-aligned with _columns.</summary>
    private readonly List<(int EntityId, Guid StackInstanceId)> _entries = [];

    private readonly List<ItemDetailsWindow> _columns = [];

    private UiLayerStack _layers = null!;

    /// <summary>Guards HandleColumnClosed against reacting to RebuildColumns' own programmatic teardown -- only a genuine user-initiated Close (via a column's own title-bar button) should remove that entry and trigger a fresh rebuild.</summary>
    private bool _isRebuilding;

    /// <summary>True while future inventory clicks should add to (or toggle out of) this comparison -- see Arm/Disarm. Independent of whether any columns are currently open: closing every column (one at a time, via each one's own Close button) does not itself disarm, and disarming (right-click) does not itself close already-open columns.</summary>
    public bool IsArmed { get; private set; }

    /// <summary>Every currently-open comparison column's own bounds -- ItemDetailsWindowController's own IsOutsideClick reads this so a click on a comparison column never wrongly closes the anchor.</summary>
    public IReadOnlyList<Rectangle> ColumnRectangles
    {
        get
        {
            var rectangles = new List<Rectangle>(_columns.Count);
            foreach (var column in _columns)
            {
                rectangles.Add(column.Rectangle);
            }

            return rectangles;
        }
    }

    public void Initialize(UiLayerStack layers) => _layers = layers;

    /// <summary>
    /// Invoked from an inventory item's own "Compare" context-menu option or ItemDetailsWindow's
    /// own Compare title button. Ensures entityId/stackId is shown as the anchor (opening/updating
    /// ItemDetailsWindowController's own pane), then arms -- future inventory clicks add to this
    /// comparison until Disarm. If this is a genuinely different anchor than whatever's currently
    /// shown/compared, any existing comparison is cleared first (comparing against a stale
    /// anchor's own items doesn't make sense); re-arming the *same* already-shown anchor (e.g.
    /// after a right-click paused adding) leaves already-open columns alone, resuming rather than
    /// restarting. A no-op if the target item has no Activator at all -- nothing to compare.
    /// </summary>
    public void Arm(int entityId, Guid stackId)
    {
        var isNewAnchor = itemDetailsWindowController.CurrentEntityId != entityId || itemDetailsWindowController.CurrentStackInstanceId != stackId;
        if (isNewAnchor)
        {
            ClearComparison();
        }

        itemDetailsWindowController.Open(entityId, stackId);

        if (itemDetailsWindowController.CurrentDefinition?.Activator is not { } activator)
        {
            return;
        }

        IsArmed = true;
        mapViewState.CompareRequiredActivatorType = activator.GetType();
        cursorTextContent.ShowPersistent(SelectNextItemMessage);
    }

    /// <summary>
    /// The inventory's own onItemSelected click path while IsArmed, instead of
    /// ItemDetailsWindowController.Open -- adds entityId/stackId to the comparison, or removes it
    /// if it's already in there (toggle, mirrors the hotbar's own re-press convention). No-op if
    /// it's the anchor itself, or if it's ineligible (Activator concrete type doesn't match
    /// MapViewState.CompareRequiredActivatorType -- the same check InventoryGridContent's own
    /// per-frame grey/highlight sync already makes, re-checked here since a stale click could
    /// still land after the eligibility state moved on).
    /// </summary>
    public void AddOrToggle(int entityId, Guid stackId)
    {
        if (!IsArmed)
        {
            return;
        }

        if (itemDetailsWindowController.CurrentEntityId == entityId && itemDetailsWindowController.CurrentStackInstanceId == stackId)
        {
            return;
        }

        if (mapViewState.CompareRequiredActivatorType is not { } requiredType ||
            !InventoryQueries.TryFindByStackInstanceId(_stacks, entityId, stackId, out var stack) ||
            !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition) ||
            definition.Activator?.GetType() != requiredType)
        {
            return;
        }

        var existingIndex = _entries.FindIndex(entry => entry.EntityId == entityId && entry.StackInstanceId == stackId);
        if (existingIndex >= 0)
        {
            _entries.RemoveAt(existingIndex);
        }
        else
        {
            _entries.Add((entityId, stackId));
        }

        RebuildColumns();
    }

    /// <summary>Right-click's own "stop adding" -- stops future clicks from adding/toggling and clears the grid's own eligible/ineligible highlighting (MapViewState.CompareRequiredActivatorType), but leaves already-open columns exactly as they are. Re-arming (Compare again, same or different anchor) is what resumes or restarts.</summary>
    public void Disarm()
    {
        IsArmed = false;
        mapViewState.CompareRequiredActivatorType = null;
        cursorTextContent.HidePersistent();
    }

    /// <summary>Full teardown -- every open column closes, all state clears. Called when the anchor itself closes (ItemDetailsWindowController.OnClosed) or changes to an unrelated item via a normal, non-compare click (see ClearIfAnchorChanging) -- a comparison against an anchor that's no longer even shown doesn't make sense.</summary>
    public void ClearComparison()
    {
        IsArmed = false;
        mapViewState.CompareRequiredActivatorType = null;
        cursorTextContent.HidePersistent();

        _isRebuilding = true;
        foreach (var column in _columns)
        {
            column.Close();
        }

        _isRebuilding = false;
        _columns.Clear();
        _entries.Clear();
    }

    /// <summary>Called by ShellBootstrapper's own onItemSelected dispatcher before a normal (non-armed) click opens a different item in ItemDetailsWindowController -- clears any active comparison first if the newly-clicked item genuinely differs from whatever's currently the anchor. A no-op both when nothing is active and when the click just re-selects the item already shown.</summary>
    public void ClearIfAnchorChanging(int entityId, Guid stackId)
    {
        if (_entries.Count == 0 && !IsArmed)
        {
            return;
        }

        if (itemDetailsWindowController.CurrentEntityId == entityId && itemDetailsWindowController.CurrentStackInstanceId == stackId)
        {
            return;
        }

        ClearComparison();
    }

    /// <summary>
    /// Closes and recreates every column from scratch, chained left to right off the anchor's own
    /// live position, each Configured with the current, full comparedAgainst set (anchor's own
    /// pane included -- see ItemDetailsWindowController.UpdateComparedAgainst) so coloring stays
    /// symmetric across every open pane. Also prunes any entry whose stack can no longer be
    /// resolved (consumed since it was added) before rebuilding, keeping _entries and the
    /// definitions built from it in lockstep, index for index.
    /// </summary>
    private void RebuildColumns()
    {
        _isRebuilding = true;
        foreach (var column in _columns)
        {
            column.Close();
        }

        _isRebuilding = false;
        _columns.Clear();

        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var (entityId, stackId) = _entries[i];
            if (!InventoryQueries.TryFindByStackInstanceId(_stacks, entityId, stackId, out var stack) ||
                !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out _))
            {
                _entries.RemoveAt(i);
            }
        }

        if (_entries.Count == 0)
        {
            itemDetailsWindowController.UpdateComparedAgainst([]);
            return;
        }

        var definitions = new List<ItemDefinition>(_entries.Count);
        foreach (var (entityId, stackId) in _entries)
        {
            InventoryQueries.TryFindByStackInstanceId(_stacks, entityId, stackId, out var stack);
            InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition);
            definitions.Add(definition);
        }

        itemDetailsWindowController.UpdateComparedAgainst(definitions);

        var anchorDefinition = itemDetailsWindowController.CurrentDefinition;
        var previousRectangle = itemDetailsWindowController.Rectangle;

        for (var i = 0; i < _entries.Count; i++)
        {
            var otherDefinitions = new List<ItemDefinition>(definitions.Count);
            if (anchorDefinition is not null)
            {
                otherDefinitions.Add(anchorDefinition);
            }

            for (var j = 0; j < definitions.Count; j++)
            {
                if (j != i)
                {
                    otherDefinitions.Add(definitions[j]);
                }
            }

            var column = CreateColumn(_entries[i].EntityId, _entries[i].StackInstanceId, definitions[i], previousRectangle, otherDefinitions);
            if (column is null)
            {
                continue;
            }

            _columns.Add(column);
            previousRectangle = column.Rectangle;
        }
    }

    /// <summary>OnCompareRequested is deliberately never set here -- comparison columns get a Close button but no Compare button of their own (see ItemDetailsWindow.OnChildrenInitialized's own doc comment on why attachment is opt-in per instance).</summary>
    private ItemDetailsWindow? CreateColumn(int entityId, Guid stackInstanceId, ItemDefinition definition, Rectangle previousRectangle, IReadOnlyList<ItemDefinition> comparedAgainst)
    {
        if (inventoryFolderController.PlayerInventoryWindow is not { } playerWindow)
        {
            return null;
        }

        var window = elementPoolService.CreateElement<ItemDetailsWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions
            {
                // Chained off the previous column's (or the anchor's, for the first one) own
                // live Rectangle -- the same "derived from a live sibling, not a hardcoded
                // offset" idiom every other child window in this codebase already uses.
                RelativePosition = new Vector2(previousRectangle.Right + Gap, previousRectangle.Top),
                MaximumSize = new Vector2(playerWindow.CurrentSize.X, mapWindow.CurrentSize.Y),
                DisplayMode = ElementDisplayMode.WrapContent,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                TitleText = definition.Name, // Distinguishes columns from each other and from the anchor's own fixed "Item Details" title.
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = false,
                CanUserScrollVertical = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = ItemDetailsWindow.BackgroundColor },
        });
        window.Configure(entityId, stackInstanceId, definition, playerWindow.ContentSize.X, comparedAgainst);
        window.Closed += HandleColumnClosed;
        window.OnRightClicked = position => contextMenuController.Open(new Vector2(position.X, position.Y), DynamicHudContextMenus.BuildCloseMenu(window, _layers));
        window.Initialize();
        _layers.Add(UiLayer.DynamicHud, window);
        _layers.OpenMenuWindow(window);

        return window;
    }

    /// <summary>Only reacts to a genuine user-initiated Close (via a column's own title-bar button) -- RebuildColumns' own programmatic teardown is guarded out via _isRebuilding. Removes the matching entry (found by this column's own index within _columns, since the two lists are built in lockstep) and rebuilds the rest.</summary>
    private void HandleColumnClosed(Element closedWindow)
    {
        _layers.Remove(UiLayer.DynamicHud, closedWindow);
        _layers.CloseMenuWindow(closedWindow);

        if (_isRebuilding || closedWindow is not ItemDetailsWindow closedColumn)
        {
            return;
        }

        var index = _columns.IndexOf(closedColumn);
        if (index < 0)
        {
            return;
        }

        _entries.RemoveAt(index);
        RebuildColumns();
    }
}
