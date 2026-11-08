using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Containers.Components;
using Game.Modules.Death.Components;
using Game.Modules.ItemPiles.Components;
using Game.Modules.Shops.Components;

namespace Game.Modules.ItemPiles;

/// <summary>What among a tile's occupants takes something put down there.</summary>
public enum DropReceiverKind : byte
{
    /// <summary>Nothing there takes it: it goes onto the ground.</summary>
    Ground,

    /// <summary>A corpse, a container or an item pile takes it.</summary>
    Container,

    /// <summary>A standing shop is there, which takes nothing put down: items reach it only through its window.</summary>
    Shop,
}

/// <summary>The one rule for which of a tile's occupants a drop goes into: a standing shop anywhere refuses, else a corpse, else a container, else an item pile.</summary>
/// <remarks>Shared by ItemDrops, which drops into it, and MapViewQuery.FindDropReceiver, which Presentation lights and labels, so what is shown is what is dropped into.</remarks>
public sealed class DropReceivers(ComponentManager componentManager)
{
    private readonly PackedComponentPool<ItemPileComponent> _piles = componentManager.GetPackedPool<ItemPileComponent>();
    private readonly PackedComponentPool<ContainerComponent> _containers = componentManager.GetPackedPool<ContainerComponent>();
    private readonly PackedComponentPool<ShopComponent> _shops = componentManager.GetPackedPool<ShopComponent>();
    private readonly PackedComponentPool<DeadComponent> _deadEntities = componentManager.GetPackedPool<DeadComponent>();

    /// <summary>What among occupants, other than dropperEntityId, a drop goes into; receiverEntityId is the shop or container, -1 for the ground.</summary>
    public DropReceiverKind Find(int dropperEntityId, ReadOnlySpan<int> occupants, out int receiverEntityId)
    {
        var corpseEntityId = -1;
        var containerEntityId = -1;
        var pileEntityId = -1;
        foreach (var occupantEntityId in occupants)
        {
            if (occupantEntityId == dropperEntityId)
            {
                continue;
            }

            if (_deadEntities.Has(occupantEntityId))
            {
                if (corpseEntityId == -1)
                {
                    corpseEntityId = occupantEntityId;
                }

                continue;
            }

            if (_shops.Has(occupantEntityId))
            {
                receiverEntityId = occupantEntityId;
                return DropReceiverKind.Shop;
            }

            if (_piles.Has(occupantEntityId))
            {
                if (pileEntityId == -1)
                {
                    pileEntityId = occupantEntityId;
                }
            }
            else if (_containers.Has(occupantEntityId) && containerEntityId == -1)
            {
                containerEntityId = occupantEntityId;
            }
        }

        receiverEntityId = corpseEntityId != -1 ? corpseEntityId : containerEntityId != -1 ? containerEntityId : pileEntityId;
        return receiverEntityId == -1 ? DropReceiverKind.Ground : DropReceiverKind.Container;
    }
}
