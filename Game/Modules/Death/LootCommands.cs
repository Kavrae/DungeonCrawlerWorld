using Engine.ECS.Components;
using Game.Modules.Death.Components;

namespace Game.Modules.Death;

/// <summary>A session's looting state, for code outside a system.</summary>
public sealed class LootCommands(ComponentManager componentManager)
{
    /// <summary>Records that entityId's loot has been opened at least once, whether or not anything was taken.</summary>
    public void MarkLooted(int entityId) => componentManager.Merge(entityId, new LootedComponent());
}
