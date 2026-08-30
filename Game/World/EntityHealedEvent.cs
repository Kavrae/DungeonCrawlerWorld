namespace Game.World;

/// <summary>
/// Published by whatever healed the target (see HealthHeal.Apply/ComplexHealthHeal) after
/// CurrentHealth has already been clamped and updated. Only published when the player is
/// involved -- either as the entity healed or as the source that healed it -- mirroring
/// EntityDamagedEvent's own player-only scope (see TODO.md's "Debug/event logging with levels"
/// item for the plan to generalize this fully).
/// </summary>
/// <param name="HealType">
/// Human-readable description of what kind of heal this was, e.g. "Regeneration" or an
/// action/item's own ActivatorName ("Heal", "Health Potion") -- free text rather than an enum,
/// purely for logging, mirroring EntityDamagedEvent.DamageType.
/// </param>
/// <param name="Amount">The heal actually computed, after OutgoingHealing/IncomingHealing modifiers, before any clamp against the target's own maximum -- mirrors EntityDamagedEvent.Amount's identical pre-clamp convention.</param>
/// <param name="CurrentHealth">
/// The entity's real summed current health after the heal landed (HealthQueries.TryGetTotals for
/// a Complex target, not just the one part/share that actually received it). Kept as float, unlike
/// EntityDamagedEvent's ushort fields -- health/heal math throughout this pipeline is already
/// float (SimpleHealthComponent.CurrentHealth), so this avoids an extra lossy cast for no benefit.
/// </param>
/// <param name="MaximumHealth">The modifier-effective max (StatModifierMath), not the raw stored field -- same reasoning as EntityDamagedEvent.MaximumHealth.</param>
public readonly record struct EntityHealedEvent(int EntityId, float Amount, StatusEffectSource Source, float CurrentHealth, float MaximumHealth, string HealType);
