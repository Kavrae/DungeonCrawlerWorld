using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for entering the dungeon without a human companion.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class LonerAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000001");

    public string Name => "Loner";

    public string RequirementText => "Entered the dungeon without a human companion.";

    public string Description => "You entered the dungeon without any human companions. Didn't anyone teach you there is safety in numbers?";

    public Lootbox? Lootbox => null;

    public string RewardText => "None! Haha. You are so dead.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilTriggered<EnteredDungeonEvent>();
}
