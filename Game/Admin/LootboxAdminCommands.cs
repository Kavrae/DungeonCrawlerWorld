using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Lootboxes;

namespace Game.Admin;

/// <summary>Admin Mode's "Grant loot box" command: any registered type, at any rarity, granted to an entity.</summary>
/// <remarks>A debugging tool over LootboxActions.Grant, not a gameplay path. Types are read from the catalog each time they're asked for, so a mod's types appear too.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class LootboxAdminCommands(ComponentManager componentManager, LootboxCatalog lootboxCatalog, EventBus eventBus)
{
    /// <summary>Every registered loot box type, by name.</summary>
    public IReadOnlyList<LootboxTypeDefinition> Types()
    {
        var types = lootboxCatalog.Definitions.ToList();
        types.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return types;
    }

    /// <summary>Grants one box of typeId at rarity to entityId.</summary>
    public void Grant(int entityId, Guid typeId, LootboxRarity rarity) =>
        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, entityId, new LootboxReward(typeId, rarity));
}
