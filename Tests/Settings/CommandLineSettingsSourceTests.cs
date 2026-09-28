using Engine.Settings;

namespace Tests.Settings;

[TestClass]
public sealed class CommandLineSettingsSourceTests
{
    [TestMethod]
    public void ReadsEverySettingArgument_InOrder_AndIgnoresTheRest()
    {
        var source = new CommandLineSettingsSource(["--seed=5", "--setting=Health.RegenRate=2", "--headless", "--setting=Mana.Maximum=40"]);

        CollectionAssert.AreEqual(
            new[] { new SettingOverride("Health", "RegenRate", "2"), new SettingOverride("Mana", "Maximum", "40") },
            source.Overrides.ToArray());
        Assert.IsEmpty(source.MalformedEntries);
    }

    [TestMethod]
    public void TheValue_IsEverythingAfterTheSecondEquals()
    {
        var source = new CommandLineSettingsSource(["--setting=Shop.Greeting=a=b"]);

        Assert.AreEqual(new SettingOverride("Shop", "Greeting", "a=b"), Assert.ContainsSingle(source.Overrides));
    }

    [TestMethod]
    public void AnEmptyValue_IsKept()
    {
        var source = new CommandLineSettingsSource(["--setting=Shop.Greeting="]);

        Assert.AreEqual(new SettingOverride("Shop", "Greeting", ""), Assert.ContainsSingle(source.Overrides));
    }

    [TestMethod]
    [DataRow("--setting=")]
    [DataRow("--setting=NoValue")]
    [DataRow("--setting=NoDot=1")]
    [DataRow("--setting=.Name=1")]
    [DataRow("--setting=Module.=1")]
    [DataRow("--setting==1")]
    public void AnArgumentThatNamesNoSetting_IsMalformed(string argument)
    {
        var source = new CommandLineSettingsSource([argument]);

        Assert.IsEmpty(source.Overrides);
        Assert.AreEqual(argument, Assert.ContainsSingle(source.MalformedEntries));
    }
}
