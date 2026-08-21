namespace Game.Modules.Death.Components;

/// <summary> Marks an entity as a corpse</summary>summary>
/// <remarks>KilledByEntityId is EntityDiedEvent.Source.EntityId when Source.Kind == StatusEffectSourceKind.Entity, else
/// null (ahazard/Admin/AI source with no single entity to attribute the kill to). DiedAtFrame is the
/// EngineTime.FrameCount DeathSystem.Update was processing when this corpse's EntityDiedEvent was
/// dispatched -- a raw tick count until a real in-game calendar/clock exists (see TODO.md).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct DeadComponent(int? KilledByEntityId, long DiedAtFrame)
{
    public override readonly string ToString() => $"KilledByEntityId : {KilledByEntityId}, DiedAtFrame : {DiedAtFrame}";
}
