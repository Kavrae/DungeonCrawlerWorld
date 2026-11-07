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
using UIEngine.UI;

namespace Presentation.UI.Inventory;

/// <summary>Builds the item windows -- inventory, loot, shop, trade, Item Details, comparison, loot box results -- and holds the rules between them.</summary>
/// <remarks>
/// The rules:
/// - A loot window and a shop window are never open together: both cascade off the player's inventory window, so two
///   at once would overlap. Opening either closes the other first.
/// - The trade window opens and closes with the shop.
/// - Hovering an item opens it in Item Details at the cursor (ItemHoverDetails); clicking one selects it until the
///   player's Inventory window closes. Compare pins
///   Item Details beside the window holding the item, and until it ends a hover opens the hovered item's comparison
///   instead (ItemComparisonController).
/// - Activate arms the item's action; opening loot boxes shows their results, where a reward opens in Item Details
///   on the stack it landed in while that still exists, otherwise read-only from its definition.
/// - A destroyed entity's id is reused straight away, so every window open for it closes (see ReleaseEntity).
/// - Every item window joins one ElementGroup while open; a press on any of them leaves Item Details open.
/// </remarks>
public sealed class ItemWindowCoordinator
{
    private readonly MapViewState _mapViewState;
    private readonly SecondaryInventoryWindowController _secondaryInventory;
    private readonly ShopWindowController _shop;

    /// <summary>The hovered item's comparison while comparing -- a second Item Details popup (see ItemComparisonController).</summary>
    private readonly ItemDetailsWindowController _comparisonPopup;

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
        var itemWindows = new ElementGroup();

        Inventory = new InventoryWindowController(elementPoolService, world, inventoryView, mapWindow, contextMenuController, itemWindows);
        Inventory.Initialize(layers);

        _secondaryInventory = new SecondaryInventoryWindowController(elementPoolService, lootCommands, Inventory, contextMenuController, mapWindow, itemWindows);
        _secondaryInventory.Initialize(layers);

        _shop = new ShopWindowController(elementPoolService, _mapViewState, Inventory, contextMenuController, mapWindow, itemWindows);
        _shop.Initialize(layers);

        var trade = new TradeWindowController(elementPoolService, Inventory, _shop, mapWindow, reservedEntityIds.TradeOfferPlayerEntityId, reservedEntityIds.TradeOfferShopEntityId, itemWindows);
        trade.Initialize(layers);
        _shop.OnOpened = trade.Open;
        _shop.OnClosed = trade.CloseForShopClosed;

        mapWindow.OnCorpseClicked = OpenLoot;
        mapWindow.OnShopClicked = OpenShop;

        // At most one of the two is ever open, so whichever isn't answers null.
        Inventory.GetSecondaryTargetEntityId = () => _secondaryInventory.OpenTargetEntityId ?? _shop.OpenTargetEntityId;

        ItemDetails = new ItemDetailsWindowController(elementPoolService, inventoryView, itemCatalog, Inventory, mapWindow, shellServices.PointerState, itemWindows);
        ItemDetails.Initialize(layers);
        _comparisonPopup = new ItemDetailsWindowController(elementPoolService, inventoryView, itemCatalog, Inventory, mapWindow, shellServices.PointerState, itemWindows, closesOnPressOutside: false, closesWhenPointerLeaves: false);
        _comparisonPopup.Initialize(layers);

        ItemComparison = new ItemComparisonController(_mapViewState, ItemDetails, _comparisonPopup, shellServices.CursorTextContent, entityId => WindowHoldingItemsOf(entityId, trade));
        shellServices.ItemHoverDetails.Popup = ItemComparison;
        ItemDetails.OnClosed = ItemComparison.ClearComparison;
        ItemDetails.OnStopComparisonRequested = ItemComparison.ClearComparison;
        ItemDetails.OnCompareRequested = (entityId, stackInstanceId) => ItemComparison.Arm(entityId, stackInstanceId);
        _comparisonPopup.OnClosed = ItemComparison.HandleComparisonPopupClosed;
        _comparisonPopup.FixedPosition = () => new Vector2(ItemDetails.Rectangle.Left, ItemDetails.Rectangle.Bottom + WindowCascadePlacement.Gap);

        Inventory.OnItemSelected = OnItemClicked;
        _secondaryInventory.OnItemSelected = OnItemClicked;
        _shop.OnItemSelected = OnItemClicked;
        trade.OnItemSelected = OnItemClicked;
        Inventory.OnClosed = () => _mapViewState.SelectedItemStackInstanceId = null;

        Inventory.OnActivateRequested = (_, stackInstanceId) => actionTargetingController.ArmItemFromStack(stackInstanceId);

        var lootboxResults = new LootboxResultsWindowController(elementPoolService, lootboxCatalog, itemCatalog, tooltipController, contextMenuController, Inventory, mapWindow, world, itemWindows);
        lootboxResults.Initialize(layers);
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

        if (_comparisonPopup.CurrentEntityId == entityId)
        {
            _comparisonPopup.Close();
        }

        if (ItemDetails.CurrentEntityId == entityId)
        {
            ItemDetails.Close();
        }
    }

    private void OnItemClicked(int entityId, uint stackInstanceId) => _mapViewState.SelectedItemStackInstanceId = stackInstanceId;

    /// <summary>The open window showing entityId's items: the shop's, the loot window's, the trade window for a trade offer, else the player's Inventory window.</summary>
    private Window? WindowHoldingItemsOf(int entityId, TradeWindowController trade)
    {
        if (_shop.OpenTargetEntityId == entityId)
        {
            return _shop.Window;
        }

        if (_secondaryInventory.OpenTargetEntityId == entityId)
        {
            return _secondaryInventory.Window;
        }

        if (_mapViewState.ReservedEntityIds is { } reserved && (entityId == reserved.TradeOfferPlayerEntityId || entityId == reserved.TradeOfferShopEntityId))
        {
            return trade.Window;
        }

        return Inventory.PlayerInventoryWindow;
    }
}
