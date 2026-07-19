namespace Engine.Collections;

/// <summary>
/// Recyclable integer id allocator. Rent() reissues a released id before minting a new
/// one, so ids stay bounded to the high-water mark of concurrently live ids rather than
/// growing forever across churn.
/// </summary>
public sealed class FreeIdPool
{
    private readonly Stack<int> _freeIds = new();
    private byte[] _issued;
    private int _nextId;

    public FreeIdPool(int initialCapacity = 0)
    {
        _issued = new byte[System.Math.Max(initialCapacity, 1)];
    }

    /// <summary>Number of ids currently rented (not released).</summary>
    public int Count => _nextId - _freeIds.Count;

    /// <summary>The highest id ever issued (i.e. the minimum capacity a caller-owned array indexed by id must have).</summary>
    public int HighestIssuedId => _nextId - 1;

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

    public void Release(int id)
    {
        if (!IsIssued(id))
        {
            throw new InvalidOperationException($"Id {id} is not currently issued.");
        }

        _issued[id] = 0;
        _freeIds.Push(id);
    }

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
