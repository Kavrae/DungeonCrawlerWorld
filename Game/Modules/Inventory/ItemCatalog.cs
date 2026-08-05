namespace Game.Modules.Inventory;

/// <summary>
/// Collects every ItemDefinition registered during IGameModule.Configure, keyed by Id -- mirrors
/// AbilityCatalog: every module's Configure call completes before any RegisterSystems runs (see
/// GameBootstrapper.Build's ordering). A fresh instance per GameModuleContext, so a dry-run
/// mod-validation trial's registrations never leak into the real build's.
/// </summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<Guid, ItemDefinition> _definitionsById = [];

    public void Register(ItemDefinition definition) => _definitionsById[definition.Id] = definition;

    public bool TryGet(Guid itemId, out ItemDefinition definition) =>
        _definitionsById.TryGetValue(itemId, out definition!);
}
