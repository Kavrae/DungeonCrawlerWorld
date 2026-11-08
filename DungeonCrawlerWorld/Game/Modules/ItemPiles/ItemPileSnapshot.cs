using Engine.Math;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory.Components;

namespace Game.Modules.ItemPiles;

/// <summary>An item pile as it was when its neighborhood unloaded: where it lay, its stacks exactly as they were, and its currency.</summary>
/// <remarks>The stacks are the components themselves, so each keeps its StackInstanceId, Override and lit state. Kept in the neighborhood's record (NeighborhoodRecord.KeptItemPiles) until it loads again.</remarks>
public sealed record ItemPileSnapshot(Vector3Int Position, IReadOnlyList<InventoryItemStackComponent> Stacks, CurrencyComponent Currency);
