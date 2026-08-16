using Engine.Diagnostics;

var diagnosticsFeatures = DiagnosticsFeaturesParser.Parse(args);

using var game = new DungeonCrawlerWorld.GameLoop(diagnosticsFeatures);
game.Run();
