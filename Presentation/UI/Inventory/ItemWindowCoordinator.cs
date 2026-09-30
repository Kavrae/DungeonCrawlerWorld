using Game.Floors;
using Game.Modules.Death;
using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Lootboxes;
using Presentation.UI.Looting;
using Presentation.UI.Shops;
using Presentation.UI.Trade;

namespace Presentation.UI.Inventory;

/// <summary>Builds the item windows -- inventory, loot, shop, trade, Item Details, comparison, loot box results -- and holds the rules between them.</summary>
/// <remarks>
/// The rules:
/// - A loot window and a shop window are never open together: both cascade off the player's inventory window, so two
///   at once would overlap. Opening either closes the other first.
/// - The trade window opens and closes with the shop.
/// - Clicking an item compares it while comparison is armed, and otherwise opens it in Item Details.
/// - Activate arms the item's action; opening loot boxes shows their results, where a reward opens in Item Details
///   on the stack it landed in while that still exists, otherwise read-only from its definition.
/// - A destroyed entity's id is reused straight away, so every window open for it closes (see ReleaseEntity).
/// </remarks>
public sealed class ItemWindowCoordinator
{
    private readonly MapViewState _mapViewState;
    private readonly SecondaryInventoryWindowController _secondaryInventory;
    private readonly ShopWindowController _shop;

    public ItemWindowCoordinator(
        ElementPoolService elementPoolService,
        ShellServices shellServices,
        World world,
        MapWindow mapWindow,
        ActionTargetingController actionTargetingController,
        InventoryView inventoryView,
        ItemCatalog itemCatalog,
        LootboxCatalog lootboxCatalog,
        LootCommands lootCommands,
        LootboxCommands lootboxCommands,
        ReservedEntityIds reservedEntityIds)
    {
        var layers = shellServices.Layers;
        var contextMenuController = shellServices.ContextMenuController;
        var tooltipController = shellServices.TooltipController;
        _mapViewState = shellServices.MapViewState;

        Inventory = new InventoryWindowController(elementPoolService, world, inventoryView, mapWindow, contextMenuController, tooltipController);
        Inventory.Initialize(layers);

        _secondaryInventory = new SecondaryInventoryWindowController(elementPoolService, lootCommands, Inventory, contextMenuController, mapWindow, tooltipController);
        _secondaryInventory.Initialize(layers);

        _shop = new ShopWindowController(elementPoolService, _mapViewState, Inventory, contextMenuController, mapWindow, tooltipController);
        _shop.Initialize(layers);

        var trade = new TradeWindowController(elementPoolService, Inventory, _shop, mapWindow, reservedEntityIds.TradeOfferPlayerEntityId, reservedEntityIds.TradeOfferShopEntityId, tooltipController);
        trade.Initialize(layers);
        _shop.OnOpened = trade.Open;
        _shop.OnClosed = trade.CloseForShopClosed;

        mapWindow.OnCorpseClicked = OpenLoot;
        mapWindow.OnShopClicked = OpenShop;

        // At most one of the two is ever open, so whichever isn't answers null.
        Inventory.GetSecondaryTargetEntityId = () => _secondaryInventory.OpenTargetEntityId ?? _shop.OpenTargetEntityId;

        ItemDetails = new ItemDetailsWindowController(elementPoolService, inventoryView, itemCatalog, Inventory, contextMenuController, _mapViewState, mapWindow);
        ItemDetails.Initialize(layers);
        ItemDetails.GetSecondaryInventoryWindowRectangle = () => _secondaryInventory.Rectangle != Rectangle.Empty ? _secondaryInventory.Rectangle : _shop.Rectangle;

        // Separate from the fallback above: the trade window opens beside the shop window, not instead of it, so both
        // count as "inside" at once. See GetTradeWindowRectangle's own doc comment.
        ItemDetails.GetTradeWindowRectangle = () => trade.Rectangle;

        // Built after Item Details, whose pane is always the comparison's anchor (see ItemComparisonController.Arm).
        ItemComparison = new ItemComparisonController(elementPoolService, inventoryView, itemCatalog, Inventory, contextMenuController, mapWindow, _mapViewState, ItemDetails, shellServices.CursorTextContent);
        ItemComparison.Initialize(layers);
        ItemDetails.GetComparisonColumnRectangles = () => ItemComparison.ColumnRectangles;
        ItemDetails.OnClosed = ItemComparison.ClearComparison;
        ItemDetails.OnCompareRequested = ItemComparison.Arm;
        Inventory.OnCompareRequested = ItemComparison.Arm;
        _secondaryInventory.OnCompareRequested = ItemComparison.Arm;
        _shop.OnCompareRequested = ItemComparison.Arm;

        Inventory.OnItemSelected = OnItemClicked;
        _secondaryInventory.OnItemSelected = OnItemClicked;
        _shop.OnItemSelected = OnItemClicked;
        trade.OnItemSelected = OnItemClicked;

        Inventory.OnActivateRequested = (_, stackInstanceId) => actionTargetingController.ArmItemFromStack(stackInstanceId);

        var lootboxResults = new LootboxResultsWindowController(elementPoolService, lootboxCatalog, itemCatalog, tooltipController, contextMenuController, Inventory, mapWindow, world);
        lootboxResults.Initialize(layers);
        ItemDetails.GetLootboxResultsWindowRectangle = () => lootboxResults.Rectangle;
        Inventory.OnOpenLootboxesRequested = entityId => lootboxResults.Show(lootboxCommands.OpenAll(entityId));
        lootboxResults.OnRewardClicked = reward =>
        {
            if (lootboxResults.Window is not { } resultsWindow)
            {
                return;
            }

            var playerEntityId = world.PlayerEntityId;
            ItemComparison.ClearIfAnchorChanging(playerEntityId, reward.StackInstanceId);
            if (inventoryView.TryGetStack(playerEntityId, reward.StackInstanceId, out _))
            {
                ItemDetails.Open(playerEntityId, reward.StackInstanceId, resultsWindow);
            }
            else if (itemCatalog.TryGet(reward.ItemDefinitionId, out var rewardDefinition))
            {
                ItemDetails.OpenDefinition(playerEntityId, rewardDefinition, resultsWindow);
            }
        };
    }

    public InventoryWindowController Inventory { get; }

    public ItemDetailsWindowController ItemDetails { get; }

    public ItemComparisonController ItemComparison { get; }

    /// <summary>Opens entityId's loot window, closing the shop window first.</summary>
    public void OpenLoot(int entityId)
    {
        _shop.CloseIfOpen();
        _secondaryInventory.OpenLoot(entityId);
    }

    /// <summary>Opens entityId's shop window, closing the loot window first.</summary>
    public void OpenShop(int entityId)
    {
        _secondaryInventory.CloseIfOpen();
        _shop.OpenShop(entityId);
    }

    /// <summary>Lets go of entityId before its id is reused: closes whichever item window is open for it.</summary>
    public void ReleaseEntity(int entityId)
    {
        if (_mapViewState.OpenShopEntityId == entityId)
        {
            _mapViewState.OpenShopEntityId = null;
        }

        if (_secondaryInventory.OpenTargetEntityId == entityId)
        {
            _secondaryInventory.CloseIfOpen();
        }

        if (_shop.OpenTargetEntityId == entityId)
        {
            _shop.CloseIfOpen();
        }

        if (ItemDetails.CurrentEntityId == entityId)
        {
            ItemDetails.Close();
        }
    }

    private void OnItemClicked(int entityId, uint stackInstanceId)
    {
        if (ItemComparison.IsArmed)
        {
            ItemComparison.AddOrToggle(entityId, stackInstanceId);
            return;
        }

        ItemComparison.ClearIfAnchorChanging(entityId, stackInstanceId);
        ItemDetails.Open(entityId, stackInstanceId);
    }
}
