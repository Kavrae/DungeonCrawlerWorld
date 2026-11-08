using Game.Modules.Currency;

namespace Game.Modules.ItemPiles;

/// <summary>What a drop puts down.</summary>
public enum ItemDropKind : byte
{
    /// <summary>One unit of a stack.</summary>
    OneUnit,

    /// <summary>A whole stack.</summary>
    WholeStack,

    /// <summary>Every stack of one item: a Merged Stack cell.</summary>
    EveryStackOfItem,

    /// <summary>All of one currency.</summary>
    Currency,

    /// <summary>All of every currency.</summary>
    AllCurrency,
}

/// <summary>What a drop puts down: one unit or the whole of a stack, every stack of an item, or currency.</summary>
/// <param name="StackInstanceId">The stack a OneUnit or WholeStack drop takes from; 0 otherwise.</param>
/// <param name="CurrencyType">The currency a Currency drop puts down; ignored otherwise.</param>
/// <param name="ItemDefinitionId">The item an EveryStackOfItem drop puts down; empty otherwise.</param>
public readonly record struct ItemDrop(ItemDropKind Kind, uint StackInstanceId, CurrencyType CurrencyType, Guid ItemDefinitionId)
{
    public static ItemDrop OneUnitOf(uint stackInstanceId) => new(ItemDropKind.OneUnit, stackInstanceId, default, Guid.Empty);

    public static ItemDrop WholeStackOf(uint stackInstanceId) => new(ItemDropKind.WholeStack, stackInstanceId, default, Guid.Empty);

    public static ItemDrop EveryStackOf(Guid itemDefinitionId) => new(ItemDropKind.EveryStackOfItem, 0, default, itemDefinitionId);

    public static ItemDrop AllOf(CurrencyType currencyType) => new(ItemDropKind.Currency, 0, currencyType, Guid.Empty);

    public static ItemDrop AllCurrency => new(ItemDropKind.AllCurrency, 0, default, Guid.Empty);
}
