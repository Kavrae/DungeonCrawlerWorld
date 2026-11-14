using Engine.Modules;
using Engine.Tags;

namespace Tests.Tags;

[TestClass]
public sealed class GameplayTagRegistryTests
{
    private const string Described = "A test tag.";

    private static readonly Guid AlphaId = new("6d0a7e10-0000-4000-8000-000000000001");
    private static readonly Guid BetaId = new("6d0a7e10-0000-4000-8000-000000000002");

    private static readonly GameplayTag Root = GameplayTag.Get("GameplayTagRegistryTests");
    private static readonly GameplayTag Damage = GameplayTag.Get("GameplayTagRegistryTests.Damage");
    private static readonly GameplayTag Fire = GameplayTag.Get("GameplayTagRegistryTests.Damage.Fire");
    private static readonly GameplayTag Lava = GameplayTag.Get("GameplayTagRegistryTests.Damage.Fire.Lava");
    private static readonly GameplayTag Poison = GameplayTag.Get("GameplayTagRegistryTests.Damage.Poison");
    private static readonly GameplayTag Magic = GameplayTag.Get("GameplayTagRegistryTests.Magic");
    private static readonly GameplayTag NeverDeclared = GameplayTag.Get("GameplayTagRegistryTests.NeverDeclared");
    private static readonly GameplayTag OtherRoot = GameplayTag.Get("GameplayTagRegistryTestsOther");
    private static readonly GameplayTag OtherChild = GameplayTag.Get("GameplayTagRegistryTestsOther.Child");

    private static readonly GameplayTagDomain DamageDomain = new("Damage");
    private static readonly GameplayTagDomain OtherDomain = new("Other");

    private sealed class DeclaringModule(string name, Guid id, Action<GameplayTagDeclarations> declare) : IModule
    {
        public string Name => name;

        public Guid Id => id;

        public void DeclareTags(GameplayTagDeclarations tags) => declare(tags);

        public void RegisterComponents(ComponentRegistration registration)
        {
        }
    }

    private static DeclaringModule Alpha(Action<GameplayTagDeclarations> declare) => new("Alpha", AlphaId, declare);

    private static DeclaringModule Beta(Action<GameplayTagDeclarations> declare) => new("Beta", BetaId, declare);

    /// <summary>The root, owned by Alpha, and every tag down to Lava, each described.</summary>
    private static void DeclareRootToLava(GameplayTagDeclarations tags, bool rootExtensible = false)
    {
        tags.DeclareRoot(Root, Described, rootExtensible);
        tags.Declare(Damage, Described);
        tags.Declare(Fire, Described);
        tags.Declare(Lava, Described);
    }

    [TestMethod]
    public void Declare_ATagAndItsParents_DeclaresEachOnce()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(tags => DeclareRootToLava(tags))]);

        Assert.IsTrue(registry.IsDeclared(Lava));
        Assert.IsTrue(registry.IsDeclared(Fire));
        Assert.IsTrue(registry.IsDeclared(Damage));
        Assert.IsTrue(registry.IsDeclared(Root));
        Assert.IsFalse(registry.IsDeclared(Poison));
        Assert.HasCount(4, registry.DeclaredTags);
    }

    [TestMethod]
    public void AModuleThatDeclaresNothing_DeclaresNoTags()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(_ => { })]);

        Assert.IsEmpty(registry.DeclaredTags);
    }

    [TestMethod]
    public void GetDisplayName_DefaultsToTheLastSegment_AndGetDescriptionGivesTheDeclaredOne()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(tags =>
        {
            tags.DeclareRoot(Root, "Everything in these tests.");
            tags.Declare(Damage, Described);
            tags.Declare(Fire, "Burns.");
        })]);

        Assert.AreEqual("Fire", registry.GetDisplayName(Fire));
        Assert.AreEqual("Burns.", registry.GetDescription(Fire));
        Assert.AreEqual("Everything in these tests.", registry.GetDescription(Root));
    }

    [TestMethod]
    public void GetDisplayName_UsesTheDeclaredName()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(tags =>
        {
            tags.DeclareRoot(Root, Described);
            tags.Declare(Magic, Described, displayName: "Arcane");
        })]);

        Assert.AreEqual("Arcane", registry.GetDisplayName(Magic));
    }

    [TestMethod]
    public void GetDisplayNameOrDescription_OfAnUndeclaredTag_Throws()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(tags => tags.DeclareRoot(Root, Described))]);

        Assert.ThrowsExactly<InvalidOperationException>(() => registry.GetDisplayName(NeverDeclared));
        Assert.ThrowsExactly<InvalidOperationException>(() => registry.GetDescription(NeverDeclared));
    }

    [TestMethod]
    public void ANamedTag_DeclaredByTheOwnerTwice_KeepsItsName()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(tags =>
        {
            DeclareRootToLava(tags);
            tags.Declare(Fire, Described, displayName: "Flame");
        })]);

        Assert.AreEqual("Flame", registry.GetDisplayName(Fire));
    }

    [TestMethod]
    public void TwoModules_MayDeclareTheSameTag_WhereItsRootIsOpen()
    {
        var registry = GameplayTagRegistry.Declare(
        [
            Alpha(tags => DeclareRootToLava(tags, rootExtensible: true)),
            Beta(tags => tags.Declare(Fire, Described, displayName: "Flame")),
        ]);

        Assert.AreEqual("Flame", registry.GetDisplayName(Fire));
    }

    [TestMethod]
    public void TwoDifferentDisplayNames_ForOneTag_Throw()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags =>
        {
            DeclareRootToLava(tags);
            tags.Declare(Fire, Described, displayName: "Flame");
            tags.Declare(Fire, Described, displayName: "Blaze");
        })]));
    }

    [TestMethod]
    public void TwoDifferentDescriptions_ForOneTag_Throw()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare(
        [
            Alpha(tags => DeclareRootToLava(tags, rootExtensible: true)),
            Beta(tags => tags.Declare(Fire, "Something else.")),
        ]));

        Assert.Contains("Beta", exception.Message);
        Assert.Contains("Alpha", exception.Message);
    }

    [TestMethod]
    public void Declaring_None_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags => tags.Declare(GameplayTag.None, Described))]));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AnEmptyDisplayName_Throws(string displayName)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags =>
        {
            tags.DeclareRoot(Root, Described);
            tags.Declare(Fire, Described, displayName);
        })]));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AnEmptyDescription_Throws(string description)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags => tags.DeclareRoot(Root, description))]));
    }

    [TestMethod]
    public void AParentNoModuleDeclares_FailsAsUndescribed()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags =>
        {
            tags.DeclareRoot(Root, Described);
            tags.Declare(Fire, Described);
        })]));

        Assert.Contains(Damage.Name, exception.Message);
    }

    [TestMethod]
    public void TwoTags_DifferingOnlyByCase_Throw()
    {
        var lowerCaseFire = GameplayTag.Get("GameplayTagRegistryTests.Damage.fire");

        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags =>
        {
            DeclareRootToLava(tags);
            tags.Declare(lowerCaseFire, Described);
        })]));
    }

    [TestMethod]
    public void ATagFiveSegmentsDeep_Throws()
    {
        var tooDeep = GameplayTag.Get("GameplayTagRegistryTests.Damage.Fire.Lava.Crust");

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags =>
        {
            DeclareRootToLava(tags);
            tags.Declare(tooDeep, Described);
        })]));

        Assert.Contains(tooDeep.Name, exception.Message);
    }

    [TestMethod]
    [DataRow("GameplayTagRegistryTests.Damage.fireball")]
    [DataRow("GameplayTagRegistryTests.Damage.Fire_Ball")]
    public void ASegmentThatIsntPascalCase_Throws(string name)
    {
        var tag = GameplayTag.Get(name);

        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags =>
        {
            tags.DeclareRoot(Root, Described);
            tags.Declare(Damage, Described);
            tags.Declare(tag, Described);
        })]));
    }

    [TestMethod]
    public void ARootDeclaredWithDeclare_OrATagDeclaredAsARoot_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags => tags.Declare(Root, Described))]));
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags => tags.DeclareRoot(Damage, Described))]));
    }

    [TestMethod]
    public void ATagUnderARootNoModuleDeclares_Throws()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags => tags.Declare(OtherChild, Described))]));

        Assert.Contains(OtherRoot.Name, exception.Message);
    }

    [TestMethod]
    public void AModuleWithoutAnId_CantOwnARoot()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([new DeclaringModule("Nameless", Guid.Empty, tags => tags.DeclareRoot(Root, Described))]));
    }

    [TestMethod]
    public void TwoModules_OwningOneRoot_Throw_NamingBoth()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare(
        [
            Alpha(tags => tags.DeclareRoot(Root, Described)),
            Beta(tags => tags.DeclareRoot(Root, Described)),
        ]));

        Assert.Contains("Alpha", exception.Message);
        Assert.Contains("Beta", exception.Message);
    }

    [TestMethod]
    public void AModuleDeclaringUnderAnotherModulesClosedRoot_Throws_NamingBoth()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare(
        [
            Alpha(tags => DeclareRootToLava(tags)),
            Beta(tags => tags.Declare(Poison, Described)),
        ]));

        Assert.Contains("Alpha", exception.Message);
        Assert.Contains("Beta", exception.Message);
        Assert.Contains(Poison.Name, exception.Message);
    }

    [TestMethod]
    public void AModuleDeclaringUnderAnotherModulesExtensibleRoot_Builds()
    {
        var registry = GameplayTagRegistry.Declare(
        [
            Alpha(tags => DeclareRootToLava(tags, rootExtensible: true)),
            Beta(tags => tags.Declare(Poison, "A mod's poison.")),
        ]);

        Assert.IsTrue(registry.IsDeclared(Poison));
    }

    [TestMethod]
    public void ATagTheOwnerMadeExtensible_OpensOnlyWhatIsBelowIt()
    {
        var acid = GameplayTag.Get("GameplayTagRegistryTests.Damage.Fire.Acid");

        var registry = GameplayTagRegistry.Declare(
        [
            Alpha(tags =>
            {
                tags.DeclareRoot(Root, Described);
                tags.Declare(Damage, Described);
                tags.Declare(Fire, Described, extensible: true);
            }),
            Beta(tags => tags.Declare(acid, Described)),
        ]);

        Assert.IsTrue(registry.IsDeclared(acid));
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare(
        [
            Alpha(tags =>
            {
                tags.DeclareRoot(Root, Described);
                tags.Declare(Damage, Described);
                tags.Declare(Fire, Described, extensible: true);
            }),
            Beta(tags => tags.Declare(Poison, Described)),
        ]));
    }

    [TestMethod]
    public void OnlyTheOwner_MayMakeATagExtensible()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare(
        [
            Alpha(tags => DeclareRootToLava(tags, rootExtensible: true)),
            Beta(tags => tags.Declare(Fire, Described, extensible: true)),
        ]));
    }

    [TestMethod]
    public void AReplacementModule_WithTheReplacedModulesId_OwnsItsRoots()
    {
        var replacement = new DeclaringModule("Alpha Replacement", AlphaId, tags => tags.Declare(Poison, Described));

        var registry = GameplayTagRegistry.Declare([Alpha(tags => DeclareRootToLava(tags)), replacement]);

        Assert.IsTrue(registry.IsDeclared(Poison));
    }

    [TestMethod]
    public void EachRegistry_HoldsOnlyItsOwnModulesTags()
    {
        var alphaRegistry = GameplayTagRegistry.Declare([Alpha(tags => DeclareRootToLava(tags))]);
        var betaRegistry = GameplayTagRegistry.Declare([Beta(tags => tags.DeclareRoot(OtherRoot, Described))]);

        Assert.IsTrue(alphaRegistry.IsDeclared(Fire));
        Assert.IsFalse(alphaRegistry.IsDeclared(OtherRoot));
        Assert.IsTrue(betaRegistry.IsDeclared(OtherRoot));
        Assert.IsFalse(betaRegistry.IsDeclared(Fire));
    }

    [TestMethod]
    public void EnsureDeclared_PassesForDeclaredTags_AndNamesTheFirstUndeclaredOne()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(tags => DeclareRootToLava(tags))]);

        registry.EnsureDeclared([Lava, Damage], "Lava Flow");
        registry.EnsureDeclared(GameplayTagSet.Empty, "Nothing");
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => registry.EnsureDeclared([Fire, NeverDeclared], "Lava Flow"));
        Assert.Contains("Lava Flow", exception.Message);
        Assert.Contains(NeverDeclared.Name, exception.Message);
    }

    [TestMethod]
    public void ADomain_AcceptsWhatIsUnderItsTags_AndRejectsTheRest()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(tags =>
        {
            DeclareRootToLava(tags);
            tags.Declare(Magic, Described);
            tags.DeclareDomain(DamageDomain, "Kinds of damage.", extensible: false, [Damage]);
        })]);

        Assert.IsTrue(registry.Accepts(DamageDomain, Damage));
        Assert.IsTrue(registry.Accepts(DamageDomain, Lava));
        Assert.IsFalse(registry.Accepts(DamageDomain, Magic));
        Assert.IsFalse(registry.Accepts(DamageDomain, Root));
        Assert.IsNull(registry.ProblemWith(DamageDomain, Fire));
        var problem = registry.ProblemWith(DamageDomain, Magic);
        Assert.IsNotNull(problem);
        Assert.Contains(Magic.Name, problem);
        Assert.Contains(Damage.Name, problem);
        Assert.AreEqual("Kinds of damage.", registry.GetDescription(DamageDomain));
    }

    [TestMethod]
    public void AcceptsBelowItsRoots_LeavesOutTheDomainsOwnTags()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(tags =>
        {
            DeclareRootToLava(tags);
            tags.DeclareDomain(DamageDomain, Described, extensible: false, [Damage]);
        })]);

        Assert.IsFalse(registry.AcceptsBelowItsRoots(DamageDomain, Damage));
        Assert.IsTrue(registry.AcceptsBelowItsRoots(DamageDomain, Fire));
        Assert.IsTrue(registry.AcceptsBelowItsRoots(DamageDomain, Lava));
    }

    [TestMethod]
    public void EnsureAccepts_NamesWhatUsesTheTag()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(tags =>
        {
            DeclareRootToLava(tags);
            tags.Declare(Magic, Described);
            tags.DeclareDomain(DamageDomain, Described, extensible: false, [Damage]);
        })]);

        registry.EnsureAccepts(DamageDomain, [Fire, Lava], "Lava Flow");
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => registry.EnsureAccepts(DamageDomain, [Fire, Magic], "Lava Flow"));
        Assert.Contains("Lava Flow", exception.Message);
        Assert.Contains(Magic.Name, exception.Message);
    }

    [TestMethod]
    public void ADomainNoModuleDeclares_IsAProblem_NotAnAnswer()
    {
        var registry = GameplayTagRegistry.Declare([Alpha(tags => DeclareRootToLava(tags))]);

        Assert.IsFalse(registry.IsDeclared(DamageDomain));
        Assert.IsNotNull(registry.ProblemWith(DamageDomain, Fire));
        Assert.ThrowsExactly<InvalidOperationException>(() => registry.Accepts(DamageDomain, Fire));
    }

    [TestMethod]
    public void AnotherModule_MayAddToAnExtensibleDomain_ButNotToAClosedOne()
    {
        var registry = GameplayTagRegistry.Declare(
        [
            Alpha(tags =>
            {
                DeclareRootToLava(tags);
                tags.DeclareDomain(DamageDomain, Described, extensible: true, [Damage]);
            }),
            Beta(tags =>
            {
                tags.DeclareRoot(OtherRoot, Described);
                tags.AddToDomain(DamageDomain, OtherRoot);
            }),
        ]);

        Assert.IsTrue(registry.Accepts(DamageDomain, OtherRoot));
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare(
        [
            Alpha(tags =>
            {
                DeclareRootToLava(tags);
                tags.DeclareDomain(DamageDomain, Described, extensible: false, [Damage]);
            }),
            Beta(tags =>
            {
                tags.DeclareRoot(OtherRoot, Described);
                tags.AddToDomain(DamageDomain, OtherRoot);
            }),
        ]));
        Assert.Contains("Alpha", exception.Message);
        Assert.Contains("Beta", exception.Message);
    }

    [TestMethod]
    public void AddingToADomainNoModuleDeclares_OrDeclaringOneTwice_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags =>
        {
            DeclareRootToLava(tags);
            tags.AddToDomain(OtherDomain, Damage);
        })]));
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare(
        [
            Alpha(tags =>
            {
                DeclareRootToLava(tags);
                tags.DeclareDomain(DamageDomain, Described, extensible: false, [Damage]);
            }),
            Beta(tags => tags.DeclareDomain(DamageDomain, Described, extensible: false, [])),
        ]));
    }

    [TestMethod]
    public void ADomainAcceptingAnUndeclaredTag_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => GameplayTagRegistry.Declare([Alpha(tags =>
        {
            tags.DeclareRoot(Root, Described);
            tags.DeclareDomain(DamageDomain, Described, extensible: false, [Damage]);
        })]));
    }
}
