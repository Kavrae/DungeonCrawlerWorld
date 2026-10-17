using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Class.Components;

namespace Game.Modules.Class;

/// <summary>
/// Write surface for an entity's classes: a permanent class fills a free slot, anything else becomes
/// a membership beside them (see ClassMembershipComponent). Blueprints still merge
/// ClassSlotsComponent directly -- they run before an entity holds anything else, and every class
/// they grant is permanent.
/// </summary>
/// <remarks>Storage ahead of its first consumer: nothing grants a class at runtime yet. See ClassMembershipComponent's own doc comment.</remarks>
public static class ClassEffects
{
    /// <summary>Grants classId, in a free slot when it is permanent and one is free, as a membership otherwise. No-ops if the entity already holds it.</summary>
    /// <param name="expiresAfterFloor">The floor after which the class is removed, or ClassMembershipComponent.Permanent.</param>
    public static void Grant(ComponentManager componentManager, int entityId, ushort classId, ClassGrantKind grantedBy, ushort expiresAfterFloor = ClassMembershipComponent.Permanent)
    {
        if (classId == ClassSlotsComponent.Empty)
        {
            return;
        }

        var slots = componentManager.GetPackedPool<ClassSlotsComponent>();
        var memberships = componentManager.GetMultiPool<ClassMembershipComponent>();

        if (ClassQueries.Has(slots, memberships, entityId, classId))
        {
            return;
        }

        if (expiresAfterFloor == ClassMembershipComponent.Permanent && HasFreeSlot(slots, entityId))
        {
            slots.Merge(entityId, new ClassSlotsComponent(classId));
            return;
        }

        var acquisitionOrder = (byte)System.Math.Min(byte.MaxValue, CountHeld(slots, memberships, entityId));
        memberships.Add(entityId, new ClassMembershipComponent(classId, grantedBy, acquisitionOrder, expiresAfterFloor));
    }

    /// <summary>Removes classId from wherever the entity holds it, closing the slot gap so Class1 stays the primary.</summary>
    /// <returns>True if it held it.</returns>
    public static bool Remove(ComponentManager componentManager, int entityId, ushort classId)
    {
        var removed = componentManager.GetMultiPool<ClassMembershipComponent>()
            .RemoveFirst(entityId, classId, static (ref readonly ClassMembershipComponent membership, ushort id) => membership.ClassId == id);

        var slots = componentManager.GetPackedPool<ClassSlotsComponent>();
        if (slots.TryGetReadonly(entityId, out var entitySlots) && entitySlots.Has(classId))
        {
            slots.TryUpdate(entityId, classId, static (ref ClassSlotsComponent current, ushort id) =>
            {
                if (current.Class1 == id)
                {
                    current.Class1 = current.Class2;
                    current.Class2 = ClassSlotsComponent.Empty;
                }
                else
                {
                    current.Class2 = ClassSlotsComponent.Empty;
                }
            });
            removed = true;
        }

        return removed;
    }

    /// <summary>Removes every membership held only for floor or earlier -- what a floor-end event calls (see TODO.md's Former Child Actress rule, whose residue roll runs before this).</summary>
    /// <returns>How many were removed.</returns>
    public static int ExpireForFloor(ComponentManager componentManager, int entityId, ushort floor)
    {
        var memberships = componentManager.GetMultiPool<ClassMembershipComponent>();
        var removed = 0;
        while (memberships.RemoveFirst(entityId, floor, static (ref readonly ClassMembershipComponent membership, ushort completedFloor) =>
            !membership.IsPermanent && membership.ExpiresAfterFloor <= completedFloor))
        {
            removed++;
        }

        return removed;
    }

    private static bool HasFreeSlot(PackedComponentPool<ClassSlotsComponent> slots, int entityId) =>
        !slots.TryGetReadonly(entityId, out var entitySlots) || entitySlots.Class2 == ClassSlotsComponent.Empty;

    private static int CountHeld(PackedComponentPool<ClassSlotsComponent> slots, MultiComponentPool<ClassMembershipComponent> memberships, int entityId)
    {
        var held = memberships.CountForEntity(entityId);
        if (slots.TryGetReadonly(entityId, out var entitySlots))
        {
            held += entitySlots.Class1 == ClassSlotsComponent.Empty ? 0 : 1;
            held += entitySlots.Class2 == ClassSlotsComponent.Empty ? 0 : 1;
        }

        return held;
    }
}
