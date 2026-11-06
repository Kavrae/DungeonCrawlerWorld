using Engine.Math;
using Engine.Tiles;
using Game.Bootstrap;
using Game.Spawning;
using Game.World;

namespace Tests.Blueprints;

/// <summary>Every built-in blueprint declares the content its build reads (BlueprintDefinition.UsesActions, UsesItems), so a plan that spawns it loads all of it ahead and building it never loads anything on the spot.</summary>
[TestClass]
public sealed class BlueprintContentDeclarationTests
{
    [TestMethod]
    public void BuildingEverySpawnableBlueprint_LoadsNothingItsDeclarationsDidNotName()
    {
        var pass = GameBuildPass.Run(GameBootstrapper.BuiltInModules(), [], new Map(new MapBounds(0, 0, 128, 128, 3)), TestContent.Layers, sessionSeed: 1, [], initialEntityCapacity: 5000, initialComponentCapacity: 5000, new UniqueNumberAllocator(1, 1, 24), loadsContentById: true);
        var definitions = pass.Context.Definitions;
        var factory = pass.Factory;
        var loader = pass.Context.ContentCatalogLoader;
        var undeclared = new List<string>();
        var position = 0;

        for (ushort blueprintId = 1; blueprintId <= definitions.Count; blueprintId++)
        {
            if (!definitions.TryResolve(blueprintId, out var resolved) || !resolved.IsSpawnable)
            {
                continue;
            }

            factory.RegisterContent(factory.ContentOf([blueprintId]));
            var missedBefore = loader.MissedPlanLoads;

            var entityId = factory.Spawn(new SpawnRequest(blueprintId, 4 + (position % 30) * 4, 4 + (position / 30) * 4) with { Crawler = true });
            position++;
            if (entityId == EntityFactory.NoEntity)
            {
                undeclared.Add($"{resolved.Definition.Name}: couldn't be placed");
                continue;
            }

            pass.Context.EntityFactory.Skeletons.EnsureBuilt(entityId);
            if (loader.MissedPlanLoads != missedBefore)
            {
                undeclared.Add($"{resolved.Definition.Name}: {loader.MissedPlanLoads - missedBefore} load(s) it didn't declare");
            }
        }

        Assert.IsEmpty(undeclared, string.Join(Environment.NewLine, undeclared));
    }
}
