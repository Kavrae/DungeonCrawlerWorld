namespace Game.Modules.StatusEffects;

/// <summary>What one attempt to add a status effect's stacks did: how many landed, and whether an immunity refused them all.</summary>
/// <remarks>
/// The immunity is decided once, where the stacks are added, and carried back here -- so a grant that is
/// refused learns it without asking again. Zero stacks for any other reason (the stack cap, a
/// non-stacking effect refreshed in place) is not a refusal.
/// </remarks>
public readonly record struct StatusEffectStacksApplied(int StacksLanded, bool RefusedByImmunity)
{
    /// <summary>None landed: the entity is immune to the effect.</summary>
    public static StatusEffectStacksApplied Refused => new(0, RefusedByImmunity: true);

    /// <summary>stacksLanded stacks landed (0 when the cap or a refresh left nothing to add), with no immunity in the way.</summary>
    public static StatusEffectStacksApplied Landed(int stacksLanded) => new(stacksLanded, RefusedByImmunity: false);
}
