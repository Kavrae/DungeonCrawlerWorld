using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Modules;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.Modules.Achievements.Systems;
using Game.Modules.Lootboxes;
using Game.Notifications;

namespace Game.Modules.Achievements;

/// <summary> Registers the built-in achievement definition into the AchievementCatalog and wires each one's trigger to the EventBus.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class AchievementModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000010");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [LootboxModule.ModuleId];

    private static readonly IReadOnlyList<IAchievementDefinition> Definitions = [
        new AngelInvestorAchievement(),
        new ArchivistAchievement(),
        new BigMusclesAchievement(),
        new DrinkingProblemAchievement(),
        new EarlyAdopterAchievement(),
        new EmptyPocketsAchievement(),
        new InertGasAchievement(),
        new InflictedDamageAchievement(),
        new KilledAMobAchievement(),
        new KillerQueenAchievement(),
        new LonerAchievement(),
        new MinMaxerAchievement(),
        new MostBoringLibrarianAchievement(),
        new RevengeOfTheNerdsAchievement(),
        new ShanghaiKidAchievement(),
        new SpellCasterAchievement(),
        new UnarmedCombatAchievement(),
        new UnbreakableAchievement(),
        new ObsessiveCollectorAchievement()
        ];

    /// <summary>Sets the achievement data dependencies and registers all built-in achievements with the achievement catalog</summary>
    public void Configure(GameModuleContext context)
    {
        foreach (var definition in Definitions)
        {
            context.Achievements.Register(definition);
        }
    }

    /// <summary>Registers the AchievementUnlockedComponent and UnclaimedAchievementLootboxComponent multi pools</summary>
    /// <remarks>
    /// Player-only today. initialCapacity
    /// tracks Definitions.Count directly instead of a guessed constant, so it never goes stale as achievements are added.
    /// </remarks>
    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterMultiPool<AchievementUnlockedComponent>(initialCapacity: Definitions.Count);
        componentManager.RegisterMultiPool<UnclaimedAchievementLootboxComponent>(initialCapacity: Definitions.Count);
    }

    /// <summary>Wires every achievement's trigger, the polling system the standing-state ones need, and the loot box claim.</summary>
    /// <remarks>
    /// Almost every trigger is a plain EventBus subscription. An achievement whose condition is a standing state rather
    /// than an event (see AchievementTriggerContext.SubscribePolled) needs a per-frame check instead, so the triggers
    /// register first and decide whether AchievementPollingSystem exists at all.
    /// </remarks>
    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        var unlockedAchievements = componentManager.GetMultiPool<AchievementUnlockedComponent>();
        var unclaimedLootboxes = componentManager.GetMultiPool<UnclaimedAchievementLootboxComponent>();

        List<Func<bool>> polledConditions = [];
        foreach (var definition in Definitions)
        {
            var triggerContext = new AchievementTriggerContext(context.EventBus, context.PlayerQuery, componentManager, context.Actions, context.Items, entityId => Unlock(definition, entityId, componentManager, unlockedAchievements, unclaimedLootboxes, context.Lootboxes, context.EventBus), polledConditions);
            definition.RegisterTrigger(triggerContext);
        }

        if (polledConditions.Count > 0)
        {
            systemManager.Register(new AchievementPollingSystem(polledConditions));
        }

        // An achievement's loot box is granted when the player closes its notification, not when it unlocks.
        var achievementLootboxClaims = new AchievementLootboxClaims(componentManager, context.Lootboxes, context.EventBus);
        context.EventBus.Subscribe<AchievementNotificationDismissedEvent>(dismissed =>
            achievementLootboxClaims.TryClaim(context.PlayerQuery.PlayerEntityId, dismissed.AchievementId));
    }

    private static void Unlock(IAchievementDefinition definition, int entityId, ComponentManager componentManager, MultiComponentPool<AchievementUnlockedComponent> unlockedAchievements, MultiComponentPool<UnclaimedAchievementLootboxComponent> unclaimedLootboxes, LootboxCatalog lootboxCatalog, EventBus eventBus)
    {
        if (AchievementQueries.HasEarned(unlockedAchievements, entityId, definition.Id))
        {
            return;
        }

        unlockedAchievements.Add(entityId, new AchievementUnlockedComponent(definition.Id, DateTime.UtcNow.Ticks));
        definition.ApplyReward(componentManager, entityId);

        if (definition.Lootbox is { } lootbox)
        {
            unclaimedLootboxes.Add(entityId, new UnclaimedAchievementLootboxComponent(definition.Id, lootbox));
        }

        eventBus.Publish(new NotificationRequestedEvent(
            NotificationCategory.Achievement,
            definition.Description,
            ShowImmediately: false,
            Title: definition.Name,
            Achievement: new AchievementNotificationDetails(definition.Id, definition.RequirementText, definition.Lootbox is { } reward ? lootboxCatalog.DisplayName(reward.Kind) : null, definition.RewardText)));
    }
}
