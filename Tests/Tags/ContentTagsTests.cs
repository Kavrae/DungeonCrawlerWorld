using Engine.Math;
using Engine.Modules;
using Engine.Tags;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.Actions.Effects;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Definitions;
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

    private sealed class TagDeclaringModule(GameplayTag tag) : IGameModule
    {
        public void DeclareTags(GameplayTagDeclarations tags) => tags.Declare(tag);

        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    private static ItemDefinition ItemTagged(GameplayTagSet tags, IReadOnlyList<ActionEffect>? effects = null) =>
        new(Guid.NewGuid(), "Tagged Item", null, "?", Color.White, tags, effects ?? []);

    private static ActionDefinition ActionTagged(GameplayTagSet tags, IActionActivator activator) =>
        new(Guid.NewGuid(), "Tagged Action", null, "?", Color.White, tags, [], activator);

    private static readonly TargetingSpec SelfTargeting = new(TargetShape.Self, Range: 0);
    private static readonly ActionTiming ImmediateTiming = new(ActionTimingCategory.Immediate, 30, null);

    [TestMethod]
    public void Spells_ScrollsAndWands_AreMagic_WithoutDeclaringIt()
    {
        Assert.IsTrue(HealAction.Build().Tags.HasExact(GameTags.Magic));
        Assert.IsTrue(MagicMissileAction.Build().Tags.HasExact(GameTags.Magic));
        Assert.IsTrue(ToxicStrikeAction.Build().Tags.HasExact(GameTags.Magic));
        Assert.IsTrue(ScrollOfHealing.Build().Tags.HasExact(GameTags.Magic));
        Assert.IsTrue(ScrollOfTorch.Build().Tags.HasExact(GameTags.Magic));
        Assert.IsTrue(WandOfFireball.Build().Tags.HasExact(GameTags.Magic));
    }

    [TestMethod]
    public void PotionsAndPhysicalAttacks_AreNotMagic()
    {
        Assert.IsFalse(HealthPotion.Build().Tags.Has(GameTags.Magic));
        Assert.IsFalse(ToxicIdol.Build().Tags.Has(GameTags.Magic));
        Assert.IsFalse(PowerAttackAction.Build().Tags.Has(GameTags.Magic));
        Assert.IsFalse(QuickAttackAction.Build().Tags.Has(GameTags.Magic));
    }

    [TestMethod]
    public void EachActivator_ImpliesItsClassification()
    {
        Assert.IsTrue(ActionTagged([], new SpellActivator(SelfTargeting, ImmediateTiming)).Tags.HasExact(GameTags.ActionSpell));
        Assert.IsTrue(ItemTagged([]).Tags.IsEmpty, "No activator implies nothing.");
        Assert.IsTrue(HealthPotion.Build().Tags.HasExact(GameTags.ItemConsumablePotion));
        Assert.IsTrue(HealthPotion.Build().Tags.Has(GameTags.ItemConsumable));
        Assert.IsTrue(ScrollOfHealing.Build().Tags.HasExact(GameTags.ItemConsumableScroll));
        Assert.IsTrue(WandOfFireball.Build().Tags.HasExact(GameTags.ItemWand));
        Assert.IsFalse(ActionTagged([], new DirectAction(SelfTargeting, ImmediateTiming)).Tags.Has(GameTags.ActionSpell));
    }

    [TestMethod]
    public void DeclaringAnImpliedTagByHand_DoesNotDuplicateIt()
    {
        var action = ActionTagged([GameTags.ActionSpell], new SpellActivator(SelfTargeting, ImmediateTiming));

        Assert.AreEqual(2, action.Tags.Count);
        Assert.AreEqual<GameplayTagSet>([GameTags.ActionSpell, GameTags.Magic], action.Tags);
    }

    [TestMethod]
    public void TheFireballWand_DealsMagicalFire()
    {
        var tags = WandOfFireball.Build().Tags;

        Assert.IsTrue(GameplayTagQuery.All([GameTags.DamageFire, GameTags.Magic]).Matches(tags));
    }

    [TestMethod]
    public void MagicMissile_DealsMagicalEnergy()
    {
        var tags = MagicMissileAction.Build().Tags;

        Assert.IsTrue(GameplayTagQuery.All([GameTags.DamageEnergy, GameTags.Magic]).Matches(tags));
        Assert.IsFalse(tags.Has(GameTags.DamageFire));
    }

    [TestMethod]
    public void QuickAttack_IsMeleeThroughUnarmed()
    {
        var tags = QuickAttackAction.Build().Tags;

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
        var module = new RegisteringModule(context => context.Items.Register(ItemTagged([], [new ActionEffect([grant])])));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInTestModules.BuildModules([module]));
        Assert.Contains(UndeclaredTag.Name, exception.Message);
    }

    [TestMethod]
    public void ABuild_WhoseModDeclaresTheTagItUses_Builds()
    {
        var declaredByMod = GameplayTag.Get("ContentTagsTests.DeclaredByMod");
        var module = new RegisteringModule(context => context.Items.Register(ItemTagged([declaredByMod])));

        var build = BuiltInTestModules.BuildModules([new TagDeclaringModule(declaredByMod), module]);

        Assert.IsTrue(build.Context.GameplayTags.IsDeclared(declaredByMod));
    }

    [TestMethod]
    public void EveryBuiltInDefinition_UsesOnlyDeclaredTags()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1)));

        Assert.IsNotEmpty(build.Context.Items.Definitions);
        Assert.IsNotEmpty(build.Context.Actions.Definitions);
    }
}
