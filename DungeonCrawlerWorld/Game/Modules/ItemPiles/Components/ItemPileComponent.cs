namespace Game.Modules.ItemPiles.Components;

/// <summary>Marks an entity as an item pile: items and currency put down on a tile, one pile per tile and layer.</summary>
/// <remarks>A pile is a container with no health, so it opens in the loot window and is never destroyed by damage. It ends once it holds nothing (ItemPileEndings).</remarks>
public readonly record struct ItemPileComponent
{
    public override readonly string ToString() => nameof(ItemPileComponent);
}
