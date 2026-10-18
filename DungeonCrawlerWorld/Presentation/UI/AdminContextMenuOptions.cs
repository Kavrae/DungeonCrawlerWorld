using Engine.Math;
using Game.Admin;
using Game.Modules.Lootboxes;
using Game.World;
using UIEngine.UI;

namespace Presentation.UI;

/// <summary>The Admin Mode entries the map's right-click menu offers, built from the session's AdminTools.</summary>
/// <remarks>Adds its entries unconditionally; the caller decides whether Admin Mode is on.</remarks>
public sealed class AdminContextMenuOptions(AdminTools adminTools)
{
    /// <summary>Appends the tile's admin entries: the neighborhood under it with "Regenerate", "Spawn here", "Build stone wall" or "Remove wall", and "Teleport here".</summary>
    public void AddTileOptions(List<DropdownOption> options, Vector3Int tilePosition)
    {
        AddNeighborhoodGroup(options, Neighborhoods.CellOf(tilePosition.X), Neighborhoods.CellOf(tilePosition.Y));

        options.Add(DropdownOption.Opening("Spawn here", SpawnChoices(tilePosition)));
        AddWallOption(options, tilePosition);

        var teleportAdminCommands = adminTools.TeleportAdminCommands;
        options.Add(new DropdownOption("Teleport here", null, teleportAdminCommands.CanTeleportPlayer(tilePosition), () => teleportAdminCommands.TryTeleportPlayer(tilePosition)));
    }

    /// <summary>Appends one occupant's admin entries: "Apply" and "Grant loot box".</summary>
    public void AddEntityOptions(List<DropdownOption> options, int entityId)
    {
        options.Add(DropdownOption.Opening("Apply", ApplyChoices(entityId)));
        options.Add(DropdownOption.Opening("Grant loot box", LootboxTypeChoices(entityId)));
    }

    /// <summary>A header naming the neighborhood, then "Regenerate" -- disabled with the reason when the streamer refuses.</summary>
    private void AddNeighborhoodGroup(List<DropdownOption> options, int cellX, int cellY)
    {
        options.Add(DropdownOption.Header($"Neighborhood ({cellX}, {cellY}): x {Neighborhoods.OriginOf(cellX)}..{Neighborhoods.OriginOf(cellX + 1) - 1}, y {Neighborhoods.OriginOf(cellY)}..{Neighborhoods.OriginOf(cellY + 1) - 1}"));

        var neighborhoodAdminCommands = adminTools.NeighborhoodAdminCommands;
        var canRegenerate = neighborhoodAdminCommands.CanRegenerate(cellX, cellY, out var reason);
        options.Add(new DropdownOption(canRegenerate ? "Regenerate" : $"Regenerate ({reason})", null, canRegenerate, () => neighborhoodAdminCommands.TryRegenerate(cellX, cellY)));
    }

    /// <summary>"Remove wall" on a tile with a structure; otherwise "Build stone wall", disabled with the reason when it can't be built (an occupant standing there).</summary>
    private void AddWallOption(List<DropdownOption> options, Vector3Int tilePosition)
    {
        var structureAdminCommands = adminTools.StructureAdminCommands;
        if (structureAdminCommands.CanRemoveStructure(tilePosition))
        {
            options.Add(new DropdownOption("Remove wall", null, true, () => structureAdminCommands.TryRemoveStructure(tilePosition)));
            return;
        }

        var canBuild = structureAdminCommands.CanBuildStoneWall(tilePosition, out var reason);
        options.Add(new DropdownOption(canBuild ? "Build stone wall" : $"Build stone wall ({reason})", null, canBuild, () => structureAdminCommands.TryBuildStoneWall(tilePosition)));
    }

    /// <summary>Every spawnable blueprint, spawned on the tile and layer the menu was opened on.</summary>
    private List<DropdownOption> SpawnChoices(Vector3Int tilePosition)
    {
        var blueprintAdminCommands = adminTools.BlueprintAdminCommands;
        return [.. blueprintAdminCommands.Spawnable().Select(choice => new DropdownOption(choice.Name, null, true, () => blueprintAdminCommands.Spawn(choice.BlueprintId, tilePosition)))];
    }

    /// <summary>Every blueprint that can be built onto an existing entity, applied to entityId.</summary>
    private List<DropdownOption> ApplyChoices(int entityId)
    {
        var blueprintAdminCommands = adminTools.BlueprintAdminCommands;
        return [.. blueprintAdminCommands.Applicable().Select(choice => new DropdownOption(choice.Name, null, true, () => blueprintAdminCommands.Apply(entityId, choice.BlueprintId)))];
    }

    /// <summary>Every loot box type, each opening a submenu of rarities that grants one box of it to entityId.</summary>
    private List<DropdownOption> LootboxTypeChoices(int entityId)
    {
        var lootboxAdminCommands = adminTools.LootboxAdminCommands;
        return [.. lootboxAdminCommands.Types().Select(type => DropdownOption.Opening(type.Name,
            [.. Enum.GetValues<LootboxRarity>().Select(rarity =>
                new DropdownOption(rarity.ToString(), null, true, () => lootboxAdminCommands.Grant(entityId, type.Id, rarity)))]))];
    }
}
