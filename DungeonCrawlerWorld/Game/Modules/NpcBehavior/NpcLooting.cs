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
/// In two steps, so a decision can choose before anything moves: TryFind only reads, naming the first candidate on or
/// beside the looter's footprint, and TryLoot takes from it, asking again whether it still may -- another NPC may have
/// looted it in between. A candidate is simulated (nothing reaches across the frozen seam) and holds at least one stack
/// or any currency. A corpse is one the NPC has the rights to (LootRights): opening it marks it looted, as the player's
/// loot window marks a corpse it opens, whether or not anything fits. When every corpse found is reserved for someone
/// else, the NPC tries no corpse again for RetryFrames (RetryLater). An item pile has no loot rights and is never marked
/// looted: it is a candidate while something in it fits the looter (InventoryActions.CanLootAny). Opportunistic only:
/// nothing walks an NPC to either.
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

    /// <summary>The first corpse looterEntityId may loot, or item pile it can take anything from, on or beside its footprint; reads only.</summary>
    /// <param name="everyCorpseReserved">Whether a corpse was found and every one found is reserved for someone else -- what RetryLater is for.</param>
    public bool TryFind(int looterEntityId, TransformComponent looterTransform, long now, out EntityKey target, out bool everyCorpseReserved)
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
                    if (IsPileCandidate(occupantEntityId, looterEntityId))
                    {
                        target = entityKeys.GetKey(occupantEntityId);
                        everyCorpseReserved = false;
                        return true;
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

                    target = entityKeys.GetKey(occupantEntityId);
                    everyCorpseReserved = false;
                    return true;
                }
            }
        }

        target = EntityKey.None;
        everyCorpseReserved = foundReservedCorpse;
        return false;
    }

    /// <summary>Loots target into looterEntityId if it is still a candidate TryFind would name for it; false, taking nothing, when it no longer is.</summary>
    public bool TryLoot(int looterEntityId, EntityKey target, long now)
    {
        if (!entityKeys.TryGetEntityId(target, out var targetEntityId))
        {
            return false;
        }

        if (IsPileCandidate(targetEntityId, looterEntityId))
        {
            return TakeEverything(targetEntityId, looterEntityId);
        }

        if (!IsCorpseCandidate(targetEntityId) || !_lootRights.CanLoot(targetEntityId, entityKeys.GetKey(looterEntityId), now))
        {
            return false;
        }

        componentManager.Merge(targetEntityId, new LootedComponent());
        TakeEverything(targetEntityId, looterEntityId);
        return true;
    }

    /// <summary>Has looterEntityId try no corpse again for RetryFrames.</summary>
    public void RetryLater(int looterEntityId, long now) =>
        componentManager.Merge(looterEntityId, new CorpseLootRetryComponent(FrameDeadline.After(now, RetryFrames)));

    /// <remarks>The tier first: a neighbouring tile can hold an unbuilt skeleton, whose other pools must not be read.</remarks>
    private bool IsCorpseCandidate(int entityId) =>
        tierQuery.IsSimulated(entityId) &&
        _deadEntities.Has(entityId) &&
        !_looted.Has(entityId) &&
        HoldsLoot(entityId);

    private bool IsPileCandidate(int entityId, int looterEntityId) =>
        tierQuery.IsSimulated(entityId) &&
        _piles.Has(entityId) &&
        (HoldsCurrency(entityId) || InventoryActions.CanLootAny(componentManager, itemCatalog, entityId, looterEntityId, playerQuery));

    private bool HoldsLoot(int entityId) => _inventoryStacks.Has(entityId) || HoldsCurrency(entityId);

    private bool HoldsCurrency(int entityId) =>
        _currencies.TryGetReadonly(entityId, out var currency) && (currency.Gold > 0 || currency.Credits > 0);

    /// <returns>Whether any stack or currency moved.</returns>
    private bool TakeEverything(int lootedEntityId, int looterEntityId)
    {
        var lootedStackCount = InventoryActions.LootEveryStack(componentManager, itemCatalog, lootedEntityId, looterEntityId, playerQuery);
        var tookCurrency = CurrencyActions.TryTransferAll(componentManager, lootedEntityId, looterEntityId);
        return lootedStackCount > 0 || tookCurrency;
    }
}
