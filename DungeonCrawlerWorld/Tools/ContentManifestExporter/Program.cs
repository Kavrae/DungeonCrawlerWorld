using Engine.Math;
using Game.Bootstrap;
using Game.Content;

const string SolutionFileName = "DungeonCrawlerWorld.sln";
const string OutputPrefix = "--output=";

var outputDirectory = args.FirstOrDefault(argument => argument.StartsWith(OutputPrefix, StringComparison.OrdinalIgnoreCase))?[OutputPrefix.Length..]
    ?? Path.Combine(FindRepositoryRoot(), "DungeonCrawlerWorld", "Content", "Definitions", "Master");

var build = GameBuildPass.Run(GameBootstrapper.BuiltInModules(), [], GameBuildPass.CreatePlaceholderMap(), sessionSeed: 0, [], initialEntityCapacity: 16, initialComponentCapacity: 16);
var context = build.Context;

MasterContent.Write(outputDirectory, context.Actions.Definitions, context.Items.Definitions, context.ContentTypes);
Console.WriteLine($"Wrote {context.Actions.Count} actions, {context.Items.Count} items and {MasterContent.AurasNamedBy(context.Actions.Definitions, context.Items.Definitions).Count} auras to {outputDirectory}.");
return 0;

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? throw new InvalidOperationException($"No {SolutionFileName} above {AppContext.BaseDirectory}.");
}
