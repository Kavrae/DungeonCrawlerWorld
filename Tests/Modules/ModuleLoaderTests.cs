using Engine.Modules;

namespace Tests.Modules;

[TestClass]
public sealed class ModuleLoaderTests
{
    [TestMethod]
    public void LoadFromDirectory_NonexistentDirectory_ReturnsEmptyResultWithoutThrowing()
    {
        var result = ModuleLoader.LoadFromDirectory<Game.Modules.GameModuleContext>(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.IsEmpty(result.ModuleFactories);
        Assert.IsEmpty(result.Failures);
    }

    [TestMethod]
    public void LoadFromDirectory_EmptyDirectory_ReturnsEmptyResult()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var result = ModuleLoader.LoadFromDirectory<Game.Modules.GameModuleContext>(directory.FullName);

            Assert.IsEmpty(result.ModuleFactories);
            Assert.IsEmpty(result.Failures);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadFromDirectory_GarbageBytesDll_ReportsFailureAndDoesNotThrow()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "garbage.dll"), [0x00, 0x01, 0x02, 0x03, 0x04]);

            var result = ModuleLoader.LoadFromDirectory<Game.Modules.GameModuleContext>(directory.FullName);

            Assert.IsEmpty(result.ModuleFactories);
            Assert.HasCount(1, result.Failures);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadFromDirectory_LoadsDllsInOrdinalFileNameOrder()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var fixturePath = Tests.Bootstrap.GameBootstrapperTests.FindAssemblyPath("Mods.TestFixtures");
            File.Copy(fixturePath, Path.Combine(directory.FullName, "b-fixtures.dll"));
            File.Copy(fixturePath, Path.Combine(directory.FullName, "a-fixtures.dll"));

            var result = ModuleLoader.LoadFromDirectory<Game.Modules.GameModuleContext>(directory.FullName);
            var loadedFileNames = result.ModuleFactories.Select(factory => Path.GetFileName(factory.ModuleType.Assembly.Location)).Distinct().ToList();

            CollectionAssert.AreEqual(new[] { "a-fixtures.dll", "b-fixtures.dll" }, loadedFileNames);
        }
        finally
        {
            Tests.Bootstrap.GameBootstrapperTests.TryDeleteDirectory(directory.FullName);
        }
    }
}
