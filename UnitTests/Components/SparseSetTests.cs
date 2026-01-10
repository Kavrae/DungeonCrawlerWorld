using DungeonCrawlerWorld.Components;
using Microsoft.Xna.Framework;

namespace UnitTests.Components
{
    [TestClass]
    public sealed class SparseSetTests
    {
        [TestMethod]
        public void NewTest()
        {
            var maximumEntityCount = 10;
            var initialCapacity = 5;
            MergeAction<BackgroundComponent> mergeAction = (ref BackgroundComponent existingComponent, BackgroundComponent newComponent) =>
            {
                existingComponent.BackgroundColor = Color.White;
            };

            var sparseSet = new SparseSet<BackgroundComponent>(maximumEntityCount, initialCapacity, mergeAction);
            Assert.AreEqual(0, sparseSet.Count, "SparseSet has a component count when it should not.");
            Assert.IsFalse(sparseSet.HasComponent(0), "SparseSet has a component for entityId 0 when it should not");
            Assert.HasCount(initialCapacity, sparseSet.GetAll(), "AllComponents count does not match initial capacity");
            Assert.HasCount(initialCapacity, sparseSet.GetAllEntityIds(), "AllEntityIds count does not match initial capacity");

            for (var entityId = 0; entityId < maximumEntityCount; entityId++)
            {
                Assert.IsFalse(sparseSet.HasComponent(entityId), $"SparseSet has a component for entityId {entityId} when it should not");
            }
            Assert.Throws<IndexOutOfRangeException>(() => sparseSet.HasComponent(maximumEntityCount));
        }

        [TestMethod]
        public void MergeTest()
        {
            var untouchedEntityId = 0;
            var mergedEntityId = 1;
            var maximumEntityCount = 2;
            var initialCapacity = 2;
            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var sparseSet = new SparseSet<HealthComponent>(maximumEntityCount, initialCapacity, mergeAction);

            sparseSet.Add(untouchedEntityId, new HealthComponent
            {
                MaximumHealth = 50
            });
            sparseSet.Add(mergedEntityId, new HealthComponent
            {
                MaximumHealth = 100
            });
            sparseSet.Add(mergedEntityId, new HealthComponent
            {
                MaximumHealth = 200
            });

            Assert.AreEqual(2, sparseSet.Count, "SparseSet component count is incorrect after merge.");
            Assert.IsTrue(sparseSet.HasComponent(untouchedEntityId));
            Assert.AreEqual(50, sparseSet.Get(untouchedEntityId).MaximumHealth);
            Assert.IsTrue(sparseSet.HasComponent(mergedEntityId), "SparseSet does not have a component for entityId 0 when it should.");
            Assert.AreEqual(150, sparseSet.Get(mergedEntityId).MaximumHealth);
        }

        [TestMethod]
        public void AddBelowInitialCapacityTest()
        {
            var maximumEntityCount = 16;
            var initialCapacity = 4;
            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var sparseSet = new SparseSet<HealthComponent>(maximumEntityCount, initialCapacity, mergeAction);

            sparseSet.Add(2, new HealthComponent());
            sparseSet.Add(4, new HealthComponent());
            sparseSet.Add(8, new HealthComponent());

            Assert.AreEqual(3, sparseSet.Count, "SparseSet component count is incorrect after resize.");
            Assert.HasCount(4, sparseSet.GetAll(), "AllComponents count does not match expectedResizeCapacity");
            Assert.HasCount(4, sparseSet.GetAllEntityIds(), "AllEntityIds count does not match expectedResizeCapacity");
            Assert.IsTrue(sparseSet.HasComponent(2), "SparseSet does not have a component for entityId 2 when it should.");
            Assert.IsTrue(sparseSet.HasComponent(4), "SparseSet does not have a component for entityId 4 when it should.");
            Assert.IsTrue(sparseSet.HasComponent(8), "SparseSet does not have a component for entityId 28 when it should.");
            Assert.IsFalse(sparseSet.HasComponent(1), "SparseSet has a component for entityId 1 when it should not.");
        }

        [TestMethod]
        public void AddPastInitialCapacityTest()
        {
            var maximumEntityCount = 16;
            var initialCapacity = 2;
            var expectedResizeCapacity = 4;
            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var sparseSet = new SparseSet<HealthComponent>(maximumEntityCount, initialCapacity, mergeAction);

            sparseSet.Add(2, new HealthComponent());
            sparseSet.Add(4, new HealthComponent());
            sparseSet.Add(8, new HealthComponent());

            Assert.AreEqual(3, sparseSet.Count, "SparseSet component count is incorrect after resize.");
            Assert.HasCount(expectedResizeCapacity, sparseSet.GetAll(), "AllComponents count does not match expectedResizeCapacity");
            Assert.HasCount(expectedResizeCapacity, sparseSet.GetAllEntityIds(), "AllEntityIds count does not match expectedResizeCapacity");
            Assert.IsTrue(sparseSet.HasComponent(2), "SparseSet does not have a component for entityId 2 when it should.");
            Assert.IsTrue(sparseSet.HasComponent(4), "SparseSet does not have a component for entityId 4 when it should.");
            Assert.IsTrue(sparseSet.HasComponent(8), "SparseSet does not have a component for entityId 28 when it should.");
            Assert.IsFalse(sparseSet.HasComponent(1), "SparseSet has a component for entityId 1 when it should not.");
        }

        [TestMethod]
        public void ResizeTest()
        {
            var maximumEntityCount = 16;
            var initialCapacity = 4;
            var newMaximumEntityCount = 64;
            var expectedComponentCount = 3;

            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var sparseSet = new SparseSet<HealthComponent>(maximumEntityCount, initialCapacity, mergeAction);

            sparseSet.Add(2, new HealthComponent());
            sparseSet.Add(4, new HealthComponent());
            sparseSet.Add(8, new HealthComponent());

            sparseSet.Resize(newMaximumEntityCount);

            Assert.AreEqual(expectedComponentCount, sparseSet.Count, "SparseSet component count is incorrect after resize.");
            Assert.HasCount(initialCapacity, sparseSet.GetAll(), "AllComponents count does not match expectedResizeCapacity");
            Assert.HasCount(initialCapacity, sparseSet.GetAllEntityIds(), "AllEntityIds count does not match expectedResizeCapacity");
            Assert.IsTrue(sparseSet.HasComponent(2), "SparseSet does not have a component for entityId 2 when it should.");
            Assert.IsTrue(sparseSet.HasComponent(4), "SparseSet does not have a component for entityId 4 when it should.");
            Assert.IsTrue(sparseSet.HasComponent(8), "SparseSet does not have a component for entityId 28 when it should.");
            Assert.IsFalse(sparseSet.HasComponent(1), "SparseSet has a component for entityId 1 when it should not.");
            Assert.IsFalse(sparseSet.HasComponent(newMaximumEntityCount - 1));
            Assert.Throws<IndexOutOfRangeException>(() => sparseSet.HasComponent(newMaximumEntityCount));
        }

        [TestMethod]
        public void RemoveNonexistedComponentTest()
        {
            var maximumEntityCount = 2;
            var initialCapacity = 2;

            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var sparseSet = new SparseSet<HealthComponent>(maximumEntityCount, initialCapacity, mergeAction);
            sparseSet.Add(0, new HealthComponent());

            sparseSet.Remove(1);

            Assert.IsTrue(sparseSet.HasComponent(0));
            Assert.IsFalse(sparseSet.HasComponent(1));
        }

        [TestMethod]
        public void RemoveLastComponentTest()
        {
            var maximumEntityCount = 2;
            var initialCapacity = 2;

            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var sparseSet = new SparseSet<HealthComponent>(maximumEntityCount, initialCapacity, mergeAction);
            sparseSet.Add(0, new HealthComponent
            {
                MaximumHealth = 100
            });
            sparseSet.Add(1, new HealthComponent
            {
                MaximumHealth = 200
            });

            sparseSet.Remove(1);

            Assert.IsTrue(sparseSet.HasComponent(0));
            Assert.AreEqual(100, sparseSet.Get(0).MaximumHealth);
            Assert.IsFalse(sparseSet.HasComponent(1));
        }

        [TestMethod]
        public void RemoveMiddleComponentTest()
        {
            var maximumEntityCount = 4;
            var initialCapacity = 4;

            MergeAction<HealthComponent> mergeAction = (ref HealthComponent existingComponent, HealthComponent newComponent) =>
            {
                existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
            };
            var sparseSet = new SparseSet<HealthComponent>(maximumEntityCount, initialCapacity, mergeAction);
            sparseSet.Add(0, new HealthComponent
            {
                MaximumHealth = 100
            });
            sparseSet.Add(1, new HealthComponent
            {
                MaximumHealth = 200
            });
            sparseSet.Add(2, new HealthComponent
            {
                MaximumHealth = 300
            });
            sparseSet.Add(3, new HealthComponent
            {
                MaximumHealth = 400
            });

            sparseSet.Remove(1);

            Assert.IsTrue(sparseSet.HasComponent(0));
            Assert.AreEqual(100, sparseSet.Get(0).MaximumHealth);

            Assert.IsFalse(sparseSet.HasComponent(1));

            Assert.IsTrue(sparseSet.HasComponent(2));
            Assert.AreEqual(300, sparseSet.Get(2).MaximumHealth);

            Assert.IsTrue(sparseSet.HasComponent(3));
            Assert.AreEqual(400, sparseSet.Get(3).MaximumHealth);

            //Component 3 is moved to position 1 to remove removed component. Components 0 and 2 don't change.
            var denseSet = sparseSet.GetAll();
            Assert.AreEqual(100, denseSet[0].MaximumHealth);
            Assert.AreEqual(400, denseSet[1].MaximumHealth);
            Assert.AreEqual(300, denseSet[2].MaximumHealth);
        }
    }
}
