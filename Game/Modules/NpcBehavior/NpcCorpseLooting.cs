using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Core.Components;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.NpcBehavior.Components;
using Game.Modules.ProcessingTier;
using Game.World;

namespace Game.Modules.NpcBehavior;

/// <summary>An NPC looting a corpse it stands on or next to: everything it can carry, and all of its currency.</summary>
/// <remarks>
/// A candidate is a corpse not yet looted, simulated (nothing reaches across the frozen seam), holding at least
/// one stack or any currency. The first one the NPC has the rights to (LootRights) is opened -- marked looted, as
/// the player's loot window marks a corpse it opens, whether or not anything fits -- and emptied into the NPC
/// through InventoryActions.LootEveryStack, so its stacks merge into what the NPC already carries. When every
/// candidate is reserved for someone else, the NPC tries no corpse again for RetryFrames. Opportunistic only:
/// nothing walks an NPC to a corpse.
/// </remarks>
public sealed class NpcCorpseLooting(
    ComponentManager componentManager,
    ItemCatalog itemCatalog,
    IPlayerQuery playerQuery,
    EntityKeys entityKeys,
    IMapQuery mapQuery,
    ProcessingTierQuery tierQuery)
{
    public const int RetryFrames = 30 * GameTiming.FramesPerSecond;

    private readonly PackedComponentPool<DeadComponent> _deadEntities = componentManager.GetPackedPool<DeadComponent>();
    private readonly PackedComponentPool<LootedComponent> _looted = componentManager.GetPackedPool<LootedComponent>();
    private readonly PackedComponentPool<CorpseLootRetryComponent> _retries = componentManager.GetPackedPool<CorpseLootRetryComponent>();
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
    private readonly PackedComponentPool<CurrencyComponent> _currencies = componentManager.GetPackedPool<CurrencyComponent>();
    private readonly LootRights _lootRights = LootRights.For(componentManager, entityKeys);

    /// <summary>Loots the first corpse looterEntityId may loot on or beside its footprint.</summary>
    /// <returns>Whether it looted one -- its decision for this visit.</returns>
    public bool TryLootNearbyCorpse(int looterEntityId, TransformComponent looterTransform, long now)
    {
        if (_retries.TryGetReadonly(looterEntityId, out var retry) && retry.RetryAfterFrame > now)
        {
            return false;
        }

        var looterEntityKey = entityKeys.GetKey(looterEntityId);
        var foundReservedCorpse = false;
        var position = looterTransform.Position;
        var size = looterTransform.Size;

        for (var y = position.Y - 1; y <= position.Y + size.Y; y++)
        {
            for (var x = position.X - 1; x <= position.X + size.X; x++)
            {
                foreach (var occupantEntityId in mapQuery.GetOccupantEntityIdSpanAt(new Vector3Int(x, y, position.Z)))
                {
                    if (!IsLootCandidate(occupantEntityId))
                    {
                        continue;
                    }

                    if (!_lootRights.CanLoot(occupantEntityId, looterEntityKey, now))
                    {
                        foundReservedCorpse = true;
                        continue;
                    }

                    Loot(occupantEntityId, looterEntityId);
                    return true;
                }
            }
        }

        if (foundReservedCorpse)
        {
            componentManager.Merge(looterEntityId, new CorpseLootRetryComponent(FrameDeadline.After(now, RetryFrames)));
        }

        return false;
    }

    /// <remarks>The tier first: a neighbouring tile can hold an unbuilt skeleton, whose other pools must not be read.</remarks>
    private bool IsLootCandidate(int entityId) =>
        tierQuery.IsSimulated(entityId) &&
        _deadEntities.Has(entityId) &&
        !_looted.Has(entityId) &&
        (_inventoryStacks.Has(entityId) || HoldsCurrency(entityId));

    private bool HoldsCurrency(int entityId) =>
        _currencies.TryGetReadonly(entityId, out var currency) && (currency.Gold > 0 || currency.Credits > 0);

    /// <remarks>Marked first: a corpse whose loot is changing hands mid-scan is never a candidate again.</remarks>
    private void Loot(int corpseEntityId, int looterEntityId)
    {
        componentManager.Merge(corpseEntityId, new LootedComponent());
        InventoryActions.LootEveryStack(componentManager, itemCatalog, corpseEntityId, looterEntityId, playerQuery);
        CurrencyActions.TryTransferAll(componentManager, corpseEntityId, looterEntityId);
    }
}
