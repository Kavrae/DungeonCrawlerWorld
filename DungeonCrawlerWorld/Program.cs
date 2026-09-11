using Engine.Diagnostics;
using Engine.Math;

var diagnosticsFeatures = DiagnosticsFeaturesParser.Parse(args);
var randomSeed = RandomSeed.Parse(args);
var benchmarkFrameRange = BenchmarkFrameRange.Parse(args);

using var game = new DungeonCrawlerWorld.GameLoop(diagnosticsFeatures, randomSeed, benchmarkFrameRange);
game.Run();
