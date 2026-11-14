using Engine.Math;
using Engine.Tiles;
using Game.Effects;
using Game.Effects.Entries;
using Engine.Modules;
using Engine.Tags;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;
using Game.Modules.StatModifiers;
using Game.Tags;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Tags;

[TestClass]
public sealed class ContentTagsTests
{
    private static readonly GameplayTag UndeclaredTag = GameplayTag.Get("ContentTagsTests.Undeclared");

    private sealed class RegisteringModule(Action<GameModuleContext> configure) : IGameModule
    {
        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void Configure(GameModuleContext context) => configure(context);

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    private static readonly GameplayTagDomain ModRoleDomain = new("ContentTagsTests role");

    /// <summary>A mod declaring tag under an extensible built-in root.</summary>
    private sealed class TagDeclaringModule(GameplayTag tag) : IGameModule
    {
        public void DeclareTags(GameplayTagDeclarations tags) => tags.Declare(tag, "Declared by a test mod.");

        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    /// <summary>A mod owning a root of its own, in a domain of its own, which no activation field accepts.</summary>
    private sealed class OwnRoleModule(GameplayTag root) : IGameModule
    {
        public Guid Id => new("3c0a7e10-0000-4000-8000-000000000001");

        public void DeclareTags(GameplayTagDeclarations tags)
        {
            tags.DeclareRoot(root, "A role of the test mod's own.");
            tags.DeclareDomain(ModRoleDomain, "The test mod's role.", extensible: false, [root]);
        }

        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    private static ItemDefinition ItemTagged(GameplayTagSet tags, IReadOnlyList<Effect>? effects = null) =>
        new(Guid.NewGuid(), "Tagged Item", null, "?", Color.White, tags, effects ?? []);

    private static ActionDefinition ActionTagged(GameplayTagSet tags, IActionActivator activator) =>
        new(Guid.NewGuid(), "Tagged Action", null, "?", Color.White, tags, [], activator);

    private static readonly TargetingSpec SelfTargeting = new(TargetShape.Self, MaximumRange: 0, Anchor: TargetAnchor.Caster);
    private static readonly ActionTiming ImmediateTiming = new(ActionTimingCategory.Immediate, 30, null);

    [TestMethod]
    public void Spells_ScrollsAndWands_AreMagic_WithoutDeclaringIt()
    {
        Assert.IsTrue(TestContent.Action(BuiltInActionIds.Heal).Tags.HasExact(GameTags.Magic));
        Assert.IsTrue(TestContent.Action(BuiltInActionIds.MagicMissile).Tags.HasExact(GameTags.Magic));
        Assert.IsTrue(TestContent.Action(BuiltInActionIds.ToxicStrike).Tags.HasExact(GameTags.Magic));
        Assert.IsTrue(TestContent.Item(BuiltInItemIds.ScrollOfHealing).Tags.HasExact(GameTags.Magic));
        Assert.IsTrue(TestContent.Item(BuiltInItemIds.ScrollOfTorch).Tags.HasExact(GameTags.Magic));
        Assert.IsTrue(TestContent.Item(BuiltInItemIds.WandOfFireball).Tags.HasExact(GameTags.Magic));
    }

    [TestMethod]
    public void PotionsAndPhysicalAttacks_AreNotMagic()
    {
        Assert.IsFalse(TestContent.Item(BuiltInItemIds.HealthPotion).Tags.Has(GameTags.Magic));
        Assert.IsFalse(TestContent.Item(BuiltInItemIds.ToxicIdol).Tags.Has(GameTags.Magic));
        Assert.IsFalse(TestContent.Action(BuiltInActionIds.PowerAttack).Tags.Has(GameTags.Magic));
        Assert.IsFalse(TestContent.Action(BuiltInActionIds.QuickAttack).Tags.Has(GameTags.Magic));
    }

    [TestMethod]
    public void EachActivator_ImpliesItsClassification()
    {
        Assert.IsTrue(ActionTagged([], new SpellActivator(SelfTargeting, ImmediateTiming)).Tags.HasExact(GameTags.Spell));
        Assert.IsTrue(ItemTagged([]).Tags.IsEmpty, "No activator implies nothing.");
        Assert.IsTrue(TestContent.Item(BuiltInItemIds.HealthPotion).Tags.HasExact(GameTags.ItemConsumablePotion));
        Assert.IsTrue(TestContent.Item(BuiltInItemIds.HealthPotion).Tags.Has(GameTags.ItemConsumable));
        Assert.IsTrue(TestContent.Item(BuiltInItemIds.ScrollOfHealing).Tags.HasExact(GameTags.ItemConsumableScroll));
        Assert.IsTrue(TestContent.Item(BuiltInItemIds.WandOfFireball).Tags.HasExact(GameTags.ItemWand));
        Assert.IsFalse(ActionTagged([], new DirectAction(SelfTargeting, ImmediateTiming)).Tags.Has(GameTags.Spell));
    }

    [TestMethod]
    public void DeclaringAnImpliedTagByHand_DoesNotDuplicateIt()
    {
        var action = ActionTagged([GameTags.Spell], new SpellActivator(SelfTargeting, ImmediateTiming));

        Assert.AreEqual(2, action.Tags.Count);
        Assert.AreEqual<GameplayTagSet>([GameTags.Spell, GameTags.Magic], action.Tags);
    }

    [TestMethod]
    public void TheFireballWand_DealsMagicalFire()
    {
        var tags = TestContent.Item(BuiltInItemIds.WandOfFireball).Tags;

        Assert.IsTrue(GameplayTagQuery.All([GameTags.DamageFire, GameTags.Magic]).Matches(tags));
    }

    [TestMethod]
    public void MagicMissile_DealsMagicalEnergy()
    {
        var tags = TestContent.Action(BuiltInActionIds.MagicMissile).Tags;

        Assert.IsTrue(GameplayTagQuery.All([GameTags.DamageEnergy, GameTags.Magic]).Matches(tags));
        Assert.IsFalse(tags.Has(GameTags.DamageFire));
    }

    [TestMethod]
    public void QuickAttack_IsMeleeThroughUnarmed()
    {
        var tags = TestContent.Action(BuiltInActionIds.QuickAttack).Tags;

        Assert.IsTrue(tags.Has(GameTags.DeliveryMelee));
        Assert.IsFalse(tags.HasExact(GameTags.DeliveryMelee));
        Assert.IsTrue(tags.HasExact(GameTags.DeliveryMeleeUnarmed));
    }

    [TestMethod]
    public void ABuild_WhoseItemUsesAnUndeclaredTag_ThrowsNamingTheItemAndTag()
    {
        var module = new RegisteringModule(context => context.Items.Register(ItemTagged([UndeclaredTag])));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInTestModules.BuildModules([module]));
        Assert.Contains("Tagged Item", exception.Message);
        Assert.Contains(UndeclaredTag.Name, exception.Message);
    }

    [TestMethod]
    public void ABuild_WhoseActionUsesAnUndeclaredTag_Throws()
    {
        var module = new RegisteringModule(context => context.Actions.Register(ActionTagged([UndeclaredTag], new DirectAction(SelfTargeting, ImmediateTiming))));

        Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInTestModules.BuildModules([module]));
    }

    [TestMethod]
    public void ABuild_WhoseStatModifierIsConditionedOnAnUndeclaredTag_Throws()
    {
        var grant = new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: false, Magnitude: -0.5f, DurationFrames: null, ConditionTag: UndeclaredTag);
        var module = new RegisteringModule(context => context.Items.Register(ItemTagged([], [new Effect([grant])])));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInTestModules.BuildModules([module]));
        Assert.Contains(UndeclaredTag.Name, exception.Message);
    }

    [TestMethod]
    public void ABuild_WhoseModDeclaresTheTagItUses_Builds()
    {
        var declaredByMod = GameplayTag.Get("Damage.ContentTagsTestsAcid");
        var module = new RegisteringModule(context => context.Items.Register(ItemTagged([declaredByMod])));

        var build = BuiltInTestModules.BuildModules([new TagDeclaringModule(declaredByMod), module]);

        Assert.IsTrue(build.Context.GameplayTags.IsDeclared(declaredByMod));
    }

    [TestMethod]
    public void ABuild_WhoseItemUsesADeclaredTagOutsideTheActivationDomain_ThrowsNamingTheItemAndTag()
    {
        var root = GameplayTag.Get("ContentTagsTestsRole");
        var module = new RegisteringModule(context => context.Items.Register(ItemTagged([root])));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInTestModules.BuildModules([new OwnRoleModule(root), module]));
        Assert.Contains("Tagged Item", exception.Message);
        Assert.Contains(root.Name, exception.Message);
        Assert.Contains(GameTagDomains.Activation.Name, exception.Message);
    }

    [TestMethod]
    public void ABuild_WhoseStatModifierIsConditionedOnATagOutsideTheActivationDomain_Throws()
    {
        var root = GameplayTag.Get("ContentTagsTestsConditionRole");
        var grant = new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: false, Magnitude: -0.5f, DurationFrames: null, ConditionTag: root);
        var module = new RegisteringModule(context => context.Items.Register(ItemTagged([], [new Effect([grant])])));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInTestModules.BuildModules([new OwnRoleModule(root), module]));
        Assert.Contains(root.Name, exception.Message);
    }

    [TestMethod]
    [DataRow("Items.jsonl", "Items", "\"t\":[\"Area.Testing\"]")]
    [DataRow("Items.jsonl", "Items", "\"fx\":[{\"e\":[{\"$\":\"StatModifierGrant\",\"tg\":\"IncomingDamage\",\"op\":\"Multiplicative\",\"pol\":\"Buff\",\"m\":-0.5,\"ct\":\"Area.Testing\"}]}]")]
    public void ARecordWithATagOutsideItsFieldsDomain_FailsAtItsFileLineAndColumn(string fileName, string tableName, string fields)
    {
        var types = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1))).Declarations.ContentTypes;
        var line = $"{{\"id\":\"{Engine.Content.ContentGuid.Format(Guid.NewGuid())}\",\"n\":\"Out Of Domain\",\"g\":\"?\",{fields}}}";
        var text = $"{{\"fmt\":1,\"table\":\"{tableName}\"}}\n{line}\n";
        var failures = new List<Engine.Content.ContentFailure>();

        var file = Engine.Content.ContentTable.Read<ItemDefinition>(text, fileName, tableName, types, Engine.Content.NoContentReferences.Instance, failures);

        Assert.IsEmpty(file.Records);
        var failure = Assert.ContainsSingle(failures);
        Assert.AreEqual(fileName, failure.File);
        Assert.AreEqual(2, failure.Line);
        Assert.AreEqual(line.IndexOf("\"Area.Testing\"", StringComparison.Ordinal) + 1, failure.Column);
        Assert.Contains("Area.Testing", failure.Reason);
        Assert.Contains(GameTagDomains.Activation.Name, failure.Reason);
    }

    [TestMethod]
    public void EveryBuiltInDefinition_UsesOnlyDeclaredTags()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1)));

        Assert.IsNotEmpty(build.Context.Items.Definitions);
        Assert.IsNotEmpty(build.Context.Actions.Definitions);
    }
}
