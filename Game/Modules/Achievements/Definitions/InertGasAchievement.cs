using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded the first time the player paralyzes a Phasing entity (e.g. a Ghost) -- the concrete,
/// player-visible proof that Paralysis (an ActionLock-based effect with no HealthComponent
/// involvement) applies correctly to an incorporeal target, not just a normal Blocking one.
/// Reads NonBlockingComponent off StatusEffectAppliedEvent.EntityId via AchievementTriggerContext.
/// ComponentManager the same way EarlyAdopterAchievement reads CrawlerComponent off an event
/// that carries no Phasing data itself.
/// </summary>
public sealed class InertGasAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000005");

    public string Name => "Inert Gas";

    public string RequirementText => "Applied Paralysis to an incorporeal entity.";

    public string Description =>
        "You paralyzed an incorporeal entity! I'm... not sure what that even means. There's no muscles or skeleton. No nervous system. What exactly did you paralyze? Ah fuck it, it works. It's sitting around doing nothing like a spooky cloud. Now what?";

    public LootboxReward? Lootbox => null;

    public string RewardText => "I'm not even sure what to give you for this. I'll figure it out later.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<StatusEffectAppliedEvent>(applied =>
            applied.EffectType == StatusEffectType.Paralysis
            && applied.Source.Kind == StatusEffectSourceKind.Entity
            && applied.Source.EntityId == context.PlayerQuery!.PlayerEntityId
            && applied.EntityId != context.PlayerQuery.PlayerEntityId
            && (NonBlockingQueries.CombinedKind(context.ComponentManager.GetMultiPool<NonBlockingComponent>(), applied.EntityId) & NonBlockingKind.Phasing) != 0);
}
