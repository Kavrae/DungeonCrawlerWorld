using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for reaching the maximum base value in all core ability scores.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MinMaxerAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-00000000000f");

    public string Name => "Min-Maxer";

    public string RequirementText => "Reached a base score of 300 in Strength, Constitution, Dexterity, Intelligence, and Charisma.";

    public string Description => "Well.  You did it. You got every ability score to 300.  You're a walking statistical anomaly and, you know what, I'll say it. I'll bet you cheated. Didn't you? You found some little exploit and cheesed the hell out of it.  You cheated your way all the way to the top. And I couldn't be more proud.";

    /// <summary>Intended reward: 3 upgrade choices (see TODO.md's Achievement content backlog) -- the ability-score upgrade-choice system doesn't exist yet, so there's nothing to grant beyond the notification itself.</summary>
    public Lootbox? Lootbox => null;

    public string RewardText => "You've received an upgrade!";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<AbilityScoreBaseValueChangedEvent>(changed =>
            changed.EntityId == context.PlayerQuery!.PlayerEntityId
            && AllCoreScoresAtCap(context));

    private static bool AllCoreScoresAtCap(AchievementTriggerContext context)
    {
        var abilityScores = context.ComponentManager.GetMultiPool<AbilityScoreComponent>();
        var playerEntityId = context.PlayerQuery!.PlayerEntityId;

        foreach (var type in Enum.GetValues<AbilityScoreType>())
        {
            if (AbilityScoreCategory.IsHidden(type))
            {
                continue;
            }

            if (!AbilityScoreQueries.TryGetComponent(abilityScores, playerEntityId, type, out var component)
                || component.BaseValue < AbilityScoreMath.MaximumBaseValue)
            {
                return false;
            }
        }

        return true;
    }
}
