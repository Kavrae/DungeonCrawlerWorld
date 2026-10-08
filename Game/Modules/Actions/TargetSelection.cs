using Engine.ECS.Entities;
using Engine.Math;

namespace Game.Modules.Actions;

/// <summary>How the caster aimed an activation: at whoever stands on a tile, or at the tile itself.</summary>
public enum TargetingMode : byte
{
    /// <summary>Marks the entity on the aimed tile; a Delayed activation lands wherever it is when the windup ends.</summary>
    Target,

    /// <summary>The aimed tiles, and whoever stands on them when the activation resolves.</summary>
    Ground,
}

/// <summary>What the caster chose when confirming an activation, carried on the request and the windup until TargetResolution turns it into tiles.</summary>
/// <remarks>
/// Built by TargetResolution.Select (or SelectEntity), never field by field: Range and AreaSize are
/// the effective values (a scroll's scaled by the caster's Intelligence), and a Target selection
/// holds a marked entity only when the spec allows Target mode and something stood on the tile. A
/// Target selection with nothing marked resolves as Ground.
/// </remarks>
/// <param name="Mode">The mode the caster aimed in.</param>
/// <param name="AimedTile">The tile aimed at. Where a Ground selection lands, and where a Target one lands if its entity is destroyed before it resolves.</param>
/// <param name="MarkedEntity">The marked entity, or EntityKey.None.</param>
/// <param name="MarkedFootprintOffset">Which tile of the marked entity's footprint was aimed at, from its top-left, so a large entity is hit where it was aimed.</param>
/// <param name="Range">The effective range.</param>
/// <param name="AreaSize">The effective area size.</param>
public readonly record struct TargetSelection(
    TargetingMode Mode,
    Vector3Int AimedTile,
    EntityKey MarkedEntity,
    Vector2Byte MarkedFootprintOffset,
    ushort Range,
    ushort AreaSize)
{
    public bool HasMarkedEntity => Mode == TargetingMode.Target && !MarkedEntity.IsNone;

    /// <summary>A Ground selection of aimedTile with spec's range and area as they are -- for an activation whose targeting is never scaled (a toggle, a melee swing).</summary>
    public static TargetSelection Ground(Vector3Int aimedTile, TargetingSpec spec) =>
        new(TargetingMode.Ground, aimedTile, EntityKey.None, default, ClampToUShort(spec.Range), ClampToUShort(spec.AreaSize));

    /// <summary>A Ground selection of aimedTile with no range or area -- for a toggle, which applies to its holder whatever is aimed at.</summary>
    public static TargetSelection Ground(Vector3Int aimedTile) => new(TargetingMode.Ground, aimedTile, EntityKey.None, default, 0, 0);

    internal static ushort ClampToUShort(int value) => (ushort)System.Math.Clamp(value, 0, ushort.MaxValue);

    public override string ToString() =>
        HasMarkedEntity
            ? $"{Mode} {MarkedEntity} (+{MarkedFootprintOffset.X},{MarkedFootprintOffset.Y}) aimed {AimedTile}, range {Range}, area {AreaSize}"
            : $"{Mode} {AimedTile}, range {Range}, area {AreaSize}";
}

/// <summary>Where a selection landed: the centre its shape was resolved around, and the marked entity it reached, if any.</summary>
/// <param name="Centre">The tile the shape is centred on: the marked entity's (clamped to range), the aimed tile, or the caster's for a Self shape.</param>
/// <param name="MarkedEntityId">The marked entity, still existing, or null.</param>
/// <param name="MarkedOnly">The activation affects only MarkedEntityId, not everyone on the tiles (TargetModeAffects.MarkedOnly).</param>
public readonly record struct ResolvedTargets(Vector3Int Centre, int? MarkedEntityId, bool MarkedOnly);
