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

    private static List<GameOutputLine> OutputOf(GameLauncher launcher)
    {
        var lines = new List<GameOutputLine>();
        launcher.DrainOutput(lines);
        return lines;
    }

    [TestMethod]
    public void TheGamesOutput_IsCaptured_WithErrorsMarked()
    {
        var launcher = LauncherFor("/c echo to-standard-output& echo to-standard-error 1>&2");

        Assert.IsTrue(launcher.TryLaunch(out _));
        WaitUntilNotRunning(launcher);
        var output = OutputOf(launcher);

        CollectionAssert.Contains(output, new GameOutputLine("to-standard-output", IsError: false));
        CollectionAssert.Contains(output, new GameOutputLine("to-standard-error ", IsError: true));
    }

    [TestMethod]
    public void ExtraArguments_ComeAfterTheTargetsOwn_ForThatLaunchOnly()
    {
        var launcher = LauncherFor("/c echo");

        Assert.IsTrue(launcher.TryLaunch(out _, extraArguments: "--seed=7"));
        WaitUntilNotRunning(launcher);

        CollectionAssert.Contains(OutputOf(launcher), new GameOutputLine("--seed=7", IsError: false));
    }

    [TestMethod]
    public void AGameThatExitsWithAnErrorCode_SaysSoInTheStatus()
    {
        var launcher = LauncherFor("/c exit 3");

        Assert.IsTrue(launcher.TryLaunch(out _));
        WaitUntilNotRunning(launcher);

        StringAssert.Contains(launcher.Status, "exit code 3");
    }

    [TestMethod]
    public void AGameThatExitsCleanly_SaysGameClosed_AndMovesTheStatusVersionOn()
    {
        var launcher = LauncherFor("/c exit");
        Assert.IsTrue(launcher.TryLaunch(out _));
        var startedVersion = launcher.StatusVersion;

        WaitUntilNotRunning(launcher);

        Assert.AreEqual(GameLauncher.GameClosedStatus, launcher.Status);
        Assert.AreNotEqual(startedVersion, launcher.StatusVersion);
    }

    [TestMethod]
    public void BuildFirst_AFailingBuild_StartsNoGame_AndSaysSo()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var launcher = new GameLauncher(new GameLaunchTarget(cmd, "/c echo game-ran", Environment.SystemDirectory, new ProcessCommand(cmd, "/c exit 2", Environment.SystemDirectory)));

        Assert.IsTrue(launcher.TryLaunch(out var status, buildFirst: true));
        Assert.AreEqual("Building...", status);
        WaitUntilNotRunning(launcher);

        StringAssert.Contains(launcher.Status, "Build failed (exit code 2)");
        Assert.IsFalse(OutputOf(launcher).Any(line => line.Text == "game-ran"));
    }

    [TestMethod]
    public void BuildFirst_ASucceedingBuild_ThenStartsTheGame()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var launcher = new GameLauncher(new GameLaunchTarget(cmd, "/c echo game-ran", Environment.SystemDirectory, new ProcessCommand(cmd, "/c echo built", Environment.SystemDirectory)));

        Assert.IsTrue(launcher.TryLaunch(out _, buildFirst: true));
        WaitUntilNotRunning(launcher);
        var output = OutputOf(launcher);

        CollectionAssert.Contains(output, new GameOutputLine("built", IsError: false));
        CollectionAssert.Contains(output, new GameOutputLine("game-ran", IsError: false));
        Assert.AreEqual(GameLauncher.GameClosedStatus, launcher.Status);
    }
}
