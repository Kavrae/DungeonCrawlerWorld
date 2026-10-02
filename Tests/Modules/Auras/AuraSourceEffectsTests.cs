using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Auras;

[TestClass]
public sealed class AuraSourceEffectsTests
{
    private const int EntityId = 1;

    private static MultiComponentPool<AuraSourceComponent> CreatePool() =>
        new(entityCapacity: 10, initialCapacity: 4);

    [TestMethod]
    public void Toggle_AbsentType_AddsSource()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();

        AuraSourceEffects.Toggle(sources, eventBus, EntityId, TestAuras.PoisonId, strength: 5);

        Assert.AreEqual(1, sources.CountForEntity(EntityId));
        var added = sources.GetReadonlyByDenseIndex(sources.GetFirstDenseIndex(EntityId));
        Assert.AreEqual(TestAuras.PoisonId, added.AuraId);
        Assert.AreEqual(5, added.Strength);
    }

    /// <summary>Removal must publish the component that was actually stored, not reconstruct one from whatever this call's own parameters happen to be -- see AuraSourceRemovedEvent's own doc comment.</summary>
    [TestMethod]
    public void Toggle_PresentType_RemovesSourceAndPublishesRemovedWithRealStoredValue()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, TestAuras.PoisonId, strength: 5);

        AuraSourceRemovedEvent? published = null;
        eventBus.Subscribe<AuraSourceRemovedEvent>(e => published = e);

        AuraSourceEffects.Toggle(sources, eventBus, EntityId, TestAuras.PoisonId, strength: 99);

        Assert.IsFalse(sources.Has(EntityId));
        Assert.IsNotNull(published);
        Assert.AreEqual(EntityId, published!.Value.EntityId);
        Assert.AreEqual(5, published.Value.Source.Strength);
    }

    [TestMethod]
    public void Toggle_DifferentTypeAlreadyPresent_AddsSecondTypeWithoutRemovingFirst()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, TestAuras.PoisonId, strength: 5);

        AuraSourceEffects.Toggle(sources, eventBus, EntityId, TestAuras.BurningId, strength: 8);

        Assert.AreEqual(2, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void RemoveAll_MultipleSources_RemovesEachAndPublishesOneEventPerInstance()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, TestAuras.PoisonId, strength: 5);
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, TestAuras.BurningId, strength: 8);

        var publishedTypes = new List<byte>();
        eventBus.Subscribe<AuraSourceRemovedEvent>(e => publishedTypes.Add(e.Source.AuraId));

        AuraSourceEffects.RemoveAll(sources, eventBus, EntityId);

        Assert.IsFalse(sources.Has(EntityId));
        Assert.HasCount(2, publishedTypes);
        CollectionAssert.Contains(publishedTypes, TestAuras.PoisonId);
        CollectionAssert.Contains(publishedTypes, TestAuras.BurningId);
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

        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.LightId, strength: 8);

        Assert.IsTrue(sources.Has(EntityId));
    }

    /// <summary>The behavioral difference from Toggle -- re-Applying an already-present type refreshes it (still present afterward) rather than flipping it off.</summary>
    [TestMethod]
    public void Apply_TypeAlreadyPresent_RefreshesRatherThanRemoving()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.LightId, strength: 8);

        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.LightId, strength: 8);

        Assert.IsTrue(sources.Has(EntityId));
        Assert.AreEqual(1, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void Revoke_TypePresent_RemovesOnlyThatType()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.LightId, strength: 8);
        AuraSourceEffects.Toggle(sources, eventBus, EntityId, TestAuras.PoisonId, strength: 5);

        AuraSourceEffects.Revoke(sources, eventBus, EntityId, TestAuras.LightId);

        Assert.AreEqual(1, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void Revoke_TypeAbsent_DoesNotThrowOrPublish()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<AuraSourceRemovedEvent>(_ => published = true);

        AuraSourceEffects.Revoke(sources, eventBus, EntityId, TestAuras.LightId);

        Assert.IsFalse(published);
    }
}
