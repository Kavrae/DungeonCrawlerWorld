using Engine.Utilities;

namespace Engine.ECS.Components.Stores;

/// <summary>A write to one component of a DirectComponentPool by ref, versioned when it is disposed.</summary>
/// <remarks>
/// Opened by Write or TryWrite and always held in a using: Dispose bumps the component's version, as TryUpdate does. A Direct
/// pool can't be observed, so nothing else is told. A scope TryWrite returns for an entity without the component is not Found,
/// and its Dispose does nothing. In Debug the pool refuses a Remove or Resize while a scope is open.
/// </remarks>
[MustBeDisposedByUsing]
public ref struct DirectComponentWrite<T> where T : struct
{
    private DirectComponentPool<T>? _pool;
    private readonly ref T _value;
    private readonly int _entityId;

    internal DirectComponentWrite(DirectComponentPool<T> pool, ref T value, int entityId)
    {
        _pool = pool;
        _value = ref value;
        _entityId = entityId;
    }

    /// <summary>Whether the entity had the component, so Value can be written.</summary>
    public readonly bool Found => _pool is not null;

    /// <summary>The component, written in place.</summary>
    public readonly ref T Value => ref _value;

    /// <summary>Ends the write: bumps the version.</summary>
    public void Dispose()
    {
        if (_pool is null)
        {
            return;
        }

        var pool = _pool;
        _pool = null;
        pool.EndWrite(_entityId);
    }
}
