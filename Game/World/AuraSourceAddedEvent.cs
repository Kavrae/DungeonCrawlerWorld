using Game.Modules.StatusEffectAura.Components;

namespace Game.World;

/// <summary>
/// Published by AuraSourceEffects.Toggle (or any future direct grant) whenever a
/// StatusEffectAuraSourceComponent instance is added to an entity outside of blueprint-time
/// population -- StatusEffectAuraSystem/MapTintGrid each subscribe to keep their own
/// incrementally-maintained grids in sync with a source that appeared after their own one-time
/// startup scatter already ran, rather than treating StatusEffectAuraSourceComponent as
/// terrain-only and static once placed. Immediate, not IBufferedEvent, same reasoning as
/// ActionActivatedEvent/StatusEffectAppliedEvent: rare (player-ability-cast frequency, not
/// per-move), and every consumer only ever writes to its own private grid/dictionary, never to
/// a pool some other system is mid-scan over.
/// </summary>
public readonly record struct AuraSourceAddedEvent(int EntityId, StatusEffectAuraSourceComponent Source);
