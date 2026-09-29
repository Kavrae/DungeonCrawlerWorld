using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Blueprints;
using Game.Spawning;
using Game.World;

namespace Game.Modules.Lootboxes;

/// <summary>Grants the player the loot box a slain entity declares, when the player landed the killing blow.</summary>
/// <remarks>
/// The box is read from what the entity was built from, never from a component, so an entity that died
/// before it was ever built past a skeleton pays out the same. Composition's rule decides which box: the
/// most recently applied part that declares one (EntityFactory.Apply), else the spawn blueprint's own
/// resolved box -- the same order EntityActions reads actions in. Only the killing blow counts, and one
/// death grants at most one box; several boxes per fight is TODO.md's "Advanced boss loot box awards".
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class BossLootboxAwarder(
    ComponentManager componentManager,
    LootboxCatalog lootboxCatalog,
    EventBus eventBus,
    IPlayerQuery playerQuery,
    BlueprintRegistry definitions,
    DirectComponentPool<SpawnRecordComponent> spawnRecords,
    MultiComponentPool<AppliedBlueprintComponent> appliedParts)
{
    public void OnEntityDied(EntityDiedEvent died)
    {
        if (died.EntityId == playerQuery.PlayerEntityId || !died.Source.IsEntity(playerQuery.PlayerEntityKey))
        {
            return;
        }

        if (TryGetLootbox(died.EntityId, out var reward))
        {
            LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, playerQuery.PlayerEntityId, reward);
        }
    }

    /// <summary>The loot box killing entityId would grant, if it declares one.</summary>
    public bool TryGetLootbox(int entityId, out LootboxReward reward)
    {
        LootboxReward? latestApplied = null;
        var latestOrder = -1;
        for (var denseIndex = appliedParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = appliedParts.GetNextDenseIndex(denseIndex))
        {
            var applied = appliedParts.GetReadonlyByDenseIndex(denseIndex);
            if (applied.Order > latestOrder && definitions.TryGet(applied.BlueprintId, out var part) && part.Lootbox is { } partLootbox)
            {
                latestApplied = partLootbox;
                latestOrder = applied.Order;
            }
        }

        if (latestApplied is not null)
        {
            reward = latestApplied;
            return true;
        }

        if (spawnRecords.TryGetReadonly(entityId, out var record) &&
            definitions.TryResolve(record.BlueprintId, out var blueprint) &&
            blueprint.Lootbox is { } spawnLootbox)
        {
            reward = spawnLootbox;
            return true;
        }

        reward = null!;
        return false;
    }
}
