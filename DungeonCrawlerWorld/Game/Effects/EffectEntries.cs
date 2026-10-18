using Game.Modules.Actions;

namespace Game.Effects;

/// <summary>Every effect entry something holds, nested ones included -- the one walk over an action's or item's effects, so a new kind of effect holder is added in one place.</summary>
public static class EffectEntries
{
    /// <summary>Visits every entry of definition: its effects, its activation effects and its toggle's periodic effects, each nested entry after the entry holding it.</summary>
    public static void ForEach(ActivatableDefinition definition, Action<IEffectEntry> visit)
    {
        ForEach(definition.Effects, visit);
        ForEach(definition.ActivationEffects, visit);

        if (definition.Toggle?.Periodic is { } periodic)
        {
            ForEach(periodic.Effects, visit);
        }
    }

    /// <summary>Visits every entry of effects, each nested entry after the entry holding it.</summary>
    public static void ForEach(IReadOnlyList<Effect> effects, Action<IEffectEntry> visit)
    {
        foreach (var effect in effects)
        {
            foreach (var entry in effect.Entries)
            {
                visit(entry);
                ForEach(entry.NestedEffects, visit);
            }
        }
    }
}
