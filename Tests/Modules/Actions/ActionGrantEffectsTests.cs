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

    private static readonly ActionDefinition DrainingActionDefinition = TestActionDefinition with { ActivationEffects = [new Effect([new ManaDrain(2)])] };

    private static ComponentManager CreateRegisteredManager()
    {
        var manager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8));
        return manager;
    }

    private static ActionCatalog CatalogWith(ActionDefinition definition)
    {
        var catalog = new ActionCatalog();
        catalog.Register(definition);
        return catalog;
    }

    [TestMethod]
    public void Grant_AlwaysMergesActionInstanceComponent()
    {
        var manager = CreateRegisteredManager();
        var overrideDefinition = ActionOverrideEffects.OverrideFlatDamage(TestActionDefinition, flatDamage: 7);

        ActionGrantEffects.Grant(manager, CatalogWith(TestActionDefinition), 0, ActionId, overrideDefinition);

        Assert.IsTrue(manager.GetMultiPool<ActionInstanceComponent>().TryGetFirst(0, ActionId, static (ref readonly ActionInstanceComponent candidate, Guid id) => candidate.ActionId == id, out var instance));
        Assert.AreEqual(overrideDefinition, instance.Override);
    }

    [TestMethod]
    public void Grant_ActionThatDrainsNoMana_DoesNotGrantManaComponent()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Intelligence, baseValue: 42);

        ActionGrantEffects.Grant(manager, CatalogWith(TestActionDefinition), 0, ActionId, overrideDefinition: null);

        Assert.IsFalse(manager.GetPackedPool<ManaComponent>().Has(0));
    }

    [TestMethod]
    public void Grant_ActionWhoseActivationEffectsDrainMana_EntityHasIntelligence_GrantsManaComponent()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Intelligence, baseValue: 42);

        ActionGrantEffects.Grant(manager, CatalogWith(DrainingActionDefinition), 0, ActionId, overrideDefinition: null);

        var mana = manager.GetPackedPool<ManaComponent>().GetReadonly(0);
        Assert.AreEqual((short)42, mana.MaximumMana);
    }

    [TestMethod]
    public void Grant_ReadsTheOverride_NotTheCatalogDefinition()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Intelligence, baseValue: 42);

        ActionGrantEffects.Grant(manager, CatalogWith(TestActionDefinition), 0, ActionId, overrideDefinition: DrainingActionDefinition);

        Assert.IsTrue(manager.GetPackedPool<ManaComponent>().Has(0), "What the entity will use is the override, and it drains mana.");
    }

    [TestMethod]
    public void Grant_ToggleWhoseUpkeepDrainsMana_GrantsManaComponent()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Intelligence, baseValue: 42);
        var upkeepToggle = TestActionDefinition with { Toggle = new ToggleSpec(new TogglePeriodicEffects([new Effect([new ManaDrain(1)])], IntervalFrames: 60)) };

        ActionGrantEffects.Grant(manager, CatalogWith(upkeepToggle), 0, ActionId, overrideDefinition: null);

        Assert.IsTrue(manager.GetPackedPool<ManaComponent>().Has(0));
    }

    [TestMethod]
    public void Grant_ActionThatDrainsMana_NoIntelligenceScore_DoesNotThrowAndDoesNotGrantManaComponent()
    {
        var manager = CreateRegisteredManager();

        ActionGrantEffects.Grant(manager, CatalogWith(DrainingActionDefinition), 0, ActionId, overrideDefinition: null);

        Assert.IsFalse(manager.GetPackedPool<ManaComponent>().Has(0));
    }
}