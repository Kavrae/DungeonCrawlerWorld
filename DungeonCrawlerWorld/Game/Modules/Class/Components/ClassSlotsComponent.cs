using Game.Blueprints;

namespace Game.Modules.Class.Components;

/// <summary>The classes an entity has, as BlueprintRegistry class ids -- two slots, since a creature has one or two classes.</summary>
/// <remarks>
/// See RaceSlotsComponent for why these are ids rather than names, and why a third is dropped. The
/// player's own class rules do not fit two fixed slots (a class granted for one floor only, a class
/// granted by an achievement on top of the others -- see TODO.md's deferred-build entry); those need
/// a sparse membership pool beside this one, which waits until something grants a class at runtime.
/// Nothing does today: every class comes from a blueprint at build time.
/// </remarks>
public struct ClassSlotsComponent(ushort class1, ushort class2 = BlueprintRegistry.None)
{
    public const ushort Empty = BlueprintRegistry.None;

    public ushort Class1 { get; set; } = class1;

    public ushort Class2 { get; set; } = class2;

    /// <summary>The class a creature counts as when only one can be meant -- the one shown beside its name.</summary>
    public readonly ushort Primary => Class1;

    public readonly bool Has(ushort classId) => classId != Empty && (Class1 == classId || Class2 == classId);

    /// <summary>Fills the first empty slot with classId, unless it is already held or both slots are full.</summary>
    public void Add(ushort classId)
    {
        if (classId == Empty || Has(classId))
        {
            return;
        }

        if (Class1 == Empty)
        {
            Class1 = classId;
        }
        else if (Class2 == Empty)
        {
            Class2 = classId;
        }
    }

    public override readonly string ToString() => $"Class1 : {Class1}\nClass2 : {Class2}";
}
