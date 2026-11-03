using Engine.ECS.Components;

namespace Game.Modules.Shops.Components;

/// <summary>Matches the shop's preference for one item type.</summary>
internal readonly struct ShopStockPreferenceForItemMatcher(Guid itemDefinitionId) : IComponentMatcher<ShopStockPreferenceComponent>
{
    public bool Matches(in ShopStockPreferenceComponent preference) => preference.ItemDefinitionId == itemDefinitionId;
}
