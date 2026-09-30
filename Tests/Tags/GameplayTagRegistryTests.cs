using Engine.Modules;
using Engine.Tags;

namespace Tests.Tags;

[TestClass]
public sealed class GameplayTagRegistryTests
{
    private static readonly GameplayTag Damage = GameplayTag.Get("GameplayTagRegistryTests.Damage");
    private static readonly GameplayTag Fire = GameplayTag.Get("GameplayTagRegistryTests.Damage.Fire");
    private static readonly GameplayTag Lava = GameplayTag.Get("GameplayTagRegistryTests.Damage.Fire.Lava");
    private static readonly GameplayTag Poison = GameplayTag.Get("GameplayTagRegistryTests.Damage.Poison");
    private static readonly GameplayTag Magic = GameplayTag.Get("GameplayTagRegistryTests.Magic");
    private static readonly GameplayTag NeverDeclared = GameplayTag.Get("GameplayTagRegistryTests.NeverDeclared");

    private sealed class DeclaringModule(string name, Action<GameplayTagDeclarations> declare) : IModule
    {
        public string Name => name;

        public void DeclareTags(GameplayTagDeclarations tags) => declare(tags);

        public void RegisterComponents(ComponentRegistration registration)
        {
        }
    }

    [TestMethod]
    public void Declare_ATag_DeclaresEveryParent()
    {
        var registry = GameplayTagRegistry.Declare([new DeclaringModule("Alpha", tags => tags.Declare(Lava))]);

        Assert.IsTrue(registry.IsDeclared(Lava));
        Assert.IsTrue(registry.IsDeclared(Fire));
        Assert.IsTrue(registry.IsDeclared(Damage));
        Assert.IsTrue(registry.IsDeclared(Damage.Parent));
        Assert.IsFalse(registry.IsDeclared(Poison));
        Assert.HasCount(4, registry.DeclaredTags);
    }

    [TestMethod]
    public void AModuleThatDeclaresNothing_DeclaresNoTags()
    {
        var registry = GameplayTagRegistry.Declare([new DeclaringModule("Alpha", _ => { })]);

        Assert.IsEmpty(registry.DeclaredTags);
    }

    [TestMethod]
    public void GetDisplayName_DefaultsToTheLastSegment()
    {
        var registry = GameplayTagRegistry.Declare([new DeclaringModule("Alpha", tags => tags.Declare(Lava))]);

        Assert.AreEqual("Lava", registry.GetDisplayName(Lava));
        Assert.AreEqual("Fire", registry.GetDisplayName(Fire));
    }

    [TestMethod]
    public void GetDisplayName_UsesTheDeclaredName()
    {
        var registry = GameplayTagRegistry.Declare([new DeclaringModule("Alpha", tags => tags.Declare(Magic, displayName: "Arcane"))]);

        Assert.AreEqual("Arcane", registry.GetDisplayName(Magic));
    }

    [TestMethod]
    public void GetDisplayName_OfAnUndeclaredTag_Throws()
    {
        var registry = GameplayTagRegistry.Declare([new DeclaringModule("Alpha", tags => tags.Declare(Fire))]);

        Assert.ThrowsExactly<InvalidOperationException>(() => registry.GetDisplayName(NeverDeclared));
    }

    [TestMethod]
    public void ANamedParent_DeclaredAfterItsChild_KeepsItsName()
    {
        var registry = GameplayTagRegistry.Declare(
        [
            new DeclaringModule("Alpha", tags => tags.Declare(Lava)),
            new DeclaringModule("Beta", tags => tags.Declare(Fire, displayName: "Flame")),
        ]);

        Assert.AreEqual("Flame", registry.GetDisplayName(Fire));
    }

    [TestMethod]
    public void TwoModules_MayDeclareTheSameTag()
    {
        var registry = GameplayTagRegistry.Declare(
        [
            new DeclaringModule("Alpha", tags => tags.Declare(Fire, displayName: "Flame")),
            new DeclaringModule("Beta", tags => tags.Declare(Fire)),
            new DeclaringModule("Gamma", tags => tags.Declare(Fire, displayName: "Flame")),
        ]);

        Assert.AreEqual("Flame", registry.GetDisplayName(Fire));
    }

    [TestMethod]
    public void TwoDifferentDisplayNames_ForOneTag_Throw()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare(
        [
            new DeclaringModule("Alpha", tags => tags.Declare(Fire, displayName: "Flame")),
            new DeclaringModule("Beta", tags => tags.Declare(Fire, displayName: "Blaze")),
        ]));
    }

    [TestMethod]
    public void Declaring_None_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([new DeclaringModule("Alpha", tags => tags.Declare(GameplayTag.None))]));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AnEmptyDisplayName_Throws(string displayName)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([new DeclaringModule("Alpha", tags => tags.Declare(Fire, displayName))]));
    }

    [TestMethod]
    public void TwoTags_DifferingOnlyByCase_Throw()
    {
        var lowerCaseFire = GameplayTag.Get("GameplayTagRegistryTests.Damage.fire");

        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare(
        [
            new DeclaringModule("Alpha", tags => tags.Declare(Fire)),
            new DeclaringModule("Beta", tags => tags.Declare(lowerCaseFire)),
        ]));
    }

    [TestMethod]
    public void EachRegistry_HoldsOnlyItsOwnModulesTags()
    {
        var alphaRegistry = GameplayTagRegistry.Declare([new DeclaringModule("Alpha", tags => tags.Declare(Fire))]);
        var betaRegistry = GameplayTagRegistry.Declare([new DeclaringModule("Beta", tags => tags.Declare(Magic))]);

        Assert.IsTrue(alphaRegistry.IsDeclared(Fire));
        Assert.IsFalse(alphaRegistry.IsDeclared(Magic));
        Assert.IsTrue(betaRegistry.IsDeclared(Magic));
        Assert.IsFalse(betaRegistry.IsDeclared(Fire));
    }

    [TestMethod]
    public void EnsureDeclared_PassesForDeclaredTags_AndNamesTheFirstUndeclaredOne()
    {
        var registry = GameplayTagRegistry.Declare([new DeclaringModule("Alpha", tags => tags.Declare(Lava))]);

        registry.EnsureDeclared([Lava, Damage], "Lava Flow");
        registry.EnsureDeclared(GameplayTagSet.Empty, "Nothing");
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => registry.EnsureDeclared([Fire, NeverDeclared], "Lava Flow"));
        Assert.Contains("Lava Flow", exception.Message);
        Assert.Contains(NeverDeclared.Name, exception.Message);
    }
}
