namespace Engine.Persistence;

/// <summary>The save a game continues, from a "--save=name" command-line argument; with none, a new game makes its own.</summary>
/// <remarks>Parsing mirrors RandomSeed: the first one counts, and the value is taken as written -- whether it can be a save's name is SaveFolder.IsValidName's to say.</remarks>
public static class SaveArgument
{
    public const string ArgumentPrefix = "--save=";

    /// <summary>The save name args give, or null when they give none.</summary>
    public static string? Parse(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[ArgumentPrefix.Length..];
            }
        }

        return null;
    }

    /// <summary>The argument Parse reads back as name.</summary>
    public static string FormatArgument(string name) => $"{ArgumentPrefix}{name}";
}
