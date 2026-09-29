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
