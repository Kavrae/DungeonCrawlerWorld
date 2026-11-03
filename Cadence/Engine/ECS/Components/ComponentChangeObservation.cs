namespace Engine.ECS.Components;

/// <summary>Whether a Packed or Multi pool raises ComponentChanged and ComponentRemoving, decided while the build registers components and fixed for the build.</summary>
/// <remarks>
/// Subscribing to either event of an Unobserved pool throws, so every observer is declared during component registration:
/// the owning module registers the pool Observed, and any other module asks for it with ComponentManager.ObserveChanges. A pool of an IScheduledTimer component is always Observed (its timer wheel schedules from ComponentChanged),
/// and so is a relationship's link pool.
///
/// What an observer may rely on: a change reaches it no later than the end of the system that made it. Every write is
/// delivered at once today; a write made on a worker thread would be delivered at the next sync point instead, in system
/// order and then entity order. Code that reads what an observer keeps straight after a write, in the same system, is
/// relying on more than that. Relationship does today: it checks a link and keeps the target side as the link is
/// written, so link pools are written on the main thread only.
/// </remarks>
public enum ComponentChangeObservation : byte
{
    Unobserved,
    Observed,
}
