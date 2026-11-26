using Engine.Tags;
using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.Input;
using Presentation.UI.Content;
using UIEngine;
using UIEngine.UI;

namespace Presentation.UI.Actions;

/// <summary>Which of the Actions window's tabs a grid fills.</summary>
public enum ActionsTabKind : byte
{
    /// <summary>Every bindable menu command.</summary>
    Menu,

    /// <summary>The player's Core actions.</summary>
    Core,

    /// <summary>Every action the player can use other than its Core ones.</summary>
    All,

    /// <summary>The actions in All that fall in one category (ActionsTab.Category).</summary>
    Category,
}

/// <summary>One of the Actions window's tabs: its kind, and for a category tab the Action category tag it shows.</summary>
public readonly record struct ActionsTab(ActionsTabKind Kind, GameplayTag Category)
{
    public static readonly ActionsTab Menu = new(ActionsTabKind.Menu, GameplayTag.None);
    public static readonly ActionsTab Core = new(ActionsTabKind.Core, GameplayTag.None);
    public static readonly ActionsTab All = new(ActionsTabKind.All, GameplayTag.None);

    public static ActionsTab OfCategory(GameplayTag category) => new(ActionsTabKind.Category, category);
}

/// <summary>The order an Actions grid lists its cells in, by name.</summary>
public enum ActionSortOrder : byte
{
    NameAscending,
    NameDescending,
}

/// <summary>One Actions window tab's grid of ActionCells: the actions or menu commands its tab holds, filtered by name and sorted.</summary>
/// <remarks>
/// One cell per action id, showing the player's effective definition (its own override if it has one) -- there is no
/// stacking. The cells are rebuilt when what the player can use changes (ActionStateView.GetActionSetVersion), the view (Icon or Card, ActionsWindowServices.GridView), the filter or
/// the sort changes, or the host is resized; what each cell shows from frame to frame -- blocked, cooling down, on, its
/// costs -- is set every frame without a rebuild.
/// </remarks>
public sealed class ActionGridContent(ActionsWindowServices services, ElementPoolService elementPoolService, int entityId, ActionsTab tab) : IElementContent
{
    private static readonly Vector2 IconCellSize = InventoryGridContent.CellSize;

    /// <summary>A card is as wide as an inventory card (InventoryGridContent.ShopCellWidthMultiplier squares), one square tall.</summary>
    private static readonly Vector2 CardCellSize = new(InventoryGridContent.CellSize.X * InventoryGridContent.ShopCellWidthMultiplier, InventoryGridContent.CellSize.Y);
    private const float CellGap = InventoryGridContent.CellGap;

    private readonly List<ActionCell> _cells = [];
    private readonly List<Guid> _reusableActionIds = [];
    private readonly List<(string Name, ActionCellSubject Subject, string? SpriteName, string Glyph, Color GlyphColor)> _reusableEntries = [];
    private readonly VersionWatcher _versionWatcher = new();
    private readonly BoundHotkeyLabels _keyLabels = new(services.HotkeyBindingView);

    private Window? _hostWindow;
    private Window? _rootWindow;
    private bool _showsCards;
    private ActionCell? _hoveredCell;
    private int _hoveredFrames;
    private ActionSortOrder _sortOrder;
    private string _nameFilter = string.Empty;

    /// <summary>How many cells the last rebuild made -- the tab's count.</summary>
    public int VisibleItemCount { get; private set; }

    public ActionSortOrder SortOrder
    {
        get => _sortOrder;
        set
        {
            if (_sortOrder != value)
            {
                _sortOrder = value;
                RebuildIfInitialized();
            }
        }
    }

    /// <summary>Case-insensitive contains-match against each cell's name; empty matches everything.</summary>
    public string NameFilter
    {
        get => _nameFilter;
        set
        {
            if (_nameFilter != value)
            {
                _nameFilter = value;
                RebuildIfInitialized();
            }
        }
    }

    /// <summary>The cells laid out now, in order.</summary>
    public IReadOnlyList<ActionCell> Cells => _cells;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        hostWindow.ContentResized += OnHostWindowContentResized;
        if (_rootWindow is null)
        {
            _rootWindow = hostWindow;
            while (_rootWindow.ParentElement is Window parentWindow)
            {
                _rootWindow = parentWindow;
            }

            _rootWindow.Closed += _ => EndHover();
        }

        _showsCards = services.GridView.ShowsCards;
        RebuildCells();
        _versionWatcher.HasChanged(services.ActionStateView.GetActionSetVersion(entityId));
    }

    public void Update(GameTime gameTime)
    {
        if (_versionWatcher.HasChanged(services.ActionStateView.GetActionSetVersion(entityId)))
        {
            RebuildCells();
        }
        else if (services.GridView.ShowsCards != _showsCards)
        {
            _showsCards = !_showsCards;
            RebuildCells();
        }

        UpdateCellStates();
        UpdateHover();
    }

    public void DrawContent(GameTime gameTime)
    {
    }

    public void Deactivate()
    {
        if (_hostWindow is not null)
        {
            elementPoolService.CloseAllChildren(_hostWindow);
        }

        _cells.Clear();
        EndHover();
    }

    private void OnHostWindowContentResized(Element _) => RebuildCells();

    private void RebuildIfInitialized()
    {
        if (_hostWindow is not null)
        {
            RebuildCells();
        }
    }

    /// <summary>What each cell shows this frame: whether it can be used now, its cooldown, whether it is on, and what it takes.</summary>
    private void UpdateCellStates()
    {
        var now = services.SimulationClock.CurrentFrame;
        _keyLabels.Refresh(entityId);
        foreach (var cell in _cells)
        {
            cell.IsContextMenuTarget = IsContextMenuAbout(cell);
            cell.IsSelected = cell.Subject.ActionId is { } selectableActionId ? selectableActionId == services.MapViewState.SelectedActionId : cell.Subject.MenuCommandId == services.MapViewState.SelectedMenuCommandId;
            cell.KeyLabel = cell.Subject.ActionId is { } boundActionId ? _keyLabels.ForAction(boundActionId) : cell.Subject.MenuCommandId is { } boundMenuCommandId ? _keyLabels.ForMenuCommand(boundMenuCommandId) : null;

            if (cell.Subject.MenuCommandId is { } menuCommandId)
            {
                cell.IsBlocked = !services.MenuCommands.TryGet(menuCommandId, out var command) || !command.CanRun();
                cell.CompareState = services.MapViewState.CompareRequiredActionActivatorType is null ? CellCompareState.None : CellCompareState.Ineligible;
                continue;
            }

            if (cell.Subject.ActionId is not { } actionId || !services.ActionStateView.TryGetEffectiveAction(entityId, actionId, out var action))
            {
                continue;
            }

            cell.CompareState = services.MapViewState.CompareRequiredActionActivatorType is not { } requiredType ? CellCompareState.None
                : action.Activator.GetType() == requiredType ? CellCompareState.Eligible
                : CellCompareState.Ineligible;
            cell.IsBlocked = services.ActionStateView.GetActionBlocker(entityId, actionId) != ActivationBlocker.None;
            cell.IsToggledOn = services.ActionStateView.IsActionToggledOn(entityId, actionId);
            cell.FillPercentage = cell.IsBlocked ? 0f : ActionSlotFill.Of(services.ActionStateView, services.EntityActions, entityId, action, now);
            cell.Costs = services.ActionStateView.ActionCostsOf(entityId, actionId);
        }
    }

    private void RebuildCells()
    {
        if (_hostWindow is not { } hostWindow)
        {
            return;
        }

        elementPoolService.CloseAllChildren(hostWindow);
        _cells.Clear();

        CollectEntries();
        _reusableEntries.Sort((a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return _sortOrder == ActionSortOrder.NameDescending ? -byName : byName;
        });
        VisibleItemCount = _reusableEntries.Count;

        var cellSize = _showsCards ? CardCellSize : IconCellSize;
        var columns = System.Math.Max(1, (int)((hostWindow.ContentSize.X + CellGap) / (cellSize.X + CellGap)));
        for (var index = 0; index < _reusableEntries.Count; index++)
        {
            var entry = _reusableEntries[index];
            var position = new Vector2(index % columns * (cellSize.X + CellGap), index / columns * (cellSize.Y + CellGap));
            var cell = elementPoolService.CreateElement<ActionCell>(hostWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = position, Size = cellSize, DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = Color.Transparent },
            });
            cell.Configure(entry.Subject, entry.Name, entry.SpriteName, entry.Glyph, entry.GlyphColor, cellSize);
            cell.Clicked += _ => SelectCell(cell);
            cell.DoubleClicked += _ => Activate(cell);
            cell.OnRightClicked = position => services.ContextMenus.Open(new Vector2(position.X, position.Y), BuildContextMenu(cell), cell);
            hostWindow.AddChild(cell);
            _cells.Add(cell);
        }

        UpdateCellStates();
    }

    /// <summary>Reports the cell under the pointer to Action Details with how many updates it has been hovered, or that none is -- the count restarts when the cell changes.</summary>
    private void UpdateHover()
    {
        ActionCell? candidate = null;
        foreach (var cell in _cells)
        {
            if (cell.IsPointerOver)
            {
                candidate = cell;
                break;
            }
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

        if (candidate is null || _rootWindow is null)
        {
            services.ActionHoverDetails.EndHover(this);
            return;
        }

        services.ActionHoverDetails.Hover(this, new ActionHoverTarget(candidate.Subject, candidate.Name), _hoveredFrames, _rootWindow);
    }

    private void EndHover()
    {
        _hoveredCell = null;
        _hoveredFrames = 0;
        services.ActionHoverDetails.EndHover(this);
    }

    /// <summary>A click on cell selects it, as a click on an inventory cell does: it and every hotbar slot bound to it glow until another is selected or the Actions window closes.</summary>
    private void SelectCell(ActionCell cell)
    {
        services.MapViewState.SelectedActionId = cell.Subject.ActionId;
        services.MapViewState.SelectedMenuCommandId = cell.Subject.MenuCommandId;
    }

    /// <summary>Whether the open context menu is about what cell shows: opened for cell, or for anything else showing it (a hotbar slot bound to it).</summary>
    private bool IsContextMenuAbout(ActionCell cell) =>
        services.ContextMenus.IsOpenFor(cell) ||
        (cell.Subject.ActionId is { } actionId && ContextMenuActions.IsAboutAction(services.ContextMenus, actionId)) ||
        (cell.Subject.MenuCommandId is { } menuCommandId && ContextMenuActions.IsAboutMenuCommand(services.ContextMenus, menuCommandId));

    /// <summary>What a double-click does, and the menu's one option: an action is armed as the first press of a hotbar slot holding it would (ActionTargetingController.ArmActionFromWindow); a menu command runs.</summary>
    private void Activate(ActionCell cell)
    {
        if (cell.Subject.MenuCommandId is { } menuCommandId)
        {
            services.MenuCommands.TryRun(menuCommandId);
            return;
        }

        if (cell.Subject.ActionId is { } actionId)
        {
            services.ActionTargeting.ArmActionFromWindow(actionId);
        }
    }

    /// <summary>A cell's menu: its activation option, then for an action "Compare", as an item's menu offers it (ActionHoverDetails.Compare).</summary>
    private List<DropdownOption> BuildContextMenu(ActionCell cell)
    {
        List<DropdownOption> options = [BuildActivationOption(cell)];
        if (cell.Subject.ActionId is { } actionId)
        {
            options.Add(new DropdownOption("Compare", null, Enabled: true, () => services.ActionHoverDetails.Compare(actionId)));
        }

        return options;
    }

    /// <summary>The cell's menu option: labelled as a hotbar slot's press option is (ActivationMenuLabels, or the command's own label), and disabled while the action is blocked or the command can't run -- the same answer the double-click's path gives.</summary>
    private DropdownOption BuildActivationOption(ActionCell cell)
    {
        if (cell.Subject.MenuCommandId is { } menuCommandId && services.MenuCommands.TryGet(menuCommandId, out var command))
        {
            return new DropdownOption(command.MenuOptionLabel, null, command.CanRun(), () => Activate(cell));
        }

        var actionStateView = services.ActionStateView;
        var actionId = cell.Subject.ActionId!.Value;
        actionStateView.TryGetEffectiveAction(entityId, actionId, out var action);
        var label = ActivationMenuLabels.For(action.Toggle is not null, actionStateView.IsActionToggledOn(entityId, actionId), isArmedHere: false, services.MapViewState.TargetingMode, action.Tags);
        return new DropdownOption(label, null, actionStateView.GetActionBlocker(entityId, actionId) == ActivationBlocker.None, () => Activate(cell));
    }

    /// <summary>Fills _reusableEntries with what this tab holds and the name filter matches.</summary>
    private void CollectEntries()
    {
        _reusableEntries.Clear();

        if (tab.Kind == ActionsTabKind.Menu)
        {
            foreach (var command in services.MenuCommands.Commands)
            {
                if (MatchesNameFilter(command.Name))
                {
                    _reusableEntries.Add((command.Name, ActionCellSubject.MenuCommand(command.Id), command.SpriteName, command.Glyph, command.GlyphColor));
                }
            }

            return;
        }

        services.ActionStateView.CopyActionIds(entityId, _reusableActionIds);
        foreach (var actionId in _reusableActionIds)
        {
            if (!services.ActionStateView.TryGetEffectiveAction(entityId, actionId, out var action) || !Holds(action) || !MatchesNameFilter(action.Name))
            {
                continue;
            }

            _reusableEntries.Add((action.Name, ActionCellSubject.Action(actionId), action.SpriteName, action.Glyph, action.GlyphColor));
        }
    }

    /// <summary>Whether this tab lists action: Core only Core actions, every other tab only the rest, and a category tab only those with a tag at or below its category.</summary>
    private bool Holds(ActionDefinition action)
    {
        var isCore = services.ActionStateView.IsCoreAction(action.Id);
        return tab.Kind switch
        {
            ActionsTabKind.Core => isCore,
            ActionsTabKind.All => !isCore,
            ActionsTabKind.Category => !isCore && HasCategory(action.Tags, tab.Category),
            _ => false,
        };
    }

    private static bool HasCategory(GameplayTagSet tags, GameplayTag category)
    {
        foreach (var tag in tags)
        {
            if (tag.IsSelfOrDescendantOf(category))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesNameFilter(string name) => _nameFilter.Length == 0 || name.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase);
}
