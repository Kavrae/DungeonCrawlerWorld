using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class HotkeyExpansionTests
{
    private static PackedComponentPool<HotkeyExpansionUnlockComponent> CreatePool(int capacity = 10) =>
        new(capacity, capacity, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Apply_EntityHasComponent_IncrementsUnlockedSlotCount()
    {
        var pool = CreatePool();
        pool.Add(0, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 10));

        HotkeyExpansion.Apply(pool, 0, amount: 5);

        Assert.AreEqual((short)15, pool.GetReadonly(0).UnlockedSlotCount);
    }

    [TestMethod]
    public void Apply_WouldExceedMax_ClampsToMaxUnlockedSlots()
    {
        var pool = CreatePool();
        pool.Add(0, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 18));

        HotkeyExpansion.Apply(pool, 0, amount: 5);

        Assert.AreEqual(HotkeyExpansion.MaxUnlockedSlots, pool.GetReadonly(0).UnlockedSlotCount);
    }

    [TestMethod]
    public void Apply_EntityHasNoComponent_DoesNotThrowAndGrantsNothing()
    {
        var pool = CreatePool();

        HotkeyExpansion.Apply(pool, 0, amount: 5);

        Assert.IsFalse(pool.Has(0));
    }
}
