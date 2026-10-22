using Builders;
using DungeonCrawlerWorld.Workshop;
using Engine.Settings;
using Presentation.Rendering;
using Workshop;

Game.GameFrameRate.Apply();

IReadOnlyList<ISettingsSource> settingsSources = [new CommandLineSettingsSource(args)];
var tabName = TabArgument.Parse(args);
IReadOnlyList<string> tabTitles = [.. BuilderTabs.Titles, ControlGalleryTab.TabTitle];
if (tabName is not null && !tabTitles.Contains(tabName, StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"--tab={tabName} names no tab. Tabs: {string.Join(", ", tabTitles)}.");
    return 2;
}

var gameShellOutputDirectory = RepositoryPaths.GameShellOutputDirectory();
var options = new WorkshopHostOptions(
    WindowTitle: "Cadence Workshop - DungeonCrawlerWorld",
    FontsDirectory: "Fonts",
    SpritesheetsDirectory: "Spritesheets",
    SpriteManifestFileName: "SpriteManifest.json",
    GameLaunchTarget: new GameLaunchTarget(RepositoryPaths.GameExecutablePath(), string.Empty, gameShellOutputDirectory, RepositoryPaths.GameShellBuildCommand()),
    CreateTabs: uiEngine =>
    {
        var validatedMods = ContentSession.ValidateMods(Path.Combine(gameShellOutputDirectory, "Mods"), settingsSources);
        var gameSession = ContentSession.Build(validatedMods, settingsSources, Console.Error);
        uiEngine.SpriteSheetService.Sprites.ReportManifestProblems(Console.Error);
        MissingSpriteCheck.Report(gameSession.Views.ContentSpriteNames, uiEngine.SpriteSheetService.Sprites, Console.Error);
        return new WorkshopTabSet(TabArgument.Select([.. BuilderTabs.Create(uiEngine, gameSession, map => ContentSession.BuildPreview(validatedMods, map, settingsSources)), new ControlGalleryTab()], tabName), ContentSession.LoadProblems(gameSession, RepositoryPaths.MasterContentDirectory()));
    })
{
    GameOutputLogPath = Path.Combine(RepositoryPaths.LogDirectory(), "workshop-game-output.log"),
};

using var workshop = new WorkshopHost(options);
workshop.Run();
return 0;
