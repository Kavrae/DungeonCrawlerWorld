namespace Engine.Collections;

/// <summary> Recyclable integer id allocator. </summary>
/// <remarks>Rent() reissues a released id before minting a new one.
/// Ids stay bounded to the high-water mark of concurrently live ids rather than
/// growing forever across churn.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class FreeIdPool(int initialCapacity = 0)
{
    private readonly Stack<int> _freeIds = new();
    private byte[] _issued = new byte[System.Math.Max(initialCapacity, 1)];
    private int _nextId;

    /// <summary>Number of ids currently rented (not released).</summary>
    public int Count => _nextId - _freeIds.Count;

    /// <summary>The highest id ever issued (i.e. the minimum capacity a caller-owned array indexed by id must have).</summary>
    public int HighestIssuedId => _nextId - 1;

    /// <summary>Returns the first available ID from the pool.</summary>
    /// <returns>If there are no free ids, increase the ID pool capacity.</returns>
    public int Rent()
    {
        int id;
        if (_freeIds.Count > 0)
        {
            id = _freeIds.Pop();
        }
        else
        {
            id = _nextId++;
            EnsureCapacity(id + 1);
        }

        _issued[id] = 1;
        return id;
    }

    /// <summary>Releases a previously rented ID back to the pool.</summary>
    /// <param name="id">The ID to release.</param>
    public void Release(int id)
    {
        if (!IsIssued(id))
        {
            return;
        }

        _issued[id] = 0;
        _freeIds.Push(id);
    }

    /// <summary>Whether the specified ID is currently issued.</summary>
    /// <param name="id">The ID to check.</param>
    /// <returns><c>true</c> if the ID is currently issued; otherwise, <c>false</c>.</returns>
    public bool IsIssued(int id) => id >= 0 && id < _nextId && _issued[id] != 0;

    private void EnsureCapacity(int minimumCapacity)
    {
        if (_issued.Length >= minimumCapacity)
        {
            return;
        }

        var newSize = System.Math.Max(_issued.Length * 2, minimumCapacity);
        Array.Resize(ref _issued, newSize);
    }
}