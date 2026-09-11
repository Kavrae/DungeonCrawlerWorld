using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
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

        // EntityId seeded Local -- an entity with no ProcessingTierComponent resolves to Beyond
        // (see ProcessingTierWiring), whose far coarser cadence no single-Update test would reach.
        var processingTiers = new DirectComponentPool<ProcessingTierComponent>(initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        processingTiers.Add(EntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));

        return (new AuraSourceExpirySystem(expiries, sources, eventBus, processingTiers, new ProcessingTierEvents()), expiries, sources, eventBus);
    }

    [TestMethod]
    public void Update_TicksFramesUntilNextTickDownByFramesPerVisit()
    {
        var (system, expiries, _, _) = Build();
        expiries.Add(EntityId, new AuraSourceExpiryComponent(StatusEffectType.Light, framesUntilNextTick: 100));

        // EntityId is 1, so it lands in bucket 1 and is only due when FrameCount leaves
        // remainder 1 -- FrameCount 0 reaches bucket 0 instead.
        system.Update(new EngineTime(default, default, false, FrameCount: EntityId), 0);

        Assert.AreEqual(100 - system.StripeCount, expiries.GetReadonly(EntityId).FramesUntilNextTick);
    }

    [TestMethod]
    public void Update_FramesUntilNextTickReachesZero_RevokesMatchingAuraSourceAndRemovesExpiry()
    {
        var (system, expiries, sources, eventBus) = Build();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);
        expiries.Add(EntityId, new AuraSourceExpiryComponent(StatusEffectType.Light, framesUntilNextTick: 1));

        system.Update(new EngineTime(default, default, false, FrameCount: EntityId), 0);

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

        system.Update(new EngineTime(default, default, false, FrameCount: EntityId), 0);

        Assert.AreEqual(1, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void Update_NoExpiries_DoesNotThrow()
    {
        var (system, _, _, _) = Build();

        system.Update(default, 0);
    }
}
