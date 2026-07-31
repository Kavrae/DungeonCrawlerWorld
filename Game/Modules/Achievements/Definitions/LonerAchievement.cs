using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded for entering the dungeon without a human companion. No companion/party system or
/// Human race exists yet (see TODO.md's Achievement content backlog), so a companion literally
/// cannot exist today -- this unlocks unconditionally on the spawn-sentinel EntityMoved (see
/// EntityMoved's own doc comment: OldPosition == NewPosition marks a spawn, not a real move).
///
/// Deliberately does NOT check entityMoved.EntityId against IPlayerQuery.PlayerEntityId --
/// GameLoop only assigns World.PlayerEntityId *after* FloorBuilder.CreatePlayer returns, but
/// CreatePlayer publishes this exact spawn-sentinel event *before* returning, so
/// PlayerEntityId still reads its -1 default at the moment this handler runs; comparing
/// against it here would (and, caught in testing, did) make this achievement silently never
/// unlock. Safe to skip today only because FloorBuilder.CreatePlayer is the sole spawn path
/// using this sentinel (see EntityMoved's own doc comment: "Any future spawn path ... must do
/// the same"). Once a monster spawner also uses it, this needs a real discriminator that
/// doesn't depend on PlayerEntityId's assignment timing -- e.g. checking the entity's own
/// MovementComponent.Mode == MovementMode.PlayerControlled instead.
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
        context.SubscribeUntilUnlocked<EntityMoved>(entityMoved =>
            entityMoved.OldPosition == entityMoved.NewPosition ? entityMoved.EntityId : (int?)null);
}
