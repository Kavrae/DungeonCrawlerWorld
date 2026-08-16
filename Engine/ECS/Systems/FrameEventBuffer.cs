namespace Engine.ECS.Systems;

/// <summary>Registered with SystemManager.RegisterFrameScoped so ClearFrame runs once, automatically, at the end of every full Update() cycle -- see FrameEventBuffer's own doc comment for why.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface IFrameScoped
{
    void ClearFrame();
}

/// <summary> A single producer's per-frame batch of T, for one or more other systems to read within the same SystemManager.Update() cycle instead of each instance triggering a synchronous multicast event dispatch</summary>
/// <remarks>
/// The deferred-buffer alternative to per-instance events for
/// anything that fires at high frequency (e.g. MovementSystem's confirmed moves, striped
/// across a large population). The producer calls Record(item) as each item happens during
/// its own Update; every consumer reads Items (a stable view for the whole cycle) during its
/// own later Update within the same cycle. SystemManager clears every registered
/// IFrameScoped buffer once, after every system has had its turn that cycle (see
/// SystemManager.Update)  so anything written before this cycle's systems ran (not just the
/// producer's own writes) survives to be read by every consumer, regardless of registration
/// order relative to the write.
/// </remarks>
public sealed class FrameEventBuffer<T> : IFrameScoped
{
    private readonly List<T> _items = [];
    private bool _hasBeenRead;

    /// <summary>Reading marks the buffer read for this cycle -- see Record's own doc comment for why.</summary>
    public IReadOnlyList<T> Items
    {
        get
        {
            _hasBeenRead = true;
            return _items;
        }
    }

    /// <summary>Records item for this cycle's consumers to read via Items.</summary>
    /// <remarks>
    /// Throws if Items has already been read this cycle -- SystemManager.Update runs every
    /// system strictly sequentially and single-threaded, so a Record call after any read this
    /// cycle unambiguously means whatever it just added will never be seen by a consumer and
    /// will be destroyed by the next ClearFrame, exactly the "silently and permanently lost"
    /// hazard this type's own doc comment warns about. Turns that silent corruption into a loud,
    /// attributable failure at the actual offending call -- a second producer, or a producer/
    /// consumer Dependencies ordering regression -- instead of a consumer mysteriously never
    /// seeing an event it should have.
    /// </remarks>
    public void Record(T item)
    {
        if (_hasBeenRead)
        {
            throw new InvalidOperationException(
                $"FrameEventBuffer<{typeof(T).Name}>.Record called after Items was already read this frame -- " +
                "this entry would be silently lost. Likely a second producer, or a producer/consumer " +
                "Dependencies ordering regression. See this type's own doc comment.");
        }

        _items.Add(item);
    }

    /// <summary>Clears the buffer for the next frame.</summary>
    public void ClearFrame()
    {
        _items.Clear();
        _hasBeenRead = false;
    }
}
