using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.UI.Chrome;
using Presentation.UI.Content;

namespace Presentation.UI.Lootboxes;

/// <summary>What one opening granted: a header per type and rarity of box opened, with that group's combined rewards as item cells beneath it.</summary>
/// <remarks>
/// The cells are the inventory grid's own InventoryItemStackCell, showing how many of each item the
/// group granted rather than the size of the stack it landed in. They only stand for items: they
/// never start a drag (IsDragSource) and offer no context menu. Hovering one shows the inventory's
/// basic tooltip after the usual delay; clicking one hands its GrantedItem to onRewardClicked.
/// Sections re-flow whenever the host's content width changes, guarded against the rebuild that a
/// scrollbar appearing mid-rebuild would otherwise start inside the one still adding cells -- the
/// same shape as InventoryGridContent.RebuildCells.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class LootboxResultsContent(
    IReadOnlyList<OpenedLootboxGroup> openedGroups,
    int playerEntityId,
    LootboxCatalog lootboxCatalog,
    ItemCatalog itemCatalog,
    TooltipController tooltipController,
    Action<GrantedItem> onRewardClicked) : IElementContent
{
    private static readonly Vector2 PopupGap = new(1, 1);

    private readonly List<(InventoryItemStackCell Cell, GrantedItem Reward)> _rewardCells = [];
    private Window _hostWindow = null!;
    private InventoryItemStackCell? _hoveredCell;
    private int _hoveredFrames;
    private bool _isRebuilding;
    private bool _isRebuildRequestedDuringRebuild;

    /// <summary>The header text of every section, in order -- "Bronze Adventurer Box", or "... x3" for three boxes of one kind.</summary>
    public IReadOnlyList<string> SectionHeaders => [.. openedGroups.Select(HeaderText)];

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        hostWindow.ContentResized += _ => Rebuild();
        hostWindow.Closed += _ => tooltipController.Hide(this);
        Rebuild();
    }

    public void Update(GameTime gameTime) => UpdateHover(Mouse.GetState());

    public void DrawContent(GameTime gameTime)
    {
    }

    public void Deactivate()
    {
        _hostWindow.ElementPoolService.CloseAllChildren(_hostWindow);
        _rewardCells.Clear();
        _hoveredCell = null;
        _hoveredFrames = 0;
        tooltipController.Hide(this);
    }

    private string HeaderText(OpenedLootboxGroup group)
    {
        var name = lootboxCatalog.DisplayName(group.Kind);
        return group.Count > 1 ? $"{name} x{group.Count}" : name;
    }

    private void Rebuild()
    {
        if (_isRebuilding)
        {
            _isRebuildRequestedDuringRebuild = true;
            return;
        }

        _isRebuilding = true;
        try
        {
            do
            {
                _isRebuildRequestedDuringRebuild = false;
                using (_hostWindow.BeginLayoutBatch())
                {
                    BuildSections();
                }
            }
            while (_isRebuildRequestedDuringRebuild);
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private void BuildSections()
    {
        var elementPoolService = _hostWindow.ElementPoolService;
        elementPoolService.CloseAllChildren(_hostWindow);
        _rewardCells.Clear();
        _hoveredCell = null;
        _hoveredFrames = 0;

        var cellSize = InventoryGridContent.CellSize;
        var cellStep = cellSize + new Vector2(InventoryGridContent.CellGap);
        var columns = System.Math.Max(1, (int)((_hostWindow.ContentSize.X + InventoryGridContent.CellGap) / cellStep.X));
        var y = 0f;

        foreach (var group in openedGroups)
        {
            var header = elementPoolService.CreateElement<TextWindow>(_hostWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = new Vector2(columns * cellStep.X, LootboxChrome.HeaderHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = Color.Transparent },
                Text = new TextOptions { Text = HeaderText(group), TextColor = LootboxChrome.HeaderColor(group.Kind.Rarity) },
            });
            _hostWindow.AddChild(header);
            y += LootboxChrome.HeaderHeight;

            for (var index = 0; index < group.Items.Count; index++)
            {
                var reward = group.Items[index];
                if (!itemCatalog.TryGet(reward.ItemDefinitionId, out var definition))
                {
                    continue;
                }

                var position = new Vector2(index % columns * cellStep.X, y + index / columns * cellStep.Y);
                var cell = elementPoolService.CreateElement<InventoryItemStackCell>(_hostWindow, new ElementOptions
                {
                    Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                    Layout = new ElementLayoutOptions { RelativePosition = position, Size = cellSize, DisplayMode = ElementDisplayMode.Fixed },
                    Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
                    Content = new ElementContentOptions { ContentColor = Color.Transparent },
                });
                cell.Configure(playerEntityId, definition.Id, reward.StackInstanceId, definition.SpriteName, definition.Glyph, definition.GlyphColor, definition.SpriteTint, reward.Quantity,
                    isDisabled: false, isDivergent: false, mergedStackBadgeVisible: false, canTrade: definition.CanTrade, itemCanBindToHotbar: false, cellSize);
                cell.IsDragSource = false;
                cell.Clicked += _ => onRewardClicked(reward);
                _hostWindow.AddChild(cell);
                _rewardCells.Add((cell, reward));
            }

            var rows = (group.Items.Count + columns - 1) / columns;
            y += rows * cellStep.Y + LootboxChrome.SectionGap;
        }
    }

    /// <summary>Same delay-to-show, hide-at-once rule as InventoryGridContent's own hover.</summary>
    private void UpdateHover(MouseState mouseState)
    {
        var mousePosition = new Point(mouseState.X, mouseState.Y);
        InventoryItemStackCell? candidate = null;
        GrantedItem candidateReward = default;
        foreach (var (cell, reward) in _rewardCells)
        {
            var isHovered = candidate is null && cell.Rectangle.Contains(mousePosition) && _hostWindow.Rectangle.Contains(mousePosition);
            cell.IsHovered = isHovered;
            if (isHovered)
            {
                candidate = cell;
                candidateReward = reward;
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

        if (candidate is null || _hoveredFrames < HudChrome.HoverTooltipDelayFrames || !itemCatalog.TryGet(candidateReward.ItemDefinitionId, out var definition))
        {
            tooltipController.Hide(this);
            return;
        }

        tooltipController.Show(this, candidate.Rectangle, PopupAnchor.East, PopupGap, PopupChrome.HoverPopupMaximumSize, ItemHoverSummary.For(definition, showCharges: false), definition.Name);
    }
}
