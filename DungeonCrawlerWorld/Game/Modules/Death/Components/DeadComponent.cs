using Engine.ECS.Entities;
using Engine.ECS.Properties;
using Game.World;

namespace Game.Modules.Death.Components;

/// <summary> Marks an entity as a corpse</summary>
/// <remarks>KilledBy is EntityDiedEvent.Source: for an entity killer, its stable key and the name and
/// crawler number it had, which outlive the killer's unload. DiedAtFrame is the
/// EngineTime.FrameCount DeathSystem.Update was processing when this corpse's EntityDiedEvent was
/// dispatched -- a raw tick count until a real in-game calendar/clock exists (see TODO.md).
/// LootOwnerEntityKey is whoever had dealt the most damage when it died (DamageLedger), the only one
/// who may loot it for LootRights.ExclusiveLootFrames; None when no entity had damaged it.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
[DescribedComponent]
public readonly partial record struct DeadComponent(ActionSource KilledBy, [property: PropertyMeaning(ComponentPropertyMeaningNames.Frame)] long DiedAtFrame, EntityKey LootOwnerEntityKey = default)
{
}
