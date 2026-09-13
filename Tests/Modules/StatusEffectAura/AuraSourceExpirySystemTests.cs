using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffectAura.Systems;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Tests.Modules.StatusEffectAura;

[TestClass]
public sealed class AuraSourceExpirySystemTests
{
    private const int EntityId = 1;

    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    private static (AuraSourceExpirySystem System, PackedComponentPool<AuraSourceExpiryComponent> Expiries, MultiComponentPool<StatusEffectAuraSourceComponent> Sources, EventBus EventBus) Build()
    {
        var expiries = new PackedComponentPool<AuraSourceExpiryComponent>(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);
        var sources = new MultiComponentPool<StatusEffectAuraSourceComponent>(maximumEntityCount: 10, initialCapacity: 4);
        var eventBus = new EventBus();

        return (new AuraSourceExpirySystem(expiries, sources, eventBus), expiries, sources, eventBus);
    }

    [TestMethod]
    public void BeforeItsExpiryFrame_SourceAndExpiryStay()
    {
        var (system, expiries, sources, eventBus) = Build();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);
        expiries.Add(EntityId, new AuraSourceExpiryComponent(StatusEffectType.Light, expiresAtFrame: 100));

        system.Update(Frame(99), 0);

        Assert.IsTrue(expiries.Has(EntityId));
        Assert.IsTrue(sources.Has(EntityId));
    }

    [TestMethod]
    public void OnItsExpiryFrame_RevokesMatchingAuraSourceAndRemovesExpiry()
    {
        var (system, expiries, sources, eventBus) = Build();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);
        expiries.Add(EntityId, new AuraSourceExpiryComponent(StatusEffectType.Light, expiresAtFrame: 100));

        system.Update(Frame(100), 0);

        Assert.IsFalse(expiries.Has(EntityId));
        Assert.IsFalse(sources.Has(EntityId));
    }

    /// <summary>Only revokes the expired Type -- an unrelated aura source the entity also carries (e.g. Poison from an unrelated toggle) must survive.</summary>
    [TestMethod]
    public void OnExpiry_LeavesOtherAuraSourceTypesIntact()
    {
        var (system, expiries, sources, eventBus) = Build();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, StatusEffectType.Poison, auraAndGlowStrength: 5, Color.Purple);
        expiries.Add(EntityId, new AuraSourceExpiryComponent(StatusEffectType.Light, expiresAtFrame: 1));

        system.Update(Frame(1), 0);

        Assert.AreEqual(1, sources.CountForEntity(EntityId));
    }

    /// <summary>A re-grant merges a fresh expiry over the old one -- the old frame must not revoke the refreshed source.</summary>
    [TestMethod]
    public void RefreshedBeforeExpiry_OnlyTheNewFrameRevokes()
    {
        var (system, expiries, sources, eventBus) = Build();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);
        expiries.Add(EntityId, new AuraSourceExpiryComponent(StatusEffectType.Light, expiresAtFrame: 100));
        system.Update(Frame(50), 0);

        expiries.Merge(EntityId, new AuraSourceExpiryComponent(StatusEffectType.Light, expiresAtFrame: 250));
        system.Update(Frame(100), 0);
        Assert.IsTrue(sources.Has(EntityId), "The old frame passed, but the refreshed expiry is later.");

        system.Update(Frame(250), 0);
        Assert.IsFalse(sources.Has(EntityId));
    }

    [TestMethod]
    public void NoExpiries_DoesNotThrow()
    {
        var (system, _, _, _) = Build();

        system.Update(default, 0);
    }
}
