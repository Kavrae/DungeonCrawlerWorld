using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.StatusEffectAura;

[TestClass]
public sealed class AuraSourceEffectsTests
{
    private const int EntityId = 1;

    private static MultiComponentPool<StatusEffectAuraSourceComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4);

    [TestMethod]
    public void Toggle_AbsentType_AddsSourceAndPublishesAdded()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceAddedEvent? published = null;
        eventBus.Subscribe<AuraSourceAddedEvent>(e => published = e);

        AuraSourceEffects.Toggle(sources, eventBus, EntityId, StatusEffectType.Poison, auraAndGlowStrength: 5, Color.Purple);

        Assert.IsTrue(sources.Has(EntityId));
        Assert.IsNotNull(published);
        Assert.AreEqual(EntityId, published!.Value.EntityId);
        Assert.AreEqual(StatusEffectType.Poison, published.Value.Source.EffectType);
        Assert.AreEqual(5, published.Value.Source.AuraAndGlowStrength);
        Assert.AreEqual(Color.Purple, published.Value.Source.GlowColor);
    }

    /// <summary>Removal must publish the component that was actually stored, not reconstruct one from whatever this call's own parameters happen to be -- see AuraSourceRemovedEvent's own doc comment.</summary>
    [TestMethod]
    public void Toggle_PresentType_RemovesSourceAndPublishesRemovedWithRealStoredValue()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, StatusEffectType.Poison, auraAndGlowStrength: 5, Color.Purple);

        AuraSourceRemovedEvent? published = null;
        eventBus.Subscribe<AuraSourceRemovedEvent>(e => published = e);

        AuraSourceEffects.Toggle(sources, eventBus, EntityId, StatusEffectType.Poison, auraAndGlowStrength: 99, Color.Green);

        Assert.IsFalse(sources.Has(EntityId));
        Assert.IsNotNull(published);
        Assert.AreEqual(EntityId, published!.Value.EntityId);
        Assert.AreEqual(5, published.Value.Source.AuraAndGlowStrength);
        Assert.AreEqual(Color.Purple, published.Value.Source.GlowColor);
    }

    [TestMethod]
    public void Toggle_DifferentTypeAlreadyPresent_AddsSecondTypeWithoutRemovingFirst()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, StatusEffectType.Poison, auraAndGlowStrength: 5, Color.Purple);

        AuraSourceEffects.Toggle(sources, eventBus, EntityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Red);

        Assert.AreEqual(2, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void RemoveAll_MultipleSources_RemovesEachAndPublishesOneEventPerInstance()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, StatusEffectType.Poison, auraAndGlowStrength: 5, Color.Purple);
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Red);

        var publishedTypes = new List<StatusEffectType>();
        eventBus.Subscribe<AuraSourceRemovedEvent>(e => publishedTypes.Add(e.Source.EffectType));

        AuraSourceEffects.RemoveAll(sources, eventBus, EntityId);

        Assert.IsFalse(sources.Has(EntityId));
        Assert.HasCount(2, publishedTypes);
        CollectionAssert.Contains(publishedTypes, StatusEffectType.Poison);
        CollectionAssert.Contains(publishedTypes, StatusEffectType.Burning);
    }

    [TestMethod]
    public void RemoveAll_NoSources_DoesNotPublish()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<AuraSourceRemovedEvent>(_ => published = true);

        AuraSourceEffects.RemoveAll(sources, eventBus, EntityId);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_AbsentType_AddsSource()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();

        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);

        Assert.IsTrue(sources.Has(EntityId));
    }

    /// <summary>The behavioral difference from Toggle -- re-Applying an already-present type refreshes it (still present afterward) rather than flipping it off.</summary>
    [TestMethod]
    public void Apply_TypeAlreadyPresent_RefreshesRatherThanRemoving()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);

        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);

        Assert.IsTrue(sources.Has(EntityId));
        Assert.AreEqual(1, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void Revoke_TypePresent_RemovesOnlyThatType()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, StatusEffectType.Light, auraAndGlowStrength: 8, Color.White);
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, StatusEffectType.Poison, auraAndGlowStrength: 5, Color.Purple);

        AuraSourceEffects.Revoke(sources, eventBus, EntityId, StatusEffectType.Light);

        Assert.AreEqual(1, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void Revoke_TypeAbsent_DoesNotThrowOrPublish()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<AuraSourceRemovedEvent>(_ => published = true);

        AuraSourceEffects.Revoke(sources, eventBus, EntityId, StatusEffectType.Light);

        Assert.IsFalse(published);
    }
}
