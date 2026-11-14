using System.Reflection;
using Engine.Math;
using Engine.Modules;
using Engine.Tags;
using Game.Bootstrap;
using Game.Modules;
using Game.Tags;
using Game.World;

namespace Tests.Tags;

[TestClass]
public sealed class BuiltInGameplayTagsTests
{
    private static readonly GameplayTag ModTag = GameplayTag.Get("Damage.Lava");

    private static readonly GameplayTag ClosedRootModTag = GameplayTag.Get("Delivery.Thrown");

    private sealed class TagDeclaringMod : IGameModule
    {
        public Guid Id => new("7a95c0de-0000-4000-8000-000000000001");

        public void DeclareTags(GameplayTagDeclarations tags) => tags.Declare(ModTag, "Molten rock.", displayName: "Molten Rock");

        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    private sealed class ClosedRootMod : IGameModule
    {
        public Guid Id => new("7a95c0de-0000-4000-8000-000000000002");

        public void DeclareTags(GameplayTagDeclarations tags) => tags.Declare(ClosedRootModTag, "Thrown by hand.");

        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    private static GameBuildPassResult Run(params ModuleFactory<GameModuleContext>[] mods) =>
        GameBuildPass.Run(GameBootstrapper.BuiltInModules(), mods, new Map(new Vector3Int(5, 5, 1)), TestContent.Layers, sessionSeed: 0, [], initialEntityCapacity: 10, initialComponentCapacity: 10);

    [TestMethod]
    public void All_ListsEveryGameTagsField()
    {
        var fieldTags = typeof(GameTags).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(GameplayTag))
            .Select(field => (GameplayTag)field.GetValue(null)!)
            .ToList();

        CollectionAssert.AreEquivalent(fieldTags, GameTags.All.ToList());
    }

    [TestMethod]
    public void ABuiltInBuild_DeclaresEveryGameTag()
    {
        var registry = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1))).Context.GameplayTags;

        foreach (var tag in GameTags.All)
        {
            Assert.IsTrue(registry.IsDeclared(tag), $"{tag} is not declared.");
        }
    }

    [TestMethod]
    public void AFewModulesBuiltAlone_StillDeclareEveryGameTag()
    {
        var registry = BuiltInTestModules.BuildModules([]).Context.GameplayTags;

        Assert.IsTrue(GameTags.All.All(registry.IsDeclared));
    }

    [TestMethod]
    public void GameTags_ReadByTheirLastSegment()
    {
        var registry = BuiltInTestModules.BuildModules([]).Context.GameplayTags;

        Assert.AreEqual("Unarmed", registry.GetDisplayName(GameTags.DeliveryMeleeUnarmed));
        Assert.AreEqual("Potion", registry.GetDisplayName(GameTags.ItemConsumablePotion));
        Assert.AreEqual("Magic", registry.GetDisplayName(GameTags.Magic));
    }

    [TestMethod]
    public void AMod_DeclaresItsTagsInItsOwnBuildOnly()
    {
        var withMod = Run(ModuleFactory<GameModuleContext>.For<TagDeclaringMod>());
        var withoutMod = Run();

        Assert.IsTrue(withMod.Context.GameplayTags.IsDeclared(ModTag));
        Assert.AreEqual("Molten Rock", withMod.Context.GameplayTags.GetDisplayName(ModTag));
        Assert.IsTrue(withMod.Context.GameplayTags.IsDeclared(GameTags.DamageFire));
        Assert.IsFalse(withoutMod.Context.GameplayTags.IsDeclared(ModTag));
    }

    [TestMethod]
    public void AMod_DeclaringUnderAClosedBuiltInRoot_FailsTheBuild_NamingCoreAndTheMod()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Run(ModuleFactory<GameModuleContext>.For<ClosedRootMod>()));

        Assert.Contains(nameof(ClosedRootMod), exception.Message);
        Assert.Contains(nameof(Game.Modules.Core.CoreModule), exception.Message);
        Assert.Contains(ClosedRootModTag.Name, exception.Message);
    }

    [TestMethod]
    public void EveryBuiltInTag_HasADescription_AndBuiltInDomainsAcceptTheirRoots()
    {
        var registry = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1))).Context.GameplayTags;

        foreach (var tag in registry.DeclaredTags)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(registry.GetDescription(tag)), $"{tag} has no description.");
        }

        foreach (var (root, _, _) in GameTags.Roots)
        {
            Assert.IsTrue(registry.Accepts(GameTagDomains.Activation, root), $"Activation doesn't accept {root}.");
        }

        Assert.IsTrue(registry.Accepts(GameTagDomains.ItemCategory, GameTags.ItemConsumablePotion));
        Assert.IsFalse(registry.Accepts(GameTagDomains.ItemCategory, GameTags.DamageFire));
        Assert.IsTrue(registry.Accepts(GameTagDomains.AbilityScore, GameTags.StatsAbilityScoreStrength));
        Assert.IsFalse(registry.Accepts(GameTagDomains.AbilityScore, GameTags.ItemWand));
        Assert.IsTrue(registry.Accepts(GameTagDomains.Area, Game.Areas.AreaTags.AreaTesting));
        Assert.IsFalse(registry.Accepts(GameTagDomains.Area, GameTags.StatsAbilityScoreStrength));
        Assert.IsFalse(registry.Accepts(GameTagDomains.Activation, Game.Areas.AreaTags.AreaTesting));
    }

    [TestMethod]
    public void TwoBuilds_HaveTheirOwnRegistry()
    {
        Assert.AreNotSame(Run().Context.GameplayTags, Run().Context.GameplayTags);
    }
}
