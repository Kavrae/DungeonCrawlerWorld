using Engine.ECS.Components.Stores;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;

namespace Tests.Modules.Mana;

[TestClass]
public sealed class ManaRestoreTests
{
    private static PackedComponentPool<ManaComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Apply_RaisesCurrentManaByAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 50, maximumMana: 100));

        ManaRestore.Apply(pool, 0, 10);

        Assert.AreEqual(60, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Apply_ClampsAtMaximumMana()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 95, maximumMana: 100));

        ManaRestore.Apply(pool, 0, 50);

        Assert.AreEqual(100, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Apply_NoManaComponent_DoesNotThrow()
    {
        var pool = CreatePool();

        ManaRestore.Apply(pool, 0, 10);
    }
}
