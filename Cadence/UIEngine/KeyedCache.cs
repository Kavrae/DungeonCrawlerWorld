namespace UIEngine;

/// <summary>A value built from a key -- a line of text, a tooltip's rows -- rebuilt only when the key changes.</summary>
/// <remarks>
/// Holds one entry: the last key it was asked for and the value built from it. A caller asks every frame with the key
/// made from what it is showing right now; an equal key returns the kept value, any other key runs build once and keeps
/// that. Nothing is declared in advance. The key must hold every input the value is built from, at the precision it is
/// shown (whole seconds remaining, not frames), or a change shows nothing until the key next changes. A value tuple or a
/// record struct is a key as it stands. build should be a static lambda, so asking allocates nothing: what the value is
/// built from comes in the key, and anything constant (a registry, the asking element) as state.
/// </remarks>
public sealed class KeyedCache<TKey, TValue> where TKey : IEquatable<TKey>
{
    private bool _hasValue;
    private TKey _key = default!;
    private TValue _value = default!;

    public TValue Get(TKey key, Func<TKey, TValue> build)
    {
        if (_hasValue && _key.Equals(key))
        {
            return _value;
        }

        _value = build(key);
        _key = key;
        _hasValue = true;
        return _value;
    }

    public TValue Get<TState>(TKey key, TState state, Func<TKey, TState, TValue> build)
    {
        if (_hasValue && _key.Equals(key))
        {
            return _value;
        }

        _value = build(key, state);
        _key = key;
        _hasValue = true;
        return _value;
    }

    /// <summary>Forgets the kept value, so the next Get builds whatever its key.</summary>
    public void Clear()
    {
        _hasValue = false;
        _key = default!;
        _value = default!;
    }
}
