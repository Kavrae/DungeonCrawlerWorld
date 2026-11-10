using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Tiles;
using Game.Modules.Containers.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Shops.Components;

namespace Game.Modules.Containers;

/// <summary>Which window an entity's items open in.</summary>
public enum ContainerWindowKind : byte
{
    /// <summary>It holds nothing anyone can open.</summary>
    None,

    /// <summary>A corpse, a container, an item pile or a destroyed shop: its loot window.</summary>
    Loot,

    /// <summary>A standing shop: its shop window and the trade.</summary>
    Shop,
}

/// <summary>Why an entity can't reach another's items now.</summary>
public enum ContainerAccessRefusal : byte
{
    None,

    /// <summary>Their footprints don't touch.</summary>
    OutOfReach,

    /// <summary>A corpse whose loot is someone else's for now (LootRights).</summary>
    LootReserved,
}

/// <summary>The one rule for reaching an entity's items from the map: what window they open in, and whether an entity may reach them.</summary>
/// <remarks>
/// Opening a window and putting something into a container (ItemDrops) both ask it, so what can be opened and what can
/// be dropped into never disagree. Reach is footprint to footprint, diagonals included -- standing on it or beside it.
/// </remarks>
public sealed class ContainerAccess(ComponentManager componentManager, EntityKeys entityKeys, SimulationClock simulationClock)
{
    private readonly DirectComponentPool<TransformComponent> _transforms = componentManager.GetDirectPool<TransformComponent>();
    private readonly PackedComponentPool<ContainerComponent> _containers = componentManager.GetPackedPool<ContainerComponent>();
    private readonly PackedComponentPool<ShopComponent> _shops = componentManager.GetPackedPool<ShopComponent>();
    private readonly PackedComponentPool<DeadComponent> _deadEntities = componentManager.GetPackedPool<DeadComponent>();
    private readonly LootRights _lootRights = LootRights.For(componentManager, entityKeys);

    /// <summary>The window entityId's items open in: a dead one's loot (a destroyed shop trades no more), a standing shop's shop, a container's loot.</summary>
    public ContainerWindowKind WindowKindOf(int entityId)
    {
        if (_deadEntities.Has(entityId))
        {
            return ContainerWindowKind.Loot;
        }

        if (_shops.Has(entityId))
        {
            return ContainerWindowKind.Shop;
        }

        return _containers.Has(entityId) ? ContainerWindowKind.Loot : ContainerWindowKind.None;
    }

    /// <summary>Why reacherEntityId can't reach entityId's items now, or None.</summary>
    public ContainerAccessRefusal GetRefusal(int reacherEntityId, int entityId)
    {
        if (!_transforms.TryGetReadonly(reacherEntityId, out var reacher) ||
            !_transforms.TryGetReadonly(entityId, out var reached) ||
            GridDistance.ChebyshevDistance(reacher.Position, reacher.Size, reached.Position, reached.Size) > 1)
        {
            return ContainerAccessRefusal.OutOfReach;
        }

        return _lootRights.CanLoot(entityId, entityKeys.GetKey(reacherEntityId), simulationClock.CurrentFrame)
            ? ContainerAccessRefusal.None
            : ContainerAccessRefusal.LootReserved;
    }
}
