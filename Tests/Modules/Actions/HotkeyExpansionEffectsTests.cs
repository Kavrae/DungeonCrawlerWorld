using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class HotkeyExpansionEffectsTests
{
    private static PackedComponentPool<HotkeyExpansionUnlockComponent> CreatePool(int capacity = 10) =>
        new(capacity, capacity, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Grant_EntityHasComponent_IncrementsUnlockedSlotCount()
    {
        var pool = CreatePool();
        pool.Add(0, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 10));

        HotkeyExpansionEffects.Grant(pool, 0, amount: 5);

        Assert.AreEqual((short)15, pool.GetReadonly(0).UnlockedSlotCount);
    }

    [TestMethod]
    public void Grant_WouldExceedMax_ClampsToMaxUnlockedSlots()
    {
        var pool = CreatePool();
        pool.Add(0, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 18));

        HotkeyExpansionEffects.Grant(pool, 0, amount: 5);

        Assert.AreEqual(HotkeyExpansionEffects.MaxUnlockedSlots, pool.GetReadonly(0).UnlockedSlotCount);
    }

    [TestMethod]
    public void Grant_EntityHasNoComponent_DoesNotThrowAndGrantsNothing()
    {
        var pool = CreatePool();

        HotkeyExpansionEffects.Grant(pool, 0, amount: 5);

        Assert.IsFalse(pool.Has(0));
    }
}
