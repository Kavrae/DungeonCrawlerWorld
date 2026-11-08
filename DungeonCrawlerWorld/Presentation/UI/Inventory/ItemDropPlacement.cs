using Engine.Math;
using Game.Modules.ItemPiles;

namespace Presentation.UI.Inventory;

/// <summary>Puts the player's goods down through Game's ItemDropCommands, at once and with no action lock, and nothing while a shop is open: a trade decides where items go until it closes.</summary>
public sealed class ItemDropPlacement(ItemDropCommands itemDropCommands, MapViewState mapViewState) : IItemDropPlacement
{
    public bool CanDropAt(ItemDrop drop, Vector3Int tile) =>
        mapViewState.OpenShopEntityId is null && itemDropCommands.GetRefusal(drop, tile) == ItemDropRefusal.None;

    public bool TryDropAt(ItemDrop drop, Vector3Int tile) =>
        mapViewState.OpenShopEntityId is null && itemDropCommands.TryDrop(drop, tile) == ItemDropRefusal.None;
}
