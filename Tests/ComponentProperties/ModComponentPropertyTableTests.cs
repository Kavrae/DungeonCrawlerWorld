using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Engine.ECS.Properties;
using Engine.Modules;
using Game.Modules;
using Tests.Bootstrap;

namespace Tests.ComponentProperties;

/// <summary>A mod's described component fills its slot through the mod assembly's generated module initializer, and the slot doesn't keep the mod's load context alive.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ModComponentPropertyTableTests
{
    private const string FixtureComponentTypeName = "Mods.TestFixtures.DescribedFixtureComponent";

    [TestMethod]
    public void AModsDescribedComponent_RegistersItselfOnLoadAndItsContextStillUnloads()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            GameBootstrapperTests.CopyModTo(directory, "Mods.TestFixtures");

            var (context, propertyNames) = LoadThroughTheModPipelineAndUnload(directory);

            CollectionAssert.AreEqual(new[] { "Charges" }, propertyNames, "The mod's module initializer didn't register its table when ModuleLoader loaded it.");
            for (var attempt = 0; attempt < 20 && context.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.IsFalse(context.IsAlive, "The mod's collectible load context was kept alive after Unload.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Context, string[] PropertyNames) LoadThroughTheModPipelineAndUnload(string directory)
    {
        var loaded = ModuleLoader.LoadFromDirectory<GameModuleContext>(directory);
        var assembly = loaded.ModuleFactories[0].ModuleType.Assembly;
        var context = AssemblyLoadContext.GetLoadContext(assembly)!;
        Assert.IsTrue(context.IsCollectible);

        var componentType = assembly.GetType(FixtureComponentTypeName, throwOnError: true)!;
        var access = typeof(ComponentPropertyAccessOf<>).MakeGenericType(componentType).GetProperty("Access")!.GetValue(null);
        var table = (ComponentPropertyTable?)access?.GetType().GetProperty("Table")!.GetValue(access);
        var propertyNames = table?.Properties.Select(property => property.Name).ToArray() ?? [];

        context.Unload();
        return (new WeakReference(context), propertyNames);
    }
}
