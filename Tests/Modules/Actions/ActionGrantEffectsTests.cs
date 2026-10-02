using Engine.ECS.Components;
using Game.Effects;
using Game.Effects.Entries;
using Engine.Math;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class ActionGrantEffectsTests
{
    private static readonly Guid ActionId = Guid.NewGuid();

    private static readonly ActionDefinition TestActionDefinition = new(
        ActionId, "Test Action", null, "#", default, [],
        Effects: [new Effect([new DirectDamage(MinFlatDamage: 0, MaxFlatDamage: 0)])],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 1), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null)));

    private static ComponentManager CreateRegisteredManager()
    {
        var manager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8));
        return manager;
    }

    [TestMethod]
    public void Grant_AlwaysMergesActionInstanceComponent()
    {
        var manager = CreateRegisteredManager();
        var overrideDefinition = ActionOverrideEffects.OverrideFlatDamage(TestActionDefinition, flatDamage: 7);

        ActionGrantEffects.Grant(manager, 0, ActionId, manaCost: 0, overrideDefinition);

        Assert.IsTrue(manager.GetMultiPool<ActionInstanceComponent>().TryGetFirst(0, ActionId, static (ref readonly ActionInstanceComponent candidate, Guid id) => candidate.ActionId == id, out var instance));
        Assert.AreEqual(overrideDefinition, instance.Override);
    }

    [TestMethod]
    public void Grant_ZeroManaCost_DoesNotGrantManaComponent()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Intelligence, baseValue: 42);

        ActionGrantEffects.Grant(manager, 0, ActionId, manaCost: 0, overrideDefinition: null);

        Assert.IsFalse(manager.GetPackedPool<ManaComponent>().Has(0));
    }

    [TestMethod]
    public void Grant_NonZeroManaCost_EntityHasIntelligence_GrantsManaComponent()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Intelligence, baseValue: 42);

        ActionGrantEffects.Grant(manager, 0, ActionId, manaCost: 2, overrideDefinition: null);

        var mana = manager.GetPackedPool<ManaComponent>().GetReadonly(0);
        Assert.AreEqual((short)42, mana.MaximumMana);
    }

    [TestMethod]
    public void Grant_NonZeroManaCost_NoIntelligenceScore_DoesNotThrowAndDoesNotGrantManaComponent()
    {
        var manager = CreateRegisteredManager();

        ActionGrantEffects.Grant(manager, 0, ActionId, manaCost: 2, overrideDefinition: null);

        Assert.IsFalse(manager.GetPackedPool<ManaComponent>().Has(0));
    }
}
