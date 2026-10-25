using Engine.Diagnostics;

namespace Workshop.Debug;

/// <summary>What a connected game's diagnostics put in the Error List: each leak finding of its latest report, as a warning naming the pool.</summary>
public static class DiagnosticsProblems
{
    /// <summary>The Error List's source column for these lines.</summary>
    public const string Source = "Diagnostics";

    public static IReadOnlyList<WorkshopProblem> LeaksOf(DiagnosticsReport? report) =>
        report?.Leaks is { } leaks
            ? [.. leaks.Select(static leak => new WorkshopProblem(WorkshopProblemSeverity.Warning, $"Possible leak in {leak.Subject}: {leak.Detail}", Source))]
            : [];
}
