using Engine.Bootstrap;
using Engine.Content;
using Game.Bootstrap;
using Game.Content;

namespace Tests.Content;

/// <summary>The game's content over a stack of layers: what each layer's failing record costs, and that every record knows its layer.</summary>
[TestClass]
public sealed class GameContentLayersTests
{
    private static readonly ContentTypeRegistry Types = EcsBuilder.Declare(GameBootstrapper.BuiltInModules()).ContentTypes;

    private static string[] MasterActionLines() =>
        File.ReadAllLines(Path.Combine(GameContent.DefaultMasterDirectory, GameContent.ActionsFileName)).Skip(1).ToArray();

    private static ContentLayer ActionsLayer(string name, ContentLayerKind kind, params string[] lines) =>
        ContentLayer.FromText(name, kind, new Dictionary<string, string>
        {
            [GameContent.ActionsFileName] = string.Join('\n', [ContentTable.Header(GameContent.ActionsTable), .. lines]) + "\n",
        });

    private static string Renamed(string line, string name)
    {
        var start = line.IndexOf("\"n\":\"", StringComparison.Ordinal) + 5;
        return line[..start] + name + line[line.IndexOf('"', start)..];
    }

    private static ContentLayers Over(params ContentLayer[] layers) => new([TestContent.MasterLayer, .. layers]);

    [TestMethod]
    public void AnOverridingLayersRecord_ReplacesTheMasterRecordInPlace_AndKnowsItsLayer()
    {
        var masterLines = MasterActionLines();
        var save = ActionsLayer("Save", ContentLayerKind.Save, Renamed(masterLines[1], "Saved Action"));

        var content = GameContent.Open(Over(save), Types);

        var master = GameContent.Open(TestContent.Layers, Types);
        CollectionAssert.AreEqual(master.Actions.Select(action => action.Id).ToArray(), content.Actions.Select(action => action.Id).ToArray());
        Assert.AreEqual("Saved Action", content.Actions[1].Name);
        Assert.IsTrue(content.TryGetLocation(GameContent.ActionsTable, content.Actions[1].Id, out var saved));
        Assert.AreSame(save, saved.Layer);
        Assert.IsTrue(content.TryGetLocation(GameContent.ActionsTable, content.Actions[0].Id, out var shipped));
        Assert.AreEqual(ContentLayerKind.Master, shipped.Layer.Kind);
    }

    [TestMethod]
    public void ASavesFailingRecord_IsLeftOut_AndNeverStopsTheGame()
    {
        var save = ActionsLayer("Save", ContentLayerKind.Save, "{ not json", Renamed(MasterActionLines()[0], "Saved Action"));

        var content = GameContent.Open(Over(save), Types);

        Assert.AreEqual("Saved Action", content.Actions[0].Name);
        Assert.AreEqual("Save", Assert.ContainsSingle(content.Failures).Layer);
        content.ThrowIfFailed();
    }

    [TestMethod]
    public void AModsFailingRecord_StopsTheGamesBuild()
    {
        var mod = ActionsLayer("Mod", ContentLayerKind.Mod, "{ not json");

        var content = GameContent.Open(Over(mod), Types);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => content.Actions);
        StringAssert.Contains(exception.Message, "[Mod] ");
    }

    [TestMethod]
    public void ABuild_ReadsTheLayersItIsGiven()
    {
        var save = ActionsLayer("Save", ContentLayerKind.Save, Renamed(MasterActionLines()[0], "Saved Action"));

        var build = GameBuildPass.Run(GameBootstrapper.BuiltInModules(), [], GameBuildPass.CreatePlaceholderMap(), Over(save), sessionSeed: 0, [], initialEntityCapacity: 16, initialComponentCapacity: 16);

        var savedId = GameContent.Open(Over(save), Types).Actions[0].Id;
        Assert.AreEqual("Saved Action", build.Context.Actions.Get(savedId).Name);
    }
}
