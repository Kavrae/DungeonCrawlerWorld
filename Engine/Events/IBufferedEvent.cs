namespace Engine.Events;

/// <summary> Marks an event type for buffered dispatch</summary>
/// <remarks
/// >Publish enqueues buffered events instead of invoking subscribers immediately
/// Delivery only happens when the owning code calls DispatchBuffered&lt;T&gt;() at its own natural checkpoint.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IBufferedEvent;