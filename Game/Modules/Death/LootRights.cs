using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Utilities;
using Game.Modules.Death.Components;

namespace Game.Modules.Death;

/// <summary>Who may loot a corpse: its loot owner alone for ExclusiveLootFrames after it died, then anyone.</summary>
/// <remarks>
/// Rights only ever widen. A corpse with no owner, an owner no longer loaded or an owner that is itself
/// dead is open to anyone at once, and anything that isn't a corpse (a container) is never reserved.
/// So a check made when a loot starts holds for as long as it stays open.
/// </remarks>
public sealed class LootRights(PackedComponentPool<DeadComponent> deadEntities, EntityKeys entityKeys)
{
    public const int ExclusiveLootFrames = 30 * GameTiming.FramesPerSecond;

    public static LootRights For(ComponentManager componentManager, EntityKeys entityKeys) =>
        new(componentManager.GetPackedPool<DeadComponent>(), entityKeys);

    public bool CanLoot(int lootedEntityId, EntityKey looterEntityKey, long now)
    {
        if (!deadEntities.TryGetReadonly(lootedEntityId, out var dead) || !IsReserved(in dead, now) || dead.LootOwnerEntityKey == looterEntityKey)
        {
            return true;
        }

        return !entityKeys.TryGetEntityId(dead.LootOwnerEntityKey, out var ownerEntityId) || deadEntities.Has(ownerEntityId);
    }

    private static bool IsReserved(in DeadComponent dead, long now) =>
        !dead.LootOwnerEntityKey.IsNone && now < dead.DiedAtFrame + ExclusiveLootFrames;
}
