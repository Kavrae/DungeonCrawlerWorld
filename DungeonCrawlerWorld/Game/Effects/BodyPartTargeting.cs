using Game.Modules.Health;
using Game.Modules.Health.Components;

namespace Game.Effects;

/// <summary>Which body part an effect entry is aimed at.</summary>
public enum BodyPartTargetingKind : byte
{
    /// <summary>No part named. What that means is the entry's own: damage and healing follow their BodyPartTargetMode, a status effect is held on the entity as a whole.</summary>
    Unspecified,

    /// <summary>The part touching the ground the effect came from (TerrainContact.GroundContactBodyPart, or the bottommost part). Unspecified when the effect didn't come from terrain.</summary>
    GroundContact,

    /// <summary>A part picked at random each time the effect applies.</summary>
    Random,

    /// <summary>A part of the named type, or the fallback's pick when the target has none.</summary>
    Type,
}

/// <summary>Which body part an effect entry is aimed at, said by the entry itself.</summary>
/// <remarks>
/// Every entry that can land on one part takes one of these, so "which part" is always the entry's
/// own statement and never inferred from what applied the effect or from anything else the target
/// is standing in. A terrain contact's ground part reaches only the entries that ask for it
/// (GroundContact): lava's damage and burn land on the feet, while Holy Ground's blessing, which
/// asks for nothing, is the whole entity's. A target without body parts ignores all of it.
/// </remarks>
public readonly record struct BodyPartTargeting(BodyPartTargetingKind Kind, BodyPartType PartType = default, BodyPartFallback Fallback = BodyPartFallback.Random)
{
    public static readonly BodyPartTargeting Unspecified = default;

    public static readonly BodyPartTargeting GroundContact = new(BodyPartTargetingKind.GroundContact);

    public static readonly BodyPartTargeting Random = new(BodyPartTargetingKind.Random);

    /// <summary>A part of partType, or fallback's pick when the target has none.</summary>
    public static BodyPartTargeting Of(BodyPartType partType, BodyPartFallback fallback = BodyPartFallback.Random) =>
        new(BodyPartTargetingKind.Type, partType, fallback);

    /// <summary>The rule a part is picked by for this application, or null when no part is named.</summary>
    public BodyPartTargetRule? ResolveRule(in EffectContext context) => Kind switch
    {
        BodyPartTargetingKind.GroundContact => context.GroundContactBodyPartRule,
        BodyPartTargetingKind.Random => new BodyPartTargetRule(null, BodyPartFallback.Random),
        BodyPartTargetingKind.Type => new BodyPartTargetRule(PartType, Fallback),
        _ => null,
    };

    /// <summary>The one part of the target this application is aimed at, or null when no part is named or the target has no body parts.</summary>
    /// <remarks>
    /// A random pick prefers a part that isn't disabled. Every other pick ignores whether the part
    /// is disabled, so the same targeting keeps resolving to the same part for as long as it applies:
    /// what a terrain's contact holds on the feet stays on the feet after they are disabled rather
    /// than drifting to another part.
    /// </remarks>
    public byte? ResolvePartId(in EffectContext context)
    {
        if (ResolveRule(in context) is not { } rule || !context.Services.BodyParts.Has(context.TargetEntityId))
        {
            return null;
        }

        var partId = BodyPartSelection.PickByTypeWithFallback(context.Services.BodyParts, context.TargetEntityId, rule, context.Services.MathUtility, preferAlive: Kind == BodyPartTargetingKind.Random);
        return partId == -1 ? null : (byte)partId;
    }
}
