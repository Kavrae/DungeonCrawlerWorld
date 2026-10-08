using Engine.Modules;
using Engine.Tags;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Auras;
using Game.Modules.Inventory;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Actions;

/// <summary>A build refuses a toggle that couldn't be switched off again, or an item that is only half a toggle.</summary>
[TestClass]
public sealed class ToggleContentValidationTests
{
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

    private static readonly ToggleItemActivator ToggleActivator = new(new ActionTiming(ActionTimingCategory.FreeCast));

    private static readonly Effect PermanentModifier =
        new([new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, -0.1f, DurationFrames: null)]);

    private static readonly Effect TimedModifier =
        new([new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, -0.1f, DurationFrames: 60)]);

    private static ItemDefinition ToggleItem(string name, IReadOnlyList<Effect> heldEffects) =>
        new(Guid.NewGuid(), name, null, "?", Color.White, GameplayTagSet.Empty, heldEffects, Activator: ToggleActivator, Toggle: ToggleSpec.HoldsEffectsOnly);

    private static InvalidOperationException BuildWith(ItemDefinition item) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInTestModules.BuildModules([new RegisteringModule(context => context.Items.Register(item))]));

    private static void BuildsWith(ItemDefinition item) =>
        BuiltInTestModules.BuildModules([new RegisteringModule(context => context.Items.Register(item))]);

    [TestMethod]
    public void ToggleHoldingAPermanentStatModifier_ThrowsNamingTheItemAndTheEntry()
    {
        var exception = BuildWith(ToggleItem("Stance Charm", [PermanentModifier]));

        Assert.Contains("Item 'Stance Charm'", exception.Message);
        Assert.Contains(nameof(StatModifierGrant), exception.Message);
    }

    /// <summary>An immunity a toggle holds is its own, taken back under its key when it goes off.</summary>
    [TestMethod]
    public void ToggleHoldingAPermanentImmunity_Builds()
    {
        BuildsWith(ToggleItem("Ward Charm", [new Effect([new StatusEffectImmunityGrant(StatusEffectType.Poison)])]));
    }

    [TestMethod]
    public void ToggleHoldingAPermanentGrantInsideAChainedEffect_Throws()
    {
        var exception = BuildWith(ToggleItem("Nested Charm", [new Effect([new ChainedEffect(1f, [PermanentModifier])])]));

        Assert.Contains("Item 'Nested Charm'", exception.Message);
    }

    [TestMethod]
    public void ToggleHoldingATimedGrantOrAPermanentAuraSource_Builds()
    {
        BuildsWith(ToggleItem("Timed Charm", [TimedModifier]));
        BuildsWith(ToggleItem("Glow Charm", [new Effect([new AuraSourceGrant(TestAuras.Light, Power: 4, Size: 2)])]));
    }

    [TestMethod]
    public void NonToggleHoldingAPermanentStatModifier_Builds()
    {
        BuildsWith(new ItemDefinition(Guid.NewGuid(), "Tonic", null, "?", Color.White, GameplayTagSet.Empty, [PermanentModifier]));
    }

    [TestMethod]
    public void ToggleItemActivatorWithoutAToggleSpec_Throws()
    {
        var exception = BuildWith(new ItemDefinition(Guid.NewGuid(), "Half Toggle", null, "?", Color.White, GameplayTagSet.Empty, [], Activator: ToggleActivator));

        Assert.Contains("Item 'Half Toggle'", exception.Message);
        Assert.Contains(nameof(ToggleSpec), exception.Message);
    }

    [TestMethod]
    public void ToggleSpecWithoutAToggleItemActivator_Throws()
    {
        var potionActivator = new PotionActivator(new Engine.Math.TargetingSpec(Engine.Math.TargetShape.Self, Range: 0, AreaSize: 0), new ActionTiming(ActionTimingCategory.Immediate));

        var exception = BuildWith(new ItemDefinition(Guid.NewGuid(), "Other Half", null, "?", Color.White, GameplayTagSet.Empty, [], Activator: potionActivator, Toggle: ToggleSpec.HoldsEffectsOnly));

        Assert.Contains("Item 'Other Half'", exception.Message);
        Assert.Contains(nameof(ToggleItemActivator), exception.Message);
    }
}
