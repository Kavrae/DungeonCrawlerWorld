namespace Game.Modules.StatModifiers;

/// <summary>
/// Published by StatModifierExpirySystem whenever it removes a modifier that reached 0
/// RemainingDurationFrames -- generic, like every other EventBus event (see its own doc
/// comment), so any module can react to a modifier wearing off without StatModifiers needing
/// to know who's listening. AbilityScoresModule is the first subscriber, keeping
/// AbilityScoreComponent.Total in sync when a temporary ability-score buff/debuff expires.
/// </summary>
public readonly record struct StatModifierExpiredEvent(int EntityId, StatModifierTarget Target);
