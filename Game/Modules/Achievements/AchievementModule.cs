using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Achievements.Components;
using Game.Modules.Achievements.Definitions;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Notifications;
using Game.World;

namespace Game.Modules.Achievements;

/// <summary> Registers the built-in achievement definition into the AchievementCatalog and wires each one's trigger to the EventBus.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class AchievementModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000010");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private static readonly IReadOnlyList<IAchievementDefinition> Definitions = [
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
        new UnbreakableAchievement()
        ];

    private EventBus? _eventBus;
    private IPlayerQuery? _playerQuery;
    private ActionCatalog? _actionCatalog;
    private ItemCatalog? _itemCatalog;

    /// <summary>Sets the achievement data dependencies and registers all built-in achievements with the achievement catalog</summary>
    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _playerQuery = context.PlayerQuery;
        _actionCatalog = context.Actions;
        _itemCatalog = context.Items;

        foreach (var definition in Definitions)
        {
            context.Achievements.Register(definition);
        }
    }

    /// <summary>Registers the AchievementUnlockedComponent multi pool</summary>
    /// <remarks>
    /// Player-only today (maximumEntityCount: 4, small headroom), so the entity-index side stays tiny. initialCapacity
    /// tracks Definitions.Count directly instead of a guessed constant, so it never goes stale as achievements are added.
    /// </remarks>
    /// <param name="componentManager"></param>
    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterMultiPool<AchievementUnlockedComponent>(maximumEntityCount: 4, initialCapacity: Definitions.Count);

    /// <remarks>
    /// This module has no per-frame work of its own.
    /// Every achievement trigger is a plain EventBus subscription. It's wired up
    /// here, not in Configure, because Configure runs before any module's RegisterComponents
    /// (see IGameModule's own doc comment), so AchievementUnlockedComponent's pool doesn't exist
    /// yet at that point. RegisterSystems is reused as the earliest hook that's guaranteed to
    /// run after every module's RegisterComponents (see Bootstrapper.Build), which is all this
    /// module actually needs -- not a genuine registration of systems.
    /// </remarks>
    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (_eventBus is not { } eventBus || _actionCatalog is not { } actionCatalog || _itemCatalog is not { } itemCatalog)
        {
            throw new InvalidOperationException("AchievementModule.Configure must run before RegisterSystems.");
        }

        var unlockedAchievements = componentManager.GetMultiPool<AchievementUnlockedComponent>();

        foreach (var definition in Definitions)
        {
            var triggerContext = new AchievementTriggerContext(eventBus, _playerQuery, componentManager, actionCatalog, itemCatalog, entityId => Unlock(definition, entityId, unlockedAchievements, eventBus));
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
