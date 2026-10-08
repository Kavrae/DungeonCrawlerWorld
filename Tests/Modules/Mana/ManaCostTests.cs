using Engine.ECS.Components.Stores;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers.Components;

namespace Tests.Modules.Mana;

[TestClass]
public sealed class ManaCostTests
{
    private static PackedComponentPool<ManaComponent> CreatePool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Take_ReducesCurrentManaByAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 50, maximumMana: 100));

        ManaCost.Take(pool, 0, 10, EmptyPools.Multi<StatModifierComponent>());

        Assert.AreEqual(40, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Take_ClampsAtZero()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 5, maximumMana: 100));

        ManaCost.Take(pool, 0, 10, EmptyPools.Multi<StatModifierComponent>());

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void HasEnough_ExactlyTheAmountLeft_IsTrue()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 10, maximumMana: 100));

        Assert.IsTrue(ManaCost.HasEnough(pool, 0, 10));
        Assert.IsFalse(ManaCost.HasEnough(pool, 0, 10.5f));
    }

    [TestMethod]
    public void HasManaAndHasEnough_NoManaComponent_AreFalse()
    {
        var pool = CreatePool();

        Assert.IsFalse(ManaCost.HasMana(pool, 0));
        Assert.IsFalse(ManaCost.HasEnough(pool, 0, 0));
    }

    [TestMethod]
    public void Take_NoManaComponent_DoesNotThrow()
    {
        var pool = CreatePool();

        ManaCost.Take(pool, 0, 10, EmptyPools.Multi<StatModifierComponent>());
    }
}
