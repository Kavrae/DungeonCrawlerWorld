namespace Game.World;

/// <summary>
/// Mandatory map-occupancy bookkeeping for a confirmed move -- narrow so MovementSystem can
/// depend on this abstraction instead of the concrete World (see MovementSystem's own doc
/// comment on why it depends on IMapQuery, not World, for reads; this is the write-side
/// counterpart). Implemented by WorldEventSync, called directly by MovementSystem rather than
/// through EventBus -- this reaction isn't optional/pluggable like ContactDamageSystem's or
/// StatusEffectAuraSystem's, so it doesn't belong on the bus.
/// </summary>
public interface IEntityMoveSync
{
    void SyncMove(EntityMoved moved);
}
