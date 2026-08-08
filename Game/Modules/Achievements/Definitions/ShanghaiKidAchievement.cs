using Game.Modules.AbilityScores;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded the first time the player's base Dexterity reaches 100 -- see BigMusclesAchievement's
/// own doc comment for why this can't unlock today and what makes it start working.
/// </summary>
public sealed class ShanghaiKidAchievement : IAchievementDefinition
{
    private const short RequiredBaseValue = 100;

    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-00000000000c");

    public string Name => "The Shanghai Kid";

    public string RequirementText => "Reached a base Dexterity of 100.";

    public string Description =>
        "Your base dexterity is now over 100. You're like a mashup of Jackie Chan and Jet Lee!  Hmmm... now there's an idea...";

    /// <summary>Intended reward: 3 upgrade choices (see TODO.md's Achievement content backlog) -- the ability-score upgrade-choice system doesn't exist yet, so there's nothing to grant beyond the notification itself.</summary>
    public LootboxReward? Lootbox => null;

    public string RewardText => "You've received an upgrade!";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<AbilityScoreBaseValueChangedEvent>(changed =>
            changed.EntityId == context.PlayerQuery!.PlayerEntityId
            && changed.Type == AbilityScoreType.Dexterity
            && changed.NewBaseValue >= RequiredBaseValue);
}
