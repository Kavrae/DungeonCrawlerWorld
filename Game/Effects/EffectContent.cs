using Game.Modules;

namespace Game.Effects;

/// <summary>Walks every effect entry a build's registered content holds, whatever holds it.</summary>
/// <remarks>
/// The one enumeration the content checks share (ContentTagValidation, AuraContentRegistration), so a
/// new kind of effect holder is added here once and every check covers it: actions, items, terrain
/// contacts and aura definitions, and the effects an entry applies in turn (a ChainedEffect's), to
/// any depth.
/// </remarks>
internal static class EffectContent
{
    /// <summary>Calls visit with every entry and a description of what holds it ("Action 'Fireball'").</summary>
    public static void ForEachEntry(GameModuleContext context, Action<IEffectEntry, string> visit)
    {
        foreach (var action in context.Actions.Definitions)
        {
            Visit(action.Effects, $"Action '{action.Name}'", visit);
        }

        foreach (var item in context.Items.Definitions)
        {
            Visit(item.Effects, $"Item '{item.Name}'", visit);
        }

        var terrain = context.Terrain;
        for (var terrainTypeId = 1; terrainTypeId <= terrain.Count; terrainTypeId++)
        {
            if (terrain.TryGet((ushort)terrainTypeId, out var terrainDefinition) && terrainDefinition.Contact is { } contact)
            {
                Visit(contact.Effects, $"Terrain '{terrainDefinition.Name}'", visit);
            }
        }

        var auras = context.Auras;
        for (var auraId = 0; auraId < auras.Count; auraId++)
        {
            var aura = auras.Get((byte)auraId);
            Visit(aura.Effects, $"Aura '{aura.Name}'", visit);
        }
    }

    private static void Visit(IReadOnlyList<Effect> effects, string heldBy, Action<IEffectEntry, string> visit)
    {
        foreach (var effect in effects)
        {
            foreach (var entry in effect.Entries)
            {
                visit(entry, heldBy);
                Visit(entry.NestedEffects, heldBy, visit);
            }
        }
    }
}
