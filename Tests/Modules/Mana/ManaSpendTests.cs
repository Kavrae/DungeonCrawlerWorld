using Engine.ECS.Components.Stores;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;

namespace Tests.Modules.Mana;

[TestClass]
public sealed class ManaSpendTests
{
    private static PackedComponentPool<ManaComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Apply_ReducesCurrentManaByAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 50, maximumMana: 100));

        ManaSpend.Apply(pool, 0, 10);

        Assert.AreEqual(40, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Apply_ClampsAtZero()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 5, maximumMana: 100));

        ManaSpend.Apply(pool, 0, 10);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Apply_NoManaComponent_DoesNotThrow()
    {
        var pool = CreatePool();

        ManaSpend.Apply(pool, 0, 10);
    }
}
