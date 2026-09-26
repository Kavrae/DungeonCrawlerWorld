using Engine.ECS.Components.Stores;
using Game.Modules.Class.Components;

namespace Game.Modules.Class;

/// <summary>Reads the classes an entity holds -- its two slots plus any memberships beyond them, as one set.</summary>
/// <remarks>
/// The only place that knows classes live in two components at all. Slots come first and in slot
/// order; memberships follow in acquisition order, so a caller that cares which class was taken first
/// (a subclass deriving from its parent) reads the list in order, while one that cares only about
/// which classes are held (Four Seasons' combination spells) reads it as a set. A null membership
/// pool means the same as an empty one -- ClassMembershipComponent is optional the way every other
/// cross-module pool is.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class ClassQueries
{
    /// <summary>True when the entity holds classId, in a slot or a membership.</summary>
    public static bool Has(PackedComponentPool<ClassSlotsComponent> slots, MultiComponentPool<ClassMembershipComponent>? memberships, int entityId, ushort classId)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (classId == ClassSlotsComponent.Empty)
        {
            return false;
        }

        if (slots.TryGetReadonly(entityId, out var entitySlots) && entitySlots.Has(classId))
        {
            return true;
        }

        if (memberships is null)
        {
            return false;
        }

        for (var denseIndex = memberships.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = memberships.GetNextDenseIndex(denseIndex))
        {
            if (memberships.GetReadonlyByDenseIndex(denseIndex).ClassId == classId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The class an entity counts as when only one can be meant -- its first slot, or its earliest membership if it has no slots.</summary>
    public static bool TryGetPrimary(PackedComponentPool<ClassSlotsComponent> slots, MultiComponentPool<ClassMembershipComponent>? memberships, int entityId, out ushort classId)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (slots.TryGetReadonly(entityId, out var entitySlots) && entitySlots.Primary != ClassSlotsComponent.Empty)
        {
            classId = entitySlots.Primary;
            return true;
        }

        classId = ClassSlotsComponent.Empty;
        return memberships is not null && TryGetNextInAcquisitionOrder(memberships, entityId, after: -1, out classId, out _);
    }

    /// <summary>Copies every class the entity holds into destination -- slots in slot order, then memberships in acquisition order.</summary>
    /// <returns>How many were copied.</returns>
    public static int CopyTo(PackedComponentPool<ClassSlotsComponent> slots, MultiComponentPool<ClassMembershipComponent>? memberships, int entityId, List<ushort> destination)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(destination);

        var before = destination.Count;

        if (slots.TryGetReadonly(entityId, out var entitySlots))
        {
            AddIfHeld(destination, entitySlots.Class1);
            AddIfHeld(destination, entitySlots.Class2);
        }

        if (memberships is not null)
        {
            var after = -1;
            while (TryGetNextInAcquisitionOrder(memberships, entityId, after, out var classId, out after))
            {
                AddIfHeld(destination, classId);
            }
        }

        return destination.Count - before;
    }

    /// <summary>The entity's membership with the lowest AcquisitionOrder above after -- one step of walking them in order, since the pool hands them back in whatever order its chain holds.</summary>
    private static bool TryGetNextInAcquisitionOrder(MultiComponentPool<ClassMembershipComponent> memberships, int entityId, int after, out ushort classId, out int order)
    {
        classId = ClassSlotsComponent.Empty;
        order = int.MaxValue;

        for (var denseIndex = memberships.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = memberships.GetNextDenseIndex(denseIndex))
        {
            ref readonly var membership = ref memberships.GetReadonlyByDenseIndex(denseIndex);
            if (membership.AcquisitionOrder > after && membership.AcquisitionOrder < order)
            {
                order = membership.AcquisitionOrder;
                classId = membership.ClassId;
            }
        }

        return classId != ClassSlotsComponent.Empty;
    }

    private static void AddIfHeld(List<ushort> destination, ushort classId)
    {
        if (classId != ClassSlotsComponent.Empty && !destination.Contains(classId))
        {
            destination.Add(classId);
        }
    }
}
