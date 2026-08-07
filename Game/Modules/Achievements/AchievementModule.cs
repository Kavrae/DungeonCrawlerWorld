using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Abilities;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.Notifications;
using Game.World;

namespace Game.Modules.Achievements;

/// <summary>
/// Registers every built-in achievement definition into the shared AchievementCatalog and
/// wires each one's trigger to the EventBus. No systems of its own -- unlocking is
/// event-driven (each definition's own RegisterTrigger, via AchievementTriggerContext), not a
/// per-frame scan. EventBus/PlayerQuery are captured in Configure (see GameModuleContext) but
/// trigger wiring itself waits for RegisterSystems, the earliest point ComponentManager
/// (needed to write AchievementUnlockedComponent and check AchievementQueries.HasEarned on
/// unlock) is available -- the same Configure-then-RegisterSystems split MovementModule uses
/// for its own deferred EntityMoveSync wiring.
/// </summary>
public sealed class AchievementModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000010");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private static readonly IReadOnlyList<IAchievementDefinition> Definitions = [
        new DrinkingProblemAchievement(),
        new EarlyAdopterAchievement(),
        new EmptyPocketsAchievement(),
        new InertGasAchievement(),
        new InflictedDamageAchievement(),
        new KilledAMobAchievement(),
        new LonerAchievement(),
        new SpellCasterAchievement(),
        new UnarmedCombatAchievement()
        ];

    private EventBus? _eventBus;
    private IPlayerQuery? _playerQuery;
    private AbilityCatalog? _abilityCatalog;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _playerQuery = context.PlayerQuery;
        _abilityCatalog = context.Abilities;

        foreach (var definition in Definitions)
        {
            context.Achievements.Register(definition);
        }
    }

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterMultiPool<AchievementUnlockedComponent>();

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (_eventBus is not { } eventBus || _abilityCatalog is not { } abilityCatalog)
        {
            throw new InvalidOperationException("AchievementModule.Configure must run before RegisterSystems.");
        }

        var unlockedAchievements = componentManager.GetMultiPool<AchievementUnlockedComponent>();

        foreach (var definition in Definitions)
        {
            var triggerContext = new AchievementTriggerContext(eventBus, _playerQuery, componentManager, abilityCatalog, entityId => Unlock(definition, entityId, unlockedAchievements, eventBus));
            definition.RegisterTrigger(triggerContext);
        }
    }

    private static void Unlock(IAchievementDefinition definition, int entityId, MultiComponentPool<AchievementUnlockedComponent> unlockedAchievements, EventBus eventBus)
    {
        if (AchievementQueries.HasEarned(unlockedAchievements, entityId, definition.Id))
        {
            return;
        }

        unlockedAchievements.Add(entityId, new AchievementUnlockedComponent(definition.Id, DateTime.UtcNow.Ticks));

        eventBus.Publish(new NotificationRequestedEvent(
            NotificationCategory.Achievement,
            definition.Description,
            ShowImmediately: false,
            Title: definition.Name,
            Achievement: new AchievementNotificationDetails(definition.RequirementText, definition.Lootbox?.DisplayLabel, definition.RewardText)));
    }
}
