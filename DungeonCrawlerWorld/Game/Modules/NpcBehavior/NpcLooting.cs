using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.ItemPiles.Components;
using Game.Modules.NpcBehavior.Components;
using Game.Modules.ProcessingTier;
using Game.World;

namespace Game.Modules.NpcBehavior;

/// <summary>An NPC looting a corpse or an item pile it stands on or next to: everything it can carry, and all of its currency.</summary>
/// <remarks>
/// A candidate is simulated (nothing reaches across the frozen seam) and holds at least one stack or any currency: a corpse
/// not yet looted, or an item pile. The first corpse the NPC has the rights to (LootRights) is opened -- marked looted, as
/// the player's loot window marks a corpse it opens, whether or not anything fits -- and emptied into the NPC through
/// InventoryActions.LootEveryStack, so its stacks merge into what the NPC already carries. When every corpse is reserved
/// for someone else, the NPC tries no corpse again for RetryFrames. An item pile has no loot rights and is never marked
/// looted: it is taken from whenever anything in it fits, and passed over when nothing does. Opportunistic only: nothing
/// walks an NPC to either.
/// </remarks>
public sealed class NpcLooting(
    ComponentManager componentManager,
    ItemCatalog itemCatalog,
    IPlayerQuery playerQuery,
    EntityKeys entityKeys,
    IMapQuery mapQuery,
    ProcessingTierQuery tierQuery)
{
    public const int RetryFrames = 30 * GameFrameRate.FramesPerSecond;

    private readonly PackedComponentPool<DeadComponent> _deadEntities = componentManager.GetPackedPool<DeadComponent>();
    private readonly PackedComponentPool<LootedComponent> _looted = componentManager.GetPackedPool<LootedComponent>();
    private readonly PackedComponentPool<ItemPileComponent> _piles = componentManager.GetPackedPool<ItemPileComponent>();
    private readonly PackedComponentPool<CorpseLootRetryComponent> _retries = componentManager.GetPackedPool<CorpseLootRetryComponent>();
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
    private readonly PackedComponentPool<CurrencyComponent> _currencies = componentManager.GetPackedPool<CurrencyComponent>();
    private readonly LootRights _lootRights = LootRights.For(componentManager, entityKeys);

    /// <summary>Loots the first corpse looterEntityId may loot, or item pile it can take anything from, on or beside its footprint.</summary>
    /// <returns>Whether it looted one -- its decision for this visit.</returns>
    public bool TryLootNearby(int looterEntityId, TransformComponent looterTransform, long now)
    {
        var waitsToRetryCorpses = _retries.TryGetReadonly(looterEntityId, out var retry) && retry.RetryAfterFrame > now;
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
                    if (IsPileCandidate(occupantEntityId))
                    {
                        if (TakeEverything(occupantEntityId, looterEntityId))
                        {
                            return true;
                        }

                        continue;
                    }

                    if (waitsToRetryCorpses || !IsCorpseCandidate(occupantEntityId))
                    {
                        continue;
                    }

                    if (!_lootRights.CanLoot(occupantEntityId, looterEntityKey, now))
                    {
                        foundReservedCorpse = true;
                        continue;
                    }

                    LootCorpse(occupantEntityId, looterEntityId);
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
    private bool IsCorpseCandidate(int entityId) =>
        tierQuery.IsSimulated(entityId) &&
        _deadEntities.Has(entityId) &&
        !_looted.Has(entityId) &&
        HoldsLoot(entityId);

    private bool IsPileCandidate(int entityId) =>
        tierQuery.IsSimulated(entityId) &&
        _piles.Has(entityId) &&
        HoldsLoot(entityId);

    private bool HoldsLoot(int entityId) =>
        _inventoryStacks.Has(entityId) ||
        (_currencies.TryGetReadonly(entityId, out var currency) && (currency.Gold > 0 || currency.Credits > 0));

    /// <remarks>Marked first: a corpse whose loot is changing hands mid-scan is never a candidate again.</remarks>
    private void LootCorpse(int corpseEntityId, int looterEntityId)
    {
        componentManager.Merge(corpseEntityId, new LootedComponent());
        TakeEverything(corpseEntityId, looterEntityId);
    }

    /// <returns>Whether any stack or currency moved.</returns>
    private bool TakeEverything(int lootedEntityId, int looterEntityId)
    {
        var lootedStackCount = InventoryActions.LootEveryStack(componentManager, itemCatalog, lootedEntityId, looterEntityId, playerQuery);
        var tookCurrency = CurrencyActions.TryTransferAll(componentManager, lootedEntityId, looterEntityId);
        return lootedStackCount > 0 || tookCurrency;
    }
}
