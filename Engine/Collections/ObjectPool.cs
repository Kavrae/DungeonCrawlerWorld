namespace Engine.Collections;

/// <summary>Generic reusable-instance pool, for cutting GC pressure on hot paths that repeatedly create and discard the same shape of object.</summary>
/// <typeparam name="T">The type of object to pool.</typeparam>
/// <param name="factory">The function used to create new instances of the object.</param>
/// <param name="reset">The function used to reset the state of an object before returning it to the pool.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ObjectPool<T>(Func<T> factory, Action<T>? reset = null) where T : class
{
    private readonly Func<T> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly Action<T>? _reset = reset;
    private readonly Stack<T> _items = new();

    /// <summary>The number of items currently available in the pool.</summary>
    public int Count => _items.Count;

    /// <summary>Returns an instance from the pool if available, otherwise creates a new one.</summary>
    public T Rent() => _items.Count > 0
        ? _items.Pop()
        : _factory();

    /// <summary>Returns an instance to the pool.</summary>
    /// <param name="item">The instance to return.</param>
    public void Return(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _reset?.Invoke(item);
        _items.Push(item);
    }
}