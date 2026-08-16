using Engine.ECS.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.StatModifiers;

[TestClass]
public sealed class StatModifierEffectsTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        new StatModifiersModule().RegisterComponents(manager);
        return manager;
    }

    [TestMethod]
    public void Apply_PermanentDuration_DoesNotAddExpiringMarker()
    {
        var manager = CreateRegisteredManager();

        StatModifierEffects.Apply(manager, 0, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: -1f, durationFrames: null, StatusEffectSource.Admin);

        Assert.IsFalse(manager.GetMultiPool<ExpiringStatModifierComponent>().Has(0));
        Assert.IsTrue(manager.GetMultiPool<StatModifierComponent>().Has(0));
    }

    [TestMethod]
    public void Apply_FiniteDuration_AddsExpiringMarker()
    {
        var manager = CreateRegisteredManager();

        StatModifierEffects.Apply(manager, 0, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: -1f, durationFrames: 30, StatusEffectSource.Admin);

        Assert.IsTrue(manager.GetMultiPool<ExpiringStatModifierComponent>().Has(0));
    }

    [TestMethod]
    public void Apply_OnePermanentAndOneFiniteGrant_MarkerCountReflectsOnlyTheFiniteOne()
    {
        var manager = CreateRegisteredManager();

        StatModifierEffects.Apply(manager, 0, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: -1f, durationFrames: null, StatusEffectSource.Admin);
        StatModifierEffects.Apply(manager, 0, StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 2f, durationFrames: 10, StatusEffectSource.Admin);

        Assert.AreEqual(2, manager.GetMultiPool<StatModifierComponent>().CountForEntity(0));
        Assert.AreEqual(1, manager.GetMultiPool<ExpiringStatModifierComponent>().CountForEntity(0));
    }
}
