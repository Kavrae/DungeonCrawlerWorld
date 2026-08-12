using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Actions;
using Game.World;

namespace Game.Modules.Achievements;

/// <summary>
/// What an IAchievementDefinition's RegisterTrigger needs: the EventBus to subscribe against,
/// a live IPlayerQuery reference (nullable, same reasoning as GameModuleContext.PlayerQuery --
/// read PlayerEntityId at event-fire time, not capture it here, since the player entity
/// doesn't exist yet while modules are being configured), the ComponentManager (for conditions
/// that need to read a component's data rather than just an event's own fields -- e.g.
/// EarlyAdopterAchievement reading CrawlerComponent off an event that carries no data itself),
/// the ActionCatalog (for conditions that need to inspect the activated action's own data,
/// e.g. SpellCasterAchievement checking ActionDefinition.Tags), and the unlock callback bound
/// to this specific achievement's identity by AchievementModule.
/// </summary>
public sealed class AchievementTriggerContext(EventBus eventBus, IPlayerQuery? playerQuery, ComponentManager componentManager, ActionCatalog actionCatalog, Action<int> unlock)
{
    public EventBus EventBus { get; } = eventBus;

    public IPlayerQuery? PlayerQuery { get; } = playerQuery;

    public ComponentManager ComponentManager { get; } = componentManager;

    public ActionCatalog Actions { get; } = actionCatalog;

    /// <summary>
    /// Subscribes a handler for TEvent that unlocks the achievement for the player -- the only
    /// entity an achievement can ever be earned by -- the first time condition (if given)
    /// returns true, then unsubscribes itself, so the achievement is earned exactly once and
    /// stops costing anything to evaluate afterward. Omitting condition means "unlock
    /// unconditionally the moment TEvent fires" (e.g. LonerAchievement, UnarmedCombatAchievement);
    /// InflictedDamageAchievement is the one definition that needs it, to check the event's own
    /// data (who dealt/received the damage) before unlocking. A cumulative achievement (e.g. a
    /// future kill counter) can still track state across calls inside its own condition closure,
    /// only returning true once its threshold is reached.
    ///
    /// Never subscribes at all when PlayerQuery is null -- there's no player to unlock this
    /// for -- rather than subscribing a handler that would just no-op forever.
    /// </summary>
    public void SubscribeUntilUnlocked<TEvent>(Func<TEvent, bool>? condition = null)
    {
        if (PlayerQuery is not { } playerQuery)
        {
            return;
        }

        Action<TEvent>? handler = null;
        handler = eventData =>
        {
            if (condition is not null && !condition(eventData))
            {
                return;
            }

            EventBus.Unsubscribe(handler!);
            unlock(playerQuery.PlayerEntityId);
        };

        EventBus.Subscribe(handler);
    }
}
