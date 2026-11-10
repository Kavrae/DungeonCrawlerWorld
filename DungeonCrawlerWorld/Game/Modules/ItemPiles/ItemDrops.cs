using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Tiles;
using Game.Blueprints;
using Game.Blueprints.Objects;
using Game.Modules.Actions;
using Game.Modules.Containers;
using Game.Modules.Core.Components;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.ItemPiles.Components;
using Game.Spawning;
using Game.World;

namespace Game.Modules.ItemPiles;

/// <summary>Puts items and currency down on a tile: into the container there, or onto the ground in the item pile there or a new one.</summary>
/// <remarks>
/// The one rule for every drop, whether it is dragged or chosen from a menu, so the two gestures always do the same thing.
/// A tile holding a corpse or a container takes the drop into it (a corpse first, then a container, then an item pile),
/// whichever of its footprint tiles was aimed at, as long as the dropper can reach its items (ContainerAccess: footprints
/// touching, and a corpse's loot not reserved for someone else). A standing shop on the tile refuses: items reach a shop only through its
/// window. Any other tile takes it onto the ground -- the dropper's own footprint or a tile next to it (Targeting), never a
/// wall -- in a new pile. Units move through InventoryActions, keeping everything that is the unit's (its Override, its lit
/// state), so ToggleItemHolderSync follows. A drop takes no action lock and needs none clear (ItemDropCommands).
/// </remarks>
public sealed class ItemDrops(
    ComponentManager componentManager,
    EntityManager entityManager,
    ItemCatalog itemCatalog,
    IPlayerQuery playerQuery,
    IMapQuery mapQuery,
    TargetResolution targetResolution,
    EntityFactory entityFactory,
    BlueprintRegistry blueprints,
    SimulationClock simulationClock)
{
    /// <summary>Where a drop onto the ground can land: one tile, the dropper's own or one touching its footprint, diagonals included.</summary>
    public static TargetingSpec Targeting { get; } = new(TargetShape.SingleTarget, MaximumRange: 1, Modes: TargetingModes.GroundOnly);

    private readonly PackedComponentPool<ItemPileComponent> _piles = componentManager.GetPackedPool<ItemPileComponent>();
    private readonly ItemDropRefusals _refusals = new(componentManager, itemCatalog);
    private readonly DropReceivers _receivers = new(componentManager);
    private readonly ContainerAccess _containerAccess = new(componentManager, componentManager.Keys, simulationClock);

    /// <summary>The item pile on tile, on its layer.</summary>
    public bool TryFindPileAt(Vector3Int tile, out int pileEntityId)
    {
        foreach (var occupantEntityId in mapQuery.GetOccupantEntityIdSpanAt(tile))
        {
            if (_piles.Has(occupantEntityId))
            {
                pileEntityId = occupantEntityId;
                return true;
            }
        }

        pileEntityId = -1;
        return false;
    }

    /// <summary>Why dropperEntityId can't drop drop anywhere now, or None.</summary>
    public ItemDropRefusal GetRefusal(int dropperEntityId, in ItemDrop drop) => _refusals.GetRefusal(dropperEntityId, in drop);

    /// <summary>Why dropperEntityId can't drop drop on tile now, or None.</summary>
    public ItemDropRefusal GetRefusal(int dropperEntityId, in ItemDrop drop, Vector3Int tile)
    {
        var refusal = GetRefusal(dropperEntityId, in drop);
        if (refusal != ItemDropRefusal.None)
        {
            return refusal;
        }

        return FindReceiver(dropperEntityId, tile, out var containerEntityId) switch
        {
            DropReceiverKind.Shop => ItemDropRefusal.IntoShop,
            DropReceiverKind.Container => ContainerRefusal(dropperEntityId, containerEntityId),
            _ => targetResolution.IsAimable(dropperEntityId, Targeting, tile) ? ItemDropRefusal.None : ItemDropRefusal.OutOfReach,
        };
    }

    /// <summary>Drops drop from dropperEntityId onto tile: into the container there, or onto the ground in a new pile.</summary>
    /// <returns>None when it was dropped; otherwise why not, with nothing changed.</returns>
    public ItemDropRefusal TryDrop(int dropperEntityId, in ItemDrop drop, Vector3Int tile)
    {
        var refusal = GetRefusal(dropperEntityId, in drop, tile);
        if (refusal != ItemDropRefusal.None)
        {
            return refusal;
        }

        if (FindReceiver(dropperEntityId, tile, out var containerEntityId) == DropReceiverKind.Container)
        {
            return Move(dropperEntityId, containerEntityId, in drop) ? ItemDropRefusal.None : ItemDropRefusal.NoRoom;
        }

        if (!TrySpawnPileAt(tile, out var pileEntityId))
        {
            return ItemDropRefusal.OutOfReach;
        }

        if (Move(dropperEntityId, pileEntityId, in drop))
        {
            return ItemDropRefusal.None;
        }

        entityManager.DestroyEntity(pileEntityId);
        return ItemDropRefusal.NothingToDrop;
    }

    /// <summary>Spawns an empty item pile on tile; false off the map.</summary>
    /// <remarks>An empty pile lasts until something leaves it (ItemPileEndings), so the caller fills it at once.</remarks>
    public bool TrySpawnPileAt(Vector3Int tile, out int pileEntityId)
    {
        pileEntityId = entityFactory.Spawn(new SpawnRequest(blueprints.GetId(ItemPile.Id), tile.X, tile.Y) { Layer = (MapLayer)tile.Z });
        return pileEntityId != EntityFactory.NoEntity;
    }

    /// <summary>What on tile a drop from dropperEntityId goes into (DropReceivers).</summary>
    private DropReceiverKind FindReceiver(int dropperEntityId, Vector3Int tile, out int containerEntityId) =>
        _receivers.Find(dropperEntityId, mapQuery.GetOccupantEntityIdSpanAt(tile), out containerEntityId);

    /// <summary>Why containerEntityId can't take a drop from dropperEntityId: the dropper can't reach its items (ContainerAccess).</summary>
    private ItemDropRefusal ContainerRefusal(int dropperEntityId, int containerEntityId) => _containerAccess.GetRefusal(dropperEntityId, containerEntityId) switch
    {
        ContainerAccessRefusal.OutOfReach => ItemDropRefusal.OutOfReach,
        ContainerAccessRefusal.LootReserved => ItemDropRefusal.LootReserved,
        _ => ItemDropRefusal.None,
    };

    private bool Move(int dropperEntityId, int receiverEntityId, in ItemDrop drop) => drop.Kind switch
    {
        ItemDropKind.OneUnit => InventoryActions.TryGiveOneUnit(componentManager, itemCatalog, dropperEntityId, receiverEntityId, drop.StackInstanceId, playerQuery),
        ItemDropKind.WholeStack => InventoryActions.TryLootStack(componentManager, itemCatalog, dropperEntityId, receiverEntityId, drop.StackInstanceId, playerQuery),
        ItemDropKind.EveryStackOfItem => InventoryActions.LootAllStacksOfItem(componentManager, itemCatalog, dropperEntityId, receiverEntityId, drop.ItemDefinitionId, playerQuery),
        ItemDropKind.Currency => CurrencyActions.TryTransfer(componentManager, dropperEntityId, receiverEntityId, drop.CurrencyType),
        ItemDropKind.AllCurrency => CurrencyActions.TryTransferAll(componentManager, dropperEntityId, receiverEntityId),
        _ => throw new ArgumentOutOfRangeException(nameof(drop), drop.Kind, null),
    };
}
