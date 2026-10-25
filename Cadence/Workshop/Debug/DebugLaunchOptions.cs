using Engine.Diagnostics;
using Engine.Math;

namespace Workshop.Debug;

/// <summary>How Play starts a debug run: the diagnostics features, the seed, the benchmark range, headless, and any extra arguments.</summary>
/// <remarks>
/// A switched-off option keeps its value (a random seed keeps the number typed, a run with no benchmark keeps its range),
/// so switching it back on restores it. Headless takes effect only with the benchmark range on, as the game refuses it
/// otherwise. The arguments are the options' own, in a fixed order, then the extra arguments as typed.
/// The module settings and the game's own flags are TODO.md "Workshop Debug tab: launch options", Phases 3 and 4.
/// </remarks>
public sealed record DebugLaunchOptions
{
    /// <summary>The option a problem with the extra arguments names.</summary>
    public const string ExtraArgumentsOption = "Extra arguments";

    public static readonly BenchmarkFrameRange DefaultBenchmarkFrames = new(600, 3600);

    /// <summary>Frame budget and gauges, a random seed, no benchmark.</summary>
    public static readonly DebugLaunchOptions Default = new();

    private static readonly (string Prefix, string Owner)[] OwnedArguments =
    [
        (DiagnosticsLinkArgument.ArgumentPrefix, "the Workshop"),
        (DiagnosticsFeaturesParser.ArgumentPrefix, "Diagnostics features"),
        (RandomSeed.ArgumentPrefix, "Seed"),
        (BenchmarkFrameRange.ArgumentPrefix, "Benchmark frames"),
        (HeadlessArgument.Argument, "Headless"),
    ];

    public DiagnosticsFeatures DiagnosticsFeatures { get; init; } = DiagnosticsFeatures.FrameBudget | DiagnosticsFeatures.Gauges;

    /// <summary>Whether the game picks its own seed; when false it is given Seed.</summary>
    public bool UsesRandomSeed { get; init; } = true;

    public int Seed { get; init; }

    /// <summary>Whether the game measures BenchmarkFrames.</summary>
    public bool RunsBenchmark { get; init; }

    public BenchmarkFrameRange BenchmarkFrames { get; init; } = DefaultBenchmarkFrames;

    /// <summary>Whether a benchmark runs with no window; nothing without RunsBenchmark.</summary>
    public bool Headless { get; init; }

    /// <summary>Added after the options' own arguments as typed.</summary>
    public string ExtraArguments { get; init; } = string.Empty;

    public bool RunsHeadless => RunsBenchmark && Headless;

    /// <summary>The options' own arguments, in order: diagnostics, seed, benchmark range, headless.</summary>
    public IReadOnlyList<string> OptionArguments
    {
        get
        {
            var arguments = new List<string> { DiagnosticsFeaturesParser.FormatArgument(DiagnosticsFeatures) };
            if (!UsesRandomSeed)
            {
                arguments.Add(RandomSeed.FormatArgument(Seed));
            }

            if (RunsBenchmark)
            {
                arguments.Add(BenchmarkFrames.FormatArgument());
                if (Headless)
                {
                    arguments.Add(HeadlessArgument.Argument);
                }
            }

            return arguments;
        }
    }

    /// <summary>The game's arguments these options make: OptionArguments, quoted where they need it, then ExtraArguments.</summary>
    public string Arguments
    {
        get
        {
            var optionArguments = CommandLineArguments.Join(OptionArguments);
            var extraArguments = ExtraArguments.Trim();
            return extraArguments.Length == 0 ? optionArguments : $"{optionArguments} {extraArguments}";
        }
    }

    /// <summary>What the game would refuse or read differently than the options say, started with targetArguments ahead of the options' own.</summary>
    /// <remarks>
    /// The game reads the first of a repeated argument. One the launch target's arguments already set is a warning on the
    /// option it overrides; an extra argument repeating one the target, the options or the Workshop pass is a warning on
    /// the extra arguments. Arguments the game refuses to start with refuse the launch.
    /// </remarks>
    /// <param name="targetArguments">The launch target's own arguments, which come first on the command line.</param>
    public IReadOnlyList<DebugLaunchProblem> ProblemsWith(IReadOnlyList<string> targetArguments)
    {
        var problems = new List<DebugLaunchProblem>();
        var extraArguments = CommandLineArguments.Split(ExtraArguments);
        var optionArguments = OptionArguments;
        foreach (var (prefix, owner) in OwnedArguments)
        {
            var isInTarget = targetArguments.Any(argument => IsArgument(argument, prefix));
            var isPassed = prefix == DiagnosticsLinkArgument.ArgumentPrefix || optionArguments.Any(argument => IsArgument(argument, prefix));
            if (isInTarget && isPassed)
            {
                problems.Add(new DebugLaunchProblem(owner, $"The launch target's arguments already set {prefix}, which {owner} sets; the game reads the first, the target's.", RefusesLaunch: false));
            }

            if ((isInTarget || isPassed) && extraArguments.Any(argument => IsArgument(argument, prefix)))
            {
                var firstOwner = isInTarget ? "the launch target" : owner;
                problems.Add(new DebugLaunchProblem(ExtraArgumentsOption, $"Extra arguments repeat {prefix}, which {firstOwner} sets; the game reads the first, {firstOwner}'s.", RefusesLaunch: false));
            }
        }

        if (HeadlessArgument.IsRefused([.. targetArguments, .. optionArguments, .. extraArguments]))
        {
            var headlessSource = targetArguments.Any(argument => IsArgument(argument, HeadlessArgument.Argument)) ? "the launch target's arguments" : "Extra arguments";
            problems.Add(new DebugLaunchProblem(ExtraArgumentsOption, $"The game refuses to start: {HeadlessArgument.RequiresRangeMessage} Tick Benchmark frames, or take {HeadlessArgument.Argument} out of {headlessSource}.", RefusesLaunch: true));
        }

        return problems;
    }

    private static bool IsArgument(string argument, string prefix) =>
        prefix.EndsWith('=') ? argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) : argument.Equals(prefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Something about the launch options the game would refuse or read differently: which option, what is wrong, and whether Play is refused for it.</summary>
public sealed record DebugLaunchProblem(string Option, string Message, bool RefusesLaunch);
