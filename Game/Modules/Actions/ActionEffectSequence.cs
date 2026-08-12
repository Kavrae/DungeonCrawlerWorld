namespace Game.Modules.Actions;

/// <summary>
/// Applies an ordered list of ActionEffect, in list order -- shared by IActionActivator
/// orchestration (an activator can trigger more than one ActionEffect, see IActionActivator.
/// Effects) and ChainedEffectEntry (a successful trigger can fire more than one ActionEffect too)
/// so both "trigger multiple ActionEffects" call sites share one implementation instead of each
/// re-writing the same loop.
/// </summary>
public static class ActionEffectSequence
{
    public static void Apply(IReadOnlyList<ActionEffect> effects, ActionEffectContext context)
    {
        foreach (var effect in effects)
        {
            effect.Apply(context);
        }
    }
}
