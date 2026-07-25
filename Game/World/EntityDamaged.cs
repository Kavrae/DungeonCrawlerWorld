namespace Game.World;

/// <summary>
/// Published by whatever dealt damage (see HealthDamage.Apply) after CurrentHealth has already
/// been clamped and updated. Only published for the player today -- see TODO.md's "Debug/event
/// logging with levels" item for the plan to generalize this.
/// </summary>
/// <param name="DamageType">
/// Human-readable description of what kind of damage this was, e.g. "Status Effect (Burning)"
/// -- see StatusEffectDamageType.Describe for the status-effect case. Free text rather than an
/// enum since damage sources are expected to vary widely (status effects, eventually melee/
/// spells) and this exists purely for logging, not for any gameplay branching.
/// </param>
public readonly record struct EntityDamaged(int EntityId, short Amount, StatusEffectSource Source, short CurrentHealth, short MaximumHealth, string DamageType);
