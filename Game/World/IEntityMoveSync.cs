using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>
/// Mandatory map-occupancy bookkeeping for a confirmed move -- narrow so MovementSystem can
/// depend on this abstraction instead of the concrete World (see MovementSystem's own doc
/// comment on why it depends on IMapQuery, not World, for reads; this is the write-side
/// counterpart). Implemented by WorldEventSync, called directly by MovementSystem rather than
/// through EventBus -- this reaction isn't optional/pluggable like ContactDamageSystem's or
/// StatusEffectAuraSystem's, so it doesn't belong on the bus.
///
/// ConvertToNonBlocking is the same "mandatory map-occupancy bookkeeping" shape as SyncMove,
/// just for a death transition instead of a move -- see DeathSystem, the other mandatory
/// consumer of this abstraction.
/// </summary>
public interface IEntityMoveSync
{
    /// <param name="moved">The confirmed move to sync into Map's index.</param>
    /// <param name="isBlocking">
    /// The mover's current IsBlocking() status, already computed by the caller (MovementSystem)
    /// for its own CanOccupy check moments earlier -- passed through here rather than re-derived,
    /// so the same MultiComponentPool lookups aren't repeated for an answer that can't have
    /// changed in between.
    /// </param>
    void SyncMove(EntityMovedEvent moved, bool isBlocking);

    void ConvertToNonBlocking(int entityId, ref TransformComponent transform);
}
