using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Game.Floors;
using Game.Modules.Core.Components;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.ItemPiles.Components;
using Game.World;

namespace Game.Modules.ItemPiles;

/// <summary>Keeps a neighborhood's item piles in its record while it is unloaded, and puts them back when it loads.</summary>
/// <remarks>
/// A neighborhood's population is generated afresh on every visit, but what the player put down is not: between
/// NeighborhoodUnloadingEvent and NeighborhoodUnloadedEvent, each pile the unload destroys there is kept in the record as an
/// ItemPileSnapshot -- where it lay, its stacks exactly as they were as it went, its currency -- so a pile changed while the
/// unload is spread over frames is kept as it last was, and one that holds nothing is not kept at all. Once the
/// neighborhood is loaded and populated again (NeighborhoodPopulatedEvent), each is put back where it lay, joining a pile
/// already there. A pile whose tile has since become a wall goes to the nearest open tile within RelocationRadius in the
/// same neighborhood, and is lost with its contents when there is none. A regeneration unloads and loads, so it keeps piles too.
/// A lit unit comes back lit, and ToggleItemHolderSync turns its toggle back on as its stack is added.
/// </remarks>
public sealed class ItemPileKeeping
{
    /// <summary>How far, in tiles, a kept pile whose tile is now a wall may move to the nearest open one.</summary>
    public const int RelocationRadius = 3;

    private readonly ComponentManager _componentManager;
    private readonly IMapQuery _mapQuery;
    private readonly ItemDrops _itemDrops;
    private readonly PackedComponentPool<ItemPileComponent> _piles;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks;
    private readonly PackedComponentPool<CurrencyComponent> _currencies;
    private readonly Dictionary<(int CellX, int CellY), NeighborhoodRecord> _unloadingRecords = [];

    public ItemPileKeeping(ComponentManager componentManager, EntityManager entityManager, IMapQuery mapQuery, ItemDrops itemDrops)
    {
        _componentManager = componentManager;
        _mapQuery = mapQuery;
        _itemDrops = itemDrops;
        _piles = componentManager.GetPackedPool<ItemPileComponent>();
        _transforms = componentManager.GetDirectPool<TransformComponent>();
        _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        _currencies = componentManager.GetPackedPool<CurrencyComponent>();

        entityManager.EntityDestroying += OnEntityDestroying;
    }

    /// <summary>Starts keeping, in its record, each item pile the unloading neighborhood's unload destroys.</summary>
    public void BeginKeeping(NeighborhoodUnloadingEvent unloading) =>
        _unloadingRecords[(unloading.CellX, unloading.CellY)] = unloading.Record;

    /// <summary>Stops keeping piles for the neighborhood whose unload is done.</summary>
    public void EndKeeping(NeighborhoodUnloadedEvent unloaded) => _unloadingRecords.Remove((unloaded.CellX, unloaded.CellY));

    /// <summary>Keeps a pile holding anything, destroyed while its neighborhood unloads, exactly as it is at that moment.</summary>
    private void OnEntityDestroying(int entityId)
    {
        if (_unloadingRecords.Count == 0 || !_piles.Has(entityId) || !_transforms.TryGetReadonly(entityId, out var transform) ||
            !_unloadingRecords.TryGetValue((Neighborhoods.CellOf(transform.Position.X), Neighborhoods.CellOf(transform.Position.Y)), out var record))
        {
            return;
        }

        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(_inventoryStacks, entityId, stacks);
        _currencies.TryGetReadonly(entityId, out var currency);
        if (stacks.Count == 0 && currency.Gold <= 0 && currency.Credits <= 0)
        {
            return;
        }

        record.KeptItemPiles.Add(new ItemPileSnapshot(transform.Position, stacks, currency));
    }

    /// <summary>Puts back every item pile the loaded neighborhood's record kept, and forgets them there.</summary>
    public void Restore(NeighborhoodPopulatedEvent populated)
    {
        foreach (var snapshot in populated.Record.KeptItemPiles)
        {
            if (TryFindOpenTile(snapshot.Position, populated.CellX, populated.CellY, out var tile) && TryGetPileAt(tile, out var pileEntityId))
            {
                Fill(pileEntityId, snapshot);
            }
        }

        populated.Record.KeptItemPiles.Clear();
    }

    private bool TryGetPileAt(Vector3Int tile, out int pileEntityId) =>
        _itemDrops.TryFindPileAt(tile, out pileEntityId) || _itemDrops.TrySpawnPileAt(tile, out pileEntityId);

    private void Fill(int pileEntityId, ItemPileSnapshot snapshot)
    {
        InventoryGrant.EnsureInventoryComponentExists(_componentManager, pileEntityId);
        foreach (var stack in snapshot.Stacks)
        {
            _inventoryStacks.Add(pileEntityId, stack);
        }

        if (snapshot.Currency.Gold > 0 || snapshot.Currency.Credits > 0)
        {
            _currencies.TryGetReadonly(pileEntityId, out var held);
            _componentManager.Merge(pileEntityId, new CurrencyComponent(held.Gold + snapshot.Currency.Gold, held.Credits + snapshot.Currency.Credits));
        }
    }

    /// <summary>position itself if nothing there blocks, else the open tile nearest it within RelocationRadius, in neighborhood (cellX, cellY) on its layer.</summary>
    private bool TryFindOpenTile(Vector3Int position, int cellX, int cellY, out Vector3Int tile)
    {
        for (var radius = 0; radius <= RelocationRadius; radius++)
        {
            for (var offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (var offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (System.Math.Max(System.Math.Abs(offsetX), System.Math.Abs(offsetY)) != radius)
                    {
                        continue;
                    }

                    tile = new Vector3Int(position.X + offsetX, position.Y + offsetY, position.Z);
                    if (Neighborhoods.CellOf(tile.X) == cellX && Neighborhoods.CellOf(tile.Y) == cellY &&
                        _mapQuery.IsOnMap(tile) && !_mapQuery.IsCellBlocked(tile))
                    {
                        return true;
                    }
                }
            }
        }

        tile = default;
        return false;
    }
}
