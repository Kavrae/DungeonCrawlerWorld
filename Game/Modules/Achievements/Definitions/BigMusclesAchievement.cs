using Game.Modules.AbilityScores;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded the first time the player's base Strength reaches 100 -- base only, ignoring any
/// StatModifierComponent-driven Total (see AbilityScoreComponent's own doc comment for the
/// base/Total split). Nothing raises a base score past character-creation values yet (the
/// future level-up and "item of divine suffering" features, see TODO.md), so this can't unlock
/// today -- it starts working the moment either calls AbilityScoreEffects.SetBaseValue, which
/// is what actually publishes AbilityScoreBaseValueChangedEvent.
/// </summary>
public sealed class BigMusclesAchievement : IAchievementDefinition
{
    private const short RequiredBaseValue = 100;

    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-00000000000a");

    public string Name => "What big muscles you have!";

    public string RequirementText => "Reached a base Strength of 100.";

    public string Description =>
        "Your base strength is now over 100. Rawr! Get thee to Chippendales!";

    /// <summary>Intended reward: 3 upgrade choices (see TODO.md's Achievement content backlog) -- the ability-score upgrade-choice system doesn't exist yet, so there's nothing to grant beyond the notification itself.</summary>
    public LootboxReward? Lootbox => null;

    public string RewardText => "You've received an upgrade!";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<AbilityScoreBaseValueChangedEvent>(changed =>
            changed.EntityId == context.PlayerQuery!.PlayerEntityId
            && changed.Type == AbilityScoreType.Strength
            && changed.NewBaseValue >= RequiredBaseValue);
}
