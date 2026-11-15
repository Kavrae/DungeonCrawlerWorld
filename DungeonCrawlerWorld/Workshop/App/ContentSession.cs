using Builders.Editing;
using Builders.MapAreas;
using Engine.Content;
using Engine.Math;
using Engine.Settings;
using Game.Bootstrap;
using Game.Content;
using Game.World;
using Workshop;

namespace DungeonCrawlerWorld.Workshop;

/// <summary>The game build the IDE reads its catalogs and views from: every built-in module and every valid mod, over a placeholder map, with no population and no player, never simulated.</summary>
/// <remarks>
/// The IDE never rebuilds a spawn record, so the session's staging world is left until one is rebuilt -- never, today.
/// Every build here reads the content layers it is given -- the source tree's master, the copy the IDE edits -- and
/// collects content failures rather than stopping: a broken record is left out and listed, so the IDE still opens on
/// content that wouldn't load in the game.
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
    public static ValidatedMods ValidateMods(string modsDirectory, ContentLayers content, IReadOnlyList<ISettingsSource> settingsSources) =>
        ModValidation.Validate(modsDirectory, content, settingsSources, collectsContentFailures: true);

    public static GameSession Build(ValidatedMods validatedMods, ContentLayers content, IReadOnlyList<ISettingsSource> settingsSources, TextWriter failureLog)
    {
        var gameSession = GameBootstrapper.Build(validatedMods, GameBuildPass.CreatePlaceholderMap(), content, sessionSeed: 0, InitialEntityCapacity, InitialComponentCapacity, settingsSources: settingsSources, buildsStagingWorldOnFirstRebuild: true, collectsContentFailures: true, loadsContentById: false);

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

    /// <summary>A session over map with the same mods, content and settings as the IDE's own, for a preview built on it (an area, or a neighborhood, as the game builds it) -- with crawler numbers, since generation spawns crawlers.</summary>
    public static GameSession BuildPreview(ValidatedMods validatedMods, ContentLayers content, Map map, IReadOnlyList<ISettingsSource> settingsSources) =>
        GameBootstrapper.Build(validatedMods, map, content, sessionSeed: 0, PreviewEntityCapacity, PreviewComponentCapacity, new UniqueNumberAllocator(PreviewCrawlerNumberSeed, MinimumPreviewCrawlerNumber, PreviewCrawlerNumberBits), settingsSources: settingsSources, buildsStagingWorldOnFirstRebuild: true, collectsContentFailures: true, loadsContentById: false);

    /// <summary>The Error List's lines for a session: each mod dropped, each setting override refused, and each content record the session's build left out -- one that didn't load, or that its area checks refused.</summary>
    /// <remarks>A failure in an area, palette or placement file is the Map tab's, and names its record when it has one.</remarks>
    public static IReadOnlyList<WorkshopProblem> LoadProblems(GameSession gameSession)
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

        foreach (var failure in gameSession.ContentFailures)
        {
            var problem = new WorkshopProblem(WorkshopProblemSeverity.Error, failure.Reason, failure.Location);
            problems.Add(TabTitleOf(failure.File) is { } tabTitle ? problem with { TabTitle = tabTitle, RecordId = failure.RecordId } : problem);
        }

        return problems;
    }

    /// <summary>Every file under masterDirectory, by its path inside it ("Items.jsonl", "Areas/start.json"): master as it is on disk now.</summary>
    public static IReadOnlyDictionary<string, string> ReadMasterFiles(string masterDirectory)
    {
        var textByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(masterDirectory, "*", SearchOption.AllDirectories))
        {
            textByPath[Path.GetRelativePath(masterDirectory, path).Replace('\\', '/')] = File.ReadAllText(path, System.Text.Encoding.UTF8);
        }

        return textByPath;
    }

    /// <summary>A trial build of the built-ins alone over master as files holds it (every master file, by path): what master would load as, failures collected, never simulated.</summary>
    public static ContentTrialOutcome TrialBuild(IReadOnlyDictionary<string, string> files, IReadOnlyList<ISettingsSource> settingsSources)
    {
        var layers = new ContentLayers([ContentLayer.FromText(GameContent.MasterLayerName, ContentLayerKind.Master, files)]);
        try
        {
            var build = GameBuildPass.BuildModules(GameBootstrapper.BuiltInModules(), [], GameBuildPass.CreatePlaceholderMap(), layers, sessionSeed: 0, settingsSources, InitialEntityCapacity, InitialComponentCapacity, purpose: Engine.Bootstrap.EcsBuildPurpose.Trial, collectsContentFailures: true);
            return new ContentTrialOutcome(build.Context.ContentFailures, null);
        }
        catch (Exception exception)
        {
            return new ContentTrialOutcome([], FirstLineOf(exception.Message));
        }
    }

    /// <summary>The tab that shows records of file, for a failure to open them: the Map tab for an area, palette or placement, the Items tab for an item.</summary>
    private static string? TabTitleOf(string file) =>
        IsAreaFile(file) ? MapTab.TabTitle : file == GameContent.ItemsFileName ? Builders.Items.ItemsTab.TabTitle : null;

    private static bool IsAreaFile(string file) =>
        file.StartsWith($"{GameContent.AreasDirectoryName}/", StringComparison.Ordinal)
        || file == GameContent.AreaPalettesFileName
        || file == GameContent.AreaPlacementsFileName;

    private static string FirstLineOf(string text) => text.Split('\n', 2)[0].TrimEnd('\r');
}
