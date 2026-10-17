using Engine.Math;
using Engine.Settings;
using Game.Bootstrap;

namespace DungeonCrawlerWorld.Workshop;

/// <summary>The game build the IDE reads its catalogs and views from: every built-in module and every valid mod, over a placeholder map, with no population and no player, never simulated.</summary>
public static class ContentSession
{
    private const int InitialEntityCapacity = 16;
    private const int InitialComponentCapacity = 16;

    /// <param name="modsDirectory">The mods the game itself would load -- GameShell's own Mods folder.</param>
    public static GameSession Build(string modsDirectory, IReadOnlyList<ISettingsSource> settingsSources, TextWriter failureLog)
    {
        var validatedMods = ModValidation.Validate(modsDirectory, settingsSources);
        var gameSession = GameBootstrapper.Build(validatedMods, GameBuildPass.CreatePlaceholderMap(), new MathUtility(new SeededRandom()), InitialEntityCapacity, InitialComponentCapacity, settingsSources: settingsSources);

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
}
