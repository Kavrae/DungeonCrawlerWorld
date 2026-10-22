using Workshop;

namespace DungeonCrawlerWorld.Workshop;

/// <summary>Reads <c>--tab=Name</c>: the IDE opens with only that tab, the standalone way to run one builder.</summary>
internal static class TabArgument
{
    private const string Prefix = "--tab=";

    /// <summary>The tab named on the command line, or null when none is.</summary>
    public static string? Parse(IReadOnlyList<string> args)
    {
        foreach (var argument in args)
        {
            if (argument.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return argument[Prefix.Length..];
            }
        }

        return null;
    }

    /// <summary>Every tab when tabName is null; otherwise only the tab with that title (any case), or none when nothing has it.</summary>
    public static IReadOnlyList<IIdeTab> Select(IReadOnlyList<IIdeTab> tabs, string? tabName)
    {
        if (tabName is null)
        {
            return tabs;
        }

        return [.. tabs.Where(tab => string.Equals(tab.Title, tabName, StringComparison.OrdinalIgnoreCase))];
    }
}
