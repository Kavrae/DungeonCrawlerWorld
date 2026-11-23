namespace Tests.Utilities;

[TestClass]
public sealed class TestSupportTests
{
    [TestMethod]
    public async Task TestWait_ReturnsOnceTheConditionHolds()
    {
        var checks = 0;

        await TestWait.UntilAsync(() => ++checks == 3, "the third check");

        Assert.AreEqual(3, checks);
    }

    [TestMethod]
    public async Task TestWait_ThatNeverHolds_FailsNamingWhatItWaitedFor()
    {
        var failure = await Assert.ThrowsExactlyAsync<AssertFailedException>(() => TestWait.UntilAsync(static () => false, "the moon turned blue", TimeSpan.FromMilliseconds(20)));

        StringAssert.Contains(failure.Message, "Timed out waiting until the moon turned blue.");
    }

    [TestMethod]
    public void TestRepository_Root_HoldsTheSolution() =>
        Assert.IsTrue(File.Exists(Path.Combine(TestRepository.Root, "DungeonCrawlerWorld.sln")));

    [TestMethod]
    public void TestDirectory_TryDelete_RemovesTheFolderAndWhatItHolds()
    {
        var directory = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(directory.FullName, "file.txt"), "content");

        TestDirectory.TryDelete(directory.FullName);

        Assert.IsFalse(Directory.Exists(directory.FullName));
    }

    [TestMethod]
    public void TestDirectory_TryDelete_AFolderThatIsGone_DoesNothing()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        TestDirectory.TryDelete(missing);

        Assert.IsFalse(Directory.Exists(missing));
    }
}
