using Game.Modules.Lootboxes;

namespace Game.Modules.Achievements.Components;

/// <summary>A loot box an unlocked achievement owes the player, waiting for the player to close that achievement's notification.</summary>
/// <param name="achievementId">The achievement that owes the box.</param>
/// <param name="reward">The box, as the achievement declared it when it unlocked.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly struct UnclaimedAchievementLootboxComponent(Guid achievementId, LootboxReward reward)
{
    public Guid AchievementId { get; } = achievementId;
    public LootboxReward Reward { get; } = reward;

    public override readonly string ToString() => $"AchievementId : {AchievementId}\nReward : {Reward.Rarity} {Reward.TypeId}";
}
