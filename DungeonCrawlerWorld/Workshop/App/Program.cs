using Builders;
using DungeonCrawlerWorld.Workshop;
using Engine.Settings;
using Presentation.Rendering;
using Workshop;
using Workshop.Debug;
using Workshop.Sprites;

Game.GameFrameRate.Apply();

IReadOnlyList<ISettingsSource> settingsSources = [new CommandLineSettingsSource(args)];
var tabName = TabArgument.Parse(args);
IReadOnlyList<string> tabTitles = [.. BuilderTabs.Titles, SpritesTab.TabTitle, ControlGalleryTab.TabTitle, DebugTab.TabTitle];
if (tabName is not null && !tabTitles.Contains(tabName, StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"--tab={tabName} names no tab. Tabs: {string.Join(", ", tabTitles)}.");
    return 2;
}

var gameOutputLogPath = Path.Combine(RepositoryPaths.LogDirectory(), "workshop-game-output.log");
foreach (var line in Engine.Persistence.StartupCleanup.Run([new Engine.Diagnostics.RollingLogTarget(gameOutputLogPath)], save: null).ReportLines())
{
    Console.WriteLine(line);
}

const string spritesheetsDirectory = "Spritesheets";
const string spriteManifestFileName = "SpriteManifest.json";
var gameShellOutputDirectory = RepositoryPaths.GameShellOutputDirectory();
var options = new WorkshopHostOptions(
    WindowTitle: "Cadence Workshop - DungeonCrawlerWorld",
    FontsDirectory: "Fonts",
    SpritesheetsDirectory: spritesheetsDirectory,
    SpriteManifestFileName: spriteManifestFileName,
    EffectsDirectory: "Effects",
    GameLaunchTarget: new GameLaunchTarget(RepositoryPaths.GameExecutablePath(), RepositoryPaths.GameLaunchArguments(), gameShellOutputDirectory, RepositoryPaths.GameShellBuildCommand()),
    CreateTabs: uiEngine =>
    {
        var builds = new WorkshopContentBuilds(Game.Bootstrap.PlayerFolders.Mods, RepositoryPaths.MasterContentDirectory(), RepositoryPaths.DraftsContentDirectory(), settingsSources, Console.Error);
        var gameSession = builds.BuildSession();
        var workspace = new Builders.Editing.ContentWorkspace(gameSession, builds.Saving());
        uiEngine.SpriteSheetService.Sprites.ReportManifestProblems(Console.Error);
        MissingSpriteCheck.Report(gameSession.Views.ContentSpriteNames, uiEngine.SpriteSheetService.Sprites, Console.Error);
        var spritesTab = SpritesTab.Create(uiEngine, Path.Combine(AppContext.BaseDirectory, spriteManifestFileName), spritesheetsDirectory);
        var builderTabs = BuilderTabs.Create(uiEngine, builds.BuildPreview, workspace);
        var tabs = TabArgument.Select([.. builderTabs.Tabs, spritesTab, new ControlGalleryTab()], tabName);
        var editing = new WorkshopContentEditing(builderTabs.Editing, session => [.. ContentSession.LoadProblems(session), .. spritesTab.Problems]);
        return new WorkshopTabSet(tabs, editing.LoadProblems) { Editing = editing };
    })
{
    GameOutputLogPath = gameOutputLogPath,
    DebugLaunchOptionsPath = DebugLaunchOptionsStore.FilePathForGame("DungeonCrawlerWorld"),
    DiagnosticsReportDirectories = [Path.Combine(RepositoryPaths.LogDirectory(), "diagnostics"), Path.Combine(RepositoryPaths.LogDirectory(), "phase-benchmarks")],
};

using var workshop = new WorkshopHost(options);
workshop.Run();
return 0;
