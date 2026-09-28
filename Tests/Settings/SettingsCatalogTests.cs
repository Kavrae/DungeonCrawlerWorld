using Engine.Modules;
using Engine.Settings;

namespace Tests.Settings;

[TestClass]
public sealed class SettingsCatalogTests
{
    private static readonly Guid AlphaId = new("5e771265-0000-4000-8000-000000000001");
    private static readonly Guid BetaId = new("5e771265-0000-4000-8000-000000000002");

    private static readonly SettingKey<int> AlphaCapacity = new(AlphaId, "Capacity");
    private static readonly SettingKey<float> AlphaRate = new(AlphaId, "Rate");
    private static readonly SettingKey<bool> AlphaEnabled = new(AlphaId, "Enabled");
    private static readonly SettingKey<string> AlphaLabel = new(AlphaId, "Label");
    private static readonly SettingKey<int> BetaCapacity = new(BetaId, "Capacity");

    private sealed class DeclaringModule(string name, Guid id, Action<SettingsDeclarations> declare) : IModule
    {
        public string Name => name;

        public Guid Id => id;

        public void DeclareSettings(SettingsDeclarations settings) => declare(settings);

        public void RegisterComponents(ComponentRegistration registration)
        {
        }
    }

    private static DeclaringModule Alpha() => new("Alpha", AlphaId, settings =>
    {
        settings.Declare(AlphaCapacity, defaultValue: 16, validate: static value => value > 0 ? null : "must be positive");
        settings.Declare(AlphaRate, defaultValue: 0.5f);
        settings.Declare(AlphaEnabled, defaultValue: true);
        settings.Declare(AlphaLabel, defaultValue: "alpha");
    });

    private static DeclaringModule Beta() => new("Beta", BetaId, settings => settings.Declare(BetaCapacity, defaultValue: 4));

    private sealed class ListSource(string name, params SettingOverride[] overrides) : ISettingsSource
    {
        public string Name => name;

        public IReadOnlyList<SettingOverride> Overrides => overrides;

        public IReadOnlyList<string> MalformedEntries => [];
    }

    [TestMethod]
    public void Resolve_WithNoSources_GivesEveryDefault()
    {
        var resolution = SettingsCatalog.Declare([Alpha(), Beta()]).Resolve([]);

        Assert.IsEmpty(resolution.Failures);
        Assert.AreEqual(16, resolution.Values.Get(AlphaCapacity));
        Assert.AreEqual(0.5f, resolution.Values.Get(AlphaRate));
        Assert.IsTrue(resolution.Values.Get(AlphaEnabled));
        Assert.AreEqual("alpha", resolution.Values.Get(AlphaLabel));
        Assert.AreEqual(4, resolution.Values.Get(BetaCapacity));
    }

    [TestMethod]
    public void Resolve_ParsesEachTypeWithTheInvariantCulture()
    {
        var source = new ListSource("test",
            new SettingOverride("Alpha", "Capacity", "32"),
            new SettingOverride("Alpha", "Rate", "1.25"),
            new SettingOverride("Alpha", "Enabled", "false"),
            new SettingOverride("Alpha", "Label", "renamed"));

        var resolution = SettingsCatalog.Declare([Alpha()]).Resolve([source]);

        Assert.IsEmpty(resolution.Failures);
        Assert.AreEqual(32, resolution.Values.Get(AlphaCapacity));
        Assert.AreEqual(1.25f, resolution.Values.Get(AlphaRate));
        Assert.IsFalse(resolution.Values.Get(AlphaEnabled));
        Assert.AreEqual("renamed", resolution.Values.Get(AlphaLabel));
    }

    [TestMethod]
    public void Resolve_ALaterOverride_ReplacesAnEarlierOne_AcrossSources()
    {
        var first = new ListSource("first", new SettingOverride("Alpha", "Capacity", "32"), new SettingOverride("Alpha", "Capacity", "48"));
        var second = new ListSource("second", new SettingOverride("Alpha", "Capacity", "64"));

        var resolution = SettingsCatalog.Declare([Alpha()]).Resolve([first, second]);

        Assert.AreEqual(64, resolution.Values.Get(AlphaCapacity));
    }

    [TestMethod]
    public void Resolve_MatchesSettingNamesIgnoringCase()
    {
        var resolution = SettingsCatalog.Declare([Alpha()]).Resolve([new ListSource("test", new SettingOverride("alpha", "CAPACITY", "8"))]);

        Assert.IsEmpty(resolution.Failures);
        Assert.AreEqual(8, resolution.Values.Get(AlphaCapacity));
    }

    [TestMethod]
    public void Resolve_AnUndeclaredSetting_IsReportedAndChangesNothing()
    {
        var resolution = SettingsCatalog.Declare([Alpha()]).Resolve([new ListSource("test", new SettingOverride("Alpha", "Missing", "1"))]);

        var failure = Assert.ContainsSingle(resolution.Failures);
        Assert.AreEqual("test", failure.Source);
        Assert.AreEqual("Alpha.Missing", failure.Setting);
        Assert.AreEqual(16, resolution.Values.Get(AlphaCapacity));
    }

    [TestMethod]
    public void Resolve_AValueThatDoesNotParse_IsReportedAndKeepsTheDefault()
    {
        var resolution = SettingsCatalog.Declare([Alpha()]).Resolve([new ListSource("test", new SettingOverride("Alpha", "Capacity", "lots"))]);

        var failure = Assert.ContainsSingle(resolution.Failures);
        Assert.Contains("lots", failure.Message);
        Assert.AreEqual(16, resolution.Values.Get(AlphaCapacity));
    }

    [TestMethod]
    public void Resolve_AValueThatFailsValidation_IsReportedAndKeepsTheDefault()
    {
        var resolution = SettingsCatalog.Declare([Alpha()]).Resolve([new ListSource("test", new SettingOverride("Alpha", "Capacity", "-3"))]);

        var failure = Assert.ContainsSingle(resolution.Failures);
        Assert.AreEqual("must be positive", failure.Message);
        Assert.AreEqual(16, resolution.Values.Get(AlphaCapacity));
    }

    [TestMethod]
    public void Resolve_AMalformedEntry_IsReported()
    {
        var resolution = SettingsCatalog.Declare([Alpha()]).Resolve([new CommandLineSettingsSource(["--setting=NoDotHere"])]);

        var failure = Assert.ContainsSingle(resolution.Failures);
        Assert.AreEqual("command line", failure.Source);
        Assert.AreEqual("--setting=NoDotHere", failure.Setting);
    }

    [TestMethod]
    public void Declare_TheSameKeyTwice_Throws()
    {
        var module = new DeclaringModule("Alpha", AlphaId, settings =>
        {
            settings.Declare(AlphaCapacity, defaultValue: 1);
            settings.Declare(AlphaCapacity, defaultValue: 2);
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => SettingsCatalog.Declare([module]));
    }

    [TestMethod]
    public void Declare_TwoModulesWithTheSameQualifiedName_Throws()
    {
        var sameNamedModule = new DeclaringModule("Alpha", BetaId, settings => settings.Declare(BetaCapacity, defaultValue: 1));

        Assert.ThrowsExactly<InvalidOperationException>(() => SettingsCatalog.Declare([Alpha(), sameNamedModule]));
    }

    [TestMethod]
    public void Declare_AKeyOwnedByAnotherModule_Throws()
    {
        var module = new DeclaringModule("Alpha", AlphaId, settings => settings.Declare(BetaCapacity, defaultValue: 1));

        Assert.ThrowsExactly<InvalidOperationException>(() => SettingsCatalog.Declare([module]));
    }

    [TestMethod]
    public void Declare_FromAModuleWithNoId_Throws()
    {
        var module = new DeclaringModule("Anonymous", Guid.Empty, settings => settings.Declare(new SettingKey<int>(Guid.Empty, "Capacity"), defaultValue: 1));

        Assert.ThrowsExactly<InvalidOperationException>(() => SettingsCatalog.Declare([module]));
    }

    [TestMethod]
    public void Declare_AnInvalidDefault_Throws()
    {
        var module = new DeclaringModule("Alpha", AlphaId, settings => settings.Declare(AlphaCapacity, defaultValue: 0, validate: static value => value > 0 ? null : "must be positive"));

        Assert.ThrowsExactly<InvalidOperationException>(() => SettingsCatalog.Declare([module]));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Has.Dot")]
    [DataRow("Has=Equals")]
    [DataRow("Has Space")]
    public void Declare_AnUnwritableName_Throws(string name)
    {
        var module = new DeclaringModule("Alpha", AlphaId, settings => settings.Declare(new SettingKey<int>(AlphaId, name), defaultValue: 1));

        Assert.ThrowsExactly<InvalidOperationException>(() => SettingsCatalog.Declare([module]));
    }

    [TestMethod]
    public void AReplacementModule_OwnsTheReplacedModulesKeys_UnderItsOwnName()
    {
        var replacement = new DeclaringModule("ReplacementAlpha", AlphaId, settings => settings.Declare(AlphaCapacity, defaultValue: 99));

        var resolution = SettingsCatalog.Declare([replacement]).Resolve([new ListSource("test", new SettingOverride("ReplacementAlpha", "Capacity", "7"))]);

        Assert.IsEmpty(resolution.Failures);
        Assert.AreEqual(7, resolution.Values.Get(AlphaCapacity));
    }

    [TestMethod]
    public void Get_AnUndeclaredKey_Throws()
    {
        var values = SettingsCatalog.Declare([Beta()]).Resolve([]).Values;

        Assert.ThrowsExactly<InvalidOperationException>(() => values.Get(AlphaCapacity));
    }

    [TestMethod]
    public void Get_WithAKeyOfAnotherType_Throws()
    {
        var values = SettingsCatalog.Declare([Alpha()]).Resolve([]).Values;

        Assert.ThrowsExactly<InvalidOperationException>(() => values.Get(new SettingKey<float>(AlphaId, "Capacity")));
    }
}
