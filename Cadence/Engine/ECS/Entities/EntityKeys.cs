namespace Engine.ECS.Entities;

/// <summary>The stable key of every living entity, and the living entity each key belongs to.</summary>
/// <remarks>
/// EntityManager issues a key when it creates an entity and releases it when it destroys one. Keys
/// count up from 1 and are never handed out twice, so a key whose entity is gone resolves to nothing
/// rather than to whichever entity reused the id. Owned by ComponentManager, so its relationships
/// resolve links without reaching the EntityManager; everything else reads it as EntityManager.Keys.
///
/// A key resolves through a table indexed by the key itself, split into pages of consecutive keys. Keys
/// are issued in order, so the entities spawned together share pages, and a page is freed as soon as
/// the last of its keys is released.
/// </remarks>
public sealed class EntityKeys
{
    private const int KeyPageShift = 8;
    private const int KeysPerPage = 1 << KeyPageShift;
    private const int KeyPageMask = KeysPerPage - 1;

    private ulong[] _keyByEntityId = [];

    /// <summary>Each page holds entity id + 1 per key, so a page fresh from allocation reads as every key released.</summary>
    private int[]?[] _entityIdPlusOnePagesByKeyPage = [];
    private int[] _livingKeyCountByKeyPage = [];
    private ulong _lastIssued;

    /// <summary>How many entities hold a key.</summary>
    public int Count { get; private set; }

    /// <summary>Grows the id-to-key table, where smaller, to hold ids below capacity without growing again.</summary>
    public void Reserve(int capacity)
    {
        if (capacity > _keyByEntityId.Length)
        {
            Array.Resize(ref _keyByEntityId, capacity);
        }

        EnsureKeyPageTableCovers(_lastIssued + (ulong)capacity);
    }

    /// <summary>Issues entityId the next unused key.</summary>
    public EntityKey Issue(int entityId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityId);

        if (entityId >= _keyByEntityId.Length)
        {
            Array.Resize(ref _keyByEntityId, System.Math.Max(entityId + 1, System.Math.Max(16, _keyByEntityId.Length * 2)));
        }

        Release(entityId);

        var key = ++_lastIssued;
        _keyByEntityId[entityId] = key;

        var keyPageIndex = KeyPageIndexOf(key);
        EnsureKeyPageTableCovers(key);
        var keyPage = _entityIdPlusOnePagesByKeyPage[keyPageIndex] ??= new int[KeysPerPage];
        keyPage[(int)(key & KeyPageMask)] = entityId + 1;
        _livingKeyCountByKeyPage[keyPageIndex]++;
        Count++;

        return new EntityKey(key);
    }

    /// <summary>Forgets entityId's key, so it no longer resolves to anything.</summary>
    public void Release(int entityId)
    {
        if ((uint)entityId >= (uint)_keyByEntityId.Length || _keyByEntityId[entityId] is not (var key and not 0))
        {
            return;
        }

        _keyByEntityId[entityId] = 0;
        Count--;

        var keyPageIndex = KeyPageIndexOf(key);
        if (--_livingKeyCountByKeyPage[keyPageIndex] == 0)
        {
            _entityIdPlusOnePagesByKeyPage[keyPageIndex] = null;
            return;
        }

        _entityIdPlusOnePagesByKeyPage[keyPageIndex]![(int)(key & KeyPageMask)] = 0;
    }

    /// <summary>entityId's key, or None when it has none.</summary>
    public EntityKey GetKey(int entityId) =>
        (uint)entityId < (uint)_keyByEntityId.Length ? new EntityKey(_keyByEntityId[entityId]) : EntityKey.None;

    /// <summary>The living entity holding key, if there still is one.</summary>
    public bool TryGetEntityId(EntityKey key, out int entityId)
    {
        var keyPageIndex = key.Value >> KeyPageShift;
        var keyPages = _entityIdPlusOnePagesByKeyPage;
        if (keyPageIndex < (ulong)keyPages.Length && keyPages[keyPageIndex] is { } keyPage && keyPage[(int)(key.Value & KeyPageMask)] is var entityIdPlusOne and not 0)
        {
            entityId = entityIdPlusOne - 1;
            return true;
        }

        entityId = 0;
        return false;
    }

    private static int KeyPageIndexOf(ulong key) => checked((int)(key >> KeyPageShift));

    private void EnsureKeyPageTableCovers(ulong key)
    {
        var requiredPageCount = KeyPageIndexOf(key) + 1;
        if (requiredPageCount <= _entityIdPlusOnePagesByKeyPage.Length)
        {
            return;
        }

        var newPageCount = System.Math.Max(requiredPageCount, _entityIdPlusOnePagesByKeyPage.Length * 2);
        Array.Resize(ref _entityIdPlusOnePagesByKeyPage, newPageCount);
        Array.Resize(ref _livingKeyCountByKeyPage, newPageCount);
    }
}
