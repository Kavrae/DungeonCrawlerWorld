using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Math;
using Engine.Tiles;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class SpellGrantTests
{
    private const int LearnerEntityId = 3;
    private const long Now = 100;

    private static ActionDefinition Spell(Guid id, string name = "Ember Lance") => new(
        id, name, null, "e", Color.OrangeRed, Tags: [],
        Effects: [new Effect([new DirectDamage(4, 6)])],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 6), new ActionTiming(ActionTimingCategory.Immediate)),
        Description: "A spear of flame.");

    private static (EffectServices Services, ComponentManager ComponentManager) Build(ActionCatalog actions)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 16, initialComponentCapacity: 16));
        return (TestActionEffects.Services(componentManager, new EntityKeys(), new EventBus(), new MathUtility(new SeededRandom((ulong)1)), actions: actions), componentManager);
    }

    private static EffectContext ContextFor(EffectServices services, ActionDefinition used) =>
        EffectContext.FromEntity(services, LearnerEntityId, LearnerEntityId, used, Now);

    private static List<ActionInstanceComponent> InstancesOf(ComponentManager componentManager)
    {
        var instances = new List<ActionInstanceComponent>();
        var pool = componentManager.GetMultiPool<ActionInstanceComponent>();
        for (var denseIndex = pool.GetFirstDenseIndex(LearnerEntityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            instances.Add(pool.GetReadonlyByDenseIndex(denseIndex));
        }

        return instances;
    }

    [TestMethod]
    public void ASpellMadeAtRuntime_IsTaughtAsTheLearnersOwnOverride_AndRegisteredNowhere()
    {
        var actions = new ActionCatalog();
        var (services, componentManager) = Build(actions);
        var spell = Spell(Guid.NewGuid());
        var grant = new SpellGrant(spell);

        Assert.AreEqual(EffectOutcome.Applied, grant.Apply(ContextFor(services, spell)));

        var instance = InstancesOf(componentManager).Single();
        Assert.AreEqual(spell.Id, instance.ActionId);
        Assert.AreSame(spell, instance.Override);
        Assert.IsFalse(actions.TryGet(spell.Id, out _));
        Assert.IsTrue(services.Actions.TryGetEffectiveAction(LearnerEntityId, spell.Id, out var learned));
        Assert.AreSame(spell, learned);
    }

    [TestMethod]
    public void ASpellTheCatalogHolds_IsTaughtByIdWithNoOverride()
    {
        var actions = new ActionCatalog();
        var spell = Spell(Guid.NewGuid());
        actions.Register(spell);
        var (services, componentManager) = Build(actions);

        new SpellGrant(spell).Apply(ContextFor(services, spell));

        var instance = InstancesOf(componentManager).Single();
        Assert.AreEqual(spell.Id, instance.ActionId);
        Assert.IsNull(instance.Override);
    }

    [TestMethod]
    public void ASpellDifferingFromTheCatalogsUnderItsGuid_IsTaughtAsItIs()
    {
        var actions = new ActionCatalog();
        var catalogSpell = Spell(Guid.NewGuid());
        actions.Register(catalogSpell);
        var (services, componentManager) = Build(actions);
        var changedSpell = catalogSpell with { Name = "Ember Lance, Revised" };

        new SpellGrant(changedSpell).Apply(ContextFor(services, changedSpell));

        Assert.AreSame(changedSpell, InstancesOf(componentManager).Single().Override);
    }

    [TestMethod]
    public void ASpellAlreadyKnown_IsRefused_AndNotGrantedAgain()
    {
        var (services, componentManager) = Build(new ActionCatalog());
        var spell = Spell(Guid.NewGuid());
        var grant = new SpellGrant(spell);
        grant.Apply(ContextFor(services, spell));

        var reservations = new EffectReservations();
        Assert.AreEqual(EffectRefusal.SpellAlreadyKnown, grant.CanApply(ContextFor(services, spell), ref reservations));
        Assert.AreEqual(EffectOutcome.NoEffect, grant.Apply(ContextFor(services, spell)));
        Assert.HasCount(1, InstancesOf(componentManager));
    }

    [TestMethod]
    public void ASpellNotYetKnown_CanBeApplied()
    {
        var (services, _) = Build(new ActionCatalog());
        var spell = Spell(Guid.NewGuid());

        var reservations = new EffectReservations();
        Assert.AreEqual(EffectRefusal.None, new SpellGrant(spell).CanApply(ContextFor(services, spell), ref reservations));
    }

    [TestMethod]
    public void AlreadyKnown_HasABlockerOfItsOwn()
    {
        Assert.AreEqual(ActivationBlocker.SpellAlreadyKnown, ActivationQueries.BlockerFor(EffectRefusal.SpellAlreadyKnown));
    }
}
