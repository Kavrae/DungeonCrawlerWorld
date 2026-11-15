namespace DungeonCrawlerWorld;

/// <summary>The master manifest a "--master-content=&lt;directory&gt;" argument names in place of the copy in the game's own output, or null for that copy.</summary>
/// <remarks>
/// The Workshop starts the game with the source tree's master, so a record it saves is in the next game it plays without a
/// build. A directory that doesn't exist is reported on one line and ignored, as a bad "--map-size=" is, rather than
/// stopping the game from starting.
/// </remarks>
internal static class MasterContentArgument
{
    public const string ArgumentPrefix = "--master-content=";

    public static string? Parse(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var directory = arg[ArgumentPrefix.Length..].Trim('"');
            if (Directory.Exists(directory))
            {
                return directory;
            }

            Console.Error.WriteLine($"[Content] {ArgumentPrefix}{directory} names no folder, so the game reads the master in its own output.");
            return null;
        }

        return null;
    }
}
