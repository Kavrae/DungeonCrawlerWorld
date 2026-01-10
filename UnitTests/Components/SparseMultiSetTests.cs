using DungeonCrawlerWorld.Components;

namespace UnitTests.Components
{
    [TestClass]
    public sealed class SparseMultiSetTests
    {
        [TestMethod]
        public void NewTest()
        {
            var maximumEntityCount = 10;
            var initialCapacity = 5;

            var sparseMultiSet = new SparseMultiSet<RaceComponent>(maximumEntityCount, initialCapacity);
            Assert.AreEqual(0, sparseMultiSet.Count, "SparseMultiSet has a component count when it should not.");
            Assert.IsFalse(sparseMultiSet.HasComponent(0), "SparseMultiSet has a component for entityId 0 when it should not");
            var denseView = sparseMultiSet.GetDenseView();
            Assert.HasCount(initialCapacity, denseView.EntityIds, "AllComponents count does not match initial capacity");
            Assert.HasCount(initialCapacity, denseView.Components, "AllEntityIds count does not match initial capacity");

            for (var entityId = 0; entityId < maximumEntityCount; entityId++)
            {
                Assert.IsFalse(sparseMultiSet.HasComponent(entityId), $"SparseMultiSet has a component for entityId {entityId} when it should not");
            }
            Assert.Throws<IndexOutOfRangeException>(() => sparseMultiSet.HasComponent(maximumEntityCount));
        }

        [TestMethod]
        public void AddBelowInitialCapacityTest()
        {
            var maximumEntityCount = 16;
            var initialCapacity = 4;
            var sparseMultiSet = new SparseMultiSet<HealthComponent>(maximumEntityCount, initialCapacity);

            sparseMultiSet.Add(2, new HealthComponent { MaximumHealth = 10 });
            sparseMultiSet.Add(2, new HealthComponent { MaximumHealth = 20 });
            sparseMultiSet.Add(8, new HealthComponent { MaximumHealth = 30 });

            Assert.AreEqual(3, sparseMultiSet.Count, "SparseMultiSet component count is incorrect after resize.");
            Assert.IsFalse(sparseMultiSet.HasComponent(0));
            Assert.IsTrue(sparseMultiSet.HasComponent(2));
            Assert.IsTrue(sparseMultiSet.HasComponent(8));

            var componentList = sparseMultiSet.Get(2);
            Assert.HasCount(2, componentList);
            //Components are inserted to the front of the list. Order doesn't matter.
            Assert.AreEqual(20, componentList[0].MaximumHealth);
            Assert.AreEqual(10, componentList[1].MaximumHealth);

            componentList = sparseMultiSet.Get(8);
            Assert.HasCount(1, componentList);
            Assert.AreEqual(30, componentList[0].MaximumHealth);

            var denseSet = sparseMultiSet.GetDenseView();
            Assert.HasCount(initialCapacity, denseSet.EntityIds);
            Assert.HasCount(initialCapacity, denseSet.Components);
        }

        [TestMethod]
        public void AddPastInitialCapacityTest()
        {
            var maximumEntityCount = 16;
            var initialCapacity = 2;
            var sparseMultiSet = new SparseMultiSet<HealthComponent>(maximumEntityCount, initialCapacity);

            sparseMultiSet.Add(2, new HealthComponent { MaximumHealth = 10 });
            sparseMultiSet.Add(2, new HealthComponent { MaximumHealth = 20 });
            sparseMultiSet.Add(8, new HealthComponent { MaximumHealth = 30 });

            Assert.AreEqual(3, sparseMultiSet.Count, "SparseMultiSet component count is incorrect after resize.");
            Assert.IsFalse(sparseMultiSet.HasComponent(0));
            Assert.IsTrue(sparseMultiSet.HasComponent(2));
            Assert.IsTrue(sparseMultiSet.HasComponent(8));

            var componentList = sparseMultiSet.Get(2);
            Assert.HasCount(2, componentList);
            //Components are inserted to the front of the list. Order doesn't matter.
            Assert.AreEqual(20, componentList[0].MaximumHealth);
            Assert.AreEqual(10, componentList[1].MaximumHealth);

            componentList = sparseMultiSet.Get(8);
            Assert.HasCount(1, componentList);
            Assert.AreEqual(30, componentList[0].MaximumHealth);

            var denseSet = sparseMultiSet.GetDenseView();
            Assert.HasCount(initialCapacity * 2, denseSet.EntityIds);
            Assert.HasCount(initialCapacity * 2, denseSet.Components);
        }

        [TestMethod]
        public void ResizeTest()
        {
            var maximumEntityCount = 8;
            var newMaximumEntityCount = 16;
            var initialCapacity = 4;
            var sparseMultiSet = new SparseMultiSet<HealthComponent>(maximumEntityCount, initialCapacity);

            sparseMultiSet.Add(2, new HealthComponent { MaximumHealth = 10 });
            sparseMultiSet.Add(2, new HealthComponent { MaximumHealth = 20 });
            sparseMultiSet.Add(7, new HealthComponent { MaximumHealth = 30 });

            sparseMultiSet.Resize(newMaximumEntityCount);

            Assert.AreEqual(3, sparseMultiSet.Count, "SparseMultiSet component count is incorrect after resize.");
            Assert.IsFalse(sparseMultiSet.HasComponent(0));
            Assert.IsTrue(sparseMultiSet.HasComponent(2));
            Assert.IsTrue(sparseMultiSet.HasComponent(7));
            Assert.IsFalse(sparseMultiSet.HasComponent(15));

            var componentList = sparseMultiSet.Get(2);
            Assert.HasCount(2, componentList);

            componentList = sparseMultiSet.Get(7);
            Assert.HasCount(1, componentList);

            var denseSet = sparseMultiSet.GetDenseView();
            Assert.HasCount(initialCapacity, denseSet.EntityIds);
            Assert.HasCount(initialCapacity, denseSet.Components);
        }

        [TestMethod]
        public void RemoveNonexistedComponentTest()
        {
            var maximumEntityCount = 2;
            var initialCapacity = 2;

            var sparseMultiSet = new SparseMultiSet<HealthComponent>(maximumEntityCount, initialCapacity);
            sparseMultiSet.Add(0, new HealthComponent());

            sparseMultiSet.RemoveAll(1);

            Assert.IsTrue(sparseMultiSet.HasComponent(0));
            Assert.IsFalse(sparseMultiSet.HasComponent(1));
        }

        [TestMethod]
        public void RemoveAllTest()
        {
            var maximumEntityCount = 2;
            var initialCapacity = 4;

            var sparseMultiSet = new SparseMultiSet<HealthComponent>(maximumEntityCount, initialCapacity);
            sparseMultiSet.Add(0, new HealthComponent { MaximumHealth = 1 });
            sparseMultiSet.Add(0, new HealthComponent { MaximumHealth = 2 });
            sparseMultiSet.Add(1, new HealthComponent { MaximumHealth = 3 });
            sparseMultiSet.Add(1, new HealthComponent { MaximumHealth = 4 });

            sparseMultiSet.RemoveAll(0);

            //Components are removed for the correct entityId
            Assert.IsFalse(sparseMultiSet.HasComponent(0));
            Assert.IsTrue(sparseMultiSet.HasComponent(1));

            //Correct components are removed
            //Order is not guaranteed
            var componentList = sparseMultiSet.Get(1);
            Assert.HasCount(2, componentList);
            Assert.IsTrue(componentList.Any(component => component.MaximumHealth == 3));
            Assert.IsTrue(componentList.Any(component => component.MaximumHealth == 4));

            //Dense view is compressed after removal
            //Order is not guaranteed
            var denseView = sparseMultiSet.GetDenseView();
            Assert.AreEqual(2, denseView.Count);
            Assert.IsTrue(denseView.Components[0].MaximumHealth == 3 || denseView.Components[0].MaximumHealth == 4);
            Assert.IsTrue(denseView.Components[1].MaximumHealth == 3 || denseView.Components[1].MaximumHealth == 4);
        }

        //TODO RemoveFirst test
    }
}
