using Engine.ECS.Components.Stores;
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

    private static (AuraSourceExpirySystem System, PackedComponentPool<AuraSourceExpiryComponent> Expiries, MultiComponentPool<StatusEffectAuraSourceComponent> Sources, EventBus EventBus) Build()
    {
        var expiries = new PackedComponentPool<AuraSourceExpiryComponent>(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);
        var sources = new MultiComponentPool<StatusEffectAuraSourceComponent>(maximumEntityCount: 10, initialCapacity: 4);
        var eventBus = new EventBus();
        return (new AuraSourceExpirySystem(expiries, sources, eventBus), expiries, sources, eventBus);
    }

    [TestMethod]
    public void Update_TicksFramesUntilNextTickDownByOne()
    {
        var (system, expiries, _, _) = Build();
        expiries.Add(EntityId, new AuraSourceExpiryComponent(StatusEffectType.Light, framesUntilNextTick: 100));

        system.Update(default, 0);

        Assert.AreEqual(99, expiries.GetReadonly(EntityId).FramesUntilNextTick);
    }

    [TestMethod]
    public void Update_FramesUntilNextTickReachesZero_RevokesMatchingAuraSourceAndRemovesExpiry()
    {
        var (system, expiries, sources, eventBus) = Build();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);
        expiries.Add(EntityId, new AuraSourceExpiryComponent(StatusEffectType.Light, framesUntilNextTick: 1));

        system.Update(default, 0);

        Assert.IsFalse(expiries.Has(EntityId));
        Assert.IsFalse(sources.Has(EntityId));
    }

    /// <summary>Only revokes the expired Type -- an unrelated aura source the entity also carries (e.g. Poison from an unrelated toggle) must survive.</summary>
    [TestMethod]
    public void Update_FramesUntilNextTickReachesZero_LeavesOtherAuraSourceTypesIntact()
    {
        var (system, expiries, sources, eventBus) = Build();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, StatusEffectType.Poison, auraAndGlowStrength: 5, Color.Purple);
        expiries.Add(EntityId, new AuraSourceExpiryComponent(StatusEffectType.Light, framesUntilNextTick: 1));

        system.Update(default, 0);

        Assert.AreEqual(1, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void Update_NoExpiries_DoesNotThrow()
    {
        var (system, _, _, _) = Build();

        system.Update(default, 0);
    }
}
