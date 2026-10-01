using Game.Modules.Auras.Components;

namespace Game.World;

/// <summary>An aura source was removed from an entity.</summary>
/// <remarks>
/// Published by every AuraSourceEffects removal, with the component value that was actually
/// stored (not one reconstructed from the caller's arguments), so AuraSystem takes exactly what was
/// added out of the aura field. There is no matching event for an add: AuraSystem observes the pool,
/// and a pool announces nothing on removal.
/// </remarks>
public readonly record struct AuraSourceRemovedEvent(int EntityId, AuraSourceComponent Source);
