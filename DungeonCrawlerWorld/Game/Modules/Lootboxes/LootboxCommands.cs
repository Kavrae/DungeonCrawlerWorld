namespace Game.Modules.Lootboxes;

/// <summary>A session's loot box actions, for code outside a system.</summary>
public sealed class LootboxCommands(LootboxOpener lootboxOpener)
{
    /// <inheritdoc cref="LootboxOpener.OpenAll"/>
    public IReadOnlyList<OpenedLootboxGroup> OpenAll(int entityId) => lootboxOpener.OpenAll(entityId);
}
