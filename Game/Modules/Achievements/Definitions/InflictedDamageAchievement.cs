using Game.Modules.Lootboxes;
using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for inflicting damage on a non-player entity.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class InflictedDamageAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000002");

    public string Name => "You've Inflicted Damage on a Mob";

    public string RequirementText => "Dealt damage to an NPC.";

    public string Description => "You've inflicted damage on a mob. Hopefully it won't hit back!";

    public LootboxReward? Lootbox => null;

    public string RewardText => "It's probably going to hit back.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<EntityDamagedEvent>(entityDamaged =>
            entityDamaged.Source.IsEntity(context.PlayerQuery!.PlayerEntityKey)
            && entityDamaged.EntityId != context.PlayerQuery.PlayerEntityId);
}
