using Engine.ECS.Components;
using Engine.ECS.Systems;
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

    private static StatModifierComponent FirstModifier(ComponentManager manager, int entityId)
    {
        var pool = manager.GetMultiPool<StatModifierComponent>();
        var denseIndex = pool.GetFirstDenseIndex(entityId);
        Assert.AreNotEqual(-1, denseIndex, "Expected a StatModifierComponent to have been granted.");
        return pool.GetReadonlyByDenseIndex(denseIndex);
    }

    [TestMethod]
    public void Apply_PermanentDuration_StoresTheNeverDeadline()
    {
        var manager = CreateRegisteredManager();

        StatModifierEffects.Apply(manager, 0, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: -1f, expiresAtFrame: FrameDeadline.Never, StatusEffectSource.Admin);

        Assert.AreEqual(FrameDeadline.Never, FirstModifier(manager, 0).ExpiresAtFrame);
    }

    [TestMethod]
    public void Apply_FiniteDuration_StoresTheDeadlineItWasGiven()
    {
        var manager = CreateRegisteredManager();

        StatModifierEffects.Apply(manager, 0, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: -1f, expiresAtFrame: FrameDeadline.After(now: 100, frames: 30), StatusEffectSource.Admin);

        Assert.AreEqual(130u, FirstModifier(manager, 0).ExpiresAtFrame);
    }

    /// <summary>Grants always stack rather than replacing -- two modifiers on one entity, each with its own deadline (StatModifierExpirySystem is what later removes each on its own frame).</summary>
    [TestMethod]
    public void Apply_TwoGrants_BothStoredSeparately()
    {
        var manager = CreateRegisteredManager();

        StatModifierEffects.Apply(manager, 0, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: -1f, expiresAtFrame: FrameDeadline.Never, StatusEffectSource.Admin);
        StatModifierEffects.Apply(manager, 0, StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 2f, expiresAtFrame: 10, StatusEffectSource.Admin);

        Assert.AreEqual(2, manager.GetMultiPool<StatModifierComponent>().CountForEntity(0));
    }

    /// <summary>Nothing writes the entity's expiry timer at grant time -- StatModifierExpirySystem maintains it from the pool's own change notification, so granting without that system registered is still valid (this manager has no systems at all).</summary>
    [TestMethod]
    public void Apply_WithNoExpirySystemRegistered_DoesNotWriteAnExpiryTimer()
    {
        var manager = CreateRegisteredManager();

        StatModifierEffects.Apply(manager, 0, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: -1f, expiresAtFrame: 30, StatusEffectSource.Admin);

        Assert.IsFalse(manager.GetPackedPool<ExpiringStatModifierComponent>().Has(0));
    }
}
