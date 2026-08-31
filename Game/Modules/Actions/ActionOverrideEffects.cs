using Game.Modules.Actions.Effects;

namespace Game.Modules.Actions;

/// <summary>
/// Builds a per-instance ActionDefinition.Override for the common "flat damage differs per race"
/// case (ActionInstanceComponent.Override's own doc comment) -- finds the first DirectDamage entry
/// across baseDefinition's Effects and replaces its flat range with a fixed value, preserving every
/// other field on that entry (e.g. TargetBodyPartType) and everything else on the definition.
/// Centralizes the with-reconstruction in one tested place instead of every grant site hand-rolling
/// it and risking a silently-dropped field, mirroring WandGrantEffects.Grant's identical role for
/// item Overrides.
/// </summary>
public static class ActionOverrideEffects
{
    public static ActionDefinition OverrideFlatDamage(ActionDefinition baseDefinition, ushort flatDamage) =>
        baseDefinition with
        {
            Effects = baseDefinition.Effects
                .Select(effect => effect with { Entries = effect.Entries.Select(entry => ReplaceFlatDamage(entry, flatDamage)).ToList() })
                .ToList()
        };

    private static IActionEffectEntry ReplaceFlatDamage(IActionEffectEntry entry, ushort flatDamage) =>
        entry is DirectDamage directDamage
            ? directDamage with { MinFlatDamage = (short)flatDamage, MaxFlatDamage = (short)flatDamage }
            : entry;
}
