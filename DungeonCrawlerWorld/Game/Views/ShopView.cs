using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;

namespace Game.Views;

/// <summary>Which entities are shops and what their stock and margins are, for pricing a trade before it's made.</summary>
/// <remarks>The pricing arithmetic itself (ShopStockPricing's bulk and band prices) reads no pool, so callers use it directly with what this returns.</remarks>
public sealed class ShopView(ComponentManager componentManager)
{
    private readonly PackedComponentPool<ShopComponent> _shops = componentManager.GetPackedPool<ShopComponent>();

    public bool IsShop(int entityId) => _shops.Has(entityId);

    public bool TryGetShop(int entityId, out ShopComponent shop) => _shops.TryGetReadonly(entityId, out shop);

    /// <inheritdoc cref="ShopMarginPricing.ResolveEffectiveShop"/>
    public ShopComponent ResolveEffectiveShop(ShopComponent shop, int buyerOrSellerEntityId) =>
        ShopMarginPricing.ResolveEffectiveShop(componentManager, shop, buyerOrSellerEntityId);

    /// <inheritdoc cref="ShopStockPricing.GetTotalStock"/>
    public int GetTotalStock(int shopEntityId, Guid itemDefinitionId) =>
        ShopStockPricing.GetTotalStock(componentManager, shopEntityId, itemDefinitionId);

    /// <inheritdoc cref="ShopStockPricing.GetPreferredStockLevel"/>
    public byte GetPreferredStockLevel(int shopEntityId, Guid itemDefinitionId) =>
        ShopStockPricing.GetPreferredStockLevel(componentManager, shopEntityId, itemDefinitionId);
}
