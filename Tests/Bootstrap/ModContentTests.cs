using Engine.Content;
using Engine.Math;
using Engine.Modules;
using Game.Bootstrap;
using Game.Content;
using Game.Modules;
using Game.Modules.Inventory;
using Game.World;

namespace Tests.Bootstrap;

/// <summary>Mods with content: each folder in the mods directory is one mod -- its DLLs and its Definitions folder, a Mod content layer -- kept or dropped as a whole.</summary>
[TestClass]
public sealed class ModContentTests
{
    private static readonly Guid ExampleTonicId = new("e6f2a017-4b3d-4a1e-9c72-000000000101");

    private string _modsDirectory = null!;

    [TestInitialize]
    public void CreateDirectory() => _modsDirectory = Directory.CreateTempSubdirectory().FullName;

    /// <summary>A loaded mod's DLL stays locked for the rest of the run (GameBootstrapperTests.TryDeleteDirectory), so the folder may be left behind.</summary>
    [TestCleanup]
    public void DeleteDirectory()
    {
        try
        {
            Directory.Delete(_modsDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The whole of projectName's build output folder, or one folder inside it, copied into the mods directory as the mod modName.</summary>
    private void CopyModFolder(string projectName, string modName, string? outputSubfolder = null)
    {
        var output = Path.GetDirectoryName(GameBootstrapperTests.FindAssemblyPath(projectName))!;
        var source = outputSubfolder is null ? output : Path.Combine(output, outputSubfolder);
        var target = Path.Combine(_modsDirectory, modName);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private GameSession BuildWithMods(ValidatedMods validated) =>
        GameBootstrapper.Build(validated, new Map(new Vector3Int(5, 5, 1)), TestContent.Layers, sessionSeed: 0, initialEntityCapacity: 100, initialComponentCapacity: 50);

    [TestMethod]
    public void AModFolder_IsOneMod_ItsModulesAndItsDefinitionsTogether_AndALooseDllsModulesAreEachTheirOwn()
    {
        CopyModFolder("Mods.ExampleMod", "ExampleMod");
        GameBootstrapperTests.CopyModTo(_modsDirectory, "Mods.TestFixtures");

        var loaded = ModuleLoader.LoadMods<GameModuleContext>(_modsDirectory);

        var example = loaded.Packages.Single(package => package.Name == "ExampleMod");
        Assert.AreEqual("ExampleModule", Assert.ContainsSingle(example.Modules).ModuleType.Name);
        Assert.AreEqual("Mod ExampleMod", example.Content!.Name);
        Assert.AreEqual(ContentLayerKind.Mod, example.Content.Kind);
        Assert.IsTrue(loaded.Packages.Where(package => package.Name != "ExampleMod").All(package => package.Modules.Count == 1 && package.Content is null));
    }

    [TestMethod]
    public void AModsContent_AddsAndReplacesRecords_AndEachKnowsItCameFromTheMod()
    {
        CopyModFolder("Mods.ExampleMod", "ExampleMod");

        var validated = ModValidation.Validate(_modsDirectory, TestContent.Layers, []);
        var session = BuildWithMods(validated);

        Assert.IsEmpty(validated.Failures);
        Assert.AreEqual("Mod ExampleMod", Assert.ContainsSingle(validated.ContentLayers).Name);
        var items = session.Catalogs.ItemCatalog;
        Assert.AreEqual("Example Tonic", items.Get(ExampleTonicId).Name);
        Assert.AreEqual(0.75f, ((Game.Effects.Entries.DirectHeal)items.Get(BuiltInItemIds.HealthPotion).Effects[0].Entries[0]).PercentOfMaxHealth);
        Assert.IsTrue(session.Content.TryGetLocation(GameContent.ItemsTable, BuiltInItemIds.HealthPotion, out var location));
        Assert.AreEqual("Mod ExampleMod", location.Layer.Name);
        Assert.Contains(ExampleTonicId, items.Summaries.Select(summary => summary.Id));
    }

    [TestMethod]
    public void AModWhoseRecordFails_IsDropped_NamingItsLayerFileAndLine_AndTheGameStillBuilds()
    {
        CopyModFolder("Mods.TestFixtures", "BrokenContentMod", "BrokenContentMod");

        var validated = ModValidation.Validate(_modsDirectory, TestContent.Layers, []);
        var session = BuildWithMods(validated);

        var failure = Assert.ContainsSingle(validated.Failures);
        StringAssert.EndsWith(failure.Source, "BrokenContentMod");
        StringAssert.Contains(failure.Exception.Message, "[Mod BrokenContentMod] Items.jsonl:2");
        Assert.IsEmpty(validated.ContentLayers);
        Assert.IsFalse(session.Content.Layers.Layers.Any(layer => layer.Kind == ContentLayerKind.Mod));
    }

    [TestMethod]
    public void TheRedirectsTable_IsReadAcrossLayers_AndKept()
    {
        var removed = Guid.NewGuid();
        var mod = ContentLayer.FromText("Mod Redirecting", ContentLayerKind.Mod, new Dictionary<string, string>
        {
            [GameContent.RedirectsFileName] = ContentTable.Header(GameContent.RedirectsTable) + "\n" +
                $"{{\"id\":\"{ContentGuid.Format(removed)}\",\"to\":\"{ContentGuid.Format(BuiltInItemIds.HealthPotion)}\",\"tb\":\"{GameContent.ItemsTable}\"}}\n",
        });

        var content = GameContent.Load(TestContent.Layers.With(mod), Engine.Bootstrap.EcsBuilder.Declare(GameBootstrapper.BuiltInModules()).ContentTypes);

        Assert.IsEmpty(content.Failures);
        Assert.AreEqual(new ContentRedirect(removed, BuiltInItemIds.HealthPotion, GameContent.ItemsTable), Assert.ContainsSingle(content.Redirects));
    }

    [TestMethod]
    public void AModsArea_IsPlacedBesideTheStartingArea_AndKnowsItCameFromTheMod()
    {
        CopyModFolder("Mods.ExampleMod", "ExampleMod");
        var validated = ModValidation.Validate(_modsDirectory, TestContent.Layers, []);
        var pass = GameBuildPass.Run(GameBootstrapper.BuiltInModules(), validated.Mods, new Map(new Engine.Tiles.MapBounds(0, 0, 80, 40, 3)), ValidatedLayers(validated), sessionSeed: 1, [], initialEntityCapacity: 5000, initialComponentCapacity: 5000, new UniqueNumberAllocator(1, 1, 24));
        var context = pass.Context;
        var builder = new Game.TestMapBuilder(pass.EcsContext.EntityManager, pass.Factory, context.Terrain, context.Auras, context.Definitions, context.Areas, context.UniqueEntities);

        var plan = builder.Plan(pass.World.Map, new NeighborhoodRecord(0, 0, seed: 42), populationSeed: 7);

        Assert.IsEmpty(validated.Failures);
        Assert.IsEmpty(context.ContentFailures);
        var chest = context.Definitions.GetId(Game.Blueprints.Objects.TreasureChest.Id);
        Assert.IsTrue(plan.Areas.Spawns.Any(spawn => spawn.Request.BlueprintId == chest && spawn.Request.X == 41 && spawn.Request.Y == 13), "Example room's chest, at its offset (36, 10) plus (5, 3).");
        Assert.IsTrue(context.Content.TryGetLocation(GameContent.AreasTable, ExampleRoomId, out var location));
        Assert.AreEqual("Mod ExampleMod", location.Layer.Name);
    }

    [TestMethod]
    public void AModWhoseAreaHasAnUndefinedSymbol_IsDropped_NamingItsFileRowAndColumn()
    {
        CopyModFolder("Mods.TestFixtures", "BrokenAreaMod", "BrokenAreaMod");

        var validated = ModValidation.Validate(_modsDirectory, TestContent.Layers, []);

        var failure = Assert.ContainsSingle(validated.Failures);
        StringAssert.Contains(failure.Exception.Message, "[Mod BrokenAreaMod] Areas/broken-area.json:14:9");
        StringAssert.Contains(failure.Exception.Message, "Symbol 'q'");
        Assert.IsEmpty(validated.ContentLayers);
    }

    [TestMethod]
    public void AModsPlacementOverTheStartingArea_IsRefused_AndTheStartingAreaKeepsItsPlace()
    {
        var area = File.ReadAllText(Path.Combine(Path.GetDirectoryName(GameBootstrapperTests.FindAssemblyPath("Mods.ExampleMod"))!, "Definitions", "Areas", "example-room.json"));
        var mod = ContentLayer.FromText("Mod Overlapping", ContentLayerKind.Mod, new Dictionary<string, string>
        {
            [$"{GameContent.AreasDirectoryName}/example-room.json"] = area,
            [GameContent.AreaPlacementsFileName] = ContentTable.Header(GameContent.AreaPlacementsTable) + "\n" +
                $"{{\"id\":\"{ContentGuid.Format(ExampleRoomPlacementId)}\",\"ar\":\"{ContentGuid.Format(ExampleRoomId)}\"}}\n",
        });

        var pass = GameBuildPass.Run(GameBootstrapper.BuiltInModules(), [], new Map(new Engine.Tiles.MapBounds(0, 0, 80, 40, 3)), TestContent.Layers.With(mod), sessionSeed: 1, [], initialEntityCapacity: 5000, initialComponentCapacity: 5000, new UniqueNumberAllocator(1, 1, 24));
        var areas = pass.Context.Areas;

        var refusal = Assert.ContainsSingle(pass.Context.ContentFailures);
        Assert.AreEqual("Mod Overlapping", refusal.Layer);
        StringAssert.Contains(refusal.Reason, "is refused: it overlaps 'Starting area'");
        Assert.IsFalse(areas.Placements.TryGet(ExampleRoomPlacementId, out _));
        Assert.ContainsSingle(areas.Placements.Definitions);
    }

    [TestMethod]
    public void ThePlayersFolders_AreUnderLocalAppData_AndNoModsFolderLoadsNone()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.AreEqual(Path.Combine(localAppData, "DungeonCrawlerWorld", "Mods"), PlayerFolders.Mods);
        Assert.AreEqual(Path.Combine(localAppData, "DungeonCrawlerWorld", "Saves"), PlayerFolders.Saves);

        var validated = ModValidation.Validate(modsDirectory: null, TestContent.Layers, []);

        Assert.IsEmpty(validated.Mods);
        Assert.IsEmpty(validated.Failures);
        Assert.IsEmpty(validated.ContentLayers);
    }

    [TestMethod]
    public void AModFolderWithADllThatFailsToLoad_IsLeftOutWhole_ItsContentWithIt()
    {
        CopyModFolder("Mods.ExampleMod", "ExampleMod");
        File.WriteAllBytes(Path.Combine(_modsDirectory, "ExampleMod", "Broken.dll"), [0x4D, 0x5A, 0x00, 0x01]);

        var loaded = ModuleLoader.LoadMods<GameModuleContext>(_modsDirectory);

        Assert.IsEmpty(loaded.Packages);
        Assert.IsTrue(loaded.Failures.Any(failure => failure.Source.EndsWith("Broken.dll", StringComparison.Ordinal)));
        Assert.IsTrue(loaded.Failures.Any(failure => failure.Source == Path.Combine(_modsDirectory, "ExampleMod")));
    }

    private static readonly Guid ExampleRoomId = new("e6f2a017-4b3d-4a1e-9c72-000000000202");
    private static readonly Guid ExampleRoomPlacementId = new("e6f2a017-4b3d-4a1e-9c72-000000000203");

    private static ContentLayers ValidatedLayers(ValidatedMods validated)
    {
        var layers = TestContent.Layers;
        foreach (var layer in validated.ContentLayers)
        {
            layers = layers.With(layer);
        }

        return layers;
    }
}
