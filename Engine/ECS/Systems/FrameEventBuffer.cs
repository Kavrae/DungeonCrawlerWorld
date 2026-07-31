namespace Engine.ECS.Systems;

/// <summary>Registered with SystemManager.RegisterFrameScoped so ClearFrame runs once, automatically, at the end of every full Update() cycle -- see FrameEventBuffer's own doc comment for why.</summary>
public interface IFrameScoped
{
    void ClearFrame();
}

/// <summary>
/// A single producer's per-frame batch of T, for one or more other systems to read within the
/// same SystemManager.Update() cycle instead of each instance triggering a synchronous
/// multicast event dispatch -- the deferred-buffer alternative to per-instance events for
/// anything that fires at high frequency (e.g. MovementSystem's confirmed moves, striped
/// across a large population). The producer calls Record(item) as each item happens during
/// its own Update; every consumer reads Items (a stable view for the whole cycle) during its
/// own later Update within the same cycle. SystemManager clears every registered
/// IFrameScoped buffer once, after every system has had its turn that cycle (see
/// SystemManager.Update) -- not the producer clearing its own buffer at the start of its next
/// Update -- specifically so anything written before this cycle's systems ran (not just the
/// producer's own writes) survives to be read by every consumer, regardless of registration
/// order relative to the write.
///
/// Hard invariant: this assumes exactly one producer per buffer, correctly ordered (see
/// SystemManager/module Dependencies) before every consumer. A second producer writing from a
/// system that runs AFTER some consumer in a given frame would have that entry silently and
/// permanently lost that frame -- not delayed to next frame, actually destroyed by the
/// end-of-cycle clear before anything ever reads it. Keep Record reachable only by known,
/// correctly-ordered producers; do not expose it as a general-purpose event sink.
/// </summary>
public sealed class FrameEventBuffer<T> : IFrameScoped
{
    private readonly List<T> _items = [];

    public IReadOnlyList<T> Items => _items;

    public void Record(T item) => _items.Add(item);

    public void ClearFrame() => _items.Clear();
}
