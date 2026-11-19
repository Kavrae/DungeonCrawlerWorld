using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Death.Components;
using Game.Modules.Shops.Components;

namespace Game.Modules.Shops;

/// <summary>The one rule for whether an entity trades as a shop: it carries a ShopComponent and isn't dead.</summary>
/// <remarks>A destroyed shop trades no more: it is a container like any other, opened as loot, dropped into, given to and taken from as one.</remarks>
public sealed class TradingShops(ComponentManager componentManager)
{
    private readonly PackedComponentPool<ShopComponent> _shops = componentManager.GetPackedPool<ShopComponent>();
    private readonly PackedComponentPool<DeadComponent> _deadEntities = componentManager.GetPackedPool<DeadComponent>();

    /// <summary>Whether entityId trades as a shop now.</summary>
    public bool IsTrading(int entityId) => IsTrading(_shops, _deadEntities, entityId);

    /// <summary>entityId's shop, when it trades as one now.</summary>
    public bool TryGetTrading(int entityId, out ShopComponent shop) => TryGetTrading(_shops, _deadEntities, entityId, out shop);

    /// <inheritdoc cref="IsTrading(int)"/>
    public static bool IsTrading(ComponentManager componentManager, int entityId) =>
        IsTrading(componentManager.GetPackedPool<ShopComponent>(), componentManager.GetPackedPool<DeadComponent>(), entityId);

    /// <inheritdoc cref="TryGetTrading(int, out ShopComponent)"/>
    public static bool TryGetTrading(ComponentManager componentManager, int entityId, out ShopComponent shop) =>
        TryGetTrading(componentManager.GetPackedPool<ShopComponent>(), componentManager.GetPackedPool<DeadComponent>(), entityId, out shop);

    private static bool IsTrading(PackedComponentPool<ShopComponent> shops, PackedComponentPool<DeadComponent> deadEntities, int entityId) =>
        shops.Has(entityId) && !deadEntities.Has(entityId);

    private static bool TryGetTrading(PackedComponentPool<ShopComponent> shops, PackedComponentPool<DeadComponent> deadEntities, int entityId, out ShopComponent shop)
    {
        if (deadEntities.Has(entityId))
        {
            shop = default;
            return false;
        }

        return shops.TryGetReadonly(entityId, out shop);
    }
}
