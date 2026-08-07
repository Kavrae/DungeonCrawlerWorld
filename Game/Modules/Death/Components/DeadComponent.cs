namespace Game.Modules.Death.Components;

/// <summary>
/// Marks an entity as a corpse -- see DeathSystem for what adds it. KilledByEntityId is
/// EntityDiedEvent.Source.EntityId when Source.Kind == StatusEffectSourceKind.Entity, else null (a
/// hazard/Admin/AI source with no single entity to attribute the kill to).
/// </summary>
public readonly record struct DeadComponent(int? KilledByEntityId);
