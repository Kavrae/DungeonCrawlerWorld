using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for killing an NPC.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class KilledAMobAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000006");

    public string Name => "You've killed a mob!";

    public string RequirementText => "Killed an NPC.";

    public string Description => "You're a murderer! He probably had a family!";

    public Lootbox? Lootbox => null; //TODO grant the experience component

    public string RewardText => "You can now gain experience. Get enough of it, and you might even go up a level.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<EntityDiedEvent>(died =>
            died.Source.Kind == StatusEffectSourceKind.Entity
            && died.EntityId != context.PlayerQuery!.PlayerEntityId
            && died.Source.EntityId == context.PlayerQuery!.PlayerEntityId);
}
