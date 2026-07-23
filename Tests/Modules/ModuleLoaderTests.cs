using Engine.Modules;

namespace Tests.Modules;

[TestClass]
public sealed class ModuleLoaderTests
{
    [TestMethod]
    public void LoadFromDirectory_NonexistentDirectory_ReturnsEmptyResultWithoutThrowing()
    {
        var result = ModuleLoader.LoadFromDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.IsEmpty(result.Modules);
        Assert.IsEmpty(result.Failures);
    }

    [TestMethod]
    public void LoadFromDirectory_EmptyDirectory_ReturnsEmptyResult()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var result = ModuleLoader.LoadFromDirectory(directory.FullName);

            Assert.IsEmpty(result.Modules);
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

            var result = ModuleLoader.LoadFromDirectory(directory.FullName);

            Assert.IsEmpty(result.Modules);
            Assert.HasCount(1, result.Failures);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}