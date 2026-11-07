using Engine.Tags;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Inventory;
using Game.Modules.StatusEffects;
using Game.Tags;
using Game.Terrain;

namespace Game.Content;

/// <summary>The build's content checks for one definition at a time, for a definition made or overridden during a session: declared tags, toggle and targeting rules, the auras and terrain it names.</summary>
/// <remarks>
/// The same rules the build runs over its whole content after every Configure (ContentTagValidation,
/// ToggleContentValidation, TargetingContentValidation, AuraContentRegistration, TerrainReferenceValidation, StatusEffectContentValidation): each
/// whole-build check is its per-record form run over every definition, so the two can't disagree. Each problem is a
/// sentence naming the definition and the rule; none means it passes.
/// </remarks>
public sealed class ContentChecks(GameplayTagRegistry gameplayTags, AuraCatalog auras, TerrainRegistry terrain, StatusEffectApplierRegistry statusEffectAppliers)
{
    public IReadOnlyList<string> ProblemsOf(ActionDefinition action)
    {
        var heldBy = $"Action '{action.Name}'";
        return
        [
            .. ContentTagValidation.ProblemsOf(gameplayTags, action, heldBy),
            .. ToggleContentValidation.ProblemsOf(action),
            .. TargetingContentValidation.ProblemsOf(action),
            .. ReferenceProblemsOf(entries => EffectEntries.ForEach(action, entries), heldBy),
        ];
    }

    public IReadOnlyList<string> ProblemsOf(ItemDefinition item)
    {
        var heldBy = $"Item '{item.Name}'";
        return
        [
            .. ContentTagValidation.ProblemsOf(gameplayTags, item, heldBy),
            .. ToggleContentValidation.ProblemsOf(item),
            .. TargetingContentValidation.ProblemsOf(item),
            .. ReferenceProblemsOf(entries => EffectEntries.ForEach(item, entries), heldBy),
        ];
    }

    public IReadOnlyList<string> ProblemsOf(AuraDefinition aura)
    {
        var heldBy = $"Aura '{aura.Name}'";
        return
        [
            .. ContentTagValidation.ProblemsOf(gameplayTags, aura, heldBy),
            .. ReferenceProblemsOf(entries => EffectEntries.ForEach(aura.Effects, entries), heldBy),
        ];
    }

    /// <summary>What the entries forEachEntry visits name that this session can't take: terrain nothing registers, a duration for a status effect that takes none or of 0 ticks, and auras clashing with the catalog's.</summary>
    private List<string> ReferenceProblemsOf(Action<Action<IEffectEntry>> forEachEntry, string heldBy)
    {
        var problems = new List<string>();
        var namedAuras = new List<AuraDefinition>();
        forEachEntry(entry =>
        {
            problems.AddRange(TerrainReferenceValidation.ProblemsOf(terrain, entry, heldBy));
            problems.AddRange(StatusEffectContentValidation.ProblemsOf(statusEffectAppliers, entry, heldBy));
            namedAuras.AddRange(entry.ReferencedAuras);
        });

        problems.AddRange(AuraContentRegistration.ProblemsOf(auras, namedAuras, heldBy));
        return problems;
    }
}
