namespace Game.Bootstrap;

/// <summary>Where a player's own files live: their saves and their mods, under %LocalAppData%\DungeonCrawlerWorld, the same for every build of the game.</summary>
/// <remarks>
/// Never inside the game's install or build folder, so a player adds a mod by dropping its folder into Mods -- its DLLs
/// and its Definitions folder (ModuleLoader.LoadMods) -- and starting the game, with nothing to build and no game folder
/// to find. The game, the Workshop and its Play all read the same folders.
/// </remarks>
public static class PlayerFolders
{
    /// <summary>%LocalAppData%\DungeonCrawlerWorld.</summary>
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DungeonCrawlerWorld");

    /// <summary>One folder per save (Engine.Persistence.SaveFolder).</summary>
    public static string Saves { get; } = Path.Combine(Root, "Saves");

    /// <summary>One folder per mod, loaded at every start.</summary>
    public static string Mods { get; } = Path.Combine(Root, "Mods");
}
