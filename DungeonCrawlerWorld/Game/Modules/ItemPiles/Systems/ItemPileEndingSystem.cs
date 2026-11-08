using Engine.ECS.Systems;

namespace Game.Modules.ItemPiles.Systems;

/// <summary>Destroys the item piles emptied since its last update.</summary>
/// <remarks>A plain system with no population: ItemPileEndings queues piles as they lose a stack or their currency changes, often mid-transfer, where destroying an entity isn't safe.</remarks>
public sealed class ItemPileEndingSystem(ItemPileEndings endings) : ISystem
{
    public byte StripeCount => 1;

    public void Update(EngineTime time, byte stripeIndex) => endings.EndEmptiedPiles();
}
