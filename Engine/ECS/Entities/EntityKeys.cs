namespace Engine.ECS.Entities;

/// <summary>The stable key of every living entity, and the living entity each key belongs to.</summary>
/// <remarks>
/// EntityManager issues a key when it creates an entity and releases it when it destroys one. Keys
/// count up from 1 and are never handed out twice, so a key whose entity is gone resolves to nothing
/// rather than to whichever entity reused the id. Owned by ComponentManager, so its relationships
/// resolve links without reaching the EntityManager; everything else reads it as EntityManager.Keys.
/// </remarks>
public sealed class EntityKeys
{
    private ulong[] _keyByEntityId = [];
    private readonly Dictionary<ulong, int> _entityIdByKey = [];
    private ulong _lastIssued;

    /// <summary>How many entities hold a key.</summary>
    public int Count => _entityIdByKey.Count;

    /// <summary>Grows the id-to-key table, where smaller, to hold ids below capacity without growing again.</summary>
    public void Reserve(int capacity)
    {
        if (capacity > _keyByEntityId.Length)
        {
            Array.Resize(ref _keyByEntityId, capacity);
        }

        _entityIdByKey.EnsureCapacity(capacity);
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
        _entityIdByKey.Add(key, entityId);
        return new EntityKey(key);
    }

    /// <summary>Forgets entityId's key, so it no longer resolves to anything.</summary>
    public void Release(int entityId)
    {
        if ((uint)entityId < (uint)_keyByEntityId.Length && _keyByEntityId[entityId] is var key and not 0)
        {
            _entityIdByKey.Remove(key);
            _keyByEntityId[entityId] = 0;
        }
    }

    /// <summary>entityId's key, or None when it has none.</summary>
    public EntityKey GetKey(int entityId) =>
        (uint)entityId < (uint)_keyByEntityId.Length ? new EntityKey(_keyByEntityId[entityId]) : EntityKey.None;

    /// <summary>The living entity holding key, if there still is one.</summary>
    public bool TryGetEntityId(EntityKey key, out int entityId) => _entityIdByKey.TryGetValue(key.Value, out entityId);
}
