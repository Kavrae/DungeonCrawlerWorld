using Engine.Diagnostics;
using Engine.Math;
using Engine.Settings;

Game.GameFrameRate.Apply();

var diagnosticsFeatures = DiagnosticsFeaturesParser.Parse(args);
var diagnosticsLink = DiagnosticsLinkArgument.Parse(args);
var randomSeed = RandomSeed.Parse(args);
var benchmarkFrameRange = BenchmarkFrameRange.Parse(args);
var mapSizeOverride = DungeonCrawlerWorld.MapSizeArgument.Parse(args);
var saveName = Engine.Persistence.SaveArgument.Parse(args);
var masterContentDirectory = DungeonCrawlerWorld.MasterContentArgument.Parse(args);
IReadOnlyList<ISettingsSource> settingsSources = [new CommandLineSettingsSource(args)];

if (HeadlessArgument.IsPresent(args))
{
    if (benchmarkFrameRange is not { } headlessRange)
    {
        Console.Error.WriteLine(HeadlessArgument.RequiresRangeMessage);
        return 2;
    }

    return DungeonCrawlerWorld.HeadlessBenchmark.Run(randomSeed, headlessRange, settingsSources, mapSizeOverride, diagnosticsFeatures, diagnosticsLink, saveName, masterContentDirectory);
}

using var game = new DungeonCrawlerWorld.GameLoop(settingsSources, diagnosticsFeatures, randomSeed, benchmarkFrameRange, mapSizeOverride, diagnosticsLink, saveName, masterContentDirectory);
game.Run();
return 0;
