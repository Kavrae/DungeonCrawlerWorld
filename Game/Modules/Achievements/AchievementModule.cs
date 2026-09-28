using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Modules;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.Modules.Achievements.Systems;
using Game.Notifications;

namespace Game.Modules.Achievements;

/// <summary> Registers the built-in achievement definition into the AchievementCatalog and wires each one's trigger to the EventBus.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class AchievementModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000010");

    public Guid Id => ModuleId;

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

    /// <summary>Shared with every AchievementTriggerContext this module hands out -- SubscribePolled appends to it, AchievementPollingSystem (registered below only if it ends up non-empty) drains it once per frame.</summary>
    private readonly List<Func<bool>> _polledConditions = [];

    /// <summary>Sets the achievement data dependencies and registers all built-in achievements with the achievement catalog</summary>
    public void Configure(GameModuleContext context)
    {
        foreach (var definition in Definitions)
        {
            context.Achievements.Register(definition);
        }
    }

    /// <summary>Registers the AchievementUnlockedComponent multi pool</summary>
    /// <remarks>
    /// Player-only today. initialCapacity
    /// tracks Definitions.Count directly instead of a guessed constant, so it never goes stale as achievements are added.
    /// </remarks>
    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterMultiPool<AchievementUnlockedComponent>(initialCapacity: Definitions.Count);
    }

    /// <remarks>
    /// Almost every achievement trigger is a plain EventBus subscription, needing no per-frame work
    /// of its own -- wired up here, with the systems, since Configure only fills what other modules
    /// read (see IModule&lt;TContext&gt;.Configure). The one
    /// exception: an achievement whose condition is a standing state rather than a discrete event
    /// (see AchievementTriggerContext.SubscribePolled) needs an actual per-frame check, which is
    /// what AchievementPollingSystem below is for -- only registered at all if at least one
    /// achievement's RegisterTrigger actually called SubscribePolled.
    /// </remarks>
    public void RegisterSystems(SystemRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        var unlockedAchievements = componentManager.GetMultiPool<AchievementUnlockedComponent>();

        foreach (var definition in Definitions)
        {
            var triggerContext = new AchievementTriggerContext(context.EventBus, context.PlayerQuery, componentManager, context.Actions, context.Items, entityId => Unlock(definition, entityId, componentManager, unlockedAchievements, context.EventBus), _polledConditions);
            definition.RegisterTrigger(triggerContext);
        }

        if (_polledConditions.Count > 0)
        {
            systemManager.Register(new AchievementPollingSystem(_polledConditions));
        }
    }

    private static void Unlock(IAchievementDefinition definition, int entityId, ComponentManager componentManager, MultiComponentPool<AchievementUnlockedComponent> unlockedAchievements, EventBus eventBus)
    {
        if (AchievementQueries.HasEarned(unlockedAchievements, entityId, definition.Id))
        {
            return;
        }

        unlockedAchievements.Add(entityId, new AchievementUnlockedComponent(definition.Id, DateTime.UtcNow.Ticks));
        definition.ApplyReward(componentManager, entityId);

        eventBus.Publish(new NotificationRequestedEvent(
            NotificationCategory.Achievement,
            definition.Description,
            ShowImmediately: false,
            Title: definition.Name,
            Achievement: new AchievementNotificationDetails(definition.RequirementText, definition.Lootbox?.DisplayLabel, definition.RewardText)));
    }
}
