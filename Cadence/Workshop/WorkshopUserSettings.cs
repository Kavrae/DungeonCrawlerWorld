namespace Workshop;

/// <summary>Where the Workshop keeps a user's own settings: %LOCALAPPDATA%\Cadence\Workshop, a folder per game under it -- never the repository or a content file.</summary>
public static class WorkshopUserSettings
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadence", "Workshop");

    /// <summary>fileName in gameName's own folder, apart from every other game's.</summary>
    public static string FilePathForGame(string gameName, string fileName) => Path.Combine(Folder, gameName, fileName);
}
