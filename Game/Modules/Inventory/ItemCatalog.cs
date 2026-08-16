using Engine.Modules;

namespace Game.Modules.Inventory;

/// <summary>Collects every ItemDefinition registered during IGameModule.Configure, keyed by Id.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ItemCatalog() : Catalog<ItemDefinition>(static definition => definition.Id);
