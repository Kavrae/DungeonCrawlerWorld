namespace Workshop.Tests;

[TestClass]
public sealed class GameLauncherTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

    private static GameLauncher LauncherFor(string arguments) =>
        new(new GameLaunchTarget(Path.Combine(Environment.SystemDirectory, "cmd.exe"), arguments, Environment.SystemDirectory));

    private static void WaitUntilNotRunning(GameLauncher launcher)
    {
        var deadline = DateTime.UtcNow + ExitTimeout;
        while (launcher.IsGameRunning && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }
    }

    [TestMethod]
    public void IsGameRunning_BeforeAnyLaunch_IsFalse()
    {
        Assert.IsFalse(LauncherFor("/c exit").IsGameRunning);
    }

    [TestMethod]
    public void AfterTheGameExits_IsGameRunningIsFalse_AndPlayStartsAnotherOne()
    {
        var launcher = LauncherFor("/c exit");

        Assert.IsTrue(launcher.TryLaunch(out _));
        WaitUntilNotRunning(launcher);

        Assert.IsFalse(launcher.IsGameRunning);
        Assert.IsTrue(launcher.TryLaunch(out var status), status);
    }

    [TestMethod]
    public void WhileTheGameRuns_PlayStartsNothing()
    {
        var launcher = LauncherFor("/c ping -n 6 127.0.0.1 > nul");
        Assert.IsTrue(launcher.TryLaunch(out _));

        Assert.IsTrue(launcher.IsGameRunning);
        Assert.IsFalse(launcher.TryLaunch(out var status));
        Assert.AreEqual("The game is already running.", status);
    }

    [TestMethod]
    public void WithNoBuiltExecutable_PlayStartsNothing_AndSaysWhere()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "no-such-game", "DungeonCrawlerWorld.exe");
        var launcher = new GameLauncher(new GameLaunchTarget(missingPath, string.Empty, Path.GetTempPath()));

        Assert.IsFalse(launcher.TryLaunch(out var status));
        StringAssert.Contains(status, missingPath);
        Assert.IsFalse(launcher.IsGameRunning);
    }
}
