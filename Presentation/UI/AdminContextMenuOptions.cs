using Engine.Math;
using Game.Admin;
using Game.Modules.Lootboxes;
using Game.World;

namespace Presentation.UI;

/// <summary>The Admin Mode entries the map's right-click menu offers, built from the session's AdminTools.</summary>
/// <remarks>Adds its entries unconditionally; the caller decides whether Admin Mode is on.</remarks>
public sealed class AdminContextMenuOptions(AdminTools adminTools)
{
    /// <summary>Appends the tile's admin entries: the neighborhood under it with "Regenerate", "Spawn here", and "Teleport here".</summary>
    public void AddTileOptions(List<ContextMenuOption> options, Vector3Int tilePosition)
    {
        AddNeighborhoodGroup(options, Neighborhoods.CellOf(tilePosition.X), Neighborhoods.CellOf(tilePosition.Y));

        options.Add(ContextMenuOption.Opening("Spawn here", SpawnChoices(tilePosition)));

        var teleportAdminCommands = adminTools.TeleportAdminCommands;
        options.Add(new ContextMenuOption("Teleport here", null, teleportAdminCommands.CanTeleportPlayer(tilePosition), () => teleportAdminCommands.TryTeleportPlayer(tilePosition)));
    }

    /// <summary>Appends one occupant's admin entries: "Apply" and "Grant loot box".</summary>
    public void AddEntityOptions(List<ContextMenuOption> options, int entityId)
    {
        options.Add(ContextMenuOption.Opening("Apply", ApplyChoices(entityId)));
        options.Add(ContextMenuOption.Opening("Grant loot box", LootboxTypeChoices(entityId)));
    }

    /// <summary>A header naming the neighborhood, then "Regenerate" -- disabled with the reason when the streamer refuses.</summary>
    private void AddNeighborhoodGroup(List<ContextMenuOption> options, int cellX, int cellY)
    {
        options.Add(ContextMenuOption.Header($"Neighborhood ({cellX}, {cellY}): x {Neighborhoods.OriginOf(cellX)}..{Neighborhoods.OriginOf(cellX + 1) - 1}, y {Neighborhoods.OriginOf(cellY)}..{Neighborhoods.OriginOf(cellY + 1) - 1}"));

        var neighborhoodAdminCommands = adminTools.NeighborhoodAdminCommands;
        var canRegenerate = neighborhoodAdminCommands.CanRegenerate(cellX, cellY, out var reason);
        options.Add(new ContextMenuOption(canRegenerate ? "Regenerate" : $"Regenerate ({reason})", null, canRegenerate, () => neighborhoodAdminCommands.TryRegenerate(cellX, cellY)));
    }

    /// <summary>Every spawnable blueprint, spawned on the tile and layer the menu was opened on.</summary>
    private List<ContextMenuOption> SpawnChoices(Vector3Int tilePosition)
    {
        var blueprintAdminCommands = adminTools.BlueprintAdminCommands;
        return [.. blueprintAdminCommands.Spawnable().Select(choice => new ContextMenuOption(choice.Name, null, true, () => blueprintAdminCommands.Spawn(choice.BlueprintId, tilePosition)))];
    }

    /// <summary>Every blueprint that can be built onto an existing entity, applied to entityId.</summary>
    private List<ContextMenuOption> ApplyChoices(int entityId)
    {
        var blueprintAdminCommands = adminTools.BlueprintAdminCommands;
        return [.. blueprintAdminCommands.Applicable().Select(choice => new ContextMenuOption(choice.Name, null, true, () => blueprintAdminCommands.Apply(entityId, choice.BlueprintId)))];
    }

    /// <summary>Every loot box type, each opening a submenu of rarities that grants one box of it to entityId.</summary>
    private List<ContextMenuOption> LootboxTypeChoices(int entityId)
    {
        var lootboxAdminCommands = adminTools.LootboxAdminCommands;
        return [.. lootboxAdminCommands.Types().Select(type => ContextMenuOption.Opening(type.Name,
            [.. Enum.GetValues<LootboxRarity>().Select(rarity =>
                new ContextMenuOption(rarity.ToString(), null, true, () => lootboxAdminCommands.Grant(entityId, type.Id, rarity)))]))];
    }
}
