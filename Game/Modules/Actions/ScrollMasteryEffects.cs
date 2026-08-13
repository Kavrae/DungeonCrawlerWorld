using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>
/// Records one scroll activation toward mastering ScrollActivator.SpellId, and -- once
/// MasteryThreshold is reached -- permanently teaches that spell. Looks the spell up in
/// ActionCatalog first (an already-authored spell, e.g. Heal); if none is registered under
/// spellId yet, synthesizes a fresh ActionDefinition at runtime from the scroll's own
/// ItemDefinition (name/glyph/tags/effects/targeting/timing, wrapped in a SpellActivator) and
/// registers it, so nobody has to hand-author a matching spell for every scroll up front -- this
/// only pays off for scrolls actually mastered, which is rare by design (see MasteryThreshold).
/// Fires the spell grant itself (not a separate achievement reward) -- mastering Scroll-of-A and
/// Scroll-of-B are two independent, repeatable crossings, each keyed by its own SpellId.
/// </summary>
public static class ScrollMasteryEffects
{
    /// <summary>
    /// Flat for every scroll today. TODO: scale with the power of the spell/effect being taught
    /// (a cheap effect shouldn't take as long to master as a strong one) -- blocked on Action
    /// Effects having some form of power-scaling concept, which doesn't exist yet. The
    /// synthesized spell's placeholder ManaCost: 0 (see SynthesizeSpellFromScroll below) has the
    /// same dependency.
    /// </summary>
    public const int MasteryThreshold = 200;

    public static void RecordUsage(ComponentManager componentManager, EventBus eventBus, ActionCatalog actionCatalog, ItemDefinition scroll, int entityId, Guid spellId)
    {
        var pool = componentManager.GetMultiPool<ScrollMasteryComponent>();

        var updated = pool.TryUpdateFirst(
            entityId,
            spellId,
            static (ref readonly ScrollMasteryComponent c, Guid state) => c.SpellId == state,
            static (ref ScrollMasteryComponent c, Guid _) => c.UsageCount++);

        if (!updated)
        {
            pool.Add(entityId, new ScrollMasteryComponent(spellId, 1));
        }

        if (!TryGetUsageCount(pool, entityId, spellId, out var usageCount) || usageCount != MasteryThreshold)
        {
            return;
        }

        if (!actionCatalog.TryGet(spellId, out var action))
        {
            action = SynthesizeSpellFromScroll(scroll, spellId);
            actionCatalog.Register(action);
        }

        ActionGrantEffects.Grant(componentManager, entityId, spellId, SpellActivator.ManaCostOf(action.Activator), damageAmount: 0, cooldownFramesRemaining: 0);
        eventBus.Publish(new ScrollMasteredEvent(entityId, spellId));
    }

    /// <summary>ManaCost: 0 is a placeholder -- see MasteryThreshold's own doc comment for the same power-scaling TODO.</summary>
    private static ActionDefinition SynthesizeSpellFromScroll(ItemDefinition scroll, Guid spellId) => new(
        Id: spellId,
        Name: scroll.Name,
        SpriteName: scroll.SpriteName,
        Glyph: scroll.Glyph,
        GlyphColor: scroll.GlyphColor,
        Tags: scroll.Tags,
        Effects: scroll.Effects,
        Activator: new SpellActivator(scroll.Activator!.Targeting, scroll.Activator.Timing, ManaCost: 0),
        Description: scroll.Description,
        Summary: scroll.Summary);

    private static bool TryGetUsageCount(MultiComponentPool<ScrollMasteryComponent> pool, int entityId, Guid spellId, out int usageCount)
    {
        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            var candidate = pool.GetReadonlyByDenseIndex(denseIndex);
            if (candidate.SpellId == spellId)
            {
                usageCount = candidate.UsageCount;
                return true;
            }
        }

        usageCount = 0;
        return false;
    }
}
