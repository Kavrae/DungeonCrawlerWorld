namespace Engine.Math;

/// <summary>
/// The seed the shared MathUtility's randomizer is built from -- parsed from a
/// "--seed=12345" command-line argument, or generated fresh when none is given.
/// </summary>
/// <remarks>
/// A seed is ALWAYS chosen, never left implicit. Omitting the argument picks a random one rather
/// than falling through to an unseeded Random, so every session -- including one nobody asked to
/// be reproducible -- has a seed that can be read back and replayed. That is what makes "here is
/// the map I got, try it yourself" possible after the fact instead of only when someone
/// remembered to ask for it up front. It also means the deterministic and non-deterministic paths
/// are the same code path, so the seeded one can't quietly rot.
///
/// Parsing mirrors DiagnosticsFeaturesParser exactly (same "--name=value" shape, same tolerance
/// of a malformed value): an unparseable seed falls back to a generated one rather than throwing,
/// since a typo in a convenience flag should not stop the game from starting.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class RandomSeed
{
    private const string ArgumentPrefix = "--seed=";

    /// <summary>Reads the seed from args, or generates one when absent or unparseable.</summary>
    /// <param name="args">The process's own command-line arguments.</param>
    /// <returns>The seed to construct the shared randomizer from.</returns>
    public static int Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var arg in args)
        {
            if (arg.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arg[ArgumentPrefix.Length..], out var seed))
            {
                return seed;
            }
        }

        return Generate();
    }

    /// <summary>A fresh seed for a session that didn't ask for a specific one -- still recorded and replayable, see this class's own doc comment.</summary>
    public static int Generate() => Random.Shared.Next();
}
