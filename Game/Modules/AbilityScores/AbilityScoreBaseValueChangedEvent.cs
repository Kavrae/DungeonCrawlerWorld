namespace Game.Modules.AbilityScores;

/// <summary>
/// Published by AbilityScoreEffects.SetBaseValue whenever an entity's AbilityScoreComponent.BaseValue
/// itself changes (as opposed to GrantModifier, which only ever changes Total) -- generic, like
/// every other EventBus event (see StatModifierExpiredEvent's own doc comment), so any module
/// can react to a permanent base-score change without AbilityScores needing to know who's
/// listening. Nothing calls SetBaseValue yet -- it exists as the hook the future level-up and
/// "item of divine suffering" features (see TODO.md) will call into -- but the base-score
/// milestone achievements (Game/Modules/Achievements/Definitions/) already subscribe to this so
/// they start working the moment either feature lands.
/// </summary>
public readonly record struct AbilityScoreBaseValueChangedEvent(int EntityId, AbilityScoreType Type, ushort NewBaseValue);
