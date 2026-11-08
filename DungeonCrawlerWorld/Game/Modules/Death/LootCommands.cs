using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Death.Components;
using Game.Modules.ItemPiles.Components;

namespace Game.Modules.Death;

/// <summary>A session's looting state, for code outside a system.</summary>
public sealed class LootCommands(ComponentManager componentManager)
{
    private readonly PackedComponentPool<ItemPileComponent> _piles = componentManager.GetPackedPool<ItemPileComponent>();

    /// <summary>Records that entityId's loot has been opened at least once, whether or not anything was taken.</summary>
    /// <remarks>An item pile is never marked: it has no opened state worth showing, so its bag always reads as unlooted.</remarks>
    public void MarkLooted(int entityId)
    {
        if (_piles.Has(entityId))
        {
            return;
        }

        componentManager.Merge(entityId, new LootedComponent());
    }
}
