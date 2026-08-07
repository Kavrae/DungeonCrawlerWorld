using Engine.ECS.Components;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.AbilityScores;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;

namespace Tests.Modules.Abilities;

[TestClass]
public sealed class AbilityGrantEffectsTests
{
    private static readonly Guid AbilityId = Guid.NewGuid();

    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        manager.RegisterMultiPool<AbilityInstanceComponent>();
        new StatModifiersModule().RegisterComponents(manager);
        new AbilityScoresModule().RegisterComponents(manager);
        new ManaModule().RegisterComponents(manager);
        return manager;
    }

    [TestMethod]
    public void Grant_AlwaysMergesAbilityInstanceComponent()
    {
        var manager = CreateRegisteredManager();

        AbilityGrantEffects.Grant(manager, 0, AbilityId, manaCost: 0, damageAmount: 7, cooldownFramesRemaining: 0);

        Assert.IsTrue(AbilityInstanceQueries.TryGet(manager.GetMultiPool<AbilityInstanceComponent>(), 0, AbilityId, out var instance));
        Assert.AreEqual((short)7, instance.DamageAmount);
    }

    [TestMethod]
    public void Grant_ZeroManaCost_DoesNotGrantManaComponent()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Intelligence, baseValue: 42);

        AbilityGrantEffects.Grant(manager, 0, AbilityId, manaCost: 0, damageAmount: 0, cooldownFramesRemaining: 0);

        Assert.IsFalse(manager.GetPackedPool<ManaComponent>().Has(0));
    }

    [TestMethod]
    public void Grant_NonZeroManaCost_EntityHasIntelligence_GrantsManaComponent()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Intelligence, baseValue: 42);

        AbilityGrantEffects.Grant(manager, 0, AbilityId, manaCost: 2, damageAmount: 0, cooldownFramesRemaining: 0);

        var mana = manager.GetPackedPool<ManaComponent>().GetReadonly(0);
        Assert.AreEqual((short)42, mana.MaximumMana);
    }

    [TestMethod]
    public void Grant_NonZeroManaCost_NoIntelligenceScore_DoesNotThrowAndDoesNotGrantManaComponent()
    {
        var manager = CreateRegisteredManager();

        AbilityGrantEffects.Grant(manager, 0, AbilityId, manaCost: 2, damageAmount: 0, cooldownFramesRemaining: 0);

        Assert.IsFalse(manager.GetPackedPool<ManaComponent>().Has(0));
    }
}
