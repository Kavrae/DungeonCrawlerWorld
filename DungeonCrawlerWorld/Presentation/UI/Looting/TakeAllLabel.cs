using Game.Modules.Death;

namespace Presentation.UI.Looting;

/// <summary>What the action moving everything out of a loot window is called.</summary>
public static class TakeAllLabel
{
    /// <summary>"Take All" on a chest, item pile or destroyed shop, "Loot All" on a corpse.</summary>
    public static string For(LootCommands lootCommands, int lootedEntityId) =>
        lootCommands.IsContainer(lootedEntityId) ? "Take All" : "Loot All";
}
