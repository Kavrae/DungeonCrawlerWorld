namespace Engine.Collections;

/// <summary>
/// Generic reusable-instance pool, for cutting GC pressure on hot paths that repeatedly
/// create and discard the same shape of object (e.g. per-frame view buffers, pooled UI
/// windows).
/// </summary>
public sealed class ObjectPool<T>(Func<T> factory, Action<T>? reset = null) where T : class
{
    private readonly Func<T> _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly Action<T>? _reset = reset;
    private readonly Stack<T> _items = new();

    public int Count => _items.Count;

    public T Rent() => _items.Count > 0
        ? _items.Pop()
        : _factory();

    public void Return(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _reset?.Invoke(item);
        _items.Push(item);
    }
}