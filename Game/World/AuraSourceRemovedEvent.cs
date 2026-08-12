using Game.Modules.StatusEffectAura.Components;

namespace Game.World;

/// <summary>
/// Mirrors AuraSourceAddedEvent for the removal half -- published by AuraSourceEffects.Toggle/
/// RemoveAll with the actual removed component value (not reconstructed from whatever a caller
/// currently passes), so a subscriber retracting its own grid contribution always retracts
/// exactly what was added, even if it differs from the toggling call's own parameters.
/// </summary>
public readonly record struct AuraSourceRemovedEvent(int EntityId, StatusEffectAuraSourceComponent Source);
