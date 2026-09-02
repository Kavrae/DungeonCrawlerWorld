using Engine.ECS.Components;
using Game.Modules.Currency.Components;

namespace Game.Modules.Currency;

/// <summary>
/// Transfers an entity's entire current balance of one (or every) currency to another entity --
/// no partial amounts yet, see TODO.md's Context menu amount picker entry. Unlike
/// InventoryActions.TryTransferStack there's no capacity concept to check (CurrencyComponent is a
/// single packed value per entity, not a stack list) and no IPlayerQuery needed. Every transfer
/// reads then writes the WHOLE component on each side (never a partial-field Merge) since
/// CurrencyModule's registered merge policy is a full overwrite (existing = incoming) -- merging
/// just a delta would silently zero out the untouched currency field.
/// </summary>
public static class CurrencyActions
{
    public static bool TryTransfer(ComponentManager componentManager, int sourceEntityId, int destinationEntityId, CurrencyType type)
    {
        if (sourceEntityId == destinationEntityId || !componentManager.IsRegistered<CurrencyComponent>())
        {
            return false;
        }

        var pool = componentManager.GetPackedPool<CurrencyComponent>();
        pool.TryGetReadonly(sourceEntityId, out var source); // defaults to (0,0) if the entity has none yet
        var amount = GetAmount(source, type);
        if (amount <= 0)
        {
            return false;
        }

        SetAmount(ref source, type, 0);
        componentManager.Merge(sourceEntityId, source);

        pool.TryGetReadonly(destinationEntityId, out var destination);
        SetAmount(ref destination, type, GetAmount(destination, type) + amount);
        componentManager.Merge(destinationEntityId, destination);

        return true;
    }

    /// <summary>
    /// Transfers exactly amount of one currency -- the Shops chokepoint (a trade's Gold side is
    /// never the payer's whole balance). amount &lt;= 0 is a no-op that returns true (a 0-Value item
    /// trades for free, not a failed trade); otherwise fails with no state changed if the source
    /// can't cover amount. Reads/writes the whole CurrencyComponent on each side same as the
    /// whole-balance overload above, for the same reason (a partial-field Merge would zero the
    /// untouched currency).
    /// </summary>
    public static bool TryTransfer(ComponentManager componentManager, int sourceEntityId, int destinationEntityId, CurrencyType type, int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (sourceEntityId == destinationEntityId || !componentManager.IsRegistered<CurrencyComponent>())
        {
            return false;
        }

        var pool = componentManager.GetPackedPool<CurrencyComponent>();
        pool.TryGetReadonly(sourceEntityId, out var source);
        if (GetAmount(source, type) < amount)
        {
            return false;
        }

        SetAmount(ref source, type, GetAmount(source, type) - amount);
        componentManager.Merge(sourceEntityId, source);

        pool.TryGetReadonly(destinationEntityId, out var destination);
        SetAmount(ref destination, type, GetAmount(destination, type) + amount);
        componentManager.Merge(destinationEntityId, destination);

        return true;
    }

    /// <summary>Every currency at once -- "Give All"/"Take All" in the Currency context menu. Iterates CurrencyType's own values rather than naming Gold/Credits individually, so a future third currency is picked up automatically.</summary>
    public static bool TryTransferAll(ComponentManager componentManager, int sourceEntityId, int destinationEntityId)
    {
        var transferredAny = false;
        foreach (var type in Enum.GetValues<CurrencyType>())
        {
            transferredAny |= TryTransfer(componentManager, sourceEntityId, destinationEntityId, type);
        }

        return transferredAny;
    }

    private static int GetAmount(CurrencyComponent currency, CurrencyType type) => type switch
    {
        CurrencyType.Gold => currency.Gold,
        CurrencyType.Credits => currency.Credits,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static void SetAmount(ref CurrencyComponent currency, CurrencyType type, int amount)
    {
        switch (type)
        {
            case CurrencyType.Gold:
                currency.Gold = amount;
                break;
            case CurrencyType.Credits:
                currency.Credits = amount;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }
}
