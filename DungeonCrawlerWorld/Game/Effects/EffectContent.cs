using Game.Modules;
using Game.Modules.Actions;

namespace Game.Effects;

/// <summary>Walks every effect entry a build's registered content holds, whatever holds it.</summary>
/// <remarks>
/// The one enumeration the content checks share (ContentTagValidation, AuraContentRegistration), over
/// EffectEntries (the walk inside one holder, which code outside Game uses too), so a
/// new kind of effect holder is added here once and every check covers it: actions and items (their
/// activation effects and a toggle's periodic effects included), terrain contacts and aura definitions, and
/// the effects an entry applies in turn (a ChainedEffect's), to any depth.
/// </remarks>
internal static class EffectContent
{
    /// <summary>Calls visit with every entry and a description of what holds it ("Action 'Fireball'").</summary>
    public static void ForEachEntry(GameModuleContext context, Action<IEffectEntry, string> visit)
    {
        foreach (var action in context.Actions.Definitions)
        {
            var heldBy = $"Action '{action.Name}'";
            EffectEntries.ForEach(action, entry => visit(entry, heldBy));
        }

        foreach (var item in context.Items.Definitions)
        {
            var heldBy = $"Item '{item.Name}'";
            EffectEntries.ForEach(item, entry => visit(entry, heldBy));
        }

        var terrain = context.Terrain;
        for (var terrainTypeId = 1; terrainTypeId <= terrain.Count; terrainTypeId++)
        {
            if (terrain.TryGet((ushort)terrainTypeId, out var terrainDefinition) && terrainDefinition.Contact is { } contact)
            {
                var heldBy = $"Terrain '{terrainDefinition.Name}'";
                EffectEntries.ForEach(contact.Effects, entry => visit(entry, heldBy));
            }
        }

        var auras = context.Auras;
        for (var auraId = 0; auraId < auras.Count; auraId++)
        {
            var aura = auras.Get((byte)auraId);
            var heldBy = $"Aura '{aura.Name}'";
            EffectEntries.ForEach(aura.Effects, entry => visit(entry, heldBy));
        }
    }
}
