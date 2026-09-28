using Engine.ECS.Systems;
using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.StatusEffects;

[TestClass]
public sealed class StatusEffectImmunityTests
{
    private static ComponentManager CreateComponentManagerWithImmunity(int entityId, StatusEffectType effectType)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(entityId, new StatusEffectImmunityComponent(effectType, expiresAtFrame: FrameDeadline.Never));
        return componentManager;
    }

    [TestMethod]
    public void IsImmune_MatchingEffectType_ReturnsTrue()
    {
        var componentManager = CreateComponentManagerWithImmunity(0, StatusEffectType.Burning);

        Assert.IsTrue(StatusEffectImmunity.IsImmune(componentManager, 0, StatusEffectType.Burning, default, new EventBus(), TestPlayerQuery.NoPlayer));
    }

    [TestMethod]
    public void IsImmune_DifferentEffectType_ReturnsFalse()
    {
        var componentManager = CreateComponentManagerWithImmunity(0, StatusEffectType.Burning);

        Assert.IsFalse(StatusEffectImmunity.IsImmune(componentManager, 0, StatusEffectType.Poison, default, new EventBus(), TestPlayerQuery.NoPlayer));
    }

    [TestMethod]
    public void IsImmune_BlockedAndPlayerIsTarget_PublishesStatusEffectImmunityBlockedEvent()
    {
        var componentManager = CreateComponentManagerWithImmunity(0, StatusEffectType.Burning);
        var eventBus = new EventBus();
        StatusEffectImmunityBlockedEvent? published = null;
        eventBus.Subscribe<StatusEffectImmunityBlockedEvent>(e => published = e);

        var immune = StatusEffectImmunity.IsImmune(componentManager, 0, StatusEffectType.Burning, ActionSource.Admin, eventBus, new TestPlayerQuery(0));

        Assert.IsTrue(immune);
        Assert.IsNotNull(published);
        Assert.AreEqual(0, published.Value.EntityId);
        Assert.AreEqual(StatusEffectType.Burning, published.Value.EffectType);
    }

    [TestMethod]
    public void IsImmune_BlockedButPlayerNotInvolved_DoesNotPublishEvent()
    {
        var componentManager = CreateComponentManagerWithImmunity(1, StatusEffectType.Burning);
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<StatusEffectImmunityBlockedEvent>(_ => published = true);

        StatusEffectImmunity.IsImmune(componentManager, 1, StatusEffectType.Burning, TestSources.Entity(2), eventBus, new TestPlayerQuery(0));

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void IsImmune_NotBlocked_DoesNotPublishEvent()
    {
        var componentManager = CreateComponentManagerWithImmunity(0, StatusEffectType.Poison);
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<StatusEffectImmunityBlockedEvent>(_ => published = true);

        StatusEffectImmunity.IsImmune(componentManager, 0, StatusEffectType.Burning, ActionSource.Admin, eventBus, new TestPlayerQuery(0));

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void IsImmune_NoEventBusOrPlayerQuery_DoesNotThrow()
    {
        var componentManager = CreateComponentManagerWithImmunity(0, StatusEffectType.Burning);

        Assert.IsTrue(StatusEffectImmunity.IsImmune(componentManager, 0, StatusEffectType.Burning, default, new EventBus(), TestPlayerQuery.NoPlayer));
    }
}
