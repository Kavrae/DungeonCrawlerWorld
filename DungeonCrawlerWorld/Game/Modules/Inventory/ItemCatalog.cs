using Engine.Modules;

namespace Game.Modules.Inventory;

/// <summary>A source of item definitions that aren't registered up front, asked when an id isn't in the catalog.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface IItemDefinitionSource
{
    /// <summary>Creates and registers the definition for itemDefinitionId, if this source knows it.</summary>
    bool TryResolveItem(Guid itemDefinitionId, out ItemDefinition definition);
}

/// <summary>Collects every ItemDefinition registered during IGameModule.Configure, keyed by Id.</summary>
/// <remarks>An id that isn't registered is offered to each IItemDefinitionSource in turn, so a definition created on demand (a loot box) can be looked up before anything in this session created it.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ItemCatalog() : Catalog<ItemDefinition>(static definition => definition.Id)
{
    private readonly List<IItemDefinitionSource> _itemDefinitionSources = [];
    private readonly Dictionary<(string Kind, int Variant), ItemDefinition> _derivedDefinitions = [];

    /// <summary>The definition (kind, variant) names, made from this catalog by create the first time it's asked for and the same instance every time after.</summary>
    /// <remarks>
    /// For a definition derived from a registered one that many stacks share (a wand charged to a given number): one
    /// instance per session, as a static field once gave, but made from this session's definitions. kind is a constant
    /// unique to what create makes and variant tells its versions apart, so asking allocates nothing; create should be a
    /// static lambda for the same reason.
    /// </remarks>
    public ItemDefinition GetOrCreateDerived(string kind, int variant, Func<ItemCatalog, int, ItemDefinition> create)
    {
        if (!_derivedDefinitions.TryGetValue((kind, variant), out var definition))
        {
            definition = create(this, variant);
            _derivedDefinitions[(kind, variant)] = definition;
        }

        return definition;
    }

    /// <summary>Adds a source asked for any id this catalog doesn't hold.</summary>
    public void AddDefinitionSource(IItemDefinitionSource itemDefinitionSource) => _itemDefinitionSources.Add(itemDefinitionSource);

    public override bool TryGet(Guid id, out ItemDefinition definition)
    {
        if (base.TryGet(id, out definition))
        {
            return true;
        }

        foreach (var itemDefinitionSource in _itemDefinitionSources)
        {
            if (itemDefinitionSource.TryResolveItem(id, out definition))
            {
                return true;
            }
        }

        return false;
    }
}
