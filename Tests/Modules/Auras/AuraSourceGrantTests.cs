using Engine.ECS.Entities;
using Game.Effects;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Health.Components;
using Game.Modules.Auras.Components;
using Game.Modules.Auras;
using Game.Modules.Poison;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Auras;

[TestClass]
public sealed class AuraSourceGrantTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private static EffectContext BuildContext(ComponentManager componentManager, MultiComponentPool<AuraSourceComponent>? auraSources, float durationScaleMultiplier = 1.0f) => TestActionEffects.Context(
        SourceEntityId: SourceEntityId,
        TargetEntityId: TargetEntityId,
        Health: componentManager.GetPackedPool<SimpleHealthComponent>(),
        EventBus: new EventBus(),
        MathUtility: new MathUtility(),
        ComponentManager: componentManager,
        EntityKeys: new EntityKeys(),
        ActivatorName: "Test",
        ActivatorTags: [], Now: 0,
        AuraSources: auraSources,
        DurationScaleMultiplier: durationScaleMultiplier);

    private static (ComponentManager ComponentManager, MultiComponentPool<AuraSourceComponent> AuraSources) Build()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        return (componentManager, componentManager.GetMultiPool<AuraSourceComponent>());
    }

    [TestMethod]
    public void Apply_Permanent_GrantsToTargetEntityNotSourceEntity()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(TestAuras.PoisonGlowOnly, Power: 5, Size: 2);

        entry.Apply(BuildContext(componentManager, auraSources));

        Assert.IsTrue(auraSources.Has(TargetEntityId));
        Assert.IsFalse(auraSources.Has(SourceEntityId));
    }

    /// <summary>A caller that wants to target itself (e.g. Toxic Idol) does so via a Self-shaped TargetingSpec, which resolves TargetEntityId to the caster -- not by anything AuraSourceGrant itself does with SourceEntityId.</summary>
    [TestMethod]
    public void Apply_SourceAndTargetAreSameEntity_GrantsToThatEntity()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(TestAuras.PoisonGlowOnly, Power: 5, Size: 2);
        var context = BuildContext(componentManager, auraSources) with { TargetEntityId = SourceEntityId };

        entry.Apply(context);

        Assert.IsTrue(auraSources.Has(SourceEntityId));
    }

    [TestMethod]
    public void Apply_PermanentAppliedTwiceOutsideAToggle_LeavesOneSource()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(TestAuras.PoisonGlowOnly, Power: 5, Size: 2);
        var context = BuildContext(componentManager, auraSources);

        entry.Apply(context);
        entry.Apply(context);

        Assert.AreEqual(1, auraSources.CountForEntity(TargetEntityId));
    }

    [TestMethod]
    public void ApplyUnderTwoKeys_AddsASourcePerKey_AndRevertRemovesOnlyItsOwn()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(TestAuras.PoisonGlowOnly, Power: 5, Size: 2);
        var first = BuildContext(componentManager, auraSources) with { HeldGrantKey = 1 };
        var second = BuildContext(componentManager, auraSources) with { HeldGrantKey = 2 };

        entry.Apply(first);
        entry.Apply(second);
        Assert.AreEqual(2, auraSources.CountForEntity(TargetEntityId));

        entry.Revert(first);

        Assert.AreEqual(1, auraSources.CountForEntity(TargetEntityId));
        Assert.AreEqual(2u, auraSources.GetReadonlyByDenseIndex(auraSources.GetFirstDenseIndex(TargetEntityId)).HeldGrantKey);
    }

    [TestMethod]
    public void Revert_OutsideAToggle_RemovesNothing()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(TestAuras.PoisonGlowOnly, Power: 5, Size: 2);
        var context = BuildContext(componentManager, auraSources);
        entry.Apply(context);

        entry.Revert(context);

        Assert.AreEqual(1, auraSources.CountForEntity(TargetEntityId));
    }

    [TestMethod]
    public void GrantsUntilRevoked_OnlyWithoutADuration()
    {
        Assert.IsTrue(new AuraSourceGrant(TestAuras.Light, Power: 8, Size: 3).GrantsUntilRevoked);
        Assert.IsFalse(new AuraSourceGrant(TestAuras.Light, Power: 8, Size: 3, DurationFrames: 100).GrantsUntilRevoked);
    }

    [TestMethod]
    public void Apply_PoolNotWired_DoesNotThrow()
    {
        var (componentManager, _) = Build();
        var entry = new AuraSourceGrant(TestAuras.PoisonGlowOnly, Power: 5, Size: 2);

        entry.Apply(BuildContext(componentManager, auraSources: null));
    }

    [TestMethod]
    public void Apply_TimedDuration_GrantsSourceAndSchedulesExpiry()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(TestAuras.Light, Power: 8, Size: 3, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, auraSources));

        Assert.IsTrue(auraSources.Has(TargetEntityId));
        var expiries = componentManager.GetPackedPool<AuraSourceExpiryComponent>();
        Assert.IsTrue(expiries.Has(TargetEntityId));
        Assert.AreEqual(100u, expiries.GetReadonly(TargetEntityId).ExpiresAtFrame);
        Assert.AreEqual(TestAuras.LightId, expiries.GetReadonly(TargetEntityId).AuraId);
    }

    [TestMethod]
    public void Apply_TimedDurationScaleMultiplierAboveOne_ScalesExpiryFrames()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(TestAuras.Light, Power: 8, Size: 3, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, auraSources, durationScaleMultiplier: 4.0f));

        Assert.AreEqual(400u, componentManager.GetPackedPool<AuraSourceExpiryComponent>().GetReadonly(TargetEntityId).ExpiresAtFrame);
    }

    /// <summary>Re-applying a timed grant before it expires refreshes it: one source afterwards.</summary>
    [TestMethod]
    public void Apply_TimedAppliedTwice_RefreshesTheOneSource()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(TestAuras.Light, Power: 8, Size: 3, DurationFrames: 100);
        var context = BuildContext(componentManager, auraSources);

        entry.Apply(context);
        entry.Apply(context);

        Assert.IsTrue(auraSources.Has(TargetEntityId));
        Assert.AreEqual(1, auraSources.CountForEntity(TargetEntityId));
    }
}
