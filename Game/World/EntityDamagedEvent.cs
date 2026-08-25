namespace Game.World;

/// <summary>
/// Published by whatever dealt damage (see HealthDamage.Apply) after CurrentHealth has already
/// been clamped and updated. Only published when the player is involved -- either as the entity
/// damaged or as the source that dealt the damage -- not yet for arbitrary NPC-vs-NPC damage;
/// see TODO.md's "Debug/event logging with levels" item for the plan to generalize this fully.
/// </summary>
/// <param name="DamageType">
/// Human-readable description of what kind of damage this was, e.g. "Status Effect (Burning)"
/// -- see StatusEffectDamageType.Describe for the status-effect case. Free text rather than an
/// enum since damage sources are expected to vary widely (status effects, eventually melee/
/// spells) and this exists purely for logging, not for any gameplay branching.
/// </param>
/// <param name="Amount">The damage actually dealt, after the target's own IncomingDamage modifiers (e.g. a flat damage-reduction buff) already reduced it -- not the raw amount the source attempted.</param>
/// <param name="MaximumHealth">The modifier-adjusted effective max (see StatModifierMath), not the raw stored SimpleHealthComponent field -- otherwise a buffed CurrentHealth could legitimately exceed the value logged here.</param>
public readonly record struct EntityDamagedEvent(int EntityId, ushort Amount, StatusEffectSource Source, ushort CurrentHealth, ushort MaximumHealth, string DamageType);
