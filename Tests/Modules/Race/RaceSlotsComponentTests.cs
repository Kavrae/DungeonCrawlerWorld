using Engine.ECS.Components.Stores;
using Game.Modules.Race.Components;

namespace Tests.Modules.Race;

[TestClass]
public sealed class RaceSlotsComponentTests
{
    private const ushort Goblin = 1;
    private const ushort Fairy = 2;
    private const ushort Ghost = 3;

    private static PackedComponentPool<RaceSlotsComponent> CreatePool() =>
        new(10, 10, static (ref existing, incoming) =>
        {
            existing.Add(incoming.Race1);
            existing.Add(incoming.Race2);
        });

    [TestMethod]
    public void Add_FillsTheSecondSlot_ThenDropsAnythingFurther()
    {
        var slots = new RaceSlotsComponent(Goblin);

        slots.Add(Fairy);
        slots.Add(Ghost);

        Assert.AreEqual(Goblin, slots.Race1);
        Assert.AreEqual(Fairy, slots.Race2);
        Assert.AreEqual(Goblin, slots.Primary);
        Assert.IsFalse(slots.Has(Ghost));
    }

    [TestMethod]
    public void Add_IgnoresARaceAlreadyHeld_AndTheEmptyId()
    {
        var slots = new RaceSlotsComponent(Goblin);

        slots.Add(Goblin);
        slots.Add(RaceSlotsComponent.Empty);

        Assert.AreEqual(RaceSlotsComponent.Empty, slots.Race2);
    }

    /// <summary>Two race blueprints on one entity, the way a composite build merges them -- see TestMapBuilder's Goblin-with-Fairy fixture.</summary>
    [TestMethod]
    public void Merge_KeepsBothRaces_InTheOrderTheyWereBuilt()
    {
        var pool = CreatePool();

        pool.Merge(0, new RaceSlotsComponent(Goblin));
        pool.Merge(0, new RaceSlotsComponent(Fairy));

        var slots = pool.GetReadonly(0);
        Assert.AreEqual(Goblin, slots.Race1);
        Assert.AreEqual(Fairy, slots.Race2);
    }
}
