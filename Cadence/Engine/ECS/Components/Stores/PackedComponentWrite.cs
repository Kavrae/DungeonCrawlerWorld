using Engine.Utilities;

namespace Engine.ECS.Components.Stores;

/// <summary>A write to one component of a PackedComponentPool by ref, versioned and observed when it is disposed.</summary>
/// <remarks>
/// Opened by Write, TryWrite or WriteByDenseIndex and always held in a using: Dispose bumps the component's version and raises
/// ComponentChanged, as TryUpdate does. A scope TryWrite returns for an entity without the component is not Found, and its
/// Dispose does nothing. While a scope is open the pool's dense storage must not change shape, so in Debug an Add, Remove,
/// Resize or ReserveDenseCapacity on the same pool throws. A scope is for a write made at once on the main thread; a write that
/// must be recorded and applied later is an IComponentUpdater.
/// </remarks>
[MustBeDisposedByUsing]
public ref struct PackedComponentWrite<T> where T : struct
{
    private PackedComponentPool<T>? _pool;
    private readonly ref T _value;
    private readonly int _denseIndex;

    internal PackedComponentWrite(PackedComponentPool<T> pool, ref T value, int denseIndex)
    {
        _pool = pool;
        _value = ref value;
        _denseIndex = denseIndex;
    }

    /// <summary>Whether the entity had the component, so Value can be written.</summary>
    public readonly bool Found => _pool is not null;

    /// <summary>The component, written in place.</summary>
    public readonly ref T Value => ref _value;

    /// <summary>Ends the write: bumps the version and raises ComponentChanged.</summary>
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
