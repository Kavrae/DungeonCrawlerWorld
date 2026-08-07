using Engine.ECS.Components;
using Game.Modules.AbilityScores;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;

namespace Tests.Modules.Mana;

[TestClass]
public sealed class ManaGrantTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        new StatModifiersModule().RegisterComponents(manager);
        new AbilityScoresModule().RegisterComponents(manager);
        new ManaModule().RegisterComponents(manager);
        return manager;
    }

    [TestMethod]
    public void EnsureMana_EntityHasIntelligence_GrantsManaComponentSizedToIntelligenceTotal()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Intelligence, baseValue: 42);

        ManaGrant.EnsureManaComponentExists(manager, 0);

        var mana = manager.GetPackedPool<ManaComponent>().GetReadonly(0);
        Assert.AreEqual((short)42, mana.MaximumMana);
        Assert.AreEqual((short)42, mana.CurrentMana);
    }

    [TestMethod]
    public void EnsureMana_EntityAlreadyHasManaComponent_DoesNotOverwriteIt()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Intelligence, baseValue: 42);
        ManaGrant.EnsureManaComponentExists(manager, 0);
        manager.GetPackedPool<ManaComponent>().TryUpdate(0, static (ref ManaComponent mana) => mana.CurrentMana = 5);

        ManaGrant.EnsureManaComponentExists(manager, 0);

        var mana = manager.GetPackedPool<ManaComponent>().GetReadonly(0);
        Assert.AreEqual((short)5, mana.CurrentMana);
        Assert.AreEqual((short)42, mana.MaximumMana);
    }

    [TestMethod]
    public void EnsureMana_NoIntelligenceScoreForEntity_DoesNotGrantManaComponent()
    {
        var manager = CreateRegisteredManager();

        ManaGrant.EnsureManaComponentExists(manager, 0);

        Assert.IsFalse(manager.GetPackedPool<ManaComponent>().Has(0));
    }
}
