using Builders;
using DungeonCrawlerWorld.Workshop;
using Engine.Settings;
using Presentation.Rendering;
using Workshop;
using Workshop.Sprites;

Game.GameFrameRate.Apply();

IReadOnlyList<ISettingsSource> settingsSources = [new CommandLineSettingsSource(args)];
var tabName = TabArgument.Parse(args);
IReadOnlyList<string> tabTitles = [.. BuilderTabs.Titles, SpritesTab.TabTitle, ControlGalleryTab.TabTitle];
if (tabName is not null && !tabTitles.Contains(tabName, StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"--tab={tabName} names no tab. Tabs: {string.Join(", ", tabTitles)}.");
    return 2;
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
    GameLaunchTarget: new GameLaunchTarget(RepositoryPaths.GameExecutablePath(), string.Empty, gameShellOutputDirectory, RepositoryPaths.GameShellBuildCommand()),
    CreateTabs: uiEngine =>
    {
        var validatedMods = ContentSession.ValidateMods(Path.Combine(gameShellOutputDirectory, "Mods"), settingsSources);
        var gameSession = ContentSession.Build(validatedMods, settingsSources, Console.Error);
        uiEngine.SpriteSheetService.Sprites.ReportManifestProblems(Console.Error);
        MissingSpriteCheck.Report(gameSession.Views.ContentSpriteNames, uiEngine.SpriteSheetService.Sprites, Console.Error);
        var spritesTab = SpritesTab.Create(uiEngine, Path.Combine(AppContext.BaseDirectory, spriteManifestFileName), spritesheetsDirectory);
        return new WorkshopTabSet(
            TabArgument.Select([.. BuilderTabs.Create(uiEngine, gameSession, map => ContentSession.BuildPreview(validatedMods, map, settingsSources)), spritesTab, new ControlGalleryTab()], tabName),
            [.. ContentSession.LoadProblems(gameSession, RepositoryPaths.MasterContentDirectory()), .. spritesTab.Problems]);
    })
{
    GameOutputLogPath = Path.Combine(RepositoryPaths.LogDirectory(), "workshop-game-output.log"),
};

using var workshop = new WorkshopHost(options);
workshop.Run();
return 0;
