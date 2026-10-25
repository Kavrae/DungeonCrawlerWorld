using Engine.Diagnostics;
using Engine.Math;
using Engine.Settings;

Game.GameFrameRate.Apply();

var diagnosticsFeatures = DiagnosticsFeaturesParser.Parse(args);
var diagnosticsLink = DiagnosticsLinkArgument.Parse(args);
var randomSeed = RandomSeed.Parse(args);
var benchmarkFrameRange = BenchmarkFrameRange.Parse(args);
var mapSizeOverride = DungeonCrawlerWorld.MapSizeArgument.Parse(args);
IReadOnlyList<ISettingsSource> settingsSources = [new CommandLineSettingsSource(args)];

// Headless is benchmark-only: with no range there is nothing to stop it, and no window to close.
if (args.Contains("--headless", StringComparer.OrdinalIgnoreCase))
{
    if (benchmarkFrameRange is not { } headlessRange)
    {
        Console.Error.WriteLine("--headless requires --benchmark-frames=START-END.");
        return 2;
    }

    return DungeonCrawlerWorld.HeadlessBenchmark.Run(randomSeed, headlessRange, settingsSources, mapSizeOverride, diagnosticsFeatures, diagnosticsLink);
}

using var game = new DungeonCrawlerWorld.GameLoop(settingsSources, diagnosticsFeatures, randomSeed, benchmarkFrameRange, mapSizeOverride, diagnosticsLink);
game.Run();
return 0;
