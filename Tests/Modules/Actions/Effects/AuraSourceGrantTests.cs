using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Effects;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Actions.Effects;

[TestClass]
public sealed class AuraSourceGrantTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private static ActionEffectContext BuildContext(ComponentManager componentManager, MultiComponentPool<StatusEffectAuraSourceComponent>? auraSources, float durationScaleMultiplier = 1.0f) => new(
        SourceEntityId: SourceEntityId,
        TargetEntityId: TargetEntityId,
        Health: componentManager.GetPackedPool<HealthComponent>(),
        EventBus: new EventBus(),
        MathUtility: new MathUtility(),
        ComponentManager: componentManager,
        ActivatorName: "Test",
        ActivatorTags: [],
        AuraSources: auraSources,
        DurationScaleMultiplier: durationScaleMultiplier);

    private static (ComponentManager ComponentManager, MultiComponentPool<StatusEffectAuraSourceComponent> AuraSources) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<HealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatusEffectAuraSourceComponent>();
        componentManager.RegisterPackedPool<AuraSourceExpiryComponent>(static (ref existing, incoming) => existing = incoming);
        return (componentManager, componentManager.GetMultiPool<StatusEffectAuraSourceComponent>());
    }

    [TestMethod]
    public void Apply_Permanent_TogglesOnTargetEntityNotSourceEntity()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(StatusEffectType.Poison, AuraAndGlowStrength: 5, Color.Purple);

        entry.Apply(BuildContext(componentManager, auraSources));

        Assert.IsTrue(auraSources.Has(TargetEntityId));
        Assert.IsFalse(auraSources.Has(SourceEntityId));
    }

    /// <summary>A caller that wants to target itself (e.g. Toxic Idol) does so via a Self-shaped TargetingSpec, which resolves TargetEntityId to the caster -- not by anything AuraSourceGrant itself does with SourceEntityId.</summary>
    [TestMethod]
    public void Apply_SourceAndTargetAreSameEntity_TogglesOnThatEntity()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(StatusEffectType.Poison, AuraAndGlowStrength: 5, Color.Purple);
        var context = BuildContext(componentManager, auraSources) with { TargetEntityId = SourceEntityId };

        entry.Apply(context);

        Assert.IsTrue(auraSources.Has(SourceEntityId));
    }

    [TestMethod]
    public void Apply_PermanentAppliedTwice_TogglesOff()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(StatusEffectType.Poison, AuraAndGlowStrength: 5, Color.Purple);
        var context = BuildContext(componentManager, auraSources);

        entry.Apply(context);
        entry.Apply(context);

        Assert.IsFalse(auraSources.Has(TargetEntityId));
    }

    [TestMethod]
    public void Apply_PoolNotWired_DoesNotThrow()
    {
        var (componentManager, _) = Build();
        var entry = new AuraSourceGrant(StatusEffectType.Poison, AuraAndGlowStrength: 5, Color.Purple);

        entry.Apply(BuildContext(componentManager, auraSources: null));
    }

    [TestMethod]
    public void Apply_TimedDuration_GrantsSourceAndSchedulesExpiry()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(StatusEffectType.Light, AuraAndGlowStrength: 8, Color.White, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, auraSources));

        Assert.IsTrue(auraSources.Has(TargetEntityId));
        var expiries = componentManager.GetPackedPool<AuraSourceExpiryComponent>();
        Assert.IsTrue(expiries.Has(TargetEntityId));
        Assert.AreEqual(100, expiries.GetReadonly(TargetEntityId).FramesUntilNextTick);
        Assert.AreEqual(StatusEffectType.Light, expiries.GetReadonly(TargetEntityId).Type);
    }

    [TestMethod]
    public void Apply_TimedDurationScaleMultiplierAboveOne_ScalesExpiryFrames()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(StatusEffectType.Light, AuraAndGlowStrength: 8, Color.White, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, auraSources, durationScaleMultiplier: 4.0f));

        Assert.AreEqual(400, componentManager.GetPackedPool<AuraSourceExpiryComponent>().GetReadonly(TargetEntityId).FramesUntilNextTick);
    }

    /// <summary>The behavioral difference from permanent mode -- re-applying a timed grant before it expires must refresh it, not flip it off (a flip would extinguish an existing grant instead of renewing it).</summary>
    [TestMethod]
    public void Apply_TimedAppliedTwice_RefreshesRatherThanTogglingOff()
    {
        var (componentManager, auraSources) = Build();
        var entry = new AuraSourceGrant(StatusEffectType.Light, AuraAndGlowStrength: 8, Color.White, DurationFrames: 100);
        var context = BuildContext(componentManager, auraSources);

        entry.Apply(context);
        entry.Apply(context);

        Assert.IsTrue(auraSources.Has(TargetEntityId));
        Assert.AreEqual(1, auraSources.CountForEntity(TargetEntityId));
    }
}
