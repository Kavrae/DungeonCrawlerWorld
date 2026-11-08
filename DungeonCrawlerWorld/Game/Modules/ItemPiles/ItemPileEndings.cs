using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.ItemPiles.Components;

namespace Game.Modules.ItemPiles;

/// <summary>Destroys every item pile that has come to hold nothing.</summary>
/// <remarks>
/// It observes the stack and currency pools, so a pile emptied by any path (Take, Take All, an NPC, a drag) is noticed with no
/// caller remembering. A pile is queued by its key as a stack leaves it or its currency changes, and checked when
/// ItemPileEndingSystem runs: never inside the removal, which happens mid-transfer.
/// </remarks>
public sealed class ItemPileEndings
{
    private readonly EntityManager _entityManager;
    private readonly EntityKeys _entityKeys;
    private readonly PackedComponentPool<ItemPileComponent> _piles;
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks;
    private readonly PackedComponentPool<CurrencyComponent> _currencies;
    private readonly List<EntityKey> _possiblyEmptiedPileKeys = [];

    public ItemPileEndings(ComponentManager componentManager, EntityManager entityManager)
    {
        _entityManager = entityManager;
        _entityKeys = componentManager.Keys;
        _piles = componentManager.GetPackedPool<ItemPileComponent>();
        _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        _currencies = componentManager.GetPackedPool<CurrencyComponent>();

        _inventoryStacks.ComponentRemoving += OnStackRemoving;
        _currencies.ComponentChanged += OnCurrencyChanged;
    }

    /// <summary>Destroys each queued pile that still exists and holds no stack and no currency.</summary>
    public void EndEmptiedPiles()
    {
        if (_possiblyEmptiedPileKeys.Count == 0)
        {
            return;
        }

        foreach (var pileKey in _possiblyEmptiedPileKeys)
        {
            if (_entityKeys.TryGetEntityId(pileKey, out var pileEntityId) && _piles.Has(pileEntityId) && !HoldsAnything(pileEntityId))
            {
                _entityManager.DestroyEntity(pileEntityId);
            }
        }

        _possiblyEmptiedPileKeys.Clear();
    }

    private void OnStackRemoving(int entityId, int denseIndex) => QueueIfPile(entityId);

    private void OnCurrencyChanged(int entityId, int denseIndex) => QueueIfPile(entityId);

    private void QueueIfPile(int entityId)
    {
        if (_piles.Has(entityId) && !_entityManager.IsDestroying(entityId))
        {
            _possiblyEmptiedPileKeys.Add(_entityKeys.GetKey(entityId));
        }
    }

    private bool HoldsAnything(int pileEntityId) =>
        _inventoryStacks.Has(pileEntityId) ||
        (_currencies.TryGetReadonly(pileEntityId, out var currency) && (currency.Gold > 0 || currency.Credits > 0));
}
