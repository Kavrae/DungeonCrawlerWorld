using Builders;
using DungeonCrawlerWorld.Workshop;
using Engine.Settings;
using Presentation.Rendering;
using Workshop;

Game.GameFrameRate.Apply();

IReadOnlyList<ISettingsSource> settingsSources = [new CommandLineSettingsSource(args)];
var tabName = TabArgument.Parse(args);
if (tabName is not null && !BuilderTabs.Titles.Contains(tabName, StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"--tab={tabName} names no tab. Tabs: {string.Join(", ", BuilderTabs.Titles)}.");
    return 2;
}

var gameShellOutputDirectory = RepositoryPaths.GameShellOutputDirectory();
var options = new WorkshopHostOptions(
    WindowTitle: "Cadence Workshop - DungeonCrawlerWorld",
    FontsDirectory: "Fonts",
    SpritesheetsDirectory: "Spritesheets",
    SpriteManifestFileName: "SpriteManifest.json",
    GameLaunchTarget: new GameLaunchTarget(RepositoryPaths.GameExecutablePath(), string.Empty, gameShellOutputDirectory),
    CreateTabs: uiEngine =>
    {
        var gameSession = ContentSession.Build(Path.Combine(gameShellOutputDirectory, "Mods"), settingsSources, Console.Error);
        MissingSpriteCheck.Report(gameSession.Views.ContentSpriteNames, uiEngine.SpriteSheetService.Sprites, Console.Error);
        return TabArgument.Select(BuilderTabs.Create(uiEngine, gameSession), tabName);
    });

using var workshop = new WorkshopHost(options);
workshop.Run();
return 0;
