namespace Game.Modules.Death.Components;

/// <summary> Marks an entity as a corpse</summary>summary>
/// <remarks>KilledByEntityId is EntityDiedEvent.Source.EntityId when Source.Kind == StatusEffectSourceKind.Entity, else 
/// null (ahazard/Admin/AI source with no single entity to attribute the kill to).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct DeadComponent(int? KilledByEntityId)
{
    public override readonly string ToString() => $"KilledByEntityId : {KilledByEntityId}";
}
