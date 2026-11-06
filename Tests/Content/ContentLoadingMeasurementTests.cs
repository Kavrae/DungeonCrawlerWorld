using System.Diagnostics;
using System.Text.Json;
using Engine.Bootstrap;
using Engine.Content;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.World;

namespace Tests.Content;

/// <summary>
/// Measures loading content eagerly against loading it by id over a synthetic master of 10,000 definitions
/// (PLAN-data-driven-actions-and-items.md Phase 5): time, allocation and what stays held, for the content alone and for a whole
/// session build, and how much of an eager load is JSON parsing. Tagged Performance, so it runs only when asked for:
/// `dotnet test Tests/Tests.csproj --filter "TestCategory=Performance&amp;FullyQualifiedName~ContentLoadingMeasurement" --logger "console;verbosity=detailed"`.
/// It reports rather than judges; the one assertion is that by-id startup decodes far fewer records than an eager one.
/// </summary>
[TestClass]
[TestCategory("Performance")]
[DoNotParallelize]
public sealed class ContentLoadingMeasurementTests
{
    private const int SyntheticActions = 5_000;
    private const int SyntheticItems = 5_000;

    private static readonly ContentTypeRegistry Types = EcsBuilder.Declare(GameBootstrapper.BuiltInModules()).ContentTypes;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EagerAgainstById_OverASyntheticMasterOf10000Definitions()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            WriteSyntheticMaster(directory);
            var layers = GameContent.MasterOnly(directory);
            var startupActions = Game.Blueprints.PlayerKit.Definition.UsesActions;
            var startupItems = Game.Blueprints.PlayerKit.Definition.UsesItems;

            Measure("Warm-up", () => GameContent.Load(layers, Types));

            var (eager, eagerTime, eagerAllocated, eagerHeld) = Measure("Eager: decode every record", () => GameContent.Load(layers, Types));
            var (byId, byIdTime, byIdAllocated, byIdHeld) = Measure("By id: index, then the player's kit", () =>
            {
                var content = GameContent.Open(layers, Types);
                content.Prefetch(startupActions, startupItems);
                return content;
            });
            var (_, parseTime, parseAllocated, _) = Measure("JSON parse alone, every record line", () => ParseEveryLine(directory));

            Report($"Records decoded: eager {Decoded(eager)}, by id {Decoded(byId)}");
            Report($"JSON parsing is {parseTime.TotalMilliseconds / eagerTime.TotalMilliseconds:P0} of the eager load's time and {(double)parseAllocated / eagerAllocated:P0} of its allocation");
            Report($"By id against eager: {byIdTime.TotalMilliseconds / eagerTime.TotalMilliseconds:P0} of the time, {(double)byIdAllocated / eagerAllocated:P0} of the allocation, {(double)byIdHeld / eagerHeld:P0} of the memory held");

            Measure("Session build, eager", () => BuildSession(layers, loadsContentById: false));
            Measure("Session build, by id", () => BuildSession(layers, loadsContentById: true));

            Assert.IsLessThan(Decoded(eager) / 10, Decoded(byId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int Decoded(GameContent content) => content.LoadedCounts.Actions + content.LoadedCounts.Items + content.LoadedCounts.Auras;

    private static GameSession BuildSession(ContentLayers layers, bool loadsContentById) =>
        GameBootstrapper.Build(ValidatedMods.None, GameBuildPass.CreatePlaceholderMap(), layers, sessionSeed: 1, initialEntityCapacity: 64, initialComponentCapacity: 64, buildsStagingWorldOnFirstRebuild: true, loadsContentById: loadsContentById);

    /// <summary>Runs make, reporting its time, its allocation and the memory still held once it returns; the result is kept alive until then.</summary>
    private (T Result, TimeSpan Time, long Allocated, long Held) Measure<T>(string name, Func<T> make)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var heldBefore = GC.GetTotalMemory(forceFullCollection: true);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();

        var result = make();

        stopwatch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var held = GC.GetTotalMemory(forceFullCollection: true) - heldBefore;
        GC.KeepAlive(result);
        Report($"{name}: {stopwatch.Elapsed.TotalMilliseconds:F1} ms, {allocated / 1_048_576.0:F1} MB allocated, {held / 1_048_576.0:F1} MB held");
        return (result, stopwatch.Elapsed, allocated, held);
    }

    private void Report(string line)
    {
        TestContext.WriteLine(line);
        Console.WriteLine(line);
    }

    /// <summary>Parses every record line of directory's tables with JsonDocument and nothing more: how much of a load the parser is.</summary>
    private static int ParseEveryLine(string directory)
    {
        var lines = 0;
        foreach (var file in new[] { GameContent.AurasFileName, GameContent.ActionsFileName, GameContent.ItemsFileName })
        {
            foreach (var line in File.ReadAllLines(Path.Combine(directory, file)).Skip(1))
            {
                using var document = JsonDocument.Parse(line);
                lines += document.RootElement.ValueKind == JsonValueKind.Object ? 1 : 0;
            }
        }

        return lines;
    }

    /// <summary>The shipped master with SyntheticActions and SyntheticItems more definitions: copies of the shipped ones, each under its own Guid and name, so every one decodes and passes its checks.</summary>
    private static void WriteSyntheticMaster(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(GameContent.DefaultMasterDirectory, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(directory, Path.GetRelativePath(GameContent.DefaultMasterDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        var content = GameContent.Load(TestContent.Layers, Types);
        var actions = content.Actions.ToList();
        var items = content.Items.ToList();
        var syntheticActions = Enumerable.Range(0, SyntheticActions).Select(index => actions[index % actions.Count] with { Id = Guid.NewGuid(), Name = $"{actions[index % actions.Count].Name} {index}" });
        var syntheticItems = Enumerable.Range(0, SyntheticItems).Select(index => items[index % items.Count] with { Id = Guid.NewGuid(), Name = $"{items[index % items.Count].Name} {index}" });

        GameContent.WriteMaster(directory, content.Auras, [.. actions, .. syntheticActions], [.. items, .. syntheticItems], Types);
    }
}
