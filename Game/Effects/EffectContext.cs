using Engine.Math;
using Engine.Tags;
using Game.Modules.Health;
using Game.World;

namespace Game.Effects;

/// <summary>One application of effects to one target: who caused it, whom it lands on, and when.</summary>
/// <remarks>
/// <para>
/// Built once per application with everything but the target fixed, then varied per target with
/// `context with { TargetEntityId = id }`. A struct passed by `in`, so applying effects allocates
/// nothing.
/// </para>
/// <para>
/// Source is who the effect is attributed to -- a damage event, a stat modifier, a status effect's
/// stacks. SourceEntityId is the live entity whose own stats shape the effect (ability-score
/// bonus, Outgoing modifiers, crit), and is null when nothing of the kind exists: terrain, an
/// aura. An entry skips everything that reads the source entity when there is none.
/// </para>
/// <para>
/// ChainDepth is only ever incremented by ChainedEffect, guarding against a proc that (directly or
/// via a longer cycle) triggers itself. DurationScaleMultiplier is 1 except for a scroll, which
/// scales by the caster's Intelligence (ScrollScalingEffects); a duration-bearing entry multiplies
/// its own base duration by it.
/// </para>
/// </remarks>
/// <param name="Services">The session's pools and services.</param>
/// <param name="ActivatorName">What applied the effect, as shown for its damage or healing: the action's or item's name.</param>
/// <param name="ActivatorTags">The tags of what applied the effect, for modifiers conditioned on one.</param>
/// <param name="Now">The simulation frame the application happens on. Required: an entry that starts a timer writes an absolute deadline from it (FrameDeadline.After(context.Now, frames)).</param>
public readonly record struct EffectContext(
    EffectServices Services,
    ActionSource Source,
    int? SourceEntityId,
    int TargetEntityId,
    string ActivatorName,
    GameplayTagSet ActivatorTags,
    long Now,
    float DurationScaleMultiplier = 1.0f,
    byte ChainDepth = 0)
{
    /// <summary>TargetEntityId of an application with no target entity: an entry placed once at TargetLocation.</summary>
    public const int NoTargetEntity = -1;

    /// <summary>The tile an activation is centred on, for an entry placed once there (EffectPlacement). Null where nothing was aimed at a tile: an aura's tick, a terrain contact.</summary>
    public Vector3Int? TargetLocation { get; init; }

    /// <summary>What an entry multiplies its amount by -- damage, healing, mana, stacks; never a duration or a stat modifier's magnitude.</summary>
    /// <remarks>1 for an action, an item and a terrain contact. An aura passes its power at the target's cell, so one definition is weaker further from its source.</remarks>
    public float Magnitude { get; init; } = 1.0f;

    /// <summary>The part touching the ground the effects came from, for an entry aimed at it (BodyPartTargeting.GroundContact). Set only by a terrain contact; null everywhere else, where such an entry names no part.</summary>
    public BodyPartTargetRule? GroundContactBodyPartRule { get; init; }

    /// <summary>Whether a refusal is reported: the "Immune" text and StatusEffectImmunityBlockedEvent.</summary>
    /// <remarks>False from something that keeps applying the same effects to the same target and has already reported this stay's refusal (an aura's tick, a contact's repeat).</remarks>
    public bool AnnouncesRefusal { get; init; } = true;

    /// <summary>The key of the toggle whose held effects are being applied or reverted, so each grant can be taken back without touching another toggle's. Null everywhere else.</summary>
    /// <remarks>Set only by Toggles. Unique among one entity's active toggles, and never 0.</remarks>
    public uint? HeldGrantKey { get; init; }

    /// <summary>A context for effects sourceEntityId causes, attributed to it as it is now.</summary>
    public static EffectContext FromEntity(EffectServices services, int sourceEntityId, int targetEntityId, string activatorName, GameplayTagSet activatorTags, long now, float durationScaleMultiplier = 1.0f) =>
        new(
            services,
            ActionSource.FromEntity(services.ComponentManager, services.EntityKeys, sourceEntityId, services.Definitions),
            sourceEntityId,
            targetEntityId,
            activatorName,
            activatorTags,
            now,
            durationScaleMultiplier);
}
