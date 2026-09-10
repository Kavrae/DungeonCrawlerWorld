using Engine.Diagnostics;
using Engine.Math;

var diagnosticsFeatures = DiagnosticsFeaturesParser.Parse(args);
var randomSeed = RandomSeed.Parse(args);

using var game = new DungeonCrawlerWorld.GameLoop(diagnosticsFeatures, randomSeed);
game.Run();
