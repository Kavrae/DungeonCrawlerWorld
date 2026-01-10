using DungeonCrawlerWorld.Components;
using Microsoft.Xna.Framework;

namespace UnitTests.Components
{
    [TestClass]
    public sealed class DenseSetTests
    {
        [TestMethod]
        public void NewTest()
        {
            var initialCapacity = 5;
            MergeAction<BackgroundComponent> mergeAction = (ref BackgroundComponent existingComponent, BackgroundComponent newComponent) =>
            {
                existingComponent.BackgroundColor = Color.White;
            };

            var denseSet = new DenseSet<BackgroundComponent>(initialCapacity, mergeAction);
            Assert.AreEqual(0, denseSet.Count, "SparseSet has a component count when it should not.");
            Assert.HasCount(initialCapacity, denseSet.GetAll(), "AllComponents count does not match initial capacity");
            Assert.IsFalse(denseSet.HasComponent(0), "SparseSet has a component for entityId 0 when it should not");

            Assert.Throws<IndexOutOfRangeException>(() => denseSet.HasComponent(initialCapacity));
        }

        [TestMethod]
        public void MergeTest()
        {
            var untouchedEntityId = 0;
            var mergedEntityId = 1;
            var initialCapacity = 2;
            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var denseSet = new DenseSet<HealthComponent>(initialCapacity, mergeAction);

            denseSet.Add(untouchedEntityId, new HealthComponent
            {
                MaximumHealth = 50
            });
            denseSet.Add(mergedEntityId, new HealthComponent
            {
                MaximumHealth = 100
            });
            denseSet.Add(mergedEntityId, new HealthComponent
            {
                MaximumHealth = 200
            });

            Assert.AreEqual(2, denseSet.Count, "DenseSet component count is incorrect after merge.");
            Assert.IsTrue(denseSet.HasComponent(untouchedEntityId));
            Assert.AreEqual(50, denseSet.Get(untouchedEntityId).MaximumHealth);
            Assert.IsTrue(denseSet.HasComponent(mergedEntityId), "DenseSet does not have a component for entityId 0 when it should.");
            Assert.AreEqual(150, denseSet.Get(mergedEntityId).MaximumHealth);
        }

        [TestMethod]
        public void ResizeTest()
        {
            var initialCapacity = 4;
            var newCapacity = 8;

            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var denseSet = new DenseSet<HealthComponent>(initialCapacity, mergeAction);

            denseSet.Add(0, new HealthComponent());
            denseSet.Add(1, new HealthComponent());
            denseSet.Add(3, new HealthComponent());

            denseSet.Resize(newCapacity);

            Assert.AreEqual(3, denseSet.Count, "DenseSet component count is incorrect after resize.");
            Assert.HasCount(newCapacity, denseSet.GetAll(), "AllComponents count does not match expectedResizeCapacity");
            Assert.IsTrue(denseSet.HasComponent(0), "DenseSet does not have a component for entityId 0 when it should.");
            Assert.IsTrue(denseSet.HasComponent(1), "DenseSet does not have a component for entityId 1 when it should.");
            Assert.IsFalse(denseSet.HasComponent(2), "DenseSet has a component for entityId 2 when it should not.");
            Assert.IsTrue(denseSet.HasComponent(3), "DenseSet does not have a component for entityId 3 when it should.");
            Assert.IsFalse(denseSet.HasComponent(4), "DenseSet has a component for entityId 4 when it should not.");
            Assert.IsFalse(denseSet.HasComponent(newCapacity - 1));
            Assert.Throws<IndexOutOfRangeException>(() => denseSet.HasComponent(newCapacity));
        }

        [TestMethod]
        public void RemoveNonexistedComponentTest()
        {
            var initialCapacity = 2;

            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var denseSet = new DenseSet<HealthComponent>(initialCapacity, mergeAction);
            denseSet.Add(0, new HealthComponent());

            denseSet.Remove(1);

            Assert.IsTrue(denseSet.HasComponent(0));
            Assert.IsFalse(denseSet.HasComponent(1));
        }

        [TestMethod]
        public void RemoveComponentTest()
        {
            var initialCapacity = 3;

            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var denseSet = new DenseSet<HealthComponent>(initialCapacity, mergeAction);
            denseSet.Add(0, new HealthComponent());
            denseSet.Add(1, new HealthComponent());
            denseSet.Add(2, new HealthComponent());

            denseSet.Remove(1);

            Assert.IsTrue(denseSet.HasComponent(0));
            Assert.IsFalse(denseSet.HasComponent(1));
            Assert.IsTrue(denseSet.HasComponent(2));
        }
    }
}
