namespace Game.Modules.Class.Components;

/// <summary>Where a class an entity holds came from -- what removes it again, and whose name a grant it makes is attributed to.</summary>
/// <cleanupVersion>1</cleanupVersion>
public enum ClassGrantKind : byte
{
    /// <summary>Built in at spawn, from a class blueprint an entity was spawned from.</summary>
    Spawn,

    /// <summary>Chosen as the floor-3 class or a floor-6 subclass.</summary>
    Advancement,

    /// <summary>Granted by an achievement..</summary>
    Achievement,

    /// <summary>Held for this floor only.</summary>
    TemporaryForFloor,
}

/// <summary>One class an entity holds that its two class slots cannot express: a temporary one, or a third.</summary>
/// <remarks>
/// A MultiComponentPool entry per class beyond ClassSlotsComponent, which covers the one or two
/// permanent classes every NPC and most players have. The player's own rules need more: Four Seasons
/// can reach four elements (combination spells depend on the set held at the time, not the order),
/// Former Child Actress takes a different subclass every floor and keeps a seeded part of it at the
/// end, Oak Fell arrives from an achievement on top of everything else. All of that is sparse -- one
/// entity in the world has any of it -- so it costs nothing per creature to express here.
///
/// Nothing grants a class at runtime yet (every class today comes from a blueprint at build time), so
/// this and ClassQueries are storage ahead of their first consumer: the floor-end event, the
/// class-selection UI, the advancement rules and the residue roll are all still to come (TODO.md).
/// </remarks>
public struct ClassMembershipComponent(ushort classId, ClassGrantKind grantedBy, byte acquisitionOrder, ushort expiresAfterFloor = ClassMembershipComponent.Permanent)
{
    /// <summary>The ExpiresAfterFloor of a membership that never expires on its own.</summary>
    public const ushort Permanent = 0;

    public ushort ClassId { get; } = classId;

    public ClassGrantKind GrantedBy { get; } = grantedBy;

    /// <summary>Where this class sits in the order the entity acquired its classes -- what a subclass derives from, since a later class can depend on an earlier one.</summary>
    public byte AcquisitionOrder { get; } = acquisitionOrder;

    /// <summary>The floor after which this class is removed, or Permanent.</summary>
    public ushort ExpiresAfterFloor { get; } = expiresAfterFloor;

    public readonly bool IsPermanent => ExpiresAfterFloor == Permanent;

    public override readonly string ToString() =>
        $"ClassId : {ClassId}\nGrantedBy : {GrantedBy}\nAcquisitionOrder : {AcquisitionOrder}\nExpiresAfterFloor : {(IsPermanent ? "never" : ExpiresAfterFloor)}";
}
