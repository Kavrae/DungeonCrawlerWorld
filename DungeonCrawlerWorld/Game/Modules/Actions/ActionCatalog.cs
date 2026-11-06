using Engine.Modules;
using Game.Content;

namespace Game.Modules.Actions;

/// <summary>A source of action definitions that aren't registered up front, asked when an id isn't in the catalog.</summary>
public interface IActionDefinitionSource
{
    /// <summary>Creates and registers the definition for actionId, if this source knows it.</summary>
    bool TryResolveAction(Guid actionId, out ActionDefinition definition);
}

/// <summary>Collects every ActionDefinition registered during IGameModule.Configure, keyed by Id.</summary>
/// <remarks>An id that isn't registered is offered to each IActionDefinitionSource in turn, so a game that loads content by id (Game.Content.ContentCatalogLoader) registers an action the first time anything asks for it.</remarks>
public sealed class ActionCatalog() : Catalog<ActionDefinition>(static definition => definition.Id)
{
    private readonly Dictionary<string, ActionDefinition> _derivedDefinitionsByKey = new(StringComparer.Ordinal);
    private readonly List<IActionDefinitionSource> _actionDefinitionSources = [];
    private readonly CachedDefinitionSummaries _summaries = new();
    private Func<IReadOnlyList<DefinitionSummary>>? _summarySource;

    /// <summary>The definition key names: derive applied to the action registered as baseActionId, made the first time it's asked for and the same instance every time after.</summary>
    /// <remarks>
    /// For a definition derived from a registered one that many entities share (a race's override of an action): one
    /// instance per session, as a static field once gave, but made from this session's definitions. A per-entity copy
    /// would allocate for every creature built, and asking again allocates nothing. key must be unique to what derive
    /// makes (e.g. "Goblin.QuickAttack").
    /// </remarks>
    public ActionDefinition GetOrCreateDerived(string key, Guid baseActionId, Func<ActionDefinition, ActionDefinition> derive)
    {
        if (!_derivedDefinitionsByKey.TryGetValue(key, out var definition))
        {
            definition = derive(Get(baseActionId));
            _derivedDefinitionsByKey[key] = definition;
        }

        return definition;
    }

    /// <summary>Adds a source asked for any id this catalog doesn't hold.</summary>
    public void AddDefinitionSource(IActionDefinitionSource actionDefinitionSource) => _actionDefinitionSources.Add(actionDefinitionSource);

    /// <summary>Makes Summaries read summaries from source -- every action the content holds, loaded or not -- as well as from what is registered.</summary>
    public void UseSummaries(Func<IReadOnlyList<DefinitionSummary>> source)
    {
        _summarySource = source;
        _summaries.Clear();
    }

    /// <summary>Every action this session can name, by Guid: the content's summaries, if the catalog has a source of them, and every registered definition beside them.</summary>
    /// <remarks>For a whole-catalog question, which must never depend on what happens to have been loaded.</remarks>
    public IReadOnlyList<DefinitionSummary> Summaries => _summaries.Get(Version, () => DefinitionSummaries.Of(_summarySource, Definitions, static action => DefinitionSummaries.Of(action)));

    public override bool TryGet(Guid id, out ActionDefinition definition)
    {
        if (base.TryGet(id, out definition))
        {
            return true;
        }

        foreach (var actionDefinitionSource in _actionDefinitionSources)
        {
            if (actionDefinitionSource.TryResolveAction(id, out definition))
            {
                return true;
            }
        }

        return false;
    }
}
