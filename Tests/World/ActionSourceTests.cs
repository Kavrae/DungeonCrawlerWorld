using System.Runtime.CompilerServices;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class ActionSourceTests
{
    /// <summary>Every holder keeps one per instance -- densest in the burning, poison, body-part burning and stat-modifier pools -- so it stays small and holds nothing the GC has to trace.</summary>
    [TestMethod]
    public void Size_IsEightBytesWithNoReferences()
    {
        Assert.AreEqual(8, Unsafe.SizeOf<ActionSource>());
        Assert.IsFalse(RuntimeHelpers.IsReferenceOrContainsReferences<ActionSource>());
    }

    [TestMethod]
    public void FromEntity_RecordsTheKeyAndTheNameAtThatMoment()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);
        componentManager.RegisterDirectPool<DisplayTextComponent>(static (ref existing, incoming) => existing = incoming);
        var entityKeys = new EntityKeys();
        var key = entityKeys.Issue(2);
        componentManager.Merge(2, new DisplayTextComponent("Goblin", "A goblin."));

        var source = ActionSource.FromEntity(componentManager, entityKeys, 2);
        componentManager.Merge(2, new DisplayTextComponent("Renamed", "Later."));

        Assert.AreEqual(key, source.Key);
        Assert.AreEqual("Goblin", source.Identity.DisplayName);
        Assert.IsTrue(source.IsEntity(key));
    }

    [TestMethod]
    public void IsEntity_NoneKey_IsNeverTrue()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);

        var keyless = ActionSource.FromEntity(componentManager, new EntityKeys(), 1);

        Assert.IsFalse(keyless.IsEntity(EntityKey.None));
        Assert.IsFalse(ActionSource.Admin.IsEntity(EntityKey.None));
    }

    [TestMethod]
    public void FromTerrain_KeepsTheTerrainTypeAndHasNoIdentity()
    {
        var source = ActionSource.FromTerrain(7);

        Assert.AreEqual((ushort)7, source.TerrainTypeId);
        Assert.AreEqual("Unknown", source.Identity.DisplayName);
        Assert.IsTrue(source.Key.IsNone);
    }

    [TestMethod]
    public void EntityIdentities_SameIdentity_SameHandle_DifferentCrawler_DifferentHandle()
    {
        var goblin = EntityIdentities.Intern(new EntityIdentity("Goblin", null));

        Assert.AreEqual(goblin, EntityIdentities.Intern(new EntityIdentity("Goblin", null)));
        Assert.AreNotEqual(goblin, EntityIdentities.Intern(new EntityIdentity("Goblin", 17)));
        Assert.AreEqual("Goblin (Crawler #17)", EntityIdentities.Get(EntityIdentities.Intern(new EntityIdentity("Goblin", 17))).DisplayName);
    }
}
