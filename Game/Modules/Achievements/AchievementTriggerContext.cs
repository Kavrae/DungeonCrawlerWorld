using Engine.Events;
using Game.World;

namespace Game.Modules.Achievements;

/// <summary>
/// What an IAchievementDefinition's RegisterTrigger needs: the EventBus to subscribe against,
/// a live IPlayerQuery reference (nullable, same reasoning as GameModuleContext.PlayerQuery --
/// read PlayerEntityId at event-fire time, not capture it here, since the player entity
/// doesn't exist yet while modules are being configured), and the unlock callback bound to
/// this specific achievement's identity by AchievementModule.
/// </summary>
public sealed class AchievementTriggerContext(EventBus eventBus, IPlayerQuery? playerQuery, Action<int> unlock)
{
    public EventBus EventBus { get; } = eventBus;

    public IPlayerQuery? PlayerQuery { get; } = playerQuery;

    /// <summary>
    /// Subscribes a handler for TEvent that calls tryMatch on every occurrence; once tryMatch
    /// returns a non-null entity id (the achievement's condition is satisfied), this unlocks
    /// that entity's achievement and unsubscribes itself -- the achievement is earned exactly
    /// once and stops costing anything to evaluate afterward. A cumulative achievement (e.g. a
    /// future kill counter) can still track state across calls inside its own tryMatch closure,
    /// only returning non-null once its threshold is reached.
    /// </summary>
    public void SubscribeUntilUnlocked<TEvent>(Func<TEvent, int?> tryMatch)
    {
        ArgumentNullException.ThrowIfNull(tryMatch);

        Action<TEvent>? handler = null;
        handler = eventData =>
        {
            if (tryMatch(eventData) is not { } entityId)
            {
                return;
            }

            EventBus.Unsubscribe(handler!);
            unlock(entityId);
        };

        EventBus.Subscribe(handler);
    }
}
