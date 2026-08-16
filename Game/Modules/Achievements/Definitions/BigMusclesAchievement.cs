using Game.Modules.AbilityScores;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Rewarded for reaching a base Strength of 100.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class BigMusclesAchievement : IAchievementDefinition
{
    private const short RequiredBaseValue = 100;

    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-00000000000a");

    public string Name => "What big muscles you have!";

    public string RequirementText => "Reached a base Strength of 100.";

    public string Description =>
        "Your base strength is now over 100. Rawr! Get thee to Chippendales!";

    /// <summary>Intended reward: 3 upgrade choices (see TODO.md's Achievement content backlog) -- the ability-score upgrade-choice system doesn't exist yet, so there's nothing to grant beyond the notification itself.</summary>
    public Lootbox? Lootbox => null;

    public string RewardText => "You've received an upgrade!";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<AbilityScoreBaseValueChangedEvent>(changed =>
            changed.EntityId == context.PlayerQuery!.PlayerEntityId
            && changed.Type == AbilityScoreType.Strength
            && changed.NewBaseValue >= RequiredBaseValue);
}
