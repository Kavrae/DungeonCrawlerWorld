using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for entering the dungeon without a weapon.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class UnarmedCombatAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000003");

    public string Name => "Unarmed Combat";

    public string RequirementText => "Entered the dungeon without a weapon.";

    public string Description =>
        "So. You just gonna waltz right into something called a “World Dungeon” and you’re not even going to bring a weapon? You’re either braver than you look, or you’re just an idiot. Good luck with that, Van Damme.";

    public Lootbox? Lootbox => new(LootboxRarity.Bronze, "Weapon");

    public string RewardText => "You've received a bronze weapon box!";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilTriggered<EnteredDungeonEvent>();
}
