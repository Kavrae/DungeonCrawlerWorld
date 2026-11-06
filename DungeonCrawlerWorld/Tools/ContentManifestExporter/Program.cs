using Engine.Content;
using Game.Bootstrap;
using Game.Content;

Game.GameFrameRate.Apply();

const string SolutionFileName = "DungeonCrawlerWorld.sln";
const string OutputPrefix = "--output=";

var outputDirectory = args.FirstOrDefault(argument => argument.StartsWith(OutputPrefix, StringComparison.OrdinalIgnoreCase))?[OutputPrefix.Length..]
    ?? Path.Combine(FindRepositoryRoot(), "DungeonCrawlerWorld", "Content", "Definitions", "Master");

var build = GameBuildPass.Run(GameBootstrapper.BuiltInModules(), [], GameBuildPass.CreatePlaceholderMap(), GameContent.MasterOnly(GameContent.DefaultMasterDirectory), sessionSeed: 0, [], initialEntityCapacity: 16, initialComponentCapacity: 16);
var context = build.Context;

bool IsFromAnotherLayer(string tableName, Guid id) =>
    context.Content.TryGetLocation(tableName, id, out var location) && location.Layer.Kind != ContentLayerKind.Master;

var actions = context.Actions.Definitions.Where(action => !IsFromAnotherLayer(GameContent.ActionsTable, action.Id)).ToList();
var items = context.Items.Definitions.Where(item => !IsFromAnotherLayer(GameContent.ItemsTable, item.Id)).ToList();

var auras = context.Content.Auras.Where(aura => !IsFromAnotherLayer(GameContent.AurasTable, aura.Id)).ToList();

GameContent.WriteMaster(outputDirectory, auras, actions, items, context.ContentTypes);
Console.WriteLine($"Wrote {actions.Count} actions, {items.Count} items and {auras.Count} auras to {outputDirectory}.");
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
