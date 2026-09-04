using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.World;

namespace Game.Modules.Achievements;

/// <summary>Provides context for triggering achievement unlock conditions.</summary>
/// <param name="eventBus">The event bus to subscribe to.</param>
/// <param name="playerQuery">The query for retrieving player information.</param>
/// <param name="componentManager">The component manager.</param>
/// <param name="actionCatalog">The action catalog.</param>
/// <param name="itemCatalog">The item catalog.</param>
/// <param name="unlock">The unlock callback.</param>
/// <param name="polledConditions">Shared list SubscribePolled appends to -- owned and drained each frame by AchievementModule's own AchievementPollingSystem.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed class AchievementTriggerContext(EventBus eventBus, IPlayerQuery? playerQuery, ComponentManager componentManager, ActionCatalog actionCatalog, ItemCatalog itemCatalog, Action<int> unlock, List<Func<bool>> polledConditions)
{
    public EventBus EventBus { get; } = eventBus;

    public IPlayerQuery? PlayerQuery { get; } = playerQuery;

    public ComponentManager ComponentManager { get; } = componentManager;

    public ActionCatalog Actions { get; } = actionCatalog;

    public ItemCatalog Items { get; } = itemCatalog;

    /// <summary>Subscribes to unlock the achievement for the player the first time condition (if given) matches TEvent.</summary>
    /// <remarks>Backed by EventBus.SubscribeOnce, which stays subscribed across repeated firings of TEvent until condition passes -- correct for an event that can fire many times over a session (most achievement triggers), where a failed check now doesn't rule out success later. For an event guaranteed to fire at most once per session, use SubscribeUntilTriggered instead.</remarks>
    /// <typeparam name="TEvent"> The type of event to subscribe to.</typeparam>
    /// <param name="condition">The optional condition to check before unlocking the achievement.</param>
    public void SubscribeUntilUnlocked<TEvent>(Func<TEvent, bool>? condition = null)
    {
        if (PlayerQuery is not { } playerQuery)
        {
            return;
        }

        EventBus.SubscribeOnce<TEvent>(_ => unlock(playerQuery.PlayerEntityId), condition);
    }

    /// <summary>Subscribes to unlock the achievement for the player the first time condition (if given) matches TEvent. Always unsubscribes on the first trigger.</summary>
    /// <typeparam name="TEvent">The type of event to subscribe to.</typeparam>
    /// <param name="condition">The optional condition to check when the event fires.</param>
    public void SubscribeUntilTriggered<TEvent>(Func<TEvent, bool>? condition = null)
    {
        if (PlayerQuery is not { } playerQuery)
        {
            return;
        }

        Action<TEvent>? handler = null;
        handler = eventData =>
        {
            EventBus.Unsubscribe(handler!);

            if (condition is null || condition(eventData))
            {
                unlock(playerQuery.PlayerEntityId);
            }
        };

        EventBus.Subscribe(handler);
    }

    /// <summary>
    /// Registers a per-frame polled condition instead of an event subscription -- for an achievement
    /// whose unlock condition is a standing state (e.g. "does any inventory stack currently hold at
    /// least N") rather than a discrete occurrence, where no event exists to hang a Subscribe off
    /// of. Checked every frame by AchievementModule's AchievementPollingSystem until it returns
    /// true once, at which point it unlocks and is removed from the poll list.
    /// </summary>
    /// <param name="condition">Re-evaluated every frame; returning true unlocks the achievement.</param>
    public void SubscribePolled(Func<bool> condition)
    {
        if (PlayerQuery is not { } playerQuery)
        {
            return;
        }

        polledConditions.Add(() =>
        {
            if (!condition())
            {
                return false;
            }

            unlock(playerQuery.PlayerEntityId);
            return true;
        });
    }
}
