using Engine.Math;
using Engine.Settings;
using Game.Bootstrap;
using Game.Content;
using Workshop;

namespace DungeonCrawlerWorld.Workshop;

/// <summary>The game build the IDE reads its catalogs and views from: every built-in module and every valid mod, over a placeholder map, with no population and no player, never simulated.</summary>
/// <remarks>The IDE never rebuilds a spawn record, so the session's staging world is left until one is rebuilt -- never, today.</remarks>
public static class ContentSession
{
    private const int InitialEntityCapacity = 16;
    private const int InitialComponentCapacity = 16;

    /// <param name="modsDirectory">The mods the game itself would load -- GameShell's own Mods folder.</param>
    public static GameSession Build(string modsDirectory, IReadOnlyList<ISettingsSource> settingsSources, TextWriter failureLog)
    {
        var validatedMods = ModValidation.Validate(modsDirectory, settingsSources);
        var gameSession = GameBootstrapper.Build(validatedMods, GameBuildPass.CreatePlaceholderMap(), new MathUtility(new SeededRandom()), InitialEntityCapacity, InitialComponentCapacity, settingsSources: settingsSources, buildsStagingWorldOnFirstRebuild: true);

        foreach (var failure in gameSession.ModuleFailures)
        {
            failureLog.WriteLine($"[ModuleLoad] {failure.Source}: {failure.Exception}");
        }

        foreach (var failure in gameSession.SettingsFailures)
        {
            failureLog.WriteLine($"[Settings] {failure.Source}: {failure.Setting}: {failure.Message}");
        }

        return gameSession;
    }

    /// <summary>The Error List's lines for a session: each mod dropped, each setting override refused, and each record of the master manifest under masterDirectory that doesn't load.</summary>
    /// <remarks>The manifest is read again from masterDirectory -- the source tree's copy, the one the IDE edits -- through the session's own codecs, collecting every failure rather than stopping at the first.</remarks>
    public static IReadOnlyList<WorkshopProblem> LoadProblems(GameSession gameSession, string masterDirectory)
    {
        var problems = new List<WorkshopProblem>();

        foreach (var failure in gameSession.ModuleFailures)
        {
            problems.Add(new WorkshopProblem(WorkshopProblemSeverity.Error, $"Mod dropped: {FirstLineOf(failure.Exception.Message)}", Path.GetFileName(failure.Source)));
        }

        foreach (var failure in gameSession.SettingsFailures)
        {
            problems.Add(new WorkshopProblem(WorkshopProblemSeverity.Warning, $"{failure.Setting}: {failure.Message}", failure.Source));
        }

        var content = MasterContent.Load(masterDirectory, gameSession.Catalogs.ContentTypes);
        foreach (var failure in content.Failures)
        {
            problems.Add(new WorkshopProblem(WorkshopProblemSeverity.Error, failure.Reason, failure.Line > 0 ? $"{failure.File}:{failure.Line}" : failure.File));
        }

        return problems;
    }

    private static string FirstLineOf(string text) => text.Split('\n', 2)[0].TrimEnd('\r');
}
