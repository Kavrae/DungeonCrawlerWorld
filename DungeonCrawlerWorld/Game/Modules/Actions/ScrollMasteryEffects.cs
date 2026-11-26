using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Actions.Components;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>
/// Records one scroll activation toward mastering the spell it casts (ScrollActivator.Spell), and -- once
/// MasteryThreshold is reached -- permanently teaches that spell, the same definition the scroll cast: every
/// scroll binds a spell that exists, so nothing is made up here. Fires the spell grant itself (not a separate
/// achievement reward) -- mastering Scroll-of-A and Scroll-of-B are two independent, repeatable crossings, each
/// keyed by its own spell.
/// </summary>
public static class ScrollMasteryEffects
{
    /// <summary>
    /// Flat for every scroll today. TODO: scale with the power of the spell/effect being taught
    /// (a cheap effect shouldn't take as long to master as a strong one) -- blocked on Action
    /// Effects having some form of power-scaling concept, which doesn't exist yet.
    /// </summary>
    public const int MasteryThreshold = 200;

    /// <param name="spellId">The spell the scroll cast (ScrollActivator.Spell's Id), which the catalog holds.</param>
    public static void RecordUsage(ComponentManager componentManager, EventBus eventBus, ActionCatalog actionCatalog, int entityId, Guid spellId)
    {
        var pool = componentManager.GetMultiPool<ScrollMasteryComponent>();

        bool counted;
        using (var heldMastery = pool.TryWriteFirst(entityId, new SpellMatcher(spellId)))
        {
            counted = heldMastery.Found;
            if (counted)
            {
                heldMastery.Value.UsageCount++;
            }
        }

        if (!counted)
        {
            pool.Add(entityId, new ScrollMasteryComponent(spellId, 1));
        }

        if (!TryGetUsageCount(pool, entityId, spellId, out var usageCount) || usageCount != MasteryThreshold)
        {
            return;
        }

        ActionGrantEffects.Grant(componentManager, actionCatalog, entityId, spellId, overrideDefinition: null);
        eventBus.Publish(new ScrollMasteredEvent(entityId, spellId));
    }

    private static bool TryGetUsageCount(MultiComponentPool<ScrollMasteryComponent> pool, int entityId, Guid spellId, out int usageCount)
    {
        var found = pool.TryGetFirst(entityId, new SpellMatcher(spellId), out var mastery);
        usageCount = found ? mastery.UsageCount : 0;
        return found;
    }

    private readonly struct SpellMatcher(Guid spellId) : IComponentMatcher<ScrollMasteryComponent>
    {
        public bool Matches(in ScrollMasteryComponent mastery) => mastery.SpellId == spellId;
    }
}
