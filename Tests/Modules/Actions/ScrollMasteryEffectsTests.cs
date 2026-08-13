using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class ScrollMasteryEffectsTests
{
    private const int EntityId = 1;
    private static readonly Guid SpellId = Guid.NewGuid();

    private static (ComponentManager ComponentManager, EventBus EventBus, ActionCatalog ActionCatalog, ItemDefinition Scroll) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<ScrollMasteryComponent>();
        componentManager.RegisterMultiPool<ActionInstanceComponent>();

        var scroll = new ItemDefinition(
            Guid.NewGuid(), "Test Scroll", "Scroll", "s", Color.White,
            Tags: [Tag.Scroll, Tag.Consumable],
            Effects: [ActionEffect.None],
            Activator: new ScrollActivator(new TargetingSpec(TargetShape.Adjacent, Range: 0), new ActionTiming(ActionTimingCategory.Immediate, 30, null), SpellId));

        return (componentManager, new EventBus(), new ActionCatalog(), scroll);
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
        var (componentManager, eventBus, actionCatalog, scroll) = Build();

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold - 1; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, scroll, EntityId, SpellId);
        }

        Assert.IsFalse(HasActionInstance(componentManager, EntityId, SpellId));
    }

    [TestMethod]
    public void RecordUsage_ReachesThreshold_LooksUpExistingSpellAndGrantsIt()
    {
        var (componentManager, eventBus, actionCatalog, scroll) = Build();
        var existingSpell = new ActionDefinition(
            SpellId, "Existing Spell", null, "e", Color.Red, Tags: [], Effects: [ActionEffect.None],
            Activator: new SpellActivator(new TargetingSpec(TargetShape.Self, Range: 0), new ActionTiming(ActionTimingCategory.Immediate, 30, null), ManaCost: 0));
        actionCatalog.Register(existingSpell);

        var masteredCount = 0;
        eventBus.Subscribe<ScrollMasteredEvent>(_ => masteredCount++);

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, scroll, EntityId, SpellId);
        }

        Assert.IsTrue(HasActionInstance(componentManager, EntityId, SpellId));
        Assert.AreEqual(1, masteredCount);
    }

    [TestMethod]
    public void RecordUsage_NoExistingSpell_SynthesizesRegistersAndGrantsIt()
    {
        var (componentManager, eventBus, actionCatalog, scroll) = Build();

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, scroll, EntityId, SpellId);
        }

        Assert.IsTrue(actionCatalog.TryGet(SpellId, out var synthesized));
        Assert.AreEqual(scroll.Name, synthesized.Name);
        Assert.IsTrue(HasActionInstance(componentManager, EntityId, SpellId));
    }

    [TestMethod]
    public void RecordUsage_PastThreshold_DoesNotPublishAgain()
    {
        var (componentManager, eventBus, actionCatalog, scroll) = Build();

        var masteredCount = 0;
        eventBus.Subscribe<ScrollMasteredEvent>(_ => masteredCount++);

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold + 5; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, scroll, EntityId, SpellId);
        }

        Assert.AreEqual(1, masteredCount);
    }

    [TestMethod]
    public void RecordUsage_DifferentSpellIds_TrackIndependentCounts()
    {
        var (componentManager, eventBus, actionCatalog, scroll) = Build();
        var otherSpellId = Guid.NewGuid();

        for (var i = 0; i < ScrollMasteryEffects.MasteryThreshold - 1; i++)
        {
            ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, scroll, EntityId, SpellId);
        }

        ScrollMasteryEffects.RecordUsage(componentManager, eventBus, actionCatalog, scroll, EntityId, otherSpellId);

        Assert.IsFalse(HasActionInstance(componentManager, EntityId, SpellId));
        Assert.IsFalse(HasActionInstance(componentManager, EntityId, otherSpellId));
    }
}
