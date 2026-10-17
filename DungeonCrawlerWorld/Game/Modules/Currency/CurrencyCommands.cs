using Engine.ECS.Components;

namespace Game.Modules.Currency;

/// <summary>A session's currency moves, for code outside a system: CurrencyActions with the session's pools bound.</summary>
public sealed class CurrencyCommands(ComponentManager componentManager)
{
    /// <inheritdoc cref="CurrencyActions.TryTransfer(ComponentManager, int, int, CurrencyType)"/>
    public bool TryTransfer(int sourceEntityId, int destinationEntityId, CurrencyType type) =>
        CurrencyActions.TryTransfer(componentManager, sourceEntityId, destinationEntityId, type);

    /// <inheritdoc cref="CurrencyActions.TryTransfer(ComponentManager, int, int, CurrencyType, int)"/>
    public bool TryTransfer(int sourceEntityId, int destinationEntityId, CurrencyType type, int amount) =>
        CurrencyActions.TryTransfer(componentManager, sourceEntityId, destinationEntityId, type, amount);

    /// <inheritdoc cref="CurrencyActions.TryTransferAll"/>
    public bool TryTransferAll(int sourceEntityId, int destinationEntityId) =>
        CurrencyActions.TryTransferAll(componentManager, sourceEntityId, destinationEntityId);
}
