namespace Workshop.Debug;

/// <summary>What the launch options put in the Error List: each of their problems, an error when it refuses Play, a warning otherwise.</summary>
public static class DebugLaunchProblems
{
    /// <summary>The Error List's source column for these lines.</summary>
    public const string Source = "Launch options";

    public static IReadOnlyList<WorkshopProblem> Of(IReadOnlyList<DebugLaunchProblem> problems) =>
        [.. problems.Select(static problem => new WorkshopProblem(problem.RefusesLaunch ? WorkshopProblemSeverity.Error : WorkshopProblemSeverity.Warning, $"{problem.Option}: {problem.Message}", Source))];
}
