using Engine.Math;
using Game.Bootstrap;
using Game.Modules.Energy.Components;
using Game.Modules.Health.Components;
using Game.World;
using GameWorldModel = Game.World.World;

namespace Tests.Bootstrap;

/// <summary>
/// Exercises GameBootstrapper's actual composition point against real compiled mod
/// assemblies (Mods.ExampleMod.dll, Mods.TestFixtures.dll), both built alongside Tests (see
/// Tests.csproj's build-order-only references) but never directly referenced -- these tests
/// only ever reach mod types through ModuleLoader's reflection path, the same way a real mod
/// dropped in Mods/ would be found. The two adversarial fixtures (a throwing module, a
/// built-in-replacing module) live in the separate Mods.TestFixtures project rather than
/// inside Mods.ExampleMod itself -- Mods.ExampleMod is the plan's shippable "one trivial
/// IModule" verification fixture, and dropping a module that intentionally breaks
/// HealthComponent into the same DLL as that would make the real game crash if someone
/// actually copied Mods.ExampleMod.dll into Mods/ per the plan's own verification steps.
/// </summary>
[TestClass]
public sealed class GameBootstrapperTests
{
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DungeonCrawlerWorld.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root from the test assembly's location.");
        }

        return directory.FullName;
    }

    private static string FindAssemblyPath(string projectName)
    {
        var assemblyFileName = $"{projectName}.dll";
        return Directory.EnumerateFiles(
                Path.Combine(FindRepositoryRoot(), "Mods", projectName, "bin"),
                assemblyFileName,
                SearchOption.AllDirectories)
            .First();
    }

    private static void CopyModTo(string modsDirectory, string projectName)
    {
        Directory.CreateDirectory(modsDirectory);
        var sourcePath = FindAssemblyPath(projectName);
        var destinationPath = Path.Combine(modsDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static (GameWorldModel World, MathUtility MathUtility) BuildWorldAndMathUtility() =>
        (new GameWorldModel(new Map(new Vector3Int(5, 5, 1))), new MathUtility());

    /// <summary>
    /// ModuleLoader's collectible AssemblyLoadContext is never explicitly unloaded (by
    /// design -- see its doc comment), so on Windows the mod DLL it loaded stays
    /// memory-mapped, and therefore locked on disk, for the rest of this test process's
    /// lifetime. Best-effort cleanup only; leaving a temp directory behind is harmless.
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    [TestMethod]
    public void Build_EmptyModsDirectory_BehavesLikeNoModsInstalled()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var (world, mathUtility) = BuildWorldAndMathUtility();

            var result = GameBootstrapper.Build(world, mathUtility, directory.FullName, initialEntityCapacity: 100, initialComponentCapacity: 50);

            Assert.IsEmpty(result.Failures);
            Assert.IsTrue(result.EcsContext.ComponentManager.IsRegistered<HealthComponent>());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Build_TrivialExampleMod_RegistersAlongsideBuiltInsWithNoFailures()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            CopyModTo(directory.FullName, "Mods.ExampleMod");
            var (world, mathUtility) = BuildWorldAndMathUtility();

            var result = GameBootstrapper.Build(world, mathUtility, directory.FullName, initialEntityCapacity: 100, initialComponentCapacity: 50);

            Assert.IsEmpty(result.Failures);
            // ExampleModule registers nothing observable -- its presence is proven by the
            // built-ins it rides alongside still registering correctly (no exception, no
            // failure reported), exactly what "join without disturbing anything" means for a
            // trivial mod.
            Assert.IsTrue(result.EcsContext.ComponentManager.IsRegistered<HealthComponent>());
        }
        finally
        {
            TryDeleteDirectory(directory.FullName);
        }
    }

    [TestMethod]
    public void Build_ThrowingMod_IsExcludedAndReported_ButRestOfWorldStillBuilds()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            CopyModTo(directory.FullName, "Mods.TestFixtures");
            var (world, mathUtility) = BuildWorldAndMathUtility();

            var result = GameBootstrapper.Build(world, mathUtility, directory.FullName, initialEntityCapacity: 100, initialComponentCapacity: 50);

            // Mods.TestFixtures.dll also defines ReplacementHealthModule, which survives
            // dry-run and legitimately replaces HealthModule -- so HealthComponent itself
            // isn't a valid "rest of world still builds" signal here (see that module's doc
            // comment). EnergyComponent is untouched by either fixture module.
            Assert.HasCount(1, result.Failures);
            Assert.Contains("ThrowingModule", result.Failures[0].Source);
            Assert.IsTrue(result.EcsContext.ComponentManager.IsRegistered<EnergyComponent>());
            var entityId = result.EcsContext.EntityManager.CreateEntity();
            Assert.AreEqual(0, entityId);
        }
        finally
        {
            TryDeleteDirectory(directory.FullName);
        }
    }

    [TestMethod]
    public void Build_ModWithMatchingBuiltInId_ReplacesTheBuiltIn()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            CopyModTo(directory.FullName, "Mods.TestFixtures");
            var (world, mathUtility) = BuildWorldAndMathUtility();

            var result = GameBootstrapper.Build(world, mathUtility, directory.FullName, initialEntityCapacity: 100, initialComponentCapacity: 50);

            // ReplacementHealthModule shares the real HealthModule's Id and registers
            // nothing -- HealthComponent ending up unregistered is only possible if the mod
            // actually replaced the built-in HealthModule rather than coexisting with it.
            // EnergyComponent registering normally confirms this replacement is selective,
            // not a side effect of the whole world failing to build.
            Assert.IsFalse(result.EcsContext.ComponentManager.IsRegistered<HealthComponent>());
            Assert.IsTrue(result.EcsContext.ComponentManager.IsRegistered<EnergyComponent>());
        }
        finally
        {
            TryDeleteDirectory(directory.FullName);
        }
    }
}
