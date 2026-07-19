using Engine.ECS.Components;
using Engine.ECS.Entities;

namespace Tests.ECS.Entities;

[TestClass]
public sealed class EntityManagerTests
{
    private struct TestComponent
    {
        public int Value;
    }

    [TestMethod]
    public void CreateEntity_FirstCall_ReturnsZero()
    {
        var componentManager = new ComponentManager(4, 4);
        var entityManager = new EntityManager(componentManager, 4);

        var entityId = entityManager.CreateEntity();

        Assert.AreEqual(0, entityId);
        Assert.IsTrue(entityManager.IsAlive(0));
        Assert.AreEqual(1, entityManager.LivingEntityCount);
    }

    [TestMethod]
    public void DestroyEntity_RemovesAllComponentsAndMarksNotAlive()
    {
        var componentManager = new ComponentManager(4, 4);
        componentManager.RegisterDirectPool<TestComponent>((ref TestComponent existing, TestComponent incoming) => existing = incoming);
        var entityManager = new EntityManager(componentManager, 4);
        var entityId = entityManager.CreateEntity();
        componentManager.GetDirectPool<TestComponent>().Add(entityId, new TestComponent { Value = 1 });

        entityManager.DestroyEntity(entityId);

        Assert.IsFalse(entityManager.IsAlive(entityId));
        Assert.IsFalse(componentManager.GetDirectPool<TestComponent>().Has(entityId));
    }

    [TestMethod]
    public void DestroyThenCreate_RecyclesTheReleasedId()
    {
        // This is the entity-id-recycling behavior Old's static, non-recycling
        // EntityManager never had -- ids must stay bounded to the living-entity
        // high-water mark, not grow forever across churn.
        var componentManager = new ComponentManager(4, 4);
        var entityManager = new EntityManager(componentManager, 4);

        var first = entityManager.CreateEntity();
        entityManager.DestroyEntity(first);
        var second = entityManager.CreateEntity();

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void CreateEntity_BeyondInitialCapacity_GrowsComponentPools()
    {
        var componentManager = new ComponentManager(2, 4);
        componentManager.RegisterDirectPool<TestComponent>((ref TestComponent existing, TestComponent incoming) => existing = incoming);
        var entityManager = new EntityManager(componentManager, 2);

        entityManager.CreateEntity();
        entityManager.CreateEntity();
        var third = entityManager.CreateEntity();

        Assert.IsGreaterThan(2, entityManager.Capacity);
        // The direct pool must have grown to accommodate the third entity id without throwing.
        componentManager.GetDirectPool<TestComponent>().Add(third, new TestComponent { Value = 1 });
        Assert.IsTrue(componentManager.GetDirectPool<TestComponent>().Has(third));
    }

    [TestMethod]
    public void DestroyEntity_NotAlive_Throws()
    {
        var componentManager = new ComponentManager(4, 4);
        var entityManager = new EntityManager(componentManager, 4);

        Assert.ThrowsExactly<InvalidOperationException>(() => entityManager.DestroyEntity(0));
    }
}
