using Engine.Utilities;

namespace Engine.ECS.Components.Stores;

/// <summary>A write to one component instance of a MultiComponentPool by ref, versioned and observed when it is disposed.</summary>
/// <remarks>
/// Opened by WriteByDenseIndex or TryWriteFirst and always held in a using: Dispose bumps the instance's and its entity's
/// versions and raises ComponentChanged, as TryUpdateFirst does. A scope TryWriteFirst returns when nothing matched is not
/// Found, and its Dispose does nothing. The rest is PackedComponentWrite's: in Debug the pool refuses structural changes while
/// a scope is open, and a write to be recorded and applied later is an IComponentUpdater.
/// </remarks>
[MustBeDisposedByUsing]
public ref struct MultiComponentWrite<T> where T : struct
{
    private MultiComponentPool<T>? _pool;
    private readonly ref T _value;
    private readonly int _denseIndex;

    internal MultiComponentWrite(MultiComponentPool<T> pool, ref T value, int denseIndex)
    {
        _pool = pool;
        _value = ref value;
        _denseIndex = denseIndex;
    }

    /// <summary>Whether an instance was found, so Value can be written.</summary>
    public readonly bool Found => _pool is not null;

    /// <summary>The component instance, written in place.</summary>
    public readonly ref T Value => ref _value;

    /// <summary>Ends the write: bumps the versions and raises ComponentChanged.</summary>
    public void Dispose()
    {
        if (_pool is null)
        {
            return;
        }

        var pool = _pool;
        _pool = null;
        pool.EndWrite(_denseIndex);
    }
}
