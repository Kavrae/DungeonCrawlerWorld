using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for entering the dungeon with an empty inventory.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EmptyPocketsAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000007");

    public string Name => "Empty Pockets";

    public string RequirementText => "Entered the dungeon with an empty inventory.";

    public string Description => "You didn't bring any supplies. None. You know you still gotta eat, right?";

    public Lootbox? Lootbox => new(LootboxRarity.Bronze, "Adventurer");

    public string RewardText => "";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilTriggered<EnteredDungeonEvent>();
}
