using Engine.Modules;

namespace Game.Modules.Actions;

/// <summary>Collects every ActionDefinition registered during IGameModule.Configure, keyed by Id.</summary>
public sealed class ActionCatalog() : Catalog<ActionDefinition>(static definition => definition.Id);
