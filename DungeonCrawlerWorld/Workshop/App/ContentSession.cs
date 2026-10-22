using Builders.MapAreas;
using Engine.Content;
using Engine.Math;
using Engine.Settings;
using Game.Areas;
using Game.Bootstrap;
using Game.Content;
using Game.World;
using Workshop;

namespace DungeonCrawlerWorld.Workshop;

/// <summary>The game build the IDE reads its catalogs and views from: every built-in module and every valid mod, over a placeholder map, with no population and no player, never simulated.</summary>
/// <remarks>
/// The IDE never rebuilds a spawn record, so the session's staging world is left until one is rebuilt -- never, today.
/// Every build here collects content failures rather than stopping: a broken record is left out and listed, so the IDE
/// still opens on content that wouldn't load in the game.
/// </remarks>
public static class ContentSession
{
    private const int InitialEntityCapacity = 16;
    private const int InitialComponentCapacity = 16;
    private const int PreviewEntityCapacity = 2048;
    private const int PreviewComponentCapacity = 256;
    private const ulong PreviewCrawlerNumberSeed = 1;
    private const int MinimumPreviewCrawlerNumber = 1;
    private const int PreviewCrawlerNumberBits = 24;

    /// <param name="modsDirectory">The mods the game itself would load -- GameShell's own Mods folder.</param>
    public static ValidatedMods ValidateMods(string modsDirectory, IReadOnlyList<ISettingsSource> settingsSources) =>
        ModValidation.Validate(modsDirectory, settingsSources);

    public static GameSession Build(ValidatedMods validatedMods, IReadOnlyList<ISettingsSource> settingsSources, TextWriter failureLog)
    {
        var gameSession = GameBootstrapper.Build(validatedMods, GameBuildPass.CreatePlaceholderMap(), new MathUtility(new SeededRandom()), InitialEntityCapacity, InitialComponentCapacity, settingsSources: settingsSources, buildsStagingWorldOnFirstRebuild: true, collectsContentFailures: true);

        foreach (var failure in gameSession.ModuleFailures)
        {
            failureLog.WriteLine($"[ModuleLoad] {failure.Source}: {failure.Exception}");
        }

        foreach (var failure in gameSession.SettingsFailures)
        {
            failureLog.WriteLine($"[Settings] {failure.Source}: {failure.Setting}: {failure.Message}");
        }

        foreach (var failure in gameSession.ContentFailures)
        {
            failureLog.WriteLine($"[Content] {failure}");
        }

        return gameSession;
    }

    /// <summary>A session over map with the same mods and settings as the IDE's own, for a preview built on it (an area, or a neighborhood, as the game builds it) -- with crawler numbers, since generation spawns crawlers.</summary>
    public static GameSession BuildPreview(ValidatedMods validatedMods, Map map, IReadOnlyList<ISettingsSource> settingsSources) =>
        GameBootstrapper.Build(validatedMods, map, new MathUtility(new SeededRandom()), PreviewEntityCapacity, PreviewComponentCapacity, new UniqueNumberAllocator(PreviewCrawlerNumberSeed, MinimumPreviewCrawlerNumber, PreviewCrawlerNumberBits), settingsSources: settingsSources, buildsStagingWorldOnFirstRebuild: true, collectsContentFailures: true);

    /// <summary>The Error List's lines for a session: each mod dropped, each setting override refused, each record of the master manifest under masterDirectory that doesn't load, and each problem the build's area checks find in it.</summary>
    /// <remarks>The manifest is read again from masterDirectory -- the source tree's copy, the one the IDE edits -- through the session's own codecs, collecting every failure rather than stopping at the first; its areas are checked against the session's terrain, blueprints and tags.</remarks>
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

        var catalogs = gameSession.Catalogs;
        var content = MasterContent.Load(masterDirectory, catalogs.ContentTypes);
        foreach (var failure in content.Failures)
        {
            problems.Add(ProblemFor(failure));
        }

        var sourceAreas = new AreaCatalog();
        sourceAreas.RegisterAll(content);
        foreach (var failure in AreaContentValidation.Validate(sourceAreas, catalogs.Terrain, catalogs.Definitions, catalogs.GameplayTags))
        {
            problems.Add(ProblemFor(failure) with { TabTitle = MapTab.TabTitle, RecordId = failure.RecordId });
        }

        return problems;
    }

    private static WorkshopProblem ProblemFor(ContentFailure failure) => new(WorkshopProblemSeverity.Error, failure.Reason, failure.Location);

    private static string FirstLineOf(string text) => text.Split('\n', 2)[0].TrimEnd('\r');
}
