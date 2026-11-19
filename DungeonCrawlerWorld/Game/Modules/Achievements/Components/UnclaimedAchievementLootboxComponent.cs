using Engine.ECS.Properties;
using Game.Modules.Lootboxes;

namespace Game.Modules.Achievements.Components;

/// <summary>A loot box an unlocked achievement owes the player, waiting for the player to close that achievement's notification.</summary>
/// <param name="achievementId">The achievement that owes the box.</param>
/// <param name="reward">The box, as the achievement declared it when it unlocked.</param>
/// <cleanupVersion>1</cleanupVersion>
[DescribedComponent]
public readonly partial struct UnclaimedAchievementLootboxComponent(Guid achievementId, LootboxReward reward)
{
    [PropertyMeaning(GamePropertyMeanings.AchievementId)]
    [ReadOnlyProperty]
    public Guid AchievementId { get; init; } = achievementId;
    [ReadOnlyProperty]
    public LootboxReward Reward { get; init; } = reward;
}
