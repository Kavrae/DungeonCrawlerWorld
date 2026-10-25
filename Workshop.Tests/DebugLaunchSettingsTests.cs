using Engine.Diagnostics;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;
using Workshop.Debug;

namespace Workshop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DebugLaunchSettingsTests
{
    private string _optionsPath = null!;

    [TestInitialize]
    public void CreateOptionsPath() => _optionsPath = Path.Combine(Path.GetTempPath(), $"debug-launch-options-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void DeleteOptionsFile() => File.Delete(_optionsPath);

    private static void Enter(DebugTabHarness harness, TextBox box, string text)
    {
        harness.Click(box);
        box.SetText(string.Empty);
        harness.Type(text);
        harness.Press(Keys.Enter);
    }

    [TestMethod]
    public void TheSeed_IsRandomAndDisabled_UntilRandomIsUnticked_ThenTypedSeedIsPassed_AndKeptAfterARestart()
    {
        using (var harness = new DebugTabHarness(_optionsPath))
        {
            var launch = harness.View.Launch;
            Assert.IsTrue(launch.RandomSeedToggle.IsOn);
            Assert.IsFalse(launch.SeedField.Enabled);
            Assert.DoesNotContain("--seed=", launch.CommandLineText);

            harness.Click(launch.RandomSeedToggle);
            Assert.IsTrue(launch.SeedField.Enabled);
            Enter(harness, launch.SeedField, "12345");

            StringAssert.EndsWith(launch.CommandLineText, "--diagnostics=frame,gauges --seed=12345");
        }

        using var restarted = new DebugTabHarness(_optionsPath);
        Assert.IsFalse(restarted.View.Launch.RandomSeedToggle.IsOn);
        Assert.AreEqual(12345, restarted.View.Launch.SeedField.Value);
        StringAssert.EndsWith(restarted.View.Launch.CommandLineText, "--seed=12345");
    }

    [TestMethod]
    public void Headless_IsDisabledUntilBenchmarkFramesIsTicked()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        var launch = harness.View.Launch;
        Assert.IsFalse(launch.HeadlessToggle.Enabled);

        harness.Click(launch.HeadlessToggle);
        Assert.IsFalse(launch.HeadlessToggle.IsOn);
        Assert.IsFalse(harness.Debugging.Options.Headless);

        harness.Click(launch.BenchmarkToggle);
        Assert.IsTrue(launch.HeadlessToggle.Enabled);
        Assert.AreEqual(600, launch.StartFrameField.Value);
        Assert.AreEqual(3600, launch.EndFrameField.Value);
        StringAssert.EndsWith(launch.CommandLineText, "--benchmark-frames=600-3600");
        Assert.AreEqual(string.Empty, launch.HeadlessNoteText);

        harness.Click(launch.HeadlessToggle);
        StringAssert.EndsWith(launch.CommandLineText, "--benchmark-frames=600-3600 --headless");
        Assert.AreEqual(DebugLaunchSection.HeadlessNote, launch.HeadlessNoteText);

        harness.Click(launch.BenchmarkToggle);
        Assert.IsFalse(launch.HeadlessToggle.Enabled);
        Assert.IsTrue(launch.HeadlessToggle.IsOn);
        StringAssert.EndsWith(launch.CommandLineText, "--diagnostics=frame,gauges");
    }

    [TestMethod]
    public void AStartAfterTheEnd_MovesTheEndToJustAfterIt()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        var launch = harness.View.Launch;
        harness.Click(launch.BenchmarkToggle);

        Enter(harness, launch.StartFrameField, "5000");

        Assert.AreEqual(new BenchmarkFrameRange(5000, 5001), harness.Debugging.Options.BenchmarkFrames);
        Assert.AreEqual(5001, launch.EndFrameField.Value);
        StringAssert.EndsWith(launch.CommandLineText, "--benchmark-frames=5000-5001");
    }

    [TestMethod]
    public void ExtraArguments_AreAddedAsTyped_AndARepeatedFlagIsShownAsAProblem()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        var launch = harness.View.Launch;
        harness.Click(launch.RandomSeedToggle);

        Enter(harness, launch.ExtraArgumentsBox, "--seed=9 \"--setting=A.B=x y\"");

        StringAssert.EndsWith(launch.CommandLineText, "--seed=0 --seed=9 \"--setting=A.B=x y\"");
        StringAssert.Contains(launch.ProblemsText, "Extra arguments repeat --seed=");
        Assert.IsNull(harness.Debugging.LaunchRefusal);
    }

    [TestMethod]
    public void ExtraArguments_ArePassed_AsTheyAreTyped_BeforeEnterOrLeavingTheBox()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        var launch = harness.View.Launch;

        harness.Click(launch.ExtraArgumentsBox);
        harness.Type("--map-size=512");

        Assert.IsTrue(launch.ExtraArgumentsBox.IsFocused);
        Assert.AreEqual("--map-size=512", harness.Debugging.Options.ExtraArguments);
        StringAssert.EndsWith(harness.Debugging.LaunchArguments, "--map-size=512");
    }

    [TestMethod]
    public void HeadlessTypedAsAnExtraArgumentWithoutARange_IsShownAsWhyPlayIsRefused()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        var launch = harness.View.Launch;

        Enter(harness, launch.ExtraArgumentsBox, "--headless");

        StringAssert.Contains(launch.ProblemsText, HeadlessArgument.RequiresRangeMessage);
        StringAssert.Contains(harness.Debugging.LaunchRefusal, HeadlessArgument.RequiresRangeMessage);
    }

    [TestMethod]
    public void UseLastSeed_IsEnabledOnceAGameReportsItsSeed_AndPassesThatSeed()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        var launch = harness.View.Launch;
        Assert.IsFalse(launch.UseLastSeedButton.Enabled);

        using var link = new DiagnosticsLink(harness.Debugging.Link.EndPoint, DiagnosticsFeatures.Gauges, randomSeed: 424242, TextWriter.Null);
        link.Start();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (harness.Debugging.Link.Session?.Hello is null)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "Timed out waiting for the hello.");
            harness.Debugging.Link.Update();
            Thread.Sleep(5);
        }

        harness.Frame();
        Assert.IsTrue(launch.UseLastSeedButton.Enabled);
        harness.Click(launch.UseLastSeedButton);

        Assert.IsFalse(launch.RandomSeedToggle.IsOn);
        Assert.AreEqual(424242, launch.SeedField.Value);
        StringAssert.EndsWith(launch.CommandLineText, "--seed=424242");
        StringAssert.Contains(harness.Status.LastMessage, "424242");
    }
}
