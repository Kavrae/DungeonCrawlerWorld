using Engine.Diagnostics;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Bootstrap;
using Game.Modules.Core.Components;
using Game.Modules.Currency.Components;
using Game.Modules.Health.Components;
using Game.World;

namespace Tests.Bootstrap;

/// <summary>
/// Exercises GameBootstrapper's actual composition point against real compiled mod
/// assemblies (Mods.ExampleMod.dll, Mods.TestFixtures.dll), both built alongside Tests (see
/// Tests.csproj's build-order-only references) but never directly referenced -- these tests
/// only ever reach mod types through ModuleLoader's reflection path, the same way a real mod
/// dropped in Mods/ would be found. The adversarial fixtures (a throwing module, a complete and an
/// incomplete built-in replacement, an item with an undeclared tag) live in the separate Mods.TestFixtures project rather than
/// inside Mods.ExampleMod itself -- Mods.ExampleMod is the plan's shippable "one trivial
/// IModule" verification fixture, and copying it into a real game's Mods/ must not also switch
/// off health regeneration.
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

    private static GameSession BuildWithModsFrom(string modsDirectory) =>
        GameBootstrapper.Build(ModValidation.Validate(modsDirectory, []), new Map(new Vector3Int(5, 5, 1)), new MathUtility(), initialEntityCapacity: 100, initialComponentCapacity: 50);

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
            var result = BuildWithModsFrom(directory.FullName);

            Assert.IsEmpty(result.ModuleFailures);
            Assert.IsTrue(result.EcsContext.ComponentManager.IsRegistered<SimpleHealthComponent>());
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
            var result = BuildWithModsFrom(directory.FullName);

            Assert.IsEmpty(result.ModuleFailures);
            // ExampleModule registers nothing observable -- its presence is proven by the
            // built-ins it rides alongside still registering correctly (no exception, no
            // failure reported), exactly what "join without disturbing anything" means for a
            // trivial mod.
            Assert.IsTrue(result.EcsContext.ComponentManager.IsRegistered<SimpleHealthComponent>());
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
            var result = BuildWithModsFrom(directory.FullName);

            Assert.AreEqual(1, result.ModuleFailures.Count(failure => failure.Source.Contains("ThrowingModule")));
            Assert.IsTrue(result.EcsContext.ComponentManager.IsRegistered<ActionLockComponent>());
            var entityId = result.EcsContext.EntityManager.CreateEntity();
            Assert.AreEqual(0, entityId);
        }
        finally
        {
            TryDeleteDirectory(directory.FullName);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void Build_ModWithMatchingBuiltInId_ReplacesTheBuiltIn()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            CopyModTo(directory.FullName, "Mods.TestFixtures");
            var result = BuildWithModsFrom(directory.FullName);
            var recorder = new SystemOrderRecorder();
            using var frameCostSubscription = EngineHooks.FrameCosts.Subscribe(recorder);
            result.EcsContext.SystemManager.Update(new EngineTime(TimeSpan.Zero, TimeSpan.FromSeconds(1d / 60), false, 1));

            Assert.IsFalse(result.ModuleFailures.Any(failure => failure.Source.Contains("ReplacementHealthModule")));
            Assert.IsTrue(result.EcsContext.ComponentManager.IsRegistered<SimpleHealthComponent>());
            Assert.DoesNotContain("SimpleHealthRegenSystem", recorder.SystemNames);
            Assert.DoesNotContain("ComplexHealthRegenSystem", recorder.SystemNames);
            Assert.Contains("MovementSystem", recorder.SystemNames);
        }
        finally
        {
            TryDeleteDirectory(directory.FullName);
        }
    }

    [TestMethod]
    public void Build_ReplacementMissingTheBuiltInsComponents_IsExcludedNamingThem_AndTheBuiltInStays()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            CopyModTo(directory.FullName, "Mods.TestFixtures");
            var result = BuildWithModsFrom(directory.FullName);

            var failure = result.ModuleFailures.Single(failure => failure.Source.Contains("IncompleteReplacementCurrencyModule"));
            Assert.Contains("CurrencyComponent", failure.Exception.Message);
            Assert.IsTrue(result.EcsContext.ComponentManager.IsRegistered<CurrencyComponent>());
        }
        finally
        {
            TryDeleteDirectory(directory.FullName);
        }
    }

    [TestMethod]
    public void Build_ModWhoseItemUsesAnUndeclaredTag_IsExcludedNamingTheTag()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            CopyModTo(directory.FullName, "Mods.TestFixtures");
            var result = BuildWithModsFrom(directory.FullName);

            var failure = result.ModuleFailures.Single(failure => failure.Source.Contains("UndeclaredTagItemModule"));
            Assert.Contains("TestFixtures.NeverDeclared", failure.Exception.Message);
            Assert.IsFalse(result.Catalogs.ItemCatalog.TryGet(new Guid("e6f2a017-4b3d-4a1e-9c72-000000000005"), out _));
        }
        finally
        {
            TryDeleteDirectory(directory.FullName);
        }
    }
}
