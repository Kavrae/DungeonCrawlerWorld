namespace DungeonCrawlerWorld;

/// <summary>The square test-map width and height requested by a "--map-size=3072" command-line argument, or null for FloorBuilder's default.</summary>
/// <remarks>
/// Exists for scaling measurements: the same seed and frame range at a
/// different map size, without editing FloorBuilder's constant. Parsing mirrors RandomSeed: an
/// unparseable or non-positive value is ignored rather than stopping the game from starting.
/// GameLoop.InitialEntityCapacity is sized for the default map; a larger map grows the pools by
/// doubling during population, which costs startup time but not correctness.
/// </remarks>
internal static class MapSizeArgument
{
    private const string ArgumentPrefix = "--map-size=";

    public static int? Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var arg in args)
        {
            if (arg.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arg[ArgumentPrefix.Length..], out var size) &&
                size > 0)
            {
                return size;
            }
        }

        return null;
    }
}
