using Engine.ECS.Components;
using Game.Effects;
using Engine.Events;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.GameTests.Modules.Actions;

[TestClass]
public sealed class ScrollMasteryEffectsTests
{
    private const int EntityId = 1;
    private static readonly Guid SpellId = Guid.NewGuid();

    private static (ComponentManager ComponentManager, EventBus EventBus, ActionCatalog ActionCatalog) Build()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            SpellId, "Test Spell", null, "e", Color.Red, Tags: [], Effects: [Effect.None],
            Activator: new SpellActivator(new TargetingSpec(TargetShape.Self, MaximumRange: 0, Anchor: TargetAnchor.Caster), new ActionTiming(ActionTimingCategory.Immediate, 30, null))));
        return (componentManager, new EventBus(), actionCatalog);
    }

    private static bool HasActionInstance(ComponentManager componentManager, int entityId, Guid actionId)
    {
        var pool = componentManager.GetMultiPool<ActionInstanceComponent>();
        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            if (pool.GetReadonlyByDenseIndex(denseIndex).ActionId == actionId)
            {
                return true;
            }
        }

        return false;
    }

    [TestMethod]
    public void RecordUsage_BelowThreshold_DoesNotGrantSpell()
    {
        var (componentManager, eventBus, actionCatalog) = Build();

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold - 1; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, EntityId, SpellId);
        }

        Assert.IsFalse(HasActionInstance(componentManager, EntityId, SpellId));
    }

    [TestMethod]
    public void RecordUsage_ReachesThreshold_GrantsTheSpellAndPublishesOnce()
    {
        var (componentManager, eventBus, actionCatalog) = Build();
        var masteredCount = 0;
        eventBus.Subscribe<ScrollMasteredEvent>(_ => masteredCount++);

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, EntityId, SpellId);
        }

        Assert.IsTrue(HasActionInstance(componentManager, EntityId, SpellId));
        Assert.AreEqual(1, masteredCount);
    }

    [TestMethod]
    public void RecordUsage_ReachesThreshold_RegistersNothingNew()
    {
        var (componentManager, eventBus, actionCatalog) = Build();
        var countBefore = actionCatalog.Count;

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, EntityId, SpellId);
        }

        Assert.AreEqual(countBefore, actionCatalog.Count);
    }

    [TestMethod]
    public void RecordUsage_PastThreshold_DoesNotPublishAgain()
    {
        var (componentManager, eventBus, actionCatalog) = Build();

        var masteredCount = 0;
        eventBus.Subscribe<ScrollMasteredEvent>(_ => masteredCount++);

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold + 5; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, EntityId, SpellId);
        }

        Assert.AreEqual(1, masteredCount);
    }

    [TestMethod]
    public void RecordUsage_DifferentSpellIds_TrackIndependentCounts()
    {
        var (componentManager, eventBus, actionCatalog) = Build();
        var otherSpellId = Guid.NewGuid();

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold - 1; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, EntityId, SpellId);
        }

        ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, EntityId, otherSpellId);

        Assert.IsFalse(HasActionInstance(componentManager, EntityId, SpellId));
        Assert.IsFalse(HasActionInstance(componentManager, EntityId, otherSpellId));
    }
}
