using Game.Modules.AbilityScores;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for reaching a base Charisma of 100.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class KillerQueenAchievement : IAchievementDefinition
{
    private const short RequiredBaseValue = 100;

    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-00000000000e");

    public string Name => "Killer Queen";

    public string RequirementText => "Reached a base Charisma of 100.";

    public string Description => "Your base charisma is now over 100. You're like Freddy Mercury, but with battle scars.";

    /// <summary>Intended reward: 3 upgrade choices (see TODO.md's Achievement content backlog) -- the ability-score upgrade-choice system doesn't exist yet, so there's nothing to grant beyond the notification itself.</summary>
    public Lootbox? Lootbox => null;

    public string RewardText => "You've received an upgrade!";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<AbilityScoreBaseValueChangedEvent>(changed =>
            changed.EntityId == context.PlayerQuery!.PlayerEntityId
            && changed.Type == AbilityScoreType.Charisma
            && changed.NewBaseValue >= RequiredBaseValue);
}
