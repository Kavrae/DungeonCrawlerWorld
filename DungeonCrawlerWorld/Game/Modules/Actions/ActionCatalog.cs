using Engine.Modules;

namespace Game.Modules.Actions;

/// <summary>Collects every ActionDefinition registered during IGameModule.Configure, keyed by Id.</summary>
public sealed class ActionCatalog() : Catalog<ActionDefinition>(static definition => definition.Id)
{
    private readonly Dictionary<string, ActionDefinition> _derivedDefinitionsByKey = new(StringComparer.Ordinal);

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
}
