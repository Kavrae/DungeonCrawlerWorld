using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded for entering the dungeon with an empty inventory. No Inventory system or
/// start-equipment-selection system exists yet (see TODO.md's Achievement content backlog), so
/// every player's inventory is empty today -- this unlocks unconditionally on EnteredDungeonEvent,
/// same reasoning as LonerAchievement/UnarmedCombatAchievement (see LonerAchievement's own doc
/// comment for why reading IPlayerQuery.PlayerEntityId here is safe).
///
/// Revisit the condition itself once Inventory and start-equipment selection exist: it should
/// then actually check whether the player's inventory is empty instead of always succeeding.
/// </summary>
public sealed class EmptyPocketsAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000007");

    public string Name => "Empty Pockets";

    public string RequirementText => "Entered the dungeon with an empty inventory.";

    public string Description =>
        "You didn't bring any supplies. None. You know you still gotta eat, right?";

    public LootboxReward? Lootbox => new(LootboxRarity.Bronze, "Adventurer");

    public string RewardText => "";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<EnteredDungeonEvent>();
}
