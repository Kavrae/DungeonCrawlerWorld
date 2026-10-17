using Engine.Tags;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.Views;

namespace Presentation.UI.Inventory;

/// <summary>What every item window and its contents reads and does -- grids, currency rows, and the windows that host them.</summary>
/// <remarks>One set, because the inventory, loot, shop and trade windows all host the same grid and currency row, which between them read inventories, shops and currency and move all three.</remarks>
public sealed record InventoryServices(
    ItemCatalog ItemCatalog,
    InventoryView InventoryView,
    ShopView ShopView,
    CurrencyView CurrencyView,
    ActionStateView ActionStateView,
    InventoryCommands InventoryCommands,
    ShopCommands ShopCommands,
    CurrencyCommands CurrencyCommands,
    GameplayTagRegistry GameplayTags);
