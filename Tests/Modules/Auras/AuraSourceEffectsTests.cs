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

    /// <summary>Removal must publish the component that was actually stored, not reconstruct one from the call's own parameters -- see AuraSourceRemovedEvent's own doc comment.</summary>
    [TestMethod]
    public void Revoke_PublishesRemovedWithTheStoredSource()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.PoisonId, power: 5, size: 2);

        AuraSourceRemovedEvent? published = null;
        eventBus.Subscribe<AuraSourceRemovedEvent>(e => published = e);

        AuraSourceEffects.Revoke(sources, eventBus, EntityId, TestAuras.PoisonId);

        Assert.IsFalse(sources.Has(EntityId));
        Assert.IsNotNull(published);
        Assert.AreEqual(EntityId, published!.Value.EntityId);
        Assert.AreEqual(5, published.Value.Source.Power);
    }

    [TestMethod]
    public void Apply_DifferentAuraAlreadyPresent_AddsSecondWithoutRemovingFirst()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.PoisonId, power: 5, size: 2);

        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.BurningId, power: 8, size: 3);

        Assert.AreEqual(2, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void AddHeld_SameAuraUnderTwoKeys_HoldsBothBesideTheUnkeyedOne()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.PoisonId, power: 5, size: 2);

        AuraSourceEffects.AddHeld(sources, EntityId, TestAuras.PoisonId, power: 16, size: 4, heldGrantKey: 1);
        AuraSourceEffects.AddHeld(sources, EntityId, TestAuras.PoisonId, power: 16, size: 4, heldGrantKey: 2);

        Assert.AreEqual(3, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void RemoveHeld_RemovesOnlyTheSourceUnderThatKey()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.PoisonId, power: 5, size: 2);
        AuraSourceEffects.AddHeld(sources, EntityId, TestAuras.PoisonId, power: 16, size: 4, heldGrantKey: 1);
        AuraSourceEffects.AddHeld(sources, EntityId, TestAuras.PoisonId, power: 16, size: 4, heldGrantKey: 2);

        AuraSourceEffects.RemoveHeld(sources, eventBus, EntityId, TestAuras.PoisonId, heldGrantKey: 1);

        var remaining = new List<AuraSourceComponent>();
        sources.CopyAll(EntityId, remaining);
        CollectionAssert.AreEquivalent(new uint[] { 0, 2 }, remaining.Select(source => source.HeldGrantKey).ToArray());
    }

    [TestMethod]
    public void ApplyAndRevoke_LeaveHeldSourcesOfTheSameAura()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.AddHeld(sources, EntityId, TestAuras.LightId, power: 16, size: 4, heldGrantKey: 7);

        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.LightId, power: 8, size: 3);
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.LightId, power: 8, size: 3);
        Assert.AreEqual(2, sources.CountForEntity(EntityId));

        AuraSourceEffects.Revoke(sources, eventBus, EntityId, TestAuras.LightId);

        Assert.AreEqual(1, sources.CountForEntity(EntityId));
        Assert.AreEqual(7u, sources.GetReadonlyByDenseIndex(sources.GetFirstDenseIndex(EntityId)).HeldGrantKey);
    }

    [TestMethod]
    public void RemoveUnheld_RemovesUnkeyedSourcesAndLeavesHeldOnes()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.PoisonId, power: 5, size: 2);
        AuraSourceEffects.AddHeld(sources, EntityId, TestAuras.PoisonId, power: 16, size: 4, heldGrantKey: 3);
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.BurningId, power: 8, size: 3);

        var removedAuraIds = new List<byte>();
        eventBus.Subscribe<AuraSourceRemovedEvent>(e => removedAuraIds.Add(e.Source.AuraId));

        AuraSourceEffects.RemoveUnheld(sources, eventBus, EntityId);

        Assert.AreEqual(1, sources.CountForEntity(EntityId));
        Assert.AreEqual(3u, sources.GetReadonlyByDenseIndex(sources.GetFirstDenseIndex(EntityId)).HeldGrantKey);
        CollectionAssert.AreEquivalent(new[] { TestAuras.PoisonId, TestAuras.BurningId }, removedAuraIds);
    }

    [TestMethod]
    public void RemoveAll_MultipleSources_RemovesEachAndPublishesOneEventPerInstance()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.PoisonId, power: 5, size: 2);
        AuraSourceEffects.AddHeld(sources, EntityId, TestAuras.BurningId, power: 8, size: 3, heldGrantKey: 1);

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

        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.LightId, power: 8, size: 3);

        Assert.IsTrue(sources.Has(EntityId));
    }

    /// <summary>Re-applying an already-present aura refreshes it: one source afterwards, never two and never none.</summary>
    [TestMethod]
    public void Apply_TypeAlreadyPresent_RefreshesRatherThanRemoving()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.LightId, power: 8, size: 3);

        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.LightId, power: 8, size: 3);

        Assert.IsTrue(sources.Has(EntityId));
        Assert.AreEqual(1, sources.CountForEntity(EntityId));
    }

    [TestMethod]
    public void Revoke_TypePresent_RemovesOnlyThatType()
    {
        var sources = CreatePool();
        var eventBus = new EventBus();
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.LightId, power: 8, size: 3);
        AuraSourceEffects.Apply(sources, eventBus, EntityId, TestAuras.PoisonId, power: 5, size: 2);

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
