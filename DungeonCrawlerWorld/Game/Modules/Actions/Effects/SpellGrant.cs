using Engine.Tags;
using Game.Effects;
using Game.Modules.Auras;

namespace Game.Modules.Actions.Effects;

/// <summary>Teaches the target Spell for good: it becomes one of the target's own actions.</summary>
/// <remarks>
/// Spell is the whole definition, so a spell made at runtime from existing effects is taught as well as one the
/// content holds. A spell the session's ActionCatalog holds under the same Guid and definition is granted by id, as
/// any learned spell is; any other is granted with Spell as the target's own override, so it lives only on whoever
/// learned it and is never registered. A target that already has an action under Spell's Guid is refused
/// (EffectRefusal.SpellAlreadyKnown) and taught nothing.
/// </remarks>
/// <param name="Spell">The spell taught.</param>
public sealed record SpellGrant(ActionDefinition Spell) : IEffectEntry
{
    /// <remarks>The spell's own tags and everything its effects name: none of them is reachable through a definition the build walks.</remarks>
    public IEnumerable<GameplayTag> ReferencedTags
    {
        get
        {
            var tags = new List<GameplayTag>();
            foreach (var tag in Spell.DeclaredTags)
            {
                tags.Add(tag);
            }

            EffectEntries.ForEach(Spell, entry => tags.AddRange(entry.ReferencedTags));
            return tags;
        }
    }

    public IEnumerable<AuraDefinition> ReferencedAuras
    {
        get
        {
            var auras = new List<AuraDefinition>();
            EffectEntries.ForEach(Spell, entry => auras.AddRange(entry.ReferencedAuras));
            return auras;
        }
    }

    public IEnumerable<string> ReferencedTerrainKeys
    {
        get
        {
            var terrainKeys = new List<string>();
            EffectEntries.ForEach(Spell, entry => terrainKeys.AddRange(entry.ReferencedTerrainKeys));
            return terrainKeys;
        }
    }

    public EffectRefusal CanApply(in EffectContext context, ref EffectReservations reservations) =>
        context.Services.Actions.Has(context.TargetEntityId, Spell.Id) ? EffectRefusal.SpellAlreadyKnown : EffectRefusal.None;

    public EffectOutcome Apply(in EffectContext context)
    {
        var services = context.Services;
        if (services.Actions.Has(context.TargetEntityId, Spell.Id))
        {
            return EffectOutcome.NoEffect;
        }

        var overrideDefinition = services.ActionCatalog.TryGet(Spell.Id, out var catalogSpell) && Equals(catalogSpell, Spell) ? null : Spell;
        ActionGrantEffects.Grant(services.ComponentManager, services.ActionCatalog, context.TargetEntityId, Spell.Id, overrideDefinition);
        return EffectOutcome.Applied;
    }
}
