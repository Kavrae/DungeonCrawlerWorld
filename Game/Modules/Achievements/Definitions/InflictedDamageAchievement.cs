using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded the first time the player deals damage to a non-player entity. Relies on
/// HealthDamage.Apply publishing EntityDamaged when the player is the damage source, not just
/// when the player is the entity damaged -- see EntityDamaged's own doc comment.
/// </summary>
public sealed class InflictedDamageAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000002");

    public string Name => "You've Inflicted Damage on a Mob";

    public string RequirementText => "Dealt damage to an NPC.";

    public string Description =>
        "You've inflicted damage on a mob. Hopefully it won't hit back!";

    public LootboxReward? Lootbox => null;

    public string RewardText => "It's probably going to hit back.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<EntityDamaged>(entityDamaged =>
            context.PlayerQuery is { } playerQuery
            && entityDamaged.Source.Kind == StatusEffectSourceKind.Entity
            && entityDamaged.Source.EntityId == playerQuery.PlayerEntityId
            && entityDamaged.EntityId != playerQuery.PlayerEntityId
                ? playerQuery.PlayerEntityId
                : (int?)null);
}
