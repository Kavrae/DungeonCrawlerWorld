using Engine.ECS.Properties;

namespace Game.Modules.NpcBehavior.Components;

/// <summary>An NPC refused a corpse's loot (LootRights): it tries no corpse again before RetryAfterFrame.</summary>
/// <remarks>A deadline that only stops gating (nothing happens when it passes), so no system or timer wheel drives it.</remarks>
[DescribedComponent]
public readonly partial record struct CorpseLootRetryComponent([property: PropertyMeaning(ComponentPropertyMeaningNames.Frame)] uint RetryAfterFrame)
{
}
