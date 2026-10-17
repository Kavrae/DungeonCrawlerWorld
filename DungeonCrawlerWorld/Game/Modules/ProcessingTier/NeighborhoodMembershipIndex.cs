using Engine.Math;
using Game.World;

namespace Game.Modules.ProcessingTier;

/// <summary>Which entities sit in each neighborhood, per MapLayer -- so a neighborhood crossing can retier only the neighborhoods whose tier actually changed.</summary>
/// <remarks>
/// <para>
/// Maintained by ProcessingTierResolver from the positions it already computes tiers from
/// (CreateEntityAt, EnsureTiered, Retier), so every path that gives an entity a tier also indexes it,
/// with no separate hook to forget. It is a hint rather than a source of truth: ProcessingTierSystem
/// re-reads each entity's TransformComponent when it walks a cell, drops entries whose transform is
/// gone, and Retier moves an entry whose real position has left the cell. That tolerance is what lets
/// the index skip removal notifications -- DirectComponentPool raises none, and a destroyed entity's
/// id can be recycled before anything walks its old cell.
/// </para>
/// <para>
/// Exists so a neighborhood crossing costs the population of the affected neighborhoods rather than a
/// scan of every entity slot. Cell coordinates are Neighborhoods.CellOf.
/// </para>
/// <para>
/// Storage: one List per occupied (cell, layer), plus two entity-indexed int arrays recording which
/// list and which slot each entity occupies, so a move or removal is an O(1) swap-remove. Grown by
/// doubling to the largest entity id seen.
/// </para>
/// </remarks>
public sealed class NeighborhoodMembershipIndex
{
    private const int NotIndexed = -1;

    private readonly Dictionary<long, int> _listSlotByKey = [];
    private readonly List<List<int>> _lists = [];
    private readonly List<long> _keyByListSlot = [];

    private int[] _listSlotByEntity = [];
    private int[] _indexInListByEntity = [];

    private long _lastKey = long.MinValue;
    private int _lastListSlot = NotIndexed;

    /// <summary>The number of entities currently indexed across every cell.</summary>
    public int Count { get; private set; }

    /// <summary>Records entityId as being in the cell containing position, moving it out of any cell it was previously indexed in.</summary>
    public void Set(int entityId, Vector3Int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityId);

        EnsureEntityCapacity(entityId);

        var key = KeyFor(Neighborhoods.CellOf(position.X), Neighborhoods.CellOf(position.Y), position.Z);
        var currentListSlot = _listSlotByEntity[entityId];
        if (currentListSlot != NotIndexed && _keyByListSlot[currentListSlot] == key)
        {
            return;
        }

        var listSlot = GetOrCreateListSlot(key);

        if (currentListSlot != NotIndexed)
        {
            RemoveFromList(entityId, currentListSlot);
        }
        else
        {
            Count++;
        }

        var list = _lists[listSlot];
        _listSlotByEntity[entityId] = listSlot;
        _indexInListByEntity[entityId] = list.Count;
        list.Add(entityId);
    }

    /// <summary>Removes entityId from the index. No-op if it isn't indexed.</summary>
    public void Remove(int entityId)
    {
        if ((uint)entityId >= (uint)_listSlotByEntity.Length || _listSlotByEntity[entityId] == NotIndexed)
        {
            return;
        }

        RemoveFromList(entityId, _listSlotByEntity[entityId]);
        _listSlotByEntity[entityId] = NotIndexed;
        Count--;
    }

    /// <summary>Whether entityId is currently indexed in the cell containing position.</summary>
    public bool IsIndexedAt(int entityId, Vector3Int position) =>
        (uint)entityId < (uint)_listSlotByEntity.Length &&
        _listSlotByEntity[entityId] != NotIndexed &&
        _listSlotByKey.TryGetValue(KeyFor(Neighborhoods.CellOf(position.X), Neighborhoods.CellOf(position.Y), position.Z), out var listSlot) &&
        _listSlotByEntity[entityId] == listSlot;

    /// <summary>Appends every entity indexed in neighborhood (cellX, cellY) on layer z to destination.</summary>
    /// <remarks>A copy rather than a span: walking a cell retiers its entities, and a retier can move an entity out of the very list being walked.</remarks>
    public void CopyCell(int cellX, int cellY, int z, List<int> destination)
    {
        if (_listSlotByKey.TryGetValue(KeyFor(cellX, cellY, z), out var listSlot))
        {
            destination.AddRange(_lists[listSlot]);
        }
    }

    private void RemoveFromList(int entityId, int listSlot)
    {
        var list = _lists[listSlot];
        var index = _indexInListByEntity[entityId];
        var lastIndex = list.Count - 1;

        if (index != lastIndex)
        {
            var movedEntityId = list[lastIndex];
            list[index] = movedEntityId;
            _indexInListByEntity[movedEntityId] = index;
        }

        list.RemoveAt(lastIndex);
    }

    private int GetOrCreateListSlot(long key)
    {
        if (key == _lastKey)
        {
            return _lastListSlot;
        }

        if (!_listSlotByKey.TryGetValue(key, out var listSlot))
        {
            listSlot = _lists.Count;
            _lists.Add([]);
            _keyByListSlot.Add(key);
            _listSlotByKey.Add(key, listSlot);
        }

        _lastKey = key;
        _lastListSlot = listSlot;
        return listSlot;
    }

    private void EnsureEntityCapacity(int entityId)
    {
        if (entityId < _listSlotByEntity.Length)
        {
            return;
        }

        var oldLength = _listSlotByEntity.Length;
        var newLength = System.Math.Max(entityId + 1, System.Math.Max(16, oldLength * 2));

        Array.Resize(ref _listSlotByEntity, newLength);
        Array.Resize(ref _indexInListByEntity, newLength);
        Array.Fill(_listSlotByEntity, NotIndexed, oldLength, newLength - oldLength);
    }

    private static long KeyFor(int cellX, int cellY, int z) =>
        ((long)cellX << 40) ^ (((long)cellY & 0xFFFFFF) << 16) ^ ((long)z & 0xFFFF);
}
