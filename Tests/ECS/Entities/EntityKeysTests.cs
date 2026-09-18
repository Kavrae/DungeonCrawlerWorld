using Engine.ECS.Components;
using Engine.ECS.Entities;

namespace Tests.ECS.Entities;

[TestClass]
public sealed class EntityKeysTests
{
    [TestMethod]
    public void Issue_CountsUpFromOne_AndResolvesBothWays()
    {
        var keys = new EntityKeys();

        var first = keys.Issue(5);
        var second = keys.Issue(2);

        Assert.AreEqual(new EntityKey(1), first);
        Assert.AreEqual(new EntityKey(2), second);
        Assert.AreEqual(first, keys.GetKey(5));
        Assert.IsTrue(keys.TryGetEntityId(second, out var entityId));
        Assert.AreEqual(2, entityId);
    }

    [TestMethod]
    public void Release_KeyNoLongerResolves_AndTheIdHasNone()
    {
        var keys = new EntityKeys();
        var key = keys.Issue(3);

        keys.Release(3);

        Assert.IsFalse(keys.TryGetEntityId(key, out _));
        Assert.AreEqual(EntityKey.None, keys.GetKey(3));
        Assert.AreEqual(0, keys.Count);
    }

    [TestMethod]
    public void GetKey_IdNeverIssued_IsNone() =>
        Assert.IsTrue(new EntityKeys().GetKey(1_000).IsNone);

    /// <summary>The point of keys: a destroyed entity's id is reused straight away, but a reference to the old entity must not resolve to the new one.</summary>
    [TestMethod]
    public void EntityManager_DestroyedIdReused_GetsANewKey_AndTheOldKeyResolvesToNothing()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);
        var entityManager = new EntityManager(componentManager, initialCapacity: 4);
        var entityId = entityManager.CreateEntity();
        var oldKey = entityManager.Keys.GetKey(entityId);

        entityManager.DestroyEntity(entityId);
        var reusedId = entityManager.CreateEntity();

        Assert.AreEqual(entityId, reusedId, "Precondition: the id is recycled.");
        Assert.AreNotEqual(oldKey, entityManager.Keys.GetKey(reusedId));
        Assert.IsFalse(entityManager.Keys.TryGetEntityId(oldKey, out _));
    }
}
