using Engine.ECS.Components;
using Game.Modules.Class;
using Game.Modules.Class.Components;

namespace Tests.Modules.Class;

[TestClass]
public sealed class ClassQueriesTests
{
    private const int EntityId = 0;
    private const ushort Tank = 1;
    private const ushort Engineer = 2;
    private const ushort IceMage = 3;
    private const ushort FireMage = 4;

    private static ComponentManager CreateManager()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        new ClassModule().RegisterComponents(componentManager);
        return componentManager;
    }

    private static List<ushort> ClassesOf(ComponentManager componentManager, int entityId)
    {
        var classes = new List<ushort>();
        ClassQueries.CopyTo(componentManager.GetPackedPool<ClassSlotsComponent>(), componentManager.GetMultiPool<ClassMembershipComponent>(), entityId, classes);
        return classes;
    }

    [TestMethod]
    public void Grant_FillsBothSlotsFirst_ThenBecomesAMembership()
    {
        var componentManager = CreateManager();

        ClassEffects.Grant(componentManager, EntityId, Tank, ClassGrantKind.Spawn);
        ClassEffects.Grant(componentManager, EntityId, Engineer, ClassGrantKind.Advancement);
        ClassEffects.Grant(componentManager, EntityId, IceMage, ClassGrantKind.Achievement);

        var slots = componentManager.GetPackedPool<ClassSlotsComponent>().GetReadonly(EntityId);
        Assert.AreEqual(Tank, slots.Class1);
        Assert.AreEqual(Engineer, slots.Class2);
        Assert.AreEqual(1, componentManager.GetMultiPool<ClassMembershipComponent>().CountForEntity(EntityId));
        CollectionAssert.AreEqual(new List<ushort> { Tank, Engineer, IceMage }, ClassesOf(componentManager, EntityId));
    }

    /// <summary>A class held for one floor never takes a permanent slot, even when one is free.</summary>
    [TestMethod]
    public void Grant_WithAnExpiry_IsAlwaysAMembership()
    {
        var componentManager = CreateManager();

        ClassEffects.Grant(componentManager, EntityId, Tank, ClassGrantKind.TemporaryForFloor, expiresAfterFloor: 3);

        Assert.IsFalse(componentManager.GetPackedPool<ClassSlotsComponent>().Has(EntityId));
        Assert.IsTrue(ClassQueries.Has(componentManager.GetPackedPool<ClassSlotsComponent>(), componentManager.GetMultiPool<ClassMembershipComponent>(), EntityId, Tank));
        Assert.IsTrue(ClassQueries.TryGetPrimary(componentManager.GetPackedPool<ClassSlotsComponent>(), componentManager.GetMultiPool<ClassMembershipComponent>(), EntityId, out var primary));
        Assert.AreEqual(Tank, primary);
    }

    [TestMethod]
    public void Grant_DoesNotDuplicateAClassAlreadyHeld()
    {
        var componentManager = CreateManager();

        ClassEffects.Grant(componentManager, EntityId, Tank, ClassGrantKind.Spawn);
        ClassEffects.Grant(componentManager, EntityId, Tank, ClassGrantKind.Achievement);

        CollectionAssert.AreEqual(new List<ushort> { Tank }, ClassesOf(componentManager, EntityId));
        Assert.IsEmpty(ClassesOf(componentManager, 1), "Another entity holds nothing.");
    }

    /// <summary>Four Seasons' four elements: two in slots, the rest beside them, read back in the order they were acquired.</summary>
    [TestMethod]
    public void CopyTo_ReturnsMembershipsInAcquisitionOrder()
    {
        var componentManager = CreateManager();

        ClassEffects.Grant(componentManager, EntityId, IceMage, ClassGrantKind.Advancement);
        ClassEffects.Grant(componentManager, EntityId, FireMage, ClassGrantKind.Advancement);
        ClassEffects.Grant(componentManager, EntityId, Tank, ClassGrantKind.Advancement);
        ClassEffects.Grant(componentManager, EntityId, Engineer, ClassGrantKind.Advancement);

        CollectionAssert.AreEqual(new List<ushort> { IceMage, FireMage, Tank, Engineer }, ClassesOf(componentManager, EntityId));
    }

    [TestMethod]
    public void ExpireForFloor_RemovesOnlyMembershipsThatRanOut()
    {
        var componentManager = CreateManager();

        ClassEffects.Grant(componentManager, EntityId, Tank, ClassGrantKind.Spawn);
        ClassEffects.Grant(componentManager, EntityId, IceMage, ClassGrantKind.TemporaryForFloor, expiresAfterFloor: 3);
        ClassEffects.Grant(componentManager, EntityId, FireMage, ClassGrantKind.TemporaryForFloor, expiresAfterFloor: 6);

        var removed = ClassEffects.ExpireForFloor(componentManager, EntityId, floor: 3);

        Assert.AreEqual(1, removed);
        CollectionAssert.AreEqual(new List<ushort> { Tank, FireMage }, ClassesOf(componentManager, EntityId));
    }

    [TestMethod]
    public void Remove_TakesAClassOutOfASlot_AndClosesTheGap()
    {
        var componentManager = CreateManager();

        ClassEffects.Grant(componentManager, EntityId, Tank, ClassGrantKind.Spawn);
        ClassEffects.Grant(componentManager, EntityId, Engineer, ClassGrantKind.Spawn);

        Assert.IsTrue(ClassEffects.Remove(componentManager, EntityId, Tank));

        var slots = componentManager.GetPackedPool<ClassSlotsComponent>().GetReadonly(EntityId);
        Assert.AreEqual(Engineer, slots.Class1);
        Assert.AreEqual(ClassSlotsComponent.Empty, slots.Class2);
        Assert.IsFalse(ClassEffects.Remove(componentManager, EntityId, Tank), "Removing a class it no longer holds changes nothing.");
    }
}
