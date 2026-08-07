using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded for entering the dungeon without a human companion. No companion/party system or
/// Human race exists yet (see TODO.md's Achievement content backlog), so a companion literally
/// cannot exist today -- this unlocks unconditionally on EnteredDungeonEvent, which GameLoop
/// publishes only after World.PlayerEntityId is already assigned, so reading
/// IPlayerQuery.PlayerEntityId here is safe (unlike the EntityMovedEvent spawn-sentinel this used to
/// rely on, which fired before that assignment).
///
/// Revisit the condition itself once a real companion/party concept exists: it should then
/// actually check for a Human-race companion near the player at spawn instead of always
/// succeeding.
/// </summary>
public sealed class LonerAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000001");

    public string Name => "Loner";

    public string RequirementText => "Entered the dungeon without a human companion.";

    public string Description =>
        "You entered the dungeon without any human companions. Didn't anyone teach you there is safety in numbers?";

    public LootboxReward? Lootbox => null;

    public string RewardText => "None! Haha. You are so dead.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<EnteredDungeonEvent>();
}
