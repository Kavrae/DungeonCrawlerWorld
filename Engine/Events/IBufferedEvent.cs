namespace Engine.Events;

/// <summary>
/// Marks an event type for buffered dispatch: Publish enqueues instead of invoking
/// subscribers immediately, and delivery only happens when the owning code calls
/// DispatchBuffered&lt;T&gt;() at its own natural checkpoint. An event type either implements
/// this or doesn't -- the dispatch mode is decided once, at the type definition, not per
/// Publish call, so a caller can't accidentally pick the wrong mode for a given event with
/// no compiler help. Use this for anything that doesn't need same-frame delivery and could
/// have meaningful fan-out cost; leave it off anything that needs an immediate, synchronous
/// effect before the publisher's own caller continues (e.g. keeping a spatial index correct
/// for the very next collision check in the same frame).
/// </summary>
public interface IBufferedEvent;